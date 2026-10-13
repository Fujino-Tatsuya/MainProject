using System.Collections.Generic;

// 개인별 뒤 배리어의 순수 규칙(Unity·NGO 비의존 — EditMode 테스트 대상).
// 원본: Docs/tech/tutorial-director.md §7 '개인 뒤 배리어'.

/// <summary>
/// 개인별 뒤 배리어 — 열린 배리어를 지나 앞 구간으로 넘어간 플레이어 <b>본인만</b> 그 배리어 뒤로 못 돌아가게 한다.
///  · 지남(서버): 배리어 박스의 진행 방향 쪽 면에서 <c>clearance</c>(플레이어 반경 이상) 넘게 나아갔고, 그 배리어 너머 구간
///    (입구 = 복도 n, 출구 = 다음 존) 안에 있을 때. 게이트가 열린 배리어만 본다(닫힌 배리어는 일반 배리어가 막는다).
///  · 마스크 bit = <c>1 &lt;&lt; (int)TutorialGate</c>(게이트 열림 마스크와 같은 배치). 한 번 켜진 비트는 끄지 않는다.
///  · 로컬 콜라이더(각 피어, 로컬 플레이어 기준) = 지난 배리어 ∧ 열린 게이트, 튜토리얼 진행 중(활성 · Finished 전)일 때만.
/// </summary>
public static class TutorialBackBarrierRules
{
    /// <summary>지남 판정 여유(m) — 플레이어 캡슐 반경(≈0.5) 이상. 켤 때 콜라이더가 플레이어와 겹치지 않을 거리.</summary>
    public const float DefaultClearance = 0.75f;

    /// <summary>배리어 8개 게이트 비트 전체.</summary>
    public static int AllBarrierMask
    {
        get
        {
            int m = 0;
            for (int c = 1; c <= TutorialAreaTracker.CorridorCount; c++)
                m |= Bit(TutorialBarrierRules.GateOf(c, true)) | Bit(TutorialBarrierRules.GateOf(c, false));
            return m;
        }
    }

    public static int Bit(TutorialGate gate) => 1 << (int)gate;

    public static bool Has(int mask, TutorialGate gate) => (mask & Bit(gate)) != 0;

    /// <summary>배리어 너머 구간 — 입구 = 복도 n, 출구 = 존 n+1(복도4 출구 = 보스 진입).</summary>
    public static TutorialArea AreaBeyond(TutorialBarrierSpec spec) => AreaBeyond(spec.Corridor, spec.AtEntry);

    static TutorialArea AreaBeyond(int c, bool atEntry)
    {
        if (c < 1 || c > TutorialAreaTracker.CorridorCount) return TutorialArea.None;
        // 동선 순서: 존n = 2c−1, 복도n = 2c, 존n+1 = 2c+1
        return (TutorialArea)(atEntry ? 2 * c : 2 * c + 1);
    }

    // ─── 스폰·부활·추락 복구 예외(C2) — 그 플레이어를 뒤 배리어 너머로 옮기지 않는다 ─────────

    /// <summary>
    /// 이 플레이어가 지나온 최전방 구간 = 지난 배리어 중 가장 앞 것의 너머 구간. 지난 배리어가 없으면 존1.
    /// 이보다 뒤 구간은 그 플레이어에게 '뒤 배리어 너머'다.
    /// </summary>
    public static TutorialArea FrontmostPassedArea(int passedMask)
    {
        TutorialArea front = TutorialArea.Zone1;
        for (int c = 1; c <= TutorialAreaTracker.CorridorCount; c++)
        {
            if (Has(passedMask, TutorialBarrierRules.GateOf(c, true)) && AreaBeyond(c, true) > front) front = AreaBeyond(c, true);
            if (Has(passedMask, TutorialBarrierRules.GateOf(c, false)) && AreaBeyond(c, false) > front) front = AreaBeyond(c, false);
        }
        return front;
    }

