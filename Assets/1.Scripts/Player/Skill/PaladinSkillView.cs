using System.Collections;
using System.Collections.Generic;
using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 팔라딘 전용 연출 창구(2026-11-02 — 거너 <see cref="GunnerBeamView"/>·어쌔신 <see cref="AssassinSkillView"/> 와 같은 자리).
/// <c>Player_Paladin</c> 루트(NetworkObject 와 같은 GameObject)에만 붙는다.
///
/// 팔라딘 스킬(<c>FirstMelee*Skill</c>)의 부모 <c>PlayerSkillBase</c> 는 MonoBehaviour 라 RPC 를 달 수 없다.
/// 그래서 <b>한 피어만 아는 사실</b>을 전 피어 화면에 퍼뜨려야 하는 팔라딘 연출을 여기 모은다.
/// <list type="bullet">
/// <item>최후의 심판(R) — 칼 낙뢰·칼날 전기(오너 → 서버 → 나머지), 대상 표식·마무리 낙뢰(서버 → 전 피어).
///       <see cref="FirstMeleeUltimateSkill"/> 가 부른다. (옛 <c>PlayerSkillVfx</c> 에서 옮김)</item>
/// <item>수호자의 의지(E) 반격·해제 자세 전환 — <see cref="FirstMeleeSubSkill"/>. (옛 <c>PaladinGuardRelay</c>)</item>
/// <item>단죄의 방패(우클릭) 적중 충격파 — <see cref="FirstMeleeInterruptSkill"/>, 간파 연출 디렉터 B3 칸. (옛 <c>PaladinGuardRelay</c>)</item>
/// <item>나누는 불굴(PAL_2_SHARE_HEAL) 전이 — 특성 연출 이벤트를 받아 재생. (옛 <c>PaladinShareHealVfx</c>)</item>
/// </list>
/// 여기 없는 팔라딘 연출: 스킬 소켓 루프(스킬이 OnClientPlay/OnEnd 로 직접 켜고 끔)·클립 이벤트 소켓(<see cref="EffectAnimEvents"/>)·
/// 불굴의 의지 발동 연출(<c>FirstMeleePassive</c> 가 NetworkBehaviour 라 RPC 를 직접 단다).
/// 대시 잔상은 전 캐릭터 공용 <see cref="PlayerDashVfx"/>.
/// </summary>
[DisallowMultipleComponent]
public class PaladinSkillView : BaseNetworkBehaviour, IInterruptCueSink
{
    // ═════════════════════════════════════════════════════════════════
    //  최후의 심판(R)
    // ═════════════════════════════════════════════════════════════════
    //
    // <b>방향이 둘이고, 아는 피어가 누구냐로 갈린다.</b>
    //  - <b>오너만</b> 아는 것 = 조준 진입/취소. 오너가 서버로 올리고 서버가 나머지에 뿌린다(두 홉).
    //  - <b>서버만</b> 아는 것 = 지목된 대상. 서버가 바로 전 피어에 뿌린다(한 홉).
    //
    // 왜 스킬이 직접 못 하나 — 조준은 PlayerSkillTargeting 의 오너 전용 입력 경로다(Update 가 owner.IsInputSource 로 막혀 있다).
    // 거기서 바로 재생하면 "호스트에서만 보인다"의 오너 판본이 된다.
    //
    // ⚠️ 오너는 왕복을 기다리지 않는다. 기다리면 "R 누르는 즉시"가 깨진다 —
    //    자기 화면에는 로컬로 바로 켜고, 남들 몫만 서버를 거쳐 보낸다(SendTo.NotOwner).
    // ⚠️ 원샷이 아니라 루프다. 조준은 취소될 수 있고 취소되면 연출도 걷혀야 한다. 핸들을 드는 쪽은 반드시 누군가 꺼야 한다 —
    //    시전으로 이어지면 스킬의 OnEnd 가, 물리면 OwnerStopUltimateVfx 가 끈다. 소켓의 safetyTimeout 은 둘 다 실패했을 때의 그물이다.
    //
    // 둘의 시차는 엔트리가 만든다 — 전기는 낙뢰가 떨어진 뒤에 흘러야 하는데, 코루틴 대신 FX_Sword_Electric_Entry 의 파트 delay 가 처리한다.
    //
    // 재생 배율(playRate)은 부르는 쪽이 넘기고 RPC 에 함께 싣는다. 값의 원본은 스킬 데이터(FirstMeleeUltimateSkillData.VfxPlayRate)이고,
    // 결정한 피어(오너·서버)의 값을 모두가 쓴다 — 피어마다 자기 데이터를 읽게 하면 한쪽만 튜닝했을 때 화면끼리 속도가 갈린다.
    // 배율은 파트 delay 에도 걸린다(전기 0.45초 → 1.2배면 실제 0.375초). 낙뢰도 같이 빨라지니 순서는 그대로다.

