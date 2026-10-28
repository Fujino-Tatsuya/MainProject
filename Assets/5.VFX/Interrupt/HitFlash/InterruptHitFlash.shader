// ----------------------------------------------------------------------------
//  InterruptHitFlash.shader — 간파 적중 월드 섬광 (할 일 132 단계 5 · §16)
//
//  파티클 전용 가산(Additive) 언릿. 모양은 외부 이미지 없이 절차 생성한다:
//    부드러운 빛 덩어리 = 가우시안 번짐(경계 없음) — 중심 빛·파란 번짐·불티가 같은 모양을 크기·색만 달리 쓴다.
//    (§16: 흰 바퀴살 광선·고리 모양은 없앴다 — 딱딱한 도형처럼 보였다)
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
        _Falloff ("빛 덩어리 가우시안 날카로움 (클수록 작은 코어)", Range(1, 16)) = 4
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_OverrideTex);
            SAMPLER(sampler_OverrideTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Falloff;
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

            // 가우시안 빛 덩어리 — 사각 가장자리 전에 0 으로 녹아 경계가 보이지 않는다.
            half BlobPattern(float d)
            {
                float glow = exp(-d * d * max(_Falloff, 1.0));
                return saturate(glow * (1.0 - smoothstep(0.6, 1.0, d)));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = (input.uv - 0.5) * 2.0;
                float d = length(p);

                half4 shape;
            #if defined(_OVERRIDE_TEX_ON)
                shape = SAMPLE_TEXTURE2D(_OverrideTex, sampler_OverrideTex, input.uv);
            #else
                shape = half4(1.0, 1.0, 1.0, BlobPattern(d));
            #endif

                half4 color = shape * _Color * input.color;
                return half4(color.rgb * color.a, color.a);
            }
            ENDHLSL
        }
    }

    CustomEditor "InterruptLightGatherShaderGUI"
}
