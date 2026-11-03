// ----------------------------------------------------------------------------
//  InterruptHitLightRays.shader — 간파 적중 빛줄기 (할 일 194 단계 5 · Q7)
//
//  파티클 메시 모드 전용 가산(Additive) 언릿. 메시 = 끝이 가늘어지는 날개 8~12개(Mesh_InterruptHitLight_Rays, 저작 메뉴가 에셋으로 저장).
//  외부 이미지 없이 절차 생성한다:
//    UV.x = 날개 번호 + 가로 진행(0~1) — frac = 날개 가로(가장자리 0 · 중심 1), floor = 날개마다 다른 노이즈 씨앗
//    UV.y = 중심 0 → 끝 1
//    흰 중심 + 주황 가장자리, 부드러운 가장자리, 길이 방향 노이즈(날개마다 다르게), 끝으로 갈수록 사라짐.
//  길이·알파의 시간 변화는 파티클 크기·색 수명 곡선이 맡는다(셰이더는 시간을 쓰지 않는다 — 실시간 파티클과 맞는다).
//  색은 HDR — 1 을 넘는 값에서 블룸이 먹는다. 정점 스트림: Position, Color, UV.
// ----------------------------------------------------------------------------
Shader "BeaverLobby/VFX/InterruptHitLightRays"
{
    Properties
    {
        [HDR] _CoreColor ("중심 색 (HDR)", Color) = (6, 5.6, 5, 1)
        [HDR] _EdgeColor ("가장자리 색 (HDR)", Color) = (3, 1.2, 0.3, 1)
        _EdgeSoftness ("가장자리 부드러움 (0~1, 클수록 넓게 번짐)", Range(0.01, 1)) = 0.6
        _CoreSharpness ("중심 날카로움 (클수록 흰 심이 가늘다)", Range(1, 16)) = 5
        _NoiseFreq ("길이 방향 노이즈 촘촘함", Range(0, 40)) = 14
        _NoiseAmount ("노이즈 세기 (0 = 없음)", Range(0, 1)) = 0.35
        _TipFade ("끝 사라짐 시작 (UV.y)", Range(0, 1)) = 0.45
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _EdgeColor;
                half _EdgeSoftness;
                half _CoreSharpness;
                half _NoiseFreq;
                half _NoiseAmount;
                half _TipFade;
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

            float Hash(float n)
            {
                return frac(sin(n * 12.9898) * 43758.5453);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float across = frac(input.uv.x);
                float seed = floor(input.uv.x);
                float along = saturate(input.uv.y);

                float edge = 1.0 - abs(across * 2.0 - 1.0);                // 가장자리 0 · 중심 1
                float soft = smoothstep(0.0, max(_EdgeSoftness, 1e-3), edge);
                float core = pow(edge, max(_CoreSharpness, 1.0)) * (1.0 - along * 0.7);

                float lengthFade = (1.0 - smoothstep(_TipFade, 1.0, along)) * smoothstep(0.0, 0.06, along);
                float wave = sin(along * _NoiseFreq + Hash(seed) * 6.2831) * 0.5 + 0.5;
                float noise = 1.0 - _NoiseAmount * wave;

                half3 color = lerp(_EdgeColor.rgb, _CoreColor.rgb, saturate(core));
                half alpha = soft * lengthFade * noise * input.color.a;
                return half4(color * input.color.rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
