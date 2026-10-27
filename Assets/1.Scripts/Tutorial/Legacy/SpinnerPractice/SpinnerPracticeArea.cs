using UnityEngine;

/// <summary>
/// 연습 모드 이동 범위(스폰 지점 중심 수평 원) 계산. 순수 함수 — 높이(y)는 건드리지 않는다.
/// 반경 0 이하는 "기준점에 고정"으로 읽는다(제한 없음이 아니다 — 출구 배리어 너머로 새는 쪽이 더 나쁘다).
/// </summary>
public static class SpinnerPracticeArea
{
    /// <summary>수평 거리가 반경을 넘으면 기준점 쪽으로 당겨 원 위에 놓는다. y 는 입력 그대로.</summary>
    public static Vector3 ClampHorizontal(Vector3 position, Vector3 anchor, float radius)
    {
        float r = Mathf.Max(0f, radius);
        Vector2 offset = new Vector2(position.x - anchor.x, position.z - anchor.z);
        if (offset.sqrMagnitude <= r * r) return position;

        Vector2 clamped = offset.sqrMagnitude > 0f ? offset.normalized * r : Vector2.zero;
        return new Vector3(anchor.x + clamped.x, position.y, anchor.z + clamped.y);
    }

    /// <summary>
    /// 직선 이동 <paramref name="from"/> → <paramref name="to"/> 의 끝점을 원 안으로 자른다 — <b>같은 직선 위</b>에서 원과 만나는
    /// 가장 먼 점. 돌진 방향을 꺾지 않기 위해서다(원 위 최근접점으로 당기면 비스듬히 휜다).
    /// 출발점이 이미 원 밖이고 직선이 원을 지나지 않으면 출발점을 돌려준다(움직이지 않음).
    /// </summary>
    public static Vector3 ClampSegmentEnd(Vector3 from, Vector3 to, Vector3 anchor, float radius)
    {
        float r = Mathf.Max(0f, radius);
        Vector2 o = new Vector2(from.x - anchor.x, from.z - anchor.z);
        Vector2 d = new Vector2(to.x - from.x, to.z - from.z);
        Vector2 end = o + d;
        if (end.sqrMagnitude <= r * r) return to;

        float a = d.sqrMagnitude;
        if (a <= 0f) return from;

        // |o + t·d|² = r² 의 큰 근(t ∈ [0, 1]).
        float b = 2f * Vector2.Dot(o, d);
        float c = o.sqrMagnitude - r * r;
        float disc = b * b - 4f * a * c;
        if (disc < 0f) return from;

        float t = (-b + Mathf.Sqrt(disc)) / (2f * a);
        if (t <= 0f) return from;
        t = Mathf.Min(1f, t);

        return Vector3.Lerp(from, to, t);
    }
}
