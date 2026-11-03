using UnityEngine;

/// <summary>
/// 캐릭터 오른손 무기 기준점(할 일 194 후속) — 캐릭터 고유라 Variant(Player_Paladin/Gunner/Assassin) 루트에 붙인다.
/// 무기 모양·본 계층이 캐릭터마다 달라(팔라딘 R_WeaponSocket · 거너 rig/artillery/LaserGun · 암살자 Dagger_Weapon_R) 이름으로 찾지 않고 여기서 지정한다.
/// 기준점 = <see cref="rightWeapon"/> 위치(소켓·무기 원점 = 손잡이) + 그 무기 로컬 축 <see cref="localOffset"/>(칼끝 쪽으로 옮기려면 여기).
/// 지금 쓰는 곳: 간파 적중 라이트 월드 이펙트 발생 위치(<see cref="InterruptHitLightOrigin.CasterWeapon"/>).
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerWeaponAnchor : MonoBehaviour
{
    [Tooltip("오른손 무기 Transform(손 소켓 본·무기 부착 본). 애니메이션으로 같이 움직이는 Armature 안 본이어야 한다. 비면 기준점 없음")]
    [SerializeField] Transform rightWeapon;
    [Tooltip("Right Weapon 로컬 좌표 오프셋(m, 그 Transform 의 스케일 적용). 0 = 소켓 위치(손잡이)")]
    [SerializeField] Vector3 localOffset;

    public Transform RightWeapon => rightWeapon;

    /// <summary>오른손 무기 기준점(월드). 무기 칸이 비었으면 false.</summary>
    public bool TryGetRightWeaponPoint(out Vector3 point)
    {
        if (rightWeapon == null)
        {
            point = default;
            return false;
        }

        point = rightWeapon.TransformPoint(localOffset);
        return true;
    }

    void OnDrawGizmosSelected()
    {
        if (!TryGetRightWeaponPoint(out Vector3 point))
            return;

        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(point, 0.12f);
    }
}
