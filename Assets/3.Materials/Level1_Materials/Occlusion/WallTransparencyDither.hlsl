#ifndef WALL_TRANSPARENCY_DITHER_INCLUDED
#define WALL_TRANSPARENCY_DITHER_INCLUDED

// 기존 벽 오클루전 셰이더와 같은 interleaved gradient noise를 쓴다.
// 화면 픽셀 좌표만 입력받아 전역 유니폼 없이 고정된 디더 패턴을 만든다.
float WallTransparencyDitherThreshold(float2 screenPixelPosition)
{
    float2 pixel = floor(screenPixelPosition);
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

void WallTransparencyDither_float(
    float2 ScreenPixelPosition,
    float Opacity,
    out float Alpha)
{
    Alpha = Opacity - WallTransparencyDitherThreshold(ScreenPixelPosition);
}

void WallTransparencyDither_half(
    half2 ScreenPixelPosition,
    half Opacity,
    out half Alpha)
{
    // half 그래프에서도 임계값 계산은 float로 유지해야 기존 셰이더와 같은 무늬가 나온다.
    Alpha = (half)((float)Opacity - WallTransparencyDitherThreshold((float2)ScreenPixelPosition));
}

#endif
