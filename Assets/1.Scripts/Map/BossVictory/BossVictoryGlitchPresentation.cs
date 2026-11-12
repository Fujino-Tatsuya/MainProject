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
/// 처치자 1 · 그 외 플레이어 0.05 · 보스 0. 처치자 캐릭터에 처치 연출 스킬(Q9, 칸 = 처치자 캐릭터 프로필 <see cref="BossVictoryProfile.finisherSlot"/>)을 표시로만 재생하고,
/// 타임라인 '느려짐 시작' 마커(꺼짐이면 그 클립 End 이벤트 — 이 피어 화면 기준, Q12)에서 처치자 0.1 → <see cref="FinisherEnded"/>.
/// 보스(255 후속 5) = 처치 순간 0 → '보스 피격 애니 시작'(23호 getowned = ParryHit_InPlace, ×1) · '보스 정지'(0) · '보스 슬로우'(꺼짐이면 처치자 느려짐 순간) 중
/// 시각이 가장 늦은 노드(<see cref="BossVictoryBossTimeScale"/>).</para>
/// <para><b>3단계</b> — <b>처치 판정 순간부터</b>(11-09 사양 변경 — 처치 연출 스킬은 글리치 속에서 재생) 화면 글리치(<see cref="BossVictoryGlitchRender"/>,
/// 세기 = 설정 SO 타임라인 곡선, 시계 <see cref="GlitchTime"/> = 복제 처치 시각 기준) ·
/// 플레이어 흑백·보스 노이즈(렌더링 레이어 비트) · HUD 숨김.</para>
/// <para><b>4단계</b> — 글리치 시계로 카메라 컷(타임라인 '카메라 컷' 트랙, <see cref="BossVictoryCameraCuts"/>): 첫 컷부터 연출 vcam(블렌드 없음) · 컷마다 그 순간 위치로 구도 고정 ·
/// 연출 동안 월드 UI·윤곽선·벽 가림·카메라 흔들림 끔 · 컷별 '벽 숨김'이 켜진 컷에서만 카메라를 향하지 않는 벽 숨김(끈 컷은 다시 보임, 255).</para>
/// <para><b>처치자 순서</b>(255 후속 4 — 타임라인 독립 노드, 단계 판정 <see cref="BossVictoryKillerSequence"/>) — 하던 동작(×0.5, '처치자 평타' 클립 끝에서 끊음 — 먼저 끝나면 대기) →
/// '백대쉬 시작' 마커 → '스킬 연출 시작' 마커(백대쉬가 덜 끝났으면 끊고, 이전 스킬을 기다리지 않음) → '느려짐 시작' 마커(꺼짐 = 스킬 End). 앞으로만 — 되감기는 ⏮.</para>
/// <para><b>타임라인</b>(설정 SO <see cref="BossVictoryTimeline"/> — 트랙별 클립·마커, 창 <c>Tools/Boss Victory/처치 연출 타임라인</c>): 화면 효과·HUD 숨김·카메라 컷·TV 꺼짐·로비 전환은
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
        Finisher,   // 처치 판정 ~ 느려짐 시작(처치자 1 · 그 외 0.05 · 보스 0)
        Slow,       // 느려짐 시작 마커(꺼짐이면 처치 연출 스킬 End) 이후(처치자 0.1 · 그 외 0.05). 보스 배율은 보스 노드가 따로 정한다(255 후속 5)
    }

    enum Role { Killer, Other, Boss }

    /// <summary>[이 피어] 지금 연출(없으면 null).</summary>
    public static BossVictoryGlitchPresentation Instance { get; private set; }

    /// <summary>[이 피어] 처치자가 End 후 배율이 된 순간 — '느려짐 시작' 마커(꺼짐이면 처치 연출 스킬 End). 글리치 시작 신호는 아니다(글리치는 처치 판정 순간부터).</summary>
    public static event Action<BossEncounterDirector.VictoryState> FinisherEnded;

    BossEncounterDirector _director;
    BossVictoryGlitchSettings _settings;
    BossVictoryProfile _profile;   // 처치자 캐릭터 프로필(267 — 타임라인·백대쉬·시간 배율·처치 연출 스킬 칸). 회차 시작 때 고정
    TutorialCharacter _killerCharacter;
    int _sequence = -1;
    bool _running;
    Phase _phase = Phase.None;
    BossEncounterDirector.VictoryState _state;

    Player _killer;
    GameObject _boss;
    PlayerSkillController _killerSkills;
    PaladinSkillView _killerSword;   // 처치자 검 번개·검 크기를 켠 팔라딘(269 — 프로필 칸, 정리 때 끈다)

    // 처치자 순서(255 후속 4): 하던 동작 ×0.5 → (대기) → 백대쉬(등장 연출 밀림 재사용, ×1) → 처치 연출 스킬(×1) → 느려짐 ×0.1. 각 단계 시각 = 타임라인 노드.
    BossVictoryKillerSequence.Step _killerStep;
    bool _dashRunning;   // 백대쉬 걸음 중(StepSkid 가 Idle 복귀까지 끝내면 false — 스킬 연출 시작이 먼저 오면 끊음)
    Animator _killerAnimator;
    int _killerActionState;
    PlayerMotor _dashMotor;          // 오너 피어만(이동은 오너 권위) — 다른 피어는 애니만, 위치는 복제로 본다
    Vector3 _dashDir;
    Vector3 _circleCenter;   // 발밑 회색 원 — 연출 시작 순간 처치자 발밑(높이 기준·마커 전 중심, 267 후속)
    System.Func<float, float> _dashEase;   // 설정 SO 백대쉬 곡선(한 번 만들어 재사용)
    double _dashStartAt;
    float _dashDone;
    bool _dashReaction;
    bool _dashIdle;
    bool _dashStarted;    // 이번 회차에 백대쉬를 시작했나(스킬과 같이 가는 프로필 — 스킬 뒤 늦게 시작할 때 한 번만)
    bool _dashMotionOnly; // 백대쉬 이동만(스킬 애니가 몸을 쥠 — 밀림 리액션·끝 Idle 복귀 없음)

    // 처치 연출 스킬 끝 자르기(255 후속 7) — 자른 프레임에 닿으면 처치자 애니 배율 0(BossVictoryAnimTrim).
    int _finisherState;          // 처치 연출 스킬 상태 짧은 이름 해시(재생 시작 뒤에만 0 아님)
    float _finisherEndEvent = -1f;   // 그 클립 End 이벤트 클립 시각(없으면 -1) — 처음 자르기 판정 때 클립에서 읽는다
    bool _killerFrozen;

    /// <summary>[이 피어] 지금 처치자(연출 중이 아니면 null) — 타임라인 창이 실제 처치자의 처치 연출 스킬 클립 길이를 보여 준다.</summary>
    public GameObject KillerObject => _running && _killer != null ? _killer.gameObject : null;

    /// <summary>[이 피어] 처치 연출 중인가 — 조준 표시 숨김 이유(267 후속). 복제된 처치 상태로 각 피어가 연 연출이라 늦은 참가자도 같다. 끝·취소(Teardown)·파괴면 false.</summary>
    public static bool HidesCombatAim => IsRunning;

    /// <summary>[이 피어] 처치 연출 진행 중(시작 ~ 끝·취소·파괴). 표시 전용 연출이 언제 접을지 본다(어쌔신 처치 오라 — 267 후속).</summary>
    public static bool IsRunning => Instance != null && Instance._running;

    /// <summary>[이 피어] 이번 회차 처치자 캐릭터(모르면·나갔으면 None = 기본 프로필) — 타임라인 창이 Play 중 이 캐릭터 프로필을 따라간다(267).</summary>
    public TutorialCharacter KillerCharacter => _running ? _killerCharacter : TutorialCharacter.None;

    /// <summary>[이 피어] 이번 회차가 쓰는 프로필(연출 중이 아니면 null).</summary>
    public BossVictoryProfile Profile => _running ? _profile : null;

    // ── 시계 진단(디버그 씬 — 랩 timelineClockLog) · 클립 노드 실측 표본(에디터 — 창이 클립 시간을 타임라인 초로 그린다) ──
    /// <summary>[디버그] 켜면 노드·키가 발화할 때마다 "타임라인 t / 실제 경과" 한 줄(디버그 랩이 켠다 — 랩 인스펙터 timelineClockLog).</summary>
    public static bool TimelineClockLog;
    double _realStartAt;     // 처치 순간의 실제 시각(realtimeSinceStartup) — 시작 경과만큼 앞으로 당김
    float _clockLogPrevT = -1f;
    string _finisherTimingKey, _bossTimingKey;

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    void ClockLog(string what)
    {
        if (!TimelineClockLog) return;
        Edit.Log($"[BossVictory 시계] {what} — 타임라인 t {_glitchTime:0.000} / 실제 경과 {Time.realtimeSinceStartupAsDouble - _realStartAt:0.000}", this);
    }

    // 효과 세기 키·카메라 컷 키를 이번 프레임에 지났으면 한 줄씩(진단 켜졌을 때만).
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    void ClockLogKeys(float from, float to)
    {
        if (!TimelineClockLog || to <= from) return;
        BossVictoryTimeline tl = _profile.timeline;
        foreach (BossVictoryTrack track in BossVictoryTimeline.Tracks)
        {
            if (!BossVictoryTimeline.HasFades(track)) continue;
            List<BossVictoryClip> clips = tl.Clips(track);
            for (int i = 0; i < clips.Count; i++)
            {
                if (!clips[i].HasKeys) continue;
                for (int k = 0; k < clips[i].keys.Count; k++)
                {
                    float at = clips[i].start + clips[i].keys[k].u * clips[i].length;
                    if (at > from && at <= to) ClockLog($"{BossVictoryTimeline.TrackLabel(track)} #{i + 1} 키 {k + 1}({at:0.000}s, 세기 {clips[i].keys[k].value:0.##})");
                }
            }
        }
        for (int i = 0; i < tl.cameraCut.Count; i++)
        {
            BossVictoryCameraCuts.Cut cut = tl.CameraCutShot(i);
            if (!cut.HasKeys) continue;
            float start = tl.cameraCut[i].start, span = tl.CameraCutEnd(i) - start;
            for (int k = 1; k < cut.keys.Count; k++)
            {
                float at = start + cut.keys[k].u * span;
                if (at > from && at <= to) ClockLog($"카메라 컷 #{i + 1} 키 {k + 1}({at:0.000}s)");
            }
        }
    }

    // [에디터] 클립 노드 실측 표본 — 노드 시작(타임라인 마커 값) 뒤 초 ↔ 클립 시간(BossVictoryClipTiming).
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    void SampleClipTiming()
    {
#if UNITY_EDITOR
        BossVictoryTimeline tl = _profile.timeline;
        if (_finisherState != 0 && SampleState(_killerAnimator, _finisherState, out AnimationClip fc, out float ft))
        {
            if (_finisherTimingKey == null) { _finisherTimingKey = BossVictoryClipTiming.Key(false, fc.name); BossVictoryClipTiming.BeginRun(_finisherTimingKey); }
            BossVictoryClipTiming.Record(_finisherTimingKey, _glitchTime - tl.finisherStartTime, ft);
        }
        if (_bossHitPlayed && _bossPlayedAnimators.Count > 0 && SampleState(_bossPlayedAnimators[0], Animator.StringToHash(_settings.bossHitStateName), out AnimationClip bc, out float bt))
        {
            if (_bossTimingKey == null) { _bossTimingKey = BossVictoryClipTiming.Key(true, bc.name); BossVictoryClipTiming.BeginRun(_bossTimingKey); }
            BossVictoryClipTiming.Record(_bossTimingKey, _glitchTime - tl.bossHitAnimTime, bt);
        }
#endif
    }

    static readonly List<AnimatorClipInfo> s_sampleClips = new List<AnimatorClipInfo>(2);

    static bool SampleState(Animator a, int stateHash, out AnimationClip clip, out float clipTime)
    {
        clip = null;
        clipTime = 0f;
        if (a == null || !a.isActiveAndEnabled || a.IsInTransition(0)) return false;
        AnimatorStateInfo info = a.GetCurrentAnimatorStateInfo(0);
        if (info.shortNameHash != stateHash) return false;
        a.GetCurrentAnimatorClipInfo(0, s_sampleClips);
        if (s_sampleClips.Count == 0 || s_sampleClips[0].clip == null) return false;
        clip = s_sampleClips[0].clip;
        clipTime = Mathf.Min(info.normalizedTime, 1f) * clip.length;
        return true;
    }

    /// <summary>[이 피어] 처치자 순서 단계(디버그 표시용).</summary>
    public string KillerStepName => _killerStep.ToString();
    bool _finisherWarned;
    float _finisherEndedAt = -1f;
    float _killerSlowAt = -1f;   // 처치자 느려짐이 온 글리치 시각 — 보스 슬로우 마커가 꺼졌을 때 보스도 이 순간(255 후속 5)

    // 보스 노드(255 후속 5) — 피격 애니는 한 번(앞으로만), 배율은 매 프레임 BossVictoryBossTimeScale.
    bool _bossHitPlayed;
    float _bossEffectRate = -1f;
    readonly List<Animator> _bossHitAnimators = new List<Animator>();   // 피격 애니를 튼 보스 Animator 중 사망 트리거 파라미터가 있는 것
    readonly List<Animator> _bossPlayedAnimators = new List<Animator>();   // 피격 애니를 튼 보스 Animator 전부(끝 자르기 판정, 255 후속 10)
    float _bossTrimAt = -1f;   // 피격 애니가 자른 끝에 닿은 글리치 시각(아직이면 -1) — 보스 배율 규칙의 한 노드

    /// <summary>[이 피어] 지금 보스(연출 중이 아니면 null) — 타임라인 창이 실제 보스의 피격 애니 클립 길이를 보여 준다.</summary>
    public GameObject BossObject => _running ? _boss : null;
    string _bossDeathTrigger;

    /// <summary>[이 피어] 이번 회차 처치 연출 스킬 End 가 온 글리치 시각(아직이면 -1). 타임라인 창 'End 시각' 표시용(255 — 판정엔 안 씀).</summary>
    public float FinisherEndedAt => _running ? _finisherEndedAt : -1f;

    readonly List<GameObject> _others = new List<GameObject>();

    // 흑백 시작 순간(11-09 사용자) — 피어 카메라 처치자로 · 처치자 외 플레이어 숨김
    bool _focusOn;
    bool _othersHidden;
    CameraTargetSwitcher _focusSwitcher;   // 이 연출이 임시 추적을 건 카메라(정리 때 이것만 푼다)
    Transform _focusProxy, _focusFrom, _focusTo;   // 짧게 블렌드(255 후속 3) — 원래 추적 대상 → 처치자 사이를 시각 t 로 옮기는 대리점
    readonly List<Renderer> _hiddenEffectRenderers = new List<Renderer>();   // 숨김 대상 '팀원 + 붙은 이펙트' — 매 프레임 다시 모음
    readonly List<Renderer> _effectScratch = new List<Renderer>();
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
    BossVictoryCameraCuts.Subjects _cutSubjects;   // 지금 컷이 시작될 때 본 처치자·보스 위치(컷 동안 고정)
    // 컷 블렌드(255 후속 8) — 들어오는 컷 기준 위치는 블렌드 시작 순간 잡아 그 컷 동안 그대로 쓴다(블렌드 끝 = 컷 시작에서 안 튐).
    int _blendIndex = -1;
    BossVictoryCameraCuts.Subjects _blendSubjects;
    int _lastBlendIndex = -1;   // 지난 프레임 블렌드 컷 — 블렌드가 끝나 그 컷이 시작되면 블렌드 때 잡은 기준 위치를 이어 쓴다
    BossVictoryCameraCuts.Subjects _lastBlendSubjects;
    int _shownCut = -1;   // 초점·벽 숨김 기준 컷(블렌드 중 = 들어오는 컷)
    bool _gameCamValid;   // 첫 컷 블렌드 출발 = 컷 카메라를 켜기 직전 게임 카메라 화면(되감아도 같은 값)
    Vector3 _gameCamPos;
    Quaternion _gameCamRot;
    float _gameCamFov;
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
#endif

    /// <summary>[이 피어] 보스 시간 배율 — 서버(호스트)의 사망 표현 대기도 이 값으로 흐른다(<see cref="BossEncounterDirector.VictoryDeathTimeScaleFor"/>).</summary>
    public float BossTimeScale => _running && _profile != null
        ? BossVictoryBossTimeScale.Scale(_profile.timeline.BossNodes(HasBossHitAnim, _bossTrimAt), _killerSlowAt, _profile.bossAfterFinisherScale, _glitchTime)
        : 1f;

    bool HasBossHitAnim => !string.IsNullOrEmpty(_settings.bossHitStateName);

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
        TickKillerSequence();
        TickBoss();
        TickAnimators();
        SampleClipTiming();
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
        _killerSlowAt = -1f;
        _bossHitPlayed = false;
        _bossTrimAt = -1f;
        _bossPlayedAnimators.Clear();
        _bossEffectRate = -1f;
        _bossHitAnimators.Clear();
        _finisherWarned = false;
        _killerStep = BossVictoryKillerSequence.Step.None;
        _dashRunning = false;
        _dashStarted = false;
        _dashMotionOnly = false;
        _finisherState = 0;
        _finisherEndEvent = -1f;
        _killerFrozen = false;
        _finisherTimingKey = _bossTimingKey = null;
        _clockLogPrevT = -1f;

        PlayerLifeInputPolicy.SetVictoryInputHeld(true);
        // 벽 가림(반투명) = 연출 내내 끈다 — 컷 유무·되감기와 무관하게 여기(시작)와 Teardown(정리) 한 곳에서만.
        WallOcclusionDriver.SuppressedByCinematic = true;
        WallTransparencyGroup.SuppressedByCinematic = true;

        _boss = FindSpawned(v.BossNetworkObjectId);
        _killer = null;
        _others.Clear();
        foreach (Player p in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (p == null) continue;
            if (p.OwnerClientId == v.KillerClientId) _killer = p;
            else _others.Add(p.gameObject);
        }
        // 프로필 = 처치자 캐릭터(복제된 KillerClientId → 그 Player 의 프리팹 해시 → 로스터 캐릭터, 새 복제 값 없음 — 늦은 참가자·호스트 로비 전환도 같은 프로필).
        // 처치자를 못 찾으면(나감 등) 기본 프로필(267).
        _killerCharacter = _killer != null ? TutorialLocalPlayerInfo.ResolveCharacter(_killer) : TutorialCharacter.None;
        _profile = _settings.ProfileFor(_killerCharacter);
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
        // 디버그 랩(244) 표시용 팀원 복제본 — Player 가 아니라 위에서 안 잡힌다. 비어 있으면 실게임과 같다.
        foreach (GameObject mate in BossEncounterDirector.DevVictoryExtraTeammates)
            if (mate != null && mate.activeInHierarchy) _others.Add(mate);
