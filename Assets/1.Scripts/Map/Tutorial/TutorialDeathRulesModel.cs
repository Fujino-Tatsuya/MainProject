using System;
using System.Collections.Generic;

// 튜토리얼 사망 규칙의 순수 규칙(Unity·NGO 비의존 — EditMode 테스트 대상).
// 원본: Docs/design/tutorial-scenario.md §2.5(D5)·§4 G11, PLAN-tutorial.md S11. 배선은 TutorialDeathRules(서버)가 한다.
// 🔴 시간은 전부 인자로 받는다(서버 시간 = ServerTime).

/// <summary>대기 중인 자동 부활 판정 결과.</summary>
public enum TutorialReviveVerdict : byte
{
    /// <summary>대기 기록 없음.</summary>
    None = 0,
    /// <summary>부활 시각 전.</summary>
    Waiting = 1,
    /// <summary>시각은 됐지만 아직 사망 연출(DeadPresentation) — Soul 이 되면 부활한다.</summary>
    WaitingForSoul = 2,
    /// <summary>보스 등장 연출 잠금 중 — 생명주기 전이가 막혀 있다. 풀리면 다시 판정한다(조용히 버리지 않는다).</summary>
    BlockedByCinematic = 3,
    /// <summary>지금 부활시킨다(<see cref="TutorialReviveOrder"/>). 기록은 지워진다.</summary>
    Revive = 4,
    /// <summary>기록을 버렸다 — 다른 경로로 이미 살아났거나(Alive) 완전 사망(PermanentDead).</summary>
    Dropped = 5,
}

/// <summary>부활 방법.</summary>
public struct TutorialReviveOrder
{
    /// <summary>목숨 1개 차감(G11 뒤 사망).</summary>
    public bool ConsumeLife;
    /// <summary>그 자리에서 부활(보스방 이동 중) — 안전 지점으로 옮기지 않는다.</summary>
    public bool InPlace;
    /// <summary>연출 잠금으로 한 번 이상 보류됐었다(해제 로그용).</summary>
    public bool WasBlocked;
}

/// <summary>
/// 튜토리얼 자동 부활 대기 장부(서버 한 곳).
///
/// 규칙(§2.5):
///  · 사망(Alive → DeadPresentation)마다 <see cref="ReviveDelaySeconds"/>(3초) 뒤 자동 부활을 예약한다.
///  · 목숨 차감 여부는 <b>사망 시점의 단계</b>로 정한다 — G11(보스방 도착)이 열렸으면 1개 차감, 아니면 없음.
///    G8(타이머 시작) 뒤라도 G11 전(존4·복도4·패드 대기·타이머 만료 강제 이동 중)이면 차감 없음.
///    위치는 보지 않는다. 대기 중 G11 이 열려도 바뀌지 않는다.
///  · 보스방 이동(패드·타이머 만료 강제 이동)이 시작되면 대기 중인 부활을 전부 '즉시·그 자리'로 바꾼다.
///    이동 중 새로 죽은 사람도 같다. 이동이 끝나도(취소 포함) 이미 바뀐 기록은 되돌리지 않는다 —
///    이동 뒤 옛 구간 지점으로 끌려가면 안 된다.
///  · 부활은 Soul 에서만 된다. 시각이 지났어도 사망 연출 중이면 Soul 을 기다린다.
///  · 연출 잠금 중이면 보류한다(첫 보류만 알림 — 로그 1회). 풀리면 다음 판정에서 부활.
///  · Alive(다른 경로로 부활 — 예: 강제 개시 부활)·PermanentDead 가 보이면 기록을 버린다.
/// </summary>
public sealed class TutorialDeathRulesModel
{
    public const double DefaultReviveDelaySeconds = 3d;

    private struct Pending
    {
        public double DueAt;
        public bool Free;
        public bool InPlace;
        public bool Blocked;
    }

    private readonly Dictionary<ulong, Pending> _pending = new Dictionary<ulong, Pending>();
    private readonly List<ulong> _ids = new List<ulong>();

    public TutorialDeathRulesModel(double reviveDelaySeconds = DefaultReviveDelaySeconds)
    {
        ReviveDelaySeconds = Math.Max(0d, reviveDelaySeconds);
    }

    public double ReviveDelaySeconds { get; }

