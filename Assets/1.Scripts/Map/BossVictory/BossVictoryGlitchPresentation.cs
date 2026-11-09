using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using VeyTrace.Rendering.Occlusion;

/// <summary>
/// 보스 처치 연출 — ZZZ 글리치(PLAN-boss-victory-glitch) 로컬 표시. <see cref="BossEncounterDirector"/> 가 런타임에 붙인다(모든 피어).
/// 복제 상태 <see cref="BossEncounterDirector.VictoryState"/>(막타·보스·처치 시각)만 읽는다 — 새 RPC·복제 값 없음.
///
/// <para><b>2단계(지금)</b> — 처치 판정 즉시: 이 피어의 스킬 이펙트·장판 표시·투사체 소거(Q11) · 대상별 시간 배율(전역 timeScale 아님):
/// 처치자 1 · 그 외 플레이어 0.05 · 보스 0. 처치자 캐릭터에 처치 연출 스킬(Q9, 칸 = <see cref="PlayerVictoryFinisher"/>)을 표시로만 재생하고,
/// 그 클립 End 이벤트(이 피어 화면 기준, Q12)에서 처치자·보스 0.1 → <see cref="FinisherEnded"/>.</para>
/// <para><b>3단계</b> — <b>처치 판정 순간부터</b>(11-09 사양 변경 — 처치 연출 스킬은 글리치 속에서 재생) 화면 글리치(<see cref="BossVictoryGlitchRender"/>,
/// 세기 = 설정 SO 타임라인 곡선, 시계 <see cref="GlitchTime"/> = 복제 처치 시각 기준) ·
/// 플레이어 흑백·보스 노이즈(렌더링 레이어 비트) · HUD 숨김.</para>
/// <para><b>4단계</b> — 글리치 시계로 카메라 컷(타임라인 '카메라 컷' 트랙, <see cref="BossVictoryCameraCuts"/>): 첫 컷부터 연출 vcam(블렌드 없음) · 컷마다 그 순간 위치로 구도 고정 ·
/// 연출 동안 월드 UI·윤곽선·벽 가림·카메라 흔들림 끔 · 컷별 '벽 숨김'이 켜진 컷에서만 카메라를 향하지 않는 벽 숨김(끈 컷은 다시 보임, 255).</para>
/// <para><b>처치자 순서</b> — 하던 동작(×0.5) → 백대쉬 → 처치 연출 스킬. 하던 동작은 클립 끝 또는 타임라인 '처치자 동작 끝 → 백대쉬' 마커 중 이른 쪽에서 끊는다(255, 마커 0 = 끔).</para>
/// <para><b>타임라인</b>(설정 SO <see cref="BossVictoryTimeline"/> — 트랙별 클립·마커, 창 <c>Tools/Boss Victory/처치 연출 타임라인</c>): 화면 효과·HUD 숨김·정지 유지·TV 꺼짐·로비 전환은
/// 매 프레임 시각 t 의 상태로 맞춘다(되감아도 맞다). <b>마커</b>(기본 0.20 = 첫 찢김 최고점): 처치자가 아닌 피어의 게임 카메라가 처치자로 즉시 컷
/// (<see cref="CameraTargetSwitcher.SetTemporaryFollow"/>) · 처치자 외 플레이어 캐릭터를 이 화면에서 숨김(렌더러·월드 캔버스·머리 위 체력바 끔 —
/// 루트 SetActive 는 NGO·카메라 추적 목록이 깨져 쓰지 않는다). 정리(디버그 랩 F7 포함) 때 원상복구.</para>
/// </summary>
[DefaultExecutionOrder(10001)]   // PlayerAttackSpeedAnimator(10000)가 Update 에서 쓰는 animator.speed 를 그 뒤에 덮는다
[DisallowMultipleComponent]
public sealed class BossVictoryGlitchPresentation : MonoBehaviour
{
    public enum Phase
    {
        None,       // 연출 없음
        Finisher,   // 처치 판정 ~ 처치 연출 스킬 End(처치자 1 · 그 외 0.05 · 보스 0)
        Slow,       // End 이후(처치자 0.1 · 그 외 0.05 · 보스 0.1) — 글리치가 이 순간 시작
    }

    enum Role { Killer, Other, Boss }

    /// <summary>[이 피어] 지금 연출(없으면 null).</summary>
    public static BossVictoryGlitchPresentation Instance { get; private set; }

    /// <summary>[이 피어] 처치자 처치 연출 스킬이 End 에 닿았다 = 처치자·보스 0.1 순간(글리치 시작 신호는 아니다 — 글리치는 처치 판정 순간부터).</summary>
    public static event Action<BossEncounterDirector.VictoryState> FinisherEnded;

    BossEncounterDirector _director;
    BossVictoryGlitchSettings _settings;
    int _sequence = -1;
    bool _running;
    Phase _phase = Phase.None;
    BossEncounterDirector.VictoryState _state;

    Player _killer;
    GameObject _boss;
    PlayerSkillController _killerSkills;
    bool _finisherPending;

    // 처치자 순서(11-09 사용자): 하던 동작 ×0.5 끝까지 → 백대쉬(등장 연출 밀림 재사용, ×1) → 처치 연출 스킬(×1) → End ×0.1.
    enum KillerStep { None, CurrentAction, BackDash, Finisher }
    KillerStep _killerStep;
    Animator _killerAnimator;
    int _killerActionState;
    PlayerMotor _dashMotor;          // 오너 피어만(이동은 오너 권위) — 다른 피어는 애니만, 위치는 복제로 본다
    Vector3 _dashDir;
    double _dashStartAt;
    float _dashDone;
    bool _dashReaction;
    bool _dashIdle;

    /// <summary>[이 피어] 처치자 순서 단계(디버그 표시용).</summary>
    public string KillerStepName => _killerStep.ToString();
    bool _finisherWarned;
    float _finisherEndedAt = -1f;

    /// <summary>[이 피어] 이번 회차 처치 연출 스킬 End 가 온 글리치 시각(아직이면 -1). 타임라인 창 'End 시각' 표시용(255 — 판정엔 안 씀).</summary>
    public float FinisherEndedAt => _running ? _finisherEndedAt : -1f;

