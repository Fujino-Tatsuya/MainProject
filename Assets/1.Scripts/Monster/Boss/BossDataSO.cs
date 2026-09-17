using System;
using UnityEngine;

// 보스 공격 테이블 1행 = 공격 1종. 배열 인덱스가 곧 MonsterBase 의 **공격 슬롯 번호**다
// (ConfigureAttackSlots / CooldownReady / CurrentAttackSlot 이 이 인덱스를 쓴다).
//
// 🔴 행 순서를 바꾸면 쿨다운 슬롯 번호가 함께 바뀐다. 행은 끝에 추가하고 중간 삽입/재정렬을 피한다.
[Serializable]
public class BossAttackEntry
{
    [Tooltip("공격 종류. 서브클래스가 이 값으로 히트 판정을 분기한다.")]
    public BossAttackId attackId = BossAttackId.LeftHook;

    [Tooltip("ClientRpc CrossFade 대상 애니메이터 **상태명**(트리거명이 아니다). " +
             "다지선다 공격은 상태 복제로 실을 수 없어 CrossFade 로 직접 재생한다(GauntletBot 선례). " +
             "🔴 이름이 틀리면 애니가 조용히 안 나온다 → 스폰 시 HasState 로 검증해 LogError 를 남긴다.")]
    public string animatorStateName = "";

    [Tooltip("재사용 대기(초). 0 이면 base 쿨(1 / attackSpeed)로 폴백한다. 확정값 = Jump 10 / Dash 5 / Grab 10 / 훅·어퍼 2~3.")]
    [Min(0f)] public float cooldown = 0f;

    [Tooltip("이 공격이 열리는 최소 거리(m). 타깃이 이보다 가까우면 후보에서 빠진다.")]
    [Min(0f)] public float minDistance = 0f;

    [Tooltip("이 공격이 열리는 최대 거리(m). 타깃이 이보다 멀면 후보에서 빠진다.")]
    [Min(0f)] public float maxDistance = 3f;

    [Tooltip("거리창을 무시한다(어디서든 후보). JumpAttack 전용 — '거리 무관 + 최원거리 타겟'이 확정 스펙이다.")]
    public bool ignoreDistanceWindow = false;

    [Tooltip("[S6] 이 공격이 노릴 대상. AggroTarget = 지금 어그로가 물린 대상, " +
             "FarthestPlayer = 최원거리 플레이어(Jump 전용). " +
             "🔴 '누가 맞는가'는 이걸로 안 정해진다 — 실제 피해는 히트박스·반경 판정이 따로 고른다. " +
             "이 값이 정하는 것은 보스가 어디를 노리고 어디로 가느냐다.")]
    public BossAttackTargeting attackTargeting = BossAttackTargeting.AggroTarget;

    [Tooltip("가중치 룰렛 비중. 후보들 가중치 합에서 비율로 뽑는다. 0 이면 절대 안 나온다.")]
    [Min(0f)] public float weight = 10f;

    [Tooltip("이 페이즈부터 사용 가능(0 = 처음부터). CurrentPhase >= 이 값일 때만 후보가 된다.")]
    [Min(0)] public int allowedFromPhase = 0;

    [Tooltip("이 공격의 데미지. 0 이면 SO 의 attackDamage 를 쓴다. 페이즈 damageMultiplier 가 곱해진다.")]
    [Min(0)] public int damage = 0;

    [Tooltip("[S3] 카운터(인터럽트) 창을 여는 공격인가. **Grab · Dash 만 true** — " +
             "훅·어퍼까지 열면 카운터가 상시 자원이 되어 그로기가 흔해진다(팀장 확정).")]
    public bool opensCounterWindow = false;

    [Tooltip("[S3] 이 공격의 카운터 입력 가능 시간(초). opensCounterWindow 가 false 면 쓰지 않는다. " +
             "확정 초기값 = Grab 1.0 / Dash 1.5. 🔴 클립의 OnAttackHit 이벤트보다 짧으면 " +
             "타이머가 먼저 끝나 진단 경고가 뜬다(공격은 이벤트 도착 후 발사된다).")]
    [Range(0f, 2f)] public float counterWindowDuration = 0f;

    [Tooltip("이 공격 중 슈퍼아머(경직 무시). 🔴 SO 의 hasSuperArmorWhileAttacking 은 **false 로 둘 것** — " +
             "켜 두면 base 가 전 공격에 슈퍼아머를 걸어 이 플래그가 무의미해진다(스폰 시 LogError 로 잡는다).")]
    public bool superArmor = false;

    [Tooltip("[S2] 히트 판정 형상을 제공할 ColliderInfo 자식의 오브젝트 이름. 비우면 기본 MeleeHitbox 를 쓴다. " +
             "🔴 문자열이라 오타가 조용히 무시된다 → 스폰 시 실존을 검증해 LogError 를 남긴다.")]
    public string hitboxAnchorName = "";

    // ─── [G1] 전진 공격(Lunge) ────────────────────────────────────────
    // 기획 문서: 훅·어퍼는 "플레이어 방향으로 전진하면서" 때린다. 전진 구간부터 이미 공격이라
    // 경로에 닿은 사람도 맞는다(팀장 확정 B5) — 돌진과 섞인 공격으로 본다.
    //
    // 🔴 클립에는 전진이 들어 있지 않다(팀장 확인). 루트모션이 아니라 **코드 이동**이다.

    [Tooltip("[G1] 공격 시작과 함께 전방으로 전진하는 거리(m). 0 이면 전진 없음(제자리 공격). " +
             "확정 초기값 = 훅·어퍼 2.5. 전진은 애니 히트 이벤트(OnAttackHit)가 오면 멈춘다 — " +
             "즉 '전진하면서 휘두르고 주먹이 닿을 때 멈춘다'가 되고, 클립 길이와 자동으로 맞는다.")]
    [Min(0f)] public float lungeDistance = 0f;

