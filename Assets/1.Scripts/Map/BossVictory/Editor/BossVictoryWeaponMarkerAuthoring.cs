using UnityEditor;
using UnityEngine;

/// <summary>
/// 거너 총(LaserGun)에 처치 연출 카메라 기준 '처치자 무기' 표식(<see cref="BossVictoryWeaponMarker"/>)을 단다 — 메뉴 <c>Tools/Boss Victory/거너 총에 처치자 무기 표식</c>.
/// 거너는 WeaponTransformRelay 가 없어 표식이 없으면 처치자 발밑으로 폴백한다. 이미 있으면 그대로.
/// </summary>
public static class BossVictoryWeaponMarkerAuthoring
{
    public const string GunnerPrefabPath = "Assets/2.Prefabs/Player/Gunner/Player_Gunner.prefab";
    public const string GunnerWeaponName = "LaserGun";

    [MenuItem("Tools/Boss Victory/거너 총에 처치자 무기 표식")]
    public static void Apply()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(GunnerPrefabPath);
        try
        {
            Transform gun = PaladinShieldGripAuthoring.Find(root.transform, GunnerWeaponName);
            if (gun == null)
            {
                Debug.LogError($"[BossVictoryWeaponMarker] {GunnerPrefabPath} 에 {GunnerWeaponName} 이 없다.");
                return;
            }
            if (gun.GetComponent<BossVictoryWeaponMarker>() != null)
            {
                Debug.Log($"[BossVictoryWeaponMarker] 이미 있음 — {GunnerWeaponName}");
                return;
            }
            gun.gameObject.AddComponent<BossVictoryWeaponMarker>();
            PrefabUtility.SaveAsPrefabAsset(root, GunnerPrefabPath);
            Debug.Log($"[BossVictoryWeaponMarker] 달음 — {GunnerPrefabPath} / {gun.name}");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
