using UnityEngine;

/// <summary>
/// R 최후의 심판 설계값. 채널 시간은 base의 MaxActiveDuration, 사거리는 base의 CastRange,
/// 피해는 base의 AttackDamageMultiplier/FlatDamageBonus를 사용한다(스냅샷 = 최종 공격력 × 계수).
/// TargetingMode는 에셋에서 SingleTarget으로, TargetableLayers는 Enemy로 설정한다.
/// 궁극 고유 추가 튜닝이 생기면 여기 필드를 추가한다.
/// </summary>
[CreateAssetMenu(menuName = "Combat/Skills/FirstMelee Ultimate Skill Data (최후의 심판)")]
[DataTableSheet("Paladin", Order = 5)]
public class FirstMeleeUltimateSkillData : PlayerSkillData
{
    // 🔴 둘 다 연출 정렬 값이라 테이블에 내보내지 않는다(인스펙터에서 바로 고친다).
    //    채널 시간(maxActiveDuration)·피해 시각은 이 값과 무관하다 — 빨라지는 것은 보이는 것뿐이다.
    [Header("연출 속도")]
    [Tooltip("궁극기 애니메이션 재생 속도 배율. 1 = 클립 원래 속도.\n" +
             "Animator 상태 Garen_Ultimate_Skill 의 속도 파라미터 UltimateSpeed 로 들어간다(다른 상태는 영향 없음).\n" +
             "클립 이벤트(칼 꽂기 Stick)도 같이 당겨진다: 클립 1.2초 → 1.2배면 실제 1.0초")]
    [SerializeField, Min(0.1f), DataTableIgnore] private float animationSpeed = 1.2f;

    [Tooltip("궁극기 이펙트 재생 속도 배율. 1 = 저작 그대로.\n" +
             "칼 낙뢰·칼날 전기·발밑 표식·칼 꽂기(Stick)·마무리 낙뢰 전부에 걸린다.\n" +
             "파티클 simulationSpeed(저작값에 곱)·startDelay·엔트리 파트 delay·수명이 함께 빨라진다")]
    [SerializeField, Min(0.1f), DataTableIgnore] private float vfxPlayRate = 1.2f;

    [Tooltip("마무리 낙뢰(하늘에서 칼이 떨어지는 FX_Lightening_Strike)를 채널 완주보다 몇 초 앞서 쏠지.\n" +
             "칼이 프리팹 delay + 낙하 시간 뒤에 닿으므로, 앞당겨 피해 시각에 닿게 한다. 0 = 완주 순간(예전).\n" +
             "피해 시각·채널 길이는 그대로다. 채널 길이(maxActiveDuration)를 넘으면 채널 길이로 자른다.\n" +
             "앞당긴 구간에 채널이 끊기면(대상 소실·사망·취소) 피해가 없으니 낙뢰를 걷는다")]
    [SerializeField, Min(0f), DataTableIgnore] private float finalStrikeVfxLead = 0.5f;

    public float AnimationSpeed => animationSpeed;
    public float VfxPlayRate => vfxPlayRate;
    public float FinalStrikeVfxLead => FinalStrikeVfxSchedule.ClampLead(finalStrikeVfxLead, MaxActiveDuration);
}
