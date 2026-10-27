using TMPro;
using UnityEngine;

/// <summary>
/// 튜토리얼 4구역 진입 전 소개 연출 데이터(PLAN-zone4-intro-cinematic) — 에셋 1개 = <c>Assets/Resources/MapScene/Zone4IntroCinematic.asset</c>.
/// · <b>시간</b>(샷 이동·머묾, 대사 시작 지연·조각 표시·조각 간격, 한 줄 최대 폭) = xlsx <c>Cinematic</c> 시트(기획 — 빌드는 항상 xlsx 값).
/// · <b>대사 문구</b> = 자막 표 <c>TutorialGuideTexts</c> 행(xlsx <c>Strings</c> 시트 키 <c>Tutorial.Guide.&lt;id&gt;</c>, ko/en). 여기는 id 만 든다.
/// · <b>카메라 구도·화면·무전 UI</b> = 이 에셋 인스펙터(xlsx 제외 — 숫자만 봐서는 구도를 알 수 없다, 경석 10-26).
/// 에셋이 없으면 코드 기본값으로 돈다(경고 1회).
/// 샷 순서: 0~3 = 제어 패널 A~D(화면 왼쪽 아래 패널부터 시계 방향 — 실제 가동 순서와 무관) · 4 = 끊어진 다리 + 중앙 섬 적.
/// </summary>
[CreateAssetMenu(fileName = "Zone4IntroCinematic", menuName = "BeaverLobby/MapScene/Zone4 Intro Cinematic", order = 30)]
[DataTableSheet("Cinematic")]
public sealed class MapSceneZone4IntroData : ScriptableObject
{
    public const string ResourcesPath = "MapScene/Zone4IntroCinematic";
    public const int PanelShotCount = 4;
    public const int ShotCount = PanelShotCount + 1;

    [Header("켜기")]
    [Tooltip("끄면 예전 흐름 — 특성 선택이 끝나는 즉시 G7 이 열린다(연출 없음).")]
    [SerializeField] private bool introEnabled = true;

    [Header("샷 시간(초) — 0~3 패널 A~D · 4 다리·중앙 섬")]
    [Tooltip("샷마다 [앞 샷에서 옮겨 가는 시간 → 머무는 시간]. 0번의 옮겨 가기 = 게임 카메라 → 첫 패널 블렌드. 개수는 5 고정(xlsx 는 개수를 못 바꾼다).")]
    [SerializeField] private CinematicShotTiming[] shots =
    {
        new CinematicShotTiming(1.0f, 1.6f),
        new CinematicShotTiming(0.9f, 1.6f),
        new CinematicShotTiming(0.9f, 1.6f),
        new CinematicShotTiming(0.9f, 1.6f),
        new CinematicShotTiming(1.2f, 5.0f),
    };

    [Tooltip("마지막 샷 → 플레이어 카메라 블렌드(초). 블렌드가 끝나는 순간 G7·타이머·기상.")]
    [SerializeField, Min(0f)] private float returnBlendSeconds = 1.2f;

    [Header("스킵")]
    [Tooltip("Space 를 이만큼 눌러야 스킵 동의(초). 채운 표는 손을 떼도 유지 — 보스 등장 연출과 같은 규칙.")]
    [SerializeField, Min(0.1f)] private float skipHoldSeconds = 1f;
    [Tooltip("전원 스킵하면 보던 자리에서 다리·중앙 섬 샷(중간보스)으로 옮겨 가는 시간(초) — 바로 플레이어로 가지 않고 중간보스를 한 번 보여 준다(경석 10-27). 이미 그 샷이면 생략.")]
    [SerializeField, Min(0f)] private float skipToBossSeconds = 0.8f;
    [Tooltip("스킵 뒤 다리·중앙 섬 샷에 머무는 시간(초).")]
    [SerializeField, Min(0f)] private float skipBossHoldSeconds = 1.2f;
    [Tooltip("스킵 뒤 다리·중앙 섬 샷 → 플레이어 카메라 블렌드(초).")]
    [SerializeField, Min(0f)] private float skipReturnSeconds = 1f;

