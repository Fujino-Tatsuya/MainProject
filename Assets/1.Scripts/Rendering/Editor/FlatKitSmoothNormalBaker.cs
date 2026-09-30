using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 외곽선용 부드러운 법선을 **임포트 때** 메시에 굽는다 (PLAN-flatkit.md · 2026-09-30).
//
// 왜: Flat Kit 외곽선은 법선 방향으로 뒷면을 밀어낸다. 각진·고르지 않은 메시(특히 AI 생성 Paladin)는 모서리에서
//     법선이 갈라져 **검은 가시**가 삐져나온다. 같은 위치의 법선을 평균낸 부드러운 법선으로 밀면 사라진다.
// 어떻게:
//   - 허용 목록 FBX 만 처리한다. SVN FBX·.meta 는 고치지 않는다 — 결과는 임포트 산출물(Library)에만 들어가고,
//     이 스크립트가 git 에 있으니 모든 팀원 PC 에서 똑같이 구워진다.
//   - **탄젠트 공간**으로 UV3(TEXCOORD3)에 넣는다. Flat Kit 원본처럼 오브젝트 공간으로 넣으면 스킨드 메시에서
//     뼈가 돌 때 같이 안 돈다. 복원은 Project/FlatKit Stylized Surface (Skinned Outline) 셰이더 외곽선 패스가 한다.
//   - UV2 가 아니라 UV3 인 이유: Paladin_VFX 의 *_MaskUV 메시처럼 이펙트가 UV 채널을 쓰는 메시와 겹치지 않게.
//   - 런타임 비용: 계산 0(임포트 때 1회). 정점당 12바이트 + 외곽선 정점 셰이더 몇 연산.
// 새 캐릭터(거너·어쌔신)는 아래 목록에 FBX 경로만 추가하고 그 FBX 를 Reimport 한다.
public class FlatKitSmoothNormalBaker : AssetPostprocessor
{
    const int Channel = 3;   // TEXCOORD3

    static readonly HashSet<string> Targets = new HashSet<string>
    {
        "Assets/2.Prefabs/Player/Paladin/paladin 1.fbx",
        "Assets/2.Prefabs/Player/PlayerBaseModel.fbx",
        "Assets/50.Art/TestAssets/TestPlayerAsset/Model/SM_Wep_Sword_03.fbx",
        "Assets/50.Art/TestAssets/TestPlayerAsset/Model/SM_Wep_Shield_01.fbx",
        "Assets/50.Art/TestAssets/TestPlayerAsset/Model/SM_Wep_Sword_03_MaskUV.fbx",
        "Assets/50.Art/TestAssets/TestPlayerAsset/Model/SM_Wep_Shield_01_MaskUV.fbx",
    };

    // 목록이나 굽는 방식을 바꾸면 올린다 — 대상 FBX 가 다시 임포트된다.
    public override uint GetVersion() => 1;

    [MenuItem("Tools/Rendering/Flat Kit/Reimport Smooth Normal Targets")]
    static void ReimportTargets()
    {
        foreach (string p in Targets)
        {
            if (AssetImporter.GetAtPath(p) == null) { Debug.LogWarning($"[SmoothNormalBaker] 없음: {p}"); continue; }
            AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
        }
    }

    void OnPostprocessModel(GameObject root)
    {
        if (!Targets.Contains(assetPath)) return;

        var meshes = new HashSet<Mesh>();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh) meshes.Add(mf.sharedMesh);
        foreach (var sr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (sr.sharedMesh) meshes.Add(sr.sharedMesh);

        foreach (Mesh mesh in meshes)
        {
            string result = Bake(mesh);
            Debug.Log($"[SmoothNormalBaker] {assetPath} / {mesh.name} — {result}");
        }
    }

    static string Bake(Mesh mesh)
    {
        var uvCheck = new List<Vector4>();
        mesh.GetUVs(Channel, uvCheck);
        if (uvCheck.Count > 0) return $"⚠ UV{Channel} 이미 사용 중 — 건너뜀(외곽선은 원래 법선으로)";

        Vector3[] v = mesh.vertices;
        Vector3[] n = mesh.normals;
        Vector4[] t = mesh.tangents;
        if (n.Length != v.Length || t.Length != v.Length) return "⚠ 법선/탄젠트 없음 — 건너뜀(임포트 설정에서 Tangents 계산 필요)";

        // 같은 위치(1/10000 격자)의 법선을 모두 더해 평균 — 모서리에서 갈라진 법선을 하나로.
        var sum = new Dictionary<Vector3Int, Vector3>(v.Length);
        var keys = new Vector3Int[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            var k = new Vector3Int(Mathf.RoundToInt(v[i].x * 10000f), Mathf.RoundToInt(v[i].y * 10000f), Mathf.RoundToInt(v[i].z * 10000f));
            keys[i] = k;
            sum[k] = sum.TryGetValue(k, out Vector3 s) ? s + n[i] : n[i];
        }

        var enc = new List<Vector3>(v.Length);
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 smooth = sum[keys[i]];
            smooth = smooth.sqrMagnitude > 1e-8f ? smooth.normalized : n[i];
            Vector3 nn = n[i].normalized;
            Vector3 tt = (Vector3)t[i] - nn * Vector3.Dot(nn, t[i]);
            // 퇴화 탄젠트(UV 가 한 점으로 모인 면)면 임의의 직교 축 — 셰이더도 같은 규칙으로는 못 맞추니 원래 법선을 쓴다.
            if (tt.sqrMagnitude < 1e-10f) { enc.Add(new Vector3(0f, 0f, 1f)); continue; }
            tt.Normalize();
            Vector3 bb = Vector3.Cross(nn, tt) * (t[i].w >= 0f ? 1f : -1f);
            enc.Add(new Vector3(Vector3.Dot(smooth, tt), Vector3.Dot(smooth, bb), Vector3.Dot(smooth, nn)));
        }
        mesh.SetUVs(Channel, enc);
        return $"굽기 완료 — 정점 {v.Length} · 위치 그룹 {sum.Count}";
    }
}
