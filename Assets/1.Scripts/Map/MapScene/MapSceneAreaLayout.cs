using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 튜토리얼 구간 모양을 <b>StageTutorial 저작 데이터</b>에서 계산해 <see cref="MapSceneAreaTracker"/> 에 넣는다(하드코딩 좌표 없음).
///  · 존 = 슬롯(SlotID 0~3 = 존1~4, 4 = 보스 진입)의 <c>transform.position</c> · <see cref="ZoneSlot.Footprint"/> · 슬롯 Y 회전.
///    스테이지 180° 회전은 저작 메뉴(<c>MapSceneStageAuthoring.RotateAroundWalls</c>)가 슬롯 위치에 이미 구워 넣었다.
///    존 프리팹의 실제 스폰 위치(<see cref="ZoneSlot.TryGetPosition"/>)가 아니라 슬롯 위치를 쓰는 이유: 복도 문턱은 슬롯 사각형 경계에
///    맞춰 저작돼 있다(존3 M_typeC 는 프리팹 피벗 때문에 스폰 위치가 0.4m 어긋난다 — 시나리오 §1.1 의 −160.95 가 그 값).
///  · 복도 n = 존n ↔ 존n+1(복도4 = 존4 ↔ 보스 진입) 사각형 사이 틈(길이) × 그 틈에 놓인 실제 복도 바닥 콜라이더의 가로 범위(너비).
///    바닥 = 존 슬롯 밖의 납작하고(두께 ≤ 1m) 윗면이 스테이지 높이 +0.5m 이하인 비트리거 콜라이더 중 복도 길이의 가운데 절반에 걸친 것.
///    못 찾으면 두 존 가로 겹침 가운데 ±2m(경고).
///  · 복도 중간 지점 = 복도 한가운데 선 → 다음 존 쪽 끝(<see cref="MapSceneCorridorGeometry.MakeCorridor"/>).
/// </summary>
public static class MapSceneAreaLayout
{
    public const int SlotCount = 5;
    const float FloorMaxHeight = 1f;
    const float FloorMaxTopAboveStage = 0.5f;
    const float FallbackHalfWidth = 2f;

    static readonly MapSceneArea[] SlotAreas =
        { MapSceneArea.Zone1, MapSceneArea.Zone2, MapSceneArea.Zone3, MapSceneArea.Zone4, MapSceneArea.BossEnter };

    static readonly MapSceneArea[] CorridorAreas =
        { MapSceneArea.Corridor1, MapSceneArea.Corridor2, MapSceneArea.Corridor3, MapSceneArea.Corridor4 };

    /// <summary>
    /// <paramref name="stage"/>(StageTutorial 루트 — <see cref="MapSceneStageMarker"/>) 아래 슬롯·바닥으로 구간을 채운다.
    /// 슬롯이 모자라거나 복도 틈이 없으면 false + <paramref name="error"/>(채울 수 있는 만큼은 채운다).
    /// </summary>
    public static bool TryBuild(Transform stage, MapSceneAreaTracker tracker, out string error)
    {
        error = null;
        if (stage == null) { error = "스테이지 없음"; return false; }

        var slots = new ZoneSlot[SlotCount];
        foreach (ZoneSlot s in stage.GetComponentsInChildren<ZoneSlot>(true))
        {
            if (s.SlotID < 0 || s.SlotID >= SlotCount) continue;
            if (slots[s.SlotID] != null) { error = $"SlotID {s.SlotID} 중복('{slots[s.SlotID].name}' · '{s.name}')"; return false; }
            slots[s.SlotID] = s;
        }

        var zones = new MapSceneRect[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            if (slots[i] == null) { error = $"SlotID {i} 슬롯이 없다"; return false; }
            Transform t = slots[i].transform;
            Vector3 p = t.position;
            zones[i] = new MapSceneRect(p.x, p.z, slots[i].Footprint.x * 0.5f, slots[i].Footprint.y * 0.5f, t.eulerAngles.y);
            tracker.SetArea(SlotAreas[i], zones[i]);
        }

        List<Bounds> floors = CollectFloorBounds(stage);
        bool ok = true;
        for (int c = 0; c < MapSceneAreaTracker.CorridorCount; c++)
        {
            MapSceneRect from = zones[c], to = zones[c + 1];
            if (!MapSceneCorridorGeometry.TryGetGap(from, to, out bool alongX, out float gapMin, out float gapMax,
                                                    out float crossMin, out float crossMax))
            {
                error = $"복도{c + 1}: 존 {c + 1}·{c + 2} 슬롯 사각형 사이에 틈이 없다(겹치거나 대각선)";
                ok = false;
                continue;
            }

            if (!TryGetFloorCross(floors, alongX, gapMin, gapMax, crossMin, crossMax, out float fMin, out float fMax))
            {
                float mid = (crossMin + crossMax) * 0.5f;
                fMin = mid - FallbackHalfWidth;
                fMax = mid + FallbackHalfWidth;
                error = $"복도{c + 1}: 바닥 콜라이더를 못 찾아 가운데 ±{FallbackHalfWidth}m 로 잡았다";
            }

            MapSceneCorridorGeometry.MakeCorridor(from, to, alongX, gapMin, gapMax, fMin, fMax,
                                                  out MapSceneRect corridor, out MapSceneRect midRect);
            tracker.SetArea(CorridorAreas[c], corridor);
            tracker.SetCorridorMid(c + 1, midRect);
        }
        return ok;
    }

