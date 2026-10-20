// ----------------------------------------------------------------------------
//  EnemyC3Sdf.hlsl — 적 공격 범위표시 C3 공용 SDF·합성(할 일 57 셰이더 재개발, PLAN-57-enemy-c3-shader §4.1)
//
//  EnemyC3Visuals.Bake(CPU 384×384)의 픽셀 함수를 그대로 옮겼다. 비교 기준이 Bake 이므로
//  식을 "더 낫게" 고치지 말 것 — 겉모습이 바뀐다. 바꿀 땐 Bake 와 픽셀 비교 테스트(EnemyC3PlateShaderTests)를 같이.
//
//  좌표 p = 디자인 보드 단위(원 판 = 반지름 165, 사각 판 = 반 변 150, 데칼은 월드 m). 원점 = 모양 중심.
//  aa  = 화면 한 픽셀이 p 에서 차지하는 길이(EnemyC3Aa). Bake 의 aa = sampleExtent/384 에 대응한다.
//  색  = 감마 공간 직선 알파(JSON 값 그대로). 합성(Over)도 감마 공간에서 한다 — Bake 와 같다.
//        선형 변환은 셰이더 마지막에 한 번(EnemyC3ToOutput).
//
//  🔴 이 파일은 속성을 선언하지 않는다(판·데칼 셰이더가 각자 선언). 데칼 전용 코드도 넣지 않는다.
// ----------------------------------------------------------------------------
#ifndef ENEMY_C3_SDF_INCLUDED
#define ENEMY_C3_SDF_INCLUDED

// 픽셀 한 칸 = p 의 길이. 화면 축 정렬이면 Bake 의 sampleExtent/n 과 같고, 기울어진 시점에서도 회전 불변이다.
// 🔴 분기 안에서 부르지 말 것 — 미분은 쿼드 단위라 분기 밖, 프래그먼트 맨 앞에서 한 번 구한다.
float EnemyC3Aa(float2 p)
{
    float2 dx = ddx(p), dy = ddy(p);
    return max(max(length(float2(dx.x, dy.x)), length(float2(dx.y, dy.y))), 1e-6);
}

// Bake.Over — 직선 알파 합성(감마 공간).
float4 EnemyC3Over(float4 under, float4 over, float coverage)
{
    float a = saturate(over.a * coverage);
    float total = a + under.a * (1.0 - a);
    if (total <= 0.0) return float4(0, 0, 0, 0);
    return float4((over.rgb * a + under.rgb * under.a * (1.0 - a)) / total, total);
}

float EnemyC3Box(float2 p, float2 halfSize)
{
    float2 q = abs(p) - halfSize;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
}

float EnemyC3Segment(float2 p, float2 a, float2 b)
{
    float2 ab = b - a;
    return length(p - a - ab * saturate(dot(p - a, ab) / max(0.000001, dot(ab, ab))));
}

// 사각형 쐐기 하나의 덮임(0~1) — 짝홀 내부 판정 + 변까지 거리.
float EnemyC3Polygon(float2 p, float2 a, float2 b, float2 c, float2 d, float aa)
{
    float2 v[4] = { a, b, c, d };
    bool inside = false;
    float2 prev = d;
    [unroll] for (int i = 0; i < 4; i++)
    {
        float2 cur = v[i];
        if ((cur.y > p.y) != (prev.y > p.y) && p.x < (prev.x - cur.x) * (p.y - cur.y) / (prev.y - cur.y) + cur.x)
            inside = !inside;
        prev = cur;
    }
    float dist = min(min(EnemyC3Segment(p, a, b), EnemyC3Segment(p, b, c)),
                     min(EnemyC3Segment(p, c, d), EnemyC3Segment(p, d, a)));
    return saturate(0.5 + (inside ? dist : -dist) / aa);
}

// Bake.Fins — 띠(돌진·직선) 방향 쐐기 열. p.y = 시작점부터 길이 방향.
// scalePct·spacing·start = geometry.linearMarker*, gap·innerWidth = geometry 의 안쪽 선.
float EnemyC3Fins(float2 p, float width, float len, float unit,
                  float scalePct, float spacing, float start, float gap, float innerWidth, float aa)
{
    float scale = max(0.0, scalePct) / 100.0;
    if (scale <= 0.0 || p.y < 0.0 || p.y > len) return 0.0;
    float stepLen = max(10.0, spacing) * unit;
    float index = round((p.y - start * unit) / stepLen);
    if (index < 0.0) return 0.0;
    float center = start * unit + index * stepLen;
    float fs = 38.4 / 0.146 * unit * scale, fw = width * 0.8 * scale;
    if (center + 0.047 * fs >= len - (gap + innerWidth) * unit) return 0.0;
    p.y -= center;
    float value = 0.0;
    [unroll] for (int side = -1; side <= 1; side += 2)
        value = max(value, EnemyC3Polygon(p,
            float2(0.30 * side * fw, -0.099 * fs), float2(0.013 * side * fw, 0.047 * fs),
            float2(0.145 * side * fw, 0.004 * fs), float2(0.35 * side * fw, -0.058 * fs), aa));
    return value;
}

