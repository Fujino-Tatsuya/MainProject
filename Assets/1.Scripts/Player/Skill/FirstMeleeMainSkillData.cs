using UnityEngine;

/// <summary>
/// Q 진격의 방패 설계값. 수치는 전부 임시값 — 기획 확정 시 에셋에서만 조절한다.
/// 베이스(PlayerSkillData)의 InputType은 Hold, TickInterval은 0보다 커야 한다.
/// </summary>
[CreateAssetMenu(menuName = "Combat/First Melee Main Skill Data")]
[DataTableSheet("Paladin", Order = 2)]
public class FirstMeleeMainSkillData : PlayerSkillData
{
    [Header("진격")]
    [SerializeField, Min(0f)] private float advanceSpeed = 6f;
    // 스킬 틱(TickInterval)당 에임 방향으로 조향되는 최대 각도(도). 프레임에서는 시간 비례로 나눠 적용된다.
    [SerializeField, Min(0f)] private float steerAnglePerTick = 5f;

    [Header("견인 (AttackInfo로 전달 — 수신측 MonsterBase가 지속넉백→경직 시퀀스 처리)")]
    [SerializeField, Min(0f)] private float knockbackStrength = 3f;   // 지속 밀기 속도(m/s)
    // 틱당 재적용되므로 TickInterval보다 약간 길게 — 틱 사이에 밀림이 끊기지 않게.
    [SerializeField, Min(0f)] private float knockbackDuration = 0.25f;
    // 마지막 틱의 넉백 종료 후 Stunned 경직(0.1~1 튜닝, PLAN C 스펙 기본 0.2).
    [SerializeField, Min(0f)] private float staggerDuration = 0.2f;
    [SerializeField, Min(1), DataTableIgnore] private int maxHitResults = 16;

    [Header("판정")]
    [Tooltip("틱 판정 영역(할 일 153) — 기준점 = 시전자 발밑 + 진행 방향(CasterAim). 돌파의 방패는 같은 도형을 돌진 경로 표본 지점마다 옮겨 훑는다.\n" +
             "예전 프리팹 앵커 Armature/MainSkill 상자를 옮긴 직사각형이 기본.")]
    [SerializeField] private AttackShapeSet hitArea = new AttackShapeSet(AttackShapeOrigin.CasterAim, ShieldPushRect());

    public float AdvanceSpeed => advanceSpeed;
    public float SteerAnglePerTick => steerAnglePerTick;
    public float KnockbackStrength => knockbackStrength;
    public float KnockbackDuration => knockbackDuration;
    public float StaggerDuration => staggerDuration;
    public int MaxHitResults => maxHitResults;
    public AttackShapeSet HitArea => hitArea;

    /// <summary>
    /// 가붕이 Q 기본 판정 도형(할 일 153) — 예전 프리팹 앵커(<c>Armature/MainSkill</c> BoxCollider center (0, 0, 0.8)·size (2, 1, 2),
    /// 앵커 로컬 y 359.97)를 발밑 기준 직사각형으로 옮긴 값. 앞 1.8 / 뒤 0.2 / 폭 2 / yaw −0.03°.
    /// 높이 −0.461 ~ 0.539: 상자는 Armature 높이(0.039)를 중심으로 ±0.5 m 였고, 판정 점(몸 표면에서 발밑에 가장 가까운 점)이
    /// 그 범위에 들면 예전 상자와 겹친 것과 같다. Armature 의 수평 위치(루트 축 x −0.142·z 0.014)는 평타(3단계)처럼 옮기지 않았다(기준점 = 루트 발밑).
    /// </summary>
    public static AttackRectShape ShieldPushRect() =>
        new AttackRectShape(1.8f, 2f, 0.2f) { YawOffset = -0.03f, MinHeight = -0.461f, MaxHeight = 0.539f };
}
