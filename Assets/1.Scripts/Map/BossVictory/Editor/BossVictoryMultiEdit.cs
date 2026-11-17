using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 처치 연출 타임라인 창 여러 개 선택 편집(할 일 296)의 순수 계산 — 테스트 <c>BossVictoryMultiEditTests</c>.
/// 끌기 = 잡은 노드 기준 스냅 Δ 를 선택 전부에 같은 Δ 로(0초 앞으로는 못 가게 Δ 를 한 번에 막아 간격 유지) ·
/// 클립 끝 끌기 = 선택 클립 전부 같은 Δ 로 길이(클립마다 최소 길이·0초 지킴).
/// </summary>
public static class BossVictoryMultiEdit
{
    /// <summary>0.01초 스냅(창과 같은 식).</summary>
    public static float Snap(float t, bool snap) => snap ? Mathf.Round(t * 100f) / 100f : t;

    /// <summary>잡은 노드 원래 시각 <paramref name="grabbedOrigin"/> 기준 스냅 Δ — 잡은 노드는 스냅 칸에 붙고, 그 Δ 를 나머지에 그대로.</summary>
    public static float SnapDelta(float grabbedOrigin, float delta, bool snap) => Snap(grabbedOrigin + delta, snap) - grabbedOrigin;

    /// <summary>
    /// 같이 움직이는 노드 중 가장 앞 시각 <paramref name="earliestOrigin"/> 이 0 아래로 가지 않게 Δ 를 막는다 — 간격(서로 거리)은 그대로.
    /// 움직일 노드가 없으면(<paramref name="earliestOrigin"/> = +∞) Δ 그대로.
    /// </summary>
    public static float ClampMoveDelta(float delta, float earliestOrigin) =>
        float.IsInfinity(earliestOrigin) ? delta : Mathf.Max(delta, -Mathf.Max(0f, earliestOrigin));

    /// <summary>가장 앞 시각(없으면 +∞).</summary>
    public static float Earliest(IEnumerable<float> origins)
    {
        float min = float.PositiveInfinity;
        foreach (float t in origins) min = Mathf.Min(min, t);
        return min;
    }

    /// <summary>오른쪽 끝 끌기 — 시작 고정, 길이 + Δ(최소 길이).</summary>
    public static void ResizeRight(float start, float length, float delta, float minLength, out float newStart, out float newLength)
    {
        newStart = start;
        newLength = Mathf.Max(minLength, length + delta);
    }

    /// <summary>왼쪽 끝 끌기 — 끝 고정, 시작 + Δ(0초 ~ 끝 − 최소 길이).</summary>
    public static void ResizeLeft(float start, float length, float delta, float minLength, out float newStart, out float newLength)
    {
        float end = start + length;
        newStart = Mathf.Clamp(start + delta, 0f, Mathf.Max(0f, end - minLength));
        newLength = Mathf.Max(minLength, end - newStart);
    }

    /// <summary>키 이동 — 클립 비율 u 를 Δ초만큼(구간 길이 <paramref name="span"/>). 앞뒤 키 사이로 막는 건 호출자(<c>BossVictoryKeys.ClampMiddleU</c>).</summary>
    public static float MoveKeyU(float u, float span, float delta) => u + delta / Mathf.Max(1e-4f, span);

    /// <summary>
    /// 사각형 선택 결과 — 기존 선택 <paramref name="baseSelection"/> 에 <paramref name="hits"/> 를 더하거나(<paramref name="additive"/>) 바꾼다. 순서 유지·중복 없음.
    /// </summary>
    public static List<T> CombineSelection<T>(IReadOnlyList<T> baseSelection, IReadOnlyList<T> hits, bool additive)
    {
        var result = new List<T>();
        if (additive && baseSelection != null)
            foreach (T b in baseSelection)
                if (!result.Contains(b)) result.Add(b);
        if (hits != null)
            foreach (T h in hits)
                if (!result.Contains(h)) result.Add(h);
        return result;
    }

    /// <summary>Shift/Ctrl 클릭 토글 — 있으면 빼고 없으면 넣는다. 넣었으면 true.</summary>
    public static bool Toggle<T>(List<T> selection, T node)
    {
        if (selection.Remove(node)) return false;
        selection.Add(node);
        return true;
    }
}
