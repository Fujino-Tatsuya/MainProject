// 카메라와 플레이어 사이를 가리는 벽을 디더 클립으로 지운다.
//
// 불투명도는 오브젝트 단위가 아니라 프래그먼트의 월드 좌표로 계산한다. 벽 하나가 통째로
// 사라지는 대신 카메라-플레이어 시선축에 가까운 픽셀부터 투명해지므로, ㅡ자 벽이라면
// 시선축에 가까운 쪽 끝은 완전히 비고 반대쪽 끝으로 갈수록 원래 불투명도로 되돌아온다.
//
// C# 쪽은 WallOcclusionDriver가 전역 유니폼 네 개만 갱신한다. 물리 쿼리도,
// MaterialPropertyBlock도, 벽별 상태도 없다.
Shader "Project/Environment/Wall Occlusion Dither"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0,2)) = 1
        _Metallic ("Metallic", Range(0,1)) = 0
        _Smoothness ("Smoothness", Range(0,1)) = 0.5

        [Header(Wall Occlusion)]
        [ToggleUI] _WallOccAffected ("Affected By Occlusion", Float) = 1
        _WallOcclusionOpacity ("Opacity Master Multiplier", Range(0,1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale;
            half _Metallic;
            half _Smoothness;
            half _WallOccAffected;
            half _WallOcclusionOpacity;
        CBUFFER_END

        // 전역 유니폼 — WallOcclusionDriver가 매 프레임 설정한다.
        // UnityPerMaterial 밖에 두어야 SRP Batcher 호환이 유지된다.
        float4 _WallOccPlayerWS;   // xyz = 플레이어 월드 위치
        float4 _WallOccCameraWS;   // xyz = 게임플레이 카메라 월드 위치
        float4 _WallOccRange;      // x=innerRadius, y=outerRadius, z=minOpacity, w=enable
        float4 _WallOccShape;      // x=floorNormalThreshold, y=behindFalloff, z=floorGuardDepth

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);
        SAMPLER(sampler_BumpMap);

        // 프래그먼트 월드 위치에서 불투명도를 구한다. 1 = 원래대로, 0 = 완전히 비어 보임.
        float WallOcclusionFactor(float3 positionWS, half3 normalWS)
        {
            if (_WallOccRange.w < 0.5 || _WallOccAffected < 0.5h)
                return 1.0;

            // 바닥만 보호한다.
            //
            // 노멀만으로 가르면 벽 윗면·선반·창틀 윗면 같은 수평 디테일까지 보호되는데,
            // 탑다운 카메라에서는 그게 가장 크게 보이는 면이라 벽 몸통만 지워지고
            // 윤곽이 통째로 남는다. 그래서 "위를 향한 면"이면서 동시에 "플레이어보다
            // 아래"일 때만 보호한다. 플레이어 위에 있는 수평면은 실제로 시야를 가리므로
            // 지우는 게 맞다.
            float upness = saturate(
                (abs(normalWS.y) - _WallOccShape.x) / max(1.0 - _WallOccShape.x, 1e-4));
            float below = saturate(
                (_WallOccPlayerWS.y - positionWS.y) / max(_WallOccShape.z, 1e-4));
            float floorProtect = upness * below;
            if (floorProtect >= 1.0)
                return 1.0;

            float3 toPlayer = _WallOccPlayerWS.xyz - _WallOccCameraWS.xyz;
            float sightLength = length(toPlayer);
            if (sightLength < 1e-4)
                return 1.0;

            // 카메라-플레이어 선분까지의 수직 거리. 선분 밖은 끝점으로 clamp되므로
            // 플레이어 뒤쪽에서도 값이 이어진다(경계 이음새 방지).
            float3 sightDir = toPlayer / sightLength;
            float along = dot(positionWS - _WallOccCameraWS.xyz, sightDir);
            float3 onSight =
                _WallOccCameraWS.xyz + sightDir * clamp(along, 0.0, sightLength);
            float radial = distance(positionWS, onSight);

            float k = saturate(
                (radial - _WallOccRange.x) /
                max(_WallOccRange.y - _WallOccRange.x, 1e-4));
            k = k * k * (3.0 - 2.0 * k);
            float opacity = lerp(_WallOccRange.z, 1.0, k);

            // 플레이어보다 뒤에 있는 면은 시야를 가리지 않으므로 원래 불투명도로 되돌린다.
            float behind = saturate(
                (along - sightLength) / max(_WallOccShape.y, 1e-4));
            opacity = lerp(opacity, 1.0, behind);

            return lerp(opacity, 1.0, floorProtect);
        }

        void ClipWallOcclusion(float4 positionCS, float3 positionWS, half3 normalWS)
        {
            float opacity =
                WallOcclusionFactor(positionWS, normalWS) * _WallOcclusionOpacity;

            // interleaved gradient noise — 화면 공간 디더로 반투명을 흉내낸다.
            float2 pixel = floor(positionCS.xy);
            float threshold =
                frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            clip(opacity - threshold);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = normals.normalWS;
                output.tangentWS = half4(normals.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // 노멀맵이 아니라 기하 노멀로 벽/바닥을 가른다(타일링 노이즈에 흔들리지 않게).
                ClipWallOcclusion(input.positionCS, input.positionWS, input.normalWS);

                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 normalTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv),
                    _BumpScale);
                half3 bitangentWS = input.tangentWS.w *
                    cross(input.normalWS, input.tangentWS.xyz);
                half3 normalWS = normalize(TransformTangentToWorld(
                    normalTS,
                    half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS)));

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half ndl = saturate(dot(normalWS, mainLight.direction));
                half3 color = baseSample.rgb *
                    (SampleSH(normalWS) + mainLight.color * ndl *
                     mainLight.distanceAttenuation * mainLight.shadowAttenuation);

                #if defined(_ADDITIONAL_LIGHTS)
                // Forward+(_CLUSTER_LIGHT_LOOP)에서 LIGHT_LOOP_BEGIN 매크로가
                // inputData.normalizedScreenSpaceUV / positionWS 를 직접 참조한다.
                // 이 두 필드를 채우지 않으면 클러스터 변종이 컴파일되지 않는다.
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV =
                    GetNormalizedScreenSpaceUV(input.positionCS);

                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light light = GetAdditionalLight(lightIndex, input.positionWS);
                    half additionalNdl = saturate(dot(normalWS, light.direction));
                    color += baseSample.rgb * light.color * additionalNdl *
                        light.distanceAttenuation * light.shadowAttenuation;
                LIGHT_LOOP_END
                #endif

                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                output.positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                output.positionCS = ApplyShadowClamping(output.positionCS);
                output.positionWS = positionWS;
                output.normalWS = half3(normalWS);
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                // 사라진 벽이 그림자만 남기지 않도록 그림자 패스도 같은 기준으로 클립한다.
                ClipWallOcclusion(input.positionCS, input.positionWS, input.normalWS);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Back
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = half3(TransformObjectToWorldNormal(input.normalOS));
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                // Depth 기반 효과(포그·SSAO 등)에 사라진 벽이 남지 않게 동일 기준으로 클립한다.
                ClipWallOcclusion(input.positionCS, input.positionWS, input.normalWS);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = half3(TransformObjectToWorldNormal(input.normalOS));
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ClipWallOcclusion(input.positionCS, input.positionWS, input.normalWS);
                return half4(normalize(input.normalWS) * 0.5h + 0.5h, 0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
