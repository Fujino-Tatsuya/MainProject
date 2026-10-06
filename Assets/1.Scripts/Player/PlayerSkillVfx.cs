using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 플레이어 연출 중 <b>한 피어만 아는 것</b>을 나머지 피어로 퍼뜨리는 창구.
/// 최후의 심판(R) 전부 — 조준 순간 칼에 내리꽂히는 낙뢰, 그 뒤 칼날을 타고 흐르는 전기,
/// 시전 확정 시 대상 발밑에 깔리는 표식 — 그리고 <b>대시 잔상</b>을 맡는다.
///
/// <b>방향이 둘이고, 아는 피어가 누구냐로 갈린다.</b>
/// <list type="bullet">
/// <item><b>오너만</b> 아는 것 = 조준 진입/취소. 오너가 서버로 올리고 서버가 나머지에 뿌린다(두 홉).</item>
/// <item><b>서버만</b> 아는 것 = 지목된 대상. 서버가 바로 전 피어에 뿌린다(한 홉) —
///       <see cref="PlayerShieldVfx"/>와 같은 모양이다.</item>
/// </list>
/// 이유는 하나다: 그 사실을 아는 피어가 하나뿐이라 로컬 재생만으로는 다른 피어에 보이지 않는다.
///
/// <b>왜 스킬이 직접 못 하나.</b> 조준은 <see cref="PlayerSkillTargeting"/>의 <b>오너 전용</b> 입력 경로다
/// (<c>Update</c>가 <c>owner.IsInputSource</c>로 막혀 있다). 거기서 바로 재생하면 이 레포가 여러 번 겪은
/// "호스트에서만 보인다"의 오너 판본이 된다. 게다가 스킬의 부모 <c>PlayerSkillBase</c>는
/// MonoBehaviour라 RPC를 달 수 없다.
///
/// ⚠️ <b>오너는 왕복을 기다리지 않는다.</b> 기다리면 "R 누르는 즉시"가 깨진다 —
/// 자기 화면에는 로컬로 바로 켜고, <b>남들 몫만</b> 서버를 거쳐 보낸다(<c>SendTo.NotOwner</c>).
///
/// ⚠️ <b>원샷이 아니라 루프다.</b> 조준은 취소될 수 있고 취소되면 연출도 걷혀야 하는데,
/// <c>PlayOnce</c>는 핸들을 들지 않아 도중에 끌 수 없다. 대신 핸들을 드는 쪽은
/// <b>반드시 누군가 꺼야 한다</b> — 시전으로 이어지면 스킬의 <c>OnEnd</c>가,
/// 물리면 <see cref="OwnerStopUltimateVfx"/>가 끈다. 소켓의 safetyTimeout은 그 둘이 다 실패했을 때의 그물이다.
///
/// <b>둘의 시차는 엔트리가 만든다.</b> 전기는 낙뢰가 떨어진 <i>뒤에</i> 흘러야 하는데, 여기서 코루틴으로
/// 재는 대신 <c>FX_Sword_Electric_Entry</c>의 파트 <c>delay</c>가 처리한다 — 두 소켓은 같은 순간에 켠다.
/// </summary>
[DisallowMultipleComponent]
public class PlayerSkillVfx : BaseNetworkBehaviour
{
    [Header("연출 — 최후의 심판(R)")]
    [Tooltip("조준 순간 칼에 내리꽂히는 낙뢰. 프리팹의 'VFX/UltimateSkill/SwordLighting' 을 물린다. 비워두면 연출만 빠진다")]
    [SerializeField] private EffectSocketPlayer swordLightning;

    [Tooltip("낙뢰 뒤 칼날을 타고 흐르는 전기. 프리팹의 'VFX/UltimateSkill/SwordElectric' 을 물린다. 낙뢰와의 시차는 엔트리의 파트 delay 가 정한다")]
    [SerializeField] private EffectSocketPlayer swordElectric;

    [Tooltip("시전이 확정되면 대상 발밑에 까는 표식. FX_Target_Floor_Entry 를 물린다.\n" +
             "소켓이 아니라 엔트리를 직접 받는다 — 붙을 자리가 내 몸이 아니라 '그때 지목된 남'이라 미리 고정할 수 없다")]
    [SerializeField] private EffectEntry targetFloor;

