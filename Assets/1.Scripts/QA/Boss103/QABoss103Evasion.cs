using System;
using System.Collections.Generic;

/// <summary>할 일 103 C3 위험 종류 — 로그 <c>pattern=</c> 토큰. 클라는 보스 공격 id 를 모르므로(서버 전용) 예고 모양으로 가른다.</summary>
public enum QABoss103ThreatKind
{
    /// <summary>끝점 부채꼴만(훅·어퍼 등).</summary>
    Cone = 0,
    /// <summary>전진 경로 띠 + 끝점 부채꼴(전진 공격).</summary>
    Lunge = 1,
    /// <summary>띠만(네모 공격 등).</summary>
    Band = 2,
    /// <summary>돌진 — 파랑(간파 가능) 띠.</summary>
    Dash = 3,
    /// <summary>레이지 돌진 — 빨강 띠, 채움 없이 즉시.</summary>
    RageDash = 4,
    /// <summary>잡기 — 바깥에서 안으로 차오르는 부채꼴.</summary>
    Grab = 5,
    /// <summary>23호 점프 착지 원.</summary>
    Jump = 6,
    /// <summary>전기 장판 예고 칸.</summary>
    Floor = 7,
    /// <summary>웰즈 자폭 드론 고정 원.</summary>
    Drone = 8,
    /// <summary>예고 모양 없는 보스 공격 시작(복제 상태 Attack 엣지).</summary>
    Generic = 9,
    /// <summary>C7e — 차징 오라(보스 둘레 원, 1초마다 피해·넉백). 클라 신호 = 방향 표시기 억제 + 점프 예고 없음.</summary>
    Aura = 10,
}

/// <summary>회피 결정 — 로그 <c>action=</c> 토큰(소문자).</summary>
public enum QABoss103DodgeAction
{
    /// <summary>피격 굴림(take) — 아무 회피 안 함.</summary>
    None = 0,
    Dash = 1,
    /// <summary>팔라딘 E(수호자의 의지) 방어.</summary>
    Guard = 2,
    /// <summary>회피 굴림이었지만 대시 충전이 없다.</summary>
    NoCharge = 3,
    /// <summary>회피 굴림이었지만 지금 행동이 대시로 끊기지 않는다(CC·끊을 수 없는 스킬).</summary>
    Blocked = 4,
    /// <summary>C7e — 피격 시각 전에 걸어서 범위 밖으로 나갔다(대시 충전을 쓰지 않음).</summary>
    Walk = 5,
}

/// <summary>
/// 할 일 103 C3 예고 회피 순수 규칙 — 예고 모양 분류·범위 판정(보스 로컬 좌표)·회피 방향·행동 선택·방어 유지 시간. Unity 의존 없음(EditMode <c>QABoss103EvasionTests</c>).
/// 보스 로컬 좌표: f = 보스 정면 방향 거리, r = 오른쪽(= Cross(up, 정면)) 방향 거리. 각도 +는 오른쪽(Y 축 시계 방향 — <c>Quaternion.AngleAxis(+, up)</c>).
/// </summary>
public static class QABoss103Evasion
{
    /// <summary>범위 판정 여유(m) — 플레이어 몸 반경 + 예고 중 보스 조준 보정 몫.</summary>
    public const float RangeMargin = 0.8f;

    /// <summary>예고 모양 없는 공격(<see cref="QABoss103ThreatKind.Generic"/>)의 위험 반경 — 보스 표면에서(m), 보스 앞 반원만.</summary>
    public const float GenericSurfaceRadius = 3.5f;

    /// <summary>방어 유지 = 남은 예고 + 이 여유(초) — 판정이 예고 끝 직후에 나간다.</summary>
    public const double GuardTailSeconds = 0.4;

    /// <summary>방어 유지 하한(초).</summary>
    public const double GuardMinSeconds = 0.3;

    /// <summary>데이터 최대 유지 시간을 못 읽을 때의 방어 안전 상한(초).</summary>
    public const double GuardCapFallbackSeconds = 3.0;

