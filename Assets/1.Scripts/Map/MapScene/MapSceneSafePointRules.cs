using System;
using System.Collections.Generic;

// 튜토리얼 스텝(구간)별 고정 안전 지점(할 일 80 — 1안) 순수 규칙(Unity·NGO 비의존 — EditMode 테스트 대상).
// 배선은 MapSceneSafePoints(4.MapScene, 서버). 문서: Docs/tech/tutorial-director.md §12.

/// <summary>고른 복귀 자리 — 지점 그대로 · 막혀서 주변 링 빈자리 · 링도 실패해 지점 그대로(겹침 허용).</summary>
public enum MapSceneSafeSpotKind { Point, Ring, PointOverlap }

/// <summary>
/// 튜토리얼 복귀 구간 — None = 기존 경로(override null) · Step = 지금 스텝 고정 지점 · BossCountdown = 보스방 이동 카운트다운(보스 진입 고정 지점) ·
/// BossFight = 보스방 도착 뒤(재합류 모서리).
/// </summary>
public enum MapSceneReturnPhase { None, Step, BossCountdown, BossFight }

public static class MapSceneSafePointRules
{
    /// <summary>기본 지점 — 들어오는 배리어 앞면에서 구간 안쪽으로(m). 존1 은 가운데.</summary>
    public const float DefaultZoneEntryInset = 2.5f;
    /// <summary>복도는 짧다(약 9.6m) — 중간 지점(뒤쪽 절반)에 닿지 않게 입구 가까이.</summary>
    public const float DefaultCorridorEntryInset = 1.5f;

    /// <summary>복귀 높이 기본값 — 지점 Transform 위로 이만큼(m) 띄워 내려놓고 떨어져 착지한다.</summary>
    public const float DefaultDropHeight = 3f;

    /// <summary>지점이 막혔을 때 둘러보는 링 반경(m, 가까운 것부터)·방향 수.</summary>
    public static readonly IReadOnlyList<float> DefaultRingRadii = new[] { 0.75f, 1.5f, 2.25f, 3f };
    public const int DefaultRingDirections = 8;

    static readonly string[] BlockingLayerNames = { "Default", "Ground", "Env", "Wall", "Barrier" };

    /// <summary>
    /// 지점을 막는(점유 검사) 레이어 = 정적 장애물만 — 바닥 레이어 위 소품(Default·Ground·Env)·벽(Wall)·배리어(Barrier).
    /// 몬스터(Enemy·EnemyHurtBox·이름 없는 래그돌 뼈 레이어)·플레이어·시체·Soul·무기처럼 움직이는 것은 넣지 않는다 —
    /// 겹쳐 내려도 유닛 충돌 밀어내기(할 일 11)가 푼다. 자동 부활 격자(<c>MapSceneDeathRules</c>)와 같은 조합.
    /// </summary>
    public static IReadOnlyList<string> BlockingLayers => BlockingLayerNames;

    public static bool IsBlockingLayer(string layerName) => Array.IndexOf(BlockingLayerNames, layerName) >= 0;

