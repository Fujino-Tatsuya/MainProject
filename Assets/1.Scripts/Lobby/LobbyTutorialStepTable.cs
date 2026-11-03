using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>로비 튜토리얼 단계 종류(할 일 157 PLAN §2.2) — 코드 분기는 이것과 <see cref="LobbyTutorialActivity"/> 로만 한다.</summary>
public enum LobbyTutorialStepKind : byte
{
    LockerVisit = 0,    // L00 — 참여자 전원 라커룸 누적 방문
    Gather = 1,         // 집결 구역 전원 동시 체류
    Dialogue = 2,       // 감독관 대사 — 서버 시각으로 끝난다
    Activity = 3,       // 훈련 내용 컴포넌트가 완료를 보고한다
    Loading = 4,        // L41 — 기존 로딩 흐름
}

/// <summary>지금 일하는 훈련 내용(PLAN §2.2). T50 은 Kind=Gather + FreePractice(출구 집결과 병행).</summary>
public enum LobbyTutorialActivity : byte
{
    None = 0,
    BasicCheck = 1,     // T10
    Combat = 2,         // T22
    Counter = 3,        // T31
    VulnerableCc = 4,   // T32
    ResetPosition = 5,  // T33
    Wall = 6,           // T34
    SuppressKill = 7,   // T40
    FreePractice = 8,   // T50
}

/// <summary>
/// 단계 동안 훈련 명단에 거는 보정 묶음(PLAN §2.3 TrainingOverrideProfile, 커밋 ②). 단계 진입·캐릭터 교체 때 진행기가 다시 맞춘다.
/// 쿨·대시 리셋은 각 내용 컴포넌트가 시작 순간 1회 건다(<see cref="TrainingResetOptions"/>) — 여기는 단계 내내 유지되는 값만.
/// </summary>
public enum LobbyTutorialTrainingProfile : byte
{
    None = 0,
    /// <summary>체력 하한 <see cref="LobbyTrainingSupportRules.TrainingHealthFloor"/>(죽지 않음).</summary>
    Training = 1,
}

/// <summary>로비 진행기가 켜고 끄는 집결 구역(씬의 <see cref="PartyGatherZone"/> 하나씩). 값은 단계표 에셋에 저장되므로 바꾸지 않는다.</summary>
public enum LobbyGatherPoint : byte
{
    None = 0,
    Entrance = 1,       // 훈련장 입구(문 앞) — L20 집결, 확정 = 명단 전원 훈련장 안(Train01Start)으로 텔레포트
    Sortie = 2,         // 출격 — 메인 진입(옛 Ready 패드 자리)
    Train01Start = 3,   // 훈련장 안 시작 위치(L20 텔레포트 도착지)
    Train02Start = 4,
    Train03Area = 5,
    TrainEnd = 6,       // 훈련 종료(출구) — T50, 확정 = 명단 전원 중앙(Brief)으로 텔레포트
    Brief = 7,          // 로비 중앙 — L10·L30 집결(이름은 158 코드 호환을 위해 유지)
}

/// <summary>목표 패널 하단 진행 줄(158 표시용).</summary>
public enum LobbyTutorialProgressMode : byte
{
    None = 0,
    Gather = 1,         // 집결 n/N
    PartyComplete = 2,  // 훈련 완료 n/N(T10)
    Countdown = 3,      // 시작까지 n — 행에 직접 쓰지 않고 Gather 행의 카운트다운 중 표시 상태로 나온다
}

/// <summary>
/// 단계표 한 행(명세 §9 권장 필드, PLAN §2.3). 문구 키는 <c>Tutorial.Guide.</c> 접두 뒤 id — <see cref="TutorialGuideTextSource"/> 가 해석한다.
/// 행 순서 = 진행 순서(분기가 없어 다음 단계 칸을 두지 않는다).
/// </summary>
[Serializable]
public sealed class LobbyTutorialStep
{
    [Tooltip("문서·로그·158 표시용 상태 ID(예 L00_LockerVisit). 표 안에서 유일.")]
    public string stateId;
    public LobbyTutorialStepKind kind;
    [Tooltip("Kind=Activity 일 때 필수. Gather 행에도 병행 내용(T50 FreePractice)을 둘 수 있다.")]
    public LobbyTutorialActivity activity;

