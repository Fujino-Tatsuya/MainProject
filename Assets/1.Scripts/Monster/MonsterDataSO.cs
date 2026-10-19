using UnityEngine;
using UnityEngine.AI;

// 몬스터 데이터 주도 설정. 스탯/인지/그로기/슈퍼아머/타이밍/애니 파라미터명을 한 곳에 모은다.
// (프로젝트 원칙: 스킬/보스/몬스터 파라미터는 ScriptableObject로 — 머지 충돌 완화 + 튜닝 편의.)
[CreateAssetMenu(fileName = "MonsterData", menuName = "Monster/Monster Data", order = 0)]
[DataTableSheet("Monster", Order = 0)]
public class MonsterDataSO : ScriptableObject
{
    [Header("아키타입")]
    public MonsterArchetype archetype = MonsterArchetype.Melee;
    public MonsterRank rank = MonsterRank.Normal;

    [Header("스탯 (Unit.Initialize로 주입)")]
    public int attackDamage = 10;
    public float moveSpeed = 2.5f;   // 배회/기본 이동 속도
    public float chaseSpeed = 4f;    // 추격 이동 속도
    [Tooltip("몬스터 고유 공격속도 = 공격 애니 재생 배율. 1 = 원본, 2 = 두 배 빠르게, 0.5 = 절반.\n" +
             "공격 판정·예고·지속 시간도 이 배율을 따른다. 공격 간격(쿨다운)과는 무관 — 그건 attackCooldown.\n" +
             "(2026-10-02 의미 변경: 예전엔 '초당 공격 횟수'였다. 쿨다운은 attackCooldown 으로 분리됐고, 공격이 끝난 뒤부터 센다.)\n" +
             "🔴 23호(BossDataSO)는 1 에서 바꾸지 말 것 — 애니 관리자에서 빠져 있어 타이머만 나뉘고 애니는 그대로다.")]
    [Min(0.05f)] public float attackSpeed = 1f;   // Unit.AttackSpeed 로 주입
    [Tooltip("공격 후 쉬는 시간(초) — 공격이 **끝난**(Attack 상태를 빠져나간) 뒤 다음 공격까지. " +
             "attackSpeed 로 공격 길이를 바꿔도 이 값은 그대로다. 0 이면 끝나자마자 다시 공격한다(HumanoidBot = 의도된 0 — 10-02 이전 밸런스 유지).\n" +
             "⚠️ 23호는 예외 — 시작 기준이고, 행 cooldown 이 0 인 행의 폴백으로만 쓰인다.")]
    [Min(0f)] public float attackCooldown = 1f;
    public int maxHp = 100;
    public int defense = 0;
    public int maxShield = 0;

    [Header("인지 / 교전 범위")]
    public float detectionRadius = 8f;  // 타깃 인지 반경
    // 인지 허용 수직 차(m). |Δy| 가 이 값을 넘으면 반경 안이라도 **인지하지 않는다** — 층 분리용.
    // 0 이면 제한을 끈다(예전 동작 = 높이 무시). 규칙과 한계는 MonsterPerceptionPolicy 참조.
    // 획득 단계에만 적용된다 — 이미 문 대상은 leashRadius 로만 풀린다.
    public float detectionHeightTolerance = 2f;
    public float attackRange = 2f;      // 이 거리 이내면 공격
    public float leashRadius = 15f;     // 스폰 지점에서 이 거리 벗어나면 복귀
    // 주기 어그로 재선정 간격(초). 0 = 끔(기존 락온 유지 — 사망·디스폰·리쉬로만 풀린다).
    // 판정은 BossAggroPolicy.ShouldRetarget 이고 교전 중 Idle/Chase 에서만 성립한다(공격 커밋 구간은 건드리지 않는다).
    // 중간보스 2종에만 값을 넣었다 — 일반몹 8종은 0 이라 동작이 그대로다.
    public float retargetInterval = 0f;
    // 추적 해제 반경(m, PLAN-boss-aggro §3.4 — 팀장 10-14). 문 대상이 이보다 멀리 1초 넘게 있으면 놓고 다시 탐색한다.
    // 아무도 없으면 스폰 지점으로 걸어서 돌아간다(체력 회복 없음 · 가는 길에도 탐색 — 리쉬 귀환과 다르다).
    // 0 = 끔(이전 동작 — 리쉬로만 풀린다). 일반몹 7종 = 인지 반경 × 1.2 · 중간보스 3종·23호 = 0.
    [Min(0f)] public float loseTargetRadius = 0f;
    public float returnSpeedMultiplier = 5f; // 복귀 시 이동속도 배수(복귀속도 = MoveSpeed × 이 값)