    /// <summary>
    /// 부채꼴 예고(<c>BossAttackConeTelegraph</c>) 필드 → 종류. 잡기(안쪽 채움) → 돌진(파랑 띠) → 레이지 돌진(빨강 띠·즉시) → 전진(띠+부채꼴) → 부채꼴 → 띠.
    /// </summary>
    public static QABoss103ThreatKind ClassifyTelegraph(bool interruptPalette, bool fillInward, float coneRadius, float pathRadius, float pathLength, float growTime)
    {
        bool cone = coneRadius > 0f;
        bool band = pathRadius > 0f && pathLength > 0f;
        if (fillInward && cone)
            return QABoss103ThreatKind.Grab;
        if (band && !cone)
        {
            if (interruptPalette)
                return QABoss103ThreatKind.Dash;
            return growTime <= 0f ? QABoss103ThreatKind.RageDash : QABoss103ThreatKind.Band;
        }
        if (cone && band)
            return QABoss103ThreatKind.Lunge;
        return QABoss103ThreatKind.Cone;
    }

    /// <summary>로그 토큰(소문자 스네이크).</summary>
    public static string Token(QABoss103ThreatKind kind)
    {
        switch (kind)
        {
            case QABoss103ThreatKind.Cone: return "cone";
            case QABoss103ThreatKind.Lunge: return "lunge";
            case QABoss103ThreatKind.Band: return "band";
            case QABoss103ThreatKind.Dash: return "dash";
            case QABoss103ThreatKind.RageDash: return "rage_dash";
            case QABoss103ThreatKind.Grab: return "grab";
            case QABoss103ThreatKind.Jump: return "jump";
            case QABoss103ThreatKind.Floor: return "floor";
            case QABoss103ThreatKind.Drone: return "drone";
            case QABoss103ThreatKind.Aura: return "aura";
            default: return "generic";
        }
    }

    public static string Token(QABoss103DodgeAction action)
    {
        switch (action)
        {
            case QABoss103DodgeAction.Dash: return "dash";
            case QABoss103DodgeAction.Guard: return "guard";
            case QABoss103DodgeAction.NoCharge: return "no_charge";
            case QABoss103DodgeAction.Blocked: return "blocked";
            case QABoss103DodgeAction.Walk: return "walk";
            default: return "none";
        }
    }

    // ── 범위 판정 ──────────────────────────────────────────────

    /// <summary>
    /// 경로 띠 안인가 — 보스 앞 <paramref name="forwardOffset"/> 부터 길이 <paramref name="length"/>, 반폭 <paramref name="halfWidth"/>,
    /// 가로 치우침 <paramref name="lateralOffset"/>(평행이동). 띠가 없으면(반폭·길이 0 이하) false.
    /// </summary>
    public static bool InBand(float f, float r, float forwardOffset, float length, float halfWidth, float lateralOffset, float margin = RangeMargin)
    {
        if (halfWidth <= 0f || length <= 0f)
            return false;
        if (Math.Abs(r - lateralOffset) > halfWidth + margin)
            return false;
        return f >= forwardOffset - margin && f <= forwardOffset + length + margin;
    }

    /// <summary>
    /// 부채꼴 안인가 — 꼭짓점 = 보스 앞 <paramref name="apexForward"/>, 축 = 정면에서 <paramref name="offsetAngleDeg"/>(+ 오른쪽),
    /// 반각 <paramref name="halfAngleDeg"/>(180 이상 = 원). 여유는 반경에 더하고, 각도는 그 거리에서 여유만큼 벌린다. 반경 0 이하면 false.
    /// </summary>
    public static bool InSector(float f, float r, float apexForward, float radius, float halfAngleDeg, float offsetAngleDeg, float margin = RangeMargin)
    {
        if (radius <= 0f)
            return false;
        double pf = f - apexForward, pr = r;
        double dist = Math.Sqrt(pf * pf + pr * pr);
        if (dist > radius + margin)
            return false;
        if (halfAngleDeg >= 180f || dist <= margin)
            return true;
        double angle = Math.Atan2(pr, pf) * 180.0 / Math.PI;           // 정면 0, 오른쪽 +
        double delta = Math.Abs(NormalizeDeg(angle - offsetAngleDeg));
        double slack = Math.Asin(Math.Min(1.0, margin / dist)) * 180.0 / Math.PI;
        return delta <= halfAngleDeg + slack;
    }

