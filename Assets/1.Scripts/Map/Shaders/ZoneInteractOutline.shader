// 상호작용 대상 근접 외곽선(PLAN-title-monitor T3).
// 단일 "Outline" 패스만 있다 — 기존 Flat Kit 렌더러 피처(PC_Renderer "Flat Kit Per Object Outline", PassNames=Outline)가
// 이 패스를 그린다. 대상 렌더러의 머티리얼 배열 끝에 이 머티리얼을 덧붙였다 떼는 식으로 쓴다(공용 머티리얼 무수정).
// 표면 패스가 없어서 덧붙여도 본체는 원래 머티리얼로만 그려진다.
// 방식 = 반전 헐(Cull Front) + 화면 공간 법선 밀기(Flat Kit 외곽선 패스와 같은 식). 각진 메시는 모서리가 갈라질 수 있어
// _OutlineScale 로 메시 자체도 조금 부풀려 틈을 메운다.
Shader "Zone/InteractOutline"
{
    Properties
    {
        _OutlineColor ("Color", Color) = (1, 0.92, 0.55, 1)
        _OutlineWidth ("Width (screen)", Range(0, 10)) = 2.5
        _OutlineScale ("Mesh Scale", Range(1, 1.1)) = 1.012
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "Outline" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _OutlineColor;
            float _OutlineWidth;
            float _OutlineScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float4 clip = TransformObjectToHClip(v.positionOS.xyz * _OutlineScale);
                float3 clipNormal = mul((float3x3)UNITY_MATRIX_VP, TransformObjectToWorldDir(v.normalOS, false));
                float2 aspect = float2(_ScreenParams.x / _ScreenParams.y, 1);
                float2 dir = clipNormal.xy;
                float len = length(dir);
                if (len > 1e-5) dir /= len;
                clip.xy += dir / aspect * _OutlineWidth * clip.w * 0.005;   // 화면 공간 굵기 — 거리와 무관
                o.positionCS = clip;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
