// 적 범위표시 C3 바닥 판(원·사각) — 할 일 57 셰이더 재개발 S1(PLAN-57-enemy-c3-shader §4.2).
// EnemyC3Visuals.Circle/Square 의 CPU 텍스처 대신 픽셀마다 SDF 로 그린다(EnemyC3Sdf.hlsl).
// 블렌드·ZWrite·Cull·_Color 는 Monster/TelegraphQuad 와 같다 — 같은 판 자리에 그대로 들어간다.
// 값은 전부 EnemyC3Visuals 가 MaterialPropertyBlock 으로 넣는다(재질 한 장 공유).
Shader "Monster/EnemyC3Plate"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _C3Mode ("Mode (square, fill layer, fill under, markers)", Vector) = (0, 0, 0, 0)
        _C3Size ("Size (core half, sample extent, marker radius %, -)", Vector) = (165, 337, 81, 0)
        _C3Geom ("Geometry (outer, inner, gap, gradient start)", Vector) = (3.5, 2.5, 2, 57)
        _C3Outline ("Outline (gamma)", Vector) = (1, 1, 1, 0.95)
        _C3InnerLine ("Inner line (gamma)", Vector) = (1, 1, 1, 1)
        _C3Band ("Band (gamma)", Vector) = (1, 1, 1, 0.25)
        _C3Fill ("Fill (gamma)", Vector) = (1, 1, 1, 0.232)
        _C3Markers ("Markers (gamma)", Vector) = (1, 1, 1, 0.42)
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
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _C3Mode;
                float4 _C3Size;
                float4 _C3Geom;
                float4 _C3Outline;
                float4 _C3InnerLine;
                float4 _C3Band;
                float4 _C3Fill;
                float4 _C3Markers;
            CBUFFER_END

            #include "EnemyC3Sdf.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // 판 UV(0~1) → 디자인 단위. 바깥 외곽선 폭만큼 넓힌 sampleExtent 가 판 전체다(Bake 와 같음).
                float2 p = (i.uv - 0.5) * _C3Size.y;
                float aa = EnemyC3Aa(p);
                bool square = _C3Mode.x > 0.5;
                float coreHalf = _C3Size.x;
                float d = square ? EnemyC3Box(p, float2(coreHalf, coreHalf)) : EnemyC3CircleSdf(p, coreHalf);

                float4 c;
                if (_C3Mode.y > 0.5) c = EnemyC3Fill(d, aa, _C3Fill);
                else
                {
                    EnemyC3Palette pal;
                    pal.outline = _C3Outline; pal.innerLine = _C3InnerLine; pal.band = _C3Band;
                    pal.fill = _C3Fill; pal.markers = _C3Markers;
                    // 판은 unit = 1(디자인 단위 그대로).
                    float band = square ? EnemyC3BandLinear(d, 1.0, _C3Geom.w) : EnemyC3BandCircle(p, coreHalf, _C3Geom.w);
                    float marker = _C3Mode.w > 0.5 ? EnemyC3CircleFins(p, coreHalf, _C3Size.z, aa) : 0.0;
                    c = EnemyC3Boundary(d, aa, 1.0, band, marker, _C3Mode.z > 0.5,
                                        _C3Geom.x, _C3Geom.y, _C3Geom.z, pal);
                }

                float3 rgb = EnemyC3ToOutput(c.rgb) * _Color.rgb;
                float a = c.a * _Color.a;
                return half4(rgb * a, a);   // 프리멀티(TelegraphQuad 와 같은 블렌드)
            }
            ENDHLSL
        }
    }
}
