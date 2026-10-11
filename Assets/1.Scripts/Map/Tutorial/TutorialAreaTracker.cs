using System;
using System.Collections.Generic;

// 튜토리얼 구간 판정의 순수 규칙(Unity·NGO 비의존 — EditMode 테스트 대상).
// 원본: Docs/design/tutorial-scenario.md §1.1·§3 트리거 열, PLAN-tutorial.md S2, Docs/tech/tutorial-director.md §6.
// 구간 모양은 TutorialAreaLayout 이 StageTutorial 저작 데이터(슬롯·복도 바닥)에서 만들어 넣는다. 좌표는 월드 XZ.

/// <summary>튜토리얼 구간. 값 순서 = 동선 순서.</summary>
public enum TutorialArea : byte
{
    None = 0,
    Zone1 = 1,
    Corridor1 = 2,
    Zone2 = 3,
    Corridor2 = 4,
    Zone3 = 5,
    Corridor3 = 6,
    Zone4 = 7,
    Corridor4 = 8,
    BossEnter = 9,
}

/// <summary>
/// 월드 XZ 평면의 회전된 사각 영역. <see cref="YawDegrees"/> 는 Unity Y 회전과 같은 방향(위에서 볼 때 시계 방향).
/// 반폭은 회전 전(로컬) 축 기준이다.
/// </summary>
public readonly struct TutorialRect
{
    public readonly float CenterX, CenterZ, HalfX, HalfZ, YawDegrees;

    public TutorialRect(float centerX, float centerZ, float halfX, float halfZ, float yawDegrees = 0f)
    {
        CenterX = centerX; CenterZ = centerZ;
        HalfX = Math.Abs(halfX); HalfZ = Math.Abs(halfZ);
        YawDegrees = yawDegrees;
    }

    public static TutorialRect FromMinMax(float minX, float minZ, float maxX, float maxZ)
        => new TutorialRect((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f, (maxX - minX) * 0.5f, (maxZ - minZ) * 0.5f);

    /// <summary>
    /// 점이 영역 안인가. <paramref name="margin"/> &gt; 0 이면 바깥으로 넓혀서, &lt; 0 이면 안으로 좁혀서 본다
    /// (히스테리시스 — 들어갈 때 안쪽 여유 = 음수, 나갈 때 바깥 여유 = 양수).
    /// </summary>
    public bool Contains(float x, float z, float margin = 0f)
    {
        ToLocal(x, z, out float lx, out float lz);
        return Math.Abs(lx) <= HalfX + margin && Math.Abs(lz) <= HalfZ + margin;
    }

    public void ToLocal(float x, float z, out float lx, out float lz)
    {
        double r = YawDegrees * Math.PI / 180d;
        float c = (float)Math.Cos(r), s = (float)Math.Sin(r);
        float dx = x - CenterX, dz = z - CenterZ;
        // 월드 = R·로컬 (x' = c·x + s·z, z' = −s·x + c·z) 의 역 = 전치
        lx = c * dx - s * dz;
        lz = s * dx + c * dz;
    }

    /// <summary>모서리(0~3, 로컬 −−, +−, ++, −+ 순) 월드 좌표 — 기즈모용. margin 은 <see cref="Contains"/> 와 같은 뜻.</summary>
    public void GetCorner(int i, float margin, out float x, out float z)
    {
        float hx = Math.Max(0f, HalfX + margin), hz = Math.Max(0f, HalfZ + margin);
        float lx = (i == 1 || i == 2) ? hx : -hx;
        float lz = i >= 2 ? hz : -hz;
        double r = YawDegrees * Math.PI / 180d;
        float c = (float)Math.Cos(r), s = (float)Math.Sin(r);
        x = CenterX + c * lx + s * lz;
        z = CenterZ - s * lx + c * lz;
    }

    /// <summary>회전을 반영한 월드 축 정렬 범위.</summary>
    public void GetWorldBounds(out float minX, out float minZ, out float maxX, out float maxZ)
    {
        minX = minZ = float.MaxValue;
        maxX = maxZ = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            GetCorner(i, 0f, out float x, out float z);
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
        }
    }

    public override string ToString() => $"({CenterX:0.##}, {CenterZ:0.##}) {HalfX * 2f:0.##}×{HalfZ * 2f:0.##} yaw {YawDegrees:0}";
}

