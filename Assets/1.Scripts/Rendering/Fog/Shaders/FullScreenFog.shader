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

                bool fogOn = _FogGlobalEnabled >= 0.5;
                bool dimOn = _DimEnabled >= 0.5;

                // 어비스는 포그·디밍과 독립이다. 예전에는 이 조기 반환과 아래 3) 이 둘 다
                // 포그에 묶여 있어서, 포그를 끄면 abyssEnabled 가 켜져 있어도 물안개가
                // 조용히 사라졌다(2026-08-06 수정).
                bool abyssOn = _AbyssEnabled >= 0.5;

                if (!fogOn && !dimOn && !abyssOn)
                    return half4(sceneColor, 1.0);

                float depth = SampleSceneDepth(uv);
                // Reversed-Z: 원거리(스카이박스) = 0
                float skyMask = (depth <= 1e-6) ? 1.0 : 0.0;

                float3 worldPos = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);

                half3 outColor = sceneColor;

                // 1) 포그 먼저(켜져 있으면)
                if (fogOn)
                {
                    float dist = length(worldPos - _WorldSpaceCameraPos);
                    float3 fogColor;
                    float f = Fog_Evaluate(worldPos, dist, skyMask, fogColor);
                    outColor = lerp(outColor, fogColor, saturate(f));
                }

                // 2) 그 위에 디밍 — 층/시야범위 + 시야 차폐를 max 로 합쳐 단일 톤 1회 적용.
                //    톤 통일(원형·부채꼴 경계 톤차 제거) + Dim_Apply 1회로 비용↓.
                //    (_LosBrightness/_LosSaturation 는 이제 미사용 — 톤은 _DimBrightness/_DimSaturation 로 일원화.)
                if (dimOn)
                {
                    float t = max(Dim_Amount(worldPos, skyMask), Los_DimAmount(worldPos, skyMask));
                    outColor = Dim_Apply(outColor, t, _DimBrightness, _DimSaturation);
                }

                // 3) 어비스 물안개 — 디밍 위에 심연색으로 덮음(구멍 내부만, 하늘 제외).
                //    디밍의 탈채도가 심연색을 흑백으로 날리지 않도록 마지막에 합성.
                if (abyssOn)
                {
                    float3 abyssCol;
                    float a = Abyss_Evaluate(worldPos, abyssCol) * (1.0 - skyMask);
                    outColor = lerp(outColor, abyssCol, saturate(a));
                }

                return half4(outColor, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
