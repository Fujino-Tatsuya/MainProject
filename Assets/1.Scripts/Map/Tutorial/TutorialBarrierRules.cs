using System;
using System.Collections.Generic;

// 튜토리얼 배리어의 순수 규칙(Unity·NGO 비의존 — EditMode 테스트 대상).
// 원본: Docs/design/tutorial-scenario.md §2.2·§4, PLAN-tutorial.md S3, Docs/tech/tutorial-director.md §7.
// 모양은 S2 구간(TutorialAreaTracker — TutorialAreaLayout 이 StageTutorial 저작 데이터로 채운 것)의 복도 사각형 양 끝에서 만든다.

/// <summary>배리어 하나 — 월드 XZ 축 정렬 박스(복도 사각형이 축 정렬이라 회전 없음).</summary>
public readonly struct TutorialBarrierSpec
{
    public readonly TutorialGate Gate;
    /// <summary>복도 번호 1~4.</summary>
    public readonly int Corridor;
    /// <summary>true = 복도 입구(앞 존 쪽), false = 복도 출구(다음 존 쪽).</summary>
    public readonly bool AtEntry;
    public readonly TutorialRect Rect;
    /// <summary>진행 방향 축 — true = 월드 X, false = 월드 Z.</summary>
    public readonly bool AlongX;
    /// <summary>진행 방향 부호(+1 = 축의 + 쪽으로 나아간다, −1 = − 쪽). 개인 뒤 배리어의 '지남' 판정에 쓴다.</summary>
    public readonly float Forward;

    public TutorialBarrierSpec(TutorialGate gate, int corridor, bool atEntry, TutorialRect rect, bool alongX = true, float forward = 1f)
    {
        Gate = gate; Corridor = corridor; AtEntry = atEntry; Rect = rect;
        AlongX = alongX; Forward = forward < 0f ? -1f : 1f;
    }

    public override string ToString() => $"{Gate} 복도{Corridor} {(AtEntry ? "입구" : "출구")} {Rect}";
}

/// <summary>
/// 게이트 → 배리어 매핑 · 배리어 모양 · 열림 판정 · 추락 복구 후보 허용 판정.
///
/// 배리어 8개 = 복도 n 의 입구(존n 쪽)·출구(존n+1 쪽):
///   복도1 G1/G2 · 복도2 G3/G4 · 복도3 G6/G7 · 복도4 G9/G10. G5(스피너봇 본 모드)·G8(타이머 시작)·G11(보스방 도착)은 배리어가 없다.
/// 배리어 박스는 복도 사각형 끝 경계에서 <b>복도 안쪽</b>으로 두께만큼(존 사각형을 침범하지 않게 — 존 진입 판정과 겹치지 않는다),
/// 가로는 복도 너비 + 양옆 겹침(복도 벽에 파고들게 — 틈 방지).
/// </summary>
public static class TutorialBarrierRules
{
    public const int BarrierCount = 8;
    public const float DefaultThickness = 0.4f;
    public const float DefaultSideOverlap = 0.3f;

    static readonly TutorialGate[] EntryGates =
        { TutorialGate.G1_Corridor1Entry, TutorialGate.G3_Corridor2Entry, TutorialGate.G6_Corridor3Entry, TutorialGate.G9_Corridor4Entry };

    static readonly TutorialGate[] ExitGates =
        { TutorialGate.G2_Zone2Entry, TutorialGate.G4_Zone3Entry, TutorialGate.G7_Zone4Entry, TutorialGate.G10_BossEnterAndPad };

    static readonly TutorialArea[] ZoneOfSlot =
        { TutorialArea.Zone1, TutorialArea.Zone2, TutorialArea.Zone3, TutorialArea.Zone4, TutorialArea.BossEnter };

    static readonly TutorialArea[] CorridorAreas =
        { TutorialArea.Corridor1, TutorialArea.Corridor2, TutorialArea.Corridor3, TutorialArea.Corridor4 };

    /// <summary>복도 n(1~4)의 입구/출구 배리어를 여는 게이트.</summary>
    public static TutorialGate GateOf(int corridor, bool atEntry)
    {
        if (corridor < 1 || corridor > TutorialAreaTracker.CorridorCount)
            throw new ArgumentOutOfRangeException(nameof(corridor), corridor, "복도 번호는 1~4");
        return atEntry ? EntryGates[corridor - 1] : ExitGates[corridor - 1];
    }

    /// <summary>이 게이트에 배리어가 있는가(G5·G8·G11 은 없다).</summary>
    public static bool HasBarrier(TutorialGate gate) =>
        Array.IndexOf(EntryGates, gate) >= 0 || Array.IndexOf(ExitGates, gate) >= 0;

    public static bool IsGateOpen(TutorialGate gate, int openedGateMask) => (openedGateMask & (1 << (int)gate)) != 0;

