using UnityEngine;
using UnityEngine.AI;

// 몬스터 데이터 주도 설정. 스탯/인지/그로기/슈퍼아머/타이밍/애니 파라미터명을 한 곳에 모은다.
// (프로젝트 원칙: 스킬/보스/몬스터 파라미터는 ScriptableObject로 — 머지 충돌 완화 + 튜닝 편의.)
[CreateAssetMenu(fileName = "MonsterData", menuName = "Monster/Monster Data", order = 0)]
public class MonsterDataSO : ScriptableObject
{
    [Header("아키타입")]
    public MonsterArchetype archetype = MonsterArchetype.Melee;

    [Header("스탯 (Unit.Initialize로 주입)")]
    public int attackDamage = 10;
    public float moveSpeed = 2.5f;   // 배회/기본 이동 속도
    public float chaseSpeed = 4f;    // 추격 이동 속도
    public float attackSpeed = 1f;   // 초당 공격 횟수. 공격 간격 = 1 / 이 값. (Unit.AttackSpeed로 주입 → 공격 쿨다운 산출)
    public int maxHp = 100;
    public int defense = 0;
    public int maxShield = 0;

    [Header("인지 / 교전 범위")]
    public float detectionRadius = 8f;  // 타깃 인지 반경
    public float attackRange = 2f;      // 이 거리 이내면 공격
    public float leashRadius = 15f;     // 스폰 지점에서 이 거리 벗어나면 복귀
    public float returnSpeedMultiplier = 5f; // 복귀 시 이동속도 배수(복귀속도 = MoveSpeed × 이 값)

    [Header("회피 / 크라우드 (부분 겹침·성능)")]
    // NavMeshAgent 회피 반경. CapsuleCollider(히트박스)보다 작게 두면 몹끼리 '부분 겹침'이 허용된다(작을수록 더 겹침).
    // 물리(RB)로 밀어내지 않고 이 값만으로 겹침량을 조절 — 서버권한 crowd에서 가장 저렴. 콜라이더는 히트용으로 별도 유지.
    public float avoidanceRadius = 0.3f;
    // 회피 품질 ↔ CPU 비용. 수십 마리 crowd면 Med(또는 Low) 권장. High는 과함.
    public ObstacleAvoidanceType obstacleAvoidance = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

    [Header("원거리 (RangedTurret / RangedMobile)")]
    public GameObject projectilePrefab;      // 서버 스폰 투사체(NetworkObject + MonsterProjectile 필요). Melee면 비움.
    public float projectileSpeed = 12f;      // 투사체 속도(m/s)
    public float projectileLifetime = 4f;    // 투사체 수명(초)
    public float minStandoff = 4f;           // RangedMobile: 이보다 가까우면 후퇴(사격 사거리 = attackRange)
    public float projectileArcHeight = 0f;   // 0=직선(기존), >0=포물선 정점 높이(m). 포물선 포격용(MortarBot), 발사 시점 타깃 지점 조준·유도 없음.
    public float projectileSplashRadius = 0f; // 0=직격만, >0=착탄 지점 반경 스플래시 데미지.
    // RangedMobile: 쿨다운 대기 중 제자리(앉는 Idle) 대신 타깃 주변 링을 걸어 재배치(전투 상태 연출, MortarBot).
    public bool repositionBetweenAttacks = false;
    // 후퇴/재배치 최소 이동 시간(초). 이동이 시작되면 이 시간 동안은 공격·정지를 미루고 계속 걷는다(짧은 찔끔 이동 방지).
    public float retreatMinDuration = 1.2f;
    public float repositionMinDuration = 1.2f;

    [Header("공격 타이밍")]
    public float attackDuration = 0.9f; // 공격 상태 지속(모션 길이 근사)
    public float attackWindup = 0.35f;  // (이벤트 전환으로 base 미사용 — 서브클래스/폴백 참고용)
    public bool cancelWindupIfTargetLeavesRange = false; // 선딜(히트 발생 전) 중 타깃이 사거리+여유를 벗어나면 공격을 취소하고 추격 복귀(원거리 준비-취소 설계, MortarBot). 멜리 커밋 몹은 false 유지.

    [Header("그로기")]
    public int maxGroggyCount = 3;      // 그로기 공격 누적 임계
    public float groggyDuration = 3f;   // 그로기 지속 시간

    [Header("슈퍼아머")]
    public bool startsWithSuperArmor = false;        // 스폰부터 슈퍼아머(무한)
    public bool hasSuperArmorWhileAttacking = false; // 공격 중 슈퍼아머(경직 무시)

    [Header("피격 / 사망")]
    public float hitStunDuration = 0.4f; // 피격 경직 시간
    public float despawnDelay = 2f;      // 사망 후 디스폰까지 지연(디졸브 폴백)

    [Header("중간보스 여부")]
    public bool isMidBoss = false;

    // 애니메이터 파라미터명 상수.
    // 자산(Animator Controller)이 아직 없을 수 있으므로 MonsterBase는 존재 여부를 확인 후 graceful 세팅한다.
    [Header("애니메이터 파라미터명")]
    public string animSpeedParam = "Speed";   // float: 이동 블렌드
    public string attackTrigger = "Attack";   // trigger (공격 진입)
    public string attackFinishTrigger = "";   // trigger (다단계 공격 2단계: 타격 시점에 발동. 비우면 단발). 예: WallBot="AttackEnd"
    public string hitTrigger = "Hit";         // trigger
    public string groggyBool = "Groggy";      // bool
    public string deathTrigger = "Death";     // trigger
    public string locomotionState = "Movement"; // 이동(로코모션) 상태명 — 액션 클립 강제 종료 후 복귀 CrossFade 대상
}
