using UnityEditor;
using UnityEngine;

/// <summary>
/// 플레이어블 공통 툰 머티리얼 정리 (PLAN-playable-toon-material.md). 메뉴 한 번으로 아래를 맞춘다 — 재실행 안전.
/// 1) 공통 부모 <c>PlayableCharacter_Toon</c>(구 Paladin_Toon, guid 유지). 부모의 Base Map 은 비운다.
/// 2) 캐릭터·무기 Variant: Base Map 만 덮고 나머지는 전부 부모 값(무기 전용 값·틴트는 버린다 — 텍스처 없는 무기는 흰색).
/// 3) Paladin_Armature·Gunner_Armature 렌더러 슬롯을 Variant 로 교체.
/// </summary>
public static class PlayableToonMaterialAuthoring
{
    const string Folder = "Assets/3.Materials/Toon";
    const string OldParentPath = Folder + "/Paladin_Toon.mat";
    const string ParentPath = Folder + "/PlayableCharacter_Toon.mat";
    const string PaladinBodyPath = Folder + "/Paladin_Toon.mat";
    const string PaladinSwordPath = Folder + "/Paladin_Sword_Toon.mat";
    const string PaladinShieldPath = Folder + "/Paladin_Shield_Toon.mat";
    const string GunnerBodyPath = Folder + "/Gunner_Toon.mat";
    const string LaserGunPath = Folder + "/LaserGun_Toon.mat";

    const string PaladinTexPath = "Assets/50.Art/Char/tex/paladin_basecolor.jpg";
    const string GunnerTexPath = "Assets/50.Art/Char/gunner/LaserGun_BaseColor_Toon.png";

    const string PaladinArmaturePath = "Assets/2.Prefabs/Player/Paladin/Paladin_Armature.prefab";
    const string GunnerArmaturePath = "Assets/2.Prefabs/Player/Gunner/Gunner_Armature.prefab";

    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

    [MenuItem("Tools/Player/Toon Material/공통 머티리얼 + Variant 정리")]
    public static void Run()
    {
        Material parent = EnsureParent();
        if (parent == null)
            return;

        Material paladinBody = EnsureVariant(PaladinBodyPath, parent, PaladinTexPath);
        EnsureVariant(PaladinSwordPath, parent, null);
        EnsureVariant(PaladinShieldPath, parent, null);
        Material gunnerBody = EnsureVariant(GunnerBodyPath, parent, GunnerTexPath);
        Material laserGun = EnsureVariant(LaserGunPath, parent, GunnerTexPath);
        AssetDatabase.SaveAssets();

        // 가붕이 몸체: 부모를 직접 쓰던 슬롯만 Variant 로. 검·방패 슬롯은 같은 에셋(guid 유지)이라 손대지 않는다.
        RetargetSlots(PaladinArmaturePath, r => true, m => m == parent ? paladinBody : m);
        // 거너: 총 메시는 LaserGun, 나머지(몸체)는 Gunner.
        RetargetSlots(GunnerArmaturePath, r => r.name == "laser_gun_mesh", _ => laserGun);
        RetargetSlots(GunnerArmaturePath, r => r is SkinnedMeshRenderer, _ => gunnerBody);

        AssetDatabase.SaveAssets();
        Debug.Log("[ToonMaterial] 공통 머티리얼 + Variant 정리 완료");
    }

    /// <summary>Paladin_Toon 이 아직 부모라면 이름을 바꾼다(guid 유지). 부모 Base Map 은 비운다.</summary>
    static Material EnsureParent()
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(ParentPath) == null)
        {
            string error = AssetDatabase.MoveAsset(OldParentPath, ParentPath);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError($"[ToonMaterial] 부모 이름 변경 실패: {error}");
                return null;
            }
        }

        Material parent = AssetDatabase.LoadAssetAtPath<Material>(ParentPath);
        if (parent.parent != null)
        {
            Debug.LogError($"[ToonMaterial] 부모가 Variant 다 — 확인 필요: {ParentPath}");
            return null;
        }

        if (parent.GetTexture(BaseMapId) != null)
        {
            parent.SetTexture(BaseMapId, null);
            EditorUtility.SetDirty(parent);
        }
        return parent;
    }

    /// <summary>경로에 Variant 를 만들거나(기존 에셋이면 guid 유지) 부모를 붙이고, 덮어쓴 값을 전부 되돌린 뒤 Base Map 만 덮는다.</summary>
    static Material EnsureVariant(string path, Material parent, string texturePath)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(parent) { parent = parent };
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (mat.parent != parent)
        {
            mat.parent = parent;
        }

        mat.RevertAllPropertyOverrides();

        if (texturePath != null)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture>(texturePath);
            if (tex == null)
                Debug.LogWarning($"[ToonMaterial] 텍스처 없음(SVN 확인): {texturePath}");
            mat.SetTexture(BaseMapId, tex);
        }

        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void RetargetSlots(string prefabPath, System.Func<Renderer, bool> filter, System.Func<Material, Material> map)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            bool changed = false;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!filter(r))
                    continue;

                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material next = map(mats[i]);
                    if (next == mats[i])
                        continue;
                    mats[i] = next;
                    changed = true;
                }
                r.sharedMaterials = mats;
            }

            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"[ToonMaterial] 슬롯 교체: {prefabPath}");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