    /// <summary>원(중심에서 <paramref name="dx"/>,<paramref name="dz"/>) 안인가.</summary>
    public static bool InCircle(float dx, float dz, float radius, float margin = RangeMargin) =>
        radius > 0f && dx * dx + dz * dz <= (radius + margin) * (radius + margin);

    /// <summary>예고 모양 없는 공격 — 보스 표면 <see cref="GenericSurfaceRadius"/> 안이고 보스 앞 반원(f ≥ 0 − 여유).</summary>
    public static bool InGeneric(float f, float surfaceDistance, float margin = RangeMargin) =>
        surfaceDistance <= GenericSurfaceRadius && f >= -margin;

    // ── 회피 방향 ──────────────────────────────────────────────

    /// <summary>
    /// 부채꼴·띠 예고의 회피 방향(보스 로컬, 단위 벡터). 사용자 확정: 콘 = 옆.
    /// <list type="bullet">
    /// <item>넓은 부채꼴(반각 90° 이상 — 앞 반원·원) = 꼭짓점 반대(옆으로는 못 나간다).</item>
    /// <item>띠가 있으면(돌진·전진·네모) = 띠 중심선에서 내가 있는 쪽 옆(정확히 중심이면 오른쪽).</item>
    /// <item>좁은 부채꼴만 = 축에서 내가 있는 쪽으로 축에 수직.</item>
    /// </list>
    /// </summary>
    public static void TelegraphEscapeLocal(float f, float r, float coneRadius, float halfAngleDeg, float offsetAngleDeg, float apexForward,
                                            float pathRadius, float lateralOffset, out float ef, out float er)
    {
        bool cone = coneRadius > 0f;
        if (cone && halfAngleDeg >= 90f)
        {
            Away(f - apexForward, r, -1f, 0f, out ef, out er);
            return;
        }
        if (pathRadius > 0f || !cone)
        {
            ef = 0f;
            er = r - lateralOffset >= 0f ? 1f : -1f;
            return;
        }
        double a = offsetAngleDeg * Math.PI / 180.0;
        float af = (float)Math.Cos(a), ar = (float)Math.Sin(a);
        float pf = f - apexForward, pr = r;
        // 축 (af, ar) 에 수직인 두 방향 중 내 쪽(점과 내적 ≥ 0).
        float qf = -ar, qr = af;
        if (qf * pf + qr * pr < 0f)
        {
            qf = -qf;
            qr = -qr;
        }
        ef = qf;
        er = qr;
    }