/// <summary>
/// 존 사이 복도 계산(순수). 두 존 사각형의 월드 범위 사이 틈 = 복도 길이 방향, 너비는 호출자가 준다(실제 복도 바닥 bounds).
/// </summary>
public static class TutorialCorridorGeometry
{
    /// <summary>
    /// 두 존 사이의 틈. 축 = 틈이 벌어진 축(X 또는 Z). <paramref name="crossMin"/>~<paramref name="crossMax"/> = 두 존이 가로 방향으로 겹치는 구간
    /// (복도 너비 후보를 거르는 범위). 틈이 없으면(겹치거나 대각선) false.
    /// </summary>
    public static bool TryGetGap(TutorialRect from, TutorialRect to, out bool alongX, out float gapMin, out float gapMax,
                                 out float crossMin, out float crossMax)
    {
        from.GetWorldBounds(out float aMinX, out float aMinZ, out float aMaxX, out float aMaxZ);
        to.GetWorldBounds(out float bMinX, out float bMinZ, out float bMaxX, out float bMaxZ);

        float gapX = Math.Max(aMinX, bMinX) - Math.Min(aMaxX, bMaxX);
        float gapZ = Math.Max(aMinZ, bMinZ) - Math.Min(aMaxZ, bMaxZ);
        alongX = gapX >= gapZ;
        float gap = alongX ? gapX : gapZ;
        if (alongX)
        {
            gapMin = Math.Min(aMaxX, bMaxX); gapMax = Math.Max(aMinX, bMinX);
            crossMin = Math.Max(aMinZ, bMinZ); crossMax = Math.Min(aMaxZ, bMaxZ);
        }
        else
        {
            gapMin = Math.Min(aMaxZ, bMaxZ); gapMax = Math.Max(aMinZ, bMinZ);
            crossMin = Math.Max(aMinX, bMinX); crossMax = Math.Min(aMaxX, bMaxX);
        }
        return gap > 0f && crossMax > crossMin;
    }

    /// <summary>
    /// 복도 사각형과 '중간 지점' 사각형. 중간 지점 = 복도 한가운데 선부터 <paramref name="to"/> 존 쪽 끝까지(복도의 뒤쪽 절반) —
    /// 샘플 간격·대시로 얇은 띠를 건너뛰는 일이 없게 절반 전체를 쓴다(출구 배리어가 있어 그 안에 머문다).
    /// </summary>
    public static void MakeCorridor(TutorialRect from, TutorialRect to, bool alongX, float gapMin, float gapMax,
                                    float crossMin, float crossMax, out TutorialRect corridor, out TutorialRect mid)
    {
        float mPoint = (gapMin + gapMax) * 0.5f;
        bool towardMax = alongX ? to.CenterX > from.CenterX : to.CenterZ > from.CenterZ;
        float midMin = towardMax ? mPoint : gapMin;
        float midMax = towardMax ? gapMax : mPoint;

        if (alongX)
        {
            corridor = TutorialRect.FromMinMax(gapMin, crossMin, gapMax, crossMax);
            mid = TutorialRect.FromMinMax(midMin, crossMin, midMax, crossMax);
        }
        else
        {
            corridor = TutorialRect.FromMinMax(crossMin, gapMin, crossMax, gapMax);
            mid = TutorialRect.FromMinMax(crossMin, midMin, crossMax, midMax);
        }
    }
}

