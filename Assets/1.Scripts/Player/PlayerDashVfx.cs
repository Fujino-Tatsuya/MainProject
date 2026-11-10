using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 대시 잔상(<see cref="DashAfterimage"/>)을 전 피어에서 켜고 끄는 창구. <b>모든 캐릭터</b> 루트에 붙는다.
///
/// 2026-11-02 — 옛 <c>PlayerSkillVfx</c> 에서 대시 잔상만 남기고 이름을 바꿨다(guid 그대로라 프리팹 참조 유지).
/// 거기 같이 있던 팔라딘 최후의 심판 연출은 <see cref="PaladinSkillView"/> 로 옮겼다.
///
/// 대시 상태는 <b>이동 권한(오너)</b>만 안다 — 오너가 자기 화면은 즉시 켜고, 남들 몫만 서버를 거쳐 보낸다(아래 <see cref="Update"/>).
/// 대시가 아닌 상태에서 잔상을 쓰는 스킬(거너 E 백스텝 등)은 각자 직접 부른다.
/// </summary>
[DisallowMultipleComponent]
public class PlayerDashVfx : BaseNetworkBehaviour
{
    // 셋 다 Awake 에서 찾는다 — 아래 Awake 주석 참고.
    private Player _player;
    private PlayerStateController _stateController;
    private DashAfterimage _dashAfterimage;
    private bool _dashWasActive;

    private void Awake()
    {
        // Player 와 PlayerStateController 는 같은 루트에 있다
        // (PlayerStateController 의 RequireComponent 가 Player 를 보장한다).
        _player = GetComponent<Player>();
        _stateController = GetComponent<PlayerStateController>();

        // 🔴 잔상만 [SerializeField] 로 안 뺀 이유: 이 참조는 프리팹마다 다시 물려 줘야 하는데
        //    빠뜨려도 에러가 안 나고 "대시해도 잔상이 없다"로만 나타난다 — 원인을 찾기 가장 나쁜 증상이다.
        //    붙어 있으면 켜고 없으면 안 켠다. 끄고 싶으면 DashAfterimage 컴포넌트를 떼면 된다.
        //    자식까지 뒤지는 건 캐릭터 교체(Player 밑 Armature 교체) 설계 때문이다.
        _dashAfterimage = GetComponentInChildren<DashAfterimage>(true);
    }

    private void OnDisable()
    {
        // 잔상 자체는 DashAfterimage.OnDisable 이 접는다. 여기서는 엣지 감지만 되돌린다 —
        // 안 되돌리면 다시 켜졌을 때 "이미 대시 중"으로 읽어 시작 RPC 를 한 번 건너뛴다.
        _dashWasActive = false;
    }

