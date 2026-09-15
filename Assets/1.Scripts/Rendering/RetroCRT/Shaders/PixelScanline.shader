// 월드 화면 전체 픽셀레이트 + 화면 V축 스캔라인.
// AfterRenderingPostProcessing에서 실행되고 Screen Space Overlay UI는 그 뒤에 그려진다.
Shader "Hidden/Rendering/PixelScanline"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "PixelScanline"
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _PixelScanlineResolution; // xy=렌더 픽셀 해상도, zw=역수
            float4 _PixelScanlinePixel;      // x=블록 px, y=활성
            float4 _PixelScanlinePattern;    // x=두께 px, y=간격 px, z=활성, w=불투명도
            half4 _PixelScanlineColor;       // rgb만 사용. 강도는 Pattern.w

            float2 ResolvePixelatedUV(float2 uv)
            {
                if (_PixelScanlinePixel.y < 0.5)
                    return uv;

                float2 resolution = max(_PixelScanlineResolution.xy, 1.0);
                float blockSize = max(_PixelScanlinePixel.x, 1.0);
                float2 pixel = min(uv * resolution, resolution - 1e-4);
                float2 blockStart = floor(pixel / blockSize) * blockSize;

                // 화면 끝의 나머지 블록도 남은 실제 폭의 중심에서 읽는다.
                float2 blockExtent = min(blockSize.xx, resolution - blockStart);
                float2 samplePixel = blockStart + blockExtent * 0.5;
                return samplePixel / resolution;
            }

            half ScanlineMask(float2 uv)
            {
                if (_PixelScanlinePattern.z < 0.5 || _PixelScanlinePattern.w <= 0.0)
                    return 0.0h;

                float height = max(_PixelScanlineResolution.y, 1.0);
                float thickness = max(_PixelScanlinePattern.x, 1.0);
                float spacing = max(_PixelScanlinePattern.y, 0.0);
                float period = max(thickness + spacing, 1.0);

                // x는 전혀 사용하지 않는다. 동일한 V 픽셀 행 전체가 하나의 가로줄이다.
                float pixelY = min(floor(uv.y * height), height - 1.0);
                float phase = fmod(pixelY, period);
                return (half)(1.0 - step(thickness, phase));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float2 sampleUV = ResolvePixelatedUV(uv);
                half4 source = SAMPLE_TEXTURE2D_X(
                    _BlitTexture,
                    sampler_LinearClamp,
                    sampleUV);

                half lineAmount =
                    ScanlineMask(uv) * saturate((half)_PixelScanlinePattern.w);
                source.rgb = lerp(source.rgb, _PixelScanlineColor.rgb, lineAmount);
                return source;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
