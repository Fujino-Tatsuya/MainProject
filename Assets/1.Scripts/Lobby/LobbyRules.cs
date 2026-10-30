using UnityEngine;

/// <summary>
/// 로비 수치 — PLAN-48 §4.6. <see cref="LobbySession"/>(카운트다운·교체 쿨다운)과 C10 상호작용(반경), <see cref="LobbyTrainingDirector"/>(집결 유지 시간)가 읽는다.
/// 아직 데이터 테이블(xlsx) 대상이 아니다([DataTableSheet] 없음). 시트로 옮길 땐 열 = countdownSeconds · interactRadius · characterSwapCooldownSeconds · gatherHoldSeconds.
/// </summary>
[CreateAssetMenu(fileName = "LobbyRules", menuName = "Lobby/Lobby Rules")]
public sealed class LobbyRules : ScriptableObject
{
    [Tooltip("전원 준비 후 게임 시작까지(초, 서버 ServerTime 기준).")]
    [Min(0f)]
    [SerializeField] private float countdownSeconds = 5f;

    [Tooltip("준비·캐릭터 선택 오브젝트 상호작용 반경(m). C10 LobbyInteractor·서버 거리 재검증이 쓴다.")]
    [Min(0f)]
    [SerializeField] private float interactRadius = 2f;

    [Tooltip("캐릭터 교체 요청 사이 최소 간격(초). 서버가 거부한다.")]
    [Min(0f)]
    [SerializeField] private float characterSwapCooldownSeconds = 0.5f;

    [Tooltip("(미사용 — 할 일 157 ② 이후 집결 유지 시간은 단계표 행 holdSeconds) 옛 전 구역 공통 집결 유지 시간. 칸은 에셋 호환으로 남긴다.")]
    [Min(0f)]
    [SerializeField] private float gatherHoldSeconds = (float)PartyGatherRules.DefaultHoldSeconds;

    [Header("로비 튜토리얼(할 일 157)")]
    [Tooltip("로비 튜토리얼 단계표(LobbyTutorialSteps). 진행기·158 표시가 모든 피어에서 같은 에셋을 읽는다 — 비면 진행기가 멈춘다.")]
    [SerializeField] private LobbyTutorialStepTable stepTable;

    [Tooltip("시연용 — 훈련 없이 즉시 출격(Q2 B). 켜면 명단 고정 전(L00·L10)에도 출격 집결 구역이 켜지고, 확정하면 훈련을 건너뛰고 바로 메인 진입. 기본 꺼짐. DEV_HOTKEYS Alt+1 과 별개.")]
    [SerializeField] private bool allowInstantSortie;

    [Header("로비 튜토리얼 대사 길이(할 일 157 Q5) — 행 dialogueSeconds > 0 → 문구 표 durationSeconds → 글자 수 규칙, + 끝 여유")]
    [Tooltip("대사 문구·표시 시간 출처(TutorialGuideTexts). 서버가 대사 단계 길이를 정할 때 읽는다(Strings 시트 문구 우선, 없으면 SO). 비면 글자 수 0 으로 본다.")]
    [SerializeField] private TutorialGuideTextTable guideTexts;

    [Tooltip("글자 수 규칙 기본 초.")]
    [Min(0f)]
    [SerializeField] private float dialogueBaseSeconds = 2f;

    [Tooltip("글자 수 규칙 — 글자(공백 제외)당 초.")]
    [Min(0f)]
    [SerializeField] private float dialoguePerCharSeconds = 0.08f;

    [Tooltip("글자 수 규칙 최소 초.")]
    [Min(0f)]
    [SerializeField] private float dialogueMinSeconds = 3f;

    [Tooltip("대사 끝 여유(초) — 어느 출처든 더한다.")]
    [Min(0f)]
    [SerializeField] private float dialogueEndPaddingSeconds = 0.5f;

    [Header("로비 튜토리얼 전투(할 일 157 ⑥)")]
    [Tooltip("T22 전투 훈련 상대 구성(LobbyCombatSpawnSet, 6마리 — Q9). 기준점은 씬 Train02Spawns. 비면 옛 존2 구성(Train02Spawns 의 source).")]
    [SerializeField] private LobbyCombatSpawnSet combatSpawnSet;

    public LobbyCombatSpawnSet CombatSpawnSet => combatSpawnSet;
    public TutorialGuideTextTable GuideTexts => guideTexts;
    public float DialogueBaseSeconds => dialogueBaseSeconds;
    public float DialoguePerCharSeconds => dialoguePerCharSeconds;
    public float DialogueMinSeconds => dialogueMinSeconds;
    public float DialogueEndPaddingSeconds => dialogueEndPaddingSeconds;

    public float CountdownSeconds => countdownSeconds;
    public float GatherHoldSeconds => gatherHoldSeconds;
    public LobbyTutorialStepTable StepTable => stepTable;
    public bool AllowInstantSortie => allowInstantSortie;

    /// <summary>T32·T34 스킬 태그(RequiredSkillTags) 검사 — 원본은 단계표의 <see cref="LobbyTutorialStepTable.EnforceRequiredSkillTags"/>(기본 꺼짐, Q12a). 여기선 읽기만.</summary>
    public bool EnforceRequiredSkillTags => stepTable != null && stepTable.EnforceRequiredSkillTags;
    public float InteractRadius => interactRadius;
    public float CharacterSwapCooldownSeconds => characterSwapCooldownSeconds;
}