    [Header("목표 패널")]
    public string titleKey;
    public string objectiveKey;
    public string[] bodyKeys = Array.Empty<string>();
    [Tooltip("T10 개인 체크 항목 — 순서 = LobbyWeaponCheckItem 값 순서.")]
    public string[] checklistKeys = Array.Empty<string>();
    public LobbyTutorialProgressMode progressMode;
    public string progressKey;

    [Header("카운트다운 표시 상태(T20 → T21)")]
    [Tooltip("활성 집결 구역에 카운트다운이 돌면 이 ID 가 표시 상태가 된다. 비면 없음.")]
    public string countdownStateId;
    public string countdownObjectiveKey;
    public string countdownProgressKey;

    [Header("집결")]
    public LobbyGatherPoint gatherPoint;
    [Tooltip("집결 유지 시간(초, 서버 시각). T20 만 3 = 시작 카운트다운.")]
    [Min(0f)]
    public float holdSeconds = 0.3f;

    [Header("표식(158)")]
    [Tooltip("바닥 경로 키. 비면 경로 없음.")]
    public string pathId;
    [Tooltip("지정 벽 키(T34). 비면 벽 표식 없음.")]
    public string targetWallId;

    [Header("대사")]
    public string dialogueId;
    public string speakerKey;
    [Tooltip("브리핑 삽화 키. 비면 로비 배경.")]
    public string backgroundId;
    [Tooltip("0 이하 = TutorialGuideTexts 의 표시 시간 → 글자 수 규칙.")]
    public float dialogueSeconds;
    [Tooltip("브리핑 입력 잠금 묶음(L31~L34).")]
    public bool locksInput;

    [Header("판정")]
    [Tooltip("T32·T34 — 인정할 스킬 툴팁 태그. 표의 enforceRequiredSkillTags 가 꺼져 있으면 검사하지 않는다(할 일 157 Q12a).")]
    public SkillTooltipTag[] requiredSkillTags = Array.Empty<SkillTooltipTag>();

    [Header("훈련 보정")]
    [Tooltip("이 단계 동안 훈련 명단에 거는 보정(체력 하한 등). 늦은 참가자·명단 밖은 받지 않는다.")]
    public LobbyTutorialTrainingProfile trainingProfile;
}

/// <summary>
/// [데이터] 로비 튜토리얼 단계표 — 할 일 157 PLAN §2.3, 명세 §9. 모든 피어가 같은 에셋을 읽으므로 네트워크엔 행 번호(<see cref="LobbyTutorialSnapshot.StepIndex"/>)만 싣는다.
/// 행 검증은 <see cref="Validate"/>(EditMode 테스트가 쓴다).
/// </summary>
[CreateAssetMenu(fileName = "LobbyTutorialSteps", menuName = "Lobby/Lobby Tutorial Step Table")]
public sealed class LobbyTutorialStepTable : ScriptableObject
{
    /// <summary>스냅샷의 byte 행 번호 한계.</summary>
    public const int MaxSteps = byte.MaxValue;

    [SerializeField] private LobbyTutorialStep[] steps = Array.Empty<LobbyTutorialStep>();

    [Tooltip("T32·T34 에서 requiredSkillTags 를 검사한다. 스킬 데이터 tooltip.tags 가 비어 있어 기본 꺼짐(할 일 157 Q12a) — 태그가 채워지면 켠다.")]
    [SerializeField] private bool enforceRequiredSkillTags;

    public IReadOnlyList<LobbyTutorialStep> Steps => steps;
    public int Count => steps != null ? steps.Length : 0;
    public bool EnforceRequiredSkillTags => enforceRequiredSkillTags;

    public LobbyTutorialStep this[int index] => steps[index];