/// <summary>
/// 플레이어 위치로 튜토리얼 구간을 판정한다(서버 한 곳).
///
/// 규칙:
///  · 판정 대상은 <see cref="PlayerLifeState.Alive"/> 뿐. 사망 연출·영혼·완전 사망은 판정하지 않고 현재 구간 = None,
///    이벤트도 내지 않는다(되살아나면 그 자리부터 다시 판정).
///  · 히스테리시스: 지금 구간은 <see cref="ExitMargin"/> 만큼 넓힌 영역을 벗어나야 떠난다. 새 구간은
///    <see cref="EnterMargin"/> 만큼 좁힌 영역에 들어와야 들어간다(원격 위치는 복제 지연·보간으로 흔들린다).
///  · 파티 첫 진입 = 존1~4·보스 진입 각각 판 전체에서 1번(<see cref="PartyFirstEntered"/>). 복도는 내지 않는다.
///  · 복도 중간 지점 = 플레이어마다 복도마다 1번(<see cref="CorridorMidReached"/>) — 왕복해도 다시 안 낸다.
///    좁힌 중간 지점 영역(EnterMargin)에 들어오면 발행.
/// </summary>
public sealed class TutorialAreaTracker
{
    public const int CorridorCount = 4;
    public const float DefaultEnterMargin = 0.5f;
    public const float DefaultExitMargin = 1.0f;

    private struct AreaShape { public TutorialArea Area; public TutorialRect Rect; }

    private struct PlayerArea
    {
        public TutorialArea Current;
        public int MidMask;   // bit = 복도 번호 − 1
    }

    private readonly List<AreaShape> _areas = new List<AreaShape>();
    private readonly TutorialRect?[] _mids = new TutorialRect?[CorridorCount];
    private readonly Dictionary<ulong, PlayerArea> _players = new Dictionary<ulong, PlayerArea>();
    private int _partyEnteredMask;   // bit = (int)TutorialArea

    public TutorialAreaTracker(float enterMargin = DefaultEnterMargin, float exitMargin = DefaultExitMargin)
    {
        EnterMargin = Math.Max(0f, enterMargin);
        ExitMargin = Math.Max(0f, exitMargin);
    }

    public float EnterMargin { get; }
    public float ExitMargin { get; }

    /// <summary>파티 첫 진입(구간, 처음 들어온 플레이어). 존1~4·보스 진입만.</summary>
    public event Action<TutorialArea, ulong> PartyFirstEntered;

    /// <summary>개인 첫 복도 중간 지점(플레이어, 복도 번호 1~4).</summary>
    public event Action<ulong, int> CorridorMidReached;

    /// <summary>플레이어의 현재 구간이 바뀜(플레이어, 이전, 새). 판정 제외(사망 등)로 None 이 되는 것도 포함.</summary>
    public event Action<ulong, TutorialArea, TutorialArea> PlayerAreaChanged;

    // ─── 정의 ────────────────────────────────────────────────────────────

    public void SetArea(TutorialArea area, TutorialRect rect)
    {
        if (area == TutorialArea.None) return;
        for (int i = 0; i < _areas.Count; i++)
            if (_areas[i].Area == area) { _areas[i] = new AreaShape { Area = area, Rect = rect }; return; }
        _areas.Add(new AreaShape { Area = area, Rect = rect });
    }

    /// <summary>복도 n(1~4)의 중간 지점 영역.</summary>
    public void SetCorridorMid(int corridor, TutorialRect rect)
    {
        if (corridor < 1 || corridor > CorridorCount) return;
        _mids[corridor - 1] = rect;
    }

    public bool TryGetArea(TutorialArea area, out TutorialRect rect)
    {
        foreach (AreaShape s in _areas)
            if (s.Area == area) { rect = s.Rect; return true; }
        rect = default;
        return false;
    }

    public bool TryGetCorridorMid(int corridor, out TutorialRect rect)
    {
        rect = default;
        if (corridor < 1 || corridor > CorridorCount || !_mids[corridor - 1].HasValue) return false;
        rect = _mids[corridor - 1].Value;
        return true;
    }

    public static bool IsZone(TutorialArea a) =>
        a == TutorialArea.Zone1 || a == TutorialArea.Zone2 || a == TutorialArea.Zone3 || a == TutorialArea.Zone4 || a == TutorialArea.BossEnter;

