// ----------------------------------------------------------------------------
//  PlayerSilhouetteMask.shader — 벽 뒤 플레이어 윤곽선의 "마스크" 패스
//
//  플레이어 모델을 화면 크기 RT 에 다시 그려서 합성 패스가 쓸 정보를 채운다.
//
//      R = 커버리지 (F)   — 이 픽셀에 캐릭터가 있다
//      G = 가려짐   (H)   — 그 캐릭터의 "맨 앞 표면"이 씬 깊이보다 뒤에 있다
//      B = 소속           — 0 = 내 캐릭터 / 1 = 다른 플레이어
//      A = 커버리지       — 합성에서 블렌딩에 쓴다
//
//  🔴 왜 F 와 H 를 나눠 담는가(2026-09-14 설계 2판): 초안은 "가려진 픽셀만" 남기고 그 테두리를
//     그렸는데, 그건 **가려진 영역의 테두리**이지 캐릭터의 윤곽이 아니다. 벽이 허리를 자르면
//     허리에 가짜 수평선이 생긴다. 필요한 것은 (캐릭터 윤곽) × (가려짐) 이고, 두 연산은
//     순서를 바꿀 수 없다. 그래서 여기서는 **가려진 픽셀도 버리지 않고** 전부 그린 뒤
//     가려짐 여부를 별도 채널로 넘긴다.
//
//  🔴 왜 ZWrite On / LEqual 인가: 자기 가림을 없애기 위해서다. 깊이 없이 그리면 몸통 뒤쪽의
//     삼각형까지 그려져 "팔이 몸을 가렸다"를 가려짐으로 오판한다(Cull Back 만으로는 안 풀린다).
//     전용 깊이 버퍼에 그려서 픽셀마다 맨 앞 표면 하나만 남긴다.
//
//  이 셰이더는 overrideMaterial 로만 쓰인다 — 원본 머티리얼을 대체해 같은 메시를 다시 그린다.
// ----------------------------------------------------------------------------
Shader "Hidden/Rendering/PlayerSilhouetteMask"
{
    Properties
    {
        // 0 = 내 캐릭터, 1 = 다른 플레이어. 머티리얼 2개로 나눠 두 번 드로우한다.
        _SilhouetteId ("Silhouette Id (0=local, 1=remote)", Float) = 0
        // 자기 자신을 "가려졌다"로 잘못 판정하지 않게 하는 여유(m). 깊이 정밀도 대비 값.
        _OccludeBias ("Occlude Bias (m)", Float) = 0.03
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "SilhouetteMask"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Back
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _SilhouetteId;
                float _OccludeBias;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // 화면 좌표 → 깊이 텍스처 UV.
                // 🔴 직접 나누지 않는다 — 렌더 스케일·XR 이 걸리면 어긋난다. URP 헬퍼를 쓴다.
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);

                float sceneRaw = SampleSceneDepth(uv);

                // 🔴 reversed-Z 때문에 raw 값끼리 대소를 비교하면 플랫폼마다 뒤집힌다.
                //    둘 다 eye(선형 m) 로 바꾼 뒤 비교한다 — 부호 규칙이 하나뿐이라 안전하다.
                float sceneEye = LinearEyeDepth(sceneRaw, _ZBufferParams);
                float selfEye  = LinearEyeDepth(input.positionCS.z, _ZBufferParams);

                // 내가 씬보다 뒤에 있으면(= 앞에 뭔가 있으면) 가려진 것이다.
                // 자기 자신도 깊이 텍스처에 들어 있으므로 여유를 빼지 않으면 항상 "가려짐"이 된다.
                half occluded = (selfEye > sceneEye + _OccludeBias) ? 1.0h : 0.0h;

                return half4(1.0h, occluded, (half)_SilhouetteId, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