    [Header("최후의 심판(R)")]
    [Tooltip("조준 순간 칼에 내리꽂히는 낙뢰. 프리팹의 'VFX/UltimateSkill/SwordLighting' 을 물린다. 비워두면 연출만 빠진다")]
    [SerializeField] private EffectSocketPlayer swordLightning;

    [Tooltip("낙뢰 뒤 칼날을 타고 흐르는 전기. 프리팹의 'VFX/UltimateSkill/SwordElectric' 을 물린다. 낙뢰와의 시차는 엔트리의 파트 delay 가 정한다")]
    [SerializeField] private EffectSocketPlayer swordElectric;

    [Tooltip("시전이 확정되면 대상 발밑에 까는 표식. FX_Target_Floor_Entry 를 물린다.\n" +
             "소켓이 아니라 엔트리를 직접 받는다 — 붙을 자리가 내 몸이 아니라 '그때 지목된 남'이라 미리 고정할 수 없다")]
    [SerializeField] private EffectEntry targetFloor;

    [Tooltip("표식 배율. 저작 크기가 10유닛이라 대개 줄여 쓴다")]
    [SerializeField, Min(0.01f)] private float targetFloorScale = 1f;

    [Tooltip("클립 Garen_Ultimate_Skill 의 Custom0 이벤트(피해 Hit 보다 앞) 때 대상 자리에 내리꽂을 낙뢰. FX_Lightening_Strike_Entry 를 물린다.\n" +
             "시각은 클립에서 Custom0 이벤트를 옮겨 맞춘다.\n" +
             "표식과 같은 자리에 떨어진다 — 좌표를 따로 넘기지 않는다")]
    [SerializeField] private EffectEntry targetStrike;

    [Tooltip("낙뢰 배율")]
    [SerializeField, Min(0.01f)] private float targetStrikeScale = 1f;

    [Header("단죄의 방패(우클릭)")]
    [Tooltip("단죄의 방패가 적중했을 때 한 번 터뜨릴 충격파. 프리팹의 'ShieldWave' 를 물린다.\n" +
             "비워두면 연출만 빠진다")]
    [SerializeField] private EffectSocketPlayer interruptWave;

    [Header("나누는 불굴(특성 PAL_2_SHARE_HEAL)")]
    [Tooltip("출발 소용돌이 — 팔라딘 몸을 엔트리 길이만큼 따라간다. 비우면 생략")]
    [SerializeField] private EffectEntry shareHealSwirl;
    [Tooltip("출발 소용돌이를 붙일 자리 기준 오프셋(월드, m)")]
    [SerializeField] private Vector3 shareHealSwirlOffset = Vector3.zero;
    [SerializeField, Min(0.01f)] private float shareHealSwirlScale = 1f;

    [Tooltip("전이 리본 엔트리 — 프리팹 루트에 HealTransferRibbon. 도착 시간은 그 컴포넌트의 travelSeconds")]
    [SerializeField] private EffectEntry shareHealRibbon;

    private static readonly List<GameObject> InstanceBuffer = new List<GameObject>();

    // 소켓 컴포넌트가 대신 들어 주지 않으므로 핸들을 직접 든다. 🔴 드는 쪽이 반드시 반납해야 한다.
    private EffectHandle _targetFloorHandle;

    // 마무리 낙뢰는 원샷이라 스스로 반납된다. 핸들은 채널이 끊겼을 때 걷기 위해서만 든다
    // (피해보다 앞서 쏘므로 — FinalStrikeVfxSchedule). 안 걷으면 그냥 수명대로 끝난다.
    private EffectHandle _targetStrikeHandle;

    // 🔴 표식이 서 있는 자리. 마무리 낙뢰도 여기로 떨어진다.
    // 표식을 깔 때 한 번 뽑고 그 뒤로는 안 바꾼다 — 대상을 **추종하지 않는다**(아래 PlayTargetFloorLocal 참고).
    // 회수(StopTargetFloorLocal)해도 지우지 않는다: 낙뢰 RPC 와 종료 RPC 의 도착 순서를 믿지 않기 위해서다.
    private Vector3 _targetPosition;

    private Player _player;
    private Animator _animator;
    private PlayerStateController _stateController;

    private void Awake() => _player = GetComponent<Player>();

