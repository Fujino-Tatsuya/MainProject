using System.Collections.Generic;

/// <summary>
/// 몬스터 데이터 인스펙터의 "칸 사전" — 칸 이름(직렬화 이름) → 한국어 표시 이름 · 묶음.
/// PLAN-monster-data-inspector.md §3.
/// <para>
/// 🔴 여기는 **보이는 이름만** 바꾼다. 변수 이름·[Header]·[Tooltip]·[Range]·[Min] 은 런타임 코드에 그대로 둔다 —
/// 데이터 테이블 Export 가 Header·Tooltip 을, Verify 가 Range·Min 을 읽는다(10-19 교차검증).
/// </para>
/// <para>
/// 사전에 없는 칸은 맨 아래 "미분류" 묶음에 그대로 나온다 — 새 칸을 추가하고 여기를 깜빡해도 사라지지 않는다.
/// 묶음을 옮기거나 ★ 목록을 바꾸려면 아래 표의 해당 줄만 고친다.
/// </para>
/// </summary>
public static class MonsterInspectorCatalog
{
    public sealed class Group
    {
        public Group(string key, string title, bool defaultOpen, string note = null, bool dimmed = false)
        {
            Key = key;
            Title = title;
            DefaultOpen = defaultOpen;
            Note = note;
            Dimmed = dimmed;
        }

        public string Key { get; }
        public string Title { get; }
        public bool DefaultOpen { get; }
        public string Note { get; }
        public bool Dimmed { get; }
    }

    public readonly struct Field
    {
        public Field(string group, string label, int order)
        {
            Group = group;
            Label = label;
            Order = order;
        }

        public string Group { get; }
        public string Label { get; }

        /// <summary>묶음 안에서 그리는 순서 = 아래 표에 적은 순서.</summary>
        public int Order { get; }
    }

    public const string Favorite = "fav";
    public const string Attacks = "attacks";
    public const string Combos = "combos";
    public const string Combat = "combat";
    public const string Ranged = "ranged";
    public const string AttackDetail = "attackDetail";
    public const string Phase = "phase";
    public const string Aggro = "aggro";
    public const string Charge = "charge";
    public const string Anim = "anim";
    public const string Tech = "tech";
    public const string Unused = "unused";
    public const string Unclassified = "unclassified";

    /// <summary>그리는 순서. 칸이 하나도 없는 묶음(일반 몬스터의 보스 묶음 등)은 건너뛴다.</summary>
    public static readonly IReadOnlyList<Group> Groups = new[]
    {
        new Group(Favorite, "★ 자주 고치는 것", true),
        new Group(Attacks, "공격 목록", true, "행 순서 = 쿨다운 슬롯 번호. 행은 끝에만 추가하고 순서를 바꾸지 않는다."),
        new Group(Combos, "콤보", true),
        new Group(Combat, "전투 기본 · 간파 · 그로기", false),
        new Group(Ranged, "원거리 몬스터 전용", false, "근접·보스는 대부분 쓰지 않는다. 투사체·낙하 공격 몬스터용."),
        new Group(AttackDetail, "공격별 피해 · 넉백 · 세부", false),
        new Group(Phase, "페이즈 · 레이드", false),
        new Group(Aggro, "어그로 · 인지", false),
        new Group(Charge, "송전기 · 장판 · 드론 연결", false),
        new Group(Anim, "연출 · 애니메이션 이름", false, "애니메이터 상태명·파라미터명은 틀려도 조용히 안 나온다 — 스폰 시 로그로 검증."),
        new Group(Tech, "기술값", false, "아트 측정 도구가 채우는 값 · 성능 설정. 보통 손대지 않는다."),
        new Group(Unused, "⚠️ 미사용", false, "지금 코드가 효과를 내지 않는 칸. 값은 보존된다 — xlsx 행 정리 후 삭제 예정.", true),
        new Group(Unclassified, "미분류 (사전에 없는 새 칸)", true, "MonsterInspectorCatalog.cs 에 한 줄 추가하면 제자리로 간다."),
    };

