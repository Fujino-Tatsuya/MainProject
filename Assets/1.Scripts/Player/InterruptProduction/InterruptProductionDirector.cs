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
public sealed class InterruptProductionDirector : MonoBehaviour, IInterruptProductionSettings
{
    public static InterruptProductionDirector Instance { get; private set; }

    // 필드 이름은 옛 GlobalTimeScale 과 글자까지 같게 둔다 — 데이터 테이블 경로(NetworkManager.interruptProfile.scale 등)가 그대로 묶인다.
    [Header("시도 — 공용")]
    [Tooltip("인터럽트 유효타 슬로우 모양(시도). 서버 값만 쓴다. 수치 원본 = GameData.xlsx SlowMotion 시트(S6)")]
    [SerializeField] private SlowMotionProfile interruptProfile = new SlowMotionProfile();
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

    /// <summary>시도 슬로우 프로필 — 사건 순간 이 객체를 넘기면 이벤트가 그 순간 값을 복사한다(캐싱 아님).</summary>
    public SlowMotionProfile InterruptProfile => interruptProfile;
    /// <summary>성공 슬로우 프로필.</summary>
    public SlowMotionProfile InterruptSuccessProfile => interruptSuccessProfile;

    // 단계 객체는 값을 들지 않는다(설정 = this, 시간 = 포트) — 만들어 두기만 하고 사건마다 this 의 필드를 읽는다.
    private readonly ITimeScalePort _timeScale = new GlobalTimeScalePort();
    private InterruptAttemptStage _attempt;
    private InterruptResolveStage _resolve;
    private InterruptSuccessStage _success;
    private InterruptFailStage _fail;

    private InterruptAttemptStage AttemptStage => _attempt ??= new InterruptAttemptStage(this, _timeScale);
    private InterruptResolveStage ResolveStage => _resolve ??= new InterruptResolveStage(_timeScale);
    private InterruptSuccessStage SuccessStage => _success ??= new InterruptSuccessStage(this, _timeScale);
    private InterruptFailStage FailStage => _fail ??= new InterruptFailStage(_timeScale);

    private void Awake()
    {
        if (Instance != null && Instance != this)
            return;

        Instance = this;
    }

    private void OnDestroy()
    {
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

    /// <summary>A3 — 개인 채널 오너 줌 배율. 칸이 꺼져 있으면 1.</summary>
    public static float OwnerZoomFactor(float zoomFactor) => InterruptProductionRules.OwnerZoomFactor(Instance, zoomFactor);

    // ---- 서버 사건 (간파 스킬이 알린다 — 인스턴스 없으면 아무것도 안 한다) ----
    // 판정·피해·카운터 창은 스킬 소관. 여기는 GlobalTimeScale.ServerStart/ServerFail 과 캐릭터 성공 relay 만 부른다.

    /// <summary>[서버] 시도 — 예측 적중이면 시도 슬로우를 건다. 발동 번호(0 = 없음)를 돌려준다.</summary>
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