    [Tooltip("[G1] 전진 속도 = moveSpeed × 이 값.\n" +
             "🔴 **전진이 히트 이벤트보다 먼저 끝나야 한다.** 늦으면 보스가 아직 이동 중일 때 판정이 나서 " +
             "예고를 그린 자리와 실제 판정 자리가 어긋난다. 훅·어퍼 기준 필요 속도는 " +
             "lungeDistance ÷ (히트 정규화 − telegraphPoseNormalized) × 클립길이 다 " +
             "(2.5m ÷ 0.2325초 = 10.75m/s → moveSpeed 2.5 기준 배수 4.3 이 하한, 여유를 둬 5).")]
    [Min(0.1f)] public float lungeSpeedMultiplier = 5f;

    [Tooltip("[G1] 전진 **경로** 판정 반경(m). 보스 몸통 크기로 준다(NavMeshAgent radius 0.85 기준 1.2 권장). " +
             "0 이면 전진은 하되 경로 데미지가 없다.\n" +
             "🔴 매 틱 이 반경의 구로 때리므로 지나간 자리가 **캡슐(띠)** 이 된다 — 예고로 그리는 도형과 " +
             "정확히 같은 모양이라 예고가 판정에 대해 거짓말하지 않는다.\n" +
             "⚠️ 예전엔 ColliderInfo 이름(DashBody)을 썼는데, 그건 돌진 전용 히트박스라 " +
             "**3.4m 폭 × 보스 앞 4.42m** 로 지나치게 컸다 — 부채꼴 모양이 무의미해졌다.")]
    [Min(0f)] public float lungePathRadius = 0f;

    // ─── [G2] 끝점 부채꼴 판정 ────────────────────────────────────────
    // 기획 문서: 훅은 "공격하는 팔 방향으로 넓은 범위", 어퍼는 "끝나는 지점에서 원형으로 더 넓게".
    //
    // 🔴 물리 질의에는 **원뿔 형상이 없다.** 구 오버랩 + 각도 필터가 표준이고, 이 프로젝트도
    //    이미 같은 패턴을 쓴다(FindGrabTarget 의 OverlapSphere · IsCounterFromFront 의 각도).
    //    박스를 비스듬히 두 개 붙이는 근사는 **예고 데칼과 모서리가 어긋나서** 채택하지 않았다.
    //
    // 🔴 여기 값이 **예고 데칼과 판정 양쪽을 동시에** 먹인다. 갈라 두면 예고가 판정에 대해 거짓말한다.

    [Tooltip("[G2] 끝점 부채꼴 반경(m). 0 이면 부채꼴을 쓰지 않고 위 hitboxAnchorName 형상으로 판정한다.")]
    [Min(0f)] public float coneRadius = 0f;

    [Tooltip("[G2] 부채꼴 **전체** 각(도). 90 이면 중심 기준 ±45. " +
             "360 = 각도 제한 없음(원형) — 어퍼의 '끝점 원형'이 이 값이다.")]
    [Range(0f, 360f)] public float coneAngle = 90f;

    [Tooltip("[G2] 부채꼴 중심을 보스 정면에서 **좌/우로 치우치게** 하는 각(도). + = 오른쪽. " +
             "오른손 훅은 +, 왼손 훅은 − 를 준다 — 문서의 '공격하는 팔 쪽이 넓어진다'가 이 한 칸이다.")]
    [Range(-180f, 180f)] public float coneOffsetAngle = 0f;

    // ─── [G2] 예고 구간 ──────────────────────────────────────────────
    // 🔴 **예고는 판정보다 먼저 끝나야 한다.** 예고와 공격을 같은 순간에 시작하면 반응 시간이 0 이라
    //    예고가 아니라 사후 통보가 된다(2026-09-16 팀장 지적으로 이 구간이 생겼다).
    //
    // 순서: 준비 자세에서 정지 → 부채꼴이 차오름 → 다 차면 자세를 풀고 **전진 + 공격**.

    [Tooltip("[G2] 예고 길이(초). 이 시간 동안 보스는 **준비 자세에서 멈춰** 있고 바닥 부채꼴이 차오른다. " +
             "0 이면 예고 없이 즉발(기존 동작). 확정 초기값 = 훅·어퍼 0.7.")]
    [Min(0f)] public float telegraphDuration = 0f;

    [Tooltip("[G2] 예고 동안 멈춰 있을 **자세**(공격 클립의 정규화 시간 0~1). " +
             "0.15 면 '팔을 뒤로 당긴' 근처다. 🔴 이 값이 클립의 OnAttackHit 시점보다 크면 " +
             "히트 프레임을 이미 지나쳐 **데미지가 나가지 않는다** — 훅·어퍼는 0.4 미만이어야 한다.")]
    [Range(0f, 0.95f)] public float telegraphPoseNormalized = 0.15f;
}

// 공격이 노릴 대상. [S6]
//
// 🔴 값 추가는 끝에만(직렬화 정수). 그리고 **쓰는 데가 생길 때만** 추가할 것 —
//    이 enum 의 전신(BossTargetRule)은 아무도 읽지 않는 죽은 데이터로 남아 있다가
//    2026-09-03 에 제거됐다. 그때 Jump 의 "최원거리" 의도는 하드코딩으로만 살아 있었다.
public enum BossAttackTargeting
{
    AggroTarget,     // 지금 어그로가 물린 대상(base 의 _target)
    FarthestPlayer,  // 최원거리 플레이어 — Jump 전용. 후열이 안전하게 딜하는 것을 응징한다
}