    /// <summary>복도면 번호(1~4), 아니면 0.</summary>
    public static int CorridorIndexOf(TutorialArea a) => a switch
    {
        TutorialArea.Corridor1 => 1,
        TutorialArea.Corridor2 => 2,
        TutorialArea.Corridor3 => 3,
        TutorialArea.Corridor4 => 4,
        _ => 0,
    };

    /// <summary>판정 대상 = 살아 있는 플레이어만(영혼·사망 연출·완전 사망 제외).</summary>
    public static bool IsEligible(PlayerLifeState state) => state == PlayerLifeState.Alive;

    // ─── 조회 ────────────────────────────────────────────────────────────

    public TutorialArea GetCurrentArea(ulong clientId) =>
        _players.TryGetValue(clientId, out PlayerArea p) ? p.Current : TutorialArea.None;

    public bool HasPartyEntered(TutorialArea area) => (_partyEnteredMask & (1 << (int)area)) != 0;

    public bool HasReachedCorridorMid(ulong clientId, int corridor) =>
        corridor >= 1 && corridor <= CorridorCount
        && _players.TryGetValue(clientId, out PlayerArea p) && (p.MidMask & (1 << (corridor - 1))) != 0;

    // ─── 판정 ────────────────────────────────────────────────────────────

    /// <summary>플레이어 한 명의 위치 샘플. 처음 보는 id 는 등록한다.</summary>
    public void UpdatePlayer(ulong clientId, float x, float z, PlayerLifeState state)
    {
        _players.TryGetValue(clientId, out PlayerArea p);   // 없으면 기본값(None, 0)

        TutorialArea old = p.Current;
        if (!IsEligible(state))
        {
            p.Current = TutorialArea.None;
            _players[clientId] = p;
            if (old != TutorialArea.None) PlayerAreaChanged?.Invoke(clientId, old, TutorialArea.None);
            return;
        }

        p.Current = Classify(old, x, z);

        int newlyMid = 0;
        for (int i = 0; i < CorridorCount; i++)
        {
            int bit = 1 << i;
            if ((p.MidMask & bit) != 0 || !_mids[i].HasValue) continue;
            if (!_mids[i].Value.Contains(x, z, -EnterMargin)) continue;
            p.MidMask |= bit;
            newlyMid |= bit;
        }
        _players[clientId] = p;   // 이벤트 전에 반영 — 구독자가 조회해도 새 값을 본다

        if (p.Current != old)
        {
            PlayerAreaChanged?.Invoke(clientId, old, p.Current);
            if (IsZone(p.Current) && !HasPartyEntered(p.Current))
            {
                _partyEnteredMask |= 1 << (int)p.Current;
                PartyFirstEntered?.Invoke(p.Current, clientId);
            }
        }

        for (int i = 0; i < CorridorCount; i++)
            if ((newlyMid & (1 << i)) != 0) CorridorMidReached?.Invoke(clientId, i + 1);
    }

    /// <summary>이탈 — 현재 구간만 지운다. 개인 1회 기록은 남긴다(재접속 없음).</summary>
    public void RemovePlayer(ulong clientId)
    {
        if (!_players.TryGetValue(clientId, out PlayerArea p) || p.Current == TutorialArea.None) return;
        TutorialArea old = p.Current;
        p.Current = TutorialArea.None;
        _players[clientId] = p;
        PlayerAreaChanged?.Invoke(clientId, old, TutorialArea.None);
    }

    private TutorialArea Classify(TutorialArea current, float x, float z)
    {
        // 지금 구간은 바깥 여유까지 유지
        if (current != TutorialArea.None && TryGetArea(current, out TutorialRect cur) && cur.Contains(x, z, ExitMargin))
            return current;

        // 새 구간은 안쪽 여유 안으로 들어와야
        foreach (AreaShape s in _areas)
            if (s.Rect.Contains(x, z, -EnterMargin)) return s.Area;

        // 어느 안쪽 여유에도 없다(구간 밖, 또는 None 상태에서 경계 띠 위) — 안쪽으로 더 들어올 때까지 None.
        return TutorialArea.None;
    }
}
