using UnityEngine;

/// <summary>
/// 2구역 진입 전 적 소개 연출 데이터(PLAN-main-radio-zone1-4 §4.2) — 에셋 1개 = <c>Assets/Resources/MapScene/Zone2IntroCinematic.asset</c>.
/// · <b>시간</b>(샷 진입·머묾, 경고 대사 지연·조각·간격, 복귀) = xlsx <c>Cinematic</c> 시트(4구역 연출과 같은 시트, 구역만 다름).
/// · <b>문구</b> = 자막 표 <c>TUT_MAIN_Z02_ENTER</c>. · <b>구도</b> = 인스펙터(xlsx 제외).
/// · 화면(레터박스·비네팅·디밍·스킵 버튼 그림)·무전 캡슐 모양은 4구역 연출 에셋(<see cref="MapSceneZone4IntroData"/>) 것을 같이 쓴다.
/// 순서(명세 §4.2 고정): 조작 잠금 → 적 무리 샷 → <b>샷 도착 뒤</b> 경고 → 복귀 → 복귀가 끝나는 순간 G2 열림·존2 기상(첫 전투).
/// 에셋이 없으면 코드 기본값으로 돈다(경고 1회).
/// </summary>
[CreateAssetMenu(fileName = "Zone2IntroCinematic", menuName = "BeaverLobby/MapScene/Zone2 Intro Cinematic", order = 29)]
[DataTableSheet("Cinematic", Order = 1)]
public sealed class MapSceneZone2IntroData : ScriptableObject
{
    public const string ResourcesPath = "MapScene/Zone2IntroCinematic";

    [Header("켜기")]
    [Tooltip("끄면 예전 흐름 — 복도1 특성 선택이 끝나는 즉시 G2 가 열리고, 존2 첫 진입 4초 뒤 기상(연출·경고 무전 없음).")]
    [SerializeField] private bool introEnabled = true;

    [Header("샷 시간(초)")]
    [Tooltip("적 무리 샷 = [게임 카메라 → 적 무리 블렌드 → 머묾]. 머묾은 경고 대사(지연 + 조각들)가 다 끝난 뒤 조금 더 — 대사가 끝나기 전에 복귀하지 않게(테스트가 확인).")]
    [SerializeField] private CinematicShotTiming[] shots = { new CinematicShotTiming(1.2f, 5.4f) };

    [Tooltip("적 무리 샷 → 플레이어 카메라 블렌드(초). 블렌드가 끝나는 순간 G2 열림·존2 기상.")]
    [SerializeField, Min(0f)] private float returnBlendSeconds = 1.0f;

    [Header("경고 대사 — 시간(문구는 Strings 시트)")]
    [Tooltip("샷 0 에 카메라가 도착한 뒤 delaySeconds 에 시작(명세 §6.3 — 적 구도 도착 → 경고 순서). 문구 '잠깐, 저 녀석들 우리를 발견했어.|조심해.' = 2조각.")]
    [SerializeField] private CinematicLineTiming line =
        new CinematicLineTiming { lineId = TutorialGuideIds.MainZ2Enter, shotIndex = 0, delaySeconds = 0.4f, chunkSeconds = 2.2f, gapSeconds = 0.2f, holdUntilNextLine = true };

    [Tooltip("대사 마지막 조각이 끝난 뒤 복귀를 시작하기 전 최소 여유(초) — 테스트 기준값.")]
    [SerializeField, Min(0f)] private float lineTailSeconds = 0.4f;

    [Header("스킵")]
    [Tooltip("Space 를 이만큼 눌러야 스킵 동의(초). 4구역·보스 등장 연출과 같은 규칙(채운 표 유지·전원 동의).")]
    [SerializeField, Min(0.1f)] private float skipHoldSeconds = 1f;
    [Tooltip("전원 스킵하면 보던 자리에서 플레이어 카메라로 블렌드(초).")]
    [SerializeField, Min(0f)] private float skipReturnSeconds = 0.8f;

    [Header("카메라 구도(인스펙터 · 기획 확인 전 임시값)")]
    [DataTableIgnore, Tooltip("내려다보는 각(도). 게임 카메라 59.8°.")]
    [SerializeField, Range(5f, 85f)] private float pitch = 55f;
    [DataTableIgnore, Tooltip("세로 FOV(도). 게임 카메라 34.")]
    [SerializeField, Range(10f, 80f)] private float fov = 40f;
    [DataTableIgnore, Tooltip("적 무리 범위가 화면에 다 들어오는 거리 × 이 여유 배율.")]
    [SerializeField, Range(0.8f, 3f)] private float fitMargin = 1.3f;
    [DataTableIgnore, Tooltip("자동 거리의 최소·최대(m) — 적이 한두 마리면 너무 가깝지 않게.")]
    [SerializeField] private Vector2 distanceRange = new Vector2(14f, 40f);
    [DataTableIgnore, Tooltip("바라보는 점 = 적 무리 중심 + 이 높이(m).")]
    [SerializeField] private float focusHeight = 1f;
    [DataTableIgnore, Tooltip("적 무리 반경을 잴 때 몬스터가 하나도 없으면 쓸 반경(m) — 존2 구간 중심을 비춘다.")]
    [SerializeField, Min(1f)] private float fallbackRadius = 8f;

    public bool IntroEnabled => introEnabled;
    public CinematicShotTiming[] Shots => shots;
    public float ReturnBlendSeconds => returnBlendSeconds;
    public CinematicLineTiming Line => line;
    public float LineTailSeconds => lineTailSeconds;
    public float SkipHoldSeconds => skipHoldSeconds;
    public float SkipReturnSeconds => skipReturnSeconds;
    public float Pitch => pitch;
    public float Fov => fov;
    public float FitMargin => fitMargin;
    public Vector2 DistanceRange => distanceRange;
    public float FocusHeight => focusHeight;
    public float FallbackRadius => fallbackRadius;

    /// <summary>소개 전체 길이(초) — 샷 합. 서버는 이 시각에 복귀를 시작한다.</summary>
    public float IntroSeconds => CinematicTimeline.TotalSeconds(shots);

    /// <summary>경고 대사 시작 시각(연출 시작부터 초).</summary>
    public float LineStart => CinematicTimeline.ArrivalTime(shots, line.shotIndex) + line.delaySeconds;

    private static MapSceneZone2IntroData _cached;
    private static bool _warned;

    /// <summary>Resources 에셋(없으면 코드 기본값 인스턴스 — 경고 1회). 모든 피어가 같은 빌드면 같은 값.</summary>
    public static MapSceneZone2IntroData Load()
    {
        if (_cached != null) return _cached;
        _cached = Resources.Load<MapSceneZone2IntroData>(ResourcesPath);
        if (_cached == null)
        {
            if (!_warned) Edit.LogWarning($"[Zone2Intro] Resources/{ResourcesPath} 가 없다 — 코드 기본값으로 돈다.");
            _warned = true;
            _cached = CreateInstance<MapSceneZone2IntroData>();
            _cached.hideFlags = HideFlags.DontSave;
        }
        return _cached;
    }
}