    /// <summary>중심에서 나가는 방향(단위). 겹쳐 있으면 <paramref name="fallbackX"/>,<paramref name="fallbackZ"/>(정규화).</summary>
    public static void Away(float dx, float dz, float fallbackX, float fallbackZ, out float x, out float z)
    {
        double len = Math.Sqrt(dx * dx + dz * dz);
        if (len < 1e-4)
        {
            dx = fallbackX;
            dz = fallbackZ;
            len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-4)
            {
                x = 1f;
                z = 0f;
                return;
            }
        }
        x = (float)(dx / len);
        z = (float)(dz / len);
    }

    /// <summary>보스 로컬(ef 정면, er 오른쪽) → 월드 평면. 오른쪽 = (fwdZ, −fwdX).</summary>
    public static void LocalToWorld(float fwdX, float fwdZ, float ef, float er, out float x, out float z)
    {
        x = fwdX * ef + fwdZ * er;
        z = fwdZ * ef - fwdX * er;
    }

    /// <summary>월드 평면 차이 → 보스 로컬(f 정면, r 오른쪽). 정면은 단위 벡터여야 한다.</summary>
    public static void WorldToLocal(float fwdX, float fwdZ, float dx, float dz, out float f, out float r)
    {
        f = dx * fwdX + dz * fwdZ;
        r = dx * fwdZ - dz * fwdX;
    }

    // ── 행동 선택 ──────────────────────────────────────────────

    /// <summary>
    /// 방어(팔라딘 E)로 막을 수 있는 위험인가. C7e — 방어는 공격자 루트 위치로 판정하고 장판·드론의 공격자는 보스라(<c>BossPatternTargets</c>)
    /// 보스를 향하면 장판도 막는다. 잡기는 피해가 아니라 끌어당김 판정(예고 끝 순간 부채꼴 안)이라, 오라는 계속 도는 원이라 방어로 못 버틴다 — 걷기·대시만.
    /// </summary>
    public static bool IsGuardable(QABoss103ThreatKind kind) => kind != QABoss103ThreatKind.Grab && kind != QABoss103ThreatKind.Aura;

    /// <summary>
    /// 반응 지연 뒤 행동. 피격 굴림 = 없음. 회피 굴림이면 방어 가능(팔라딘·막을 수 있는 위험·E 쿨·바쁘지 않음) → 방어,
    /// 아니면 충전 없음 → no_charge, 지금 행동이 대시로 안 끊김 → blocked, 그 밖 → 대시.
    /// </summary>
    public static QABoss103DodgeAction ChooseAction(bool dodge, bool guardUsable, bool dashHasCharge, bool dashAllowedNow)
    {
        if (!dodge)
            return QABoss103DodgeAction.None;
        if (guardUsable)
            return QABoss103DodgeAction.Guard;
        if (!dashHasCharge)
            return QABoss103DodgeAction.NoCharge;
        return dashAllowedNow ? QABoss103DodgeAction.Dash : QABoss103DodgeAction.Blocked;
    }

    /// <summary>방어(홀드 모드) 유지 시간 = 남은 예고 + 꼬리, [하한, 상한]. 상한 0 이하면 <see cref="GuardCapFallbackSeconds"/>.</summary>
    public static double GuardHoldSeconds(double remainingSeconds, double capSeconds)
    {
        double cap = capSeconds > 0.0 ? capSeconds : GuardCapFallbackSeconds;
        double want = Math.Max(0.0, remainingSeconds) + GuardTailSeconds;
        return Math.Min(cap, Math.Max(GuardMinSeconds, want));
    }

    private static double NormalizeDeg(double deg)
    {
        deg %= 360.0;
        if (deg > 180.0) deg -= 360.0;
        if (deg < -180.0) deg += 360.0;
        return deg;
    }
}