    /// <summary>
    /// [서버, 전멸 리스폰] 지나온 최전방 구간이 정확히 <paramref name="front"/> 인 마스크 = 너머 구간이 <paramref name="front"/> 이하인 배리어 비트 전부.
    /// 복도4 면 G1~G9(G10 은 뺀다) — 복도4 로 리스폰한 사람이 G10 뒤 배리어에 갇히지 않고, G9 뒤로는 못 돌아간다.
    /// 마스크를 '더하기만' 하는 일반 갱신(<see cref="UpdatePassedMask"/>)의 유일한 예외다. 존1 이하면 0.
    /// </summary>
    public static int MaskWithFrontmost(TutorialArea front)
    {
        int m = 0;
        for (int c = 1; c <= TutorialAreaTracker.CorridorCount; c++)
        {
            if (AreaBeyond(c, true) <= front) m |= Bit(TutorialBarrierRules.GateOf(c, true));
            if (AreaBeyond(c, false) <= front) m |= Bit(TutorialBarrierRules.GateOf(c, false));
        }
        return m;
    }

    /// <summary>이 구간이 그 플레이어의 뒤 배리어 너머(지나온 최전방 구간보다 뒤)인가. 튜토리얼 밖(None)은 false.</summary>
    public static bool IsAreaBehind(TutorialArea area, int passedMask) =>
        area != TutorialArea.None && area < FrontmostPassedArea(passedMask);

    /// <summary>
    /// 이 점(<paramref name="area"/> 안)이 지난 배리어 앞면에 너무 가까운가 — 그 배리어의 너머 구간이면서 앞면에서 <paramref name="clearance"/> 미만.
    /// 거기로 옮기면 그 플레이어의 로컬 뒤 배리어 콜라이더와 겹친다(끼임).
    /// </summary>
    public static bool IsNearPassedFace(IReadOnlyList<TutorialBarrierSpec> specs, TutorialArea area, float x, float z,
                                        int passedMask, float clearance)
    {
        if (specs == null || passedMask == 0) return false;
        for (int i = 0; i < specs.Count; i++)
        {
            TutorialBarrierSpec s = specs[i];
            if (Has(passedMask, s.Gate) && AreaBeyond(s) == area && DistancePastFace(s, x, z) < clearance) return true;
        }
        return false;
    }

    /// <summary>
    /// [서버, 추락 복구] 그 플레이어 기준 복귀 후보 허용 — 점을 품은 구간 중 하나라도 (들어갈 수 있고 · 뒤 배리어 너머가 아니고 ·
    /// 지난 배리어 앞면에서 <paramref name="clearance"/> 이상)이면 허용. 품은 구간이 없으면(보스방 등) 허용.
    /// <paramref name="passedMask"/> = 0 이면 <see cref="TutorialBarrierRules.IsPointReachable"/> 과 같다.
    /// 보스 진입 패드(C3): <paramref name="pads"/> 범위 + <paramref name="padMargin"/> 안이면 누구든 거부한다(복귀만으로 카운트다운 시작 방지).
    /// 패드 정보가 없으면(null·빈 목록) 기존 동작.
    /// </summary>
    public static bool IsReturnPointAllowed(TutorialAreaTracker areas, IReadOnlyList<TutorialBarrierSpec> specs, float x, float z,
                                            int openedGateMask, int passedMask, float clearance,
                                            IReadOnlyList<TutorialPadArea> pads = null,
                                            float padMargin = TutorialRevivePointRules.DefaultPadMargin)
    {
        if (TutorialRevivePointRules.IsOnPad(pads, x, z, padMargin)) return false;
        if (areas == null) return true;
        TutorialArea front = FrontmostPassedArea(passedMask);
        bool inAny = false;
        for (var a = TutorialArea.Zone1; a <= TutorialArea.BossEnter; a++)
        {
            if (!areas.TryGetArea(a, out TutorialRect r) || !r.Contains(x, z)) continue;
            inAny = true;
            if (a < front || !TutorialBarrierRules.IsAreaReachable(a, openedGateMask)) continue;
            if (IsNearPassedFace(specs, a, x, z, passedMask, clearance)) continue;
            return true;
        }
        return !inAny;
    }

