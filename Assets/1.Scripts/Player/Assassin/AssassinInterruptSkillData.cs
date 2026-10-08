using UnityEngine;

/// <summary>
/// 어쌔신 우클릭 간파 수치(character_assassin.md §11, PLAN-assassin A10). 암살자 전용 수치는 없다 —
/// 저작 메뉴가 처음 만들 때 가붕이 간파 SO(FirstMeleeInterruptSkillData)의 값을 그대로 복사한다. 이후 튜닝은 이 에셋에서.
/// 클래스를 따로 둔 것은 데이터 테이블 시트를 Assassin 으로 내보내기 위해서다(필드는 가붕이 간파와 같다).
/// </summary>
[CreateAssetMenu(fileName = "AssassinInterruptSkillData", menuName = "Player/Assassin/Interrupt Skill Data")]
[DataTableSheet("Assassin", Order = 7)]
public sealed class AssassinInterruptSkillData : PlayerSkillData, IPlayerInterruptSkillData
{
    [Header("판정 타이밍")]
    [Tooltip("시전 → 판정까지(초). 클립에 Hit 이벤트가 있으면 먼저 온 쪽 1회.")]
    [SerializeField, Min(0f)] private float hitDelay = 0.15f;
    [Tooltip("스킬 종료(초). End 이벤트가 먼저 오면 그쪽. MaxActiveDuration 보다 작아야 한다.")]
    [SerializeField, Min(0.05f)] private float skillDuration = 0.6f;

    [Header("판정")]
    [SerializeField, Min(1), DataTableIgnore] private int maxHitResults = 8;

    public float HitDelay => hitDelay;
    public float SkillDuration => skillDuration;
    public int MaxHitResults => maxHitResults;
}
