using UnityEngine;

/// <summary>
/// 거너 우클릭 근접 간파 수치(character_gunner.md §8·§12.5). 베이스의 cooldownTime·hittableLayers·animatorStateName 도 쓴다.
/// 간파 유효 구간·전방 120도는 보스 쪽 데이터다(§12.5 끝) — 여기 없다.
/// </summary>
[CreateAssetMenu(fileName = "GunnerInterruptData", menuName = "Player/Gunner/Interrupt Data")]
[DataTableSheet]
public class GunnerInterruptData : PlayerSkillData
{
    [Header("판정 타이밍")]
    [Tooltip("시전 → 총구가 맞닿는 판정까지(초). 클립에 Hit 이벤트가 있으면 그쪽이 먼저.")]
    [SerializeField, Min(0f)] private float hitDelay = 0.2f;
    [Tooltip("스킬 종료(초). MaxActiveDuration 보다 작아야 한다.")]
    [SerializeField, Min(0.05f)] private float skillDuration = 0.7f;
    [SerializeField, Min(1), DataTableIgnore] private int maxHitResults = 8;

    [Header("후폭풍 이동 — 판정 순간부터, 적중 여부 무관(§8.5)")]
    [SerializeField, Min(0f)] private float recoilDistance = 1.5f;
    [SerializeField, Min(0.01f)] private float recoilDuration = 0.2f;

    public float HitDelay => hitDelay;
    public float SkillDuration => skillDuration;
    public int MaxHitResults => maxHitResults;
    public float RecoilDistance => recoilDistance;
    public float RecoilDuration => recoilDuration;
}
