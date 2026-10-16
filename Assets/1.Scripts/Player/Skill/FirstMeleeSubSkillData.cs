using UnityEngine;

/// <summary>
/// E 수호자의 의지 설계값 — 전방 방어 후 자동 반격.
/// 반격 피해는 base의 AttackDamageMultiplier/FlatDamageBonus, 반격 대상 레이어는 base의 HittableLayers를 쓴다.
/// MaxActiveDuration(강제 종료 안전망)은 guardDuration + counterDuration 이상이어야 한다.
/// </summary>
[CreateAssetMenu(menuName = "Combat/Skills/FirstMelee Sub Skill Data (수호자의 의지)")]
[DataTableSheet("Paladin", Order = 3)]
public class FirstMeleeSubSkillData : PlayerSkillData
{
    [Header("수호자의 의지 — 방어")]
    [Tooltip("방어 대기 최대 시간(초). 이 안에 막지 못하면 E가 끝난다.")]
    [SerializeField, Min(0.05f)] private float guardDuration = 1f;
    [Tooltip("전방 방어 전체 각도(도, 바닥 평면). 180 = 좌우 90도.")]
    [SerializeField, Range(0f, 360f)] private float guardAngle = 180f;

    [Header("수호자의 의지 — 반격")]
    [Tooltip("반격 부채꼴 전체 각도(도, 바닥 평면). 방어 방향 기준.")]
    [SerializeField, Range(0f, 360f)] private float counterAngle = 120f;
    [Tooltip("반격 거리(m). 3D 구 반경 + 평면 각도 — 구 브로드페이즈 후 바닥 평면 각도로 거른다. 대상 콜라이더 표면 기준.")]
    [SerializeField, Min(0f)] private float counterRange = 3f;
    // 애니메이션 클립이 생기면 Hit/End 이벤트로 교체할 자리값 — 테이블 열로 노출하지 않는다
    [Tooltip("방어 성공부터 반격 판정까지(초). counterDuration 이하.")]
    [SerializeField, Min(0f), DataTableIgnore] private float counterHitDelay = 0.1f;
    [Tooltip("방어 성공부터 반격 종료(피해·넉백 무시 종료)까지(초).")]
    [SerializeField, Min(0f), DataTableIgnore] private float counterDuration = 0.4f;

    [Header("구 보호막 (미사용)")]
    // 미사용 — 병합 Export 때 제거. 지금 지우면 xlsx 열이 남아 테이블 Play·빌드가 오류를 낸다.
    [Tooltip("미사용 — 병합 Export 때 제거")]
    [SerializeField, Min(0)] private int shieldAmount = 10;
    [Tooltip("미사용 — 병합 Export 때 제거")]
    [SerializeField, Min(0f)] private float shieldDuration = 5f;

    public float GuardDuration => guardDuration;
    public float GuardAngle => guardAngle;
    public float CounterAngle => counterAngle;
    public float CounterRange => counterRange;
    public float CounterHitDelay => counterHitDelay;
    public float CounterDuration => counterDuration;

    public int ShieldAmount => shieldAmount;
    public float ShieldDuration => shieldDuration;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (counterHitDelay > counterDuration)
        {
            Debug.LogWarning($"[{name}] counterHitDelay({counterHitDelay})가 counterDuration({counterDuration})보다 큽니다 — counterDuration에 맞춥니다.", this);
            counterHitDelay = counterDuration;
        }

        if (MaxActiveDuration < guardDuration + counterDuration)
            Debug.LogWarning($"[{name}] maxActiveDuration({MaxActiveDuration})이 guardDuration + counterDuration({guardDuration + counterDuration})보다 작습니다 — 반격 전에 강제 종료될 수 있습니다.", this);
    }
#endif
}