    [Header("대사 — 시간(문구는 Strings 시트)")]
    [Tooltip("소개 중 대사. 각 줄 = 어느 샷에 도착한 뒤 몇 초 뒤 시작 · 조각(한 줄) 표시 시간 · 조각 사이 간격. 다음 대사가 시작되면 앞 대사의 남은 조각은 버린다.")]
    [SerializeField] private CinematicLineTiming[] lines =
    {
        // 장치 대사는 장치 4곳을 다 보여 줄 때까지 남는다(경석 10-27 — 장치를 다 보기 전에 내려가면 헷갈린다).
        new CinematicLineTiming { lineId = TutorialGuideIds.Z4IntroDevices, shotIndex = 0, delaySeconds = 0.3f, chunkSeconds = 2.6f, gapSeconds = 0.25f, holdUntilNextLine = true },
        new CinematicLineTiming { lineId = TutorialGuideIds.Z4IntroBridge, shotIndex = 4, delaySeconds = 0.3f, chunkSeconds = 2.4f, gapSeconds = 0.25f },
    };

    [Tooltip("복귀 뒤 무전 문구 id(Strings 시트) — 플레이와 병행(조작을 막지 않는다).")]
    [SerializeField] private string returnLineId = TutorialGuideIds.Z4IntroReturn;
    [Tooltip("복귀가 끝난 뒤 복귀 무전이 나올 때까지(초).")]
    [SerializeField, Min(0f)] private float returnLineDelaySeconds = 0.2f;
    [Tooltip("복귀 무전 한 조각 표시 시간(초).")]
    [SerializeField, Min(0.1f)] private float returnLineChunkSeconds = 1.8f;
    [Tooltip("복귀 무전 조각 사이 간격(초).")]
    [SerializeField, Min(0f)] private float returnLineGapSeconds = 0.25f;

    [Tooltip("무전 한 줄 최대 폭(1920×1080 기준 UI 단위). 넘으면 띄어쓰기에서 다음 조각으로 넘긴다. 문구에 | 를 넣으면 거기서 무조건 끊는다.")]
    [SerializeField, Min(100f)] private float maxLineWidth = 760f;

    [Header("카메라 구도 — 패널 샷(인스펙터 · 기획 확인 전 임시값)")]
    [DataTableIgnore, Tooltip("패널을 내려다보는 각(도). 게임 카메라 59.8°.")]
    [SerializeField, Range(5f, 85f)] private float panelPitch = 50f;
    [DataTableIgnore, Tooltip("패널까지 거리(m).")]
    [SerializeField, Min(1f)] private float panelDistance = 14f;
    [DataTableIgnore, Tooltip("세로 FOV(도). 게임 카메라 34.")]
    [SerializeField, Range(10f, 80f)] private float panelFov = 40f;
    [DataTableIgnore, Tooltip("바라보는 점 = 패널 메시 중심 + 이 높이(m). 0 = 패널이 화면 한가운데(경석 10-27).")]
    [SerializeField] private float panelFocusHeight = 0f;
    [DataTableIgnore, Tooltip("바라보는 점을 존 중앙 쪽으로 당기는 비율(0 = 패널이 화면 한가운데 · 0.3 = 중앙 쪽 30%). 0 이 아니면 바깥 패널이 화면 가장자리로 밀린다(경석 10-27 — 4번째 패널이 너무 아래).")]
    [SerializeField, Range(0f, 1f)] private float panelTowardCenter = 0f;
    [DataTableIgnore, Tooltip("패널 샷에 머무는 동안 그 패널에 외곽선(근접 하이라이트와 같은 것)을 켠다.")]
    [SerializeField] private bool panelOutline = true;