/// <summary>
/// 할 일 103 C7e — 위험별 피격 시각 추정과 회피 시각 계산(순수, EditMode <c>QABoss103EvasionTests</c>). 시각은 모두 '감지부터 초'.
/// 값은 dev No23_B 코드·데이터 조사(2026-10-27) — 클립 60fps 가정, 클라는 한 방향 지연만큼 실제가 빠르다.
/// <list type="bullet">
/// <item>부채꼴·띠(훅 예고 켠 보스 등): 예고 끝 = 출발, 타격 이벤트 0.233초 뒤.</item>
/// <item>돌진(파랑 띠): 공격 진입 1.5초 뒤 출발(이어치기 돌진은 띠가 0.7초라도 출발은 1.5초) → 19.5m/s 로 나까지. 레이지 돌진 = 띠와 동시 출발, 15m/s.</item>
/// <item>잡기: 예고 끝 순간 부채꼴 안이면 끌려간다(이게 회피 마감). 첫 피해는 그 1.96초 뒤.</item>
/// <item>점프: 원(1.2초)이 다 찬 뒤 착지 클립 타격 0.39초. 장판: 예고 시간 끝 1회. 드론: 고정 + 1.0초 1회.</item>
/// <item>예고 없는 공격(훅·어퍼 — No23 은 <c>hookTelegraphMarkers</c> 꺼짐): 공격 진입 → 첫 타 훅 0.93·어퍼 0.73초, 감지는 진입 + 0.2초.
///   이어치기 타(앞 공격 진입 0.9초 안)는 진입 0.23초 뒤라 감지 때 이미 늦다.</item>
/// </list>
/// 대시 무적 = 서버 0.25초(<c>DashValidationPolicy</c>) → 대시 시작 = 마감 − 무적 절반(반응 지연 뒤). 방어 창은 시전 0.225초 뒤 열린다.
/// 간파는 시전 0.333초 뒤 판정(<c>Garen_Parry</c> 이벤트)이라 그때도 창이 열려 있어야 한다.
/// </summary>
public static class QABoss103HitTiming
{
    public const double DashInvulnSeconds = 0.25;
    public const double ConeReleaseHitSeconds = 0.233;
    public const double DashLaunchSeconds = 1.5;
    public const double DashTravelSpeed = 19.5;
    public const double RageDashSpeed = 15.0;
    /// <summary>돌진 몸 판정 앞쪽 몫(m) — 보스 중심부터 이만큼은 출발 순간 이미 닿는다.</summary>
    public const double DashBodyReach = 2.2;
    public const double GrabFirstDamageSeconds = 1.96;
    public const double JumpLandingHitSeconds = 0.39;
    /// <summary>예고 없는 공격 첫 타(감지부터) — 훅 0.73·어퍼 0.53 의 가운데(대시 무적 0.25초가 둘 다 덮는다).</summary>
    public const double GenericFirstHitSeconds = 0.62;
    /// <summary>예고 없는 이어치기 타(감지부터) — 진입 0.233초 − 감지 지연 0.2초.</summary>
    public const double GenericChainHitSeconds = 0.03;
    /// <summary>앞 예고 없는 공격 감지부터 이 안(초)의 새 공격 = 이어치기(타 간격 ≈ 0.53초).</summary>
    public const double GenericChainGapSeconds = 0.9;
    public const double GuardStartupSeconds = 0.225;
    /// <summary>방어 창이 피격보다 이만큼(초) 먼저 열리게 시전한다.</summary>
    public const double GuardLeadSeconds = 0.1;
    public const double ParryHitSeconds = 0.333;
    /// <summary>간파 판정 순간이 추정 닫힘보다 이만큼(초) 앞이어야 한다(RPC·프레임 여유).</summary>
    public const double ParryCloseMarginSeconds = 0.07;
    /// <summary>반응이 마감보다 이만큼(초) 넘게 늦으면 놓친 것(expired).</summary>
    public const double LateSlackSeconds = 0.05;

    /// <summary>
    /// 감지 → 첫 피해(초). <paramref name="lead"/> = 감지 때 남은 예고, <paramref name="grow"/> = 예고 전체 길이(부채꼴·띠),
    /// <paramref name="distance"/> = 보스 중심 → 나(m, 돌진류), <paramref name="chained"/> = 예고 없는 이어치기 타.
    /// </summary>
    public static double HitDelay(QABoss103ThreatKind kind, double lead, double grow, double distance, bool chained)
    {
        lead = Math.Max(0.0, double.IsNaN(lead) ? 0.0 : lead);
        switch (kind)
        {
            case QABoss103ThreatKind.Dash:
                return DashLaunchDelay(lead, grow) + Travel(distance, DashTravelSpeed);
            case QABoss103ThreatKind.RageDash:
                return lead + Travel(distance, RageDashSpeed);
            case QABoss103ThreatKind.Grab:
                return lead + GrabFirstDamageSeconds;
            case QABoss103ThreatKind.Jump:
                return lead + JumpLandingHitSeconds;
            case QABoss103ThreatKind.Floor:
            case QABoss103ThreatKind.Drone:
                return lead;
            case QABoss103ThreatKind.Generic:
                return chained ? GenericChainHitSeconds : GenericFirstHitSeconds;
            case QABoss103ThreatKind.Aura:
                return 0.0;
            default:
                return lead + ConeReleaseHitSeconds;
        }
    }