    private void OnEnable() => TraitSkillFx.Raised += HandleTraitFx;

    // 플레이어가 꺼지거나 파괴될 때(디스폰·씬 언로드) 마지막으로 회수한다.
    // 소켓 이펙트는 EffectSocketPlayer.OnDisable 이 알아서 하지만, 표식 핸들은 내가 든 것이라 내가 놓아야 한다.
    private void OnDisable()
    {
        TraitSkillFx.Raised -= HandleTraitFx;
        StopTargetFloorLocal();
    }

    /// <summary>
    /// [오너] 조준 연출을 켠다. 자기 화면은 즉시, 나머지 피어는 서버를 거쳐서.
    /// </summary>
    public void OwnerPlayUltimateVfx(float playRate)
    {
        PlayUltimateLocal(playRate);

        // 오프라인(싱글 테스트·VFXScene)에서는 스폰되지 않아 RPC를 부르면 예외가 난다.
        if (!IsNetworkActive || !IsOwner) return;

        RequestPlayUltimateRpc(playRate);
    }

    /// <summary>
    /// [오너] 조준 연출을 걷는다. 조준을 물렸을 때 — 시전까지 간 경우는
    /// <see cref="StopUltimateVfxLocal"/>이 각 피어에서 처리한다.
    /// </summary>
    public void OwnerStopUltimateVfx()
    {
        StopUltimateVfxLocal();

        if (!IsNetworkActive || !IsOwner) return;

        RequestStopUltimateRpc();
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

    private void PlayUltimateLocal(float playRate)
    {
        if (swordLightning != null)
        {
            swordLightning.SetPlayRate(playRate);
            swordLightning.Play();
        }

        if (swordElectric != null)
        {
            swordElectric.SetPlayRate(playRate);
            swordElectric.Play();
        }
    }

    // ── 오너 → 서버 → 오너를 뺀 전원 ────────────────────────────────
    // 두 홉인 이유: 클라는 다른 클라에게 직접 못 보낸다. 서버가 중계한다.
    // 둘 다 Reliable 이다 — 켜기를 놓치면 남들 화면에만 연출이 빠지고,
    // 끄기를 놓치면 남들 화면에 영영 남고 풀 인스턴스도 안 돌아온다. 스킬당 두 발이라 비용도 무시할 만하다.

    [Rpc(SendTo.Server)] // RequireOwnership 기본값 true — 오너만 부를 수 있다
    private void RequestPlayUltimateRpc(float playRate) => PlayUltimateVfxRpc(playRate);

    [Rpc(SendTo.Server)]
    private void RequestStopUltimateRpc() => StopUltimateVfxRpc();

    // 오너는 이미 로컬로 켜고 껐다. 여기서 다시 보내면 한 번 더 재생된다.
    [Rpc(SendTo.NotOwner)]
    private void PlayUltimateVfxRpc(float playRate) => PlayUltimateLocal(playRate);

    [Rpc(SendTo.NotOwner)]
    private void StopUltimateVfxRpc() => StopUltimateVfxLocal();

    // ── 대상 표식·마무리 낙뢰 (서버 → 전 피어) ───────────────────────

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
    public void ServerPlayTargetFloor(Unit target, float playRate)
    {
        if (target == null) return;

        Vector3 fallback = target.transform.position;
        bool hasRef = target.NetworkObject != null && target.NetworkObject.IsSpawned;

        // 오프라인(싱글 테스트·VFXScene)에서는 스폰되지 않아 RPC를 부르면 예외가 난다.
        if (!IsNetworkActive)
        {
            PlayTargetFloorLocal(target.transform, fallback, playRate);
            return;
        }

        if (!IsServer) return;

        PlayTargetFloorRpc(hasRef ? target.NetworkObject : default, hasRef, fallback, playRate);
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
    private void PlayTargetFloorRpc(NetworkObjectReference targetRef, bool hasRef, Vector3 fallbackPosition,
                                    float playRate)
    {
        // 트랜스폼을 찾아도 추종하지 않는다 — 위치를 한 번 읽는 용도다.
        Transform target = null;
        if (hasRef && targetRef.TryGet(out NetworkObject targetObject))
            target = targetObject.transform;

        PlayTargetFloorLocal(target, fallbackPosition, playRate);
    }

    /// <summary>
    /// [서버] 클립 Custom0 이벤트다(<see cref="FinalStrikeVfxSchedule"/>, 시전당 1회). <b>표식이 서 있는 그 자리</b>에 마무리 낙뢰를 내리꽂는다.
    /// 칼이 떨어져 닿는 데 시간이 걸려 피해 시각보다 앞서 쏜다 — 그 사이 채널이 끊기면 <see cref="StopTargetStrikeLocal"/>이 걷는다.
    ///
    /// <b>좌표를 다시 보내지 않는다.</b> 각 피어는 표식을 깔 때 자리를 이미 고정해 뒀다
    /// (<see cref="_targetPosition"/>). 그걸 그대로 쓰면 표식과 낙뢰가 <b>정의상 같은 자리</b>다 —
    /// 좌표를 새로 실어 보내면 서버가 본 위치와 그 피어가 보간으로 그리고 있는 위치가 달라
    /// 표식 옆에 떨어진다.
    /// </summary>
    public void ServerPlayTargetStrike(float playRate)
    {
        if (!IsNetworkActive)
        {
            PlayTargetStrikeLocal(playRate);
            return;
        }

        if (!IsServer) return;

        PlayTargetStrikeRpc(playRate);
    }

    // 원샷이지만 Reliable 이다. 이 레포의 다른 원샷 연출(피격 파문 등)은 난타 중 한 발이 빠져도 표가 안 나서
    // Unreliable 인데, 이건 궁극기 마무리라 한 번뿐이다 — 유실되면 클라이맥스가 통째로 사라진다.
    [Rpc(SendTo.ClientsAndHost)]
    private void PlayTargetStrikeRpc(float playRate) => PlayTargetStrikeLocal(playRate);

    private void PlayTargetStrikeLocal(float playRate)
    {
        if (targetStrike == null || EffectManager.Instance == null) return;

        // 표식을 깔 때 고정해 둔 그 자리. 표식이 안 움직이므로 낙뢰도 따라 움직이면 안 된다 —
        // 둘이 어긋나면 "표식 옆에 칼이 꽂히는" 그림이 된다.
        // 원샷이라 엔트리 수명 뒤에 매니저가 알아서 반납한다. 핸들은 도중에 걷을 때만 쓴다.
        _targetStrikeHandle = EffectManager.Instance.PlayRecallable(targetStrike, _targetPosition, Quaternion.identity,
            targetStrikeScale, 0f, playRate);
    }

    /// <summary>
    /// [전 피어] 떨어지던 마무리 낙뢰를 즉시 걷는다. 채널이 완주하지 못하고 끝났을 때(피해 없음) 스킬의 <c>OnEnd</c>가 부른다 —
    /// <c>OnEnd</c>는 전 피어에서 도니 RPC 가 필요 없다. 낙뢰 RPC 와 종료 RPC 는 같은 서버에서 순서대로 나가 낙뢰가 먼저 도착한다.
    /// 이미 수명이 끝났거나 쏜 적이 없으면 조용한 no-op 이다.
    /// </summary>
    public void StopTargetStrikeLocal()
    {
        if (!_targetStrikeHandle.IsSet) return;

        if (EffectManager.Instance != null) EffectManager.Instance.ReleaseImmediate(_targetStrikeHandle);
        _targetStrikeHandle = EffectHandle.None;
    }

    private void PlayTargetFloorLocal(Transform target, Vector3 fallbackPosition, float playRate)
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
            targetFloor, _targetPosition, Quaternion.identity, targetFloorScale, 0f, playRate);
    }