    readonly List<GameObject> _others = new List<GameObject>();

    // 흑백 시작 순간(11-09 사용자) — 피어 카메라 처치자로 · 처치자 외 플레이어 숨김
    bool _focusOn;
    bool _othersHidden;
    CameraTargetSwitcher _focusSwitcher;   // 이 연출이 임시 추적을 건 카메라(정리 때 이것만 푼다)
    readonly List<Renderer> _hiddenOtherRenderers = new List<Renderer>();
    readonly List<Behaviour> _hiddenOtherBehaviours = new List<Behaviour>();   // 월드 캔버스·머리 위 체력바
    readonly List<Animator> _animators = new List<Animator>();
    readonly List<Role> _animatorRoles = new List<Role>();
    readonly List<float> _animatorSpeeds = new List<float>();     // 덮기 전 원래 속도
    readonly List<float> _animatorLastSet = new List<float>();    // 이 연출이 마지막으로 쓴 속도(다르면 다른 계통이 바꾼 것)
    readonly List<bool> _animatorOverridden = new List<bool>();
    readonly Dictionary<ParticleSystem, float> _particleSpeeds = new Dictionary<ParticleSystem, float>();

    // 3단계 — 화면 글리치(처치 판정 순간부터)
    bool _glitchOn;
    bool _renderOn;
    float _glitchTime;
    bool _hudHidden;
    CinematicOverlay _overlay;
    readonly List<Renderer> _tagged = new List<Renderer>();

    // 보스 노이즈 UV — 카메라 이동으로만 누적(11-09 사용자)
    Vector2 _noiseOffset;
    Vector3 _noiseCamPos;
    bool _noiseCamValid;
    bool _cutJumped;   // 이번 프레임 컷 전환(텔레포트) — 누적에서 뺀다

    // 6단계 — 유지 → CRT 꺼짐
    BossVictoryCrtOff _crt;
    Coroutine _crtRoutine;
    bool _crtPending;
    bool _finished;

    /// <summary>[이 피어] 연출이 끝났다(타임라인 로비 전환 마커 — 기본 = TV 꺼짐 끝, 화면 검정). 서버(호스트)는 이 순간 로비 결과로 넘긴다.</summary>
    public static event Action<BossEncounterDirector.VictoryState> PresentationFinished;

    /// <summary>[이 피어] 지금 회차 연출이 끝났는가(로비 전환 마커를 지났다).</summary>
    public bool Finished => _running && _finished;

    /// <summary>[이 피어] 지금 표시 중인 회차(<see cref="BossEncounterDirector.VictoryState.Sequence"/>).</summary>
    public int Sequence => _sequence;

    // 4단계 — 카메라 컷
    int _cutIndex = -1;
    bool _cutSuppressed;
    CinemachineExternalImpulseListener _impulseListener;
    bool _impulseWasEnabled;
    readonly List<Canvas> _hiddenWorldCanvases = new List<Canvas>();
    struct WallEntry
    {
        public Renderer Renderer;
        public Vector3 Normal;
        public Vector3 Center;
        public bool Hidden;
    }
    readonly List<WallEntry> _walls = new List<WallEntry>();

    /// <summary>[이 피어] 지금 카메라 컷 번호(타임라인 카메라 컷 트랙 — 게임 카메라면 -1).</summary>
    public int CutIndex => _cutIndex;

    /// <summary>[이 피어] 처치 판정 뒤 초(연출 없으면 -1). 컷·CRT 꺼짐이 같은 시계를 읽는다.</summary>
    public float GlitchTime => _glitchOn ? _glitchTime : -1f;

    public Phase CurrentPhase => _phase;

#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
    /// <summary>[이 피어] 타임라인 창 스크럽 미리보기 — 글리치 시계를 t 로 옮긴다(다음 Update 에 화면 효과·HUD·마커·TV 꺼짐을 t 상태로 맞춘다).
    /// 처치자 순서·애니 배율은 애니 이벤트로 흘러 옮기지 않는다. 연출 중이 아니면 false.</summary>
    public bool PreviewSeek(float t)
    {
        if (!_running || !_glitchOn) return false;
        _glitchTime = Mathf.Max(0f, t);
        return true;
    }

    /// <summary>[이 피어] 타임라인 창에서 컷 값을 고쳤다 — 지금 컷을 다음 Update 에 새 값으로 다시 잡는다(구도·벽 숨김).</summary>
    public void PreviewRecut()
    {
        if (_running && _cutIndex >= 0) _cutIndex = -1;
    }
#endif

    /// <summary>[이 피어] 보스 시간 배율 — 서버(호스트)의 사망 표현 대기도 이 값으로 흐른다(<see cref="BossEncounterDirector.VictoryDeathTimeScaleFor"/>).</summary>
    public float BossTimeScale => _phase switch
    {
        Phase.Finisher => 0f,
        Phase.Slow => _settings != null ? _settings.bossAfterFinisherScale : 0.1f,
        _ => 1f,
    };

    void Awake()
    {
        _director = GetComponent<BossEncounterDirector>();
    }

    void OnEnable() => Instance = this;

    void OnDisable()
    {
        Teardown();
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (_director == null || !_director.IsSpawned) return;
        BossEncounterDirector.VictoryState v = _director.Victory;
        if (!v.Active)
        {
            if (_running) Teardown();   // 실게임에선 씬 끝까지 켜져 있다 — 꺼지는 건 디버그 랩 원상복구뿐
            return;
        }
        if (v.Sequence != _sequence)
        {
            Teardown();
            _sequence = v.Sequence;
            Begin(v);
        }
        if (!_running) return;
        // 하던 동작 → 백대쉬 = 클립 끝 또는 타임라인 '처치자 동작 끝' 마커 중 이른 쪽(255 — 마커 0 = 끔, 클립 끝까지).
        if (_killerStep == KillerStep.CurrentAction &&
            (ReachedClipEnd(_killerAnimator, _killerActionState) ||
             BossVictoryTimeline.ShouldEndKillerAction(_settings.timeline.killerActionEndTime, _glitchTime)))
            BeginBackDash();
        if (_finisherPending) TryStartFinisher();
        TickAnimators();
        if (_glitchOn) TickGlitch();
    }

