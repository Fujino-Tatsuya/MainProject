using UnityEngine;

/// <summary>
/// 거너 우클릭 근접 간파 수치(character_gunner.md §8·§12.5). 베이스의 cooldownTime·hittableLayers·animatorStateName 도 쓴다.
/// 간파 유효 구간·전방 120도는 보스 쪽 데이터다(§12.5 끝) — 여기 없다.
/// </summary>
[CreateAssetMenu(fileName = "GunnerInterruptData", menuName = "Player/Gunner/Interrupt Data")]
[DataTableSheet("Gunner", Order = 4)]
public class GunnerInterruptData : PlayerSkillData, IPlayerInterruptSkillData
{
    // ⚠️ 미사용 2칸(PLAN-anim-event-timing P3-2 ①) — 거너 간파는 gunner_skill_RMB_interrupt 의 Hit·End 이벤트가 판정·종료·후폭풍 출발이다
    //    (안전망 = maxActiveDuration). GameData.xlsx Gunner 시트에 행이 있어 지우면 테이블 모드가 막힌다 — 다음 병합 Export 때 행부터 정리하고 지운다.
    [Header("판정 타이밍 (미사용)")]
    [Tooltip("⚠️ 미사용 — 판정·후폭풍 출발 시점은 gunner_skill_RMB_interrupt 클립의 Hit 이벤트.")]
    [SerializeField, Min(0f)] private float hitDelay = 0.2f;
    [Tooltip("⚠️ 미사용 — 종료 시점은 gunner_skill_RMB_interrupt 클립의 End 이벤트. 이벤트가 안 오면 maxActiveDuration 안전망.")]
    [SerializeField, Min(0.05f)] private float skillDuration = 0.7f;
    [SerializeField, Min(1), DataTableIgnore] private int maxHitResults = 8;

    [Header("후폭풍 이동 — 클립 Hit 순간부터, 적중 여부 무관(§8.5)")]
    [SerializeField, Min(0f)] private float recoilDistance = 1.5f;
    [SerializeField, Min(0.01f)] private float recoilDuration = 0.2f;

    public float HitDelay => hitDelay;
    public float SkillDuration => skillDuration;
    public int MaxHitResults => maxHitResults;
    public float RecoilDistance => recoilDistance;
    public float RecoilDuration => recoilDuration;
}
