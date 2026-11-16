using UnityEngine;

/// <summary>
/// 보스 처치 연출 카메라 컷 기준 '처치자 무기'(<see cref="BossVictoryCameraCuts.Anchor.KillerWeapon"/>)가 따라갈 무기 오브젝트 표식 — 동작 없음.
/// 거너는 총(LaserGun)이 본 자식이라 WeaponTransformRelay 가 없어 이 표식으로 고른다. 찾는 순서 = <see cref="Find"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class BossVictoryWeaponMarker : MonoBehaviour
{
    /// <summary>
    /// 처치자 무기 — ① 처치자 아래 이 표식(첫 것) ② WeaponTransformRelay 첫 무기(손 소켓을 따라가는 무기 줄) ③ 없으면 null(부른 쪽이 처치자 발밑으로 + 경고).
    /// </summary>
    public static Transform Find(Transform killer)
    {
        if (killer == null) return null;
        BossVictoryWeaponMarker marker = killer.GetComponentInChildren<BossVictoryWeaponMarker>(true);
        if (marker != null) return marker.transform;
        WeaponTransformRelay relay = killer.GetComponentInChildren<WeaponTransformRelay>(true);
        return relay != null ? relay.FirstWeapon : null;
    }
}
