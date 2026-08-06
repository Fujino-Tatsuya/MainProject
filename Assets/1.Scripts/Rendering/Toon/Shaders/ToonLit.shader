// ----------------------------------------------------------------------------
//  ToonLit.shader — URP17 캐릭터 셀셰이더 (블아풍)
//  평면음영(스텝 밴딩) + 림라이트 + 그림자 색조 + 메카 금속 토글.
//  카메라 각도 독립(N·L 기반) — 탑다운/정면 모두 동작 (하우징 대응).
//
//  탑다운 가독성 3종 (2026-07-30):
//   1) 캐릭터 조명 독립 — _CharKeyColor/_CharKeyIntensity/_CharLightBlend 가 mainLight 를 대체.
//      맵이 어두워도 캐릭터 밝기가 유지된다.
//   2) 아웃라인 화면공간 두께 — _OutlinePixels(px). 카메라 거리와 무관하게 일정.
//   3) 고정광 방향은 **월드공간**(_FixedLightDir). 예전 오브젝트공간 방식은 임포트 회전 때문에
//      세로광이 수평광으로 뒤집혀 있었다.
//
//  ⚠️ 시도했다가 되돌린 것 (2026-07-31) — 같은 함정을 다시 밟지 말 것:
//    URP SSAO 를 읽어 "가려진 곳에만 음영"(크리즈)을 만들려 했다. 방향은 레퍼런스와 맞지만
//    **PC_Renderer 의 SSAO 설정이 Samples=1 / BlurQuality=0 으로 매우 노이즈가 심하다.**
//    그 값을 smoothstep 으로 계단화하면 노이즈가 그대로 **얼룩 반점**으로 증폭돼 캐릭터 표면이
//    더러워진다. AO 를 쓰려면 먼저 SSAO 품질(Samples/Blur)을 올리거나, 계단화 대신 부드럽게
//    곱하는 방식이어야 한다. 지금 룩은 팀장 승인 상태이므로 손대지 않는다.
//  LoS 픽셀 클리핑(④단계)은 후속: FogCore.hlsl include 지점 주석 표기.
// ----------------------------------------------------------------------------
Shader "Project/ToonLit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor ("Base Color", Color) = (1,1,1,1)

        [Header(Character Key Light (map independent))][Space]
        // 맵이 어두우면 캐릭터도 같이 죽던 문제의 뿌리. _FIXEDLIGHT_ON 은 **방향만** 고정했고
        // litColor *= mainLight.color 가 그대로 남아 씬 라이트에 종속돼 있었다.
        // Blend 1 = 씬 라이트를 완전히 무시하고 아래 키라이트 색·강도로만 캐릭터를 칠한다(ZZZ/블아식).
        [HDR]_CharKeyColor ("Char Key Color (캐릭터 전용 키라이트)", Color) = (1,0.98,0.94,1)
        _CharKeyIntensity ("Char Key Intensity", Range(0,3)) = 1.15
        _CharLightBlend ("Char Light Blend (0=씬라이트 1=키라이트)", Range(0,1)) = 1.0
        _CharAmbient ("Char Ambient (키라이트 기준 환경광)", Range(0,1)) = 0.30

        [Header(Cel Shading)][Space]
        [Toggle(_FIXEDLIGHT_ON)] _FixedLightOn ("Fixed Light (라이트독립 음영)", Float) = 0
        // ⚠️ 월드공간이다. 예전엔 오브젝트공간이었는데, 블렌더 Z-up 모델은 Unity 에서 X -90도로
        // 눕혀 임포트되므로(Paladin tripo_part_0 = rotation -90) "세로광 (0.15,1,0.35)" 이 월드에서는
        // (0.15,0.35,-1) 즉 거의 수평 후방광이 됐다. 게다가 오브젝트공간이면 캐릭터가 회전할 때
        // 음영도 같이 돌아 탑다운에서 광원이 캐릭터를 따라도는 것처럼 보인다.
        _FixedLightDir ("Fixed Light Dir (월드공간)", Vector) = (0.5,0.55,0.4,0)
        _ShadeColor ("Shade Tint (그림자 채색)", Color) = (0.46,0.44,0.56,1)
        _ShadeThreshold ("Shade Threshold (half-lambert)", Range(0,1)) = 0.5
        _ShadeSmooth ("Shade Smoothness", Range(0.001,0.7)) = 0.025
        _Shade2Threshold ("2nd Shade Threshold", Range(0,1)) = 0.32
        _Shade2Smooth ("2nd Shade Smoothness", Range(0.001,0.7)) = 0.03
        // 중간 밴드의 밝기. 낮출수록 2단 그림자가 깊어져 대비가 세진다(0.5 = 예전 동작).
        _Shade2Level ("2nd Shade Level (중간밴드 밝기)", Range(0,1)) = 0.35
        _ShadeStrength ("Shade Strength", Range(0,1)) = 1.0

        [Header(Face Clean (SD))][Space]
        _ShadeAmbient ("Ambient Fill (얼굴 어둠 방지)", Range(0,1)) = 0.35

        // 부위별 머티리얼로 얼굴을 분리할 수 없는 단일 서브메시 모델(Paladin = tripo_part_0)용.
        // 오브젝트공간 높이로 머리 영역만 골라 Ambient Fill 을 올린다.
        // fbx 를 부위별 서브메시로 나눌 수 있게 되면 이 토글은 꺼고 Skin 머티리얼로 옮기는 게 정석이다.
        [Header(Face Lift (single submesh models))][Space]
        [Toggle(_FACELIFT_ON)] _FaceLiftOn ("Face Lift (머리 영역만 밝게)", Float) = 0
        _FaceLiftHeight ("Face Lift Height (피벗 위 m — 얼굴 시작 높이)", Float) = 1.42
        _FaceLiftSmooth ("Face Lift Smooth (경계 부드러움)", Range(0.001,1)) = 0.1
        _FaceLiftAmbient ("Face Lift Ambient (머리 Ambient Fill)", Range(0,1)) = 0.9
        // 얼굴을 셀음영·라이트색에서 떼어내 알베도 플랫로 보내는 비율.
        // Ambient Fill 만 올려선 부족하다 — litColor 에 mainLight.color 가 곱해져서
        // 씬 라이트가 어두우면 얼굴도 같이 어두워진다(실제로 그렇게 나왔다).
        // 1 = 완전 플랫(라이트 무관), 0 = 몸과 동일 연산. 경계는 위 smoothstep 으로 섞인다.
        _FaceFlatten ("Face Flatten (얼굴 라이트 분리)", Range(0,1)) = 1.0
        // 플랫로 보낸 얼굴의 밝기 배수. 몸 음영을 진하게 갈수록 얼굴은 따로 들어올려야 한다.
        _FaceBrightness ("Face Brightness (얼굴 전용 밝기)", Range(0.5,2)) = 1.12

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
        // 아웃라인 색은 **표면색(BaseMap x BaseColor)에서 따온다**. 색조는 유지하고 밝기만 낮춘 뒤
        // _OutlineColor 를 전역 틴트로 곱한다. 검은 파트(머리/옷/스타킹)는 albedo 가 0이라
        // 단순 곱으로는 아웃라인이 사라지므로 _OutlineMinLum 으로 최소 밝기를 보장한다.
        _OutlineColor ("Outline Tint (표면색에 곱하는 전역 틴트)", Color) = (1,1,1,1)
        _OutlineDarken ("Outline Darken (표면색 대비 어둡기)", Range(0,1)) = 0.25
        _OutlineMinLum ("Outline Min Luminance (검은 파트 보정)", Range(0,1)) = 0.14
        // 오브젝트공간 고정 두께. 카메라가 멀어지면 화면상 두께가 줄어든다 → 탑다운에서 1px 미만.
        // 아래 Screen Space 토글을 켜면 이 값은 무시되고 _OutlinePixels 가 쓰인다.
        _OutlineWidth ("Outline Width (오브젝트공간 — 폴백)", Range(0,0.03)) = 0.008
        [Toggle(_OUTLINE_SCREEN_ON)] _OutlineScreenOn ("Outline Screen Space (거리보정)", Float) = 1
        _OutlinePixels ("Outline Pixels (화면 기준 두께 px)", Range(0,12)) = 3.0
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
            #pragma shader_feature_local _FACELIFT_ON

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
                float  _FaceLiftHeight;
                half   _FaceLiftSmooth;
                half   _FaceLiftAmbient;
                half   _OutlineDarken;
                half   _OutlineMinLum;
                half   _FaceFlatten;
                // ⚠️ UnityPerMaterial 은 모든 패스에서 레이아웃이 동일해야 한다.
                //    프로퍼티를 추가할 때 ForwardLit / Outline 두 블록에 같은 순서로 넣어라.
                half4  _CharKeyColor;
                half   _CharKeyIntensity;
                half   _CharLightBlend;
                half   _CharAmbient;
                half   _FaceBrightness;
                half   _Shade2Level;
                half   _OutlinePixels;
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

            // 1단/2단 스텝 램프(0=완전그림자, 1=완전조명).
            //
            // 레퍼런스는 젠레스 존 제로 강의의 4단계 중 마지막 — round(x) 나 step(0.2,x) 이 아니라
            // **smoothstep(0.2, 0.25, x)**, 즉 "경계는 또렷하되 계단은 없는" 좁은 창이다.
            // 그래서 _ShadeSmooth 는 크게 두면 안 된다(0.02~0.04 권장). 대비는 창 폭이 아니라
            // _ShadeColor 와 _Shade2Level 로 만든다.
            half ToonRamp (half ndl)
            {
                // Half-Lambert: 측면광에서도 정면이 죽지 않게(블아풍). ndl[-1,1] → hl[0,1]
                half hl = ndl * 0.5 + 0.5;
                half s1 = smoothstep(_ShadeThreshold - _ShadeSmooth, _ShadeThreshold + _ShadeSmooth, hl);
                half s2 = smoothstep(_Shade2Threshold - _Shade2Smooth, _Shade2Threshold + _Shade2Smooth, hl);
                // 3톤(최암부 0 / 중간 _Shade2Level / 밝은면 1). _Shade2Level 0.5 = 예전 동작.
                return saturate(s2 * _Shade2Level + s1 * (1.0h - _Shade2Level));
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
                    // 라이트 독립: **월드공간** 고정 방향. 씬 라이트가 움직여도, 캐릭터가 회전해도
                    // 음영 방향이 화면 기준으로 일관된다.
                    // (예전엔 오브젝트공간이었다 — 임포트 회전 -90 때문에 세로광이 수평광으로
                    //  뒤집혀 있었고, 캐릭터 yaw 에 따라 음영이 같이 돌아갔다.)
                    float3 L = normalize(_FixedLightDir.xyz);
                #else
                    float3 L = normalize(mainLight.direction);
                #endif
                half ndl = dot(N, L);

                // 그림자 수신(셀프/캐스트) — 강도 조절(블아는 약하게)
                half shadowAtten = lerp(1.0h, mainLight.shadowAttenuation, _ReceiveShadowStrength);

                // Ambient Fill — 단일 서브메시 모델은 얼굴을 머티리얼로 못 나누므로,
                // 오브젝트공간 높이로 머리 영역만 골라 fill 을 올린다(Paladin 전용 토글).
                half ambientFill = _ShadeAmbient;
                half faceMask = 0.0h;   // 아래 얼굴 분리 블록에서도 쓰므로 스코프 밖에 둔다.
                #if defined(_FACELIFT_ON)
                {
                    // ⚠️ 오브젝트공간 Y를 쓰면 안 된다. 블렌더 Z-up 모델은 Unity에서 X -90도로 눕혀
                    // 임포트되므로(Paladin tripo_part_0 = rotation (270,0,0)) 오브젝트 Y가 위쪽이 아니다
                    // — 마스크가 위/아래가 아니라 앞/뒤로 잘린다.
                    // 회전과 무관하게 "피벗 위로 몇 m"를 쓴다. 피벗은 캐릭터 발밑이므로 곧 키 높이다.
                    // (Paladin 루트 캡슐 실측: height 1.79 / center.y 0.88 → 얼굴은 약 1.45~1.75m)
                    float pivotWorldY = GetObjectToWorldMatrix()._m13;
                    float heightAbovePivot = IN.positionWS.y - pivotWorldY;

                    faceMask = smoothstep(_FaceLiftHeight - _FaceLiftSmooth,
                                          _FaceLiftHeight + _FaceLiftSmooth, heightAbovePivot);
                    ambientFill = lerp(_ShadeAmbient, _FaceLiftAmbient, faceMask);
                }
                #endif

                half ramp = ToonRamp(ndl) * shadowAtten;
                // ambient fill: 그림자에서도 얼굴이 너무 어두워지지 않게 바닥 들어올림
                ramp = lerp(ambientFill, 1.0h, ramp);
                ramp = lerp(1.0h, ramp, _ShadeStrength);

                // 그림자부 = 채색 틴트, 조명부 = 알베도
                half3 shadedAlbedo = albedo * _ShadeColor.rgb;
                half3 litColor = lerp(shadedAlbedo, albedo, ramp);

                // ---- 캐릭터 조명 독립 ----
                // 여기가 "맵이 어두우면 캐릭터도 죽는" 증상의 뿌리였다. mainLight.color 를 그대로
                // 곱하면 캐릭터 밝기가 맵 라이팅에 종속된다. 머티리얼이 소유한 키라이트로 대체한다.
                // Blend 0 이면 예전 동작(씬 라이트), 1 이면 완전 독립.
                half3 keyLight   = _CharKeyColor.rgb * _CharKeyIntensity;
                half3 lightColor = lerp(mainLight.color, keyLight, _CharLightBlend);
                litColor *= lightColor;

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
                // SH 도 맵 라이팅이다. 키라이트 모드에서는 SH 대신 키라이트 기준 환경광을 쓴다
                // (그러지 않으면 litColor 만 독립시켜도 어두운 맵에서 그림자부가 그대로 까맣게 남는다).
                half3 charAmbient = _CharKeyColor.rgb * _CharAmbient;
                half3 ambient = lerp(SampleSH(N), charAmbient, _CharLightBlend) * albedo;
                half3 color = litColor + ambient * ambientFill;

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

                // ---- 얼굴: 몸과 다른 라이트 연산으로 분리 ----
                //
                // 애니풍 얼굴은 코·볼 음영이 지저분하게 끼면 안 되고, 씬 라이트가 어두워도 밝아야 한다.
                // 그래서 얼굴 영역은 셀음영을 걷어내고 **알베도 플랫 × 키라이트**로 보낸다.
                // SDF 얼굴 그림자 텍스처가 준비되면 이 블록을 SDF 판정으로 교체하는 게 정석이다.
                //
                // ⚠️ 순서 주의: 림라이트보다 **앞**에 둔다. 뒤에 두면 _FaceFlatten 1.0 에서 얼굴의
                //    림 테두리까지 지워져 머리 실루엣이 배경에 묻는다(탑다운에서 특히 치명적).
                #if defined(_FACELIFT_ON)
                {
                    half3 faceFlat = albedo * _FaceBrightness * lightColor;
                    color = lerp(color, faceFlat, faceMask * _FaceFlatten);
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
            #pragma shader_feature_local _OUTLINE_SCREEN_ON
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
                float  _FaceLiftHeight;
                half   _FaceLiftSmooth;
                half   _FaceLiftAmbient;
                half   _OutlineDarken;
                half   _OutlineMinLum;
                half   _FaceFlatten;
                // ⚠️ UnityPerMaterial 은 모든 패스에서 레이아웃이 동일해야 한다.
                //    프로퍼티를 추가할 때 ForwardLit / Outline 두 블록에 같은 순서로 넣어라.
                half4  _CharKeyColor;
                half   _CharKeyIntensity;
                half   _CharLightBlend;
                half   _CharAmbient;
                half   _FaceBrightness;
                half   _Shade2Level;
                half   _OutlinePixels;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct AttributesO { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct VaryingsO   { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };

            VaryingsO vertOutline (AttributesO IN)
            {
                VaryingsO OUT;
                float3 nrmOS = normalize(IN.normalOS);

            #if defined(_OUTLINE_SCREEN_ON)
                // ---- 화면공간 일정 두께 ----
                // 오브젝트공간 고정 확장은 카메라가 멀어지면 화면상 두께가 같이 줄어든다.
                // 탑다운뷰 거리(카메라가 십수 m 위)에서는 0.006~0.008 이 1px 미만이 되어
                // "아웃라인이 안 보인다"가 됐다. 그래서 클립공간에서 밀어내 px 두께를 고정한다.
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = normalize(TransformObjectToWorldNormal(nrmOS));
                float4 posCS = TransformWorldToHClip(posWS);

                // 노멀을 화면 방향으로 투영. 뷰 → 클립(투영행렬의 3x3)까지 거쳐야
                // FOV/종횡비가 반영된다 = 별도 거리·FOV 보정식이 필요 없다.
                float3 nrmVS = TransformWorldToViewDir(nrmWS, true);
                float3 nrmCS = mul((float3x3)UNITY_MATRIX_P, nrmVS);

                // 카메라를 정면으로 보는(또는 등지는) 정점은 화면상 방향이 없다 → 확장 0.
                float  len2  = dot(nrmCS.xy, nrmCS.xy);
                float2 dir   = len2 > 1e-8f ? nrmCS.xy * rsqrt(len2) : float2(0, 0);

                // NDC 는 화면 폭/높이에 대해 [-1,1] 이므로 1px = 2/해상도.
                // posCS.w 를 곱해 NDC 오프셋을 클립공간으로 되돌린다(원근 나눗셈 상쇄).
                posCS.xy += dir * posCS.w * (_OutlinePixels * 2.0f) / _ScreenParams.xy;
                OUT.positionCS = posCS;
            #else
                // 폴백: 오브젝트 공간 노멀 확장(예전 방식)
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz + nrmOS * _OutlineWidth);
            #endif

                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }
            // 아웃라인 색을 **표면색에서 따온다**.
            //
            // 이전에는 _OutlineColor 를 그대로 출력했다(머티리얼별 근사 어두운색을 손으로 지정).
            // 단순히 albedo 를 곱는 방식은 검은 파트(머리/옷/스타킹)에서 albedo≈0 이라 아웃라인이
            // 사라지는 문제가 있었다 — 그래서 **색조(hue)는 표면에서 가져오고 밝기만 따로 다룬다**:
            //   1. 표면색을 최대성분으로 나눠 색조만 남긴다(검은색도 방향은 보존).
            //   2. 밝기는 표면 밝기를 _OutlineDarken 으로 낮추되 _OutlineMinLum 을 하한으로 둔다.
            //   3. 마지막에 _OutlineColor 를 전역 틴트로 곱해 전체 톤을 조절한다.
            // 결과: 파트별로 손으로 색을 지정하지 않아도 아웃라인이 그 부위 색을 따라가고,
            //       검은 파트에서도 선이 남는다.
            half4 fragOutline (VaryingsO IN) : SV_Target
            {
                half3 surface = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _BaseColor.rgb;

                half  lum = max(surface.r, max(surface.g, surface.b));
                // 색조 보존. 완전 검정(lum=0)이면 무채색으로 떨어지게 둔다.
                half3 hue = lum > 1e-4h ? surface / lum : half3(1, 1, 1);

                half  outLum = max(lum * _OutlineDarken, _OutlineMinLum);
                half3 rgb    = hue * outLum * _OutlineColor.rgb;

                return half4(rgb, 1);
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