    // ═════════════════════════════════════════════════════════════════
    //  단죄의 방패(우클릭) 적중 충격파
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// [서버] 단죄의 방패가 적중했다. 충격파를 전 피어에 한 번 터뜨린다.
    ///
    /// <b>왜 스킬이 직접 못 하나.</b> 적중 판정(<c>FirstMeleeInterruptSkill.ResolveHit</c>)은
    /// <b>서버에서만</b> 돈다 — <c>OnTick</c>은 <c>TickServer</c>가, 애니메이션 이벤트는
    /// <c>PlayerSkillController.HandleAnimationEvent</c>의 <c>if (IsNetworkActive &amp;&amp; !IsServer) return;</c>가
    /// 각각 막는다. 거기서 바로 재생하면 <b>호스트에서만 보인다</b>.
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

    // ═════════════════════════════════════════════════════════════════
    //  수호자의 의지(E) 반격·해제 자세
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// [서버] 수호자의 의지(E) 방어 성공 — 반격 자세 상태로 <b>전 피어(오너 포함)</b>에서 CrossFade 한다.
    /// 방어중 E 재입력 해제(해제 상태 SubSkillGuardEnd, 할 일 94)도 같은 창구를 쓴다.
    ///
    /// <b>왜 서버만 하면 안 되나.</b> 반격 판정·종료는 반격 클립의 Hit·End 이벤트로 정해지는데,
    /// 스킬 이벤트는 서버 애니메이터에서만 처리된다. 플레이어에는 오너 권한 NetworkAnimator 가 있어
    /// 서버만 바꾸면 오너 동기가 상태를 방어 자세로 되돌린다 — 그러면 Hit·End 가 안 온다.
    /// 그래서 시작 상태와 같은 길을 탄다(<c>PlayerSkillController</c>: 서버 PlaySkillPresentation →
    /// PlaySkillClientRpc 의 비서버 피어 CrossFadeInFixedTime).
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