    [Header("회전")]
    // 몸을 돌리는 속도. **0 = 즉시 회전**(한 프레임에 목표 방향으로 스냅 — 몹 8종·중간보스 3종의 기존 동작).
    // >0 이면 플레이어와 같은 규약으로 감속 회전한다(PlayerMovement.rotate_Speed 기본 10):
    //   Slerp(현재, 목표, turnSpeed × deltaTime) + Dot > 0.999 도달 클램프.
    // 🔴 회전은 **서버 권한**이다 — 클라는 NetworkTransform 보간으로 받으므로 서버에서만 감속하면 된다.
    // ⚠️ 0 을 기본값으로 두는 것이 이 필드의 계약이다. 기본을 >0 으로 바꾸면 몹 전체 거동이 한꺼번에 바뀐다.
    public float turnSpeed = 0f;

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

    [Header("원거리 — 공격 방식 (RangedMobile, PLAN-monster-tells)")]
    [Tooltip("Projectile = 투사체를 쏜다(기존). SkyStrike = 타깃 발밑에 원형 예고 → 하늘에서 수직 낙하(코그모 궁 방식).\n" +
             "SkyStrike 는 벽에 부딪히지 않는 대신 **시야가 있을 때만** 쏜다(벽 = 엄폐물 유지). 반경 = projectileSplashRadius.")]
    [DataTableIgnore] public MonsterRangedStyle rangedStyle = MonsterRangedStyle.Projectile;
    [Tooltip("SkyStrike: 원이 다 차는 데 걸리는 시간(초) = 발사 → 착탄. 다 차는 순간 피해.")]
    [DataTableIgnore] [Min(0.1f)] public float skyStrikeTelegraphTime = 1.5f;
    [Tooltip("SkyStrike: 폭탄이 보이며 떨어지는 마지막 구간(초). 예고 시간 안에 포함된다(추가 회피 시간이 아니다).")]
    [DataTableIgnore] [Min(0.05f)] public float skyStrikeFallTime = 0.35f;
    [Tooltip("SkyStrike: 착탄 바닥 높이 ± 이 값 안의 대상만 맞는다(m). 위층·다리 아래는 안 맞게.")]
    [DataTableIgnore] [Min(0f)] public float skyStrikeFloorTolerance = 1.5f;
    [Tooltip("SkyStrike: 시야를 막는 레이어. 기본 Default — 존 프리팹의 벽·바닥이 전부 Default 다. 몬스터·플레이어(Unit)는 막지 않는다.")]
    [DataTableIgnore] public LayerMask skyStrikeLosBlockMask = 1;
    // RangedMobile: 쿨다운 대기 중 제자리(앉는 Idle) 대신 타깃 주변 링을 걸어 재배치(전투 상태 연출, MortarBot).
    public bool repositionBetweenAttacks = false;
    // 후퇴/재배치 최소 이동 시간(초). 이동이 시작되면 이 시간 동안은 공격·정지를 미루고 계속 걷는다(짧은 찔끔 이동 방지).
    public float retreatMinDuration = 1.2f;
    public float repositionMinDuration = 1.2f;

    [Header("공격 타이밍")]
    public float attackDuration = 0.9f; // 공격 상태 지속(모션 길이 근사)
    [Tooltip("공격 선딜(초) = 예비동작(AttackAnticipation 상태) 길이. 실제 시간 = 이 값 ÷ attackSpeed.\n" +
             "예비동작 상태가 있는 몹(Chomp·Humanoid)만 쓴다 — 없는 몹은 무시된다. 0 이면 예비동작 원래 길이.\n" +
             "히트는 애니 이벤트(OnAttackHit)가 내므로 예비동작이 길어지면 타격도 그만큼 늦게 나간다.\n" +
             "🔴 attackDuration 보다 작게 — 그 시간이 지나면 공격이 강제 종료된다(스폰 시 경고). PLAN-attack-windup")]
    public float attackWindup = 0.35f;
    public bool cancelWindupIfTargetLeavesRange = false; // 선딜(히트 발생 전) 중 타깃이 사거리+여유를 벗어나면 공격을 취소하고 추격 복귀(원거리 준비-취소 설계, MortarBot). 멜리 커밋 몹은 false 유지.
    // 사거리 안으로 들어온 순간부터 첫 공격까지 최소 지연(초). 0 = 기존 동작 — 사거리에 닿는 프레임에 바로 때린다.
    // 규칙과 구현 근거는 MonsterEngagePolicy 참조. 완전히 벗어났다 다시 들어오면 다시 걸린다.
    public float engageAttackDelay = 0f;

