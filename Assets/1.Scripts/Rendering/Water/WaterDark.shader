// ----------------------------------------------------------------------------
//  WaterDark.shader - 어비스 불투명 어두운 물 (Unlit 1패스)
//
//  방식 확정(2026-07, FOG_NEXT_PLAN.md Task 2):
//  - Opaque + ZWrite On + Unlit → 물 안쪽/바닥 절대 안 보임, 쿼드 1장 드로우콜 +1
//  - 물결 = 절차 밸류노이즈 2겹(서로 다른 scale·스크롤) → _FlowColor 얇게 lerp
//  - fake 깊이 = 저주파 노이즈 명암(정적) + (per-hole 쿼드용) UV 가장자리 밝힘
//  - 금지: 실시간 반사/굴절/투명 블렌딩/버텍스 웨이브 (프레임 예산)
//  - Geometry 큐에서 depth를 쓰므로 FullScreenFog의 FoW/LoS 디밍이 물 위에도 정상 적용
// ----------------------------------------------------------------------------
Shader "Custom/WaterDark"
{
    Properties
    {
        _DeepColor    ("Deep Color (심연색)", Color) = (0.02, 0.04, 0.08, 1)
        _FlowColor    ("Flow Color (물결색)", Color) = (0.10, 0.18, 0.26, 1)
        _FlowSpeed1   ("Flow Speed 1 (xy=월드 xz/s)", Vector) = (0.6, 0.25, 0, 0)
        _FlowSpeed2   ("Flow Speed 2 (xy=월드 xz/s)", Vector) = (-0.4, 0.5, 0, 0)
        _FlowScale1   ("Flow Scale 1 (노이즈 UV 스케일)", Float) = 0.15
        _FlowScale2   ("Flow Scale 2", Float) = 0.33
        _FlowStrength ("Flow Strength (물결 세기)", Range(0, 1)) = 0.4
        _FlowSharp    ("Flow Sharpness (물결 얇기)", Range(1, 8)) = 3
        _DepthNoiseScale ("Fake Depth Noise Scale (저주파)", Float) = 0.02
        _DepthDarken  ("Fake Depth Darken (깊이 명암)", Range(0, 1)) = 0.35
        _EdgeBrighten ("Edge Brighten (UV 가장자리, per-hole 쿼드용)", Range(0, 1)) = 0
        _EdgeWidth    ("Edge Width (UV 비율)", Range(0.01, 0.5)) = 0.08
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "WaterDarkUnlit"
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor;
                float4 _FlowColor;
                float4 _FlowSpeed1;
                float4 _FlowSpeed2;
                float  _FlowScale1;
                float  _FlowScale2;
                float  _FlowStrength;
                float  _FlowSharp;
                float  _DepthNoiseScale;
                float  _DepthDarken;
                float  _EdgeBrighten;
                float  _EdgeWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float2 uv          : TEXCOORD1;
            };

            // FogCore.hlsl 의 Fog_Hash21/Fog_ValueNoise 와 동일 패턴(로컬 복제 —
            // FogCore 는 전역 프로퍼티 선언이 많아 include 하지 않는다).
            float Water_Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Water_ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = Water_Hash21(i);
                float b = Water_Hash21(i + float2(1, 0));
                float c = Water_Hash21(i + float2(0, 1));
                float d = Water_Hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = IN.positionWS.xz;

                // 물결: 노이즈 2겹(서로 다른 스케일·방향 스크롤). 두 겹이 겹치는
                // 곳만 얇게 밝아지도록 곱 + 샤프닝 → 흐르는 줄기 느낌.
                float n1 = Water_ValueNoise(p * _FlowScale1 + _FlowSpeed1.xy * _Time.y * _FlowScale1);
                float n2 = Water_ValueNoise(p * _FlowScale2 + _FlowSpeed2.xy * _Time.y * _FlowScale2);
                float flow = pow(saturate(n1 * n2 * 2.2), _FlowSharp);

                float3 col = lerp(_DeepColor.rgb, _FlowColor.rgb, saturate(flow * _FlowStrength));

                // fake 깊이감: 정적 저주파 노이즈로 미묘한 명암 — 평평한 단색 티 제거.
                float dn = Water_ValueNoise(p * _DepthNoiseScale);
                col *= lerp(1.0, 1.0 - _DepthDarken, dn);

                // UV 가장자리 밝힘 — 구멍에 딱 맞춘 쿼드에서만 의미 있음(메가 플레인은 0 유지).
                float2 e = min(IN.uv, 1.0 - IN.uv);
                float rim = 1.0 - saturate(min(e.x, e.y) / max(1e-4, _EdgeWidth));
                col = lerp(col, col + _FlowColor.rgb, rim * rim * _EdgeBrighten);

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