    [Header("카메라 구도 — 다리·중앙 섬 샷(인스펙터)")]
    [DataTableIgnore, Tooltip("내려다보는 각(도).")]
    [SerializeField, Range(5f, 85f)] private float bridgePitch = 58f;
    [DataTableIgnore, Tooltip("켜면 다리 조각 전부 + 존 중앙(중앙 섬)을 감싸는 범위가 화면에 다 들어오는 거리로 자동(경석 10-27 — 다리가 안 보였다). 끄면 bridgeDistance.")]
    [SerializeField] private bool bridgeAutoFit = true;
    [DataTableIgnore, Tooltip("자동 맞춤 여유 배율(1 = 화면 끝에 딱 · 1.15 = 15% 여유).")]
    [SerializeField, Range(0.8f, 2f)] private float bridgeFitMargin = 1.1f;
    [DataTableIgnore, Tooltip("거리(m) — 자동 맞춤을 끈 때만.")]
    [SerializeField, Min(1f)] private float bridgeDistance = 36f;
    [DataTableIgnore, Tooltip("세로 FOV(도).")]
    [SerializeField, Range(10f, 80f)] private float bridgeFov = 45f;
    [DataTableIgnore, Tooltip("다리·중앙 섬 샷에서 다리 조각에 외곽선을 켠다.")]
    [SerializeField] private bool bridgeOutline = true;
    [DataTableIgnore, Tooltip("외곽선을 줄 다리 조각 이름(비면 전부 — 기본). 실루엣 외곽선은 조각들을 하나로 합쳐 둘레만 그리므로 겹친 이동식 다리도 넣는다 " +
                              "(경석 10-27 — 고정 다리만 넣었더니 이동식 다리가 튀어나온 연결 지점에 테두리가 없었다).")]
    [SerializeField] private string[] bridgeOutlineNames = new string[0];
    [DataTableIgnore, Tooltip("다리 외곽선 머티리얼 — 화면 공간 실루엣 외곽선(Zone/CinematicSilhouetteOutline · Assets/3.Materials/Map/MA_CinematicBridgeOutline.mat). " +
                              "깊이와 무관하게 다리 둘레에만 테두리를 그린다. 두께(Width)·색(Color, 알파 = 진하기)은 그 머티리얼에서 조절 — 그대로 쓴다(경석 10-27). " +
                              "비우면 근접 하이라이트 머티리얼(반전 헐 — 바닥·벽 홈에 붙은 다리는 테두리가 묻혀 조각만 보였다).")]
    [SerializeField] private Material bridgeOutlineMaterial;
    [DataTableIgnore, Tooltip("다리 영역 표시 머티리얼(같은 셰이더, 테두리 패스 끔 · 큐 3990 — Assets/3.Materials/Map/MA_CinematicBridgeOutlineMask.mat). " +
                              "모든 조각의 영역을 먼저 표시해야 조각들이 하나의 실루엣으로 이어진다. 테두리 머티리얼(큐 4000)은 영역 표시 패스를 끈다.")]
    [SerializeField] private Material bridgeOutlineMaskMaterial;
    [DataTableIgnore, Tooltip("다리 외곽선을 존 중심에서 이 반경(m) 안쪽만 그린다 — 이동식 다리 중 2층 바닥 아래로 들어간 부분을 빼려고(경석 10-27). " +
                              "0 = 자동(연출 시작 때 다리를 따라 바깥으로 가며 위에서 레이를 쏴 다리보다 높은 바닥에 처음 닿는 거리 — 로그 '[Zone4Intro] 다리 외곽선 반경'). 0 보다 크면 이 값.")]
    [SerializeField, Min(0f)] private float bridgeClipRadius = 0f;
    [DataTableIgnore, Tooltip("자동 측정이 실패하면 쓸 반경(m). 0 = 자르지 않음.")]
    [SerializeField, Min(0f)] private float bridgeClipFallbackRadius = 0f;
    [DataTableIgnore, Tooltip("잘린 끝(바닥 가장자리)을 따라 남길 테두리 폭(m) — 반경 + 이 값까지 테두리를 그린다.")]
    [SerializeField, Min(0f)] private float bridgeClipEdgeWidth = 0.25f;
    [DataTableIgnore, Tooltip("바라보는 점 = 다리 조각 + 존 중앙 범위의 중심 + 이 높이(m).")]
    [SerializeField] private float bridgeFocusHeight = 0f;
    [DataTableIgnore, Tooltip("바라보는 점을 화면 아래(+)/위(−)로 옮기는 거리(m).")]
    [SerializeField] private float bridgeFocusSouth = 0f;

