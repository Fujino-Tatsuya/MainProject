using System;
using System.Collections.Generic;

// 튜토리얼 스텝(구간)별 고정 안전 지점(할 일 80 — 1안) 순수 규칙(Unity·NGO 비의존 — EditMode 테스트 대상).
// 배선은 TutorialSafePoints(4.MapScene, 서버). 문서: Docs/tech/tutorial-director.md §12.

/// <summary>안전 지점으로 되돌리는 이유.</summary>
public enum TutorialRecoveryReason : byte
{
    /// <summary>낙사(추락 생존·추락 사망) — 기존 추락 복구가 복귀 지점을 물을 때.</summary>
    Fall,
    /// <summary>튜토리얼 구간(존1~보스 진입) 사각형 밖에 서 있다 — 벽 틈 등으로 맵 밖에 나감.</summary>
    OutOfArea,
    /// <summary>벽·닫힌 배리어 속에 몸 중심이 박혀 있다.</summary>
    Stuck,
}

public static class TutorialSafePointRules
{
    public const float DefaultOutOfAreaSeconds = 1.5f;
    public const float DefaultOutOfAreaMargin = 1f;
    public const float DefaultStuckSeconds = 2f;
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
    /// 점이 튜토리얼 구간(존1~보스 진입) 어디에도 없는가 — 각 사각형을 바깥으로 <paramref name="margin"/> 넓혀 본다.
    /// 구간이 하나도 없으면(구간 계산 실패) false — 판정하지 않는다.
    /// </summary>
    public static bool IsOutsideAll(TutorialAreaTracker areas, float x, float z, float margin)
    {
        if (areas == null) return false;
        bool any = false;
        for (var a = TutorialArea.Zone1; a <= TutorialArea.BossEnter; a++)
        {
            if (!areas.TryGetArea(a, out TutorialRect r)) continue;
            any = true;
            if (r.Contains(x, z, margin)) return false;
        }
        return any;
    }

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

/// <summary>
/// 조건이 연속으로 <c>threshold</c> 초 이상 이어졌는지(구간 이탈·끼임 판정). 조건이 끊기면 0 부터.
/// 한 번 알리면 다시 처음부터 잰다 — 복귀가 실패해 그대로면 다음 threshold 뒤 다시 알린다.
/// </summary>
public sealed class TutorialRecoveryTimer
{
    private double _since = -1d;

    public bool IsRunning => _since >= 0d;

    public bool Tick(bool condition, double now, float thresholdSeconds)
    {
        if (!condition)
        {
            _since = -1d;
            return false;
        }

        if (_since < 0d) _since = now;
        if (now - _since < Math.Max(0f, thresholdSeconds)) return false;
        _since = now;
        return true;
    }

    public void Reset() => _since = -1d;
}
