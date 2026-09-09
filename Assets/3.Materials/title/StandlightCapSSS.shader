// ----------------------------------------------------------------------------
// StandlightCapSSS.shader — thin plastic lampshade approximation for URP 17.
// Uses direct/back lighting plus a view-dependent edge term to mimic light
// scattering through a thin plastic cap while retaining opaque shadows.
// ----------------------------------------------------------------------------
Shader "Project/StandlightCapSSS"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)

        [Header(Thin Plastic SSS)]
        [HDR] _SSSColor ("Scattering Color", Color) = (1.0, 0.42, 0.12, 1.0)
        _SSSStrength ("Scattering Strength", Range(0, 4)) = 0.85
        _SSSThickness ("Thinness", Range(0, 1)) = 0.72
        _SSSPower ("Back Scatter Falloff", Range(0.25, 8)) = 2.5
        _SSSEdgePower ("Edge Scatter Falloff", Range(0.25, 8)) = 2.0
        _SSSEdgeBlend ("Edge Scatter Blend", Range(0, 1)) = 0.35

        [Header(Surface)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.45
        _SpecularStrength ("Specular Strength", Range(0, 1)) = 0.25
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 0.7
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _SSSColor;
                half _SSSStrength;
                half _SSSThickness;
                half _SSSPower;
                half _SSSEdgePower;
                half _SSSEdgeBlend;
                half _Smoothness;
                half _SpecularStrength;
                half _AmbientStrength;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = positionInputs.positionCS;
                OUT.positionWS = positionInputs.positionWS;
                OUT.normalWS = normalInputs.normalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return OUT;
            }

            half3 EvaluateLight(half3 normalWS, half3 viewDirWS, half3 baseColor, Light light)
            {
                half3 lightDir = normalize(light.direction);
                half nDotL = saturate(dot(normalWS, lightDir));
                half diffuse = nDotL * light.distanceAttenuation * light.shadowAttenuation;

                // Back lighting is the primary thin-plastic scattering term.
                half backFacing = saturate(dot(-normalWS, lightDir));
                half backScatter = pow(backFacing, max(_SSSPower, 0.001h));

                // A softer grazing contribution keeps the cap from looking like
                // a hard two-sided decal when the light is near the silhouette.
                half viewFacing = saturate(dot(normalWS, viewDirWS));
                half edgeScatter = pow(1.0h - viewFacing, max(_SSSEdgePower, 0.001h));
                half scatter = lerp(backScatter, max(backScatter, edgeScatter), _SSSEdgeBlend);
                scatter *= _SSSStrength * lerp(0.35h, 1.0h, _SSSThickness);
                scatter *= light.distanceAttenuation * light.shadowAttenuation;

                half3 halfVector = SafeNormalize(lightDir + viewDirWS);
                half specular = pow(saturate(dot(normalWS, halfVector)), lerp(8.0h, 96.0h, _Smoothness));
                specular *= _SpecularStrength * light.distanceAttenuation * light.shadowAttenuation;

                return baseColor * light.color * diffuse
                     + _SSSColor.rgb * light.color * scatter
                     + specular * light.color;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // LIGHT_LOOP_BEGIN uses this data when the renderer runs in
                // Forward+. Populate its required fields before expanding it.
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                half3 normalWS = normalize(IN.normalWS);
                half3 viewDirWS = SafeNormalize(GetWorldSpaceViewDir(IN.positionWS));
                half3 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _BaseColor.rgb;

                half3 color = SampleSH(normalWS) * baseColor * _AmbientStrength;

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                color += EvaluateLight(normalWS, viewDirWS, baseColor, mainLight);

                #ifdef _ADDITIONAL_LIGHTS
                uint pixelLightCount = GetAdditionalLightsCount();

                // Forward+ stores additional directional lights in the first
                // slots; they are not included in GetAdditionalLightsCount().
                #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint lightIndex = 0u;
                            lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS);
                            lightIndex++)
                {
                    CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                    Light additionalLight = GetAdditionalLight(lightIndex, IN.positionWS);
                    color += EvaluateLight(normalWS, viewDirWS, baseColor, additionalLight);
                }
                #endif

                // In Forward this iterates the per-object light list; in
                // Forward+ LIGHT_LOOP_BEGIN traverses the cluster light list.
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light additionalLight = GetAdditionalLight(lightIndex, IN.positionWS);
                    color += EvaluateLight(normalWS, viewDirWS, baseColor, additionalLight);
                LIGHT_LOOP_END
                #endif

                color = MixFog(color, IN.fogFactor);
                return half4(color, _BaseColor.a);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }

    FallBack Off
}
