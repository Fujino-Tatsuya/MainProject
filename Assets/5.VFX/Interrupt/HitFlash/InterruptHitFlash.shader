// ----------------------------------------------------------------------------
//  InterruptHitFlash.shader — 간파 적중 섬광 4단계 월드 이펙트 (할 일 132 단계 5)
//
//  파티클 전용 가산(Additive) 언릿. 모양은 외부 이미지 없이 절차 생성한다:
//    Core = 뜨거운 중심 + 부드러운 번짐(중심 빛·파란 번짐·불티)
//    Rays = 스타버스트 광선(갈래 수 칸) + 중심 — 파티클 회전으로 갈래 방향이 바뀐다
//    Ring = 얇은 원형 충격파 링 — 크기 곡선으로 퍼진다
//  '덮어쓸 텍스처' 칸을 채우면 절차 생성 대신 그 텍스처(RGBA)를 쓴다(손그림 교체용).
//  키워드 _OVERRIDE_TEX_ON 은 셰이더 GUI 가 칸에 맞춰 켠다(InterruptLightGatherRules.ApplyOverrideKeyword — 빛 모임과 같은 칸 규칙).
//  색은 HDR — 1 을 넘는 값에서 블룸이 먹는다. 정점 스트림: Position, Color, UV.
// ----------------------------------------------------------------------------
Shader "BeaverLobby/VFX/InterruptHitFlash"
{
    Properties
    {
        [HDR] _Color ("색 (HDR — 1 넘으면 블룸)", Color) = (6, 6, 6, 1)
        [NoScaleOffset] _OverrideTex ("덮어쓸 텍스처 (비우면 절차 생성)", 2D) = "black" {}
        [KeywordEnum(Core, Rays, Ring)] _Pattern ("절차 생성 모양", Float) = 0
        _RayCount ("광선 갈래 수 (Rays)", Range(4, 24)) = 8
        _RingWidth ("링 두께 (Ring, 반지름 대비)", Range(0.01, 0.3)) = 0.06
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _ _OVERRIDE_TEX_ON
            #pragma shader_feature_local_fragment _PATTERN_CORE _PATTERN_RAYS _PATTERN_RING

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_OverrideTex);
            SAMPLER(sampler_OverrideTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _RayCount;
                half _RingWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            // 뜨거운 중심 + 부드러운 번짐.
            half CorePattern(float d)
            {
                float soft = pow(saturate(1.0 - d), 2.2);
                float hot = exp(-d * d * 30.0);
                return saturate(soft * 0.6 + hot);
            }

            // 스타버스트 — 굵은 광선 + 그 사이 가는 광선, 바깥으로 갈수록 가늘고 흐려진다.
            half RaysPattern(float2 p, float d)
            {
                float a = atan2(p.y, p.x);
                float n = max(_RayCount, 1.0);
                float mainRays = pow(abs(cos(a * n * 0.5)), 60.0);
                float thinRays = pow(abs(cos(a * n + 0.8)), 180.0) * 0.5;
                float fade = pow(saturate(1.0 - d), 1.5);
                float core = exp(-d * d * 60.0);
                return saturate((mainRays + thinRays) * fade + core);
            }

            // 얇은 링 — 반지름 0.85(사각 안쪽), 두께 칸.
            half RingPattern(float d)
            {
                float w = max(_RingWidth, 1e-3);
                return exp(-pow((d - 0.85) / w, 2.0));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = (input.uv - 0.5) * 2.0;
                float d = length(p);

                half4 shape;
            #if defined(_OVERRIDE_TEX_ON)
                shape = SAMPLE_TEXTURE2D(_OverrideTex, sampler_OverrideTex, input.uv);
            #elif defined(_PATTERN_RAYS)
                shape = half4(1.0, 1.0, 1.0, RaysPattern(p, d));
            #elif defined(_PATTERN_RING)
                shape = half4(1.0, 1.0, 1.0, RingPattern(d));
            #else
                shape = half4(1.0, 1.0, 1.0, CorePattern(d));
            #endif

                half4 color = shape * _Color * input.color;
                return half4(color.rgb * color.a, color.a);
            }
            ENDHLSL
        }
    }

    CustomEditor "InterruptLightGatherShaderGUI"
}
