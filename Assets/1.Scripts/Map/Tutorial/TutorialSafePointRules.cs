using System.Collections.Generic;

// 튜토리얼 스텝(구간)별 고정 안전 지점(할 일 80 — 1안) 순수 규칙(Unity·NGO 비의존 — EditMode 테스트 대상).
// 배선은 TutorialSafePoints(4.MapScene, 서버). 문서: Docs/tech/tutorial-director.md §12.

public static class TutorialSafePointRules
{
    /// <summary>기본 지점 — 들어오는 배리어 앞면에서 구간 안쪽으로(m). 존1 은 가운데.</summary>
    public const float DefaultZoneEntryInset = 2.5f;
    /// <summary>복도는 짧다(약 9.6m) — 중간 지점(뒤쪽 절반)에 닿지 않게 입구 가까이.</summary>
    public const float DefaultCorridorEntryInset = 1.5f;

    /// <summary>
    /// 찾는 구간 순서 = 자동 부활과 같은 '그 플레이어 기준 지금 단계 구간'부터 지나온 최전방 구간까지 거꾸로
    /// (<see cref="TutorialRevivePointRules.AreaOrder(TutorialStep, int, int)"/>). 보스 진입은 G10 을 지난 사람만.
    /// </summary>
    public static List<TutorialArea> AreaOrder(TutorialStep step, int openedGateMask, int passedMask) =>
        TutorialRevivePointRules.AreaOrder(step, openedGateMask, passedMask);

    /// <summary>감시하는가 — 튜토리얼 진행 중(단계 None·Finished 아님)이고 보스방 이동(카운트다운·도착 대기) 중이 아님.</summary>
    public static bool ShouldWatch(bool tutorialActive, TutorialStep step, bool bossMoveBusy) =>
        tutorialActive && step != TutorialStep.None && step != TutorialStep.Finished && !bossMoveBusy;

    /// <summary>
    /// 저작 메뉴가 처음 놓는 기본 위치(월드 XZ) — 존1 = 가운데, 그 밖 = 그 구간으로 들어오는 배리어(너머 구간 = area)의
    /// 진행 방향 앞면에서 안쪽으로 <paramref name="inset"/>, 가로는 배리어 가운데. 배리어·구간이 없으면 false.
    /// </summary>
    public static bool TryDefaultAnchor(TutorialArea area, TutorialAreaTracker areas, IReadOnlyList<TutorialBarrierSpec> specs,
                                        float inset, out float x, out float z)
    {
        x = z = 0f;
        if (areas == null || !areas.TryGetArea(area, out TutorialRect rect)) return false;
        if (area == TutorialArea.Zone1)
        {
            x = rect.CenterX;
            z = rect.CenterZ;
            return true;
        }

        if (specs == null) return false;
        for (int i = 0; i < specs.Count; i++)
        {
            TutorialBarrierSpec s = specs[i];
            if (TutorialBackBarrierRules.AreaBeyond(s) != area) continue;
            s.Rect.GetWorldBounds(out float minX, out float minZ, out float maxX, out float maxZ);
            if (s.AlongX)
            {
                x = (s.Forward > 0f ? maxX : minX) + s.Forward * inset;
                z = (minZ + maxZ) * 0.5f;
            }
            else
            {
                x = (minX + maxX) * 0.5f;
                z = (s.Forward > 0f ? maxZ : minZ) + s.Forward * inset;
            }
            return true;
        }
        return false;
    }
}