// 페이즈 진입 시 실행할 고정 시퀀스. [S7]
public enum BossPhaseSequence
{
    None = 0,
    ChargeSequence = 1,  // 송전기(차징) 시퀀스 — 실패 시 레이지 돌진 3회
}

// 페이즈 1행. hpThreshold 는 **내림차순**으로 저작한다(0.66 → 0.33).
[Serializable]
public class BossPhaseEntry
{
    [Tooltip("이 비율 **이하**로 체력이 떨어지면 이 페이즈로 진입한다. 확정값 = 0.66 / 0.33.")]
    [Range(0f, 1f)] public float hpThreshold = 0.66f;

    [Tooltip("[S7] 진입 시 실행할 고정 시퀀스(송전기 등).")]
    public BossPhaseSequence sequence = BossPhaseSequence.None;

    [Tooltip("이 페이즈의 공격 데미지 배수.")]
    [Min(0f)] public float damageMultiplier = 1f;

    [Tooltip("이 페이즈의 추격 이동속도 배수(chaseSpeed 에 곱해진다).")]
    [Min(0.1f)] public float speedMultiplier = 1f;
}

// 보스 데이터. MonsterDataSO 파생 —
// 스탯·인지·그로기·애니 파라미터명·attackDuration 은 전부 base 필드를 그대로 쓴다.
//
// 왜 파생인가: 일반몹 SO 를 오염시키지 않기 위해서다. 이 프로젝트는 이미 attackWindup·maxShield 같은
// **죽은 필드가 있는 상태**라(boss-rebuild-standard.md §6 에 9건 정리) 공용 SO 를 더 늘리지 않는다.
//
// 정본: Docs/tech/boss-rebuild-standard.md §10.3
//
// ⚠️ §10.3 대비 의도적 차이 1건 — `meleeRange` / `rangedThreshold` 를 넣지 않았다.
//    공격별 minDistance/maxDistance 거리창 + base 의 attackRange(접근/정지 기준)가 그 역할을 이미
//    전부 덮는다. 읽는 곳 없는 필드를 만드는 것이 이 문서가 경고하는 바로 그 실패 모드다.
[CreateAssetMenu(fileName = "BossData", menuName = "Monster/Boss Data", order = 1)]
public class BossDataSO : MonsterDataSO
{
    [Header("공격 테이블 — 배열 인덱스 = 쿨다운 슬롯 번호")]
    [Tooltip("공격 6종. 행 순서가 슬롯 번호이므로 중간 삽입/재정렬을 피한다.")]
    public BossAttackEntry[] attacks =
    {
        new BossAttackEntry { attackId = BossAttackId.LeftHook,  animatorStateName = "", cooldown = 2.5f, minDistance = 0f, maxDistance = 3f,  weight = 30f, superArmor = true },
        new BossAttackEntry { attackId = BossAttackId.RightHook, animatorStateName = "", cooldown = 2.5f, minDistance = 0f, maxDistance = 3f,  weight = 30f, superArmor = true },
        new BossAttackEntry { attackId = BossAttackId.Upper,     animatorStateName = "", cooldown = 3f,   minDistance = 0f, maxDistance = 2.5f, weight = 12f, superArmor = true },
        new BossAttackEntry { attackId = BossAttackId.Grab,      animatorStateName = "", cooldown = 10f,  minDistance = 0f, maxDistance = 3.5f, weight = 15f, superArmor = true,  opensCounterWindow = true },
        new BossAttackEntry { attackId = BossAttackId.Jump,      animatorStateName = "", cooldown = 10f,  ignoreDistanceWindow = true, attackTargeting = BossAttackTargeting.FarthestPlayer, weight = 15f, superArmor = true },
        new BossAttackEntry { attackId = BossAttackId.Dash,      animatorStateName = "", cooldown = 5f,   minDistance = 5f, maxDistance = 20f, weight = 20f, superArmor = true,  opensCounterWindow = true },

        // 🔴 아래 2행은 **weight 0** — 룰렛이 뽑지 않고 페이즈 시스템이 직접 트리거한다.
        //    카운터 창 없음(차징 중엔 다른 애니가 안 나오고, 레이지는 실패 벌칙이라 쉽게 풀려선 안 된다).
        new BossAttackEntry { attackId = BossAttackId.ChargeSequence, animatorStateName = "", cooldown = 0f, ignoreDistanceWindow = true, weight = 0f, superArmor = true },
        new BossAttackEntry { attackId = BossAttackId.RageDash,       animatorStateName = "", cooldown = 0f, ignoreDistanceWindow = true, weight = 0f, superArmor = true },
    };

    [Header("공격 선택기")]
    [Tooltip("직전에 쓴 공격의 가중치에 곱하는 감쇠(0.3 = 30%로 줄인다). 같은 공격이 몰려 나오는 것을 완화한다. " +
             "1 이면 감쇠 없음. 확률 감쇠라 '연속 N회 금지'는 보장하지 못한다 — 그건 아래 repeatBlockAfter 가 한다.")]
    [Range(0f, 1f)] public float repeatPenalty = 0.3f;

    [Tooltip("같은 공격이 이 횟수만큼 연속되면 다음 선택에서 후보에서 **제외**한다. 2 = 3연속 금지. " +
             "0 이면 하드 제외 없이 repeatPenalty 감쇠만 쓴다.\n" +
             "🔴 단 다른 후보가 하나도 없으면 제외하지 않는다 — 보스가 제자리에 멈춰 서는 쪽이 더 나쁜 버그다.")]
    [Min(0)] public int repeatBlockAfter = 2;