    /// <summary>MonsterDataSO · BossDataSO 의 최상위 칸.</summary>
    public static readonly IReadOnlyDictionary<string, Field> Fields = Build(new (string, string, string)[]
    {
        // ── ★ 자주 고치는 것 (10-19 초안 — git·xlsx 변경 이력 기준) ──
        (Favorite, "maxHp", "최대 HP"),
        (Favorite, "attackDamage", "공격력"),
        (Favorite, "attackSpeed", "공격 속도 (애니 배율)"),
        (Favorite, "moveSpeed", "이동 속도"),
        (Favorite, "chaseSpeed", "추격 속도"),
        (Favorite, "attackCooldown", "공격 간격 (공격 후 휴식)"),
        (Favorite, "defense", "방어"),
        (Favorite, "groggyDuration", "그로기 시간"),
        (Favorite, "vulnerableDuration", "취약 시간"),
        (Favorite, "breakDuration", "제압 시간"),
        (Favorite, "globalAttackInterval", "전역 공격 간격"),

        // ── 공격 목록 · 선택기 ──
        (Attacks, "attacks", "공격 목록"),
        (Attacks, "repeatPenalty", "직전 공격 가중치 감쇠"),
        (Attacks, "repeatBlockAfter", "같은 공격 연속 제한"),

        // ── 콤보 ──
        (Combos, "combos", "콤보 목록"),
        (Combos, "openingCombo", "개전 콤보 (전투 시작 1회)"),
        (Combos, "comboStepInterval", "콤보 타 사이 간격"),
        (Combos, "comboChaseGrace", "다음 타 추격 유예"),
        (Combos, "raidOpeningStepInterval", "레이드 ① 개전 타 간격"),
        (Combos, "comboDashWindup", "즉시 연결 — 후속 돌진 준비(간파 창)"),
        (Combos, "comboGrabTelegraph", "즉시 연결 — 후속 잡기 예고"),
        (Combos, "comboGrabTelegraphAfterDash", "즉시 연결 — 돌진 다음 잡기 예고"),
        (Combos, "comboHookFollowThrough", "즉시 연결 — 훅·어퍼 히트 뒤 더 재생"),
        (Combos, "singleHookUpper", "훅·어퍼 단타 사용"),
        (Combos, "comboJumpRecovery", "즉시 연결 — 점프 뒤 착지 후딜"),
        (Combos, "comboDashRecovery", "즉시 연결 — 돌진 뒤 복귀"),
        (Combos, "hookTelegraphMarkers", "훅·어퍼 바닥 예고 표시"),

        // ── 전투 기본 · 간파 · 그로기 ──
        (Combat, "archetype", "유형"),
        (Combat, "rank", "등급"),
        (Combat, "attackRange", "공격 사거리"),
        (Combat, "attackWindup", "공격 선딜 (예비동작 길이)"),
        (Combat, "attackDuration", "공격 지속 (모션 길이)"),
        (Combat, "cancelWindupIfTargetLeavesRange", "선딜 중 이탈하면 공격 취소"),
        (Combat, "engageAttackDelay", "사거리 진입 후 첫 공격 지연"),
        (Combat, "turnSpeed", "회전 속도 (0 = 즉시)"),
        (Combat, "maxGroggyCount", "그로기 누적 임계"),
        (Combat, "startsWithSuperArmor", "스폰부터 슈퍼아머"),
        (Combat, "hasSuperArmorWhileAttacking", "공격 중 슈퍼아머"),
        (Combat, "hitStunDuration", "피격 경직"),
        (Combat, "despawnDelay", "사망 후 디스폰 지연"),
        (Combat, "counterFrontAngle", "간파 인정 정면 각도"),
        (Combat, "backAttackAngle", "백어택 반각"),
        (Combat, "counterGaugeStep", "간파 1회 게이지 감소"),
        (Combat, "environmentGaugeStep", "환경 1회 게이지 감소"),
        (Combat, "suppressDamageMultiplier", "제압 중 받는 피해 배율"),

        // ── 원거리 몬스터 전용 ──
        (Ranged, "projectilePrefab", "투사체 프리팹"),
        (Ranged, "projectileSpeed", "투사체 속도"),
        (Ranged, "projectileLifetime", "투사체 수명"),
        (Ranged, "minStandoff", "최소 유지 거리 (가까우면 후퇴)"),
        (Ranged, "projectileArcHeight", "포물선 높이 (0 = 직선)"),
        (Ranged, "projectileSplashRadius", "스플래시 반경"),
        (Ranged, "rangedStyle", "공격 방식"),
        (Ranged, "skyStrikeTelegraphTime", "낙하 공격 예고 시간"),
        (Ranged, "skyStrikeFallTime", "낙하 구간"),
        (Ranged, "skyStrikeFloorTolerance", "낙하 높이 허용"),
        (Ranged, "skyStrikeLosBlockMask", "시야 차단 레이어"),
        (Ranged, "repositionBetweenAttacks", "쿨 중 재배치"),
        (Ranged, "retreatMinDuration", "후퇴 최소 시간"),
        (Ranged, "repositionMinDuration", "재배치 최소 시간"),

        // ── 공격별 피해 · 넉백 · 세부 ──
        (AttackDetail, "grabCycleSpeed", "잡기 사이클 속도 배수"),
        (AttackDetail, "grabRadius", "잡기 반경"),
        (AttackDetail, "grabPullDuration", "잡기 끌어당김 시간"),
        (AttackDetail, "grabCatchDuration", "잡기 붙잡기까지 시간"),
        (AttackDetail, "grabPullKnockback", "잡기 실패자 밀어내기"),
        (AttackDetail, "grabHoldDuration", "잡기 유지 시간"),
        (AttackDetail, "grabTickInterval", "잡기 틱 간격"),
        (AttackDetail, "grabTickDamage", "잡기 틱 피해"),
        (AttackDetail, "grabSlamCount", "내려치기 횟수"),
        (AttackDetail, "grabSlamDamage", "내려치기 피해"),
        (AttackDetail, "grabThrowDuration", "던지기 모션 시간"),
        (AttackDetail, "grabThrowDamage", "던지기 피해"),
        (AttackDetail, "grabEndDuration", "놓아주기 시간"),
        (AttackDetail, "grabRecoveryDuration", "잡기 후 복귀"),
        (AttackDetail, "jumpTakeoffDuration", "점프 이륙 시간"),
        (AttackDetail, "jumpHoverTime", "점프 체공 시간"),
        (AttackDetail, "jumpLandingDuration", "점프 착지 길이"),
        (AttackDetail, "jumpRecoveryDuration", "점프 착지 후 경직"),
        (AttackDetail, "jumpAoeRadius", "점프 착지 반경"),
        (AttackDetail, "jumpLandingDamage", "점프 착지 피해"),
        (AttackDetail, "jumpLandSeparation", "점프 착지 이격 거리"),
        (AttackDetail, "jumpKnockbackStrength", "점프 넉백"),
        (AttackDetail, "dashDuration", "돌진 시간"),
        (AttackDetail, "dashSpeedMultiplier", "돌진 속도 배수"),
        (AttackDetail, "dashMaxDistance", "돌진 최대 거리"),
        (AttackDetail, "dashDamage", "돌진 피해"),
        (AttackDetail, "dashCarryFrontOffset", "돌진 운반 앞 간격"),
        (AttackDetail, "dashStunDuration", "돌진 기절"),
        (AttackDetail, "dashKnockbackStrength", "돌진 넉백"),
        (AttackDetail, "rageDashCount", "레이지 돌진 횟수"),
        (AttackDetail, "rageDashInterval", "레이지 돌진 간격"),
        (AttackDetail, "rageDashDuration", "레이지 돌진 시간"),
        (AttackDetail, "rageDashSpeedMultiplier", "레이지 속도 배수"),
        (AttackDetail, "rageDashMaxDistance", "레이지 최대 거리"),
        (AttackDetail, "rageDashDamage", "레이지 피해"),
        (AttackDetail, "vulnerableKnockbackDistance", "취약 넉백 거리"),
        (AttackDetail, "vulnerableKnockbackTime", "취약 넉백 시간"),
        (AttackDetail, "wallReboundDistance", "벽 반동 거리"),
        (AttackDetail, "wallReboundTime", "벽 반동 시간"),

        // ── 페이즈 · 레이드 ──
        (Phase, "phases", "페이즈 표"),
        (Phase, "partyHpMultipliers", "인원별 HP 배수 (1인·2인·3인)"),
        (Phase, "playerHitDamageCapRatio", "한 방 피해 상한 (최대 HP 비율)"),
        (Phase, "raidScript", "레이드 각본 사용"),
        (Phase, "raidChargeTimeLimit", "② 전력 공급 제한시간"),
        (Phase, "raidDroneGuideCooldown", "② 드론 재지목 간격"),
        (Phase, "raidDischargeWarnTime", "② 방전 예고"),
        (Phase, "raidDischargeDamage", "② 방전 피해"),
        (Phase, "raidPylonHpMultiplier", "② 송전탑 HP 배율"),
        (Phase, "raidSelfDestructTime", "④ 자폭 시간"),
        (Phase, "raidSelfDestructGaugeDamage", "④ 자폭 실드량"),
        (Phase, "raidSelfDestructBlastWarn", "④ 대폭발 예고"),
        (Phase, "raidSelfDestructBlastDamage", "④ 대폭발 피해"),
        (Phase, "raidSelfDestructBlastLethal", "④ 대폭발 즉사"),

        // ── 어그로 · 인지 ──
        (Aggro, "detectionRadius", "인지 반경"),
        (Aggro, "detectionHeightTolerance", "인지 높이 허용"),
        (Aggro, "leashRadius", "리쉬 반경 (스폰 복귀)"),
        (Aggro, "loseTargetRadius", "추적 해제 반경"),
        (Aggro, "returnSpeedMultiplier", "복귀 속도 배수"),
        (Aggro, "retargetInterval", "어그로 재선정 간격 (일반)"),
        (Aggro, "aggroRetargetInterval", "어그로 재선정 주기 (보스)"),
        (Aggro, "aggroAvoidsRepeatTarget", "재선정 때 현재 대상 제외"),
        (Aggro, "threatAggro", "위협 어그로 사용"),
        (Aggro, "threatHalfLife", "위협 반감기"),
        (Aggro, "threatSwitchRatio", "위협 교체 배수"),
        (Aggro, "threatMinHold", "위협 최소 유지"),
        (Aggro, "threatMinScore", "위협 최소 점수"),
        (Aggro, "farPressureDistance", "원거리 압박 거리"),
        (Aggro, "farPressureWeightMultiplier", "원거리 압박 가중치 배수"),
        (Aggro, "farPressureCooldownScale", "원거리 압박 쿨 배수"),
        (Aggro, "playerScanRadius", "플레이어 탐색 반경 (송전탑 수 공용)"),

        // ── 송전기 · 장판 · 드론 연결 ──
        (Charge, "chargeTimeLimit", "송전기 제한시간"),
        (Charge, "chargePylonsSolo", "송전탑 수 — 1인"),
        (Charge, "chargePylonsDuo", "송전탑 수 — 2인"),
        (Charge, "chargePylonsTrioPlus", "송전탑 수 — 3인 이상"),
        (Charge, "chargeClearGroggyDuration", "송전기 성공 그로기"),
        (Charge, "chargeAuraRadius", "차징 오라 반경"),
        (Charge, "chargeAuraInterval", "차징 오라 주기"),
        (Charge, "chargeAuraDamage", "차징 오라 피해"),
        (Charge, "chargeAuraKnockbackStrength", "차징 오라 넉백"),
        (Charge, "chargeAuraTelegraphPrefab", "차징 오라 표시 프리팹"),
        (Charge, "chargeZonePrefab", "차징 전기 장판 프리팹"),
        (Charge, "electricFloor", "전기 장판 데이터"),
        (Charge, "wellsDrone", "자폭 드론 데이터"),

        // ── 연출 · 애니메이션 이름 ──
        (Anim, "animSpeedParam", "파라미터: 이동 속도"),
        (Anim, "attackTrigger", "트리거: 공격"),
        (Anim, "attackFinishTrigger", "트리거: 공격 2단계"),
        (Anim, "hitTrigger", "트리거: 피격"),
        (Anim, "groggyBool", "불: 그로기"),
        (Anim, "deathTrigger", "트리거: 사망"),
        (Anim, "locomotionState", "상태: 이동"),
        (Anim, "hitReactionState", "상태: 간파 리액션 (왼쪽)"),
        (Anim, "hitReactionStateRight", "상태: 간파 리액션 (오른쪽)"),
        (Anim, "grabCatchState", "상태: 잡기 붙잡기"),
        (Anim, "grabHoldState", "상태: 잡기 유지"),
        (Anim, "grabThrowState", "상태: 잡기 던지기"),
        (Anim, "grabEndState", "상태: 잡기 놓아주기"),
        (Anim, "jumpTakeoffState", "상태: 점프 이륙"),
        (Anim, "jumpHoverState", "상태: 점프 체공"),
        (Anim, "jumpLandingState", "상태: 점프 착지"),
        (Anim, "silentLandingState", "상태: 조용한 착지 (입장·복귀)"),
        (Anim, "chargeLoopStartFrame", "차징 루프 시작 프레임"),
        (Anim, "chargeLoopEndFrame", "차징 루프 끝 프레임"),
        (Anim, "locomotionFullBlendWhileMoving", "이동 중 이동 클립 100%"),
        (Anim, "locomotionAnimSpeedRange", "이동 애니 속도 범위"),
        (Anim, "locomotionPlaybackScale", "이동 애니 배율 (눈 튜닝)"),
        (Anim, "animatorControllerOverride", "애니메이터 컨트롤러 교체"),
        (Anim, "telegraphStyle", "공격 예고 색·마스크"),
        (Anim, "vulnerableTint", "취약 틴트 색"),

        // ── 기술값 ──
        (Tech, "avoidanceRadius", "길찾기 회피 반경"),
        (Tech, "obstacleAvoidance", "길찾기 회피 품질"),
        (Tech, "locomotionClipSpeed", "이동 클립 고유 속도 (측정)"),
        (Tech, "locomotionFullBlendSpeed", "블렌드 100% 속도 (측정)"),
        (Tech, "locomotionIdleCycleSeconds", "대기 클립 주기 (측정)"),
        (Tech, "locomotionMoveCycleSeconds", "이동 클립 주기 (측정)"),

        // ── ⚠️ 미사용 (PLAN §3.4) ──
        (Unused, "maxShield", "최대 실드"),
        (Unused, "grabThrowDistance", "던지기 거리 (변위 미적용)"),
        (Unused, "bombThrowInterval", "웰즈 폭탄 주기 (폭탄 제거됨)"),
        (Unused, "spreadAngle", "폭탄 퍼짐 각"),
        (Unused, "raidSelfDestructCounterBonus", "④ 간파 게이지 보너스 (실드로 대체)"),
        (Unused, "raidSelfDestructCounterInterval", "④ 간파 창 간격 (실드로 대체)"),
        (Unused, "raidSelfDestructCounterWindow", "④ 간파 창 길이 (실드로 대체)"),
        (Unused, "raidSelfDestructDamageReduction", "④ 자폭 중 피해 감소 (실드로 대체)"),
    });

