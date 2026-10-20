// 바닥 판 예고(드론 착탄 원·전기 장판 사각) — PLAN-telegraph-style.
// 🔴 Sprites/Default 를 대체한다: 같은 블렌드(프리멀티 알파)·ZWrite Off·Cull Off·같은 속성명(_MainTex·_Color)이라
//    BossPatternVisuals.Paint(MaterialPropertyBlock) 호출이 그대로 동작한다. 마스크가 없으면(세기 0) 결과도 같다.
// 색·마스크 계산은 데칼(SG_TelegraphDecal)과 같은 TelegraphMask.hlsl.
// _CodeShape(할 일 57 S4c2) — 0 이면(기본) 예전 그대로 _MainTex. 1~3 이면 BossPatternVisuals 가 런타임에 굽던
//    64²/128² 흰 텍스처(알파만)를 셰이더에서 재현한다: 텍셀 중심마다 예전 식 → 8비트 → 이중선형(Clamp).
//    색·마스크·블렌드는 텍스처 경로와 같다. 예전 원본 식 = Editor/LegacyDecalTextureReference(픽셀 비교 기준).
Shader "Monster/TelegraphQuad"
{
    Properties
    {
        _MainTex ("Shape", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _CodeShape ("Code shape (x: 0 texture, 1 square, 2 disc, 3 crosshair; y: rim)", Vector) = (0, 0, 0, 0)
        _MaskTex ("Mask", 2D) = "white" {}
        _MaskParams ("Mask (mode, strength, tile m, rotation rad)", Vector) = (0, 0, 1, 0)
        _MaskOrigin ("Mask Origin", Vector) = (0, 0, 0, 0)
        _MaskAxes ("Mask Axes (right xz, forward xz)", Vector) = (1, 0, 0, 1)
        _MaskExtent ("Mask Extent (width, length m)", Vector) = (1, 1, 0, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float4 _MaskParams;
                float4 _MaskOrigin;
                float4 _MaskAxes;
                float4 _MaskExtent;
                float4 _CodeShape;
            CBUFFER_END

            #define _BaseColor _Color
            #include "TelegraphMask.hlsl"

            // 예전 BossPatternVisuals.BuildSquare(64)·BuildDisc(128)·BuildCrosshair(128) 의 한 텍셀 알파(8비트).
            float CodeShapeSize() { return _CodeShape.x < 1.5 ? 64.0 : 128.0; }

            float CodeShapeTexel(float2 texel)
            {
                float size = CodeShapeSize(), rim = _CodeShape.y;
                float a;
                if (_CodeShape.x < 1.5)
                {
                    // 사각 — 테두리 rim(정규화) 안은 1, 안쪽 채움 0.55
                    float2 uv = (texel + 0.5) / size;
                    float2 e = min(uv, 1.0 - uv);
                    a = min(e.x, e.y) < rim ? 1.0 : 0.55;
                }
                else
                {
                    float halfSize = size * 0.5;
                    float2 off = texel + 0.5 - halfSize;
                    if (_CodeShape.x < 2.5)
                    {
                        // 원판 — 채움 0.55 + 테두리 1, 바깥 1px 페더
                        float d = sqrt(off.x * off.x + off.y * off.y) / halfSize;
                        a = d > 1.0 ? 0.0 : (d > 1.0 - rim ? 1.0 : 0.55);
                        a *= saturate((1.0 - d) * size * 0.5);
                    }
                    else
                    {
                        // 조준 — 원 테두리 + 안쪽으로 들어오는 네 눈금
                        float2 dxy = off / halfSize;
                        float d = sqrt(dxy.x * dxy.x + dxy.y * dxy.y);
                        bool onRing = abs(d - 0.86) < 0.07;
                        bool onTick = d > 0.55 && d < 1.0 && (abs(dxy.x) < 0.05 || abs(dxy.y) < 0.05);
                        a = onRing || onTick ? 1.0 : 0.0;
                    }
                }
                return round(saturate(a) * 255.0) / 255.0;   // Color → Color32
            }

            // 예전 텍스처의 이중선형 샘플(Clamp, 밉 없음).
            float CodeShapeAlpha(float2 uv)
            {
                float size = CodeShapeSize();
                float2 p = uv * size - 0.5;
                float2 i0 = floor(p), f = p - i0;
                float2 lo = clamp(i0, 0.0, size - 1.0), hi = clamp(i0 + 1.0, 0.0, size - 1.0);
                float a00 = CodeShapeTexel(lo), a10 = CodeShapeTexel(float2(hi.x, lo.y));
                float a01 = CodeShapeTexel(float2(lo.x, hi.y)), a11 = CodeShapeTexel(hi);
                return lerp(lerp(a00, a10, f.x), lerp(a01, a11, f.x), f.y);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float4 shape = _CodeShape.x < 0.5
                    ? SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color
                    : float4(1.0, 1.0, 1.0, CodeShapeAlpha(i.uv)) * i.color;
                float3 rgb; float a;
                TelegraphShade_float(shape, i.positionWS, rgb, a);
                return half4(rgb * a, a);   // 프리멀티(Sprites/Default 와 같은 블렌드)
            }
            ENDHLSL
        }
    }
}