    [Header("페이즈 — hpThreshold 내림차순으로 저작")]
    public BossPhaseEntry[] phases =
    {
        new BossPhaseEntry { hpThreshold = 0.66f, sequence = BossPhaseSequence.ChargeSequence, damageMultiplier = 1f, speedMultiplier = 1f },
        new BossPhaseEntry { hpThreshold = 0.33f, sequence = BossPhaseSequence.ChargeSequence, damageMultiplier = 1f, speedMultiplier = 1f },
    };

    [Header("카운터 / 그로기 — maxGroggyCount·groggyDuration 은 base 필드 재사용")]
    [Tooltip("[S3] 카운터 성공으로 인정하는 정면 각도(도). 60 = 보스 정면 기준 ±60°. " +
             "헤드어택(은희) 구현 후 그쪽 판정으로 교체될 지점이다.")]
    [Range(0f, 180f)] public float counterFrontAngle = 60f;

    [Tooltip("후방(백어택) 판정 반각(도). 60 = 보스 뒤쪽 기준 ±60°. " +
             "BossDirectionIndicator 가 이 값으로 후방 호를 그리므로, 백어택 판정이 구현되면 " +
             "**같은 값을 읽어야** 표시와 판정이 어긋나지 않는다.")]
    [Range(0f, 180f)] public float backAttackAngle = 60f;

    [Tooltip("카운터 성공 시 재생할 피격 리액션 애니메이터 상태명(예: getowned). 스폰 시 HasState 로 검증한다.")]
    public string hitReactionState = "getowned";

    [Tooltip("[G6] 인터럽트 성공 리액션 — **오른쪽** 상태명. 위 hitReactionState 는 왼쪽이다. " +
             "🔴 잡기는 **항상 오른쪽**, 돌진은 **L·R 50:50 난수**다(팀장 확정 R1). " +
             "난수는 서버가 뽑아 복제한다 — 피어마다 따로 뽑으면 화면이 갈린다. " +
             "비우면 왼쪽 하나만 쓴다(기존 동작).")]
    public string hitReactionStateRight = "getowned_R";

    [Tooltip("[S3] Break(그로기 카운트 최대 도달) 지속 시간(초). 일반 그로기는 base 의 groggyDuration 을 쓴다.")]
    [Min(0f)] public float breakDuration = 5f;

    [Tooltip("어그로 재선정 주기(초). 0 이면 끈다(= 처음 문 대상을 끝까지 문다). " +
             "🔴 base 의 락온은 사망·디스폰·리쉬로만 풀려서 3인전에서 한 명만 계속 물린다. " +
             "공격 도중에는 바뀌지 않는다 — 조준·잡기 체인·돌진 방향이 흔들리기 때문(BossAggroPolicy).")]
    [Min(0f)] public float aggroRetargetInterval = 8f;

    [Tooltip("어그로 재선정 때 **지금 물고 있는 대상을 후보에서 뺀다**. " +
             "끄면(기본) 최근접을 다시 고르므로 근접이 탱킹하는 구도에서는 어그로가 실질적으로 안 돈다. " +
             "켜면 주기마다 실제로 다른 사람에게 압박이 간다.\n" +
             "🔴 대안이 없으면(1대1) 자동으로 현행과 같아진다 — 켜도 회귀가 없다.\n" +
             "⚠️ 켜면 후열이 과하게 압박받을 수 있다. MPPM 으로 보고 정할 값이라 노브로 뺐다(2026-09-03).")]
    public bool aggroAvoidsRepeatTarget = false;

    [Tooltip("[S7] 송전기(차징) 전멸 성공 시의 전체 행동 불능 시간(초). 일반 카운터 그로기(groggyDuration)와 " +
             "따로 두는 이유 — 기믹을 깬 보상이라 카운터 한 번보다 길어야 손맛이 산다(팀장 확정 2026-09-03). " +
             "🔴 Break 로 승격하지는 않는다(페이즈 연출 직후 장시간 무력화가 겹치면 연출이 죽는다).")]
    [Min(0f)] public float chargeClearGroggyDuration = 1f;

    [Header("Grab 체인 — 값은 플레이로 튜닝(PLAN §9)")]
    [Tooltip("잡기 판정 반경(m). 애니 이벤트(OnAttackHit) 시점에 이 반경 안의 최근접 플레이어를 잡는다.")]
    [Min(0.1f)] public float grabRadius = 2.2f;

    [Tooltip("붙잡고 있는 시간(초).")]
    [Min(0f)] public float grabHoldDuration = 2f;

    [Tooltip("붙잡은 동안 주기 데미지 간격(초). 0 이면 주기 데미지 없음.")]
    [Min(0f)] public float grabTickInterval = 0.5f;

    [Tooltip("붙잡은 동안 1회 주기 데미지(전기). 0 이면 데미지 없음.")]
    [Min(0)] public int grabTickDamage = 5;

    [Tooltip("던지는 모션 시간(초).")]
    [Min(0f)] public float grabThrowDuration = 0.6f;

    [Tooltip("던지는 거리(m). ⚠️ 실제 이동 적용은 플레이어 CC 수신 경로(PLAN §5.1 G1)가 정해진 뒤에 붙는다.")]
    [Min(0f)] public float grabThrowDistance = 6f;

    [Tooltip("던질 때 데미지.")]
    [Min(0)] public int grabThrowDamage = 20;

    [Tooltip("체인 종료 후 복귀 시간(초). 빗나갔을 때도 이 시간만큼 경직된다(헛잡기 대가).")]
    [Min(0f)] public float grabRecoveryDuration = 0.8f;

    [Tooltip("Hold 단계 애니메이터 상태명. 비우면 잡기 클립을 그대로 유지한다.")]
    public string grabHoldState = "";

    [Tooltip("Throw 단계 애니메이터 상태명. 비우면 Hold 클립을 그대로 유지한다.")]
    public string grabThrowState = "";

