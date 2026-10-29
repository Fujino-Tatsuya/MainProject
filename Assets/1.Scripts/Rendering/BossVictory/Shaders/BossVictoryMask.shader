// ----------------------------------------------------------------------------
//  BossVictoryMask.shader — 보스 B 승리 연출(PLAN-boss-victory-cinematic §5.3) 인물·보스 마스크
//
//  막타·팀원·보스 렌더러를 overrideMaterial 로 다시 그려
//    · 색 = 소속 ID(R 막타 / G 팀원 / B 보스, A 커버리지) → 합성 패스가 대상별 회색 램프를 고른다
//    · 스텐실 bit 7(128) = 인물이 보이는 픽셀 → 타이포(TMP)가 이 비트가 없는 곳에만 그려져 "글자가 인물 뒤"가 된다
//
//  🔴 "실제로 보이는 표면만"(Codex C9)을 하드웨어 깊이(ZTest LEqual + Offset)로 고르던 1판은, 같은 메시를 다시 그려도 깊이가
//     미세하게 어긋나는 곳(패널 이음새·접힌 면)에서 픽셀이 빠져 배경(주황)으로 분류됐다 → 보스 위에 주황 점선이 생겼다(경석 10-27).
//     → 깊이 텍스처와 선형 거리로 비교하고 여유(_OccludeBias m)를 준다(PlayerSilhouetteMask 와 같은 방식). 하드웨어 깊이는 안 쓴다.
//  이 셰이더는 overrideMaterial 로만 쓰인다.
// ----------------------------------------------------------------------------
Shader "Hidden/Rendering/BossVictoryMask"
{
    Properties
    {
        _VictoryId ("Victory Id (R killer, G teammate, B boss)", Color) = (1, 0, 0, 1)
        _OccludeBias ("Occlude Bias (m)", Float) = 0.08
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "BossVictoryMask"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            ZTest Always
            Cull Back
            Blend Off

            Stencil
            {
                Ref 128
                WriteMask 128
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _VictoryId;
                float _OccludeBias;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                // reversed-Z 차이를 피하려고 둘 다 선형 거리(m)로 바꿔 비교한다.
                float sceneEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float selfEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                // 앞에 다른 것이 있으면(가려짐) 버린다 — 버린 픽셀은 스텐실도 안 찍힌다.
                clip(sceneEye + _OccludeBias - selfEye);
                return half4(_VictoryId.rgb, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