    [Header("그로기")]
    public int maxGroggyCount = 3;      // 그로기 공격 누적 임계
    public float groggyDuration = 3f;   // 그로기 지속 시간

    [Header("슈퍼아머")]
    public bool startsWithSuperArmor = false;        // 스폰부터 슈퍼아머(무한)
    public bool hasSuperArmorWhileAttacking = false; // 공격 중 슈퍼아머(경직 무시)

    [Header("피격 / 사망")]
    public float hitStunDuration = 0.4f; // 피격 경직 시간
    public float despawnDelay = 2f;      // 사망 후 디스폰까지 지연(디졸브 폴백)

    // ⚠️ 2026-09-21 SO 전수조사 — **유령 필드**라 주석 처리했다. 참조 0.
    //    에셋 12개가 값을 갖고 있었고 그중 3개가 true 였다
    //    (GauntletBotData · SpinnerBotData · WallBotData — 실제 중간보스 3종).
    //    🔴 즉 **누군가 의도를 갖고 채웠지만 코드가 한 번도 읽지 않았다.**
    //    중간보스 구분은 이 플래그가 아니라 **전용 클래스 + MonsterCounterWindow 컴포넌트**가 한다
    //    (SpinnerBot·GauntletBot·WallBot). 동작은 되고 있으니 기능 결손은 아니다.
    //
    // [Header("중간보스 여부")]
    // public bool isMidBoss = false;

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