    static List<Bounds> CollectFloorBounds(Transform stage)
    {
        var list = new List<Bounds>();
        float groundTop = stage.position.y + FloorMaxTopAboveStage;
        foreach (Collider col in stage.GetComponentsInChildren<Collider>(true))
        {
            if (col.isTrigger || col.GetComponentInParent<ZoneSlot>(true) != null) continue;
            Bounds b = col.bounds;
            // 납작하고 바닥 높이인 것만 — 창틀 캡(높이 2.75m, 납작함)을 뺀다
            if (b.size.y > FloorMaxHeight || b.max.y > groundTop || b.size.x <= 0f || b.size.z <= 0f) continue;
            list.Add(b);
        }
        return list;
    }

    /// <summary>
    /// 복도 길이의 가운데 절반에 걸치고 가로 중심이 두 존 겹침 안인 바닥들의 가로 범위 합.
    /// 가운데 절반만 보는 이유: 복도 양 끝(존 경계)에는 문턱·벽 문틀 바닥 조각(floor_bar_metal 등)이 옆으로 비껴 놓여 있다.
    /// </summary>
    static bool TryGetFloorCross(List<Bounds> floors, bool alongX, float gapMin, float gapMax, float crossMin, float crossMax,
                                 out float min, out float max)
    {
        min = float.MaxValue;
        max = float.MinValue;
        float quarter = (gapMax - gapMin) * 0.25f;
        float innerMin = gapMin + quarter, innerMax = gapMax - quarter;
        foreach (Bounds b in floors)
        {
            float bMin = alongX ? b.min.x : b.min.z, bMax = alongX ? b.max.x : b.max.z;
            float cross = alongX ? b.center.z : b.center.x;
            if (bMax <= innerMin || bMin >= innerMax || cross <= crossMin || cross >= crossMax) continue;
            min = Mathf.Min(min, alongX ? b.min.z : b.min.x);
            max = Mathf.Max(max, alongX ? b.max.z : b.max.x);
        }
        if (max <= min) return false;
        min = Mathf.Max(min, crossMin);
        max = Mathf.Min(max, crossMax);
        return max > min;
    }

    // ─── 디버그 기즈모 ────────────────────────────────────────────────────

    static readonly Color ZoneColor = new Color(0.3f, 0.8f, 1f);
    static readonly Color CorridorColor = new Color(1f, 0.8f, 0.2f);
    static readonly Color MidColor = new Color(1f, 0.3f, 0.9f);

    /// <summary>구간(실선) · 들어갈 때 안쪽 여유 · 나갈 때 바깥 여유(옅은 선) · 복도 중간 지점을 그린다. 높이 <paramref name="y"/>.</summary>
    public static void DrawGizmos(MapSceneAreaTracker tracker, float y)
    {
        for (var a = MapSceneArea.Zone1; a <= MapSceneArea.BossEnter; a++)
        {
            if (!tracker.TryGetArea(a, out MapSceneRect r)) continue;
            Color c = MapSceneAreaTracker.CorridorIndexOf(a) > 0 ? CorridorColor : ZoneColor;
            DrawRect(r, 0f, y, c);
            DrawRect(r, -tracker.EnterMargin, y, new Color(c.r, c.g, c.b, 0.45f));
            DrawRect(r, tracker.ExitMargin, y, new Color(c.r, c.g, c.b, 0.25f));
#if UNITY_EDITOR
            UnityEditor.Handles.Label(new Vector3(r.CenterX, y + 1f, r.CenterZ), a.ToString());
#endif
        }
        for (int i = 1; i <= MapSceneAreaTracker.CorridorCount; i++)
        {
            if (!tracker.TryGetCorridorMid(i, out MapSceneRect m)) continue;
            DrawRect(m, -tracker.EnterMargin, y + 0.05f, MidColor);
        }
    }

    static void DrawRect(MapSceneRect r, float margin, float y, Color color)
    {
        Gizmos.color = color;
        for (int i = 0; i < 4; i++)
        {
            r.GetCorner(i, margin, out float ax, out float az);
            r.GetCorner((i + 1) % 4, margin, out float bx, out float bz);
            Gizmos.DrawLine(new Vector3(ax, y, az), new Vector3(bx, y, bz));
        }
    }
}
