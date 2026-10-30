using UnityEngine;

/// <summary>
/// E 수호자의 의지 설계값 — 전방 방어 후 자동 반격.
/// 반격 피해는 base의 AttackDamageMultiplier/FlatDamageBonus, 반격 대상 레이어는 base의 HittableLayers를 쓴다.
/// 방어 창·반격 판정·반격 종료는 초 값이 아니라 클립 이벤트다(방어 시작 Custom0 · 방어 종료 Custom1, 반격 클립 Hit·End —
/// <see cref="FirstMeleeSubSkill"/>). 초 단위 값은 안전망 maxActiveDuration 하나만 남는다.
/// 🔴 maxActiveDuration 은 방어 종료 Custom1 시각 + 반격 클립 End 보다 길어야 한다
/// (할 일 91 A6 임시값, 공속 1: 준비 1.0s÷Speed 4 = 0.25 + 방어중 1.5 + 해제 0.25 ≈ 2.0 — 해제 Custom1 은 ≈ 1.78,
/// 창 닫히기 직전 방어 성공 시 + 반격 End 1.1 ≈ 2.9 → 안전망 4.0).
/// 짧으면 반격 중에 안전망이 E 를 끊는다(반격은 OnEnd 보장 반격으로 1회 나간다).
/// 홀드 방식 방어중은 길이가 guardHoldMaxDuration 까지 늘므로 실제 안전망 = maxActiveDuration + guardHoldMaxDuration 이다(코드가 더한다).
/// </summary>
[CreateAssetMenu(menuName = "Combat/Skills/FirstMelee Sub Skill Data (수호자의 의지)")]
[DataTableSheet("Paladin", Order = 3)]
public class FirstMeleeSubSkillData : PlayerSkillData
{
    [Header("수호자의 의지 — 방어")]
    [Tooltip("전방 방어 전체 각도(도, 바닥 평면). 180 = 좌우 90도.")]
    [SerializeField, Range(0f, 360f)] private float guardAngle = 180f;
    // 홀드 방식(설정 '방어 스킬: 홀드 방식', 할 일 95)만 쓴다. 탭 방식의 방어중 길이는 지금처럼 애니메이터 전이 exitTime 이다.
    // 안전망(maxActiveDuration)에는 이 값이 더해진다(FirstMeleeSubSkill.SafetyNetExtraSeconds).
    [Tooltip("홀드 방식 방어중 최대 유지 시간(초). E 를 누르고 있어도 방어중에 들어온 뒤 이 시간이 지나면 해제한다.\n" +
             "탭 방식에는 쓰지 않는다(방어중 길이 = 애니메이터 전이).")]
    [SerializeField, Min(0f)] private float guardHoldMaxDuration = 3f;

    [Header("수호자의 의지 — 반격")]
    [Tooltip("반격 판정 영역(할 일 153) — 기준점 = 반격 순간 시전자 발밑 + 고정 방어 방향(CasterAim). 예전 counterRange 3·counterAngle 120 부채꼴이 기본.")]
    [SerializeField] private AttackShapeSet hitArea = new AttackShapeSet(AttackShapeOrigin.CasterAim, CounterSector());
    // 미사용(할 일 153 — hitArea 로 옮김) — 병합 Export 때 제거. 지금 지우면 xlsx 행이 남아 테이블 Play·빌드가 오류를 낸다.
    [Tooltip("미사용 — hitArea 로 옮김, 병합 Export 때 제거")]
    [SerializeField, Range(0f, 360f)] private float counterAngle = 120f;
    [Tooltip("미사용 — hitArea 로 옮김, 병합 Export 때 제거")]
    [SerializeField, Min(0f)] private float counterRange = 3f;

    [Header("구 보호막 (미사용)")]
    // 미사용 — 병합 Export 때 제거. 지금 지우면 xlsx 열이 남아 테이블 Play·빌드가 오류를 낸다.
    [Tooltip("미사용 — 병합 Export 때 제거")]
    [SerializeField, Min(0)] private int shieldAmount = 10;
    [Tooltip("미사용 — 병합 Export 때 제거")]
    [SerializeField, Min(0f)] private float shieldDuration = 5f;

    public float GuardAngle => guardAngle;
    public float GuardHoldMaxDuration => guardHoldMaxDuration;
    public AttackShapeSet HitArea => hitArea;
    // 미사용 — 판정은 HitArea. 병합 Export 때 칸과 함께 제거.
    public float CounterAngle => counterAngle;
    public float CounterRange => counterRange;

    public int ShieldAmount => shieldAmount;
    public float ShieldDuration => shieldDuration;

    /// <summary>
    /// 반격 기본 판정 도형(할 일 153) — 예전 반격 부채꼴(counterRange 3·counterAngle 120, 대상 콜라이더 표면 기준·바닥 평면 각도)을 옮긴 값.
    /// 높이 ±3: 예전 넓은 질의가 발밑 중심 반경 3 m 의 3D 구라 그보다 높거나 낮은 대상은 못 맞았다(원통으로 근사 — 모서리만 조금 넓다).
    /// </summary>
    public static AttackSectorShape CounterSector() =>
        new AttackSectorShape(3f, 120f) { MinHeight = -3f, MaxHeight = 3f };
}
