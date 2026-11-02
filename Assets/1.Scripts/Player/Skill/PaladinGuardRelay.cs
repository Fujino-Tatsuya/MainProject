using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 팔라딘 전용 전 피어 전송 창구(2026-11-02 — <see cref="PlayerShieldVfx"/> 에서 분리, 동작 무변경).
/// <c>Player_Paladin</c> 루트(NetworkObject 와 같은 GameObject)에만 붙는다.
///
/// 팔라딘 스킬(<see cref="FirstMeleeSubSkill"/>·<see cref="FirstMeleeInterruptSkill"/>)의 부모 <c>PlayerSkillBase</c> 는
/// MonoBehaviour 라 RPC 를 달 수 없다. 그래서 서버 판정 뒤 전 피어에 퍼뜨려야 하는 두 가지를 여기 둔다.
/// <list type="bullet">
/// <item>반격 자세 전환(<see cref="ServerPlayGuardCounterPose"/>) — 수호자의 의지 방어 성공·E 재입력 해제.</item>
/// <item>단죄의 방패 적중 충격파(<see cref="ServerInterruptWave"/>) — 간파 연출 디렉터 B3 칸을 거쳐 재생.</item>
/// </list>
/// 예전에는 보호막 연출(<see cref="PlayerShieldVfx"/>)에 같이 있었는데, 보호막 연출이 모든 캐릭터에 붙으면서
/// 팔라딘 전용 코드가 거너·어쌔신에 딸려 가지 않게 갈랐다.
/// </summary>
[DisallowMultipleComponent]
public class PaladinGuardRelay : BaseNetworkBehaviour, IInterruptCueSink
{
    [Tooltip("단죄의 방패(우클릭)가 적중했을 때 한 번 터뜨릴 충격파. 프리팹의 'ShieldWave' 를 물린다.\n" +
             "비워두면 연출만 빠진다")]
    [SerializeField] private EffectSocketPlayer interruptWave;

    private Animator _animator;
    private PlayerStateController _stateController;

    /// <summary>
    /// [서버] 단죄의 방패가 적중했다. 충격파를 전 피어에 한 번 터뜨린다.
    ///
    /// <b>왜 스킬이 직접 못 하나.</b> 적중 판정(<c>FirstMeleeInterruptSkill.ResolveHit</c>)은
    /// <b>서버에서만</b> 돈다 — <c>OnTick</c>은 <c>TickServer</c>가, 애니메이션 이벤트는
    /// <c>PlayerSkillController.HandleAnimationEvent</c>의 <c>if (IsNetworkActive &amp;&amp; !IsServer) return;</c>가
    /// 각각 막는다. 거기서 바로 재생하면 <b>호스트에서만 보인다</b>.
    /// 그리고 스킬의 부모 <c>PlayerSkillBase</c>는 MonoBehaviour라 RPC를 달 수 없다.
    /// </summary>
    public void ServerInterruptWave()
    {
        if (!IsNetworkActive)
        {
            PlayInterruptWave();
            return;
        }

        if (!IsServer) return;

        InterruptWaveRpc();
    }

    // 원샷이라 Unreliable 이다. 한 발 유실되면 충격파 한 번이 안 뜰 뿐 상태가 어긋나지 않는다.
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    private void InterruptWaveRpc() => PlayInterruptWave();

    // 간파 연출 디렉터(할 일 118) — B3 칸은 피어마다 자기 값. 켜져 있으면 디렉터가 PlayCue 로 돌려 부른다.
    private void PlayInterruptWave() =>
        InterruptProductionDirector.PlayCue(InterruptProductionCue.PaladinWave,
            InterruptProductionEvent.LocalCue(InterruptProductionPhase.Success, IsSpawned ? OwnerClientId : 0), this);

    // [각 피어] B3 충격파 원샷.
    void IInterruptCueSink.PlayCue(InterruptProductionCue cue, in InterruptProductionEvent e)
    {
        if (cue == InterruptProductionCue.PaladinWave)
            interruptWave?.PlayOnce();
    }

    /// <summary>
    /// [서버] 수호자의 의지(E) 방어 성공 — 반격 자세 상태로 <b>전 피어(오너 포함)</b>에서 CrossFade 한다.
    /// 방어중 E 재입력 해제(해제 상태 SubSkillGuardEnd, 할 일 94)도 같은 창구를 쓴다.
    ///
    /// <b>왜 서버만 하면 안 되나.</b> 반격 판정·종료는 반격 클립의 Hit·End 이벤트로 정해지는데,
    /// 스킬 이벤트는 서버 애니메이터에서만 처리된다. 플레이어에는 오너 권한 NetworkAnimator 가 있어
    /// 서버만 바꾸면 오너 동기가 상태를 방어 자세로 되돌린다 — 그러면 Hit·End 가 안 온다.
    /// 그래서 시작 상태와 같은 길을 탄다(<c>PlayerSkillController</c>: 서버 PlaySkillPresentation →
    /// PlaySkillClientRpc 의 비서버 피어 CrossFadeInFixedTime). 스킬 부모는 MonoBehaviour 라 RPC 를 여기 둔다.
    /// </summary>
    public void ServerPlayGuardCounterPose(int stateHash, float fadeSeconds)
    {
        if (IsNetworkActive && !IsServer) return;

        // 서버(오프라인 포함)는 바로 바꾼다 — 판정 이벤트가 이 애니메이터에서 난다.
        PlayGuardCounterPoseLocal(stateHash, fadeSeconds);

        if (IsNetworkActive)
            GuardCounterPoseRpc(stateHash, fadeSeconds);
    }

    // 상태 전환이라 Reliable 이다 — 유실되면 그 피어 화면이 방어 자세에 남는다(오너면 동기로 서버까지 되돌린다).
    // 이 피어가 이미 E 를 떠났으면(오너가 대시로 먼저 끊음, 할 일 95) 늦게 온 자세를 틀지 않는다 — 대시·이동 중에 반격·해제 자세가
    // 다시 켜지면 오너 권한 NetworkAnimator 가 그대로 전 피어로 퍼뜨린다. 관찰자도 PlaySkillClientRpc~EndSkillClientRpc 동안 스킬 상태다.
    [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Reliable)]
    private void GuardCounterPoseRpc(int stateHash, float fadeSeconds)
    {
        if (_stateController == null)
            _stateController = GetComponent<PlayerStateController>();

        if (_stateController != null && !_stateController.IsInSkillState)
            return;

        PlayGuardCounterPoseLocal(stateHash, fadeSeconds);
    }

    private void PlayGuardCounterPoseLocal(int stateHash, float fadeSeconds)
    {
        // Player·PlayerSkillController 와 같은 해석(미지정이면 자식의 첫 Animator).
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>();

        if (_animator != null)
            _animator.CrossFadeInFixedTime(stateHash, fadeSeconds);
    }
}