    // ─── [G5] 잡기 재설계 (2026-09-16 팀장 확정) ──────────────────────
    // 최종 사이클: **예고 → 끌어당김 → 붙잡기 → 지짐이 → 내려치기 ×3 → 놓아주기**
    //
    // 클립 대응(FBX 60fps 실측):
    //   MagneticGrab(1.65s) → Grab(3.12s 중 붙잡는 순간까지) → Holding(1.13s)
    //   → Throw(0.65s) ×3 → GrabEnd(1.37s)
    //
    // 🔴 **끌어당기는 순간까지는 인터럽트가 안 된다.** 실제로 붙잡은 뒤부터 3번째 내려치기 직전까지만
    //    열린다 — 그래서 잡기의 카운터 창은 공격 **시작**이 아니라 붙잡기 성립 시점에 열린다.

    [Tooltip("[G5] 🔴 **잡기 사이클 전체 속도 배수.** 애니 재생속도와 코드 단계 타이머에 **동시에** 걸린다 — " +
             "이 한 칸만 바꾸면 사이클 전체가 같이 빨라진다.\n" +
             "🔴 한쪽에만 걸면 애니와 FSM 이 어긋나고, 이 프로젝트에서 그건 **조용한 데미지 0** 으로 나타난다.\n" +
             "확정 초기값 1.4(9.2초짜리 원본 사이클이 길어서).")]
    [Range(0.25f, 3f)] public float grabCycleSpeed = 1.4f;

    [Tooltip("[G5] 끌어당김 구간 길이(초) = MagneticGrab 클립 길이. 이 구간에 부채꼴 안의 **전원**이 끌려온다.")]
    [Min(0f)] public float grabPullDuration = 1.65f;

    [Tooltip("[G5] 끌어당김 뒤 **붙잡기까지** 걸리는 시간(초). Grab 클립에서 손이 닿는 순간까지다 " +
             "(클립 전체 3.12초를 다 쓰지 않는다 — 붙잡은 뒤엔 지짐이로 넘어간다).")]
    [Min(0f)] public float grabCatchDuration = 1.1f;

    [Tooltip("[G5] 내려치기 횟수. **3번째에서 플레이어를 놓아준다.**")]
    [Min(1)] public int grabSlamCount = 3;

    [Tooltip("[G5] 내려치기 1회 데미지. 0 이면 grabThrowDamage 를 쓴다.")]
    [Min(0)] public int grabSlamDamage = 0;

    [Tooltip("[G5] 놓아주는 구간 길이(초) = GrabEnd 클립 길이.")]
    [Min(0f)] public float grabEndDuration = 1.37f;

    [Tooltip("[G5] 붙잡기 단계 애니메이터 상태명. 비우면 공격 행의 animatorStateName 을 그대로 쓴다.")]
    public string grabCatchState = "Grab";

    [Tooltip("[G5] 놓아주기 단계 애니메이터 상태명.")]
    public string grabEndState = "GrabEnd";

    [Tooltip("[G5] 끌려왔지만 **붙잡히지 않은** 플레이어를 보스 전방으로 밀어내는 세기. 0 이면 밀지 않는다.")]
    [Min(0f)] public float grabPullKnockback = 4f;

    [Header("JumpAttack — 거리 무관 / 최원거리 플레이어 타겟")]
    [Tooltip("최원거리 플레이어를 찾는 탐색 반경(m). 보스룸을 덮을 만큼 넉넉히 — 이 밖이면 못 찾는다.")]
    [Min(1f)] public float jumpSearchRadius = 30f;

    [Tooltip("체공 시간(초) = 예고 장판이 점증하는 시간. 이 시간이 끝나면 착지점으로 이동한다.")]
    [Min(0.1f)] public float jumpHoverTime = 1.2f;

    [Tooltip("착지 클립 길이(초). 이 안에 히트 이벤트(OnAttackHit)가 오면 착지 데미지가 나간다.")]
    [Min(0.1f)] public float jumpLandingDuration = 1f;

    [Tooltip("착지 후 복귀 경직(초).")]
    [Min(0f)] public float jumpRecoveryDuration = 0.4f;

    [Tooltip("착지 AoE 반경(m). 예고 장판 크기와 같은 값이다 — 예고가 판정에 대해 거짓말하지 않게.")]
    [Min(0.1f)] public float jumpAoeRadius = 3.5f;

    [Tooltip("착지 데미지. 0 이면 공격 테이블 행의 damage(그것도 0 이면 attackDamage)를 쓴다.")]
    [Min(0)] public int jumpLandingDamage = 0;

    [Tooltip("체공 포즈 애니메이터 상태명. 비우면 도약 클립을 유지한다.")]
    public string jumpHoverState = "";

    [Tooltip("착지 공격 애니메이터 상태명.")]
    public string jumpLandingState = "";

    // 🔴 **지금은 아무도 읽지 않는다**(2026-09-09). 착지 예고 2개가 AoeTelegraph 프리팹 →
    //    EffectCatalog 루프 이펙트(Drop_Charge_Boundary / Drop_Charge_Indicator)로 전부 넘어갔다.
    //    이 레포에는 조용히 무시되는 설정값이 이미 쌓여 있어(정본 §6) 지우기 전까지 명시해 둔다.
    [Tooltip("⚠️ **미사용** — 착지 예고는 EffectCatalog 의 Drop_Charge_Boundary(경계) + " +
             "Drop_Charge_Indicator(차오르는 원) 이펙트가 그린다. 여기에 프리팹을 넣어도 아무 일도 안 난다.\n" +
             "예고를 바꾸려면 EffectCatalog.asset 과 FX_Drop_Charge_* 프리팹을 볼 것.")]
    public GameObject jumpTelegraphPrefab;