    [Tooltip("표식 배율. 저작 크기가 10유닛이라 대개 줄여 쓴다")]
    [SerializeField, Min(0.01f)] private float targetFloorScale = 1f;

    [Tooltip("채널을 완주했을 때 대상 자리에 내리꽂을 낙뢰. FX_Lightening_Strike_Entry 를 물린다.\n" +
             "표식과 같은 자리에 떨어진다 — 좌표를 따로 넘기지 않는다")]
    [SerializeField] private EffectEntry targetStrike;

    [Tooltip("낙뢰 배율")]
    [SerializeField, Min(0.01f)] private float targetStrikeScale = 1f;

    // 소켓 컴포넌트가 대신 들어 주지 않으므로 핸들을 직접 든다. 🔴 드는 쪽이 반드시 반납해야 한다.
    private EffectHandle _targetFloorHandle;

    // 대시 잔상용. 셋 다 Awake 에서 찾는다 — 아래 Awake 주석 참고.
    private Player _player;
    private PlayerStateController _stateController;
    private DashAfterimage _dashAfterimage;
    private bool _dashWasActive;

    // 🔴 표식이 서 있는 자리. 마무리 낙뢰도 여기로 떨어진다.
    // 표식을 깔 때 한 번 뽑고 그 뒤로는 안 바꾼다 — 대상을 **추종하지 않는다**(아래 PlayTargetFloorLocal 참고).
    // 회수(StopTargetFloorLocal)해도 지우지 않는다: 낙뢰 RPC 와 종료 RPC 의 도착 순서를 믿지 않기 위해서다.
    private Vector3 _targetPosition;

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

    /// <summary>
    /// [오너] 조준 연출을 켠다. 자기 화면은 즉시, 나머지 피어는 서버를 거쳐서.
    /// </summary>
    public void OwnerPlayUltimateVfx()
    {
        PlayLocal();

        // 오프라인(싱글 테스트·VFXScene)에서는 스폰되지 않아 RPC를 부르면 예외가 난다.
        if (!IsNetworkActive || !IsOwner) return;

        RequestPlayRpc();
    }

    /// <summary>
    /// [오너] 조준 연출을 걷는다. 조준을 물렸을 때 — 시전까지 간 경우는
    /// <see cref="StopUltimateVfxLocal"/>이 각 피어에서 처리한다.
    /// </summary>
    public void OwnerStopUltimateVfx()
    {
        StopUltimateVfxLocal();

        if (!IsNetworkActive || !IsOwner) return;

        RequestStopRpc();
    }

    /// <summary>
    /// [전 피어] 로컬 정리. 스킬의 <c>OnEnd</c>는 모든 피어에서 도니 여기는 RPC가 필요 없다.
    /// 재생 중이 아니면 조용한 no-op이라 중복 호출을 걱정하지 않아도 된다.
    /// </summary>
    public void StopUltimateVfxLocal()
    {
        swordLightning?.Stop();
        swordElectric?.Stop();
    }

    private void PlayLocal()
    {
        swordLightning?.Play();
        swordElectric?.Play();
    }

    // ── 대상 표식 (서버 → 전 피어) ──────────────────────────────────

