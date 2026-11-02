using System.Collections.Generic;
using UnityEngine;

/// <summary>2구역 적 소개 순수 규칙(EditMode 테스트 <c>MapSceneZone2IntroRulesTests</c>).</summary>
public static class MapSceneZone2IntroRules
{
    /// <summary>
    /// 적 무리 구도 — 위치들의 xz 범위 중심(높이 = 평균)과 그 범위를 감싸는 반경(최소 <paramref name="minRadius"/>).
    /// 위치가 없으면 false(호출자가 구간 중심으로 대신한다).
    /// </summary>
    public static bool TryFrame(IReadOnlyList<Vector3> positions, float minRadius, out Vector3 center, out float radius)
    {
        center = Vector3.zero;
        radius = minRadius;
        if (positions == null || positions.Count == 0) return false;

        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity, minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity, y = 0f;
        foreach (Vector3 p in positions)
        {
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            y += p.y;
        }
        center = new Vector3((minX + maxX) * 0.5f, y / positions.Count, (minZ + maxZ) * 0.5f);
        radius = Mathf.Max(minRadius, new Vector2(maxX - minX, maxZ - minZ).magnitude * 0.5f);
        return true;
    }

    /// <summary>
    /// 경고 대사(조각 <paramref name="chunks"/> 개)가 복귀 시작 전에 <paramref name="tail"/> 초 여유를 두고 끝나나 — 대사가 끝나기 전에 카메라가 돌아가지 않게(명세 §4.2 4번).
    /// </summary>
    public static bool LineEndsBeforeReturn(float lineStart, int chunks, float chunkSeconds, float gapSeconds, float returnAt, float tail) =>
        lineStart + CinematicLineSchedule.TotalSeconds(chunks, chunkSeconds, gapSeconds) + tail <= returnAt + 1e-4f;
}