    [Tooltip("착지점을 대상에게서 **이만큼 떨어뜨린다**(m). 0 이면 플레이어 위에 정확히 내려앉는다.\n" +
             "🔴 0 으로 두면 안 된다 — 보스 캡슐을 플레이어 캡슐 안에 꽂으면 물리 디페네트레이션이 " +
             "수평으로 못 밀어내고 **위로** 밀어내서 플레이어가 떠오른다(2026-08-13 관찰).\n" +
             "착지 AoE 반경(jumpAoeRadius)이 이 값보다 크면 데미지는 그대로 들어간다.")]
    [Min(0f)] public float jumpLandSeparation = 1.2f;

    // 🔴 확정 스펙(2026-08-13): **돌진과 점프어택은 플레이어를 넉백시킨다.**
    // ⚠️ **강도만 노출한다.** 넉백 진입점 `Unit.Knockback(방향, 강도)` 이 받는 것이 그것뿐이다.
    //    지속·경직을 노출해 두면 조절해도 아무 일이 없는 "고장난 노브"가 된다
    //    (`AttackInfo.knockbackDuration` 등은 이 경로에서 **읽히지 않는다** — 2026-08-13 실측).
    [Tooltip("착지 AoE 넉백 속도(m/s). 0 이면 밀지 않는다. 방향은 **보스 → 대상** 바깥쪽이다.\n" +
             "⚠️ 슈퍼아머 대상은 밀리지 않는다(Unit.Knockback 이 차단한다).")]
    [Min(0f)] public float jumpKnockbackStrength = 9f;

    // 🔴 알파 2개도 **지금은 아무도 읽지 않는다**(2026-09-09). 진하기가 파티클 프리팹 저작값이 됐다 —
    //    SO 에서 조절할 방법이 없다. 예고 이펙트가 확정되면 위 프리팹 필드와 함께 지운다
    //    (SO 에 저장된 값을 잃으므로 지울 때 팀장 확인).
    [Tooltip("⚠️ **미사용** — 큰 원(경계)은 FX_Drop_Charge_Boundary 프리팹의 파티클 저작값이 진하기를 정한다. " +
             "여기를 아무리 조절해도 화면은 바뀌지 않는다.")]
    [Range(0f, 1f)] public float jumpTelegraphOuterAlpha = 0.4f;
    [Tooltip("⚠️ **미사용** — 차오르는 작은 원은 FX_Drop_Charge_Indicator 프리팹의 파티클 저작값이 " +
             "진하기를 정한다. 여기를 아무리 조절해도 화면은 바뀌지 않는다.")]
    [Range(0f, 1f)] public float jumpTelegraphFillAlpha = 0.85f;

    [Header("송전기(차징) — 페이즈 진입 시퀀스")]
    [Tooltip("제한시간(초). 이 시간 안에 송전탑을 전부 부수지 못하면 레이지로 넘어간다.")]
    [Min(1f)] public float chargeTimeLimit = 20f;

    [Tooltip("차징 중 초당 획득 실드량. ⚠️ 실드 개념은 PlayerSkill 머지에서 Unit 에서 제거됐다 — " +
             "되살릴 방법이 정해질 때까지 소비되지 않는다(값만 보존).")]
    [Min(0f)] public float chargeShieldGainPerSec = 0f;

    [Tooltip("차징 중 보스 발밑에 깔리는 전기 장판 프리팹(AreaZone + NetworkObject). " +
             "⚠️ 정본의 zonePushForce(밀치기)는 플레이어 변위 경로가 없어 아직 적용되지 않는다 — 데미지만 나간다.")]
    public GameObject chargeZonePrefab;

    [Tooltip("송전탑 수 — 1인. 🔴 정본 확정: 1인 1 / 2인 2 / **3인 이상 4**. " +
             "레거시 ChargeController 의 Clamp(playerCount,1,3) + player3=3 이 3인에 3개만 켜는 버그였다.")]
    [Min(1)] public int chargePylonsSolo = 1;
    [Tooltip("송전탑 수 — 2인.")]
    [Min(1)] public int chargePylonsDuo = 2;
    [Tooltip("송전탑 수 — 3인 이상. 확정값 4.")]
    [Min(1)] public int chargePylonsTrioPlus = 4;

    [Header("레이지 — 송전기 실패 벌칙")]
    [Tooltip("돌진 횟수. 확정값 3.")]
    [Min(1)] public int rageDashCount = 3;

    [Tooltip("돌진 사이 간격(초).")]
    [Min(0f)] public float rageDashInterval = 0.5f;

    [Tooltip("돌진 1회 지속(초).")]
    [Min(0.1f)] public float rageDashDuration = 0.7f;

    [Tooltip("돌진 속도 배수. 이동 속도 = MoveSpeed × 이 값.")]
    [Min(1f)] public float rageDashSpeedMultiplier = 8f;

    [Tooltip("돌진 최대 거리(m). NavMesh 경계에서 잘린다(낭떠러지 진입 불가).")]
    [Min(1f)] public float rageDashMaxDistance = 16f;

    [Tooltip("돌진 1회 데미지. 0 이면 공격 행의 damage(그것도 0 이면 attackDamage).")]
    [Min(0)] public int rageDashDamage = 0;

