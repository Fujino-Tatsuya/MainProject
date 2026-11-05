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
//                       + 218 후속 TV 글리치(_PanelGlitch0.x > 0 일 때만 — 가로 찢김·RGB 어긋남·주사선·잡음, 절차적·텍스처 없음)
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
        float4 _PanelGlitch0;      // x = 글리치 세기(× 블러 세기), y = 시간(unscaled), z = 주사선 줄 수(화면 높이 기준), w = 찢김 띠 비율 최대
        float4 _PanelGlitch1;      // x = 주사선 어둡기, y = RGB 어긋남(UV), z = 찢김 최대 폭(UV), w = 잡음 세기

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

            // 원본·블러를 세기로 섞고 채도·어둡게(201 그대로).
            half3 Compose(float2 uv, float amount)
            {
                half3 sharp = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                half3 blurred = SAMPLE_TEXTURE2D_X(_PanelBlurTex, sampler_LinearClamp, uv).rgb;
                half3 outColor = lerp(sharp, blurred, amount);

                half luma = dot(outColor, half3(0.2126, 0.7152, 0.0722));
                outColor = lerp(outColor, half3(luma, luma, luma), (1.0 - saturate(_PanelBlurParams.y)) * amount);
                outColor *= 1.0 - saturate(_PanelBlurParams.z) * amount;
                return outColor;
            }

            // 0~1 해시(텍스처 없는 잡음). 입력은 수백 이하로 유지한다 — 크면 float 정밀도로 무늬가 생긴다.
            float GlitchHash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float amount = saturate(_PanelBlurParams.x);

                float g = saturate(_PanelGlitch0.x);
                if (g <= 0.0)
                    return half4(Compose(uv, amount), 1.0);

                // ── 218 후속 TV 글리치 ── 찢김·떨림은 초당 15번만 바뀐다(아날로그 끊김), 잡음은 초당 30번.
                float tick = floor(_PanelGlitch0.y * 15.0);
                float seed = frac(tick * 0.61803) * 97.0;

                // 가로 띠 찢김: 띠 개수도 매 틱 바뀌고, 띠마다 (찢김 비율 × 세기³) 확률로 — 상시(약한 세기)엔 드물게, 버스트엔 많이 옆으로 밀린다.
                float bands = lerp(12.0, 40.0, GlitchHash(float2(seed, 7.1)));
                float band = floor(uv.y * bands);
                float tearOn = step(GlitchHash(float2(band, seed)), _PanelGlitch0.w * g * g * g);
                float shift = (GlitchHash(float2(band + 17.0, seed)) * 2.0 - 1.0) * _PanelGlitch1.z * g * tearOn;

                // 줄마다 미세 떨림(주사선 단위).
                float row = floor(uv.y * _PanelGlitch0.z);
                shift += (GlitchHash(float2(row * 0.37, seed + 3.0)) - 0.5) * 0.002 * g;

                float2 guv = float2(uv.x + shift, uv.y);

                // RGB 어긋남 — 채도·흑백 처리 뒤 채널을 따로 뽑아 흑백 화면 위에도 빨강·파랑 테두리가 남는다. 찢긴 띠는 두 배.
                float split = _PanelGlitch1.y * g * (1.0 + tearOn);
                half3 outColor;
                outColor.r = Compose(guv + float2(split, 0.0), amount).r;
                outColor.g = Compose(guv, amount).g;
                outColor.b = Compose(guv - float2(split, 0.0), amount).b;

                // 주사선(가로 줄).
                half scan = 0.5 + 0.5 * cos(uv.y * _PanelGlitch0.z * 6.2831853);
                outColor *= 1.0 - saturate(_PanelGlitch1.x) * g * scan;

                // 잡음(흑백 알갱이, 화면 픽셀 단위).
                float noiseSeed = frac(floor(_PanelGlitch0.y * 30.0) * 0.61803) * 251.0;
                float n = GlitchHash(input.positionCS.xy * 0.1 + noiseSeed);
                outColor += (n - 0.5) * saturate(_PanelGlitch1.w) * g;

                return half4(max(outColor, 0.0), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
