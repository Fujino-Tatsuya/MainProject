// ----------------------------------------------------------------------------
//  InterruptHitLightRaysScreen.shader — 간파 적중 빛줄기, 화면(렌즈) 공간용 (할 일 194 후속 V3)
//
//  InterruptHitLightRays.shader(V1·V2 공용 — 건드리지 않는다)의 V3 전용 복제본. 그림은 같고 다른 점만:
//    · ZTest Always + Overlay 큐 — 바닥·캐릭터에 가려지거나 파고들지 않고 맨 위에 그린다(렌즈 플레어처럼).
//    · _Intensity — 밝기 배율. 연출이 Look 칸(rayScreenBrightness)을 MaterialPropertyBlock 으로 넣는다.
//  배치(화면 점·카메라 정면·화면 비율 크기)는 InterruptHitLightFx 가 파티클 시스템 자체를 카메라 근평면 앞에 놓아 맡는다.
//  정점 스트림: Position, Color, UV.
// ----------------------------------------------------------------------------
Shader "BeaverLobby/VFX/InterruptHitLightRaysScreen"
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
        _Intensity ("밝기 배율 (연출이 Look 값으로 덮어쓴다)", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Overlay"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest Always
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
                half _Intensity;
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
                return half4(color * input.color.rgb * alpha * max(_Intensity, 0.0), alpha);
            }
            ENDHLSL
        }
    }
}