    // ── 돌진(Dash) — S5 ───────────────────────────────────────────────────
    // 🔴 레이지와 값을 **따로** 둔다. 레이지는 송전기 실패 벌칙이라 강화판이어야 하는데,
    //    같은 필드를 쓰면 평상시 돌진이 벌칙과 동일해져 페이즈 연출의 의미가 사라진다.
    //
    // 참조 설계 = 오버워치 라인하르트 돌진. 확인한 규칙 3가지를 그대로 가져왔다:
    //   ① 끌고 가는 대상은 **첫 1명뿐**. 나머지는 스침 데미지만(히트 윈도우가 유닛당 1회 보장).
    //   ② **벽에 처박혔을 때만** 큰 데미지. 거리를 소진하고 멈추면 데미지 없이 기절만.
    //   ③ 슈퍼아머 대상은 밀리지 않는다 — `BeginRestrainedByInstigator` 의 bool 반환이 그 계약이다.
    [Header("돌진(Dash) — [S5]")]
    [Tooltip("돌진 지속(초).")]
    [Min(0.1f)] public float dashDuration = 0.7f;

    [Tooltip("돌진 속도 배수. 이동 속도 = MoveSpeed × 이 값. 레이지(8)보다 낮게 둔다.")]
    [Min(1f)] public float dashSpeedMultiplier = 6f;

    [Tooltip("돌진 최대 거리(m). NavMesh 경계에서 잘린다(낭떠러지 진입 불가).")]
    [Min(1f)] public float dashMaxDistance = 16f;

    [Tooltip("벽 충돌 데미지. 0 이면 공격 행의 damage(그것도 0 이면 attackDamage). " +
             "🔴 거리를 소진하고 멈추면 이 데미지는 나가지 않는다 — 벽에 처박는 것이 처벌이다.")]
    [Min(0)] public int dashDamage = 0;

    [Tooltip("끌고 가는 플레이어가 보스 정면에서 유지하는 거리(m). " +
             "🔴 목적지를 이 값 + 여유만큼 **앞당겨** 잡으므로, 이 값이 커질수록 보스가 벽에서 멀리 멈춘다.")]
    [Min(0f)] public float dashCarryFrontOffset = 1.8f;

    [Tooltip("벽 충돌 시 끌려온 대상에게 거는 기절(초). 슈퍼아머라 밀리지 않은 대상에겐 걸지 않는다.")]
    [Min(0f)] public float dashStunDuration = 1f;

    // 🔴 확정 스펙(2026-08-13): **돌진은 플레이어를 넉백시킨다.** 강도만 노출한다(위 점프 주석 참조).
    [Tooltip("벽 충돌 넉백 속도(m/s). 0 이면 밀지 않는다. 방향은 **보스 → 대상** 바깥쪽(= 벽 쪽)이다.\n" +
             "⚠️ 경로를 스치기만 한 대상은 여기 해당하지 않는다 — 끌려간(캐리된) 대상이 벽에 처박힐 때다.")]
    [Min(0f)] public float dashKnockbackStrength = 12f;

    [Header("Wells (23호에 종속) — [S8]")]
    [Tooltip("[S8] 폭탄 투척 주기(초). Wells 는 23호 NetworkObject 에 상태를 싣는다 — " +
             "스폰되지 않는 중첩 NetworkObject 라 자기 NetworkVariable 을 가질 수 없다.")]
    [Min(0f)] public float bombThrowInterval = 6f;

    [Tooltip("[S8] 폭탄 프리팹(서버 스폰).")]
    public GameObject bombPrefab;

    [Tooltip("[S8] 투척 초기 힘.")]
    [Min(0f)] public float throwImpulse = 8f;

    [Tooltip("[S8] 다발 투척 시 좌우 분산 각(도).")]
    [Min(0f)] public float spreadAngle = 15f;

    [Tooltip("[S8] 투척 상향각(도). 폭탄은 **대각선으로 던져 포물선**을 그린 뒤 바닥에서 수평 당구로 바뀐다. " +
             "🔴 소켓 회전에 의존하지 않는 이유: 아트 임포트 회전 때문에 고정 방향이 뒤집혀 있던 전례가 있다.")]
    [Range(0f, 80f)] public float bombThrowPitch = 35f;

    // ─── 공격 간격 ────────────────────────────────────────────────────────
    //
    // 🔴 확정 스펙(2026-08-13): **다음 공격까지가 너무 빠르다.** 조절 가능해야 한다.
    //    원인: 쿨다운이 **공격 행마다 따로**라 훅L(2.5s)·훅R(2.5s)·어퍼(3s)를 번갈아 쓰면
    //    쉬는 구간이 0 이 된다 — "전역 간격"이라는 개념이 아예 없었다.
    //    (`MonsterDataSO.attackSpeed` 는 행 쿨다운이 0 일 때만 쓰이는 폴백이라 보스에선 죽어 있다.)
    [Header("공격 간격 — 전역")]
    [Tooltip("공격이 **끝난 뒤** 다음 공격을 고르기까지 최소로 쉬는 시간(초). " +
             "행별 쿨다운과 **별개로** 항상 적용된다. 0 이면 쉬지 않는다(이전 동작).\n" +
             "⚠️ 페이즈 시퀀스(송전기) 진입은 이 간격을 기다리지 않는다 — 연출이 늦으면 안 되므로.")]
    [Min(0f)] public float globalAttackInterval = 1.5f;

    // ─── 폭탄 착지 지점 ───────────────────────────────────────────────────
    //
    // 🔴 확정 스펙(2026-08-13): 폭탄은 **랜덤하게** 던져지되 **무조건 room 안**에 떨어지고
    //    **벽에 걸쳐서도 안 된다.** 그래서 임펄스를 랜덤으로 주는 대신 **착지 지점을 먼저 뽑고
    //    속도를 역산**한다. room 의 정의는 **NavMesh(보행 가능 영역)** 다 — 돌진이 벽을 판정하는
    //    기준과 같다. 아래 값이 그 추첨 범위다.
    [Header("[S8] 폭탄 착지 — 무조건 room 안")]
    [Tooltip("보스로부터 최소 이 거리 밖에 떨어진다(m). 발밑에 쌓이는 것을 막는다.")]
    [Min(0f)] public float bombLandingMinDistance = 3f;

