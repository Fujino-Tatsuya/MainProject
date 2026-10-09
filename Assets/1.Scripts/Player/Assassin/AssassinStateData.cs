using UnityEngine;

/// <summary>
/// 어쌔신 분노 게이지·변신 수동 해제 제한 수치(character_assassin.md §4.2·§10.2, 임시값).
/// 패시브(백어택 + 분노 게이지) P 칸 툴팁 출처도 겸한다 — <see cref="AssassinState"/> 가 <see cref="IPassiveTooltipProvider"/> 로 내보낸다.
/// </summary>
[CreateAssetMenu(fileName = "AssassinStateData", menuName = "Player/Assassin/State Data")]
[DataTableSheet("Assassin", Order = 1)]
public sealed class AssassinStateData : ScriptableObject, ISkillTooltipSource
{
    [Header("툴팁 — 패시브(백어택·분노 게이지)")]
    [SerializeField] private SkillTooltipText tooltip;

    [Header("분노 게이지")]
    [Tooltip("분노 게이지 최대치. 넘는 충전은 버린다.")]
    [SerializeField, Min(1f)] private float maxRage = 100f;

    [Tooltip("R 변신에 필요한 최소 게이지. 변신하면 보유 게이지 전부가 연료가 된다.")]
    [SerializeField, Min(0f)] private float minTransformRage = 40f;

    [Tooltip("일반 기본 공격 1타가 유효 대상(보스·몹·송전기·허수아비)에 적중했을 때 충전량. 여러 대상이어도 1번.")]
    [SerializeField, Min(0f)] private float basicAttackRageGain = 3f;

    [Tooltip("일반 Q 1회가 유효 대상에 적중했을 때 충전량. 여러 대상이어도 1번.")]
    [SerializeField, Min(0f)] private float dashStrikeRageGain = 5f;

    [Tooltip("일반 E 강타 1회가 유효 대상에 적중했을 때 충전량. 여러 대상이어도 1번.")]
    [SerializeField, Min(0f)] private float enhancedStrikeRageGain = 15f;

    [Tooltip("변신 중 초당 게이지 감소량. 변신 지속시간 = 시작 게이지 ÷ 이 값. 0 이 되면 시간 만료.")]
    [SerializeField, Min(0.01f)] private float transformRageDecayPerSecond = 10f;

    [Header("변신")]
    [Tooltip("변신 시작 후 이 시간 동안 R 재입력(수동 해제)을 무시한다.")]
    [SerializeField, Min(0f)] private float releaseLockSeconds = 2f;

    [Tooltip("백어택 피해 배율(§4.1, 임시값). 어쌔신 모든 타격에 실린다 — 적용 여부(보스 후방·변신 강제)는 MonsterBase 가 정한다.")]
    [SerializeField, Min(1f)] private float backAttackMultiplier = 1.2f;

    public float BackAttackMultiplier => backAttackMultiplier;

    public AssassinStateRules Rules => new AssassinStateRules(
        maxRage, minTransformRage,
        basicAttackRageGain, dashStrikeRageGain, enhancedStrikeRageGain,
        transformRageDecayPerSecond, releaseLockSeconds);

    public SkillTooltipText Tooltip => tooltip;
    public Object TooltipValueSource => this;

    // 패시브 자체 피해는 없다(백어택 배율은 각 타격에 실린다 — A11).
    public SkillTooltipDamage GetTooltipDamage(Player player) => default;
}
