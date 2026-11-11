using UnityEngine;

/// <summary>
/// 배경 글자 RECTIFY 안쪽 'Ring Wipe Fill'(267 후속, _lane-artifacts/Work2/rectify-fill/SPEC-ring-wipe-fill.md) — 링 상태 순수 함수.
/// 링 i(0 마젠타 · 1 블루 · 2 그레이)는 클립 시작 뒤 i × 간격에 공통 중심에서 시작 반지름으로 생겨, 크기 그대로 페이드(알파 0→1) →
/// 불투명해진 뒤 시작 반지름 → Rmax 로 easeOutCubic 확장. 나중 링이 앞 링을 덮고(painter's order) 마지막 링(그레이)이 다 덮으면 그대로 고정. 반복 없음.
/// 좌표 = 단어 사각형 UV(글자 사각형 합집합, 0~1) — 거리 단위는 단어 높이(가로에 단어 종횡비 보정).
/// 시각만으로 정해져 스크럽·모든 피어 같다(새 RPC 없음).
/// </summary>
public static class BossVictoryRectifyFill
{
    public const int RingCount = 3;

    public struct Ring
    {
        public bool exists;     // 생겼는가(생성 시각 ≤ t)
        public float alpha;     // 0~1 페이드
        public float grow;      // 0~1 확장(easeOutCubic 적용 뒤)
    }

    /// <summary>링 i 의 상태 — t_i = elapsed − i × delay(생성 시각 기준), 알파 = t_i/페이드, 확장 = easeOutCubic((t_i − 페이드)/확장 시간).</summary>
    public static Ring Evaluate(int i, float elapsed, float delay, float fade, float expand)
    {
        var r = new Ring();
        if (i < 0 || elapsed < 0f) return r;
        float t = elapsed - i * Mathf.Max(0f, delay);
        if (t < 0f) return r;
        r.exists = true;
        r.alpha = fade > 0f ? Mathf.Clamp01(t / fade) : 1f;
        float te = t - Mathf.Max(0f, fade);   // 페이드가 끝난 뒤부터 확장
        float p = expand > 0f ? Mathf.Clamp01(te / expand) : (te >= 0f ? 1f : 0f);
        r.grow = EaseOutCubic(p);
        return r;
    }

    public static float EaseOutCubic(float p)
    {
        float q = 1f - Mathf.Clamp01(p);
        return 1f - q * q * q;
    }

    /// <summary>글자 사각형(뷰포트 min.xy/max.zw) 합집합 = 단어 사각형. 없으면 zero.</summary>
    public static Vector4 WordRect(Vector4[] glyphRects, int count)
    {
        if (glyphRects == null || count <= 0) return Vector4.zero;
        Vector4 w = glyphRects[0];
        for (int i = 1; i < count && i < glyphRects.Length; i++)
        {
            Vector4 g = glyphRects[i];
            w = new Vector4(Mathf.Min(w.x, g.x), Mathf.Min(w.y, g.y), Mathf.Max(w.z, g.z), Mathf.Max(w.w, g.w));
        }
        return w;
    }

    /// <summary>단어 종횡비(가로/세로, 화면 비율 반영) — 단어 UV 거리 보정용.</summary>
    public static float WordAspect(Vector4 wordRect, float screenAspect)
    {
        float h = wordRect.w - wordRect.y;
        return h > 1e-6f ? (wordRect.z - wordRect.x) * screenAspect / h : 1f;
    }

    /// <summary>Rmax — 중심(단어 UV)에서 네 모서리까지 최대 거리(단어 높이 단위, 가로 = 종횡비 보정).</summary>
    public static float MaxRadius(Vector2 center, float wordAspect)
    {
        float dx = Mathf.Max(center.x, 1f - center.x) * wordAspect;
        float dy = Mathf.Max(center.y, 1f - center.y);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>현재 반지름 — 시작 반지름 → Rmax + 경계(다 퍼지면 모서리까지 완전히 덮도록). 단위 = 단어 높이.</summary>
    public static float Radius(float grow, float startRadius, float maxRadius, float softness) =>
        Mathf.Lerp(Mathf.Max(0f, startRadius), maxRadius + Mathf.Max(0f, softness), grow);

    // ── 4번째 흰 원 · 주황 채움(267 후속) ──

    /// <summary>흰 원 — 클립 시작 뒤 <paramref name="start"/> 초에 생김. 페이드 → 확장 순서는 링과 같다(값만 따로).</summary>
    public static Ring WhiteRing(float elapsed, float start, float fade, float expand) =>
        elapsed < 0f ? default : Evaluate(0, elapsed - Mathf.Max(0f, start), 0f, fade, expand);

    /// <summary>주황 채움 시작 시각(클립 시작 기준) = 흰 원이 다 퍼진 뒤 + 유지.</summary>
    public static float OrangeStart(float whiteStart, float whiteFade, float whiteExpand, float hold) =>
        Mathf.Max(0f, whiteStart) + Mathf.Max(0f, whiteFade) + Mathf.Max(0f, whiteExpand) + Mathf.Max(0f, hold);

    /// <summary>흰 → 주황 와이프 진행 0~1(시작 전 0, 와이프 0초면 시작 즉시 1).</summary>
    public static float OrangeProgress(float elapsed, float orangeStart, float wipe)
    {
        if (elapsed < orangeStart) return 0f;
        return wipe > 0f ? Mathf.Clamp01((elapsed - orangeStart) / wipe) : 1f;
    }

    /// <summary>
    /// 줄 단위 들쭉날쭉 와이프 — 단어 UV x(0~1)가 덮였나. 시작 쪽부터 경계가 지나가고 줄마다 <paramref name="rowOffset"/>(−0.5~0.5) × 들쭉날쭉만큼
    /// 앞뒤로 어긋난다. 진행 0 = 하나도 안 덮음, 1 = 전부 덮음(셰이더와 같은 식).
    /// </summary>
    public static bool WipeCovered(float x01, float progress, float jitter, float rowOffset, bool rightToLeft)
    {
        float j = Mathf.Max(0f, jitter);
        float s = rightToLeft ? 1f - x01 : x01;
        float edge = Mathf.Lerp(-j, 1f + j, Mathf.Clamp01(progress));
        return s + rowOffset * j < edge;
    }

    /// <summary>주황 채움 색 — 왼쪽 → 오른쪽 보간(세기 0 = 왼쪽 색만).</summary>
    public static Color OrangeColor(float x01, Color left, Color right, float strength) =>
        Color.Lerp(left, Color.Lerp(left, right, Mathf.Clamp01(x01)), Mathf.Clamp01(strength));
}
