using UnityEngine;
using Unity.Netcode;

public class TwentyThreeAnimEvents : NetworkBehaviour
{
    [SerializeField] GrabController grabController;
    [SerializeField] JumpController jumpController;

    [Header("공격 궤적 VFX")]
    [Tooltip("각 공격 오브젝트(UpperAttack / RightHookAttack / LeftHookAttack)에 붙인 EffectSocketPlayer.\n" +
             "애니메이션 이벤트는 Animator가 붙은 오브젝트의 컴포넌트에만 전달되므로, 자식에 붙은 것들은 " +
             "여기서 중계해야 한다")]
    [SerializeField] EffectSocketPlayer upperAttackTrail;
    [SerializeField] EffectSocketPlayer rightHookTrail;
    [SerializeField] EffectSocketPlayer leftHookTrail;

    void Start()
    {

    }

    // ── 공격 궤적 ────────────────────────────────────────────────────────────
    // ⚠️ 아래 여섯 개는 이 파일에서 유일하게 IsServer 가드가 없는 메서드들이다. 의도된 것이다 —
    // 연출은 각 피어가 자기 화면에 그려야 한다. 위쪽 메서드들을 흉내 내 IsServer로 감싸면
    // 호스트에서만 궤적이 보인다(피격 이펙트에서 이미 한 번 낸 버그다).
    // 애니메이션 이벤트 자체가 모든 피어에서 발화하므로 RPC도 필요 없다.

    public void UpperAttackTrailStart() => upperAttackTrail?.Play();
    public void UpperAttackTrailEnd() => upperAttackTrail?.Stop();

    public void RightHookTrailStart() => rightHookTrail?.Play();
    public void RightHookTrailEnd() => rightHookTrail?.Stop();

    public void LeftHookTrailStart() => leftHookTrail?.Play();
    public void LeftHookTrailEnd() => leftHookTrail?.Stop();
    public void GrabLightningEvent()
    {
        if (IsServer)
            grabController.PlayLightningVFXClientRpc();
    }

    public void TryGrabEvent()
    {
        if(IsServer)
            grabController.Detect();
    }

    public void ThrowEvent()
    {
        if (IsServer)
            grabController.Throw();
    }

    public void SetTargetEvent()
    {
        if (IsServer)
            jumpController.SetTarget();
    }

    public void FallEvent()
    {
        if (IsServer)
            jumpController.ShowMyMeshClientRpc(true);
    }

    public void OnLandedEvent()
    {
        if (IsServer)
            jumpController.OnLanded();
    }
}