    /// <summary>
    /// [이동 권한] 대시 상태를 감시해 시작/종료를 전 피어에 알린다.
    ///
    /// 🔴 <b>왜 <c>PlayerDashState.Enter/Exit</c> 에 직접 걸지 않았나</b> (2026-09-29 조사).
    /// <c>PlayerDashController.TryBeginPredictedDash</c> 는 <c>!player.IsMotionAuthority</c> 면
    /// <c>BeginDash</c> 를 <b>부르지 않고</b> 통과시킨다. 즉 <c>PlayerDashState</c> 는
    /// <b>이동 권한을 가진 피어에만 존재한다</b> — Enter/Exit 에 걸면 그 피어 화면에서만
    /// 잔상이 보이는, 이 레포가 여러 번 겪은 그 버그가 된다.
    ///
    /// 🔴 <b>그 권한이 2026-09-29 이후 뒤집혔다</b>(2026-10-06 발견).
    /// <c>Player.ServerAuthoritativeMovement == false</c> 라 <c>IsMotionAuthority == IsOwner</c> 다.
    /// 그래서 이 폴링은 <b>오너</b>에서 돌고, 오너는 클라일 수 있다 —
    /// 클라는 <c>SendTo.ClientsAndHost</c>(서버 전용) RPC 를 <b>보낼 수 없다.</b>
    /// 그걸 그대로 두면 <b>호스트가 대쉬할 때만 잔상이 보이고 클라가 대쉬하면 아무에게도 안 보인다</b>
    /// (MPPM 에서만 드러나는 증상이다). 그래서 아래는 오너 → 서버 → 오너를 뺀 전원의 <b>2홉</b>이다.
    ///
    /// 🔴 <b>왜 이벤트가 아니라 폴링인가.</b>
    /// <c>PlayerStateController</c> 에는 상태 전이 이벤트가 없고, 대시는 전용 애니메이터 상태도 없어서
    /// (<c>PlayerAnimatorController</c> 에 dash 가 없다) <c>NetworkAnimator</c> 로도 못 받는다.
    /// 남은 신호는 <c>CurrentState</c> 뿐이다. 매 프레임 enum 비교 한 번이고,
    /// <b>은희 님 상태기를 한 줄도 건드리지 않는다.</b>
    /// 덤으로 중단 경로(넉백·구속·연출 잠금·사망·서버 취소)를 하나도 빠뜨리지 않는다 —
    /// 어느 경로로 끝나든 <c>CurrentState</c> 가 Dash 가 아니게 되기 때문이다.
    /// </summary>
    private void Update()
    {
        if (_dashAfterimage == null || _stateController == null) return;

        // 대시 상태를 들고 있는 피어만 판정한다. 나머지는 아래 RPC 를 받아서 켠다.
        // _player 가 없으면(비네트워크 테스트 씬) 내가 곧 권한이다.
        if (_player != null && !_player.IsMotionAuthority) return;

        bool active = _stateController.CurrentState == PlayerActionState.Dash;
        if (active == _dashWasActive) return;
        _dashWasActive = active;

        // 내 화면은 즉시 — 아래 RPC 는 나를 뺀 전원에게만 간다.
        // 오프라인(VFXScene·싱글 테스트)에서는 스폰되지 않아 RPC 가 예외라 여기서 끝난다.
        if (active) { _dashAfterimage.Play(); PlayDashSound(); }
        else _dashAfterimage.Stop();

        if (!IsNetworkActive) return;

        // 🔴 호스트가 오너면 바로 뿌리고, 클라가 오너면 서버를 한 번 거친다.
        //    클라는 다른 클라에게 직접 못 보낸다.
        if (IsServer)
        {
            if (active) PlayDashAfterimageRpc();
            else StopDashAfterimageRpc();
            return;
        }

        if (active) RequestPlayDashRpc();
        else RequestStopDashRpc();
    }

    /// <summary>[전 피어] 각자 자기 화면에 잔상을 켠다. 속도선은 DashAfterimage 가 같이 끈다.</summary>
    private void PlayDashAfterimageLocal()
    {
        _dashAfterimage?.Play();
        PlayDashSound();
    }

    // 대시 소리(11-06) — 잔상과 같은 길(오너 즉시 · 남은 RPC)로 대시 시작마다 1번. 키 = Sound 시트.
    private const string DashSoundKey = "S1_Dash";

    private void PlayDashSound()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.Play(DashSoundKey, transform.position, this);   // 남의 대시 = 30%(Q6)
    }

    /// <summary>[전 피어] 잔상 생성을 멈춘다. 이미 떠 있는 조각은 제 수명대로 사라진다.</summary>
    private void StopDashAfterimageLocal() => _dashAfterimage?.Stop();

    // 오너 → 서버. RequireOwnership 기본값 true 라 오너만 부를 수 있다.
    [Rpc(SendTo.Server)]
    private void RequestPlayDashRpc() => PlayDashAfterimageRpc();

    [Rpc(SendTo.Server)]
    private void RequestStopDashRpc() => StopDashAfterimageRpc();

    // 서버 → 오너를 뺀 전원. 오너는 위 Update 에서 이미 로컬로 켜고 껐다.
    // 🔴 둘 다 Reliable 이다. 끄기를 놓치면 잔상이 영영 남는다.
    //    DashAfterimage.safetyTimeout(2초)은 그마저 샜을 때의 마지막 그물이지 대책이 아니다.
    [Rpc(SendTo.NotOwner)]
    private void PlayDashAfterimageRpc() => PlayDashAfterimageLocal();

    [Rpc(SendTo.NotOwner)]
    private void StopDashAfterimageRpc() => StopDashAfterimageLocal();
}