    /// <summary>공격 행(BossAttackEntry) 안의 칸.</summary>
    public static readonly IReadOnlyDictionary<string, string> AttackFields = new Dictionary<string, string>
    {
        ["attackId"] = "공격",
        ["damage"] = "피해 (0 = 공격력)",
        ["cooldown"] = "쿨 (0 = 공격 간격)",
        ["minDistance"] = "거리 최소",
        ["maxDistance"] = "거리 최대",
        ["weight"] = "가중치 (0 = 안 뽑힘)",
        ["opensCounterWindow"] = "간파 창 열림",
        ["counterWindowDuration"] = "간파 창 길이",
        ["ignoreDistanceWindow"] = "거리 무시",
        ["attackTargeting"] = "노릴 대상",
        ["allowedFromPhase"] = "사용 시작 페이즈",
        ["superArmor"] = "슈퍼아머",
        ["animatorStateName"] = "애니 상태명",
        ["prepStateName"] = "준비동작 상태명",
        ["hitboxAnchorName"] = "판정 콜라이더 이름",
        ["lungeDistance"] = "전진 거리",
        ["lungeSpeedMultiplier"] = "전진 속도 배수",
        ["lungePathRadius"] = "전진 경로 판정 반경",
        ["coneRadius"] = "부채꼴 반경",
        ["coneAngle"] = "부채꼴 각",
        ["coneOffsetAngle"] = "부채꼴 좌우 치우침",
        ["boxWidth"] = "네모 폭",
        ["boxLength"] = "네모 길이",
        ["boxLateralOffset"] = "네모 좌우 이동",
        ["boxBackOffset"] = "네모 뒤쪽 길이",
        ["telegraphDuration"] = "예고 시간",
        ["telegraphFillInward"] = "예고 안쪽으로 채움",
        ["telegraphPoseNormalized"] = "예고 자세 · 재개 지점",
        ["hitEventFallbackNormalized"] = "히트 이벤트 폴백 지점",
    };

