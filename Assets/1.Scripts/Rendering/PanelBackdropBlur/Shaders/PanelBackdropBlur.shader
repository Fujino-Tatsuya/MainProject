// ----------------------------------------------------------------------------
//  PanelBackdropBlur.shader — 패널 배경 블러(할 일 201)
//
//  MaskBlur(203, 맵 비네팅)와 같은 분리형 5탭 가우시안이지만 셰이더·머티리얼·전역 이름을 따로 둔다
//  (맵 비네팅 블러와 같은 프레임에 함께 돌아도 서로의 값을 덮지 않게).
//
//  패스 (Blitter 블릿):
//    0 PanelBlurH     — 가로 블러(첫 회는 다운샘플 겸)
//    1 PanelBlurV     — 세로 블러
//    2 PanelBlurComp  — 원본과 블러를 세기(_PanelBlurParams.x)로 섞고 채도·어둡게도 같은 세기로
// ----------------------------------------------------------------------------
Shader "Hidden/Rendering/PanelBackdropBlur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float4 _PanelBlurStep;     // xy = 이번 축의 UV 스텝(텍셀 크기 × 세기)
        float4 _PanelBlurParams;   // x = 세기 0~1, y = 채도(1 = 원색), z = 어둡게(0 = 그대로)

        TEXTURE2D_X(_PanelBlurTex); // 마지막 세로 패스가 SetGlobalTextureAfterPass 로 넘긴 블러 결과

        static const float kWeights[3] = { 0.375, 0.25, 0.0625 };

        half3 BlurAxis(float2 uv, float2 step)
        {
            half3 sum = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb * kWeights[0];

            [unroll]
            for (int i = 1; i < 3; i++)
            {
                float2 offset = step * i;
                sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset).rgb * kWeights[i];
                sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - offset).rgb * kWeights[i];
            }

            return sum;
        }
        ENDHLSL

        Pass
        {
            Name "PanelBlurH"
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(BlurAxis(input.texcoord, float2(_PanelBlurStep.x, 0.0)), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "PanelBlurV"
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(BlurAxis(input.texcoord, float2(0.0, _PanelBlurStep.y)), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "PanelBlurComp"
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float amount = saturate(_PanelBlurParams.x);

                half3 sharp = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                half3 blurred = SAMPLE_TEXTURE2D_X(_PanelBlurTex, sampler_LinearClamp, uv).rgb;
                half3 outColor = lerp(sharp, blurred, amount);

                half luma = dot(outColor, half3(0.2126, 0.7152, 0.0722));
                outColor = lerp(outColor, half3(luma, luma, luma), (1.0 - saturate(_PanelBlurParams.y)) * amount);
                outColor *= 1.0 - saturate(_PanelBlurParams.z) * amount;

                return half4(outColor, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
