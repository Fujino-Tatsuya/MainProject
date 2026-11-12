// ----------------------------------------------------------------------------
//  BossVictoryGlitchMask.shader — 보스 처치 연출 ZZZ 글리치(PLAN-boss-victory-glitch 3단계) 플레이어·보스 마스크
//  플레이어(흑백 대상)·보스(노이즈 대상) 렌더러를 overrideMaterial 로 다시 그려 색 = 소속 ID(R 플레이어 / B 보스, A 커버리지).
//  G = 보스 프레넬(1 − saturate(N·V), 0 정면 · 1 가장자리) — 보스 표현 '림라이트'(11-12)가 합성에서 읽는다. 플레이어(B 0)는 0.
//  "보이는 표면만" = 깊이 텍스처와 선형 거리 비교 + 여유(_OccludeBias m) — 옛 BossVictoryMask 와 같은 방식(하드웨어 깊이는 이음새가 빠졌다).
// ----------------------------------------------------------------------------
Shader "Hidden/Rendering/BossVictoryGlitchMask"
{
    Properties
    {
        _GlitchId ("Glitch Id (R player, B boss)", Color) = (1, 0, 0, 1)
        _OccludeBias ("Occlude Bias (m)", Float) = 0.08
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "BossVictoryGlitchMask"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest Always
            Cull Back
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GlitchId;
                float _OccludeBias;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float selfEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                clip(sceneEye + _OccludeBias - selfEye);   // 앞에 다른 것이 있으면 버린다
                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half fresnel = (half)(1.0 - saturate(dot(n, v)));
                return half4(_GlitchId.r, fresnel * _GlitchId.b, _GlitchId.b, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
