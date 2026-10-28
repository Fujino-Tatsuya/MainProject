// ----------------------------------------------------------------------------
//  InterruptLightGather.shader — 간파 빛 모임 (할 일 132 단계 4)
//
//  파티클 전용 가산(Additive) 언릿. 모양은 외부 이미지 없이 절차 생성한다:
//    Pillar = 흰 빛 기둥(가운데 심 + 넓은 번짐 + 위로 오르는 줄기 3개, 위·아래 부드럽게 사라짐)
//    Mote   = 둥근 빛 알갱이(뜨거운 가운데 + 부드러운 번짐) — 늘인 빌보드면 빛줄기가 된다
//  '덮어쓸 텍스처' 칸을 채우면 절차 생성 대신 그 텍스처(RGBA)를 쓴다(손그림 교체용).
//  키워드 _OVERRIDE_TEX_ON 은 셰이더 GUI 가 칸에 맞춰 켠다(InterruptLightGatherRules.ApplyOverrideKeyword).
//
//  시간은 _Time 을 쓰지 않는다 — 파티클 나이(AgePercent)로 움직여 파티클 시간 기준(실시간/스케일)을 그대로 따른다.
//  정점 스트림: Position, Color, UV(TEXCOORD0.xy), AgePercent(TEXCOORD0.z), StableRandom.x(TEXCOORD0.w).
//  색은 HDR — 1 을 넘는 값에서 블룸이 먹는다.
// ----------------------------------------------------------------------------
Shader "BeaverLobby/VFX/InterruptLightGather"
{
    Properties
    {
        [HDR] _Color ("색 (HDR — 1 넘으면 블룸)", Color) = (6, 6, 6, 1)
        [NoScaleOffset] _OverrideTex ("덮어쓸 텍스처 (비우면 절차 생성)", 2D) = "black" {}
        [KeywordEnum(Pillar, Mote)] _Pattern ("절차 생성 모양", Float) = 0
        _CoreWidth ("기둥 심 폭 (Pillar)", Range(0.02, 0.6)) = 0.14
        _StreakSpeed ("줄기 오르는 빠르기 (Pillar, 수명당)", Range(0, 6)) = 2.5
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
            #pragma shader_feature_local_fragment _PATTERN_PILLAR _PATTERN_MOTE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_OverrideTex);
            SAMPLER(sampler_OverrideTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _CoreWidth;
                half _StreakSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float4 uv : TEXCOORD0;   // xy = UV, z = AgePercent, w = StableRandom.x
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float4 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            // 흰 빛 기둥 — u 가로(0~1), v 세로(0 아래 ~ 1 위).
            half PillarPattern(float2 uv, float age, float rnd)
            {
                float x = abs(uv.x - 0.5) * 2.0;                      // 0 = 가운데, 1 = 가장자리
                float core = exp(-(x * x) / max(_CoreWidth * _CoreWidth, 1e-4));
                float glow = pow(saturate(1.0 - x), 3.0) * 0.35;

                float streaks = 0.0;
                [unroll]
                for (int k = 0; k < 3; k++)
                {
                    float seed = frac(rnd * (7.31 + k * 3.17) + k * 0.37);
                    float lineX = 0.25 + 0.5 * seed;
                    float across = exp(-pow((uv.x - lineX) * 28.0, 2.0));
                    float along = frac(uv.y * 1.5 - age * _StreakSpeed - seed);
                    streaks += across * smoothstep(0.0, 0.2, along) * (1.0 - smoothstep(0.3, 0.6, along));
                }

                float vertical = smoothstep(0.0, 0.12, uv.y) * (1.0 - smoothstep(0.45, 1.0, uv.y));
                return saturate((core + glow + streaks * 0.6) * vertical);
            }

            // 둥근 빛 알갱이.
            half MotePattern(float2 uv)
            {
                float d = length(uv - 0.5) * 2.0;
                float soft = pow(saturate(1.0 - d), 2.5);
                float hot = exp(-d * d * 40.0) * 0.8;
                return saturate(soft + hot);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 shape;
            #if defined(_OVERRIDE_TEX_ON)
                shape = SAMPLE_TEXTURE2D(_OverrideTex, sampler_OverrideTex, input.uv.xy);
            #elif defined(_PATTERN_MOTE)
                shape = half4(1.0, 1.0, 1.0, MotePattern(input.uv.xy));
            #else
                shape = half4(1.0, 1.0, 1.0, PillarPattern(input.uv.xy, input.uv.z, input.uv.w));
            #endif

                half4 color = shape * _Color * input.color;
                return half4(color.rgb * color.a, color.a);
            }
            ENDHLSL
        }
    }

    CustomEditor "InterruptLightGatherShaderGUI"
}
