using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 플레이어블 Flat Kit 머티리얼 = <b>가붕이 몸(FK_Paladin_Toon) 하나를 부모</b>로 두고, 나머지는 그 Material Variant 로
/// <b>텍스처(·틴트)만</b> 바꿔 쓴다(팀장 10-02). 셰이더·외곽선·셀 셰이딩 값은 부모에서 한 번 고치면 전원에 반영된다.
///
/// | Variant | 덮는 값 |
/// |---|---|
/// | FK_Paladin_Sword_Toon · FK_Paladin_Shield_Toon (기존 guid 유지 — 프리팹 참조 그대로) | Base Map 없음 + 금속 틴트(텍스처가 없다) |
/// | Gunner/FK_Gunner_Body (신규) | Base Map = Gunner_BaseColor_4K |
/// | Gunner/FK_LaserGun (신규) | Base Map = LaserGun_BaseColor_Toon |
///
/// 그리고 Gunner_Armature 의 ToonLit 슬롯(Gunner_Toon · LaserGun_Toon)을 위 Variant 로 바꾼다.
/// 은희 ToonLit Variant(3.Materials/Toon/)는 지우지 않는다. 안 쓰는 예전 FK 5개는 Player/Legacy/ 로 옮긴다.
/// 재실행 안전 — 이미 Variant 면 덮는 값만 다시 맞춘다.
/// </summary>
static class FlatKitPlayerVariants
{
    const string Root = "Assets/3.Materials/FlatKit/Player";
    const string ParentPath = Root + "/FK_Paladin_Toon.mat";
    const string GunnerDir = Root + "/Gunner";
    const string LegacyDir = Root + "/Legacy";
    const string GunnerArmature = "Assets/2.Prefabs/Player/Gunner/Gunner_Armature.prefab";
    const string GunnerBodyTex = "Assets/50.Art/Char/gunner/Gunner_BaseColor_4K.png";
    const string LaserGunTex = "Assets/50.Art/Char/gunner/LaserGun_BaseColor_Toon.png";

    static readonly string[] LegacyMats =
    {
        "FK_PlayerBaseModel_M_Arms.mat", "FK_PlayerBaseModel_M_Body.mat", "FK_PlayerBaseModel_M_Legs.mat",
        "FK_SM_Wep_Shield_01_Castle_SHD.mat", "FK_SM_Wep_Sword_03_Castle_SHD.mat",
    };

    [MenuItem("Tools/Rendering/Flat Kit/플레이어 Variant 구성 (가붕이 부모)")]
    static void Run()
    {
        var parent = AssetDatabase.LoadAssetAtPath<Material>(ParentPath);
        if (parent == null) { Debug.LogError($"[PlayerVariants] 부모 없음: {ParentPath}"); return; }
        if (parent.parent != null) { Debug.LogError("[PlayerVariants] 부모가 Variant 다 — 부모는 최상위여야 한다."); return; }

        // 무기 — 기존 머티리얼을 그 자리에서 Variant 로(guid 유지). 텍스처가 없어 틴트만 덮는다.
        foreach (string w in new[] { "FK_Paladin_Sword_Toon.mat", "FK_Paladin_Shield_Toon.mat" })
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/{w}");
            if (m == null) { Debug.LogWarning($"[PlayerVariants] 없음: {w}"); continue; }
            Color tint = m.GetColor("_BaseColor");
            MakeVariant(m, parent, null, tint);
        }

        // 거너 — 신규 Variant.
        if (!AssetDatabase.IsValidFolder(GunnerDir)) AssetDatabase.CreateFolder(Root, "Gunner");
        var body = LoadOrCreate($"{GunnerDir}/FK_Gunner_Body.mat", parent);
        MakeVariant(body, parent, AssetDatabase.LoadAssetAtPath<Texture2D>(GunnerBodyTex), Color.white);
        var gun = LoadOrCreate($"{GunnerDir}/FK_LaserGun.mat", parent);
        MakeVariant(gun, parent, AssetDatabase.LoadAssetAtPath<Texture2D>(LaserGunTex), Color.white);

        AssetDatabase.SaveAssets();

        AssignGunner(body, gun);
        MoveLegacy();

        AssetDatabase.SaveAssets();
        Debug.Log($"[PlayerVariants] 완료 — 부모 {parent.name} · Variant: 검·방패·{body.name}·{gun.name}");
    }

    static Material LoadOrCreate(string path, Material parent)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(parent) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
        AssetDatabase.CreateAsset(m, path);
        Debug.Log($"[PlayerVariants] 생성: {path}");
        return m;
    }

    // 부모를 걸고 모든 재정의를 지운 뒤, 덮을 값만 다시 쓴다 — 그래야 나머지 값이 전부 부모를 따른다.
    static void MakeVariant(Material v, Material parent, Texture tex, Color tint)
    {
        v.parent = parent;
        v.RevertAllPropertyOverrides();
        v.SetTexture("_BaseMap", tex);
        v.SetColor("_BaseColor", tint);
        EditorUtility.SetDirty(v);
        Debug.Log($"[PlayerVariants] {v.name} ← 부모 {parent.name} · BaseMap {(tex ? tex.name : "없음")} · 틴트 {tint}");
    }

    // Gunner_Armature: ToonLit(Gunner_Toon → 몸, LaserGun_Toon → 총) 슬롯을 Flat Kit Variant 로.
    static void AssignGunner(Material body, Material gun)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(GunnerArmature);
        int swapped = 0;
        try
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    Material to = mats[i].name == "Gunner_Toon" ? body : mats[i].name == "LaserGun_Toon" ? gun : null;
                    if (to == null || mats[i] == to) continue;
                    mats[i] = to;
                    changed = true;
                    swapped++;
                }
                if (changed) r.sharedMaterials = mats;
            }
            if (swapped > 0) PrefabUtility.SaveAsPrefabAsset(root, GunnerArmature);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Debug.Log($"[PlayerVariants] {GunnerArmature} — 슬롯 {swapped}개 교체");
    }

    static void MoveLegacy()
    {
        if (!AssetDatabase.IsValidFolder(LegacyDir)) AssetDatabase.CreateFolder(Root, "Legacy");
        foreach (string f in LegacyMats)
        {
            string from = $"{Root}/{f}";
            if (AssetDatabase.LoadAssetAtPath<Material>(from) == null) continue;   // 이미 옮겼다
            string err = AssetDatabase.MoveAsset(from, $"{LegacyDir}/{f}");
            if (!string.IsNullOrEmpty(err)) Debug.LogWarning($"[PlayerVariants] 이동 실패 {f}: {err}");
        }
    }
}