    /// <summary>보스방 이동이 진행 중인가(<see cref="BeginMove"/> ~ <see cref="EndMove"/>).</summary>
    public bool IsMoveInProgress { get; private set; }

    public int PendingCount => _pending.Count;

    public bool HasPending(ulong clientId) => _pending.ContainsKey(clientId);

    /// <summary>
    /// 이 열린 게이트 상태(bit = <see cref="TutorialGate"/>)에서 죽으면 목숨이 차감되는가 = G11(보스방 도착) 열림.
    /// G8(타이머 시작)은 보지 않는다. 결과를 <see cref="RecordDeath"/> 의 bossRoomReached 로 넘긴다.
    /// </summary>
    public static bool IsLifeChargedAt(int openedGateMask) => (openedGateMask & (1 << (int)TutorialGate.G11_BossRoom)) != 0;

    /// <summary>대기 중인 client 목록(복사본 — 순회 중 기록이 지워져도 안전).</summary>
    public IReadOnlyList<ulong> PendingIds
    {
        get
        {
            _ids.Clear();
            _ids.AddRange(_pending.Keys);
            return _ids;
        }
    }

    /// <summary>
    /// 사망 기록. <paramref name="bossRoomReached"/> = G11(보스방 도착)이 열렸는가(단계 기준). 같은 client 가 다시 죽으면 새 기록으로 덮는다.
    /// </summary>
    public void RecordDeath(ulong clientId, double now, bool bossRoomReached)
    {
        _pending[clientId] = new Pending
        {
            DueAt = now + ReviveDelaySeconds,
            Free = !bossRoomReached,
            InPlace = IsMoveInProgress,
        };
    }

    /// <summary>목숨 차감 없는 사망을 기다리는 중인가 — 사망 목적지 훅이 "목숨 수와 무관하게 Soul" 을 정할 때 묻는다.</summary>
    public bool IsPenaltyFree(ulong clientId) => _pending.TryGetValue(clientId, out Pending p) && p.Free;

    /// <summary>보스방 이동 시작. 대기 중인 부활을 전부 즉시·그 자리로 바꾸고 그 수를 돌려준다. 이미 진행 중이면 0.</summary>
    public int BeginMove()
    {
        if (IsMoveInProgress) return 0;
        IsMoveInProgress = true;
        return ConvertAllToInPlace();
    }

    /// <summary>이동 끝(도착·취소). 이후 사망은 다시 3초·안전 지점. 이미 바뀐 기록은 그대로.</summary>
    public void EndMove() => IsMoveInProgress = false;

    /// <summary>대기 중인 부활을 전부 즉시·그 자리로 바꾼다(튜토리얼 종료 등). 바뀐 수.</summary>
    public int ConvertAllToInPlace()
    {
        int n = 0;
        foreach (ulong id in PendingIds)
        {
            Pending p = _pending[id];
            if (p.InPlace) continue;
            p.InPlace = true;
            _pending[id] = p;
            n++;
        }
        return n;
    }

    /// <summary>기록을 지운다(이탈 등). 있었으면 true.</summary>
    public bool Forget(ulong clientId) => _pending.Remove(clientId);

    /// <summary>
    /// 한 명의 대기 부활을 판정한다. <see cref="TutorialReviveVerdict.Revive"/>·<see cref="TutorialReviveVerdict.Dropped"/> 면 기록이 지워진다.
    /// <paramref name="firstBlock"/> = 이번이 처음 보류됐다(로그 1회용).
    /// </summary>
    public TutorialReviveVerdict Evaluate(ulong clientId, double now, PlayerLifeState state, bool cinematicLocked,
                                         out TutorialReviveOrder order, out bool firstBlock)
    {
        order = default;
        firstBlock = false;
        if (!_pending.TryGetValue(clientId, out Pending p)) return TutorialReviveVerdict.None;

        if (state == PlayerLifeState.Alive || state == PlayerLifeState.PermanentDead)
        {
            _pending.Remove(clientId);
            return TutorialReviveVerdict.Dropped;
        }

        if (!p.InPlace && now < p.DueAt) return TutorialReviveVerdict.Waiting;

        if (cinematicLocked)
        {
            firstBlock = !p.Blocked;
            p.Blocked = true;
            _pending[clientId] = p;
            return TutorialReviveVerdict.BlockedByCinematic;
        }

        if (state != PlayerLifeState.Soul) return TutorialReviveVerdict.WaitingForSoul;

        _pending.Remove(clientId);
        order = new TutorialReviveOrder { ConsumeLife = !p.Free, InPlace = p.InPlace, WasBlocked = p.Blocked };
        return TutorialReviveVerdict.Revive;
    }
}

