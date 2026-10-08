using UnityEngine;

/// <summary>
/// 어쌔신 R 스택·변신 지속·수동 해제 제한 수치(character_assassin.md §4.2·§10.2, 임시값).
/// 패시브(백어택 + R 스택) P 칸 툴팁 출처도 겸한다 — <see cref="AssassinState"/> 가 <see cref="IPassiveTooltipProvider"/> 로 내보낸다.
/// </summary>
[CreateAssetMenu(fileName = "AssassinStateData", menuName = "Player/Assassin/State Data")]
[DataTableSheet("Assassin", Order = 1)]
public sealed class AssassinStateData : ScriptableObject, ISkillTooltipSource
{
    [Header("툴팁 — 패시브(백어택·R 스택)")]
    [SerializeField] private SkillTooltipText tooltip;

    [SerializeField, Min(1)] private int maxStacks = 4;

    [Tooltip("소모 스택 1/2/3/4개의 변신 지속시간(초). 원소 수가 상한보다 적으면 마지막 값을 쓴다.")]
    [SerializeField] private float[] transformDurations = { 4f, 6f, 8f, 10f };

    [Tooltip("변신 시작 후 이 시간 동안 R 재입력(수동 해제)을 무시한다.")]
    [SerializeField, Min(0f)] private float releaseLockSeconds = 2f;

    [Tooltip("백어택 피해 배율(§4.1, 임시값). 어쌔신 모든 타격에 실린다 — 적용 여부(보스 후방·변신 강제)는 MonsterBase 가 정한다.")]
    [SerializeField, Min(1f)] private float backAttackMultiplier = 1.2f;

    public float BackAttackMultiplier => backAttackMultiplier;

    public AssassinStateRules Rules => new AssassinStateRules(maxStacks, transformDurations, releaseLockSeconds);

    public SkillTooltipText Tooltip => tooltip;
    public Object TooltipValueSource => this;

    // 패시브 자체 피해는 없다(백어택 배율은 각 타격에 실린다 — A11).
    public SkillTooltipDamage GetTooltipDamage(Player player) => default;
}
