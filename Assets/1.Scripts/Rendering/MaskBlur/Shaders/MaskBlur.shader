// ----------------------------------------------------------------------------
//  MaskBlur.shader — 화면공간 마스크 블러
//
//  왜 DoF 가 아니라 이것인가:
//  탑다운 쿼터뷰에서 좌우 화면 외곽은 중앙과 깊이가 거의 같다. 깊이 기반 DoF 는
//  화면 위치를 모르므로 좌우를 흐리게 할 수 없다("가까운 아래 / 먼 위"만 흐려진다).
//  선명 영역을 화면 좌표로 직접 지정해야 원하는 그림이 나온다.
//
//  패스 구성 (Blitter 로 3회 블릿):
//    0 MaskBlurDownH  — 다운샘플 + 가로 블러
//    1 MaskBlurV      — 세로 블러 (분리형 가우시안의 두 번째 축)
//    2 MaskBlurComp   — 원본과 블러를 마스크로 합성 + 바깥 영역 탈채도/암부 + 픽셀레이트
//
//  픽셀레이트를 왜 별도 패스로 두지 않는가 (2026-08-06):
//  합성 패스에서 블러 텍스처를 양자화한 UV 로 샘플하면 그것으로 끝난다.
//    - "블러 → 픽셀레이트" 순서가 자동으로 성립한다(이미 블러된 텍스처를 블록 샘플하므로).
//    - "마스크 바깥만"도 자동으로 성립한다(블러 텍스처는 amount 로만 섞여 중앙엔 안 온다).
//    - 패스가 안 늘어 ProfilerHUD 마커 4개가 그대로 유효하고 비용이 사실상 0 이다.
//
//  블러를 반해상도에서 분리형으로 도는 이유: 풀해상도 단일 박스블러는 더 비싸고
//  품질도 나쁘다. 배경 디포커스는 저주파라 해상도를 깎아도 티가 안 난다.
// ----------------------------------------------------------------------------
Shader "Hidden/Rendering/MaskBlur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        // 블러 패스용. xy = 이번 축의 UV 스텝(텍셀 크기 × 세기).
        float4 _MaskBlurStep;

        // 합성 패스용.
        float4 _MaskBlurCenter;   // xy = 선명 영역 중심(화면 UV)
        float4 _MaskBlurSize;     // xy = 선명 영역 반경(화면 UV). 종횡비 보정은 CPU 가 한다.
        float4 _MaskBlurShape;    // x=초타원 지수, y=페더, z=탈채도, w=암부
        float4 _MaskBlurFlags;    // x = 1 이면 _MaskBlurMaskTex 를 쓴다(절차 모양 대체)

        // 픽셀레이트. xy = 화면을 나눌 블록 개수(CPU 가 해상도/블록크기로 계산),
        //             z  = 1 이면 화면 전체 모드(마스크 무시),
        //             w  = 1 이면 활성. 0 이면 아래 함수들이 원본 UV 를 그대로 돌려준다.
        float4 _MaskBlurPixel;

        // 픽셀레이트 전용 반경(화면 UV). 블러와 같은 모양·중심을 쓰지만 반경만 따로다.
        // 반경을 키우면 선명하게 남는 중앙이 넓어져 = 픽셀 영역이 좁아진다.
        float4 _MaskBlurPixelSize;

        TEXTURE2D_X(_MaskBlurTex);   // 1번 패스가 SetGlobalTextureAfterPass 로 넘긴 블러 결과
        TEXTURE2D(_MaskBlurMaskTex);
        SAMPLER(sampler_MaskBlurMaskTex);

        // 5탭 가우시안(σ≈1.0). 분리형이므로 두 패스를 합쳐 실질 25탭이 된다.
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

        // UV 를 블록 격자로 스냅한다.
        //
        // ⚠️ 블록 '중심'(+0.5)을 찍어야 한다. 경계를 찍으면 bilinear 필터가 인접 블록을
        //    절반씩 섞어 블록이 뭉개진다("픽셀레이트를 켰는데 그냥 흐릿함"의 원인).
        float2 PixelateUV(float2 uv)
        {
            if (_MaskBlurPixel.w < 0.5)
                return uv;

            float2 blocks = max(_MaskBlurPixel.xy, 1.0);
            return (floor(uv * blocks) + 0.5) / blocks;
        }

        // 초타원 마스크. 0 = 반경 안, 1 = 반경 밖.
        //
        // |x/a|^n + |y/b|^n = 1 — n=2 는 타원, n 이 커지면 사각형에 수렴한다
        // → 하나의 값으로 원↔사각을 연속 조절. 블러와 픽셀레이트가 이 함수를 공유하고
        //   반경(radius)만 달리 넘겨 "같은 모양, 다른 크기"를 만든다.
        float SuperellipseAmount(float2 uv, float2 radius)
        {
            float2 d = abs(uv - _MaskBlurCenter.xy) / max(radius, 1e-4);
            float n = max(_MaskBlurShape.x, 2.0);
            float r = pow(pow(d.x, n) + pow(d.y, n), rcp(n));

            float feather = max(_MaskBlurShape.y, 1e-4);
            return saturate(smoothstep(1.0 - feather, 1.0 + feather, r));
        }

        // 0 = 완전히 선명, 1 = 완전히 블러.
        float BlurAmount(float2 uv)
        {
            if (_MaskBlurFlags.x >= 0.5)
            {
                // 텍스처 마스크: 흰색 = 선명. 절차 모양을 통째로 대체한다.
                float m = SAMPLE_TEXTURE2D(_MaskBlurMaskTex, sampler_MaskBlurMaskTex, uv).r;
                return saturate(1.0 - m);
            }

            return SuperellipseAmount(uv, _MaskBlurSize.xy);
        }

        // 0 = 픽셀레이트 안 함, 1 = 완전히 픽셀레이트.
        float PixelAmount(float2 uv)
        {
            if (_MaskBlurPixel.w < 0.5)
                return 0.0;

            // 화면 전체 모드는 마스크를 보지 않는다.
            if (_MaskBlurPixel.z >= 0.5)
                return 1.0;

            // ⚠️ 텍스처 마스크를 쓰면 픽셀 반경을 따로 줄 수 없다 — 블러 영역과 같아진다.
            //    (절차 모양은 반경만 바꿔 스케일할 수 있지만 텍스처는 그럴 수 없다.)
            if (_MaskBlurFlags.x >= 0.5)
                return BlurAmount(uv);

            return SuperellipseAmount(uv, _MaskBlurPixelSize.xy);
        }
        ENDHLSL

        Pass
        {
            Name "MaskBlurDownH"
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(BlurAxis(input.texcoord, float2(_MaskBlurStep.x, 0.0)), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "MaskBlurV"
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(BlurAxis(input.texcoord, float2(0.0, _MaskBlurStep.y)), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "MaskBlurComp"
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                // 블러 마스크와 픽셀 마스크는 중심·모양이 같고 '반경'만 다르다.
                // 픽셀 반경이 더 크므로 픽셀레이트가 걸리는 영역이 블러보다 좁다.
                float amount = BlurAmount(uv);
                float pixAmount = PixelAmount(uv);

                half3 sharp = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                half3 blurred = SAMPLE_TEXTURE2D_X(_MaskBlurTex, sampler_LinearClamp, uv).rgb;

                // 🔴 블러 텍스처만 양자화하면 픽셀레이트가 거의 안 보인다(2026-08-06 실측).
                //    블러가 고주파를 이미 없앤 그림이라 인접 블록에 색 차이가 남지 않는다.
                //    그래서 선명 성분까지 격자에서 읽어 블록에 대비를 만든다.
                //
                // ⚠️ UV 를 lerp 하면 안 된다 — 블록이 밀려 왜곡된다. 반드시 '색'을 섞는다.
                //    격자 샘플 2개는 픽셀 영역 안에서만 뜨고 중앙은 분기로 통째로 건너뛴다.
                if (pixAmount > 0.0)
                {
                    float2 uvPix = PixelateUV(uv);

                    half3 sharpPix =
                        SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uvPix).rgb;
                    half3 blurPix =
                        SAMPLE_TEXTURE2D_X(_MaskBlurTex, sampler_LinearClamp, uvPix).rgb;

                    sharp = lerp(sharp, sharpPix, pixAmount);
                    blurred = lerp(blurred, blurPix, pixAmount);
                }

                half3 outColor = lerp(sharp, blurred, amount);

                // 탈채도·암부는 이미 화면을 읽은 참에 같은 패스에서 처리한다(거의 공짜).
                // PLAN-vision §3 의 "배경 톤다운"이 이 자리다.
                float desat = _MaskBlurShape.z * amount;
                if (desat > 0.0)
                {
                    half luma = dot(outColor, half3(0.2126, 0.7152, 0.0722));
                    outColor = lerp(outColor, half3(luma, luma, luma), desat);
                }

                outColor *= 1.0 - _MaskBlurShape.w * amount;

                return half4(outColor, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
