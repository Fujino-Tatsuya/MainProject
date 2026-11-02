using System;
using UnityEngine;

/// <summary>무전 한 줄의 시간 — 사건부터 시작까지·조각(한 줄) 표시·조각 사이 간격. 문구는 자막 표(<c>TutorialGuideTexts</c> → xlsx Strings).</summary>
[Serializable]
public struct MapSceneRadioLineTiming
{
    [Tooltip("문구 id — TutorialGuideTexts 행(xlsx Strings 시트 키 Tutorial.Guide.<id>).")]
    public string lineId;
    [Tooltip("사건(서버)부터 무전이 시작될 때까지(초).")]
    [Min(0f)] public float delaySeconds;
    [Tooltip("한 조각(한 줄)을 보여 주는 시간(초). 문구의 | 와 최대 폭에서 조각이 나뉜다.")]
    [Min(0.1f)] public float chunkSeconds;
    [Tooltip("조각이 꺼지고 다음 조각이 나오기까지(초).")]
    [Min(0f)] public float gapSeconds;

    public MapSceneRadioLineTiming(string id, float delay, float chunk, float gap)
    {
        lineId = id;
        delaySeconds = delay;
        chunkSeconds = chunk;
        gapSeconds = gap;
    }
}

/// <summary>
/// 메인 1~4구역 <b>일반 무전</b> 데이터(PLAN-main-radio-zone1-4 §4.1) — 에셋 1개 = <c>Assets/Resources/MapScene/MainRadioLines.asset</c>.
/// · <b>시간</b> = xlsx <c>Radio</c> 시트(기획 — 빌드는 항상 xlsx 값). 줄 개수는 인스펙터에서만 늘린다(xlsx 는 배열 크기를 못 바꾼다).
/// · <b>문구</b> = 자막 표 행. 캡슐 모양(그림·위치·글꼴·움직임)은 4구역 연출 에셋(<see cref="MapSceneZone4IntroData"/>) 것을 같이 쓴다 — 한 곳에서 조절.
/// 여기 없는 id 를 틀면 <see cref="DefaultTiming"/>. 2구역 경고(<c>TUT_MAIN_Z02_ENTER</c>)는 연출 대사라 <see cref="MapSceneZone2IntroData"/> 가 시간을 든다.
/// 시작값 기준(§5): 한국어 낭독 ≈ 초당 7~8자 + 0.6초 → 한 조각 2.4~2.6초.
/// </summary>
[CreateAssetMenu(fileName = "MainRadioLines", menuName = "BeaverLobby/MapScene/Main Radio Lines", order = 31)]
[DataTableSheet("Radio")]
public sealed class MapSceneRadioData : ScriptableObject
{
    public const string ResourcesPath = "MapScene/MainRadioLines";

    [Tooltip("일반 무전 줄(사건 순서). 같은 id 는 판마다 한 번만 나온다(서버).")]
    [SerializeField] private MapSceneRadioLineTiming[] lines =
    {
        new MapSceneRadioLineTiming(TutorialGuideIds.MainZ1Enter, 1.0f, 2.6f, 0.25f),   // 로딩 걷힘 ∧ 포드 착지 끝(조작 복귀) 뒤
        new MapSceneRadioLineTiming(TutorialGuideIds.MainZ2Clear, 0.3f, 2.6f, 0.25f),
        new MapSceneRadioLineTiming(TutorialGuideIds.MainZ3Enemy, 0.3f, 2.8f, 0.25f),
        new MapSceneRadioLineTiming(TutorialGuideIds.MainZ3Clear, 0.3f, 2.6f, 0.25f),
        new MapSceneRadioLineTiming(TutorialGuideIds.Z4BridgeOpen, 0.3f, 2.4f, 0.25f),
        new MapSceneRadioLineTiming(TutorialGuideIds.Z4ExitOpen, 0.3f, 2.4f, 0.25f),
    };

    [Tooltip("표에 없는 id 의 시간.")]
    [SerializeField] private MapSceneRadioLineTiming defaultTiming = new MapSceneRadioLineTiming(null, 0.3f, 2.6f, 0.25f);

    [Header("3구역 — 강해 보이는 상대(스피너봇) 첫 인식")]
    [Tooltip("살아 있는 플레이어 누군가와 존3 핵심 몬스터의 거리(m)가 이 안이 된 첫 순간 TUT_MAIN_Z03_ENEMY 1회. 게임 카메라 화면 세로 절반 정도 — Play 로 맞춤.")]
    [SerializeField, Min(1f)] private float zone3SightDistance = 14f;

    public MapSceneRadioLineTiming[] Lines => lines;
    public MapSceneRadioLineTiming DefaultTiming => defaultTiming;
    public float Zone3SightDistance => zone3SightDistance;

    /// <summary>id 의 시간(없으면 기본값 — id 만 바꿔 준다).</summary>
    public MapSceneRadioLineTiming TimingOf(string id)
    {
        if (lines != null)
            foreach (MapSceneRadioLineTiming line in lines)
                if (line.lineId == id) return line;
        MapSceneRadioLineTiming fallback = defaultTiming;
        fallback.lineId = id;
        return fallback;
    }

    private static MapSceneRadioData _cached;
    private static bool _warned;

    /// <summary>Resources 에셋(없으면 코드 기본값 인스턴스 — 경고 1회). 모든 피어가 같은 빌드면 같은 값.</summary>
    public static MapSceneRadioData Load()
    {
        if (_cached != null) return _cached;
        _cached = Resources.Load<MapSceneRadioData>(ResourcesPath);
        if (_cached == null)
        {
            if (!_warned) Edit.LogWarning($"[Radio] Resources/{ResourcesPath} 가 없다 — 코드 기본값으로 돈다.");
            _warned = true;
            _cached = CreateInstance<MapSceneRadioData>();
            _cached.hideFlags = HideFlags.DontSave;
        }
        return _cached;
    }
}
