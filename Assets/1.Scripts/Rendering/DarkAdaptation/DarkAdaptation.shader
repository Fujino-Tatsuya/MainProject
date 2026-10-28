// ----------------------------------------------------------------------------
//  DarkAdaptation.shader — 간파 성공 암순응 (할 일 87 C3)
//
//  휘도 임계보다 어두운 픽셀만 낮추고 밝은 픽셀(빛 이펙트·발광)은 남긴다.
//  후처리 전(HDR)에 돌므로 임계는 HDR 휘도 기준이고, 남긴 픽셀에서 Bloom 이 그대로 나온다.
//  시전자 화면 좌표 주변은 원형으로 살린다(반경 0 = 원형 끔). 화면비는 _DarkAdaptAspect 로 보정한다.
//  패스 1(MaskCover) = 오너 마스크 암전 덮개 알파(할 일 132).
//  패스 2(HitFlashRadialBlur) = 적중 섬광 방사형 블러(할 일 132 §15).
// ----------------------------------------------------------------------------
Shader "Hidden/Rendering/DarkAdaptation"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "DarkAdaptation"
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _DarkAdaptParams;   // x=강도(0~1), y=휘도 임계, z=임계 부드러움, w=최대 어둡힘 비율
            float4 _DarkAdaptReveal;   // xy=원형 중심(뷰포트 0~1), z=반경(화면 높이 비율, 0=끔), w=가장자리 부드러움
            float _DarkAdaptAspect;    // 화면 가로/세로

            // 1 = 원 안(살림), 0 = 원 밖.
            float RevealAmount(float2 uv)
            {
                float radius = _DarkAdaptReveal.z;
                if (radius <= 0.0)
                    return 0.0;

                float2 delta = uv - _DarkAdaptReveal.xy;
                delta.x *= max(_DarkAdaptAspect, 1e-4);   // 화면 높이 단위로 맞춰 원이 찌그러지지 않게
                float dist = length(delta);
                return 1.0 - smoothstep(radius, radius + max(_DarkAdaptReveal.w, 1e-4), dist);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);

                // 임계 이상은 그대로, 임계 − 부드러움 아래는 완전히 어둡힘 대상.
                float luma = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));
                float threshold = _DarkAdaptParams.y;
                float keep = smoothstep(threshold - max(_DarkAdaptParams.z, 1e-4), threshold, luma);

                float darken = _DarkAdaptParams.x * _DarkAdaptParams.w * (1.0 - keep) * (1.0 - RevealAmount(uv));
                color.rgb *= 1.0 - darken;
                return color;
            }
            ENDHLSL
        }

        // 패스 1 — 마스크 암전 덮개(할 일 132). 입력 = 비트 4 렌더러만 다시 그린 남김 레이어(투명 검정 위).
        // 출력 = 흰색 + 알파(1 − 남김). HUD 위 최상위 오버레이 캔버스가 이 알파로 칸 색을 깐다 — 남길 픽셀만 뚫린다.
        Pass
        {
            Name "MaskCover"
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCover

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 FragCover(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 kept = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);

                // 불투명은 알파 1, 가산 입자는 알파가 낮아도 색이 남는다 — 둘 중 큰 쪽을 남김으로 본다.
                half keep = saturate(max(kept.a, max(kept.r, max(kept.g, kept.b))));
                return half4(1.0, 1.0, 1.0, 1.0 - keep);
            }
            ENDHLSL
        }

        // 패스 2 — 적중 섬광 방사형 블러(할 일 132 §15). 중심(적중 지점) 쪽으로 샘플을 겹쳐 읽어 바깥으로 갈수록 길게 번진다.
        // 후처리 전(HDR)이라 번진 밝은 픽셀에서 Bloom 이 그대로 나온다.
        Pass
        {
            Name "HitFlashRadialBlur"
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlur

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _HitFlashBlur;   // xy=중심(뷰포트 0~1), z=세기(중심까지 거리 중 샘플이 덮는 비율 [0, 1])

            #define BLUR_SAMPLES 12

            half4 FragBlur(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 toCenter = _HitFlashBlur.xy - uv;
                float strength = saturate(_HitFlashBlur.z);

                half4 sum = 0;
                [unroll]
                for (int i = 0; i < BLUR_SAMPLES; i++)
                {
                    float k = (float)i / (BLUR_SAMPLES - 1);
                    sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + toCenter * (k * strength));
                }

                return sum / BLUR_SAMPLES;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
