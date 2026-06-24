// ----------------------------------------------------------------------------
//  FullScreenFog.shader - URP 풀스크린 포그 블릿 셰이더
//  FogRendererFeature 가 생성하는 머티리얼이 사용. _BlitTexture(씬색) + 뎁스 →
//  월드좌표 복원 → FogCore 평가 → 씬색에 블렌딩.
// ----------------------------------------------------------------------------
Shader "Hidden/Fog/FullScreenFog"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "FullScreenFog"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "FogCore.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                half3 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                if (_FogGlobalEnabled < 0.5)
                    return half4(sceneColor, 1.0);

                float depth = SampleSceneDepth(uv);
                // Reversed-Z: 원거리(스카이박스) = 0
                float skyMask = (depth <= 1e-6) ? 1.0 : 0.0;

                float3 worldPos = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float dist = length(worldPos - _WorldSpaceCameraPos);

                float3 fogColor;
                float f = Fog_Evaluate(worldPos, dist, skyMask, fogColor);

                half3 outColor = lerp(sceneColor, fogColor, saturate(f));
                return half4(outColor, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