    public bool TryGet(int index, out LobbyTutorialStep step)
    {
        step = steps != null && index >= 0 && index < steps.Length ? steps[index] : null;
        return step != null;
    }

    /// <summary>상태 ID 의 행 번호. 없으면 -1.</summary>
    public int IndexOf(string stateId)
    {
        if (steps == null || string.IsNullOrEmpty(stateId))
            return -1;

        for (var i = 0; i < steps.Length; i++)
        {
            if (steps[i] != null && steps[i].stateId == stateId)
                return i;
        }

        return -1;
    }

    /// <summary>행 종류 목록 — 순수 모델(<see cref="LobbyTutorialFlow"/>) 생성용.</summary>
    public LobbyTutorialStepKind[] Kinds()
    {
        var kinds = new LobbyTutorialStepKind[Count];
        for (var i = 0; i < kinds.Length; i++)
            kinds[i] = steps[i] != null ? steps[i].kind : LobbyTutorialStepKind.Activity;
        return kinds;
    }

#if UNITY_EDITOR
    /// <summary>에디터 전용 — 행 통째 교체(에셋 생성 도구·테스트).</summary>
    public void EditorSetSteps(LobbyTutorialStep[] newSteps) => steps = newSteps ?? Array.Empty<LobbyTutorialStep>();
#endif

    /// <summary>행 검증 — 문제를 <paramref name="errors"/> 에 더하고 없으면 true.</summary>
    public bool Validate(List<string> errors) => Validate(steps, errors);

    /// <summary>
    /// 행 검증 규칙: 1행 이상·<see cref="MaxSteps"/> 이하, 상태 ID 비지 않고 유일, Gather → 집결 지점·유지 시간 ≥ 0,
    /// Dialogue → 대사 ID, Activity → Activity ≠ None, LockerVisit 은 첫 행, 카운트다운 표시 상태는 Gather 행만·지시문 키 필요.
    /// </summary>
    public static bool Validate(IReadOnlyList<LobbyTutorialStep> rows, List<string> errors)
    {
        var start = errors.Count;
        if (rows == null || rows.Count == 0)
        {
            errors.Add("단계표가 비었다.");
            return false;
        }

        if (rows.Count > MaxSteps)
            errors.Add($"행 {rows.Count}개 — 최대 {MaxSteps}.");

        var ids = new HashSet<string>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row == null)
            {
                errors.Add($"[{i}] 행이 null.");
                continue;
            }

            var label = $"[{i}] {row.stateId}";
            if (string.IsNullOrWhiteSpace(row.stateId))
                errors.Add($"[{i}] stateId 비었다.");
            else if (!ids.Add(row.stateId))
                errors.Add($"{label} stateId 중복.");

            switch (row.kind)
            {
                case LobbyTutorialStepKind.Gather:
                    if (row.gatherPoint == LobbyGatherPoint.None)
                        errors.Add($"{label} Gather 인데 gatherPoint 없음.");
                    if (row.holdSeconds < 0f)
                        errors.Add($"{label} holdSeconds 음수.");
                    break;
                case LobbyTutorialStepKind.Dialogue:
                    if (string.IsNullOrWhiteSpace(row.dialogueId))
                        errors.Add($"{label} Dialogue 인데 dialogueId 없음.");
                    break;
                case LobbyTutorialStepKind.Activity:
                    if (row.activity == LobbyTutorialActivity.None)
                        errors.Add($"{label} Activity 인데 activity 없음.");
                    break;
                case LobbyTutorialStepKind.LockerVisit:
                    if (i != 0)
                        errors.Add($"{label} LockerVisit 은 첫 행만.");
                    break;
            }

            if (!string.IsNullOrEmpty(row.countdownStateId))
            {
                if (row.kind != LobbyTutorialStepKind.Gather)
                    errors.Add($"{label} countdownStateId 는 Gather 행만.");
                if (string.IsNullOrWhiteSpace(row.countdownObjectiveKey))
                    errors.Add($"{label} countdownObjectiveKey 없음.");
            }
        }

        return errors.Count == start;
    }
}