    [Tooltip("보스로부터 최대 이 거리 안에 떨어진다(m).")]
    [Min(1f)] public float bombLandingMaxDistance = 9f;

    [Tooltip("보행 영역 **가장자리에서 이만큼 안쪽**에만 떨어뜨린다(m). 이 값이 '벽에 걸치지 않는다'를 " +
             "만든다. 폭탄 반경 + 여유로 잡을 것.")]
    [Min(0f)] public float bombWallMargin = 1.2f;

    // ─── 차징(송전기) 중 근접 차단 오라 ───────────────────────────────────
    //
    // 🔴 확정 스펙(2026-08-13): 차징 동안 보스는 **송전탑들의 중심**으로 이동해 애니메이션을 하고,
    //    그동안 **주변 원형 범위**가 **주기적으로** 데미지 + 넉백을 줘 플레이어가 다가와 때리지
    //    못하게 한다. 크기는 점프어택과 비슷하게 잡는다(`jumpAoeRadius` 기준값 3.5).
    [Header("차징 오라 — 접근 차단")]
    [Tooltip("오라 반경(m). 점프어택(jumpAoeRadius)과 비슷하게 잡는다. 0 이면 오라를 쓰지 않는다.")]
    [Min(0f)] public float chargeAuraRadius = 3.5f;

    [Tooltip("타격 주기(초). 범위 안에 머무는 동안 이 간격으로 계속 맞는다.")]
    [Min(0.1f)] public float chargeAuraInterval = 1f;

    [Tooltip("1회 데미지.")]
    [Min(0)] public int chargeAuraDamage = 20;

    [Tooltip("넉백 속도(m/s). 0 이면 밀지 않는다. 방향은 **보스 → 대상** 바깥쪽이다.\n" +
             "⚠️ 강도만 있다 — 넉백 진입점이 강도만 받는다(위 점프 넉백 주석 참조).")]
    [Min(0f)] public float chargeAuraKnockbackStrength = 8f;

    [Tooltip("오라 범위 표시용 프리팹(AoeTelegraph). 비우면 범위가 보이지 않는다 — " +
             "플레이어가 크기를 알아야 피할 수 있으므로 배선을 권장한다. " +
             "점프 예고와 같은 프리팹(JumpTelegraph)을 그대로 써도 된다.")]
    public GameObject chargeAuraTelegraphPrefab;

    [Tooltip("차징 위치로 이동할 때 이 거리 안에 들어오면 도착으로 본다(m).")]
    [Min(0.1f)] public float chargeMoveArriveDistance = 0.6f;

    [Tooltip("차징 위치로 이동할 때 이동속도 배수(moveSpeed × 이 값). **빠르게 가야 한다**(확정 스펙).\n" +
             "⚠️ 배수만 올려서는 안 빨라진다 — 가속도도 함께 올린다(코드가 돌진과 같은 값을 쓴다).")]
    [Min(1f)] public float chargeMoveSpeedMultiplier = 3f;

    [Tooltip("차징 위치로 이동하는 데 허용하는 최대 시간(초). 넘으면 **그 지점으로 워프**한 뒤 차징한다 " +
             "— 제자리에서 차징하면 오라 범위와 연출 기준이 어긋나기 때문이다(확정 스펙).")]
    [Min(0.5f)] public float chargeMoveTimeout = 4f;

    // ─── 폭발 장판(FireFloor) ─────────────────────────────────────────────
    //
    // 🔴 **0 = 프리팹 값을 그대로 쓴다.** 값을 넣은 항목만 덮어쓴다.
    //    이 규약을 쓰는 이유: `AreaZone` 은 "타입별 수치는 프리팹으로 저작"이 원래 설계라
    //    (컴포넌트 헤더 주석) SO 가 무조건 이기게 만들면 **정본이 둘**이 된다. 기존 애셋에는
    //    이 필드들이 없어 코드 초기값(0)이 적용되므로, 이관 자체로는 동작이 바뀌지 않는다.
    //
    // ⚠️ 주입은 **스폰 전에만** 유효하다 — `AreaZone.OnNetworkSpawn` 이 수명 타이머를 시작하기
    //    때문이다. 그래서 `SpawnOrGrow` 의 Instantiate↔Spawn 사이에서 적용한다(`ApplyTuning`).
    //    이미 스폰된 장판에 값을 밀면 타이머가 프리팹 값으로 이미 흐른 뒤라 의미가 깨진다.
    [Header("폭발 장판(FireFloor) — 0 이면 프리팹 값")]
    [Tooltip("장판 스폰 반경(m). 0 = 프리팹 값.")]
    [Min(0f)] public float fireZoneRadius;

    [Tooltip("장판 성장 상한 반경(m). 0 = 프리팹 값.")]
    [Min(0f)] public float fireZoneMaxRadius;

    [Tooltip("장판 수명(초). 0 = 프리팹 값. 확정값은 10초다.")]
    [Min(0f)] public float fireZoneLifetime;

    [Tooltip("겹쳐 성장할 때 수명을 다시 채우나. UsePrefab = 프리팹 값을 건드리지 않는다.")]
    public AreaZoneToggleOverride fireZoneRefreshLifetimeOnGrow = AreaZoneToggleOverride.UsePrefab;
}

/// <summary>
/// bool 값의 3상태 덮어쓰기. SO 에서 "안 건드림"을 표현하기 위해 필요하다 —
/// 그냥 bool 이면 기본값 false 가 곧 "끄기 지시"가 돼 프리팹 저작을 조용히 뒤집는다.
/// </summary>
public enum AreaZoneToggleOverride
{
    UsePrefab = 0,
    ForceOn = 1,
    ForceOff = 2,
}
