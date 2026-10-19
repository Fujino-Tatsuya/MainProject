using UnityEngine;

/// <summary>
/// R 최후의 심판 설계값. 피해·종료 시각은 클립 이벤트(Hit·End)가 정하고, 안전망·슈퍼아머 길이는 클립 실제 재생 시간이다.
/// base의 MaxActiveDuration 은 클립 길이를 런타임에 못 구했을 때의 대체값으로만 남는다. 사거리는 base의 CastRange,
/// 피해는 base의 AttackDamageMultiplier/FlatDamageBonus를 사용한다(스냅샷 = 최종 공격력 × 계수).
/// TargetingMode는 에셋에서 SingleTarget으로, TargetableLayers는 Enemy로 설정한다.
/// 궁극 고유 추가 튜닝이 생기면 여기 필드를 추가한다.
/// </summary>
[CreateAssetMenu(menuName = "Combat/Skills/FirstMelee Ultimate Skill Data (최후의 심판)")]
[DataTableSheet("Paladin", Order = 5)]
public class FirstMeleeUltimateSkillData : PlayerSkillData
{
    // 🔴 둘 다 연출 정렬 값이라 테이블에 내보내지 않는다(인스펙터에서 바로 고친다).
    //    피해·종료 시각은 클립 이벤트라 animationSpeed 를 같이 탄다. 안전망(클립 길이 ÷ 배율 + 0.1)도 같이 줄어든다.
    [Header("연출 속도")]
    [Tooltip("궁극기 애니메이션 재생 속도 배율. 1 = 클립 원래 속도.\n" +
             "Animator 상태 Garen_Ultimate_Skill 의 속도 파라미터 UltimateSpeed 로 들어간다(다른 상태는 영향 없음).\n" +
             "클립 이벤트(칼 꽂기 Stick·마무리 낙뢰 Custom0·피해 Hit·종료 End)도 같이 당겨진다: 클립 1.2초 → 1.2배면 실제 1.0초.\n" +
             "안전망 = 클립 길이 ÷ 배율 + 0.1 이라 End 를 클립 안 어디에 두어도 안전망이 먼저 끊지 않는다")]
    [SerializeField, Min(0.1f), DataTableIgnore] private float animationSpeed = 1.2f;

    [Tooltip("궁극기 이펙트 재생 속도 배율. 1 = 저작 그대로.\n" +
             "칼 낙뢰·칼날 전기·발밑 표식·칼 꽂기(Stick)·마무리 낙뢰 전부에 걸린다.\n" +
             "파티클 simulationSpeed(저작값에 곱)·startDelay·엔트리 파트 delay·수명이 함께 빨라진다")]
    [SerializeField, Min(0.1f), DataTableIgnore] private float vfxPlayRate = 1.2f;

    public float AnimationSpeed => animationSpeed;
    public float VfxPlayRate => vfxPlayRate;
}
