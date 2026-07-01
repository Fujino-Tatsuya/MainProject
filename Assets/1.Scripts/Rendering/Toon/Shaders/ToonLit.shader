// ----------------------------------------------------------------------------
//  ToonLit.shader — URP17 캐릭터 셀셰이더 (블아풍)
//  평면음영(스텝 밴딩) + 림라이트 + 그림자 색조 + 메카 금속 토글.
//  카메라 각도 독립(N·L 기반) — 탑다운/정면 모두 동작 (하우징 대응).
//  LoS 픽셀 클리핑(④단계)은 후속: FogCore.hlsl include 지점 주석 표기.
// ----------------------------------------------------------------------------
Shader "Project/ToonLit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor ("Base Color", Color) = (1,1,1,1)

        [Header(Cel Shading)][Space]
        [Toggle(_FIXEDLIGHT_ON)] _FixedLightOn ("Fixed Light (라이트독립 세로음영)", Float) = 0
        _FixedLightDir ("Fixed Light Dir (오브젝트공간)", Vector) = (0.15,1.0,0.35,0)
        _ShadeColor ("Shade Tint (그림자 채색)", Color) = (0.62,0.60,0.72,1)
        _ShadeThreshold ("Shade Threshold (half-lambert)", Range(0,1)) = 0.5
        _ShadeSmooth ("Shade Smoothness", Range(0.001,0.7)) = 0.08
        _Shade2Threshold ("2nd Shade Threshold", Range(0,1)) = 0.28
        _Shade2Smooth ("2nd Shade Smoothness", Range(0.001,0.7)) = 0.12
        _ShadeStrength ("Shade Strength", Range(0,1)) = 1.0

        [Header(Face Clean (SD))][Space]
        _ShadeAmbient ("Ambient Fill (얼굴 어둠 방지)", Range(0,1)) = 0.35

        [Header(Rim Light)][Space]
        [HDR]_RimColor ("Rim Color", Color) = (1,1,1,1)
        _RimPower ("Rim Power", Range(0.5,16)) = 4.0
        _RimIntensity ("Rim Intensity", Range(0,3)) = 0.5
        _RimLightAlign ("Rim Light Align (광원쪽만)", Range(0,1)) = 0.3

        [Header(Hair Anisotropic (Angel Ring))][Space]
        [Toggle(_HAIR_ON)] _HairOn ("Hair Mode", Float) = 0
        [HDR]_HairSpecColor ("Hair Spec Color", Color) = (1,1,1,1)
        _HairThreshold ("Hair Spec Threshold", Range(0,1)) = 0.62
        _HairSmooth ("Hair Spec Smoothness", Range(0.001,0.5)) = 0.05
        _HairShift ("Hair Highlight Shift", Range(-1,1)) = 0.0

        [Header(Metal Toggle (Mecha 23ho))][Space]
        [Toggle(_METAL_ON)] _MetalOn ("Metal Mode", Float) = 0
        [HDR]_SpecColor2 ("Spec Color", Color) = (1,1,1,1)
        _SpecThreshold ("Spec Threshold", Range(0,1)) = 0.6
        _SpecSmooth ("Spec Smoothness", Range(0.001,0.5)) = 0.06
        _MetalBandSmooth ("Metal Band Smooth (매끈)", Range(0.001,0.9)) = 0.35

        [Header(Shadow Receive)][Space]
        _ReceiveShadowStrength ("Receive Shadow Strength", Range(0,1)) = 0.6

        [Header(Tone Brightness Saturation)][Space]
        _Brightness ("Brightness", Range(0.5,2)) = 1.18
        _Saturation ("Saturation", Range(0,2)) = 1.15

        [Header(Outline)][Space]
        _OutlineColor ("Outline Tint (표면색 곱계수)", Color) = (0.55,0.55,0.6,1)
        _OutlineWidth ("Outline Width", Range(0,0.03)) = 0.008
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        // ------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local _METAL_ON
            #pragma shader_feature_local _FIXEDLIGHT_ON
            #pragma shader_feature_local _HAIR_ON

            // URP 라이팅/그림자 키워드
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // (④단계) LoS 클리핑용 — 후속에서 활성:
            // #include "../../Fog/Shaders/FogCore.hlsl"   // Los_Amount(worldPos) 재사용

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half4  _ShadeColor;
                half   _ShadeThreshold;
                half   _ShadeSmooth;
                half   _Shade2Threshold;
                half   _Shade2Smooth;
                half   _ShadeStrength;
                half   _ShadeAmbient;
                half4  _RimColor;
                half   _RimPower;
                half   _RimIntensity;
                half   _RimLightAlign;
                half4  _SpecColor2;
                half   _SpecThreshold;
                half   _SpecSmooth;
                half   _MetalBandSmooth;
                half   _ReceiveShadowStrength;
                half   _Brightness;
                half   _Saturation;
                float4 _FixedLightDir;
                half4  _OutlineColor;
                half   _OutlineWidth;
                half4  _HairSpecColor;
                half   _HairThreshold;
                half   _HairSmooth;
                half   _HairShift;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
                float3 bitangentWS: TEXCOORD4;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrmInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS   = nrmInputs.normalWS;
                OUT.bitangentWS= nrmInputs.bitangentWS;
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor  = ComputeFogFactor(posInputs.positionCS.z);
                return OUT;
            }

            // 1단/2단 스텝 램프(0=완전그림자, 1=완전조명). smoothstep으로 부드러운 경계.
            half ToonRamp (half ndl)
            {
                // Half-Lambert: 측면광에서도 정면이 죽지 않게(블아풍). ndl[-1,1] → hl[0,1]
                half hl = ndl * 0.5 + 0.5;
                half s1 = smoothstep(_ShadeThreshold - _ShadeSmooth, _ShadeThreshold + _ShadeSmooth, hl);
                half s2 = smoothstep(_Shade2Threshold - _Shade2Smooth, _Shade2Threshold + _Shade2Smooth, hl);
                // 2단: 어두운 영역도 완전 검정 대신 중간톤 유지
                return saturate(s2 * 0.5 + s1 * 0.5);
            }

            half3 frag (Varyings IN) : SV_Target
            {
                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                half3 albedo  = baseTex.rgb;

                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // ---- 메인 라이트 ----
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                #if defined(_FIXEDLIGHT_ON)
                    // 라이트 독립: 오브젝트공간 고정 방향 → 월드. 실제 라이트가 움직이거나
                    // 캐릭터가 돌아도 음영이 캐릭터 기준으로 일관(블아식 세로 고정 음영).
                    float3 L = normalize(mul((float3x3)UNITY_MATRIX_M, _FixedLightDir.xyz));
                #else
                    float3 L = normalize(mainLight.direction);
                #endif
                half ndl = dot(N, L);

                // 그림자 수신(셀프/캐스트) — 강도 조절(블아는 약하게)
                half shadowAtten = lerp(1.0h, mainLight.shadowAttenuation, _ReceiveShadowStrength);

                half ramp = ToonRamp(ndl) * shadowAtten;
                // ambient fill: 그림자에서도 얼굴이 너무 어두워지지 않게 바닥 들어올림
                ramp = lerp(_ShadeAmbient, 1.0h, ramp);
                ramp = lerp(1.0h, ramp, _ShadeStrength);

                // 그림자부 = 채색 틴트, 조명부 = 알베도
                half3 shadedAlbedo = albedo * _ShadeColor.rgb;
                half3 litColor = lerp(shadedAlbedo, albedo, ramp);
                litColor *= mainLight.color;

                // ---- 추가 라이트(포인트/스폿) ----
                #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light addLight = GetAdditionalLight(lightIndex, IN.positionWS);
                    half addNdl = dot(N, normalize(addLight.direction));
                    half addRamp = smoothstep(_ShadeThreshold - _ShadeSmooth, _ShadeThreshold + _ShadeSmooth, addNdl);
                    half addAtten = addLight.distanceAttenuation * addLight.shadowAttenuation;
                    litColor += albedo * addLight.color * addRamp * addAtten;
                LIGHT_LOOP_END
                #endif

                // ---- 환경광(SH) ----
                half3 ambient = SampleSH(N) * albedo;
                half3 color = litColor + ambient * _ShadeAmbient;

                // ---- 메카 금속 토글: 스페큘러(스텝) + 매끈 밴딩 ----
                #if defined(_METAL_ON)
                {
                    float3 H = normalize(L + V);
                    half ndh = saturate(dot(N, H));
                    half specRaw = pow(ndh, 64.0h);
                    half spec = smoothstep(_SpecThreshold - _SpecSmooth, _SpecThreshold + _SpecSmooth, specRaw);
                    color += _SpecColor2.rgb * spec * shadowAtten;
                }
                #endif

                // ---- 머리 이방성 하이라이트(엔젤링, Kajiya-Kay 셀) ----
                #if defined(_HAIR_ON)
                {
                    float3 Th = normalize(IN.bitangentWS + N * _HairShift);
                    float3 Hh = normalize(L + V);
                    half TdotH = dot(Th, Hh);
                    half sinTH = sqrt(saturate(1.0h - TdotH * TdotH));
                    half hairSpec = smoothstep(_HairThreshold - _HairSmooth, _HairThreshold + _HairSmooth, sinTH);
                    color += _HairSpecColor.rgb * hairSpec * saturate(ndl + 0.3h);
                }
                #endif

                // ---- 림라이트(프레넬, 광원쪽 가중) ----
                half fresnel = pow(1.0h - saturate(dot(N, V)), _RimPower);
                half rimAlign = lerp(1.0h, saturate(ndl), _RimLightAlign);
                half rim = fresnel * _RimIntensity * rimAlign;
                // 흰 테두리 방지: 림을 표면색(albedo)으로 틴트 → 검은 스타킹은 옅은 어두운 테두리, 대비↑
                color += _RimColor.rgb * albedo * rim;

                // ---- 톤: 밝기 + 채도(블아풍 밝고 대비 있는 룩) ----
                color *= _Brightness;
                half luma = dot(color, half3(0.299h, 0.587h, 0.114h));
                color = lerp(luma.xxx, color, _Saturation);

                // (④단계 LoS 클리핑 자리)
                // half occ = Los_Amount(IN.positionWS); if (occ > 0.5) clip(-1);

                color = MixFog(color, IN.fogFactor);
                return color;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float4 _BaseMap_ST;
            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct Varyings   { float4 positionCS:SV_POSITION; };

            float4 GetShadowPositionHClip(Attributes input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                positionCS = ApplyShadowClamping(positionCS);
                return positionCS;
            }

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings o; o.positionCS = GetShadowPositionHClip(input); return o;
            }
            half4 ShadowPassFragment(Varyings input) : SV_TARGET { return 0; }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        //  인버티드 헐 아웃라인 (백페이스 확장). 화면공간 일정 두께 근사.
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vertOutline
            #pragma fragment fragOutline
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half4  _ShadeColor;
                half   _ShadeThreshold;
                half   _ShadeSmooth;
                half   _Shade2Threshold;
                half   _Shade2Smooth;
                half   _ShadeStrength;
                half   _ShadeAmbient;
                half4  _RimColor;
                half   _RimPower;
                half   _RimIntensity;
                half   _RimLightAlign;
                half4  _SpecColor2;
                half   _SpecThreshold;
                half   _SpecSmooth;
                half   _MetalBandSmooth;
                half   _ReceiveShadowStrength;
                half   _Brightness;
                half   _Saturation;
                float4 _FixedLightDir;
                half4  _OutlineColor;
                half   _OutlineWidth;
                half4  _HairSpecColor;
                half   _HairThreshold;
                half   _HairSmooth;
                half   _HairShift;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct AttributesO { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct VaryingsO   { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };

            VaryingsO vertOutline (AttributesO IN)
            {
                VaryingsO OUT;
                // 오브젝트 공간에서 노멀 방향으로 정점 확장(검증된 인버티드 헐 방식)
                float3 posOS = IN.positionOS.xyz + normalize(IN.normalOS) * _OutlineWidth;
                OUT.positionCS = TransformObjectToHClip(posOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }
            // 아웃라인 색 = 머티리얼별 근사 어두운색(_OutlineColor) 직접 출력.
            // 검은 파트(머리/옷/스타킹)는 albedo가 0이라 곱 방식이 안 통하므로 부위별 근사색을 지정.
            half4 fragOutline (VaryingsO IN) : SV_Target
            {
                return half4(_OutlineColor.rgb, 1);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back

            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings   { float4 positionCS:SV_POSITION; };
            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings o; o.positionCS = TransformObjectToHClip(input.positionOS.xyz); return o;
            }
            half4 DepthOnlyFragment(Varyings input) : SV_TARGET { return 0; }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull Back

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings   { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD1; };
            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }
            half4 DepthNormalsFragment(Varyings input) : SV_TARGET
            {
                return half4(normalize(input.normalWS) * 0.5 + 0.5, 0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