#endif
        _killerSkills = _killer != null ? _killer.GetComponent<PlayerSkillController>() : null;
        _circleCenter = _killer != null ? _killer.transform.position : v.HitPoint;   // 발밑 회색 원 — 처치자 발밑(높이 기준 + 마커 전 중심), 시작 순간 고정

        int cleared = ClearEffectsLocal();
        BeginKillerSword();   // 소거 뒤 — 처치자 검 번개·크기(269, 팔라딘만)

        if (_killer != null) CollectAnimators(_killer.gameObject, Role.Killer);
        for (int i = 0; i < _others.Count; i++) CollectAnimators(_others[i], Role.Other);
        if (_boss != null) CollectAnimators(_boss, Role.Boss);
        StartKillerSequence();
        ApplyEffectRates();

        // 글리치 = 처치 판정 순간부터(복제 처치 시각 기준 — 늦게 받은 피어는 그만큼 앞에서 시작). 이후는 게임 시간(랩 배속·일시정지에 같이 멈춤).
        double serverNow = _director.NetworkManager != null ? _director.NetworkManager.ServerTime.Time : v.StartServerTime;
        StartGlitch(Mathf.Max(0f, v.Elapsed(serverNow)));
        _realStartAt = Time.realtimeSinceStartupAsDouble - _glitchTime;
        ClockLog("처치 순간(글리치 시작)");

        if (_killer == null)
            Edit.LogWarning($"[BossVictory] 막타 client {v.KillerClientId} 의 플레이어가 이 피어에 없다 — 처치 연출 스킬을 못 튼다.", this);
        Edit.Log($"[BossVictory] 글리치 연출 시작 — 회차 {v.Sequence} · 막타 client {v.KillerClientId} · 팀원 {_others.Count} · " +
                 $"Animator {_animators.Count} · 이펙트 소거 {cleared} · 프로필 {_profile.name}({_killerCharacter})", this);
    }

    // 처치자 검 번개·검 크기(269) — 프로필 칸이 켜져 있고 처치자가 검을 든 팔라딘(PaladinSkillView)일 때만. 시작 ~ 정리(끝·취소·파괴) 동안.
    // 복제된 처치 상태로 각 피어가 Begin 하므로 새 RPC 없이 모든 피어·늦은 참가자 같다. 다른 캐릭터는 PaladinSkillView 가 없어 무시된다.
    void BeginKillerSword()
    {
        _killerSword = null;
        if (_killer == null || !UsesKillerSword(_profile)) return;
        if (!_killer.TryGetComponent(out PaladinSkillView sword)) return;
        _killerSword = sword;
        sword.BeginVictorySword(_profile.swordLightning, _profile.swordScale);
    }

    void EndKillerSword()
    {
        if (_killerSword != null) _killerSword.EndVictorySword();
        _killerSword = null;
    }

    /// <summary>프로필이 처치자 검 연출(번개 또는 크기 ≠ 1)을 쓰는가(순수 판단, EditMode).</summary>
    public static bool UsesKillerSword(BossVictoryProfile profile) =>
        profile != null && (profile.swordLightning || !Mathf.Approximately(profile.swordScale, 1f));

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

    // ── 처치자 순서: 하던 동작 ×0.5 → 대기 → 백대쉬 → 처치 연출 스킬 → 느려짐(255 후속 4) ─────────────────
    // 단계 시각 = 타임라인 노드(처치자 평타 클립 끝 · 백대쉬 시작 · 스킬 연출 시작 · 느려짐 시작). 각 피어가 자기 글리치 시계로 판단(Q12) — 앞으로만.

    void StartKillerSequence()
    {
        if (_killer == null) return;
        _killerAnimator = _killer.CharacterAnimator;
        if (TryCaptureAction(_killerAnimator, out int state))
        {
            _killerActionState = state;
            _killerStep = BossVictoryKillerSequence.Step.CurrentAction;   // 서버는 처치자 동작을 취소하지 않는다(SetCinematicInputLockServer cancelCurrentAction: false)
            return;
        }
        _killerStep = BossVictoryKillerSequence.Step.Wait;   // 아무 동작도 안 하던 중(대기·이동 루프) — 빈 구간, 백대쉬 시작까지 그대로
    }

    // 매 프레임 — 지금 단계보다 타임라인이 정하는 단계가 뒤면 넘어간다. 하던 동작이 자기 클립 끝에 닿으면 대기(애니는 클립의 Exit 전이 그대로).
    void TickKillerSequence()
    {
        BossVictoryTimeline tl = _profile.timeline;
        if (_killerStep != BossVictoryKillerSequence.Step.None)
        {
            if (_killerStep == BossVictoryKillerSequence.Step.CurrentAction && ReachedClipEnd(_killerAnimator, _killerActionState))
                SetKillerStep(BossVictoryKillerSequence.Step.Wait);
            BossVictoryKillerSequence.Step target = BossVictoryKillerSequence.Target(tl.KillerTimes(), _glitchTime);
            if (target > _killerStep) EnterKillerStep(target);
            // 스킬과 같이 가는 프로필(267 후속) — 스킬이 먼저 시작했어도 백대쉬는 자기 마커 시각에 이동만으로 시작.
            if (BossVictoryKillerSequence.StartDashDuringFinisher(_profile.finisherWithBackDash, _killerStep, _dashStarted, tl.backDashStartTime, _glitchTime))
            {
                ClockLog($"백대쉬 시작(스킬과 같이) {tl.backDashStartTime:0.000}s");
                BeginBackDash(motionOnly: true);
            }
            if (_killerStep == BossVictoryKillerSequence.Step.Finisher && !_killerFrozen) TickFinisherTrim();
        }
        if (_phase == Phase.Finisher && BossVictoryKillerSequence.SlowByMarker(tl.slowStartTime, _glitchTime))
            EnterSlow("느려짐 시작 마커");
    }

    void EnterKillerStep(BossVictoryKillerSequence.Step step)
    {
        BossVictoryKillerSequence.Step from = _killerStep;
        SetKillerStep(step);
        switch (step)
        {
            case BossVictoryKillerSequence.Step.Wait:   // 처치자 평타 클립 끝 — 하던 동작을 끊고 대기 자세
                BossEncounterDirector.CrossFadeIdle(_killerAnimator);
                ClockLog($"처치자 평타 클립 끝 {_profile.timeline.KillerTimes().ActionEnd:0.000}s");
                Edit.Log($"[BossVictory] 처치자 평타 끝 — 하던 동작 끊고 대기 (글리치 t {_glitchTime:0.00})", this);
                break;
            case BossVictoryKillerSequence.Step.BackDash:
                ClockLog($"백대쉬 시작 {_profile.timeline.backDashStartTime:0.000}s");
                BeginBackDash();
                break;
            case BossVictoryKillerSequence.Step.Finisher:
                if (_profile.finisherWithBackDash)
                {
                    // 267 후속 — 백대쉬를 끊지 않고 이동만 이어 간다(스킬 애니가 몸을 쥠). 하던 평타는 스킬 재생 CrossFade 가 끊는다.
                    if (_dashRunning) _dashMotionOnly = true;
                    StartFinisher();
                    Edit.Log($"[BossVictory] 스킬 연출 시작 — 백대쉬와 같이({(_dashRunning ? "이동 이어 감" : "백대쉬는 자기 마커에")}) (글리치 t {_glitchTime:0.00})", this);
                    break;
                }
                if (from == BossVictoryKillerSequence.Step.BackDash && _dashRunning)
                    Edit.Log($"[BossVictory] 스킬 연출 시작 — 백대쉬를 끊는다 (글리치 t {_glitchTime:0.00})", this);
                EndDash();
                StartFinisher();
                break;
        }
    }

    void SetKillerStep(BossVictoryKillerSequence.Step step)
    {
        _killerStep = step;
        ApplyEffectRates();   // 처치자 배율 = 하던 동작 중에만 ×killerCurrentActionScale
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

    // 백대쉬 = 보스 등장 연출 밀림 동작(BossEncounterDirector.StepSkid — 같은 리액션 상태) · 거리·시간·곡선·방향 보정 = 설정 SO 처치 연출 전용 값
    // (기본 = 등장 밀림 값 복사, 255 후속 3). 방향 = 보스 반대쪽을 보정 각만큼 돌림(BossVictoryBackDash.Direction).
    void BeginBackDash(bool motionOnly = false)
    {
        _dashStarted = true;
        _dashMotionOnly = motionOnly;
        Vector3 killerPos = _killer.transform.position;
        _dashDir = BossVictoryBackDash.Direction(killerPos, _boss != null ? _boss.transform.position : killerPos, _killer.transform.forward, _profile.backDashAngle);
        _dashEase ??= u => BossVictoryCameraCuts.Evaluate(_profile.backDashEase, u);
        _dashStartAt = Time.timeAsDouble;
        _dashDone = 0f;
        _dashReaction = false;
        _dashIdle = false;
        _dashMotor = null;
        _dashRunning = true;
        if (_killer.IsOwner && _killer.TryGetComponent(out PlayerMotor motor))
        {
            if (_killer.TryGetComponent(out PlayerStateController state) && state.CurrentState == PlayerActionState.Dash) state.EndDash();
            motor.ClearKnockbackVelocity();
            motor.PassThroughUnitsOverride = true;
            _dashMotor = motor;
        }
        Edit.Log($"[BossVictory] 처치자 백대쉬 — {_profile.backDashDistance:0.##}m / {_profile.backDashDuration:0.##}초 · {_profile.backDashEase} · 보정 {_profile.backDashAngle:0}° · 후 대기 {_profile.backDashIdleDelay:0.##}초{(_dashMotor != null ? "(오너 이동)" : "(애니만 — 위치는 복제)")} (글리치 t {_glitchTime:0.00})", this);
    }

    void FixedUpdate()
    {
        if (!_running || !_dashRunning || _killer == null) return;
        if (!BossEncounterDirector.StepSkid(_killer, _dashMotor, _dashDir, _profile.backDashDistance, _dashStartAt, _profile.backDashDuration,
                                            Time.timeAsDouble, ref _dashDone, ref _dashReaction, ref _dashIdle, _dashEase, _profile.backDashIdleDelay,
                                            animate: !_dashMotionOnly))
            return;
        EndDash();   // Idle 복귀까지 끝 — 스킬 연출 시작 마커까지 대기 자세
    }

    void EndDash()
    {
        _dashRunning = false;
        if (_dashMotor != null) _dashMotor.PassThroughUnitsOverride = false;
        _dashMotor = null;
    }

    // ── 처치 연출 스킬(Q9) ─────────────────────────────────────────────────

    // '스킬 연출 시작' 마커 순간 바로 — 이전 스킬(ActiveSkill)이 남아 있어도 기다리지 않는다(255 후속 4). 그 스킬이 늦게 끝나며 하는
    // 0층 자세 복귀·애니 이벤트는 표시 전용 재생이 쥔 동안 컨트롤러가 막는다(PlayerSkillController.PresentationPoseHeld).
    void StartFinisher()
    {
        if (_killerSkills == null) return;
        PlayerSkillSlot slot = _profile.finisherSlot;
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
        // 대상 = 보스(대상 지정 스킬의 대상 자리 연출 — 팔라딘 R 하늘에서 떨어지는 칼. 판정 없음).
        if (!_killerSkills.PlayPresentationOnly(skill, dir, _boss != null ? _boss.transform : null))
        {
            _killerSkills.PresentationSkillEnded -= OnFinisherEnded;
            Edit.LogError($"[BossVictory] 처치 연출 스킬 재생 실패 — {skill.name}", this);
            return;
        }
        _finisherState = Animator.StringToHash(skill.PresentationEndStateName ?? skill.Data.AnimatorStateName);   // End 를 내는 클립(거너 Q = 발사)
        ClockLog($"처치 연출 스킬 시작 {_profile.timeline.finisherStartTime:0.000}s");
        Edit.Log($"[BossVictory] 처치 연출 스킬 — {_killer.name} {slot} {skill.Data.AnimatorStateName}" +
                 (FinisherTrim > 0f ? $" · 끝 자르기 {FinisherTrim:0.###}초(클립 시간)" : ""), this);
    }

    float FinisherTrim => _profile.timeline.finisherTrimSeconds;

    // 자른 프레임(클립 시간 — 공속과 무관)에 닿으면 그 자세로 멈춤(처치자 배율 0). 그 뒤 애니 이벤트는 안 난다(멈춰서 — 의도).
    // 이미 생긴 이펙트는 지금 규칙(스킬 동안 ×1 · 느려짐 뒤 End 후 배율) 그대로. '느려짐 시작' 이 꺼져 있고 자른 끝이 End 이벤트보다 앞이면 여기서 느려짐.
    void TickFinisherTrim()
    {
        float trim = FinisherTrim;
        if (trim <= 0f || !BossVictoryAnimTrim.TryReachTrim(_killerAnimator, _finisherState, trim, out AnimationClip clip)) return;
        _killerFrozen = true;
        _finisherEndEvent = EndEventTime(clip);
        ClockLog($"처치 연출 스킬 끝 자르기 도달(클립 {trim:0.000}s)");
        int fps = Mathf.RoundToInt(BossVictoryAnimTrim.FrameRate(clip.frameRate));
        Edit.Log($"[BossVictory] 처치 연출 스킬 끝 자르기 — {BossVictoryAnimTrim.FrameLabel(trim, clip.length, fps)}({clip.name}) 에서 멈춤 (글리치 t {_glitchTime:0.00})", this);
        if (_phase == Phase.Finisher && BossVictoryAnimTrim.SlowAtTrimEnd(_profile.timeline.slowStartTime, trim, _finisherEndEvent))
            EnterSlow("처치 연출 스킬 클립 끝(잘림 — End 이벤트 전)");
    }

    static float EndEventTime(AnimationClip clip)
    {
        foreach (AnimationEvent ev in clip.events)
            if (ev.functionName == nameof(PlayerAnimationEventRelay.HandleSkillEvent) && ev.intParameter == (int)SkillAnimationEventType.End)
                return ev.time;
        return -1f;
    }

    // 고른 스킬 클립에 End 이벤트가 없으면 처치자가 영영 안 느려진다 — 대체 경로 없이 오류로 알린다(EditMode 테스트가 미리 잡는다).
    void WarnIfNoEndEvent(PlayerSkillBase skill)
    {
        if (_finisherWarned) return;
        Animator animator = _killer.GetComponentInChildren<Animator>(true);
        RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : null;
        AnimationClip clip = controller != null ? AnimationEventRequirements.FindStateClip(controller, skill.PresentationEndStateName) : null;
        if (clip != null && AnimationEventRequirements.HasEvent(clip, nameof(PlayerAnimationEventRelay.HandleSkillEvent), (int)SkillAnimationEventType.End))
            return;
        _finisherWarned = true;
        Edit.LogError($"[BossVictory] 처치 연출 스킬 {skill.name}(End 상태 {skill.PresentationEndStateName}) 클립에 Skill End 이벤트가 없다 — 처치자·보스가 0.1 로 느려지지 않는다.", this);
    }

    void OnFinisherEnded(PlayerSkillBase skill)
    {
        if (_killerSkills != null) _killerSkills.PresentationSkillEnded -= OnFinisherEnded;
        if (!_running) return;
        _finisherEndedAt = _glitchTime;
        ClockLog("처치 연출 스킬 End 이벤트");
        if (_profile.timeline.slowStartTime > 0f)
        {
            Edit.Log($"[BossVictory] 처치 연출 스킬 End (글리치 t {_glitchTime:0.00}) — 느려짐은 '느려짐 시작' 마커 {_profile.timeline.slowStartTime:0.00}s", this);
            return;
        }
        EnterSlow("처치 연출 스킬 End");
    }

    // 처치자 ×killerAfterFinisherScale — 느려짐 시작 마커(켜짐) 또는 스킬 End(마커 꺼짐) 한 번. 보스 슬로우 마커가 꺼졌으면 보스도 이 순간(TickBoss).
    void EnterSlow(string reason)
    {
        if (_phase != Phase.Finisher) return;
        _phase = Phase.Slow;
        _killerSlowAt = _glitchTime;
        ClockLog($"처치자 느려짐 — {reason}");
        ApplyEffectRates();
        Edit.Log($"[BossVictory] {reason} — 처치자 ×{_profile.killerAfterFinisherScale}{(_profile.timeline.bossSlowTime > 0f ? "" : $" · 보스 ×{_profile.bossAfterFinisherScale}(보스 슬로우 끔)")} (글리치 t {_glitchTime:0.00})", this);
        FinisherEnded?.Invoke(_state);
    }

    // ── 보스 노드(255 후속 5): 피격 애니 시작 · 정지 · 슬로우 ─────────────────
    // 배율 = BossVictoryBossTimeScale(시각이 가장 늦은 노드) — 애니(TickAnimators)·이펙트(여기)·서버 사망 표현 대기(BossTimeScale)가 같은 값.

    void TickBoss()
    {
        if (_boss == null) return;
        if (!_bossHitPlayed && BossVictoryBossTimeScale.ShouldPlayHitAnim(_profile.timeline.BossNodes(HasBossHitAnim), _glitchTime))
            PlayBossHitAnim();
        if (_bossHitPlayed && _bossTrimAt < 0f) TickBossHitTrim();
        // 처치 순간 서버 사망 상태가 건 사망 트리거(AnyState→Death)가 피격 애니를 빼앗지 않게 — 사망 상태 복제가 늦게 와도 매 프레임 비운다.
        for (int i = 0; i < _bossHitAnimators.Count; i++)
            if (_bossHitAnimators[i] != null) _bossHitAnimators[i].ResetTrigger(_bossDeathTrigger);
        float rate = BossTimeScale;
        if (Mathf.Approximately(rate, _bossEffectRate)) return;
        ClockLog($"보스 배율 ×{rate:0.##}");
        _bossEffectRate = rate;
        SetEffectRate(_boss.transform, rate);
    }

    // 설정 SO 상태(23호 getowned = Boss_23_ParryHit_InPlace, 루트모션 없음)를 처음부터 — 그 상태가 있는 보스 Animator 에만.
    void PlayBossHitAnim()
    {
        _bossHitPlayed = true;
        string state = _settings.bossHitStateName;
        ClockLog($"보스 피격 애니 시작 {_profile.timeline.bossHitAnimTime:0.000}s '{state}'");
        int hash = Animator.StringToHash(state);
        _bossDeathTrigger = _boss.TryGetComponent(out MonsterBase monster) && monster.MonsterData != null ? monster.MonsterData.deathTrigger : null;
        int played = 0;
        for (int i = 0; i < _animators.Count; i++)
        {
            Animator a = _animators[i];
            if (_animatorRoles[i] != Role.Boss || a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null || !a.HasState(0, hash)) continue;
            if (_settings.bossHitFadeSeconds > 0f) a.CrossFadeInFixedTime(hash, _settings.bossHitFadeSeconds, 0, 0f);
            else a.Play(hash, 0, 0f);
            played++;
            _bossPlayedAnimators.Add(a);
            if (!string.IsNullOrEmpty(_bossDeathTrigger) && HasTrigger(a, _bossDeathTrigger)) _bossHitAnimators.Add(a);
        }
        if (played == 0)
            Edit.LogWarning($"[BossVictory] 보스 피격 애니 상태 '{state}' 가 보스 Animator 에 없다 — 마지막 자세 그대로(설정 SO bossHitStateName).", this);
        else
            Edit.Log($"[BossVictory] 보스 피격 애니 '{state}' (글리치 t {_glitchTime:0.00})", this);
    }

    // 피격 애니 끝 자르기(255 후속 10) — 자른 프레임(클립 시간)에 닿은 순간을 보스 배율 노드로 남긴다(배율 0, 뒤 슬로우 노드가 오면 다시 움직임). 한 번만.
    void TickBossHitTrim()
    {
        float trim = _profile.timeline.bossHitTrimSeconds;
        if (trim <= 0f) return;
        int hash = Animator.StringToHash(_settings.bossHitStateName);
        for (int i = 0; i < _bossPlayedAnimators.Count; i++)
        {
            if (!BossVictoryAnimTrim.TryReachTrim(_bossPlayedAnimators[i], hash, trim, out AnimationClip clip)) continue;
            _bossTrimAt = _glitchTime;
            ClockLog($"보스 피격 애니 끝 자르기 도달(클립 {trim:0.000}s)");
            Edit.Log($"[BossVictory] 보스 피격 애니 끝 자르기 — {BossVictoryAnimTrim.FrameLabel(trim, clip.length, clip.frameRate)}({clip.name}) 에서 정지 (글리치 t {_glitchTime:0.00})", this);
            return;
        }
    }

    static bool HasTrigger(Animator a, string name)
    {
        foreach (AnimatorControllerParameter p in a.parameters)
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == name) return true;
        return false;
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
        float before = _glitchTime;
        _glitchTime += Time.deltaTime;
        float t = _glitchTime;
        ClockLogKeys(before, t);
        BossVictoryGlitchSettings s = _settings;
        BossVictoryTimeline tl = _profile.timeline;
        TickCuts();
        TickCrtAndLobby(tl, t);

        bool hide = tl.Evaluate(BossVictoryTrack.HudHide, t) > 0.5f;
        if (hide != _hudHidden)
        {
            _overlay ??= new CinematicOverlay("BossVictoryGlitch", null);
            if (hide) _overlay.HideHud();
            else _overlay.RestoreHud();
            _hudHidden = hide;
        }

        if (_renderOn) SetGlitchFrame(s, tl, t);
        ApplyCameraBob(tl.CameraBobOffset(t));
        TickMarkers(tl, t);   // 노이즈 UV 누적 뒤 — 카메라 컷이 다음 프레임 누적에서 빠지게
    }

    // 카메라 두둥실(267 후속) — 투영 행렬 세로 이동(화면 높이 %). 카메라 위치·회전은 안 건드려 컷·추적과 겹치지 않고 누적도 없다.
    // 시각 t 만으로 정해지는 값(스크럽 OK·모든 피어 같은 화면). 0 이면 원래 투영으로.
    Camera _bobCam;
    void ApplyCameraBob(float percent)
    {
        Camera cam = Camera.main;
        if (_bobCam != null && (_bobCam != cam || Mathf.Abs(percent) < 1e-5f)) { _bobCam.ResetProjectionMatrix(); _bobCam = null; }
        if (cam == null || Mathf.Abs(percent) < 1e-5f) return;
        cam.ResetProjectionMatrix();
        Matrix4x4 m = cam.projectionMatrix;
        m.m12 -= percent * 0.02f;   // NDC 세로 2 = 화면 높이 100% · + = 화면 내용이 위로
        cam.projectionMatrix = m;
        _bobCam = cam;
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
        // 발밑 회색 원(246 복원) — 반경 = 설정 반경 × 클립 세기, 중심 = 카메라 바로 아래 바닥(매 프레임, 높이 = 처치자 발). '카메라 처치자로' 마커 전엔 처치자 발밑(피어마다 카메라가 다른 구간).
        Camera circleCam = Camera.main;
        frame.Rectify = tl.Evaluate(BossVictoryTrack.Rectify, t);
        frame.RectifyFill = tl.TryActiveClip(BossVictoryTrack.RectifyFill, t, out BossVictoryClip fillClip) ? fillClip.Evaluate(t) : 0f;
        frame.RectifyFillElapsed = frame.RectifyFill > 0f ? t - fillClip.start : -1f;
        frame.GroundCircleCenter = BossVictoryTimeline.GroundCircleCenter(t, tl.focusKillerTime, _circleCenter,
            circleCam != null, circleCam != null ? circleCam.transform.position : Vector3.zero);
        frame.GroundCircleRadius = s.groundCircleRadius * tl.Evaluate(BossVictoryTrack.GroundCircle, t);
        frame.NoiseOffset = TickNoiseOffset(s) + new Vector2(0f, tl.NoiseScrollOffset(t));   // 카메라 이동 누적 + 타임라인 '노이즈 UV 수직 이동'(267 후속 — 시각 t 상태, 스크럽 OK)
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
        if (_focusProxy != null && _focusFrom != null && _focusTo != null)
        {
            float u = (t - tl.focusKillerTime) / Mathf.Max(0.0001f, _settings.focusKillerBlendSeconds);
            _focusProxy.position = Vector3.Lerp(_focusFrom.position, _focusTo.position, BossVictoryCameraCuts.Evaluate(BossVictoryCameraCuts.Ease.EaseInOut, u));
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
        _focusSwitcher = switcher;
        Transform from = switcher.CurrentFollowTarget;
        if (_settings.focusKillerBlendSeconds > 0f && from != null)
        {
            // 짧게 블렌드 — 대리점을 원래 대상 자리에 두고 따라가게 한 뒤(점프 없음) TickMarkers 가 시각 t 로 처치자 쪽에 옮긴다.
            _focusProxy = new GameObject("BossVictoryFocusProxy").transform;
            _focusProxy.position = from.position;
            _focusFrom = from;
            _focusTo = target;
            switcher.SetTemporaryFollow(_focusProxy);
            return;
        }
        switcher.SetTemporaryFollow(target);   // 즉시 컷(기본)
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
        if (_othersHidden && _settings != null && _settings.hideOthersTarget == BossVictoryGlitchSettings.HideOthersTarget.TeammatesAndAttachedEffects)
            HideAttachedEffects();
    }

    // 팀원에게 붙어 따라다니는 풀 이펙트(EffectManager 추종) — 풀 인스턴스는 반납·재대출되므로 매 프레임 지금 붙어 있는 것만 끄고,
    // 더 이상 팀원을 따라가지 않는 것은 다시 켠다(다른 대상에 재대출된 인스턴스가 꺼진 채 남지 않게). 팀원 몸 아래 소켓 이펙트는 HideOthers 렌더러에 이미 포함.
    void HideAttachedEffects()
    {
        _effectScratch.Clear();
        if (EffectManager.TryGet(out EffectManager effects, this))
            for (int i = 0; i < _others.Count; i++)
                if (_others[i] != null) effects.CollectFollowingRenderers(_others[i].transform, _effectScratch);
        for (int i = _hiddenEffectRenderers.Count - 1; i >= 0; i--)
        {
            Renderer r = _hiddenEffectRenderers[i];
            if (r != null && _effectScratch.Contains(r)) continue;
            if (r != null) r.enabled = true;
            _hiddenEffectRenderers.RemoveAt(i);
        }
        for (int i = 0; i < _effectScratch.Count; i++)
        {
            Renderer r = _effectScratch[i];
            if (r == null || !r.enabled) continue;
            r.enabled = false;
            _hiddenEffectRenderers.Add(r);
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
        if (_focusProxy != null) Destroy(_focusProxy.gameObject);
        _focusProxy = _focusFrom = _focusTo = null;
    }

    void ShowOthers()
    {
        for (int i = 0; i < _hiddenOtherRenderers.Count; i++)
            if (_hiddenOtherRenderers[i] != null) _hiddenOtherRenderers[i].enabled = true;
        _hiddenOtherRenderers.Clear();
        for (int i = 0; i < _hiddenOtherBehaviours.Count; i++)
            if (_hiddenOtherBehaviours[i] != null) _hiddenOtherBehaviours[i].enabled = true;
        _hiddenOtherBehaviours.Clear();
        for (int i = 0; i < _hiddenEffectRenderers.Count; i++)
            if (_hiddenEffectRenderers[i] != null) _hiddenEffectRenderers[i].enabled = true;
        _hiddenEffectRenderers.Clear();
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
                ClockLog($"TV 꺼짐 시작 {crt.start:0.000}s");
                _crtRoutine = StartCoroutine(BeginCrtOff());
            }
        }
        else if (_crt != null || _crtPending) DisposeCrt();

        bool end = t >= tl.lobbyTransitionTime;
        if (end == _finished) return;
        _finished = end;
        if (!end) return;
        ClockLog($"로비 전환 {tl.lobbyTransitionTime:0.000}s");
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

    // 컷 시계 = 글리치 시계. 컷 = 클립 시작 ~ 다음 컷 시작(마지막 = 로비 전환). 기준 위치(처치자·보스)는 컷이 바뀌는 순간 한 번 잡아 컷 동안 고정하고,
    // 화면은 매 프레임 시각 t 의 진행으로 시작 → 끝 화면 사이를 곡선대로 보간한다(255 후속 2 — 끝 화면 없으면 고정, 되감아도 t 그대로).
    // 컷마다 벽 숨김 켜기/끄기(255): 켠 컷 = 지금 카메라 위치에서 가리는 벽을 더 끈다(이동 중이면 매 프레임, 이어지는 켠 컷끼리 누적) · 끈 컷 = 숨겼던 벽을 모두 다시 켠다.
    // 되감기(스크럽)로 앞 컷으로 가면 벽을 모두 켠 뒤 그 컷 기준으로 다시 끈다 · 첫 컷 전으로 가면 게임 카메라로 돌아간다.
    // 컷 블렌드(255 후속 8): 들어오는 컷 시작 전 [시작 − 블렌드, 시작) 동안 지금 컷 화면(계속 진행 중 — 첫 컷이면 게임 카메라 그 순간 화면)
    // → 들어오는 컷 첫 화면으로 월드 기준 섞음. 시각 t 의 순수 결과(되감기 정확). 블렌드 중 벽 숨김 = 들어오는 컷 설정, 컷 진입 처리 = 블렌드 시작 순간.
    void TickCuts()
    {
        BossVictoryTimeline tl = _profile.timeline;
        int index = tl.ActiveCameraCut(_glitchTime);
        int incoming = tl.BlendingCameraCut(_glitchTime, out float blend);
        if (index < 0 && incoming < 0)
        {
            _blendIndex = -1;
            _gameCamValid = false;   // 게임 카메라로 돌아감 — 다음 첫 컷 블렌드는 그때 화면에서
            if (_cutIndex < 0 && (_overlay == null || !_overlay.IsCameraActive)) return;
            RestoreCuts();
            _overlay?.ExitCamera(0f);
            _overlay?.SetDimFocus(null);
            _cutJumped = true;
            return;
        }
        if (incoming >= 0 && incoming != _blendIndex)
        {
            _blendIndex = incoming;
            _blendSubjects = CutSubjects();
        }
        else if (incoming < 0) _blendIndex = -1;

        _overlay ??= new CinematicOverlay("BossVictoryGlitch", null);
        if (index < 0 && !_gameCamValid)
        {
            Camera main = Camera.main;
            _gameCamValid = main != null;
            if (main != null)
            {
                _gameCamPos = main.transform.position;
                _gameCamRot = main.transform.rotation;
                _gameCamFov = main.fieldOfView;
            }
        }

        bool changed = index != _cutIndex;
        bool rewound = changed && _cutIndex >= 0 && (index < 0 || tl.cameraCut[index].start < tl.cameraCut[_cutIndex].start);
        if (changed)
        {
            bool fromBlend = index >= 0 && _blendIndex < 0 && _lastBlendIndex == index;
            _cutIndex = index;
            _cutJumped = !fromBlend;   // 블렌드 끝에서 이어지면 텔레포트가 아니다
            if (index >= 0) _cutSubjects = fromBlend ? _lastBlendSubjects : CutSubjects();
        }
        _lastBlendIndex = _blendIndex;
        _lastBlendSubjects = _blendSubjects;

        Vector3 pos;
        Quaternion rot;
        float fov;
        if (index >= 0)
            BossVictoryCameraCuts.Pose(tl.CameraCutShot(index), _cutSubjects, tl.CameraCutProgress(index, _glitchTime), out pos, out rot, out fov);
        else
        {
            pos = _gameCamPos;
            rot = _gameCamValid ? _gameCamRot : Quaternion.identity;
            fov = _gameCamValid ? _gameCamFov : 40f;
        }
        if (incoming >= 0)
        {
            BossVictoryCameraCuts.Pose(tl.CameraCutShot(incoming), _blendSubjects, 0f, out Vector3 toPos, out Quaternion toRot, out float toFov);
            BossVictoryCameraCuts.Blend(pos, rot, fov, toPos, toRot, toFov, blend, out pos, out rot, out fov);
        }
        // 벽 숨김·초점 = 들어오는 컷(블렌드 중) 또는 지금 컷
        int shown = incoming >= 0 ? incoming : index;
        BossVictoryCameraCuts.Cut cut = tl.CameraCutShot(shown);
        BossVictoryCameraCuts.Subjects shownSubjects = incoming >= 0 ? _blendSubjects : _cutSubjects;

        if (!_overlay.IsCameraActive)
        {
            if (!_overlay.EnterCamera(pos, rot, fov, 0f))
                Edit.LogWarning("[BossVictory] CameraTargetSwitcher 가 없어 컷 카메라를 못 켰다.", this);
            SuppressForCuts(shownSubjects);   // 컷 진입 처리 = 블렌드 시작 순간(첫 컷 블렌드면 그때)
        }
        else _overlay.PlaceCamera(pos, rot, fov);   // 매 프레임 — 이동 컷·블렌드·창에서 고친 값이 바로 반영
        bool shownChanged = shown != _shownCut;
        _shownCut = shown;
        if (shownChanged) _overlay.SetDimFocus(BossVictoryCameraCuts.AnchorPoint(cut.anchor, shownSubjects));
        if (!cut.hideWalls || rewound) ShowWalls();
        if (cut.hideWalls) HideWalls(pos, rot * Vector3.forward);
        if (shownChanged && incoming >= 0) ClockLog($"카메라 컷 #{incoming + 1} 블렌드 시작");
        if (changed && index >= 0) ClockLog($"카메라 컷 #{index + 1} 시작 {tl.cameraCut[index].start:0.000}s");
        if (shownChanged && incoming >= 0)
            Edit.Log($"[BossVictory] 컷 {incoming} 블렌드 시작 {tl.CameraCutBlendSeconds(incoming):0.##}초 {tl.CameraCutShot(incoming).blendCurve} — {(index >= 0 ? $"컷 {index}" : "게임 카메라")}에서 (t {_glitchTime:0.00})", this);
        if (changed && index >= 0)
        {
            BossVictoryCameraCuts.Cut ac = tl.CameraCutShot(index);
            Edit.Log($"[BossVictory] 컷 {index} 기준 {ac.anchor}{(ac.Moves ? $" · 이동 키 {(ac.HasKeys ? ac.keys.Count : 2)}개{(ac.smoothPath ? " 곡선" : "")}" : " · 고정")}{(ac.hideWalls ? "" : " · 벽 숨김 끔")} — t {_glitchTime:0.00}", this);
        }
    }

    /// <summary>[이 피어] 컷 화면 저장 기준 위치 — 컷이 켜져 있으면 그 컷이 잡은 위치, 아니면 지금 위치. 연출 중이 아니면 false(타임라인 창 'Scene 뷰로' 버튼).</summary>
    public bool TryGetCutSubjects(out BossVictoryCameraCuts.Subjects subjects)
    {
        subjects = _cutIndex >= 0 ? _cutSubjects : CutSubjects();
        return _running;
    }

    BossVictoryCameraCuts.Subjects CutSubjects()
    {
        var s = new BossVictoryCameraCuts.Subjects();
        s.Killer = _killer != null ? _killer.transform.position : _state.HitPoint;
        s.BossBase = _boss != null ? _boss.transform.position : _state.HitPoint;
        return s;
    }

    // 컷 카메라에 들어갈 때 한 번 — 머리 위 UI·윤곽선·잔여 흔들림이 컷을 깬다(옛 연출과 같은 목록). 벽 가림은 컷과 무관하게 Begin~Teardown.
    void SuppressForCuts(BossVictoryCameraCuts.Subjects subjects)
    {
        if (_cutSuppressed) return;
        _cutSuppressed = true;
        PlayerSilhouetteFeature.SuppressedByCinematic = true;
        EnemyOutlineFeature.SuppressedByCinematic = true;

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
        _shownCut = -1;
        _blendIndex = _lastBlendIndex = -1;
        _gameCamValid = false;
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
        Role.Killer => _killerFrozen ? 0f   // 처치 연출 스킬 끝 자르기(255 후속 7) — 그 프레임에 멈춤
                     : _phase == Phase.Slow ? _profile.killerAfterFinisherScale
                     : _killerStep == BossVictoryKillerSequence.Step.CurrentAction ? _profile.killerCurrentActionScale
                     : -1f,   // 백대쉬·처치 연출 스킬 동안은 덮지 않는다(1)
        Role.Other => _profile.otherPlayersScale,
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
        float killer = _phase == Phase.Slow ? _profile.killerAfterFinisherScale
                     : _killerStep == BossVictoryKillerSequence.Step.CurrentAction ? _profile.killerCurrentActionScale : 1f;
        if (_killer != null) SetEffectRate(_killer.transform, killer);
        for (int i = 0; i < _others.Count; i++)
            if (_others[i] != null) SetEffectRate(_others[i].transform, _profile.otherPlayersScale);
        // 보스 이펙트 배율은 TickBoss(보스 노드가 시각으로 바꾼다).
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
        _killerStep = BossVictoryKillerSequence.Step.None;
        _killerFrozen = false;
        _bossHitAnimators.Clear();
        _bossPlayedAnimators.Clear();
        EndDash();
        EndKillerSword();

        if (_killerSkills != null)
        {
            _killerSkills.PresentationSkillEnded -= OnFinisherEnded;
            _killerSkills.StopPresentationOnly();   // End 뒤에도 — 표시 전용 재생이 쥔 0층 자세를 놓는다
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
        ApplyCameraBob(0f);
        RestoreCuts();
        RestoreMarkers();
        _overlay?.Dispose();   // HUD 복구 · 컷 카메라 나감(블렌드 없음) · 디밍 기준점 복구
        _overlay = null;

        _killer = null;
        _killerSkills = null;
        _boss = null;
        _others.Clear();
        PlayerLifeInputPolicy.SetVictoryInputHeld(false);
        WallOcclusionDriver.SuppressedByCinematic = false;
        WallTransparencyGroup.SuppressedByCinematic = false;
    }

    static GameObject FindSpawned(ulong networkObjectId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.SpawnManager == null) return null;
        return nm.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject no) && no != null ? no.gameObject : null;
    }
}