    /// <summary>공격 행을 펼쳤을 때 먼저 보이는 칸. 나머지는 행 안의 "세부" 에 접힌다. minDistance·maxDistance 는 한 줄로 그린다.</summary>
    public static readonly IReadOnlyList<string> AttackCoreFields = new[]
    {
        "attackId", "damage", "cooldown", "minDistance", "maxDistance", "weight", "opensCounterWindow", "counterWindowDuration",
    };

    public static readonly IReadOnlyDictionary<string, string> ComboFields = new Dictionary<string, string>
    {
        ["name"] = "이름",
        ["weight"] = "가중치",
        ["allowedFromPhase"] = "사용 시작 페이즈",
        ["cooldown"] = "콤보 쿨",
        ["steps"] = "순서",
        ["chainOnHit"] = "즉시 연결 (맞히는 순간 다음 타)",
    };

    public static readonly IReadOnlyDictionary<string, string> PhaseFields = new Dictionary<string, string>
    {
        ["hpThreshold"] = "HP 비율 이하에서 진입",
        ["sequence"] = "진입 시퀀스",
        ["damageMultiplier"] = "피해 배수",
        ["speedMultiplier"] = "추격 속도 배수",
    };

    public static string AttackName(BossAttackId id) => id switch
    {
        BossAttackId.LeftHook => "왼훅",
        BossAttackId.RightHook => "오른훅",
        BossAttackId.Upper => "어퍼",
        BossAttackId.Grab => "잡기",
        BossAttackId.Jump => "점프어택",
        BossAttackId.Dash => "돌진",
        BossAttackId.ChargeSequence => "송전기(차징)",
        BossAttackId.RageDash => "레이지 돌진",
        _ => id.ToString(),
    };

    /// <summary>칸이 들어갈 묶음. 사전에 없으면 미분류.</summary>
    public static string GroupOf(string fieldName) =>
        Fields.TryGetValue(fieldName, out Field field) ? field.Group : Unclassified;

    private static Dictionary<string, Field> Build((string group, string name, string label)[] rows)
    {
        var map = new Dictionary<string, Field>();
        foreach ((string group, string name, string label) in rows)
        {
            map.Add(name, new Field(group, label, map.Count)); // 같은 칸을 두 번 적으면 여기서 바로 터진다(정적 초기화)
        }

        return map;
    }
}