    /// <summary>
    /// [서버] 시전이 확정됐다. 지목된 대상 발밑에 표식을 깐다.
    ///
    /// <b>왜 서버가 알려야 하나.</b> "누구를 지목했는지"는 <c>PlaySkillClientRpc</c>에 실려 가지 않는다 —
    /// 그 RPC는 <c>slot / direction / aimPoint / hasAimPoint</c>만 보내고, SingleTarget 스킬은
    /// <c>aimPoint</c>를 쓰지 않아 <b>항상 0</b>이다. 그래서 리모트 클라는 대상을 알 방법이 없다.
    ///
    /// <b>대상 참조를 보내되 추종하지는 않는다.</b> 참조를 보내는 이유는 각 피어가
    /// <b>자기가 지금 그리고 있는</b> 위치를 읽게 하려는 것이다 — 서버가 실어 보낸 좌표는
    /// 보간 때문에 피어마다 어긋난다. 위치는 표식을 까는 순간 <b>한 번만</b> 뽑고 그 뒤로 고정이다.
    /// 참조가 풀리면(디스폰 등) 시전 시점 좌표로 물러선다.
    ///
    /// 🔴 <b>추종을 끊은 이유</b>(2026-09-30). <c>EffectManager.UpdateFollow</c> 는 매 프레임
    /// position 과 <b>rotation 을 같이</b> 베낀다. 그래서 몬스터가 제자리에서 돌기만 해도
    /// 바닥 표식이 같이 돌았다. 회전만 빼는 옵션은 <c>EffectManager</c> 에 없다.
    /// 트레이드오프: 채널 1.5초 동안 몬스터가 걸어 나가면 표식은 <b>시전 자리에 남는다</b>.
    /// 마무리 낙뢰도 같은 자리에 떨어지므로 둘이 어긋나지는 않는다.
    /// </summary>
    public void ServerPlayTargetFloor(Unit target)
    {
        if (target == null) return;

        Vector3 fallback = target.transform.position;
        bool hasRef = target.NetworkObject != null && target.NetworkObject.IsSpawned;

        // 오프라인(싱글 테스트·VFXScene)에서는 스폰되지 않아 RPC를 부르면 예외가 난다.
        if (!IsNetworkActive)
        {
            PlayTargetFloorLocal(target.transform, fallback);
            return;
        }

        if (!IsServer) return;

        PlayTargetFloorRpc(hasRef ? target.NetworkObject : default, hasRef, fallback);
    }