    /// <summary>회피 마감(감지부터 초) — 이때 무적이거나 범위 밖이어야 한다. 잡기만 피해가 아니라 예고 끝(끌어당김 판정)이다.</summary>
    public static double DeadlineDelay(QABoss103ThreatKind kind, double lead, double grow, double distance, bool chained) =>
        kind == QABoss103ThreatKind.Grab ? Math.Max(0.0, double.IsNaN(lead) ? 0.0 : lead) : HitDelay(kind, lead, grow, distance, chained);

    /// <summary>돌진 출발(감지부터) — 공격 진입 1.5초 뒤. 감지 때 예고 경과 = grow − lead 라 1.5 − 경과, 예고 끝보다 이르지 않다.</summary>
    public static double DashLaunchDelay(double lead, double grow)
    {
        double elapsed = Math.Max(0.0, grow - lead);
        return Math.Max(lead, DashLaunchSeconds - elapsed);
    }

    private static double Travel(double distance, double speed) =>
        double.IsNaN(distance) ? 0.0 : Math.Max(0.0, distance - DashBodyReach) / speed;

    /// <summary>대시 시작 시각 = 마감 − 무적 절반, 단 반응 지연 끝(<paramref name="readyAt"/>)보다 이르지 않다.</summary>
    public static double DashStartAt(double deadlineAt, double readyAt, double invulnSeconds = DashInvulnSeconds) =>
        Math.Max(readyAt, deadlineAt - invulnSeconds * 0.5);

    /// <summary>방어 시전 시각 = 마감 − 선딜 − 여유(창이 마감 전에 열린다), 단 반응 지연 끝보다 이르지 않다.</summary>
    public static double GuardStartAt(double deadlineAt, double readyAt) =>
        Math.Max(readyAt, deadlineAt - GuardStartupSeconds - GuardLeadSeconds);

    /// <summary>반응 지연이 마감을 넘겨 끝났다 — 굴림은 '피함'이었지만 늦었다.</summary>
    public static bool Missed(double readyAt, double deadlineAt) => readyAt > deadlineAt + LateSlackSeconds;

    /// <summary>간파 마지막 누름 시각 = 추정 닫힘 − 판정 선딜 − 여유.</summary>
    public static double CounterPressDeadline(double closeAt) => closeAt - ParryHitSeconds - ParryCloseMarginSeconds;
}

/// <summary>
/// 위험 1건마다 봇당 1회만 굴린다(사용자 확정). 굴린 위험 id 를 최근 <see cref="Memory"/> 개까지 기억한다 — 같은 id 는 다시 굴리지 않는다.
/// 굴림: [0,1) 균일 값이 패턴 피격 확률 이상이면 회피(= 확률 1 − 피격), 미만이면 피격.
/// </summary>
public sealed class QABoss103ThreatLedger
{
    public const int Memory = 128;

    private readonly HashSet<string> _seen = new HashSet<string>();
    private readonly Queue<string> _order = new Queue<string>();

    public int Rolls { get; private set; }
    public int Dodges { get; private set; }
    public int Takes { get; private set; }

    public bool HasRolled(string threatId) => threatId != null && _seen.Contains(threatId);

    /// <summary>처음 보는 위험이면 굴리고 true(<paramref name="dodge"/> = 회피 굴림). 이미 굴렸으면 false — rng 를 건드리지 않는다.</summary>
    public bool TryRoll(string threatId, Random rng, double hitChance, out bool dodge)
    {
        dodge = false;
        if (threatId == null || _seen.Contains(threatId))
            return false;
        _seen.Add(threatId);
        _order.Enqueue(threatId);
        while (_order.Count > Memory)
            _seen.Remove(_order.Dequeue());

        Rolls++;
        dodge = rng.NextDouble() >= hitChance;
        if (dodge) Dodges++;
        else Takes++;
        return true;
    }
}

/// <summary>
/// 할 일 103 C3 간파 순수 규칙 — 성공/실패 굴림의 누름 시각, 정면 판정, 창 길이 추정.
/// 성공 = 창 열림 + 반응 지연, 단 추정 닫힘 <see cref="CloseMarginMs"/> 전을 넘으면 그 안으로 당긴다. 실패 = 관측 닫힘 + <see cref="LatePressMs"/>.
/// </summary>
public static class QABoss103Counter
{
    /// <summary>성공 굴림 누름은 추정 닫힘보다 이만큼(ms) 앞이어야 한다.</summary>
    public const double CloseMarginMs = 80.0;

