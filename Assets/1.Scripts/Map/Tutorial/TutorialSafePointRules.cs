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

    /// <summary>지점이 막혔을 때 둘러보는 링 반경(m, 가까운 것부터)·방향 수.</summary>
    public static readonly IReadOnlyList<float> DefaultRingRadii = new[] { 0.75f, 1.5f, 2.25f, 3f };
    public const int DefaultRingDirections = 8;

    static readonly string[] BlockingLayerNames = { "Default", "Ground", "Env", "Wall", "Barrier" };

    /// <summary>
    /// 지점을 막는(점유 검사) 레이어 = 정적 장애물만 — 바닥 레이어 위 소품(Default·Ground·Env)·벽(Wall)·배리어(Barrier).
    /// 몬스터(Enemy·EnemyHurtBox·이름 없는 래그돌 뼈 레이어)·플레이어·시체·Soul·무기처럼 움직이는 것은 넣지 않는다 —
    /// 겹쳐 내려도 유닛 충돌 밀어내기(할 일 11)가 푼다. 자동 부활 격자(<c>TutorialDeathRules</c>)와 같은 조합.
    /// </summary>
    public static IReadOnlyList<string> BlockingLayers => BlockingLayerNames;

    public static bool IsBlockingLayer(string layerName) => Array.IndexOf(BlockingLayerNames, layerName) >= 0;

    /// <summary>
    /// 고정 지점(<paramref name="x"/>, <paramref name="z"/>)이 정적 장애물에 막혔을 때 그 주변 빈자리 — 반경 <paramref name="radii"/>(앞에서부터 = 가까운 것부터)마다
    /// <paramref name="directions"/> 방향(+X 부터 반시계)을 보고, 구간 사각형 <paramref name="bounds"/> 안이면서 <paramref name="isFree"/> 인 첫 점.
    /// 구간 밖(벽 너머 다른 구간·맵 밖)은 보지 않는다. 없으면 false.
    /// </summary>
    public static bool TryFindNearestFree(float x, float z, TutorialRect bounds, IReadOnlyList<float> radii, int directions,
                                          Func<float, float, bool> isFree, out float freeX, out float freeZ)
    {
        freeX = x;
        freeZ = z;
        if (radii == null || isFree == null || directions <= 0) return false;

        for (int r = 0; r < radii.Count; r++)
        {
            for (int d = 0; d < directions; d++)
            {
                double angle = 2.0 * Math.PI * d / directions;
                float cx = x + (float)Math.Cos(angle) * radii[r];
                float cz = z + (float)Math.Sin(angle) * radii[r];
                if (!bounds.Contains(cx, cz) || !isFree(cx, cz)) continue;
                freeX = cx;
                freeZ = cz;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 구간 순서 어디에서도 설 자리를 못 찾았을 때 — 튜토리얼 진행 중에는 씬 시작 지점(추락 복구의 스폰 폴백)으로 보내지 않고
    /// 순서의 첫 '지점이 있는' 구간(보통 지금 단계 구간)을 고른다. 그 지점에 그대로(겹침 허용) 내린다. 지점이 하나도 없으면 false(기존 동작).
    /// </summary>
    public static bool TryPickLastResort(IReadOnlyList<TutorialArea> order, Func<TutorialArea, bool> hasPoint, out TutorialArea area)
    {
        if (order != null && hasPoint != null)
        {
            for (int i = 0; i < order.Count; i++)
            {
                if (!hasPoint(order[i])) continue;
                area = order[i];
                return true;
            }
        }
        area = TutorialArea.None;
        return false;
    }

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