    /// <summary>
    /// [전 피어] 표식을 걷는다. 스킬의 <c>OnEnd</c>는 모든 피어에서 도니 여기는 RPC가 필요 없다.
    /// 깔려 있지 않으면 조용한 no-op이다.
    /// </summary>
    public void StopTargetFloorLocal()
    {
        if (!_targetFloorHandle.IsSet) return;

        if (EffectManager.Instance != null) EffectManager.Instance.Release(_targetFloorHandle);
        _targetFloorHandle = EffectHandle.None;
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void PlayTargetFloorRpc(NetworkObjectReference targetRef, bool hasRef, Vector3 fallbackPosition)
    {
        // 트랜스폼을 찾아도 추종하지 않는다 — 위치를 한 번 읽는 용도다.
        Transform target = null;
        if (hasRef && targetRef.TryGet(out NetworkObject targetObject))
            target = targetObject.transform;

        PlayTargetFloorLocal(target, fallbackPosition);
    }

    /// <summary>
    /// [서버] 채널을 완주했다. <b>표식이 서 있는 그 자리</b>에 마무리 낙뢰를 내리꽂는다.
    ///
    /// <b>좌표를 다시 보내지 않는다.</b> 각 피어는 표식을 깔 때 자리를 이미 고정해 뒀다
    /// (<see cref="_targetPosition"/>). 그걸 그대로 쓰면 표식과 낙뢰가 <b>정의상 같은 자리</b>다 —
    /// 좌표를 새로 실어 보내면 서버가 본 위치와 그 피어가 보간으로 그리고 있는 위치가 달라
    /// 표식 옆에 떨어진다.
    /// </summary>
    public void ServerPlayTargetStrike()
    {
        if (!IsNetworkActive)
        {
            PlayTargetStrikeLocal();
            return;
        }

        if (!IsServer) return;

        PlayTargetStrikeRpc();
    }

    // 원샷이지만 Reliable 이다. 이 레포의 다른 원샷 연출(피격 파문 등)은 난타 중 한 발이 빠져도 표가 안 나서
    // Unreliable 인데, 이건 궁극기 마무리라 한 번뿐이다 — 유실되면 클라이맥스가 통째로 사라진다.
    [Rpc(SendTo.ClientsAndHost)]
    private void PlayTargetStrikeRpc() => PlayTargetStrikeLocal();

    private void PlayTargetStrikeLocal()
    {
        if (targetStrike == null || EffectManager.Instance == null) return;

        // 표식을 깔 때 고정해 둔 그 자리. 표식이 안 움직이므로 낙뢰도 따라 움직이면 안 된다 —
        // 둘이 어긋나면 "표식 옆에 칼이 꽂히는" 그림이 된다.
        // 원샷은 핸들이 없다 — 엔트리의 computedDuration 뒤에 매니저가 알아서 반납한다.
        EffectManager.Instance.Play(targetStrike, _targetPosition, Quaternion.identity, targetStrikeScale);
    }

    private void PlayTargetFloorLocal(Transform target, Vector3 fallbackPosition)
    {
        // 🔴 자리를 **딱 한 번** 뽑아 고정한다. 표식도 마무리 낙뢰도 여기만 본다.
        //    대상 트랜스폼을 찾았으면 그걸 쓴다 — 서버가 실어 보낸 좌표보다
        //    "이 피어가 지금 그리고 있는 위치"에 가깝다(보간 차이를 안 탄다).
        _targetPosition = target != null ? target.position : fallbackPosition;

        // 재시전으로 두 겹이 되지 않게 먼저 회수한다(EffectSocketPlayer.Play 와 같은 규칙).
        StopTargetFloorLocal();

        if (targetFloor == null || EffectManager.Instance == null) return;

        // 🔴 추종 오버로드를 쓰지 않는다. EffectManager.UpdateFollow 는 매 프레임
        //    position 과 **rotation 을 같이** 베끼므로, 몬스터가 제자리에서 돌면 바닥 표식도
        //    같이 돈다(2026-09-30 반려 사유). 회전만 빼는 옵션은 EffectManager 에 없다.
        _targetFloorHandle = EffectManager.Instance.PlayLooping(
            targetFloor, _targetPosition, Quaternion.identity, targetFloorScale);
    }

    // 플레이어가 꺼지거나 파괴될 때(디스폰·씬 언로드) 마지막으로 회수한다.
    // 소켓 이펙트는 EffectSocketPlayer.OnDisable 이 알아서 하지만, 이 핸들은 내가 든 것이라 내가 놓아야 한다.
    private void OnDisable()
    {
        StopTargetFloorLocal();

        // 잔상 자체는 DashAfterimage.OnDisable 이 접는다. 여기서는 엣지 감지만 되돌린다 —
        // 안 되돌리면 다시 켜졌을 때 "이미 대시 중"으로 읽어 시작 RPC 를 한 번 건너뛴다.
        _dashWasActive = false;
    }

    // ── 대시 잔상 (이동 권한 → 전 피어) ─────────────────────────────

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
    /// (MPPM 에서만 드러나는 증상이다). 그래서 아래는 최후의 심판과 같은 <b>2홉</b>이다.
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
        if (active) _dashAfterimage.Play();
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
    private void PlayDashAfterimageLocal() => _dashAfterimage?.Play();

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

    // ── 오너 → 서버 → 오너를 뺀 전원 ────────────────────────────────
    // 두 홉인 이유: 클라는 다른 클라에게 직접 못 보낸다. 서버가 중계한다.
    // 둘 다 Reliable 이다 — 켜기를 놓치면 남들 화면에만 연출이 빠지고,
    // 끄기를 놓치면 남들 화면에 영영 남고 풀 인스턴스도 안 돌아온다. 스킬당 두 발이라 비용도 무시할 만하다.

    [Rpc(SendTo.Server)] // RequireOwnership 기본값 true — 오너만 부를 수 있다
    private void RequestPlayRpc() => PlayUltimateVfxRpc();

    [Rpc(SendTo.Server)]
    private void RequestStopRpc() => StopUltimateVfxRpc();

    // 오너는 이미 로컬로 켜고 껐다. 여기서 다시 보내면 한 번 더 재생된다.
    [Rpc(SendTo.NotOwner)]
    private void PlayUltimateVfxRpc() => PlayLocal();

    [Rpc(SendTo.NotOwner)]
    private void StopUltimateVfxRpc() => StopUltimateVfxLocal();
}