    /// <summary>실패 굴림 누름 = 관측 닫힘 뒤 이만큼(ms).</summary>
    public const double LatePressMs = 150.0;

    /// <summary>창 길이를 아직 못 잰 첫 창의 추정(ms) — 23호 창(돌진 1.5초·착지 창 등) 중 짧은 쪽에 맞춘 보수값.</summary>
    public const double DefaultWindowMs = 800.0;

    /// <summary>실패 굴림인데 창이 이 시간(ms) 넘게 안 닫히면 누르지 않고 버린다(관측 놓침 방어).</summary>
    public const double MissGiveUpMs = 6000.0;

    /// <summary>간파 정면 판정 기본(도) — 데이터 <c>counterFrontAngle</c> 을 못 읽을 때. dev No23 = 90(앞 반원).</summary>
    public const float DefaultFrontAngle = 90f;

    public static bool RollHit(Random rng, double successChance) => rng.NextDouble() < successChance;

    /// <summary>성공 굴림 누름 지연(창 열림부터, ms) = min(반응, 창 − 여유), 0 미만이면 0.</summary>
    public static double HitPressDelayMs(double reactMs, double windowMs, double marginMs = CloseMarginMs) =>
        Math.Max(0.0, Math.Min(reactMs, windowMs - marginMs));

    /// <summary>
    /// 보스 정면(<paramref name="fwdX"/>,<paramref name="fwdZ"/>)과 보스→나(<paramref name="toX"/>,<paramref name="toZ"/>) 사이 각이 <paramref name="limitDeg"/> 이하인가.
    /// 겹쳐 있으면 정면으로 본다(게임 <c>IsCounterFromFront</c> 와 같은 규칙).
    /// </summary>
    public static bool IsFront(float fwdX, float fwdZ, float toX, float toZ, float limitDeg)
    {
        double a = Math.Sqrt(fwdX * fwdX + fwdZ * fwdZ), b = Math.Sqrt(toX * toX + toZ * toZ);
        if (b < 1e-2 || a < 1e-6)
            return true;
        double cos = (fwdX * toX + fwdZ * toZ) / (a * b);
        cos = Math.Max(-1.0, Math.Min(1.0, cos));
        return Math.Acos(cos) * 180.0 / Math.PI <= limitDeg + 1e-6;
    }
}

/// <summary>
/// 23호 간파 창 길이 추정(창 길이는 복제되지 않는다) — <c>QAInterruptBot88</c> 처럼 관측한 열림→닫힘 간격을 최근 <see cref="Capacity"/> 개 모은다.
/// 88 은 최댓값을 쓰지만 103 은 창 종류(돌진·착지·레이드)가 섞여 <b>최솟값</b>을 쓴다 — 성공 굴림이 닫힘 뒤로 밀리지 않게 하는 보수 선택.
/// </summary>
public sealed class QABoss103WindowEstimate
{
    public const int Capacity = 5;
    /// <summary>이보다 긴 관측(초)은 버린다 — 열림을 놓친 뒤 늦게 본 닫힘 등.</summary>
    public const double MaxSampleSeconds = 6.0;

    private readonly Queue<double> _samples = new Queue<double>();

    public int Count => _samples.Count;

    public void Add(double seconds)
    {
        if (!(seconds > 0.0) || seconds > MaxSampleSeconds)
            return;
        _samples.Enqueue(seconds);
        while (_samples.Count > Capacity)
            _samples.Dequeue();
    }

    /// <summary>추정 창 길이(ms) — 표본 최솟값, 없으면 <see cref="QABoss103Counter.DefaultWindowMs"/>.</summary>
    public double EstimateMs()
    {
        if (_samples.Count == 0)
            return QABoss103Counter.DefaultWindowMs;
        double min = double.MaxValue;
        foreach (double s in _samples)
            if (s < min)
                min = s;
        return min * 1000.0;
    }
}