/// <summary>부활 지점 후보(월드 XZ + 속한 구간).</summary>
public readonly struct TutorialRevivePoint
{
    public readonly float X, Z;
    public readonly TutorialArea Area;

    public TutorialRevivePoint(float x, float z, TutorialArea area)
    {
        X = x; Z = z; Area = area;
    }
}

/// <summary>위협(살아 있는 몬스터) — 위치와 인지 반경.</summary>
public readonly struct TutorialThreat
{
    public readonly float X, Z, Radius;

    public TutorialThreat(float x, float z, float radius)
    {
        X = x; Z = z; Radius = Math.Max(0f, radius);
    }
}

/// <summary>
/// 보스 진입 패드 범위(월드 XZ 투영) — 축 정렬 사각형(박스 콜라이더) 또는 원(구·캡슐). 런타임은 서버의 BossEnterTrigger 트리거 콜라이더 bounds 에서 만든다.
/// </summary>
public readonly struct TutorialPadArea
{
    public readonly float CenterX, CenterZ, HalfX, HalfZ, Radius;
    public readonly bool IsCircle;

    TutorialPadArea(float cx, float cz, float halfX, float halfZ, float radius, bool circle)
    {
        CenterX = cx; CenterZ = cz;
        HalfX = Math.Abs(halfX); HalfZ = Math.Abs(halfZ); Radius = Math.Abs(radius);
        IsCircle = circle;
    }

    public static TutorialPadArea Box(float minX, float minZ, float maxX, float maxZ) =>
        new TutorialPadArea((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f, (maxX - minX) * 0.5f, (maxZ - minZ) * 0.5f, 0f, false);

    public static TutorialPadArea Circle(float centerX, float centerZ, float radius) =>
        new TutorialPadArea(centerX, centerZ, 0f, 0f, radius, true);

    /// <summary>점이 범위를 <paramref name="margin"/> 만큼 넓힌 영역 안인가(사각형은 모서리도 직각으로 넓힌다 — 보수적).</summary>
    public bool Contains(float x, float z, float margin)
    {
        float dx = x - CenterX, dz = z - CenterZ;
        if (IsCircle)
        {
            float r = Radius + margin;
            return dx * dx + dz * dz <= r * r;
        }
        return Math.Abs(dx) <= HalfX + margin && Math.Abs(dz) <= HalfZ + margin;
    }

    public override string ToString() => IsCircle
        ? $"원 ({CenterX:0.##}, {CenterZ:0.##}) r {Radius:0.##}"
        : $"사각 ({CenterX:0.##}, {CenterZ:0.##}) {HalfX * 2f:0.##}×{HalfZ * 2f:0.##}";
}

/// <summary>
/// 자동 부활 지점 규칙 — "지금 단계 구간의 안전한 지점(몬스터 인지 범위 밖, 배리어 안쪽)"(§2.5).
///
///  · 지금 단계 구간 = 디렉터 단계(<see cref="TutorialStep"/>)의 구간(S12). 단 열린 게이트로 들어갈 수 있는 가장 앞 구간을 넘지 않는다.
///    <b>보스 진입 존은 뺀다</b> — 진입 패드가 있어 거기서 되살아나면 그 순간 보스방 카운트다운이 시작될 수 있다(G10 뒤엔 복도4).
///  · 개인 뒤 배리어(15 후속 C2): 그 플레이어가 지나온 최전방 구간보다 뒤로는 보내지 않는다 — 단계 구간이 그보다 뒤면 그 구간으로 올리고,
///    찾는 순서도 거기서 멈춘다. G10 을 지난 플레이어만 예외로 보스 진입 존에서 되살아난다(복도4 는 그 플레이어의 뒤 배리어 너머).
///  · 찾는 순서 = 지금 단계 구간 → 앞 구간들(거꾸로). 그 구간에 안전한 후보가 있으면 사망 지점에 가장 가까운 것.
///  · 어디에도 안전한 후보가 없으면 위협에서 가장 먼 후보(여유가 같으면 앞 순서 구간).
///  · 닫힌 구간·보스 진입 후보는 쓰지 않는다. 후보가 없으면 실패(호출자가 그 자리 부활로 물러선다).
///  · 보스 진입 패드 제외(15 후속 C3): 패드 범위 + <see cref="DefaultPadMargin"/> 안의 후보는 안전해도 '가장 먼 후보' 폴백에서도 쓰지 않는다
///    (부활·추락 복구만으로 보스방 카운트다운이 시작되지 않게). 그래서 남는 후보가 없으면 호출자가 <see cref="PadFallbackCandidates"/>
///    (복도4 출구 배리어 G10 바로 앞, 보스 진입 존 쪽)를 쓴다. 패드 정보가 없으면(비튜토리얼·패드 미발견) 기존 동작.
/// </summary>
public static class TutorialRevivePointRules
{
    public const float DefaultSpacing = 1.5f;
    public const float DefaultInset = 1f;
    /// <summary>패드 범위 밖으로 더 두는 여유(m).</summary>
    public const float DefaultPadMargin = 1f;
    /// <summary>패드 폴백 — G10 배리어 앞면에서 보스 진입 존 쪽으로 이만큼(m). 개인 뒤 배리어 여유보다 작으면 여유를 쓴다.</summary>
    public const float DefaultPadFallbackDistance = 1f;
    /// <summary>패드 폴백 — 앞 지점이 패드 영역이면 배리어를 따라 옆으로 옮겨 보는 간격(m)·배리어 박스 가로 끝에서 남길 여유(m).</summary>
    public const float PadFallbackLateralStep = 0.5f;
    public const float PadFallbackLateralInset = 1f;

    /// <summary>열린 배리어로 들어갈 수 있는 가장 앞 구간(존1 ~ 복도4) — 지금 단계 구간의 상한.</summary>
    public static TutorialArea ResolveStageArea(int openedGateMask)
    {
        TutorialArea stage = TutorialArea.Zone1;
        for (TutorialArea a = TutorialArea.Corridor1; a <= TutorialArea.Corridor4; a++)
        {
            if (!TutorialBarrierRules.IsAreaReachable(a, openedGateMask)) break;
            stage = a;
        }
        return stage;
    }

    /// <summary>
    /// 디렉터 단계의 구간(S12 — 단계가 존·복도 진행에 맞게 갱신된다). 보스 진입·종료는 복도4(패드 존 제외).
    /// 존 단계는 그 존에 파티가 처음 들어갈 때 걸리므로, 출구 게이트가 열렸어도 아직 아무도 다음 존에 안 들어갔으면 앞 구간이다.
    /// </summary>
    public static TutorialArea StepArea(TutorialStep step) => step switch
    {
        TutorialStep.Corridor1 => TutorialArea.Corridor1,
        TutorialStep.Zone2 => TutorialArea.Zone2,
        TutorialStep.Corridor2 => TutorialArea.Corridor2,
        TutorialStep.Zone3Practice or TutorialStep.Zone3Fight => TutorialArea.Zone3,
        TutorialStep.Corridor3 => TutorialArea.Corridor3,
        TutorialStep.Zone4Lore or TutorialStep.Zone4Combat => TutorialArea.Zone4,
        TutorialStep.Corridor4 or TutorialStep.BossEnter or TutorialStep.Finished => TutorialArea.Corridor4,
        _ => TutorialArea.Zone1,
    };

    /// <summary>
    /// 지금 단계 구간 = 디렉터 단계 구간. 단 배리어로 들어갈 수 없는 구간이면 들어갈 수 있는 가장 앞 구간으로 내린다(둘 중 앞쪽).
    /// 배리어 기준만 쓰면 출구 게이트가 열린 순간(아직 아무도 다음 존에 안 들어감) 다음 존에서 되살아나 그 존 첫 진입·기상을 앞당긴다.
    /// </summary>
    public static TutorialArea ResolveStageArea(TutorialStep step, int openedGateMask)
    {
        TutorialArea byStep = StepArea(step);
        TutorialArea byBarrier = ResolveStageArea(openedGateMask);
        return byStep < byBarrier ? byStep : byBarrier;
    }

    /// <summary>
    /// 그 플레이어 기준 지금 단계 구간 — 단계 구간(<see cref="ResolveStageArea(TutorialStep, int)"/>)이 그 플레이어가 지나온 최전방 구간
    /// (<see cref="TutorialBackBarrierRules.FrontmostPassedArea"/>)보다 뒤면 그 구간으로 올린다(뒤 배리어 너머에서 되살아나 갇히지 않게).
    /// G10 을 지난 플레이어는 보스 진입 존이 된다(복도4 로 내리면 자기 뒤 배리어에 갇힌다).
    /// </summary>
    public static TutorialArea ResolveStageArea(TutorialStep step, int openedGateMask, int passedMask)
    {
        TutorialArea stage = ResolveStageArea(step, openedGateMask);
        TutorialArea front = TutorialBackBarrierRules.FrontmostPassedArea(passedMask);
        return front > stage ? front : stage;
    }

    /// <summary>찾는 순서 — 배리어 기준 상한부터 존1 까지 거꾸로.</summary>
    public static List<TutorialArea> AreaOrder(int openedGateMask) => AreaOrderFrom(ResolveStageArea(openedGateMask), TutorialArea.Zone1);

    /// <summary>찾는 순서 — 지금 단계 구간(단계 기준, <see cref="ResolveStageArea(TutorialStep, int)"/>)부터 존1 까지 거꾸로.</summary>
    public static List<TutorialArea> AreaOrder(TutorialStep step, int openedGateMask) =>
        AreaOrderFrom(ResolveStageArea(step, openedGateMask), TutorialArea.Zone1);

    /// <summary>
    /// 찾는 순서(그 플레이어 기준) — <see cref="ResolveStageArea(TutorialStep, int, int)"/> 부터 그 플레이어가 지나온 최전방 구간까지 거꾸로.
    /// 뒤 배리어 너머 구간은 넣지 않는다. <paramref name="passedMask"/> = 0 이면 <see cref="AreaOrder(TutorialStep, int)"/> 와 같다.
    /// </summary>
    public static List<TutorialArea> AreaOrder(TutorialStep step, int openedGateMask, int passedMask) =>
        AreaOrderFrom(ResolveStageArea(step, openedGateMask, passedMask), TutorialBackBarrierRules.FrontmostPassedArea(passedMask));

    private static List<TutorialArea> AreaOrderFrom(TutorialArea stage, TutorialArea floor)
    {
        var order = new List<TutorialArea>();
        for (TutorialArea a = stage; a >= floor && a >= TutorialArea.Zone1; a--)
            order.Add(a);
        return order;
    }

    /// <summary>
    /// 구간 사각형 안쪽(가장자리에서 <paramref name="inset"/>)에 <paramref name="spacing"/> 간격 격자 점을 깐다(월드 XZ, 회전 반영).
    /// 안쪽 여유가 남지 않는 축은 가운데 한 줄.
    /// </summary>
    public static void SampleGrid(TutorialRect rect, float spacing, float inset, List<(float x, float z)> into)
    {
        spacing = Math.Max(0.1f, spacing);
        inset = Math.Max(0f, inset);
        float hx = rect.HalfX - inset, hz = rect.HalfZ - inset;

        double r = rect.YawDegrees * Math.PI / 180d;
        float c = (float)Math.Cos(r), s = (float)Math.Sin(r);

        int nx = hx > 0f ? (int)Math.Floor(2f * hx / spacing) + 1 : 1;
        int nz = hz > 0f ? (int)Math.Floor(2f * hz / spacing) + 1 : 1;
        float x0 = hx > 0f ? -(nx - 1) * spacing * 0.5f : 0f;
        float z0 = hz > 0f ? -(nz - 1) * spacing * 0.5f : 0f;

        for (int i = 0; i < nx; i++)
        for (int j = 0; j < nz; j++)
        {
            float lx = x0 + i * spacing, lz = z0 + j * spacing;
            into.Add((rect.CenterX + c * lx + s * lz, rect.CenterZ - s * lx + c * lz));
        }
    }

    /// <summary>위협 반경 밖까지의 최소 여유(m). 위협이 없으면 +∞, 반경 안이면 음수.</summary>
    public static float Clearance(float x, float z, IReadOnlyList<TutorialThreat> threats)
    {
        float best = float.PositiveInfinity;
        if (threats == null) return best;
        foreach (TutorialThreat t in threats)
        {
            float dx = x - t.X, dz = z - t.Z;
            best = Math.Min(best, (float)Math.Sqrt(dx * dx + dz * dz) - t.Radius);
        }
        return best;
    }

    /// <summary>
    /// 부활 지점을 고른다. <paramref name="index"/> = <paramref name="candidates"/> 안의 위치.
    /// <paramref name="safe"/> = 위협 반경 밖인가(false 면 '가장 먼 후보' 폴백).
    /// </summary>
    public static bool TrySelect(IReadOnlyList<TutorialRevivePoint> candidates, IReadOnlyList<TutorialThreat> threats,
                                 int openedGateMask, float deathX, float deathZ, out int index, out bool safe) =>
        TrySelect(candidates, threats, AreaOrder(openedGateMask), deathX, deathZ, out index, out safe);

    /// <summary>단계 기준(<see cref="AreaOrder(TutorialStep, int)"/>)으로 고른다 — 런타임(<see cref="TutorialDeathRules"/>)이 쓴다.</summary>
    public static bool TrySelect(IReadOnlyList<TutorialRevivePoint> candidates, IReadOnlyList<TutorialThreat> threats,
                                 TutorialStep step, int openedGateMask, float deathX, float deathZ, out int index, out bool safe) =>
        TrySelect(candidates, threats, AreaOrder(step, openedGateMask), deathX, deathZ, out index, out safe);

    /// <summary>
    /// 그 플레이어 기준(<see cref="AreaOrder(TutorialStep, int, int)"/>)으로 고른다 — 런타임(<see cref="TutorialDeathRules"/>)이 쓴다.
    /// 뒤 배리어 너머 구간의 후보는 안전해도 '가장 먼 후보' 폴백에서도 쓰지 않는다.
    /// </summary>
    public static bool TrySelect(IReadOnlyList<TutorialRevivePoint> candidates, IReadOnlyList<TutorialThreat> threats,
                                 TutorialStep step, int openedGateMask, int passedMask, float deathX, float deathZ,
                                 out int index, out bool safe) =>
        TrySelect(candidates, threats, AreaOrder(step, openedGateMask, passedMask), deathX, deathZ, null, 0f, out index, out safe);

    /// <summary>
    /// 그 플레이어 기준 + 보스 진입 패드 제외(C3) — 패드 범위 + <paramref name="padMargin"/> 안의 후보는 어떤 단계에서도 쓰지 않는다.
    /// <paramref name="pads"/> 가 null·빈 목록이면 패드 없는 오버로드와 같다. 실패하면 호출자가 <see cref="PadFallbackCandidates"/> 를 쓴다.
    /// </summary>
    public static bool TrySelect(IReadOnlyList<TutorialRevivePoint> candidates, IReadOnlyList<TutorialThreat> threats,
                                 TutorialStep step, int openedGateMask, int passedMask, float deathX, float deathZ,
                                 IReadOnlyList<TutorialPadArea> pads, float padMargin, out int index, out bool safe) =>
        TrySelect(candidates, threats, AreaOrder(step, openedGateMask, passedMask), deathX, deathZ, pads, padMargin, out index, out safe);

    /// <summary>점이 어느 패드 범위 + <paramref name="margin"/> 안인가. 패드 정보가 없으면 false.</summary>
    public static bool IsOnPad(IReadOnlyList<TutorialPadArea> pads, float x, float z, float margin)
    {
        if (pads == null) return false;
        for (int i = 0; i < pads.Count; i++)
            if (pads[i].Contains(x, z, margin)) return true;
        return false;
    }

    /// <summary>
    /// 패드 제외 뒤 후보가 없을 때의 안전 폴백 — 복도4 출구 배리어(<paramref name="g10"/>) 앞면에서 보스 진입 존 쪽으로
    /// max(<paramref name="distance"/>, <paramref name="clearance"/>) 인 가운데 지점부터. 그 지점이 패드 영역이면 더 안쪽(패드 쪽)으로 가지 않고
    /// ① 배리어 쪽으로 붙여(앞면에서 <paramref name="clearance"/> — 개인 뒤 배리어 콜라이더와 겹치지 않는 최소) 보고
    /// ② 배리어를 따라 옆으로(<see cref="PadFallbackLateralStep"/> 간격, 배리어 박스 가로 끝에서 <see cref="PadFallbackLateralInset"/> 안쪽까지) 옮긴다.
    /// 패드 영역 밖인 점만 선호 순서대로 <paramref name="into"/> 에 넣는다(호출자가 바닥·점유 검사로 첫 통과 점을 쓴다). 하나도 없으면 비어 있다.
    /// </summary>
    public static void PadFallbackCandidates(TutorialBarrierSpec g10, IReadOnlyList<TutorialPadArea> pads, float padMargin,
                                             float distance, float clearance, List<(float x, float z)> into)
    {
        clearance = Math.Max(0f, clearance);
        float far = Math.Max(distance, clearance);
        g10.Rect.GetWorldBounds(out float minX, out float minZ, out float maxX, out float maxZ);
        float face = g10.AlongX ? (g10.Forward > 0f ? maxX : minX) : (g10.Forward > 0f ? maxZ : minZ);
        float crossMin = g10.AlongX ? minZ : minX, crossMax = g10.AlongX ? maxZ : maxX;
        float crossMid = (crossMin + crossMax) * 0.5f;
        float lateralLimit = Math.Max(0f, (crossMax - crossMin) * 0.5f - PadFallbackLateralInset);
        int steps = (int)Math.Floor(lateralLimit / PadFallbackLateralStep);

        for (int k = 0; k <= 2 * steps; k++)
        {
            // 0, +1, −1, +2, −2 … 칸
            int n = (k + 1) / 2;
            float cross = crossMid + (k % 2 == 1 ? n : -n) * PadFallbackLateralStep;
            AddIfOffPad(g10, face + g10.Forward * far, cross, pads, padMargin, into);
            if (clearance < far) AddIfOffPad(g10, face + g10.Forward * clearance, cross, pads, padMargin, into);
        }
    }

    private static void AddIfOffPad(TutorialBarrierSpec spec, float along, float cross, IReadOnlyList<TutorialPadArea> pads, float padMargin,
                                    List<(float x, float z)> into)
    {
        float x = spec.AlongX ? along : cross, z = spec.AlongX ? cross : along;
        if (!IsOnPad(pads, x, z, padMargin)) into.Add((x, z));
    }

    private static bool TrySelect(IReadOnlyList<TutorialRevivePoint> candidates, IReadOnlyList<TutorialThreat> threats,
                                  List<TutorialArea> order, float deathX, float deathZ, out int index, out bool safe) =>
        TrySelect(candidates, threats, order, deathX, deathZ, null, 0f, out index, out safe);

    private static bool TrySelect(IReadOnlyList<TutorialRevivePoint> candidates, IReadOnlyList<TutorialThreat> threats,
                                  List<TutorialArea> order, float deathX, float deathZ,
                                  IReadOnlyList<TutorialPadArea> pads, float padMargin, out int index, out bool safe)
    {
        index = -1;
        safe = false;
        if (candidates == null || candidates.Count == 0) return false;

        foreach (TutorialArea area in order)
        {
            float bestDist = float.PositiveInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                TutorialRevivePoint p = candidates[i];
                if (p.Area != area || Clearance(p.X, p.Z, threats) < 0f || IsOnPad(pads, p.X, p.Z, padMargin)) continue;
                float dx = p.X - deathX, dz = p.Z - deathZ;
                float d = dx * dx + dz * dz;
                if (d < bestDist) { bestDist = d; index = i; }
            }
            if (index >= 0) { safe = true; return true; }
        }

        float bestClear = float.NegativeInfinity;
        int bestRank = int.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            int rank = order.IndexOf(candidates[i].Area);
            if (rank < 0 || IsOnPad(pads, candidates[i].X, candidates[i].Z, padMargin)) continue;
            float clear = Clearance(candidates[i].X, candidates[i].Z, threats);
            if (clear > bestClear || (clear == bestClear && rank < bestRank))
            {
                bestClear = clear; bestRank = rank; index = i;
            }
        }
        return index >= 0;
    }
}
