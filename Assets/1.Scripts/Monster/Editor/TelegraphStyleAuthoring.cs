using UnityEditor;
using UnityEngine;

/// <summary>
/// 공격 예고 스타일 준비(PLAN-telegraph-style). <b>멱등</b> — 있는 에셋은 두고, 빈 칸만 채운다.
/// 1) 예고 데칼 재질 <c>Resources/Telegraph/MA_TelegraphDecal</c>(SG_TelegraphDecal) — 기존 MA_BossMarkerDecal 값(Normal Blend 등)을 복사.
/// 2) 바닥 판 재질 <c>Resources/Telegraph/MA_TelegraphQuad</c>(Monster/TelegraphQuad).
///    🔴 둘 다 Resources 에 두는 이유 — 코드가 런타임에 불러 쓰므로 프리팹 연결 없이도 빌드에 셰이더가 들어간다.
/// 3) 기본 스타일 <c>TelegraphStyle_Default.asset</c>(값 = 코드 기본값 = 2026-10-13 화면).
/// 4) 모든 몬스터 데이터(MonsterDataSO 계열)의 <c>telegraphStyle</c> 가 비어 있으면 기본 스타일을 연결한다.
///    🔴 연결해 두는 이유 — 칸이 비면 코드 기본값으로 그리긴 하지만, 아트가 "어느 에셋을 고치면 되는지"를 인스펙터에서 바로 보게.
/// </summary>
public static class TelegraphStyleAuthoring
{
    const string DecalShaderPath = "Assets/3.Materials/SG_TelegraphDecal.shadergraph";
    const string QuadShaderName = "Monster/TelegraphQuad";
    const string ResourcesDir = "Assets/Resources/Telegraph";
    const string DecalMaterialPath = ResourcesDir + "/MA_TelegraphDecal.mat";
    const string QuadMaterialPath = ResourcesDir + "/MA_TelegraphQuad.mat";
    const string OldDecalMaterialPath = "Assets/3.Materials/MA_BossMarkerDecal.mat";
    internal const string DefaultStylePath = "Assets/2.Prefabs/Monster/Data/TelegraphStyle_Default.asset";

    [MenuItem("Tools/Monster/공격 예고 스타일 준비 (멱등)")]
    static void Setup()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(ResourcesDir)) AssetDatabase.CreateFolder("Assets/Resources", "Telegraph");

        // 1) 데칼 재질
        if (AssetDatabase.LoadAssetAtPath<Material>(DecalMaterialPath) == null)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(DecalShaderPath);
            if (shader == null) { Debug.LogError($"[TelegraphStyle] {DecalShaderPath} 셰이더를 못 불렀다."); return; }
            var mat = new Material(shader) { name = "MA_TelegraphDecal" };
            var old = AssetDatabase.LoadAssetAtPath<Material>(OldDecalMaterialPath);
            if (old != null) mat.CopyPropertiesFromMaterial(old);   // Normal Blend·Draw Order 등 같은 이름 속성
            mat.SetColor("_BaseColor", Color.white);
            mat.SetVector("_MaskParams", new Vector4(0f, 0f, 1f, 0f));
            AssetDatabase.CreateAsset(mat, DecalMaterialPath);
            Debug.Log($"[TelegraphStyle] 생성 {DecalMaterialPath} (셰이더 {shader.name})");
        }

        // 2) 판 재질
        if (AssetDatabase.LoadAssetAtPath<Material>(QuadMaterialPath) == null)
        {
            var shader = Shader.Find(QuadShaderName);
            if (shader == null) { Debug.LogError($"[TelegraphStyle] 셰이더 '{QuadShaderName}' 를 못 찾았다."); return; }
            AssetDatabase.CreateAsset(new Material(shader) { name = "MA_TelegraphQuad" }, QuadMaterialPath);
            Debug.Log($"[TelegraphStyle] 생성 {QuadMaterialPath}");
        }

        // 3) 기본 스타일
        var style = AssetDatabase.LoadAssetAtPath<TelegraphStyleSO>(DefaultStylePath);
        if (style == null)
        {
            style = ScriptableObject.CreateInstance<TelegraphStyleSO>();
            AssetDatabase.CreateAsset(style, DefaultStylePath);
            Debug.Log($"[TelegraphStyle] 생성 {DefaultStylePath}");
        }

        // 4) 몬스터 데이터 연결(빈 칸만)
        int wired = 0, already = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:MonsterDataSO"))
        {
            var data = AssetDatabase.LoadAssetAtPath<MonsterDataSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (data == null) continue;
            if (data.telegraphStyle != null) { already++; continue; }
            data.telegraphStyle = style;
            EditorUtility.SetDirty(data);
            wired++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[TelegraphStyle] 준비 완료 · 몬스터 데이터 연결 {wired}개 · 이미 연결 {already}개");
        Check();
    }

    // 🔴 셰이더 그래프를 파일로 직접 만들었다 — 임포트는 조용히 성공해도 컴파일 오류가 있으면 데칼이 안 그려진다(분홍·투명).
    [MenuItem("Tools/Monster/공격 예고 셰이더 검사")]
    static void Check()
    {
        Shader decal = AssetDatabase.LoadAssetAtPath<Shader>(DecalShaderPath);
        Shader quad = Shader.Find(QuadShaderName);
        foreach (Shader s in new[] { decal, quad })
        {
            if (s == null) { Debug.LogError("[TelegraphStyle] 셰이더 없음"); continue; }
            ShaderMessage[] msgs = ShaderUtil.GetShaderMessages(s);
            int errors = 0;
            foreach (ShaderMessage m in msgs)
            {
                if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) errors++;
                Debug.Log($"[TelegraphStyle] {s.name} {m.severity}: {m.message} ({m.file}:{m.line})");
            }
            Debug.Log($"[TelegraphStyle] 검사 {s.name} · 오류 {errors} · 지원 {s.isSupported} · 패스 {s.passCount}");
        }
        foreach (string p in new[] { DecalMaterialPath, QuadMaterialPath })
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) continue;
            Debug.Log($"[TelegraphStyle] 재질 {p} · _BaseColor {m.HasProperty("_BaseColor") || m.HasProperty("_Color")} · " +
                      $"_MaskTex {m.HasProperty("_MaskTex")} · _MaskParams {m.HasProperty("_MaskParams")} · Base_Map {m.HasProperty("Base_Map")}");
        }
    }
}