    /// <summary>
    /// 이 구간에 정상 동선으로 들어갈 수 있는가 — 그 구간으로 들어가는 배리어가 열렸는가.
    /// 존1 은 늘 · 복도1 = G1 · 존2 = G2 · 복도2 = G3 · 존3 = G4 · 복도3 = G6 · 존4 = G7 · 복도4 = G9 · 보스 진입 = G10.
    /// </summary>
    public static bool IsAreaReachable(TutorialArea area, int openedGateMask)
    {
        int c = TutorialAreaTracker.CorridorIndexOf(area);
        if (c > 0) return IsGateOpen(GateOf(c, atEntry: true), openedGateMask);
        switch (area)
        {
            case TutorialArea.Zone1: return true;
            case TutorialArea.Zone2: return IsGateOpen(GateOf(1, false), openedGateMask);
            case TutorialArea.Zone3: return IsGateOpen(GateOf(2, false), openedGateMask);
            case TutorialArea.Zone4: return IsGateOpen(GateOf(3, false), openedGateMask);
            case TutorialArea.BossEnter: return IsGateOpen(GateOf(4, false), openedGateMask);
            default: return true;   // None — 튜토리얼 구간 밖(보스방 등)은 막지 않는다
        }
    }

    /// <summary>
    /// 추락 복구 후보 허용 판정 — 점을 품은 튜토리얼 구간 중 하나라도 들어갈 수 있으면 허용, 품은 구간이 모두 닫힌 구간이면 거부.
    /// 어느 구간에도 없으면(보스방 등 튜토리얼 밖) 허용. 경계선 위 점은 양쪽 구간에 다 들어간다 → 한쪽만 열려도 허용
    /// (배리어 박스는 복도 안쪽에 있고 그 자리는 콜라이더 점유 검사가 따로 거른다).
    /// </summary>
    public static bool IsPointReachable(TutorialAreaTracker areas, float x, float z, int openedGateMask)
    {
        if (areas == null) return true;
        bool inAny = false;
        for (var a = TutorialArea.Zone1; a <= TutorialArea.BossEnter; a++)
        {
            if (!areas.TryGetArea(a, out TutorialRect r) || !r.Contains(x, z)) continue;
            if (IsAreaReachable(a, openedGateMask)) return true;
            inAny = true;
        }
        return !inAny;
    }

    /// <summary>
    /// 구간(S2)에서 배리어 8개 모양을 만든다. 복도 사각형이나 양옆 존이 없으면 그 복도는 건너뛰고 <paramref name="error"/> 에 남긴다.
    /// </summary>
    public static List<TutorialBarrierSpec> BuildSpecs(TutorialAreaTracker areas, float thickness, float sideOverlap, out string error)
    {
        error = null;
        var list = new List<TutorialBarrierSpec>(BarrierCount);
        if (areas == null) { error = "구간 없음"; return list; }

        thickness = Math.Max(0.05f, thickness);
        sideOverlap = Math.Max(0f, sideOverlap);

        for (int c = 1; c <= TutorialAreaTracker.CorridorCount; c++)
        {
            if (!areas.TryGetArea(CorridorAreas[c - 1], out TutorialRect corridor)
                || !areas.TryGetArea(ZoneOfSlot[c - 1], out TutorialRect from)
                || !areas.TryGetArea(ZoneOfSlot[c], out TutorialRect to))
            {
                error = $"복도{c}: 복도 또는 양옆 존 구간이 없다 — 배리어 생략";
                continue;
            }

            corridor.GetWorldBounds(out float minX, out float minZ, out float maxX, out float maxZ);
            float dx = to.CenterX - from.CenterX, dz = to.CenterZ - from.CenterZ;
            bool alongX = Math.Abs(dx) >= Math.Abs(dz);
            bool towardMax = alongX ? dx > 0f : dz > 0f;

            float lenMin = alongX ? minX : minZ, lenMax = alongX ? maxX : maxZ;
            float crossMin = (alongX ? minZ : minX) - sideOverlap, crossMax = (alongX ? maxZ : maxX) + sideOverlap;
            float t = Math.Min(thickness, (lenMax - lenMin) * 0.5f);

            // 입구 = 앞 존 쪽 끝, 출구 = 다음 존 쪽 끝. 둘 다 끝 경계에서 복도 안쪽으로 두께 t.
            float entryEdge = towardMax ? lenMin : lenMax;
            float exitEdge = towardMax ? lenMax : lenMin;
            float inward = towardMax ? 1f : -1f;

            list.Add(Make(GateOf(c, true), c, true, alongX, inward, entryEdge, entryEdge + inward * t, crossMin, crossMax));
            list.Add(Make(GateOf(c, false), c, false, alongX, inward, exitEdge, exitEdge - inward * t, crossMin, crossMax));
        }
        return list;
    }

    static TutorialBarrierSpec Make(TutorialGate gate, int corridor, bool atEntry, bool alongX, float forward,
                                    float a, float b, float crossMin, float crossMax)
    {
        float lo = Math.Min(a, b), hi = Math.Max(a, b);
        TutorialRect r = alongX
            ? TutorialRect.FromMinMax(lo, crossMin, hi, crossMax)
            : TutorialRect.FromMinMax(crossMin, lo, crossMax, hi);
        return new TutorialBarrierSpec(gate, corridor, atEntry, r, alongX, forward);
    }
}
