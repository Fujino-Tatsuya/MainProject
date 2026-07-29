using UnityEngine;

/// <summary>
/// PlayerWeaponSlot에 붙는 단일 컴포넌트. 각 쌍(target, weapon)에 대해
/// weapon이 target(손 소켓 본)의 월드 위치·회전을 매 프레임 그대로 따라가게 한다.
/// 무기를 본 계층에 부모로 박지 않고 Player 바로 아래 WeaponSlot에 두면서도
/// 손을 따라가게 하려는 용도. 애니메이션 평가가 끝난 뒤 반영하려고 LateUpdate에서 갱신한다.
/// target은 스켈레톤 안(예: R_/L_WeaponSocket 본)에 있어야 애니메이션으로 같이 움직인다.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class WeaponTransformRelay : MonoBehaviour
{
    [System.Serializable]
    private struct WeaponFollow
    {
        [Tooltip("따라갈 대상. 스켈레톤 안의 손 소켓 본(R_/L_WeaponSocket 등).")]
        public Transform target;
        [Tooltip("같은 줄의 target을 따라가는 무기. 보통 WeaponSlot 자식.")]
        public Transform weapon;
    }

    [Tooltip("target을 weapon이 1:1로 따라간다. 인덱스가 아니라 한 줄로 짝지어진다.")]
    [SerializeField] private WeaponFollow[] follows;

    private void LateUpdate()
    {
        if (follows == null)
            return;

        for (int i = 0; i < follows.Length; i++)
        {
            Transform target = follows[i].target;
            Transform weapon = follows[i].weapon;
            if (target == null || weapon == null)
                continue;

            weapon.position = target.position;
            weapon.rotation = target.rotation;
        }
    }
}
