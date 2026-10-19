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
    // ⚠️ 미사용 2칸(PLAN-anim-event-timing P2-2) — 어쌔신 간파는 Attack_Up_01 의 Hit·End 이벤트가 판정·종료다(안전망 = maxActiveDuration).
    //    GameData.xlsx Assassin 시트에 행이 있어 지우면 테이블 모드가 막힌다 — 다음 병합 Export 때 행부터 정리하고 지운다.
    [Header("판정 타이밍 (미사용)")]
    [Tooltip("⚠️ 미사용 — 판정 시점은 Attack_Up_01 클립의 Hit 이벤트.")]
    [SerializeField, Min(0f)] private float hitDelay = 0.15f;
    [Tooltip("⚠️ 미사용 — 종료 시점은 Attack_Up_01 클립의 End 이벤트. 이벤트가 안 오면 maxActiveDuration 안전망.")]
    [SerializeField, Min(0.05f)] private float skillDuration = 0.6f;

    [Header("판정")]
    [SerializeField, Min(1), DataTableIgnore] private int maxHitResults = 8;

    public float HitDelay => hitDelay;
    public float SkillDuration => skillDuration;
    public int MaxHitResults => maxHitResults;
}