    [Header("화면(인스펙터)")]
    [DataTableIgnore, Tooltip("레터박스 위·아래 각각 화면 높이 비율 — 보스 등장 연출 0.04.")]
    [SerializeField, Range(0f, 0.2f)] private float letterboxHeight = 0.04f;
    [DataTableIgnore, Tooltip("연출 비네팅 강도 — 보스 등장 연출 0.35.")]
    [SerializeField, Range(0f, 1f)] private float vignetteIntensity = 0.35f;
    [DataTableIgnore]
    [SerializeField, Range(0.01f, 1f)] private float vignetteSmoothness = 0.42f;
    [DataTableIgnore, Tooltip("레터박스·비네팅 페이드(초).")]
    [SerializeField, Min(0f)] private float overlayFadeSeconds = 0.4f;
    [DataTableIgnore, Tooltip("연출 동안 층·시야 디밍(전장의 안개)을 얼마나 줄이나(0 = 평소대로 · 1 = 안 누름). 멀리·다른 층을 비추면 평소 디밍이 화면을 통째로 어둡게 깐다(경석 10-27). 첫 블렌드 동안 오르고 복귀 블렌드 동안 내려간다.")]
    [SerializeField, Range(0f, 1f)] private float dimSuppress = 1f;
    [DataTableIgnore, Tooltip("스킵 버튼 배경(button_pill_neutral) — 보스 등장 연출과 같은 그림.")]
    [SerializeField] private Sprite skipButtonSprite;

    [Header("무전 UI(인스펙터) — SVN r504 진행 도움 말풍선")]
    [DataTableIgnore, Tooltip("캡슐 배경(progress_hint_background_long) — 양 끝을 9-슬라이스로 잘라 글자 길이에 맞춘다.")]
    [SerializeField] private Sprite captionBackground;
    [DataTableIgnore, Tooltip("원형 배지(progress_hint_badge).")]
    [SerializeField] private Sprite captionBadge;
    [DataTableIgnore, Tooltip("무전기(progress_hint_walkie).")]
    [SerializeField] private Sprite captionWalkie;
    [DataTableIgnore, Tooltip("글꼴 — 비우면 안내 UI 자막 글꼴.")]
    [SerializeField] private TMP_FontAsset captionFont;
    [DataTableIgnore, Tooltip("캡션 중심의 화면 위치 비율(x 0 = 왼쪽 · 1 = 오른쪽 / y 0 = 아래 · 1 = 위). 경석 10-27: 가운데 아래, 세로 1/4 ~ 1.25/4 지점 → y 0.28.")]
    [SerializeField] private Vector2 captionAnchor = new Vector2(0.5f, 0.28f);
    [DataTableIgnore, Tooltip("위 비율 자리에서 더 옮기는 거리(UI 단위, 1920×1080 기준).")]
    [SerializeField] private Vector2 captionOffset = Vector2.zero;
    [DataTableIgnore, Tooltip("캡슐 크기 배율(1 = 아트 원본 70 높이).")]
    [SerializeField, Range(0.3f, 2f)] private float captionScale = 0.85f;
    [DataTableIgnore]
    [SerializeField, Min(8f)] private float captionFontSize = 30f;
    [DataTableIgnore]
    [SerializeField] private Color captionTextColor = new Color(0.96f, 0.93f, 0.86f, 1f);
    [DataTableIgnore, Tooltip("조각이 나타나고 사라지는 페이드(초).")]
    [SerializeField, Min(0f)] private float captionFadeSeconds = 0.15f;
    [DataTableIgnore, Tooltip("조각이 나타날 때 아래에서 올라오는 거리(UI 단위) — 아웃라인 대신 등장 움직임으로 시선을 끈다(경석 10-27). 0 = 안 움직임.")]
    [SerializeField, Min(0f)] private float captionRiseDistance = 14f;
    [DataTableIgnore, Tooltip("올라오는 시간(초).")]
    [SerializeField, Min(0.01f)] private float captionRiseSeconds = 0.25f;
    [DataTableIgnore, Tooltip("조각이 나타날 때 무전기 배지가 한 번 커지는 정도(0.15 = 15% 커졌다 돌아옴). 0 = 펄스 없음.")]
    [SerializeField, Range(0f, 1f)] private float badgePulseScale = 0.18f;
    [DataTableIgnore, Tooltip("배지 펄스 시간(초).")]
    [SerializeField, Min(0.01f)] private float badgePulseSeconds = 0.35f;

    public bool IntroEnabled => introEnabled;
    public CinematicShotTiming[] Shots => shots;
    public float ReturnBlendSeconds => returnBlendSeconds;
    public float SkipHoldSeconds => skipHoldSeconds;
    public float SkipToBossSeconds => skipToBossSeconds;
    public float SkipBossHoldSeconds => skipBossHoldSeconds;
    public float SkipReturnSeconds => skipReturnSeconds;