    // ── 시작 ───────────────────────────────────────────────────────────────

    void Begin(BossEncounterDirector.VictoryState v)
    {
        _settings = BossVictoryGlitchSettings.Load();
        _state = v;
        _running = true;
        _phase = Phase.Finisher;
        _finisherEndedAt = -1f;
        _finisherPending = false;
        _finisherWarned = false;
        _killerStep = KillerStep.None;

        PlayerLifeInputPolicy.SetVictoryInputHeld(true);

        _boss = FindSpawned(v.BossNetworkObjectId);
        _killer = null;
        _others.Clear();
        foreach (Player p in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (p == null) continue;
            if (p.OwnerClientId == v.KillerClientId) _killer = p;
            else _others.Add(p.gameObject);
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
        // 디버그 랩(244) 표시용 팀원 복제본 — Player 가 아니라 위에서 안 잡힌다. 비어 있으면 실게임과 같다.
        foreach (GameObject mate in BossEncounterDirector.DevVictoryExtraTeammates)
            if (mate != null && mate.activeInHierarchy) _others.Add(mate);
#endif
        _killerSkills = _killer != null ? _killer.GetComponent<PlayerSkillController>() : null;

        int cleared = ClearEffectsLocal();

        if (_killer != null) CollectAnimators(_killer.gameObject, Role.Killer);
        for (int i = 0; i < _others.Count; i++) CollectAnimators(_others[i], Role.Other);
        if (_boss != null) CollectAnimators(_boss, Role.Boss);
        StartKillerSequence();
        ApplyEffectRates();

        // 글리치 = 처치 판정 순간부터(복제 처치 시각 기준 — 늦게 받은 피어는 그만큼 앞에서 시작). 이후는 게임 시간(랩 배속·일시정지에 같이 멈춤).
        double serverNow = _director.NetworkManager != null ? _director.NetworkManager.ServerTime.Time : v.StartServerTime;
        StartGlitch(Mathf.Max(0f, v.Elapsed(serverNow)));

        if (_killer == null)
            Edit.LogWarning($"[BossVictory] 막타 client {v.KillerClientId} 의 플레이어가 이 피어에 없다 — 처치 연출 스킬을 못 튼다.", this);
        Edit.Log($"[BossVictory] 글리치 연출 시작 — 회차 {v.Sequence} · 막타 client {v.KillerClientId} · 팀원 {_others.Count} · " +
                 $"Animator {_animators.Count} · 이펙트 소거 {cleared}", this);
    }

    // 처치 판정 즉시 이 피어 표시 소거(Q11). 서버 판정 장판·투사체는 디렉터가 서버에서 디스폰한다(TryStartVictoryServer).
    int ClearEffectsLocal()
    {
        int count = 0;
        if (EffectManager.TryGet(out EffectManager effects, this)) count += effects.ReleaseAllImmediate();
        if (_boss != null && _boss.TryGetComponent(out TwentyThreeBoss twentyThree)) count += twentyThree.StopPresentationEffectsLocal();
        foreach (DefaultAttackProjectile projectile in FindObjectsByType<DefaultAttackProjectile>(FindObjectsSortMode.None))
        {
            if (projectile == null) continue;
            Destroy(projectile.gameObject);
            count++;
        }
        return count;
    }

    // ── 처치자 순서: 하던 동작 ×0.5 → 백대쉬 → 처치 연출 스킬 ─────────────────
    // 단계 전환 = 애니(클립 끝)·등장 연출 밀림의 기존 시간 값(밀림 시간 + Idle 복귀 지연) — 새 타이머 칸 없음. 각 피어가 자기 화면으로 판단(Q12).

    void StartKillerSequence()
    {
        if (_killer == null) return;
        _killerAnimator = _killer.CharacterAnimator;
        if (TryCaptureAction(_killerAnimator, out int state))
        {
            _killerActionState = state;
            _killerStep = KillerStep.CurrentAction;   // 서버는 처치자 동작을 취소하지 않는다(SetCinematicInputLockServer cancelCurrentAction: false)
            return;
        }
        BeginBackDash();   // 아무 동작도 안 하던 중(대기·이동 루프) — 바로 백대쉬
    }

    // 지금(전이 중이면 다음) 상태가 루프 아닌 클립 = 하던 동작. 루프(대기·이동)면 동작 없음.
    static bool TryCaptureAction(Animator a, out int state)
    {
        state = 0;
        if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null) return false;
        AnimatorStateInfo info = a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0) : a.GetCurrentAnimatorStateInfo(0);
        if (info.loop) return false;
        state = info.fullPathHash;
        return state != 0;
    }

    // 처치 순간 클립이 끝났는가 — 다른 상태로 넘어가기 시작했거나, 이미 다른 상태거나, 끝 프레임에 닿았다(옛 연출과 같은 기준).
    static bool ReachedClipEnd(Animator a, int startState)
    {
        if (a == null || startState == 0) return true;
        if (!a.isActiveAndEnabled) return false;
        if (a.IsInTransition(0)) return a.GetNextAnimatorStateInfo(0).fullPathHash != startState;
        AnimatorStateInfo st = a.GetCurrentAnimatorStateInfo(0);
        if (st.fullPathHash != startState) return true;
        return !st.loop && st.normalizedTime >= 0.98f;
    }

    // 백대쉬 = 보스 등장 연출 밀림(BossEncounterDirector.StepSkid — 같은 리액션 상태·거리·시간·이징). 보스 반대쪽으로.
    void BeginBackDash()
    {
        _killerStep = KillerStep.BackDash;
        ApplyEffectRates();
        Vector3 away = _boss != null ? _killer.transform.position - _boss.transform.position : -_killer.transform.forward;
        away.y = 0f;
        _dashDir = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.back;
        _dashStartAt = Time.timeAsDouble;
        _dashDone = 0f;
        _dashReaction = false;
        _dashIdle = false;
        _dashMotor = null;
        if (_killer.IsOwner && _killer.TryGetComponent(out PlayerMotor motor))
        {
            if (_killer.TryGetComponent(out PlayerStateController state) && state.CurrentState == PlayerActionState.Dash) state.EndDash();
            motor.ClearKnockbackVelocity();
            motor.PassThroughUnitsOverride = true;
            _dashMotor = motor;
        }
        Edit.Log($"[BossVictory] 처치자 백대쉬 — {_director.IntroSkidDistance:0.##}m / {_director.IntroSkidDuration:0.##}초{(_dashMotor != null ? "(오너 이동)" : "(애니만 — 위치는 복제)")}", this);
    }

    void FixedUpdate()
    {
        if (!_running || _killerStep != KillerStep.BackDash || _killer == null) return;
        if (!BossEncounterDirector.StepSkid(_killer, _dashMotor, _dashDir, _director.IntroSkidDistance, _dashStartAt, _director.IntroSkidDuration,
                                            Time.timeAsDouble, ref _dashDone, ref _dashReaction, ref _dashIdle))
            return;
        EndDashMotor();
        _killerStep = KillerStep.Finisher;
        _finisherPending = true;
    }

    void EndDashMotor()
    {
        if (_dashMotor != null) _dashMotor.PassThroughUnitsOverride = false;
        _dashMotor = null;
    }

    // ── 처치 연출 스킬(Q9) ─────────────────────────────────────────────────

    void TryStartFinisher()
    {
        if (_killerSkills == null) { _finisherPending = false; return; }
        // 서버의 스킬 취소(연출 잠금)가 이 피어에 닿아 실행 중 스킬이 없어진 뒤에 튼다 — 취소의 자세 복귀가 처치 연출 애니를 덮지 않게.
        if (_killerSkills.ActiveSkill != null) return;

        _finisherPending = false;
        PlayerSkillSlot slot = PlayerVictoryFinisher.ResolveSlot(_killer.gameObject, _settings);
        PlayerSkillBase skill = _killerSkills.GetSkill(slot);
        if (skill == null || skill.Data == null)
        {
            Edit.LogError($"[BossVictory] 처치 연출 스킬이 없다 — {_killer.name} 칸 {slot}. 글리치가 시작되지 않는다.", this);
            return;
        }
        WarnIfNoEndEvent(skill);

        Vector3 dir = _boss != null ? _boss.transform.position - _killer.transform.position : _killer.transform.forward;
        dir.y = 0f;
        _killerSkills.PresentationSkillEnded += OnFinisherEnded;
        if (!_killerSkills.PlayPresentationOnly(skill, dir))
        {
            _killerSkills.PresentationSkillEnded -= OnFinisherEnded;
            Edit.LogError($"[BossVictory] 처치 연출 스킬 재생 실패 — {skill.name}", this);
            return;
        }
        Edit.Log($"[BossVictory] 처치 연출 스킬 — {_killer.name} {slot} {skill.Data.AnimatorStateName}", this);
    }

    // 고른 스킬 클립에 End 이벤트가 없으면 처치자가 영영 안 느려진다 — 대체 경로 없이 오류로 알린다(EditMode 테스트가 미리 잡는다).
    void WarnIfNoEndEvent(PlayerSkillBase skill)
    {
        if (_finisherWarned) return;
        Animator animator = _killer.GetComponentInChildren<Animator>(true);
        RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : null;
        AnimationClip clip = controller != null ? AnimationEventRequirements.FindStateClip(controller, skill.Data.AnimatorStateName) : null;
        if (clip != null && AnimationEventRequirements.HasEvent(clip, nameof(PlayerAnimationEventRelay.HandleSkillEvent), (int)SkillAnimationEventType.End))
            return;
        _finisherWarned = true;
        Edit.LogError($"[BossVictory] 처치 연출 스킬 {skill.name}(상태 {skill.Data.AnimatorStateName}) 클립에 Skill End 이벤트가 없다 — 처치자·보스가 0.1 로 느려지지 않는다.", this);
    }

    void OnFinisherEnded(PlayerSkillBase skill)
    {
        if (_killerSkills != null) _killerSkills.PresentationSkillEnded -= OnFinisherEnded;
        if (!_running || _phase != Phase.Finisher) return;
        _phase = Phase.Slow;
        _finisherEndedAt = _glitchTime;
        ApplyEffectRates();
        Edit.Log($"[BossVictory] 처치 연출 스킬 End — 처치자 ×{_settings.killerAfterFinisherScale} · 보스 ×{_settings.bossAfterFinisherScale} (글리치 t {_glitchTime:0.00})", this);
        FinisherEnded?.Invoke(_state);
    }

    // ── 화면 글리치(3단계) ─────────────────────────────────────────────────

    void StartGlitch(float startTime)
    {
        _glitchOn = true;
        _glitchTime = startTime;
        _noiseOffset = Vector2.zero;
        _noiseCamValid = false;
        _cutJumped = false;
        _renderOn = BossVictoryGlitchRender.Enable(_settings);
        if (!_renderOn)
        {
            Edit.LogWarning($"[BossVictory] 셰이더({BossVictoryGlitchRender.MaskShaderName}·{BossVictoryGlitchRender.CompositeShaderName})가 없어 글리치 없이 돈다.", this);
            return;
        }
        // 흑백 = 플레이어(처치자·팀원, 렌더링 레이어 비트) · 노이즈 = 보스.
        if (_killer != null) BossVictoryGlitchRender.Tag(_killer.gameObject, BossVictoryGlitchRender.PlayerMask, _tagged);
        for (int i = 0; i < _others.Count; i++) BossVictoryGlitchRender.Tag(_others[i], BossVictoryGlitchRender.PlayerMask, _tagged);
        if (_boss != null) BossVictoryGlitchRender.Tag(_boss, BossVictoryGlitchRender.BossMask, _tagged);
    }

    // 글리치 시계 = 게임 시간(랩 배속·일시정지에 같이 멈춘다). 실게임은 timeScale 1.
    // 타임라인(설정 SO 클립·마커)이 정하는 것은 전부 시각 t 의 상태로 맞춘다(되감아도 맞다 — 창 스크럽 미리보기).
    void TickGlitch()
    {
        _glitchTime += Time.deltaTime;
        float t = _glitchTime;
        BossVictoryGlitchSettings s = _settings;
        BossVictoryTimeline tl = s.timeline;
        float et = tl.EffectTime(t);   // 정지 유지 클립 안이면 그 시작에 고정
        TickCuts();
        TickCrtAndLobby(tl, t);

        bool hide = tl.Evaluate(BossVictoryTrack.HudHide, et) > 0.5f;
        if (hide != _hudHidden)
        {
            _overlay ??= new CinematicOverlay("BossVictoryGlitch", null);
            if (hide) _overlay.HideHud();
            else _overlay.RestoreHud();
            _hudHidden = hide;
        }

        if (_renderOn) SetGlitchFrame(s, tl, et);
        TickMarkers(tl, t);   // 노이즈 UV 누적 뒤 — 카메라 컷이 다음 프레임 누적에서 빠지게
    }

    void SetGlitchFrame(BossVictoryGlitchSettings s, BossVictoryTimeline tl, float t)
    {
        var frame = new BossVictoryGlitchRender.Frame
        {
            Time = t,
            Desaturate = tl.Evaluate(BossVictoryTrack.Desaturate, t),
            Amber = tl.Evaluate(BossVictoryTrack.Amber, t),
            Scanline = tl.Evaluate(BossVictoryTrack.Scanline, t),
            Tear = tl.Evaluate(BossVictoryTrack.Tear, t),
            Smear = tl.Evaluate(BossVictoryTrack.Smear, t),
            BossNoise = tl.Evaluate(BossVictoryTrack.BossNoise, t),
        };
        BossScreenRect(out frame.BossScreenCenter, out frame.BossScreenSize);
        frame.NoiseOffset = TickNoiseOffset(s);
        BossVictoryGlitchRender.SetFrame(frame);
    }

    // ── 마커: 피어 카메라 처치자로 · 처치자 외 플레이어 숨김(11-09 사용자) ─────────────────
    // 시각 = 타임라인 마커(기본 0.20 = 첫 찢김 최고점). 늦게 받은 피어는 첫 프레임에 바로. 마커 앞으로 되감으면 되돌린다.

    void TickMarkers(BossVictoryTimeline tl, float t)
    {
        bool focus = t >= tl.focusKillerTime;
        if (focus != _focusOn)
        {
            if (focus) FocusKiller();
            else Unfocus();
            _focusOn = focus;
        }
        bool hide = t >= tl.hideOthersTime;
        if (hide != _othersHidden)
        {
            if (hide) HideOthers();
            else ShowOthers();
            _othersHidden = hide;
        }
    }

    // 처치자 본인 화면은 그대로. 나머지 피어는 즉시 컷(SetTemporaryFollow = PreviousStateIsValid false — 감쇠로 쓸고 가지 않는다).
    void FocusKiller()
    {
        if (_killer == null || _killer.IsOwner) return;
        CameraTargetSwitcher switcher = CameraTargetSwitcher.Active;
        if (switcher == null) return;
        Transform target = FindFollowTarget(_killer.transform);
        switcher.SetTemporaryFollow(target);
        _focusSwitcher = switcher;
        _cutJumped = true;   // 다음 프레임 노이즈 UV 누적에서 이 순간이동을 뺀다
    }

    // 플레이어 vcam 이 평소 따라가는 자식(CameraFollowTarget 태그) — 없으면 루트.
    static Transform FindFollowTarget(Transform root)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t != root && t.CompareTag("CameraFollowTarget")) return t;
        return root;
    }

    // 게임오브젝트는 끄지 않는다 — NetworkObject 루트를 끄면 NGO 동기화·RPC 가 깨지고, CameraFollowTarget 이 비활성이면 카메라 추적 목록에서 빠진다.
    void HideOthers()
    {
        for (int i = 0; i < _others.Count; i++)
        {
            GameObject root = _others[i];
            if (root == null) continue;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;
                r.enabled = false;   // 그림자도 같이 빠진다
                _hiddenOtherRenderers.Add(r);
            }
            foreach (Canvas c in root.GetComponentsInChildren<Canvas>(true))
                HideBehaviour(c);
            foreach (UnitOverheadHealthBar bar in root.GetComponentsInChildren<UnitOverheadHealthBar>(true))
                HideBehaviour(bar);   // 바 본체는 공용 오버레이 캔버스로 옮겨져 있다 — 컴포넌트를 끄면 OnDisable 이 바를 숨긴다
        }
    }

    void HideBehaviour(Behaviour b)
    {
        if (b == null || !b.enabled) return;
        b.enabled = false;
        _hiddenOtherBehaviours.Add(b);
    }

    // 매 프레임 렌더러를 켜는 표시(오너 조준 쿼드 — PlayerAimIndicator.LateUpdate)가 있어 기본 순서 LateUpdate 뒤(10001)에 다시 끈다.
    void LateUpdate()
    {
        for (int i = 0; i < _hiddenOtherRenderers.Count; i++)
        {
            Renderer r = _hiddenOtherRenderers[i];
            if (r != null && r.enabled) r.enabled = false;
        }
    }

    void RestoreMarkers()
    {
        Unfocus();
        ShowOthers();
        _focusOn = false;
        _othersHidden = false;
    }

    void Unfocus()
    {
        if (_focusSwitcher != null) _focusSwitcher.ClearTemporaryFollow();   // 지금 대상(오너·관전 대상)으로 컷 복귀
        _focusSwitcher = null;
    }

    void ShowOthers()
    {
        for (int i = 0; i < _hiddenOtherRenderers.Count; i++)
            if (_hiddenOtherRenderers[i] != null) _hiddenOtherRenderers[i].enabled = true;
        _hiddenOtherRenderers.Clear();
        for (int i = 0; i < _hiddenOtherBehaviours.Count; i++)
            if (_hiddenOtherBehaviours[i] != null) _hiddenOtherBehaviours[i].enabled = true;
        _hiddenOtherBehaviours.Clear();
    }

    // ── 끝: 유지 → CRT 꺼짐(6단계) ─────────────────────────────────────────

    // TV 꺼짐 클립 시작 = 이 프레임 끝 화면을 캡처해 꺼짐 시작, 진행 = 클립 안 위치. 로비 전환 마커를 지나면 연출 끝(호스트 화면이면 서버가 결과로 넘긴다).
    // 시계 = 게임 시간(랩 일시정지에 같이 멈춤). 되감으면 꺼짐을 걷고 끝 표시도 내린다(창 스크럽 미리보기).
    void TickCrtAndLobby(BossVictoryTimeline tl, float t)
    {
        if (tl.TryGetCrtOff(out BossVictoryClip crt) && t >= crt.start)
        {
            if (_crt != null) _crt.SetProgress((t - crt.start) / Mathf.Max(0.01f, crt.length), t - crt.start);
            else if (!_crtPending)
            {
                _crtPending = true;
                _crtRoutine = StartCoroutine(BeginCrtOff());
            }
        }
        else if (_crt != null || _crtPending) DisposeCrt();

        bool end = t >= tl.lobbyTransitionTime;
        if (end == _finished) return;
        _finished = end;
        if (!end) return;
        Edit.Log($"[BossVictory] 연출 끝(로비 전환 마커) — 회차 {_sequence} · t {_glitchTime:0.00}", this);
        PresentationFinished?.Invoke(_state);
    }

    void DisposeCrt()
    {
        if (_crtRoutine != null) { StopCoroutine(_crtRoutine); _crtRoutine = null; }
        _crt?.Dispose();
        _crt = null;
        _crtPending = false;
    }

    System.Collections.IEnumerator BeginCrtOff()
    {
        yield return new WaitForEndOfFrame();
        _crtRoutine = null;
        if (!_running) yield break;
        Shader shader = _settings.crtOffShader != null ? _settings.crtOffShader : Shader.Find(BossVictoryCrtOff.ShaderName);
        if (shader == null)
            Edit.LogWarning($"[BossVictory] 셰이더 {BossVictoryCrtOff.ShaderName} 가 없어 CRT 꺼짐 대신 검정 페이드.", this);
        _crt = new BossVictoryCrtOff("BossVictory", shader, _settings.crtOffGlowColor);
    }

    // ── 카메라 컷(4단계) ───────────────────────────────────────────────────

    // 컷 시계 = 글리치 시계(길이로 자르지 않는다 — 마지막 컷이 끝까지 유지). 구도는 컷이 바뀌는 순간 한 번 계산해 고정한다(흔들림 없음).
    // 컷마다 벽 숨김 켜기/끄기(255): 켠 컷 = 이 컷 카메라에서 가리는 벽을 더 끈다(이어지는 켠 컷끼리 누적 — 255 전과 같음) · 끈 컷 = 숨겼던 벽을 모두 다시 켠다.
    // 되감기(스크럽)로 앞 컷으로 가면 벽을 모두 켠 뒤 그 컷 기준으로 다시 끈다 · 첫 컷 전으로 가면 게임 카메라로 돌아간다.
    void TickCuts()
    {
        BossVictoryTimeline tl = _settings.timeline;
        int index = tl.ActiveCameraCut(_glitchTime);
        if (index == _cutIndex) return;
        if (index < 0)
        {
            RestoreCuts();
            _overlay?.ExitCamera(0f);
            _overlay?.SetDimFocus(null);
            _cutJumped = true;
            return;
        }
        bool rewound = _cutIndex >= 0 && tl.cameraCut[index].start < tl.cameraCut[_cutIndex].start;
        _cutIndex = index;
        _cutJumped = true;
        BossVictoryCameraCuts.Cut cut = tl.CameraCutShot(index);
        BossVictoryCameraCuts.Subjects subjects = CutSubjects();
        BossVictoryCameraCuts.Pose(cut, subjects, out Vector3 pos, out Quaternion rot);

        _overlay ??= new CinematicOverlay("BossVictoryGlitch", null);
        if (!_overlay.IsCameraActive)
        {
            if (!_overlay.EnterCamera(pos, rot, cut.fov, 0f))
                Edit.LogWarning("[BossVictory] CameraTargetSwitcher 가 없어 컷 카메라를 못 켰다.", this);
            SuppressForCuts(subjects);
        }
        else _overlay.PlaceCamera(pos, rot, cut.fov);
        _overlay.SetDimFocus(BossVictoryCameraCuts.Focus(cut, subjects));
        if (!cut.hideWalls || rewound) ShowWalls();
        if (cut.hideWalls) HideWalls(pos, rot * Vector3.forward);
        Edit.Log($"[BossVictory] 컷 {index} {cut.kind}{(cut.hideWalls ? "" : " · 벽 숨김 끔")} — t {_glitchTime:0.00}", this);
    }

    BossVictoryCameraCuts.Subjects CutSubjects()
    {
        var s = new BossVictoryCameraCuts.Subjects();
        s.Killer = _killer != null ? _killer.transform.position : _state.HitPoint;
        s.BossBase = _boss != null ? _boss.transform.position : _state.HitPoint;
        s.BossCenter = s.BossBase + Vector3.up;
        if (_boss != null && TryBossBounds(out Bounds b)) s.BossCenter = b.center;
        return s;
    }

    bool TryBossBounds(out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (Renderer r in _boss.GetComponentsInChildren<Renderer>())
        {
            if (r == null || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    // 컷 카메라에 들어갈 때 한 번 — 머리 위 UI·윤곽선·벽 가림·잔여 흔들림이 컷을 깬다(옛 연출과 같은 목록).
    void SuppressForCuts(BossVictoryCameraCuts.Subjects subjects)
    {
        if (_cutSuppressed) return;
        _cutSuppressed = true;
        PlayerSilhouetteFeature.SuppressedByCinematic = true;
        EnemyOutlineFeature.SuppressedByCinematic = true;
        WallOcclusionDriver.SuppressedByCinematic = true;
        WallTransparencyGroup.SuppressedByCinematic = true;

        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (c == null || !c.enabled || !c.isRootCanvas || c.renderMode != RenderMode.WorldSpace) continue;
            c.enabled = false;
            _hiddenWorldCanvases.Add(c);
        }

        Camera main = Camera.main;
        _impulseListener = main != null ? main.GetComponent<CinemachineExternalImpulseListener>() : null;
        if (_impulseListener != null)
        {
            _impulseWasEnabled = _impulseListener.enabled;
            _impulseListener.enabled = false;
        }

        CollectWalls(Vector3.Lerp(subjects.Killer, subjects.BossBase, 0.5f));
    }

    // 벽 = Wall 레이어·Wall 태그, 또는 이름이 wall 로 시작하는 조상 아래(BossRoomAuthoring.LooksLikeWall 과 같은 기준).
    static bool IsWall(Transform t, int wallLayer)
    {
        for (; t != null; t = t.parent)
        {
            GameObject go = t.gameObject;
            if ((wallLayer >= 0 && go.layer == wallLayer) || go.CompareTag(GameLayers.Wall)) return true;
            string n = t.name;
            if (n.Length >= 4 && n.StartsWith("wall", StringComparison.OrdinalIgnoreCase) &&
                n.IndexOf("walltransparency", StringComparison.OrdinalIgnoreCase) < 0)
                return true;
        }
        return false;
    }

    void CollectWalls(Vector3 interior)
    {
        int wallLayer = LayerMask.NameToLayer(GameLayers.Wall);
        float r2 = _settings.wallSearchRadius * _settings.wallSearchRadius;
        foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r == null || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (_tagged.Contains(r)) continue;   // 인물·보스
            Bounds b = r.bounds;
            if (b.SqrDistance(interior) > r2) continue;
            if (!IsWall(r.transform, wallLayer)) continue;
            Vector3 n = BossVictoryCameraCuts.WallInwardNormal(b, interior);
            if (n == Vector3.zero) continue;
            _walls.Add(new WallEntry { Renderer = r, Normal = n, Center = b.center });
        }
    }

    // 지금 컷 카메라에서 앞면이 확실히 카메라를 향하지 않는 벽(뒷면·옆면)을 끈다 — 다시 켜는 건 벽 숨김 끈 컷·되감기·정리뿐.
    void HideWalls(Vector3 camPos, Vector3 camForward)
    {
        for (int i = 0; i < _walls.Count; i++)
        {
            WallEntry w = _walls[i];
            if (w.Hidden || w.Renderer == null) continue;
            if (!BossVictoryCameraCuts.ShouldHideWall(w.Normal, w.Center, camPos, camForward, _settings.wallFacingThreshold)) continue;
            w.Renderer.enabled = false;
            w.Hidden = true;
            _walls[i] = w;
        }
    }

    void ShowWalls()
    {
        for (int i = 0; i < _walls.Count; i++)
        {
            WallEntry w = _walls[i];
            if (!w.Hidden) continue;
            if (w.Renderer != null) w.Renderer.enabled = true;
            w.Hidden = false;
            _walls[i] = w;
        }
    }

    void RestoreCuts()
    {
        _cutIndex = -1;
        ShowWalls();
        _walls.Clear();
        for (int i = 0; i < _hiddenWorldCanvases.Count; i++)
            if (_hiddenWorldCanvases[i] != null) _hiddenWorldCanvases[i].enabled = true;
        _hiddenWorldCanvases.Clear();
        if (_impulseListener != null) _impulseListener.enabled = _impulseWasEnabled;
        _impulseListener = null;
        if (!_cutSuppressed) return;
        _cutSuppressed = false;
        PlayerSilhouetteFeature.SuppressedByCinematic = false;
        EnemyOutlineFeature.SuppressedByCinematic = false;
        WallOcclusionDriver.SuppressedByCinematic = false;
        WallTransparencyGroup.SuppressedByCinematic = false;
    }

    // 보스 노이즈 UV 오프셋 — 이번 프레임 카메라 위치 변화를 카메라 right/up 축에 투영해 누적(화면 공간·보스 기준 같은 규칙).
    // 오른쪽 → U 감소 · 위 → V 증가. 첫 프레임·컷 전환 프레임(텔레포트)은 기준점만 다시 잡는다.
    Vector2 TickNoiseOffset(BossVictoryGlitchSettings s)
    {
        Camera cam = Camera.main;
        if (cam == null) { _noiseCamValid = false; return _noiseOffset; }
        Transform ct = cam.transform;
        Vector3 pos = ct.position;
        if (_noiseCamValid && !_cutJumped)
        {
            Vector3 delta = pos - _noiseCamPos;
            _noiseOffset.x -= Vector3.Dot(delta, ct.right) * s.bossNoiseCameraScale.x;
            _noiseOffset.y += Vector3.Dot(delta, ct.up) * s.bossNoiseCameraScale.y;
            _noiseOffset.x = Mathf.Repeat(_noiseOffset.x, 1024f);   // 텍스처는 Repeat — 정밀도만 지킨다
            _noiseOffset.y = Mathf.Repeat(_noiseOffset.y, 1024f);
        }
        _noiseCamPos = pos;
        _noiseCamValid = true;
        _cutJumped = false;
        return _noiseOffset;
    }

    // 보스 화면 위치·높이(뷰포트) — 노이즈 "보스 기준" 공간용. 못 구하면 화면 가운데·높이 1.
    void BossScreenRect(out Vector2 center, out float height)
    {
        center = new Vector2(0.5f, 0.5f);
        height = 1f;
        Camera cam = Camera.main;
        if (cam == null || _boss == null) return;
        Bounds b = new Bounds(_boss.transform.position, Vector3.zero);
        foreach (Renderer r in _boss.GetComponentsInChildren<Renderer>())
            if (r != null && !(r is ParticleSystemRenderer)) b.Encapsulate(r.bounds);
        Vector3 top = cam.WorldToViewportPoint(b.center + Vector3.up * b.extents.y);
        Vector3 bottom = cam.WorldToViewportPoint(b.center - Vector3.up * b.extents.y);
        Vector3 mid = cam.WorldToViewportPoint(b.center);
        if (mid.z <= 0f) return;
        center = new Vector2(mid.x, mid.y);
        height = Mathf.Max(0.05f, Mathf.Abs(top.y - bottom.y));
    }

    // ── 시간 배율 ──────────────────────────────────────────────────────────

    void CollectAnimators(GameObject root, Role role)
    {
        foreach (Animator a in root.GetComponentsInChildren<Animator>(true))
        {
            if (a == null || _animators.Contains(a)) continue;
            _animators.Add(a);
            _animatorRoles.Add(role);
            _animatorSpeeds.Add(a.speed > 0f ? a.speed : 1f);
            _animatorLastSet.Add(0f);
            _animatorOverridden.Add(false);
        }
    }

    float Scale(Role role) => role switch
    {
        Role.Killer => _phase == Phase.Slow ? _settings.killerAfterFinisherScale
                     : _killerStep == KillerStep.CurrentAction ? _settings.killerCurrentActionScale
                     : -1f,   // 백대쉬·처치 연출 스킬 동안은 덮지 않는다(1)
        Role.Other => _settings.otherPlayersScale,
        Role.Boss => BossTimeScale,
        _ => -1f,
    };

    // 매 프레임(실행 순서 10001) — 다른 계통(공속 배율 등)이 Update 에서 쓴 값을 그 뒤에 덮는다.
    void TickAnimators()
    {
        for (int i = 0; i < _animators.Count; i++)
        {
            Animator a = _animators[i];
            if (a == null || !a.isActiveAndEnabled) continue;
            float scale = Scale(_animatorRoles[i]);
            if (scale < 0f)
            {
                if (_animatorOverridden[i]) { a.speed = _animatorSpeeds[i]; _animatorOverridden[i] = false; }
                continue;
            }
            // 덮기 전 값, 또는 덮는 동안 다른 계통이 바꾼 값 = 풀 때 돌릴 "원래 값".
            if (a.speed > 0f && (!_animatorOverridden[i] || !Mathf.Approximately(a.speed, _animatorLastSet[i])))
                _animatorSpeeds[i] = a.speed;
            float want = _animatorSpeeds[i] * scale;
            if (!Mathf.Approximately(a.speed, want)) a.speed = want;
            _animatorLastSet[i] = want;
            _animatorOverridden[i] = true;
        }
    }

    // 이펙트 — EffectManager 추종 이펙트 + 대상 몸 아래 파티클. 단계가 바뀔 때만(처치 연출 이펙트는 End 뒤 처치자 배율로).
    void ApplyEffectRates()
    {
        float killer = _phase == Phase.Slow ? _settings.killerAfterFinisherScale
                     : _killerStep == KillerStep.CurrentAction ? _settings.killerCurrentActionScale : 1f;
        if (_killer != null) SetEffectRate(_killer.transform, killer);
        for (int i = 0; i < _others.Count; i++)
            if (_others[i] != null) SetEffectRate(_others[i].transform, _settings.otherPlayersScale);
        if (_boss != null) SetEffectRate(_boss.transform, BossTimeScale);
    }

    void SetEffectRate(Transform root, float rate)
    {
        if (EffectManager.TryGet(out EffectManager effects, this)) effects.SetPlayRateForTarget(root, rate);
        foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == null) continue;
            ParticleSystem.MainModule main = ps.main;
            if (!_particleSpeeds.TryGetValue(ps, out float original))
            {
                original = main.simulationSpeed;
                _particleSpeeds[ps] = original;
            }
            main.simulationSpeed = original * rate;
        }
    }

    // ── 정리 ───────────────────────────────────────────────────────────────

    void Teardown()
    {
        if (!_running) return;
        _running = false;
        _phase = Phase.None;
        _finisherPending = false;
        _killerStep = KillerStep.None;
        EndDashMotor();

        if (_killerSkills != null)
        {
            _killerSkills.PresentationSkillEnded -= OnFinisherEnded;
            if (_killerSkills.PresentationSkill != null) _killerSkills.StopPresentationOnly();
        }

        for (int i = 0; i < _animators.Count; i++)
            if (_animators[i] != null && _animatorOverridden[i]) _animators[i].speed = _animatorSpeeds[i];
        _animators.Clear();
        _animatorRoles.Clear();
        _animatorSpeeds.Clear();
        _animatorLastSet.Clear();
        _animatorOverridden.Clear();

        if (EffectManager.TryGet(out EffectManager effects, this))
        {
            if (_killer != null) effects.SetPlayRateForTarget(_killer.transform, 1f);
            for (int i = 0; i < _others.Count; i++)
                if (_others[i] != null) effects.SetPlayRateForTarget(_others[i].transform, 1f);
            if (_boss != null) effects.SetPlayRateForTarget(_boss.transform, 1f);
        }
        foreach (KeyValuePair<ParticleSystem, float> pair in _particleSpeeds)
        {
            if (pair.Key == null) continue;
            ParticleSystem.MainModule main = pair.Key.main;
            main.simulationSpeed = pair.Value;
        }
        _particleSpeeds.Clear();

        _glitchOn = false;
        BossVictoryGlitchRender.Untag(_tagged);
        if (_renderOn) { BossVictoryGlitchRender.Disable(); _renderOn = false; }
        _hudHidden = false;
        DisposeCrt();
        _finished = false;
        RestoreCuts();
        RestoreMarkers();
        _overlay?.Dispose();   // HUD 복구 · 컷 카메라 나감(블렌드 없음) · 디밍 기준점 복구
        _overlay = null;

        _killer = null;
        _killerSkills = null;
        _boss = null;
        _others.Clear();
        PlayerLifeInputPolicy.SetVictoryInputHeld(false);
    }

    static GameObject FindSpawned(ulong networkObjectId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.SpawnManager == null) return null;
        return nm.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject no) && no != null ? no.gameObject : null;
    }
}