// Bake.CircleFins — 원 둘레 안쪽 네 방향 쐐기. markerRadiusPercent = geometry.markerRadiusPercent.
float EnemyC3CircleFins(float2 p, float radius, float markerRadiusPercent, float aa)
{
    float unit = radius / 165.0, rad = radius * markerRadiusPercent / 100.0, value = 0.0;
    [unroll] for (int i = 0; i < 4; i++)
    {
        float a = i * 1.57079632679;   // Mathf.PI/2
        float2 outward = float2(cos(a), sin(a)), side = float2(-outward.y, outward.x);
        float2 q = float2(dot(p, side), dot(p, outward) - rad) / unit;
        value = max(value, EnemyC3Polygon(q, float2(-9, 0), float2(0, -16), float2(9, 0), float2(0, -6), aa / unit));
    }
    return value;
}

// 원 SDF.
float EnemyC3CircleSdf(float2 p, float radius) { return length(p) - radius; }

// Bake.Decal 의 부채꼴 SDF — +Y 가 정면, halfAngle(도). 179.9 이상이면 원.
// 각 범위 밖에서 가장 가까운 호 위의 점은 끝점이다(전체 원 테두리가 생기지 않게).
float EnemyC3SectorSdf(float2 p, float radius, float halfAngleDeg)
{
    if (halfAngleDeg >= 179.9) return length(p) - radius;
    float a = radians(halfAngleDeg);
    float2 edge = float2(sin(a), cos(a)) * radius;
    float2 folded = float2(abs(p.x), p.y);
    float angle = atan2(folded.x, folded.y);
    float arcAngle = min(angle, a);
    float2 arcPoint = float2(sin(arcAngle), cos(arcAngle)) * radius;
    float dist = min(length(folded - arcPoint), EnemyC3Segment(folded, float2(0, 0), edge));
    bool inside = length(p) <= radius && angle <= a;
    return inside ? -dist : dist;
}

// Bake.Decal 의 합집합 SDF(띠 + 끝 원). p 원점 = 텍스처 중심, extentY = 데칼 길이(모양 아래 끝이 시작점).
float EnemyC3UnionSdf(float2 p, float extentY, float pathWidth, float pathLength, float coneCenter, float coneRadius)
{
    p.y += extentY * 0.5;
    return min(EnemyC3Box(p - float2(0, pathLength * 0.5), float2(pathWidth * 0.5, pathLength * 0.5)),
               length(p - float2(0, coneCenter)) - coneRadius);
}

// 띠 그라데이션 덮임 — 원 판(중심에서 바깥으로)과 그 밖(경계에서 안쪽으로) 두 식.
float EnemyC3BandCircle(float2 p, float coreRadius, float gradientStart)
{
    float lo = gradientStart / 100.0;
    // Mathf.InverseLerp(lo, 1, v) — lo == 1 이면 0.
    return lo != 1.0 ? saturate((length(p) / coreRadius - lo) / (1.0 - lo)) : 0.0;
}

float EnemyC3BandLinear(float d, float unit, float gradientStart)
{
    return saturate(1.0 + d / max(unit, 51.16 * (1.0 - gradientStart / 100.0) * unit));
}

// 겉모습 한 층의 색 5종(감마, 직선 알파).
struct EnemyC3Palette
{
    float4 outline, innerLine, band, fill, markers;
};

// Bake 의 테두리 층 합성: (채움) → 띠 → 쐐기 → 안쪽 선 → 바깥 외곽선.
// d = 모양 SDF, band = 띠 덮임, marker = 쐐기 덮임, fillUnder = 09·16 처럼 채움을 테두리 층에 깔지.
// outerWidth·innerWidth·gap 은 디자인 단위 — unit 을 곱해 p 단위로 바꾼다.
float4 EnemyC3Boundary(float d, float aa, float unit, float band, float marker, bool fillUnder,
                       float outerWidth, float innerWidth, float gap, EnemyC3Palette c)
{
    float inside = saturate(0.5 - d / aa);
    float4 col = float4(0, 0, 0, 0);
    if (fillUnder) col = EnemyC3Over(col, c.fill, inside);
    col = EnemyC3Over(col, c.band, band * inside);
    col = EnemyC3Over(col, c.markers, marker * inside);
    float innerStart = gap * unit, innerEnd = innerStart + innerWidth * unit;
    float inner = saturate(0.5 + (-d - innerStart) / aa) * saturate(0.5 + (d + innerEnd) / aa);
    col = EnemyC3Over(col, c.innerLine, inner * inside);
    float rim = saturate(0.5 + d / aa) * saturate(0.5 + (outerWidth * unit - d) / aa);
    return EnemyC3Over(col, c.outline, rim);
}

// Bake 의 채움 층(fill=true): 모양 안을 채움 색으로만.
float4 EnemyC3Fill(float d, float aa, float4 fillColor)
{
    return EnemyC3Over(float4(0, 0, 0, 0), fillColor, saturate(0.5 - d / aa));
}

// 감마 직선 알파 → 출력 색공간(선형 프로젝트면 선형). Bake 텍스처는 sRGB 로 읽혀 선형이 됐다 — 같은 결과.
float3 EnemyC3ToOutput(float3 gammaRgb)
{
#if defined(UNITY_COLORSPACE_GAMMA)
    return gammaRgb;
#else
    return SRGBToLinear(gammaRgb);
#endif
}

#endif