    // ═════════════════════════════════════════════════════════════════
    //  나누는 불굴(PAL_2_SHARE_HEAL) 전이
    // ═════════════════════════════════════════════════════════════════
    //
    // 서버가 초과 회복을 아군에게 넘기면 특성 연출 공용 경로로 TraitFxTrait.PaladinShareHeal Hit 사건이 전 피어에 난다
    // (PlayerTraitEffects.ShareOverflow → Player.TraitSkillFxRpc → TraitSkillFx.Raised). 그래서 여기는 RPC 가 없다.
    // 그 사건 중 시전자가 이 팔라딘인 것만 받아: ① 팔라딘 몸에 출발 소용돌이(따라감) ② 리본(HealTransferRibbon)을 아군에게 날림 →
    // ③ 리본이 닿는 순간 아군의 공용 힐 원샷(PlayerOneShotVfx.Heal).
    // 회복 수치는 서버에서 이미 들어갔다 — 여기는 연출만 늦게 닿는다. 아군이 날아가는 중 쓰러지거나
    // 끊기거나 연출 잠금으로 숨으면 리본은 흐려지고 힐 원샷은 생략한다.

    private void HandleTraitFx(TraitFxEvent e)
    {
        if (e.Trait != TraitFxTrait.PaladinShareHeal || e.Kind != TraitFxKind.Hit) return;
        if (_player == null || e.Caster != _player || e.Target == null) return;
        if (!EffectManager.TryGet(out EffectManager effects, this)) return;

        PlayShareHealSwirl(effects);
        LaunchShareHealRibbon(effects, e.Target);
    }

    private void PlayShareHealSwirl(EffectManager effects)
    {
        if (shareHealSwirl == null) return;

        EffectHandle handle = effects.PlayLooping(shareHealSwirl, _player.transform, shareHealSwirlOffset, shareHealSwirlScale);
        effects.StartCoroutine(ReleaseAfter(handle, shareHealSwirl.ResolvedDuration));
    }

    private void LaunchShareHealRibbon(EffectManager effects, Unit target)
    {
        Transform ally = target.transform;
        if (shareHealRibbon == null)
        {
            // 리본이 없어도 아군 힐은 낸다(도착 지연 없이).
            PlayAllyHeal(target);
            return;
        }

        EffectHandle handle = effects.PlayRecallable(shareHealRibbon, _player.transform.position, Quaternion.identity);
        effects.GetInstances(handle, InstanceBuffer);
        bool launched = false;
        foreach (GameObject instance in InstanceBuffer)
        {
            HealTransferRibbon strands = instance.GetComponentInChildren<HealTransferRibbon>(true);
            if (strands == null) continue;
            strands.Begin(_player.transform, ally, () => IsReceivable(target), () => PlayAllyHeal(target));
            launched = true;
            break;
        }
        InstanceBuffer.Clear();

        if (!launched)
        {
            Edit.LogWarning($"[PaladinSkillView] 리본 엔트리 '{shareHealRibbon.name}' 에 HealTransferRibbon 이 없다 — 아군 힐만 바로 낸다.", this);
            PlayAllyHeal(target);
        }
    }

    // 쓰러짐·연출 숨김이면 도착 연출을 생략한다(서버 전달 조건과 같은 기준).
    private static bool IsReceivable(Unit target)
    {
        if (target == null || !target.isActiveAndEnabled) return false;
        if (target.TryGetComponent(out PlayerLifeCycleController life) && life.State != PlayerLifeState.Alive) return false;
        return !(target.TryGetComponent(out PlayerEncounterLock encounterLock) && encounterLock.IsCinematicHidden);
    }

    private static void PlayAllyHeal(Unit target)
    {
        PlayerOneShotVfxPresenter oneShots = PlayerOneShotVfxPresenter.For(target);
        if (oneShots != null) oneShots.Play(PlayerOneShotVfx.Heal);
    }

    private static IEnumerator ReleaseAfter(EffectHandle handle, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (EffectManager.Instance != null) EffectManager.Instance.Release(handle);
    }
}