    /// <summary>배리어 박스의 진행 방향 쪽 면에서 진행 방향으로 잰 거리(m). 면 뒤(돌아오는 쪽)면 음수.</summary>
    public static float DistancePastFace(TutorialBarrierSpec spec, float x, float z)
    {
        spec.Rect.GetWorldBounds(out float minX, out float minZ, out float maxX, out float maxZ);
        float face = spec.AlongX ? (spec.Forward > 0f ? maxX : minX) : (spec.Forward > 0f ? maxZ : minZ);
        float p = spec.AlongX ? x : z;
        return (p - face) * spec.Forward;
    }

    /// <summary>이 위치가 배리어를 '완전히' 지났는가 — 앞면에서 <paramref name="clearance"/> 이상 나아갔고 너머 구간 안.</summary>
    public static bool HasPassed(TutorialAreaTracker areas, TutorialBarrierSpec spec, float x, float z, float clearance)
    {
        if (areas == null) return false;
        if (DistancePastFace(spec, x, z) < clearance) return false;
        return areas.TryGetArea(AreaBeyond(spec), out TutorialRect beyond) && beyond.Contains(x, z);
    }

    /// <summary>
    /// [서버] 위치 샘플 하나로 지난 배리어 마스크를 갱신한다. 열린 게이트의 배리어만 새로 켠다. 기존 비트는 유지.
    /// <paramref name="newlyPassed"/> = 이번에 새로 켜진 비트(로그용).
    /// </summary>
    public static int UpdatePassedMask(int passedMask, TutorialAreaTracker areas, IReadOnlyList<TutorialBarrierSpec> specs,
                                       float x, float z, int openedGateMask, float clearance, out int newlyPassed)
    {
        newlyPassed = 0;
        if (specs == null) return passedMask;
        for (int i = 0; i < specs.Count; i++)
        {
            TutorialBarrierSpec s = specs[i];
            int bit = Bit(s.Gate);
            if ((passedMask & bit) != 0 || (openedGateMask & bit) == 0) continue;
            if (!HasPassed(areas, s, x, z, clearance)) continue;
            passedMask |= bit;
            newlyPassed |= bit;
        }
        return passedMask;
    }

    /// <summary>
    /// [각 피어] 로컬 플레이어에게 켜 둘 뒤 배리어 = 지난 배리어 ∧ 열린 게이트(닫힌 배리어와 중복 생성 안 함).
    /// 튜토리얼 진행 중이 아니면(비활성·Finished) 0.
    /// </summary>
    public static int LocalColliderMask(int passedMask, int openedGateMask, bool tutorialRunning) =>
        tutorialRunning ? passedMask & openedGateMask & AllBarrierMask : 0;

    /// <summary>새로 켤 것 = 원하는데 아직 꺼진 것. 실제 켜기는 로컬 플레이어와 겹치지 않을 때까지 미룬다(호출자).</summary>
    public static int ToEnable(int wantedMask, int activeMask) => wantedMask & ~activeMask;

    /// <summary>끌 것 = 켜져 있는데 더는 원하지 않는 것(튜토리얼 끝 등).</summary>
    public static int ToDisable(int wantedMask, int activeMask) => activeMask & ~wantedMask;

    /// <summary>로그용 — "G1,G2".</summary>
    public static string Describe(int mask)
    {
        if (mask == 0) return "없음";
        var parts = new List<string>();
        for (int g = 0; g < TutorialFlowModel.GateCount; g++)
            if ((mask & (1 << g)) != 0) parts.Add("G" + (g + 1));
        return string.Join(",", parts);
    }
}
