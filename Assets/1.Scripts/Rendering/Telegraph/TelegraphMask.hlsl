// ----------------------------------------------------------------------------
//  TelegraphMask.hlsl — 공격 예고 색·마스크 합성(PLAN-telegraph-style)
//
//  두 경로가 같은 계산을 쓴다:
//   - 데칼  SG_TelegraphDecal  (커스텀 함수 노드 → TelegraphShade_float/half)
//   - 판    Monster/TelegraphQuad (드론 원·전기 장판)
//
//  🔴 이 파일은 속성을 **선언하지 않는다.** 셰이더 그래프가 블랙보드 속성(_BaseColor·_MaskTex·_Mask*)을
//     CBUFFER 에 선언하고, 판 셰이더는 include 전에 직접 선언한다. 여기서 선언하면 그래프 쪽에서 중복 선언이 된다.
//
//  마스크 좌표 = **예고 기준점의 로컬 좌표**(월드 위치 − 원점을 예고의 좌우/전방 축에 투영).
//  🔴 데칼 UV 로 잡지 않는다 — 채움은 프로젝터 크기를 키우며 자라서 UV 기준이면 패턴이 같이 커지며 미끄러진다.
//     기준점은 보스를 따라가므로 보스가 돌면 패턴도 돈다(팀장 10-13 T7).
//
//  _MaskParams  x = 방식(0 늘이기 · 1 반복)  y = 세기(0~1)  z = 반복 크기(m)  w = 회전(라디안)
//  _MaskOrigin  xyz = 기준점(월드). 늘이기 모드면 모양 바운딩 박스의 중심
//  _MaskAxes    xy = 좌우 축(월드 xz) · zw = 전방 축(월드 xz)
//  _MaskExtent  xy = 늘이기 기준 크기(폭, 길이 m)
//  마스크 값 = R × A — 흑백 그림(R)과 알파만 있는 흰 그림(A) 둘 다 받는다. 흰 = 보임, 검·투명 = 안 보임.
// ----------------------------------------------------------------------------
#ifndef TELEGRAPH_MASK_INCLUDED
#define TELEGRAPH_MASK_INCLUDED

float2 TelegraphMaskUV(float3 worldPos)
{
    float2 d = worldPos.xz - _MaskOrigin.xz;
    float2 local = float2(dot(d, _MaskAxes.xy), dot(d, _MaskAxes.zw));   // (좌우, 전방) m

    float s, c;
    sincos(_MaskParams.w, s, c);
    local = float2(local.x * c - local.y * s, local.x * s + local.y * c);

    if (_MaskParams.x < 0.5)
        return local / max(_MaskExtent.xy, 1e-4) + 0.5;     // 늘이기 — 모양 전체에 한 장
    return local / max(_MaskParams.z, 1e-4);                // 반복 — 반복 크기(m)마다 한 장
}

// baseRGBA = 모양 텍스처(흰색 + 모양 알파). 색은 여기서 **한 번만** 곱한다(알파 이중 곱 금지).
void TelegraphShade_float(float4 BaseRGBA, float3 WorldPos, out float3 Color, out float Alpha)
{
    float4 m = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, TelegraphMaskUV(WorldPos));
    float mask = lerp(1.0, m.r * m.a, saturate(_MaskParams.y));
    Color = BaseRGBA.rgb * _BaseColor.rgb;
    Alpha = BaseRGBA.a * _BaseColor.a * mask;
}

void TelegraphShade_half(half4 BaseRGBA, half3 WorldPos, out half3 Color, out half Alpha)
{
    float3 c; float a;
    TelegraphShade_float(BaseRGBA, WorldPos, c, a);
    Color = c; Alpha = a;
}

#endif
