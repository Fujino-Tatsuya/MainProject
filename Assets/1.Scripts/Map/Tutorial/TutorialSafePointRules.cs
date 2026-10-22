using System;
using System.Collections.Generic;

// 튜토리얼 스텝(구간)별 고정 안전 지점(할 일 80 — 1안) 순수 규칙(Unity·NGO 비의존 — EditMode 테스트 대상).
// 배선은 TutorialSafePoints(4.MapScene, 서버). 문서: Docs/tech/tutorial-director.md §12.

public static class TutorialSafePointRules
{
    /// <summary>기본 지점 — 들어오는 배리어 앞면에서 구간 안쪽으로(m). 존1 은 가운데.</summary>
    public const float DefaultZoneEntryInset = 2.5f;
    /// <summary>복도는 짧다(약 9.6m) — 중간 지점(뒤쪽 절반)에 닿지 않게 입구 가까이.</summary>
    public const float DefaultCorridorEntryInset = 1.5f;

    /// <summary>복귀 높이 기본값 — 지점 Transform 위로 이만큼(m) 띄워 내려놓고 떨어져 착지한다.</summary>
    public const float DefaultDropHeight = 3f;

    /// <summary>
    /// 지금 스텝 구간 = 자동 부활과 같은 '그 플레이어 기준 지금 단계 구간'
    /// (<see cref="TutorialRevivePointRules.ResolveStageArea(TutorialStep, int, int)"/>). 보스 진입은 G10 을 지난 사람만.
    /// </summary>
    public static TutorialArea StepArea(TutorialStep step, int openedGateMask, int passedMask) =>
        TutorialRevivePointRules.ResolveStageArea(step, openedGateMask, passedMask);

    /// <summary>
    /// 복귀 지점 고르기 — 지금 스텝 구간(<paramref name="stepArea"/>)에 지점이 있으면 항상 그것만. 점유·바닥 검사·다른 구간 대체 없음.
    /// 없으면 false(기존 추락 복구 경로).
    /// </summary>
    public static bool TryPickStepPoint(TutorialArea stepArea, Func<TutorialArea, bool> hasPoint, out TutorialArea area)
    {
        area = stepArea != TutorialArea.None && hasPoint != null && hasPoint(stepArea) ? stepArea : TutorialArea.None;
        return area != TutorialArea.None;
    }

    /// <summary>복귀 높이 — 지점 Y + <paramref name="dropHeight"/>(음수는 0).</summary>
    public static float DropY(float pointY, float dropHeight) => pointY + Math.Max(0f, dropHeight);

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
