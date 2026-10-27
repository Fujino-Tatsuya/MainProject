using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 플레이어블 Flat Kit 머티리얼 = <b>가붕이 몸(FK_Paladin_Toon) 하나를 부모</b>로 두고, 나머지는 그 Material Variant 로
/// <b>텍스처(·틴트)만</b> 바꿔 쓴다(팀장 10-02). 셰이더·외곽선·셀 셰이딩 값은 부모에서 한 번 고치면 전원에 반영된다.
///
/// | Variant | 덮는 값 |
/// |---|---|
/// | FK_Paladin_Sword_Toon · FK_Paladin_Shield_Toon (기존 guid 유지) | 무기별 Base Map/틴트를 보존 |
/// | Gunner/FK_Gunner_Body (신규) | Base Map = Gunner_BaseColor_4K |
/// | Gunner/FK_LaserGun (신규) | Base Map = LaserGun_BaseColor_Toon |
/// | Assassin/FK_Assassin_Body | Base Map = Assassin_BaseColor — 머리·몸 전용. 단검은 별도 좌우 재질(10-26) |
///
/// 그리고 Gunner_Armature 의 ToonLit 슬롯(Gunner_Toon · LaserGun_Toon)과 Assassin_Armature 의 ToonLit 슬롯(Assassin_Toon —
/// HeadNeck 2 · SourceBody 1 · 단검 L/R 1씩 = 5개)을 위 Variant 로 바꾼다. 단검 루트 배율(1.25)은 여기서 다루지 않는다.
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
    const string AssassinDir = Root + "/Assassin";
    const string AssassinArmature = "Assets/2.Prefabs/Player/Assassin/Assassin_Armature.prefab";
    const string AssassinBodyTex = "Assets/50.Art/Char/assassin/Textures/Assassin_BaseColor.png";
    const int AssassinSlotCount = 3; // HeadNeck 2 · SourceBody 1. 좌우 단검 전용 재질은 제외(10-26).

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

        // 무기 — 기존 GUID와 무기별 텍스처/틴트를 유지한다.
        foreach (string w in new[] { "FK_Paladin_Sword_Toon.mat", "FK_Paladin_Shield_Toon.mat" })
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/{w}");
            if (m == null) { Debug.LogWarning($"[PlayerVariants] 없음: {w}"); continue; }
            Color tint = m.GetColor("_BaseColor");
            MakeVariant(m, parent, m.GetTexture("_BaseMap") as Texture2D, tint);
        }

        // 거너 — 신규 Variant.
        if (!AssetDatabase.IsValidFolder(GunnerDir)) AssetDatabase.CreateFolder(Root, "Gunner");
        var body = LoadOrCreate($"{GunnerDir}/FK_Gunner_Body.mat", parent);
        MakeVariant(body, parent, AssetDatabase.LoadAssetAtPath<Texture2D>(GunnerBodyTex), Color.white);
        var gun = LoadOrCreate($"{GunnerDir}/FK_LaserGun.mat", parent);
        MakeVariant(gun, parent, AssetDatabase.LoadAssetAtPath<Texture2D>(LaserGunTex), Color.white);

        // 어쌔신 — 신규 Variant. 텍스처는 SVN 아트라 없으면(작업사본이 낡음) 하얀 몸으로 덮지 않고 건너뛴다.
        Material assassin = null;
        var assassinTex = AssetDatabase.LoadAssetAtPath<Texture2D>(AssassinBodyTex);
        if (assassinTex == null)
            Debug.LogError($"[PlayerVariants] 어쌔신 텍스처 없음(SVN 확인): {AssassinBodyTex} — 어쌔신 Variant 건너뜀");
        else
        {
            if (!AssetDatabase.IsValidFolder(AssassinDir)) AssetDatabase.CreateFolder(Root, "Assassin");
            assassin = LoadOrCreate($"{AssassinDir}/FK_Assassin_Body.mat", parent);
            MakeVariant(assassin, parent, assassinTex, Color.white);
        }

        AssetDatabase.SaveAssets();

        AssignGunner(body, gun);
        if (assassin != null) AssignAssassin(assassin);
        MoveLegacy();

        AssetDatabase.SaveAssets();
        Debug.Log($"[PlayerVariants] 완료 — 부모 {parent.name} · Variant: 검·방패·{body.name}·{gun.name}{(assassin != null ? "·" + assassin.name : "")}");
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

    // 이미 분리된 단검 재질은 이름이 달라 보존된다. 몸·얼굴의 몸 재질 사용 슬롯은 3개다.
    static void AssignAssassin(Material body)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(AssassinArmature);
        int swapped = 0, inUse = 0;
        try
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && mats[i].name == "Assassin_Toon")
                    {
                        mats[i] = body;
                        changed = true;
                        swapped++;
                    }
                    if (mats[i] == body) inUse++;
                }
                if (changed) r.sharedMaterials = mats;
            }
            if (swapped > 0) PrefabUtility.SaveAsPrefabAsset(root, AssassinArmature);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        string msg = $"[PlayerVariants] {AssassinArmature} — 슬롯 {swapped}개 교체 · {body.name} 사용 슬롯 {inUse}개";
        if (inUse != AssassinSlotCount) Debug.LogWarning($"{msg} (기대 {AssassinSlotCount}개)");
        else Debug.Log(msg);
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
