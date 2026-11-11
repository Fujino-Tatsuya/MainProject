using UnityEngine;

/// <summary>
/// 배경 글자 RECTIFY 안쪽 '원형 확장 색상 전환'(267 후속, SPEC-circle-color-fill) — 원 상태 순수 함수.
/// 원 k 는 클립 시작 뒤 k × 주기에 생긴다(주기 = 페이드 + 확장 × 다음 원 비율). 생기면 크기 그대로 알파 0→1(페이드),
/// 그 뒤 반지름이 시작 → 단어 전체를 덮는 반지름으로 ease-out 확장. 색 = 목록[k % 개수](연달아 같은 색 없음).
/// 위치 = 시드 고정 해시로 글자 하나를 고르고 그 글자 안쪽 한 점. 시각만으로 정해져 스크럽·모든 피어 같다.
/// 셰이더는 지금 원과 앞의 두 원(앞앞 원은 항상 다 덮은 상태 = 바탕)을 오래된 순으로 덮어 그린다.
/// </summary>
public static class BossVictoryRectifyFill
{
    public const int CirclesDrawn = 3;

    public struct Circle
    {
        public bool exists;     // 생겼는가(k ≥ 0, 생성 시각 ≤ t)
        public float alpha;     // 0~1 페이드
        public float grow;      // 0~1 확장 진행(ease-out 적용 뒤)
        public int colorIndex;
    }

    /// <summary>원 사이 간격(초) = 페이드 + 확장 × 다음 원 비율. 최소 0.01.</summary>
    public static float Period(float fade, float expand, float nextAt) =>
        Mathf.Max(0.01f, Mathf.Max(0f, fade) + Mathf.Max(0f, expand) * Mathf.Clamp01(nextAt));

    /// <summary>클립 시작 뒤 <paramref name="elapsed"/> 초에 가장 최근에 생긴 원 번호(아직 없으면 −1).</summary>
    public static int Latest(float elapsed, float period) => elapsed < 0f ? -1 : Mathf.FloorToInt(elapsed / period);

    /// <summary>원 k 의 상태.</summary>
    public static Circle Evaluate(int k, float elapsed, float fade, float expand, float nextAt, int colorCount)
    {
        var c = new Circle { colorIndex = colorCount > 0 && k >= 0 ? k % colorCount : 0 };
        if (k < 0) return c;
        float age = elapsed - k * Period(fade, expand, nextAt);
        if (age < 0f) return c;
        c.exists = true;
        c.alpha = fade > 0f ? Mathf.Clamp01(age / fade) : 1f;
        float g = expand > 0f ? Mathf.Clamp01((age - Mathf.Max(0f, fade)) / expand) : (age >= fade ? 1f : 0f);
        c.grow = BossVictoryKeys.Ease(BossVictoryKeyCurve.EaseOut, g);
        return c;
    }

    /// <summary>원 k 가 고르는 글자 번호(시드 고정 해시).</summary>
    public static int GlyphFor(int k, int seed, int glyphCount) =>
        glyphCount <= 0 ? 0 : (int)(Hash((uint)k, (uint)seed) % (uint)glyphCount);

    /// <summary>원 k 의 글자 안 점 비율(0~1, 글자 사각형 안쪽 30~70% · 25~75% — 획 위에 오기 쉽게).</summary>
    public static Vector2 PointInGlyph(int k, int seed)
    {
        float u = (Hash((uint)k, (uint)seed + 7919u) & 0xFFFF) / 65535f;
        float v = (Hash((uint)k, (uint)seed + 104729u) & 0xFFFF) / 65535f;
        return new Vector2(Mathf.Lerp(0.3f, 0.7f, u), Mathf.Lerp(0.25f, 0.75f, v));
    }

    /// <summary>시작 → 끝 반지름 사이 현재 반지름.</summary>
    public static float Radius(float grow, float startRadius, float endRadius) => Mathf.Lerp(startRadius, endRadius, grow);

    static uint Hash(uint a, uint b)
    {
        uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u + (a << 6) + (a >> 2));
        h ^= h >> 16; h *= 0x85EBCA6Bu; h ^= h >> 13; h *= 0xC2B2AE35u; h ^= h >> 16;
        return h;
    }
}