    /// <summary>다리·중앙 섬(중간보스) 샷 번호 = 마지막 샷.</summary>
    public int BossShotIndex => Mathf.Max(0, (shots != null ? shots.Length : ShotCount) - 1);

    /// <summary>다리·중앙 섬 샷에 카메라가 도착하는 시각(초) — 이 뒤의 스킵은 중간보스를 이미 보고 있으니 바로 복귀.</summary>
    public float BossShotArrival => CinematicTimeline.ArrivalTime(shots, BossShotIndex);
    public CinematicLineTiming[] Lines => lines;
    public string ReturnLineId => returnLineId;
    public float ReturnLineDelaySeconds => returnLineDelaySeconds;
    public float ReturnLineChunkSeconds => returnLineChunkSeconds;
    public float ReturnLineGapSeconds => returnLineGapSeconds;
    public float MaxLineWidth => maxLineWidth;
    public float PanelPitch => panelPitch;
    public float PanelDistance => panelDistance;
    public float PanelFov => panelFov;
    public float PanelFocusHeight => panelFocusHeight;
    public float PanelTowardCenter => panelTowardCenter;
    public bool PanelOutline => panelOutline;
    public bool BridgeAutoFit => bridgeAutoFit;
    public float BridgeFitMargin => bridgeFitMargin;
    public bool BridgeOutline => bridgeOutline;
    public string[] BridgeOutlineNames => bridgeOutlineNames;
    public Material BridgeOutlineMaterialSource => bridgeOutlineMaterial;
    public Material BridgeOutlineMaskMaterial => bridgeOutlineMaskMaterial;
    public float BridgeClipRadius => bridgeClipRadius;
    public float BridgeClipFallbackRadius => bridgeClipFallbackRadius;
    public float BridgeClipEdgeWidth => bridgeClipEdgeWidth;
    public float BridgePitch => bridgePitch;
    public float BridgeDistance => bridgeDistance;
    public float BridgeFov => bridgeFov;
    public float BridgeFocusHeight => bridgeFocusHeight;
    public float BridgeFocusSouth => bridgeFocusSouth;
    public float LetterboxHeight => letterboxHeight;
    public float VignetteIntensity => vignetteIntensity;
    public float VignetteSmoothness => vignetteSmoothness;
    public float OverlayFadeSeconds => overlayFadeSeconds;
    public float DimSuppress => dimSuppress;
    public Sprite SkipButtonSprite => skipButtonSprite;
    public Sprite CaptionBackground => captionBackground;
    public Sprite CaptionBadge => captionBadge;
    public Sprite CaptionWalkie => captionWalkie;
    public TMP_FontAsset CaptionFont => captionFont;
    public Vector2 CaptionAnchor => captionAnchor;
    public Vector2 CaptionOffset => captionOffset;
    public float CaptionScale => captionScale;
    public float CaptionFontSize => captionFontSize;
    public Color CaptionTextColor => captionTextColor;
    public float CaptionFadeSeconds => captionFadeSeconds;
    public float CaptionRiseDistance => captionRiseDistance;
    public float CaptionRiseSeconds => captionRiseSeconds;
    public float BadgePulseScale => badgePulseScale;
    public float BadgePulseSeconds => badgePulseSeconds;

    /// <summary>소개 전체 길이(초) — 샷 합. 서버는 이 시각에 복귀를 시작한다.</summary>
    public float IntroSeconds => CinematicTimeline.TotalSeconds(shots);

    private static MapSceneZone4IntroData _cached;
    private static bool _warned;

    /// <summary>Resources 에셋(없으면 코드 기본값 인스턴스 — 경고 1회). 모든 피어가 같은 빌드면 같은 값.</summary>
    public static MapSceneZone4IntroData Load()
    {
        if (_cached != null) return _cached;
        _cached = Resources.Load<MapSceneZone4IntroData>(ResourcesPath);
        if (_cached == null)
        {
            if (!_warned) Edit.LogWarning($"[Zone4Intro] Resources/{ResourcesPath} 가 없다 — 코드 기본값으로 돈다(구도·무전 그림 없음).");
            _warned = true;
            _cached = CreateInstance<MapSceneZone4IntroData>();
            _cached.hideFlags = HideFlags.DontSave;
        }
        return _cached;
    }
}
