// ----------------------------------------------------------------------------
//  DarkAdaptation.shader — 간파 성공 암순응 (할 일 87 C3)
//
//  휘도 임계보다 어두운 픽셀만 낮추고 밝은 픽셀(빛 이펙트·발광)은 남긴다.
//  후처리 전(HDR)에 돌므로 임계는 HDR 휘도 기준이고, 남긴 픽셀에서 Bloom 이 그대로 나온다.
//  시전자 화면 좌표 주변은 원형으로 살린다(반경 0 = 원형 끔). 화면비는 _DarkAdaptAspect 로 보정한다.
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
    }

    Fallback Off
}
