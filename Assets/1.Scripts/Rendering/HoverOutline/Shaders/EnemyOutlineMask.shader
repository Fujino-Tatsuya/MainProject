// ----------------------------------------------------------------------------
//  EnemyOutlineMask.shader — 몬스터 외곽선(할 일 58)의 "마스크" 패스
//
//  EnemyOutlineFeature 가 대상 렌더러를 이 셰이더로 한 번 더 그린다(DrawRenderer 덮어쓰기 머티리얼).
//
//      R = 커버리지        — 이 픽셀에 대상이 있다
//      G = 색 인덱스       — 0 = 빨강(호버·스킬 범위) / 1 = 회색(나를 노림). 머티리얼 2개로 나눠 그린다.
//      B = eye 깊이(m)     — 그 픽셀의 맨 앞 대상 표면. 합성이 윤곽 픽셀의 씬 깊이와 비교해 가림을 판정한다.
//      A = 커버리지
//
//  🔴 ZWrite On / LEqual — 전용 깊이 버퍼에서 대상끼리 앞뒤를 가린다(겹친 자리는 앞 대상의 색·깊이).
//  🔴 여기서는 씬 깊이로 픽셀을 지우지 않는다 — 벽이 몸을 자르는 경계에 가짜 선이 생긴다. 가림은 합성에서.
// ----------------------------------------------------------------------------
Shader "Hidden/Rendering/EnemyOutlineMask"
{
    Properties
    {
        _OutlineColorIndex ("Color Index (0=primary, 1=aggro)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "EnemyOutlineMask"

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

            CBUFFER_START(UnityPerMaterial)
                float _OutlineColorIndex;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float eyeDepth : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                // 뷰 공간 z 로 직접 — 원근·직교 모두 같은 식(LinearEyeDepth 는 직교에서 틀린다).
                output.eyeDepth = -TransformWorldToView(positionWS).z;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(1.0h, (half)_OutlineColorIndex, (half)input.eyeDepth, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
