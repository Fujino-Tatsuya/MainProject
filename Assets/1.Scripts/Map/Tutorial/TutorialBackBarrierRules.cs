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
    public static TutorialArea AreaBeyond(TutorialBarrierSpec spec)
    {
        int c = spec.Corridor;
        if (c < 1 || c > TutorialAreaTracker.CorridorCount) return TutorialArea.None;
        // 동선 순서: 존n = 2c−1, 복도n = 2c, 존n+1 = 2c+1
        return (TutorialArea)(spec.AtEntry ? 2 * c : 2 * c + 1);
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
