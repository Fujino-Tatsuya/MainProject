// ----------------------------------------------------------------------------
//  TraitBackdropSaturate.shader — 특성 창 배경 블러 결과 채도 조절(할 일 165 후속)
//
//  창이 열릴 때 블러 마지막에 1회만 블릿한다(Blitter, 패스 0). 매 프레임에는 돌지 않는다.
//  _Saturation: 0 = 완전 흑백, 1 = 원색. 루마 = Rec.709 가중치(0.2126, 0.7152, 0.0722)
//  — MaskBlur 합성 패스의 탈채도와 같은 값.
// ----------------------------------------------------------------------------
Shader "Hidden/Traits/TraitBackdropSaturate"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "TraitBackdropSaturate"
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Saturation;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb;
                half luma = dot(color, half3(0.2126, 0.7152, 0.0722));
                return half4(lerp(half3(luma, luma, luma), color, saturate(_Saturation)), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
