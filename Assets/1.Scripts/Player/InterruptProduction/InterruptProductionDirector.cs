using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 간파 연출 디렉터(할 일 118) — 간파 연출의 단일 진입점. NetworkManager.prefab 의 <see cref="GlobalTimeScale"/> 옆에 붙는다.
/// 104 스위치 10칸(<see cref="InterruptProductionCue"/>)을 들고, 재생 지점은 정적 API 로 묻는다.
///
/// <list type="bullet">
/// <item>사건이 올 때마다 필드를 읽는다(캐싱 없음) — Play 중 인스펙터에서 바꾸면 다음 사건부터 바로 쓰인다.</item>
/// <item>피어마다 자기 인스턴스 값을 읽는다(동기화 없음). 단 히트스톱은 서버가 이벤트를 만들 때만 보므로 서버 값이 전 피어에 간다.</item>
/// <item>인스턴스가 없으면(EditMode·NetworkManager 없는 씬) 전부 꺼짐.</item>
/// <item>판정·피해·카운터 창에는 손대지 않는다 — 연출 재생 여부만 정한다.</item>
/// <item>시도·성공 슬로우 프로필도 여기 둔다(옛 GlobalTimeScale 필드, 이름 그대로 — xlsx SlowMotion 시트 행이 그대로 묶인다).
///       서버 인스턴스 값만 쓰인다(서버가 이벤트에 실어 보낸다). 스위치 bool 은 테이블에 내지 않는다(프리팹이 원본).</item>
/// </list>
/// 문서: Docs/tech/interrupt-presentation.md.
/// </summary>
[DisallowMultipleComponent]
[DataTableSheet("SlowMotion", Order = 0)]
public sealed class InterruptProductionDirector : MonoBehaviour, IInterruptProductionSettings, IInterruptOwnerPresentationSettings,
    IInterruptOwnerDimLook
{
    public static InterruptProductionDirector Instance { get; private set; }

    /// <summary>
    /// [각 피어] 간파 성공 사건 — 성공 슬로우가 이 피어에서 시작됐다. 칸(스위치)과 무관하게 매번 낸다.
    /// 연출 밖 구독자(디버그 씬 <c>DebugTimeScale</c> 의 성공 후 1배속)용이다 — 새 연출은 여기 말고 칸(cue)으로 붙인다.
    /// </summary>
    public static event System.Action<InterruptProductionEvent> LocalSucceeded;

    // 도메인 리로드를 끈 Play 진입에서도 이전 세션 구독자가 남지 않게 비운다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticEvents() => LocalSucceeded = null;

    // 필드 이름은 옛 GlobalTimeScale 과 글자까지 같게 둔다 — 데이터 테이블 경로(NetworkManager.interruptProfile.scale 등)가 그대로 묶인다.
    // 강 = 옛 시도 프로필 자리(interruptProfile) 그대로, 약은 새 칸(할 일 132) — 옛 xlsx 행이 강 프로필에 그대로 묶인다.
    [Header("시도 — 공용")]
    [Tooltip("간파 시도 슬로우 모양 — 성공 예측(강). 서버 값만 쓴다. 수치 원본 = GameData.xlsx SlowMotion 시트(S6)")]
    [SerializeField] private SlowMotionProfile interruptProfile = new SlowMotionProfile
    {
        scale = 0.15f,
        hitStopDuration = 0f,
        hitStopScale = 0.02f,
        enterDuration = 0.05f,
        holdDuration = 0.5f,
        exitDuration = 0.25f,
        failExitDuration = 0.1f,
    };
    [Tooltip("간파 시도 슬로우 모양 — 실패 예측(약). 서버 값만 쓴다(할 일 132)")]
    [SerializeField] private SlowMotionProfile interruptWeakProfile = new SlowMotionProfile
    {
        scale = 0.5f,
        hitStopDuration = 0f,
        hitStopScale = 0.02f,
        enterDuration = 0.05f,
        holdDuration = 0.2f,
        exitDuration = 0.25f,
        failExitDuration = 0.1f,
    };
    [Tooltip("내가 일으킨 강 시도 슬로우의 유지 단계 카메라 거리 배율. [0.1, 1] — 1 이면 줌 없음. 이 피어 값을 쓴다(A3 칸이 켜져야 쓰인다)")]
    [SerializeField, Range(SlowMotionCameraZoom.MinZoomFactor, 1f)] private float interruptZoomFactor = 0.7f;
    [Tooltip("내가 일으킨 약 시도 슬로우의 유지 단계 카메라 거리 배율. [0.1, 1] — 1 이면 줌 없음. 이 피어 값을 쓴다(A3 칸이 켜져야 쓰인다)")]
    [SerializeField, Range(SlowMotionCameraZoom.MinZoomFactor, 1f)] private float interruptWeakZoomFactor = 0.93f;
    [Tooltip("B1 방패 발광·루프 — 간파 시도(우클릭) 시작에 shieldGlow FadeIn·glowLoop Play. 끄는 쪽은 늘 돈다")]
    [DataTableIgnore, SerializeField] private bool paladinShieldGlow;
    [Tooltip("B2 방패 기 모으기 — 간파 시도 클립(Garen_Parry) 이벤트의 ShieldCharge 이펙트")]
    [DataTableIgnore, SerializeField] private bool paladinShieldCharge;
    [Tooltip("C1 기 모으기·튀기기·올려치기 — 간파 시도 클립(Attack_Up_01) 이벤트의 Assassin_InsightWindup/Pop/Slash 이펙트")]
    [DataTableIgnore, SerializeField] private bool assassinInsightAttempt;

    [Header("성공 — 공용")]
    [Tooltip("간파 성공(카운터 창 소비) 시 다시 거는 슬로우 모양. 맨 앞 히트스톱 뒤 그 배율에서 진입한다(할 일 87). 서버 값만 쓴다")]
    [SerializeField] private SlowMotionProfile interruptSuccessProfile = new SlowMotionProfile
    {
        scale = 0.25f,
        hitStopDuration = 0.05f,
        hitStopScale = 0.02f,
        enterDuration = 0.05f,
        holdDuration = 1.2f,
        exitDuration = 0.4f,
        failExitDuration = 0.1f,
    };
    [Tooltip("A1 히트스톱 — 간파 성공 슬로우 맨 앞의 짧은 정지. 서버 값만 쓴다(끄면 hitStopDuration 0 으로 전 피어에 실린다)")]
    [DataTableIgnore, SerializeField] private bool hitStop;
    [Tooltip("A4 성공 이펙트 칸 — 간파 성공 때 Player 의 InterruptSuccessFx casterFx·targetFx 원샷")]
    [DataTableIgnore, SerializeField] private bool successFxSlot;
    [Tooltip("B3 충격파 — 간파 성공(방패 적중) 때 PlayerShieldVfx.interruptWave 원샷")]
    [DataTableIgnore, SerializeField] private bool paladinWave;
    [Tooltip("C2 간파 성공 이펙트 — 간파 성공 때 올려치기 프레임에 AssassinSkillView.insightSuccess 원샷(C1 을 꺼도 같은 프레임에 나온다)")]
    [DataTableIgnore, SerializeField] private bool assassinInsightSuccess;
    [Tooltip("E1 섬광 Interrupt_Flash — 간파당한 보스·엘리트·허수아비·스파링 봇 몸에서 터지는 섬광(판정·그로기는 그대로)")]
    [DataTableIgnore, SerializeField] private bool targetFlash;
    [Tooltip("A2 암순응 — 간파 성공 때 화면의 어두운 픽셀을 잠깐 낮춘다(InterruptDarkAdaptation). 본인 화면 = 강하게, 다른 화면 = 약하게")]
    [DataTableIgnore, SerializeField] private bool darkAdaptation;

    [Header("성공 — 개인")]
    [Tooltip("A3 오너 줌 — 내가 일으킨 슬로우 동안 카메라가 다가간다(CameraTargetSwitcher). 끄면 배율 1")]
    [DataTableIgnore, SerializeField] private bool ownerZoom;

    // 오너 암전·빛 모임(할 일 132) — 오너 클라가 입력 순간 예측해 바로 켜고, 서버 결과로 걷거나 성공에 넘긴다. 이 피어 값을 쓴다.
    // 데이터 테이블 열 후보지만 아직 xlsx 칸이 없어 [DataTableIgnore] — 프리팹이 원본.
    [Header("시도·결론 — 개인(오너 암전·빛 모임, 할 일 132)")]
    [Tooltip("강 시도(성공 예측) 입력 순간 오너 암전이 0 → 1 로 들어오는 시간(초, 실시간)")]
    [DataTableIgnore, SerializeField, Min(0f)] private float ownerDimInDuration = 0.1f;
    [Tooltip("예측 빗나감(서버 목적이 다름)·서버 실패·판정 전 종료 때 오너 암전을 걷는 시간(초, 실시간). 빛 모임은 그 순간 멈춘다")]
    [DataTableIgnore, SerializeField, Min(0f)] private float ownerDimOutDuration = 0.2f;
    [Tooltip("서버 성공 확정 뒤 오너 암전을 걷기 시작할 때까지(초, 실시간) — 적중 섬광(할 일 132 단계 5) 전 임시 동작")]
    [DataTableIgnore, SerializeField, Min(0f)] private float ownerSuccessHoldDuration = 0.4f;
    [Tooltip("오너 암전 덮개 색(RGB만 쓴다) — 남길 것(오너·간파 대상·잔상·빛 모임·오너 무기 이펙트) 밖의 월드·HUD 를 덮는다. 예: 검정, 남색")]
    [DataTableIgnore, SerializeField] private Color ownerDimColor = Color.black;
    [Tooltip("오너 암전 덮개 불투명도 [0, 1] — 1 = 100%(완전히 덮음), 0.9 = 90%")]
    [DataTableIgnore, SerializeField, Range(0f, 1f)] private float ownerDimOpacity = 1f;

    /// <summary>시도 슬로우 프로필(강) — 사건 순간 이 객체를 넘기면 이벤트가 그 순간 값을 복사한다(캐싱 아님).</summary>
    public SlowMotionProfile InterruptProfile => interruptProfile;
    /// <summary>시도 슬로우 프로필(약).</summary>
    public SlowMotionProfile InterruptWeakProfile => interruptWeakProfile;
    /// <summary>성공 슬로우 프로필.</summary>
    public SlowMotionProfile InterruptSuccessProfile => interruptSuccessProfile;

    public float OwnerDimInDuration => ownerDimInDuration;
    public float OwnerDimOutDuration => ownerDimOutDuration;
    public float OwnerSuccessHoldDuration => ownerSuccessHoldDuration;
    public Color OwnerDimColor => ownerDimColor;
    public float OwnerDimOpacity => ownerDimOpacity;

    // 단계 객체는 값을 들지 않는다(설정 = this, 시간 = 포트) — 만들어 두기만 하고 사건마다 this 의 필드를 읽는다.
    private readonly ITimeScalePort _timePort = new GlobalTimeScalePort();
    private InterruptAttemptStage _attempt;
    private InterruptResolveStage _resolve;
    private InterruptSuccessStage _success;
    private InterruptFailStage _fail;
    private InterruptPersonalChannel _personal;
    private InterruptSharedChannel _shared;

    private InterruptPersonalChannel PersonalChannel => _personal ??= new InterruptPersonalChannel(this);
    private InterruptSharedChannel SharedChannel => _shared ??= new InterruptSharedChannel(this);
    private InterruptAttemptStage AttemptStage => _attempt ??= new InterruptAttemptStage(this, _timePort, SharedChannel);
    private InterruptResolveStage ResolveStage => _resolve ??= new InterruptResolveStage(_timePort);
    private InterruptSuccessStage SuccessStage => _success ??= new InterruptSuccessStage(this, _timePort, PersonalChannel, SharedChannel);
    private InterruptFailStage FailStage => _fail ??= new InterruptFailStage(_timePort);

    // 오너 연출 상태 기계(할 일 132). 암전 = 마스크 암전 렌더 출력(단계 3, Awake 에서 만든다 — 없으면 로그), 빛 모임 = 단계 4 전까지 로그.
    private readonly InterruptOwnerPresentationLogOutput _ownerLogOutput = new InterruptOwnerPresentationLogOutput();
    private InterruptOwnerDimOutput _ownerDimOutput;
    private InterruptOwnerPresentation _ownerPresentation;

    private IInterruptOwnerDimOutput DefaultOwnerDimOutput =>
        _ownerDimOutput != null ? _ownerDimOutput : (IInterruptOwnerDimOutput)_ownerLogOutput;

    /// <summary>오너 연출 상태 기계 — 단계 5(적중 섬광)가 <see cref="InterruptOwnerPresentation.SuccessEntered"/> 를 구독한다.</summary>
    public InterruptOwnerPresentation OwnerPresentation =>
        _ownerPresentation ??= new InterruptOwnerPresentation(this, DefaultOwnerDimOutput, _ownerLogOutput);

    // 피어 연출 — 슬로우 이벤트(Started/Failed/ForcedReset)의 유일한 구독자. 객체 참조만 들고 값은 들지 않는다.
    private GlobalTimeScale _timeScale;
    private NetworkManager _networkManager;
    private InterruptDarkAdaptation _darkAdaptation;
    private readonly List<IInterruptCueSink> _sinkBuffer = new List<IInterruptCueSink>();

    private void Awake()
    {
        // TryGetComponent — 없으면 진짜 null(에디터의 GetComponent 가짜 null 은 인터페이스로 넘기면 null 비교를 통과한다).
        TryGetComponent(out _timeScale);
        TryGetComponent(out _networkManager);
        TryGetComponent(out _darkAdaptation);

        if (Instance != null && Instance != this)
            return;

        Instance = this;
        _ownerDimOutput = new InterruptOwnerDimOutput(this);
    }

    private void OnEnable()
    {
        if (_timeScale == null)
            return;

        _timeScale.Started += HandleStarted;
        _timeScale.Failed += HandleFailed;
        _timeScale.ForcedReset += HandleForcedReset;
    }

    private void Update()
    {
        // 오너 암전 램프·성공 임시 걷기 — 실시간(슬로우 중에도 걷기 시간이 그대로).
        _ownerPresentation?.Tick(Time.unscaledTime);
    }

    private void OnDisable()
    {
        _ownerPresentation?.ForcedReset();

        if (ReferenceEquals(_timeScale, null))
            return;

        // 파괴된 GlobalTimeScale 도 C# 객체는 남아 있어 그대로 뗄 수 있다.
        _timeScale.Started -= HandleStarted;
        _timeScale.Failed -= HandleFailed;
        _timeScale.ForcedReset -= HandleForcedReset;
    }

    private void OnDestroy()
    {
        // 비트 원복·덮개 캔버스·RT 정리(상태 기계는 OnDisable 에서 이미 0 을 넘겼다).
        _ownerDimOutput?.Dispose();
        _ownerDimOutput = null;

        if (Instance == this)
            Instance = null;
    }

    /// <summary>이 디렉터의 칸 — 호출 순간 필드를 읽는다.</summary>
    public bool Get(InterruptProductionCue cue)
    {
        switch (cue)
        {
            case InterruptProductionCue.HitStop: return hitStop;
            case InterruptProductionCue.DarkAdaptation: return darkAdaptation;
            case InterruptProductionCue.OwnerZoom: return ownerZoom;
            case InterruptProductionCue.SuccessFxSlot: return successFxSlot;
            case InterruptProductionCue.PaladinShieldGlow: return paladinShieldGlow;
            case InterruptProductionCue.PaladinShieldCharge: return paladinShieldCharge;
            case InterruptProductionCue.PaladinWave: return paladinWave;
            case InterruptProductionCue.AssassinInsightAttempt: return assassinInsightAttempt;
            case InterruptProductionCue.AssassinInsightSuccess: return assassinInsightSuccess;
            case InterruptProductionCue.TargetFlash: return targetFlash;
            default: return false;
        }
    }

    // ---- 재생 지점이 부르는 정적 API (인스턴스 없으면 꺼짐) ----

    /// <summary>이 피어의 칸. 인스턴스가 없으면 꺼짐.</summary>
    public static bool IsOn(InterruptProductionCue cue) => InterruptProductionRules.IsOn(Instance, cue);

    /// <summary>B2·C1 — 클립 이벤트 이펙트 재생 허용(<see cref="EffectAnimEvents"/>). 간파 id 가 아니면 늘 true.</summary>
    public static bool AllowsAnimEffect(string id) => InterruptProductionRules.AllowsAnimEffect(Instance, id);

    /// <summary>
    /// A3 — 개인 채널 오너 줌 배율(카메라가 매 프레임 묻는다). 지금 슬로우 목적으로 강·약 시도(이 디렉터 칸)·성공(<paramref name="successZoomFactor"/>)
    /// 배율을 고른다(할 일 132). 칸이 꺼져 있거나 인스턴스가 없으면 1.
    /// </summary>
    public static float OwnerZoomFactor(SlowMotionPurpose purpose, float successZoomFactor) =>
        Instance != null
            ? Instance.SuccessStage.OwnerZoomFactor(InterruptProductionRules.SelectOwnerZoomFactor(
                purpose, Instance.interruptZoomFactor, Instance.interruptWeakZoomFactor, successZoomFactor))
            : 1f;

    /// <summary>
    /// [각 피어] 시도 — 시전 시작(OnClientPlay, 전 피어)에 스킬이 알린다. 그 스킬의 시도 cue(B1)를 공용 채널로 스킬 자신에 튼다.
    /// 끄는 쪽(OnEnd)은 디렉터를 거치지 않는다.
    /// </summary>
    public static void LocalAttempt(in InterruptProductionEvent e)
    {
        if (Instance != null)
            Instance.AttemptStage.Local(e, e.Skill as IInterruptCueSink);
    }

    /// <summary>
    /// [각 피어] 캐릭터·대상 RPC 수신부(B3 충격파·C2 어쌔신 성공·E1 맞은 쪽 섬광)가 부른다 — 칸이 켜져 있으면 <paramref name="sink"/> 에 튼다(공용 채널).
    /// 전송로(RPC)는 그대로다(설계 Q1-a). 인스턴스가 없으면 안 튼다(옛 스위치 꺼짐과 같다).
    /// </summary>
    public static void PlayCue(InterruptProductionCue cue, in InterruptProductionEvent e, IInterruptCueSink sink)
    {
        if (Instance != null)
            Instance.SharedChannel.PlayCue(cue, e, sink);
    }

    // ---- 오너 사건 (할 일 132 — 간파한 본인 화면만, 오너 연출 상태 기계로) ----

    /// <summary>
    /// [오너] 간파(근접·대쉬) 입력 순간의 즉시 예측 — 강(성공 예측)이면 오너 암전·빛 모임을 바로 켠다. 약은 아무것도 안 켠다
    /// (줌·슬로우는 서버 시도 슬로우가 처리), 대상 없음도 아무것도 안 켠다(정정1). 앞 시도가 결론나지 않았으면 중복 입력으로 무시한다.
    /// <paramref name="owner"/>·<paramref name="target"/> = 암전에 남길 오너 캐릭터(무기 이펙트 포함 하위 전부)·이번 시도 간파 대상(예측이 고른 캐시 대상).
    /// </summary>
    public static void LocalOwnerAttempt(PlayerInterruptSkillBase skill, BeaverLobby.Player.Dash.InterruptAttemptForecast forecast,
        GameObject owner, GameObject target)
    {
        if (Instance == null)
            return;

        // 받아들인 시도만 대상을 바꾼다 — 태깅은 다음 프레임 암전이 0 → 양수가 될 때(들어옴 램프 첫 값).
        if (Instance.OwnerPresentation.Predict(forecast, skill, Time.unscaledTime))
            Instance._ownerDimOutput?.SetSubjects(owner, target);
    }

    /// <summary>
    /// [오너] 이번 오너 암전에 함께 남길 루트(빛 모임 VFX 등, 단계 4). 암전이 걷히면 빠진다 — 시도마다 다시 부른다.
    /// 렌더링 레이어 비트 4 를 그 순간 루트 아래 렌더러에 붙인다(<see cref="InterruptOwnerMaskTagger"/>).
    /// </summary>
    public static void KeepInOwnerDim(GameObject root)
    {
        if (Instance != null)
            Instance._ownerDimOutput?.KeepWhileDimmed(root);
    }

    /// <summary>[오너] 간파 스킬 애니 Hit 이벤트 — 빛 모임 끝(기존 Hit 이벤트 재사용).</summary>
    public static void LocalOwnerHit()
    {
        if (Instance != null)
            Instance._ownerPresentation?.LocalHit();
    }

    /// <summary>[오너] 판정 전 종료·서버 거부 — 결론이 없으면 실패처럼 걷는다.</summary>
    public static void LocalOwnerEnded()
    {
        if (Instance != null)
            Instance._ownerPresentation?.LocalEnded(Time.unscaledTime);
    }

    /// <summary>
    /// 오너 암전·빛 모임 출력을 갈아 끼운다(단계 4 VFX 등). null 이면 기본 — 암전 = 마스크 암전 렌더 출력, 빛 모임 = 로그.
    /// </summary>
    public static void SetOwnerOutputs(IInterruptOwnerDimOutput dimOutput, IInterruptLightGatherOutput gatherOutput)
    {
        if (Instance == null)
            return;

        Instance.OwnerPresentation.SetOutputs(
            dimOutput ?? Instance.DefaultOwnerDimOutput, gatherOutput ?? Instance._ownerLogOutput);
    }

    // ---- 피어 사건 (GlobalTimeScale 이벤트 → 단계) ----

    private void HandleStarted(uint triggerId, ulong casterClientId, SlowMotionPurpose purpose, Vector3 targetPosition)
    {
        // 보는 사람을 못 정하면(NetworkManager 없음) 암순응은 건너뛴다 — 옛 InterruptDarkAdaptation 과 같다.
        bool hasViewer = _networkManager != null;
        bool viewerIsCaster = hasViewer && InterruptProductionRules.IsCasterView(casterClientId, _networkManager.LocalClientId);

        // 오너 연출(할 일 132) — 내 슬로우만(팀원 화면은 시도 단계 무변화). 시도 목적이 예측과 다르면 걷고, 성공이면 성공 단계로.
        if (viewerIsCaster)
            OwnerPresentation.ServerStarted(triggerId, purpose, true, Time.unscaledTime);

        if (purpose != SlowMotionPurpose.Success)
            return;

        var e = InterruptProductionEvent.LocalSuccess(triggerId, casterClientId, targetPosition);
        SuccessStage.LocalStarted(purpose, e, viewerIsCaster, hasViewer ? _darkAdaptation : null,
            FindSuccessFxSinks(casterClientId));
        LocalSucceeded?.Invoke(e);
    }

    private void HandleFailed(uint triggerId)
    {
        _ownerPresentation?.ServerFailed(triggerId, Time.unscaledTime);
        FailStage.Local(InterruptProductionEvent.LocalFail(triggerId));
    }

    private void HandleForcedReset(uint triggerId)
    {
        _ownerPresentation?.ForcedReset();
        SuccessStage.LocalForcedReset(_darkAdaptation);
    }

    // A4 — 발동자 플레이어의 성공 이펙트 칸. 스폰된 오브젝트 중 발동자 소유의 켜진 InterruptSuccessFx(옛 구독 조건과 같다).
    // SpawnManager.GetPlayerNetworkObject 는 클라에서 남의 것을 찾으면 에러를 찍는다 — 스폰 목록에서 직접 찾는다.
    private List<IInterruptCueSink> FindSuccessFxSinks(ulong casterClientId)
    {
        _sinkBuffer.Clear();
        NetworkSpawnManager spawnManager = _networkManager != null ? _networkManager.SpawnManager : null;
        if (spawnManager == null)
            return _sinkBuffer;

        foreach (NetworkObject networkObject in spawnManager.SpawnedObjectsList)
        {
            if (networkObject == null || networkObject.OwnerClientId != casterClientId)
                continue;

            foreach (InterruptSuccessFx fx in networkObject.GetComponents<InterruptSuccessFx>())
            {
                if (fx.isActiveAndEnabled)
                    _sinkBuffer.Add(fx);
            }
        }

        return _sinkBuffer;
    }

    // ---- 서버 사건 (간파 스킬이 알린다 — 인스턴스 없으면 아무것도 안 한다) ----
    // 판정·피해·카운터 창은 스킬 소관. 여기는 GlobalTimeScale.ServerStart/ServerFail 과 캐릭터 성공 relay 만 부른다.

    /// <summary>[서버] 시도 — 성공 예측 = 강 / 실패 예측 = 약 시도 슬로우를 건다(할 일 132). 발동 번호(0 = 없음)를 돌려준다.</summary>
    public static uint ServerAttempt(in InterruptProductionEvent e) => Instance != null ? Instance.AttemptStage.Server(e) : 0;

    /// <summary>[서버] 판정 — 시도 슬로우 정리(빗나감이면 실패 복귀).</summary>
    public static void ServerResolve(in InterruptProductionEvent e)
    {
        if (Instance != null)
            Instance.ResolveStage.Server(e);
    }

    /// <summary>[서버] 성공 — 성공 슬로우(+A1)를 걸고 시전자의 캐릭터 성공 relay 를 부른다. 판정 사건 바로 뒤에 알린다.</summary>
    public static void ServerSuccess(in InterruptProductionEvent e)
    {
        if (Instance != null)
            Instance.SuccessStage.Server(e, FindSuccessRelays(e.Caster));
    }

    /// <summary>[서버] 실패 — 판정 전 종료·앵커 누락. 모든 피어에서 불려도 발동 번호가 0 이면 아무것도 안 한다.</summary>
    public static void ServerFail(in InterruptProductionEvent e)
    {
        if (Instance != null)
            Instance.FailStage.Server(e);
    }

    // 성공은 드물어 사건마다 찾는다(값 캐싱 없음 규칙과도 맞다).
    private static IInterruptSuccessRelay[] FindSuccessRelays(Player caster) =>
        caster != null ? caster.GetComponentsInChildren<IInterruptSuccessRelay>(true) : null;
}