    // 애니 재생 속도(animator.speed) — MonsterBase 가 한 곳에서 정한다(PLAN-monster-anim-speed S2).
    //   locomotionState 재생 중 + 움직이는 중 → 실제 속도 ÷ locomotionClipSpeed (범위 제한)
    //   그 밖 상태 + 로직 Attack            → attackSpeed
    //   나머지(피격·그로기·사망·서 있음)     → 1
    [Header("애니 재생 속도 — 이동 클립 맞춤")]
    [Tooltip("이동 클립 고유 속도(m/s) = 재생 속도 1 일 때 발이 땅을 밀고 가는 속도. " +
             "이동 중 재생 속도 = 실제 이동 속도 ÷ 이 값 → 발 미끄러짐이 사라진다.\n" +
             "0 이면 맞추지 않는다(재생 속도 1 고정 = 예전 동작).")]
    [DataTableIgnore] [Min(0f)] public float locomotionClipSpeed = 0f;   // 아트 측정값 — 테이블 밖(측정 도구가 SO 에 기록)
    [Tooltip("이동 블렌드에서 이동 클립 비중이 100% 가 되는 속도(m/s) = 블렌드 트리 마지막 자식의 임계값.\n" +
             "이보다 느리면 대기 클립과 섞여 발이 덜 나가므로 재생 속도를 그만큼 덜 줄인다: 재생 속도 = max(실제 속도, 이 값) ÷ 클립 고유 속도.\n" +
             "블렌드 트리는 런타임에 못 읽는다 — `Tools/Monster/이동 클립 고유 속도 측정 → SO 기록` 이 같이 채운다.")]
    [DataTableIgnore] [Min(0f)] public float locomotionFullBlendSpeed = 0f;   // 아트 측정값 — 테이블 밖(측정 도구가 SO 에 기록)
    [Tooltip("블렌드 대기 클립 한 주기(초, 블렌드 timeScale·상태 speed 반영). 1D 블렌드는 자식 클립 시간을 맞추므로 " +
             "대기 클립이 길수록 섞인 구간의 이동 클립이 느려진다 — 그 보정에 쓴다. 0 = 보정 생략. 측정 도구가 채운다.")]
    [DataTableIgnore] [Min(0f)] public float locomotionIdleCycleSeconds = 0f;   // 아트 측정값 — 테이블 밖(측정 도구가 SO 에 기록)
    [Tooltip("블렌드 이동 클립 한 주기(초, 블렌드 timeScale·상태 speed 반영). 0 = 보정 생략. 측정 도구가 채운다.")]
    [DataTableIgnore] [Min(0f)] public float locomotionMoveCycleSeconds = 0f;   // 아트 측정값 — 테이블 밖(측정 도구가 SO 에 기록)
    [Tooltip("움직이는 동안 이동 블렌드 값을 max(실제 속도, locomotionFullBlendSpeed) 로 보내 **이동 클립 100%** 로 재생한다.\n" +
             "블렌드 임계값이 이동 속도보다 높고 대기 클립이 긴 몹(ChompBot: th 4.5 · 대기 3.8초 vs 이동 0.4초)은 늘 섞인 구간에서 돌아 " +
             "이동 클립이 슬로모션이 된다 — 재생 속도로는 못 메운다(배회 9배 필요). 켜면 출발 순간 대기→이동 섞임이 사라진다(팀장 10-02 Chomp 만).")]
    public bool locomotionFullBlendWhileMoving = false;
    [Tooltip("이동 클립 재생 속도 범위. 너무 느리거나 빠르면 어색해서 자른다 — 범위 밖에선 다시 약간 미끄러진다.")]
    public Vector2 locomotionAnimSpeedRange = new Vector2(0.5f, 2.5f);
    [Tooltip("발 맞춤으로 계산한 이동 애니 재생 속도에 곱하는 값. 1 = 발이 바닥에 정확히 맞는다(기본).\n" +
             "1 보다 작으면 애니가 그만큼 느려지고 몸이 발보다 앞서 나간다(0.85 = 15% 미끄러짐). 보폭이 짧아 " +
             "다리가 바빠 보이는 몹(ChompBot) 용 — 이동 속도는 그대로 두고 애니만 늦춘다.\n" +
             "Play 중 인스펙터에서 바꾸면 바로 반영된다(매 프레임 읽음).")]
    [DataTableIgnore] [Range(0.1f, 1.5f)] public float locomotionPlaybackScale = 1f;   // 팀장 눈 튜닝값(10-06) — 확정되면 테이블 노출 여부 결정

    [Header("애니메이터 컨트롤러 교체 (선택)")]
    [Tooltip("비우면 프리팹/아트 프리팹에 배선된 컨트롤러를 그대로 쓴다. " +
             "채우면 MonsterBase 가 OnNetworkSpawn 에서 이것으로 덮는다(게이트 없음 = 전 피어).\n" +
             "⚠️ 에디터/프리팹 인스펙터에는 여전히 원래 컨트롤러가 보인다 — 교체는 런타임에만 일어난다.\n\n" +
             "🔴 아트 팩 컨트롤러에 '우리 코드가 빠져나올 수 없는 상태'가 있을 때 쓴다 — " +
             "PeekABot 의 Hide/Raise, TeslaBot 의 Charge(→Shoot 전이가 우리가 안 쓰는 Attack 트리거를 요구)가 " +
             "그랬고, 그 상태로 들어가면 3단 신축 컬럼이 중간에 걸려 몸체가 분리돼 보였다.\n\n" +
             "왜 프리팹 오버라이드가 아니라 데이터인가: Animator 가 2단 중첩 프리팹 안에 있어 " +
             "외부 프리팹에서 m_Controller 를 오버라이드하면 타깃이 해석되지 않는다(2026-09-10 실측 — " +
             "저장은 성공하고 YAML 에도 남는데 로드하면 null 이다).")]
    public RuntimeAnimatorController animatorControllerOverride;

    [Header("공격 예고 색·마스크 (아트 조절)")]
    [Tooltip("이 몬스터의 공격 예고·인터럽트·드론 원·전기 장판 색과 마스크. 비우면 코드 기본값(2026-10-13 화면).\n" +
             "Play 중 그 에셋 값을 바꾸면 켜진 예고에 바로 반영되고 값은 에셋에 남는다. 규격 = Docs/tech/telegraph-style.md")]
    [DataTableIgnore] public TelegraphStyleSO telegraphStyle;
}