    /// <summary>
    /// 고정 지점(<paramref name="x"/>, <paramref name="z"/>)이 정적 장애물에 막혔을 때 그 주변 빈자리 — 반경 <paramref name="radii"/>(앞에서부터 = 가까운 것부터)마다
    /// <paramref name="directions"/> 방향(+X 부터 반시계)을 보고, 구간 사각형 <paramref name="bounds"/> 안이면서 <paramref name="isFree"/> 인 첫 점.
    /// 구간 밖(벽 너머 다른 구간·맵 밖)은 보지 않는다. 없으면 false.
    /// </summary>
    public static bool TryFindNearestFree(float x, float z, MapSceneRect bounds, IReadOnlyList<float> radii, int directions,
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
    /// 지금 스텝 지점에서 내릴 자리(XZ) — 지점이 정적 장애물에 막히지 않았으면(<paramref name="pointBlocked"/> false) 지점 그대로,
    /// 막혔으면 구간 <paramref name="bounds"/> 안 주변 링의 가장 가까운 빈자리(<see cref="TryFindNearestFree"/>),
    /// 링도 실패(또는 구간 사각형 없음)하면 지점 그대로(겹침 허용). 다른 구간·기록 지점·씬 시작 지점으로는 가지 않는다.
    /// </summary>
    public static MapSceneSafeSpotKind PickSpot(float x, float z, bool pointBlocked, MapSceneRect? bounds,
                                                Func<float, float, bool> isFree, out float spotX, out float spotZ)
    {
        spotX = x;
        spotZ = z;
        if (!pointBlocked) return MapSceneSafeSpotKind.Point;
        if (bounds.HasValue && TryFindNearestFree(x, z, bounds.Value, DefaultRingRadii, DefaultRingDirections, isFree, out spotX, out spotZ))
            return MapSceneSafeSpotKind.Ring;
        spotX = x;
        spotZ = z;
        return MapSceneSafeSpotKind.PointOverlap;
    }

    /// <summary>
    /// 지금 스텝 구간 = 자동 부활과 같은 '그 플레이어 기준 지금 단계 구간'
    /// (<see cref="MapSceneRevivePointRules.ResolveStageArea(MapSceneStep, int, int)"/>). 보스 진입은 G10 을 지난 사람만.
    /// </summary>
    public static MapSceneArea StepArea(MapSceneStep step, int openedGateMask, int passedMask) =>
        MapSceneRevivePointRules.ResolveStageArea(step, openedGateMask, passedMask);

    /// <summary>
    /// 복귀 지점 고르기 — 지금 스텝 구간(<paramref name="stepArea"/>)에 지점이 있으면 항상 그것만(다른 구간 대체 없음).
    /// 내릴 자리는 <see cref="PickSpot"/>. 없으면 false(기존 추락 복구 경로).
    /// </summary>
    public static bool TryPickStepPoint(MapSceneArea stepArea, Func<MapSceneArea, bool> hasPoint, out MapSceneArea area)
    {
        area = stepArea != MapSceneArea.None && hasPoint != null && hasPoint(stepArea) ? stepArea : MapSceneArea.None;
        return area != MapSceneArea.None;
    }

    /// <summary>복귀 높이 — 고른 자리(지점 또는 링 빈자리, 높이는 지점 Y) + <paramref name="dropHeight"/>(음수는 0).</summary>
    public static float DropY(float pointY, float dropHeight) => pointY + Math.Max(0f, dropHeight);

    /// <summary>
    /// 지금 복귀 구간 — 튜토리얼 밖·활성 전 = <see cref="MapSceneReturnPhase.None"/>.
    /// 단계 Finished(보스방 도착 뒤 = 보스전) = <see cref="MapSceneReturnPhase.BossFight"/>.
    /// 보스방 이동 카운트다운(<paramref name="countdownActive"/> — 패드·타이머 만료 강제 공통) = <see cref="MapSceneReturnPhase.BossCountdown"/>.
    /// 카운트다운이 끝난 뒤 엘리베이터 연출·도착 대기(<paramref name="encounterBusy"/>)는 None — 기존 경로 그대로(할 일 89).
    /// 그 밖 = <see cref="MapSceneReturnPhase.Step"/>(지금 스텝 고정 지점).
    /// </summary>
    public static MapSceneReturnPhase ReturnPhase(bool tutorialActive, MapSceneStep step, bool countdownActive, bool encounterBusy)
    {
        if (!tutorialActive || step == MapSceneStep.None) return MapSceneReturnPhase.None;
        if (step == MapSceneStep.Finished) return encounterBusy ? MapSceneReturnPhase.None : MapSceneReturnPhase.BossFight;
        if (countdownActive) return MapSceneReturnPhase.BossCountdown;
        return encounterBusy ? MapSceneReturnPhase.None : MapSceneReturnPhase.Step;
    }

    /// <summary>
    /// 복귀 구간별 고정 지점 구간 — 스텝 = <paramref name="stepArea"/>, 보스방 이동 카운트다운 = 보스 진입 구간.
    /// 보스전(모서리)·None 은 구간 지점을 쓰지 않는다(None).
    /// </summary>
    public static MapSceneArea FixedPointArea(MapSceneReturnPhase phase, MapSceneArea stepArea) => phase switch
    {
        MapSceneReturnPhase.Step => stepArea,
        MapSceneReturnPhase.BossCountdown => MapSceneArea.BossEnter,
        _ => MapSceneArea.None,
    };

    /// <summary>
    /// 저작 메뉴가 처음 놓는 기본 위치(월드 XZ) — 존1 = 가운데, 그 밖 = 그 구간으로 들어오는 배리어(너머 구간 = area)의
    /// 진행 방향 앞면에서 안쪽으로 <paramref name="inset"/>, 가로는 배리어 가운데. 배리어·구간이 없으면 false.
    /// </summary>
    public static bool TryDefaultAnchor(MapSceneArea area, MapSceneAreaTracker areas, IReadOnlyList<MapSceneBarrierSpec> specs,
                                        float inset, out float x, out float z)
    {
        x = z = 0f;
        if (areas == null || !areas.TryGetArea(area, out MapSceneRect rect)) return false;
        if (area == MapSceneArea.Zone1)
        {
            x = rect.CenterX;
            z = rect.CenterZ;
            return true;
        }

        if (specs == null) return false;
        for (int i = 0; i < specs.Count; i++)
        {
            MapSceneBarrierSpec s = specs[i];
            if (MapSceneBackBarrierRules.AreaBeyond(s) != area) continue;
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
