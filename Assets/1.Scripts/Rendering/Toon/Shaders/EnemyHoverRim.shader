// ----------------------------------------------------------------------------
//  몬스터 호버 림(프레넬) 외곽선 — 할 일 58 (Docs/tech/monster-hover-outline.md)
//
//  용도: HoverOutlineOverlaySet 이 대상 렌더러 밑에 얹는 **같은 메시 자식 렌더러**(부풀리기 없음)에 쓴다.
//        |dot(N, V)| 가 임계값보다 작은 곳(실루엣 가장자리)만 색을 칠하고 그 밖은 알파 0 — 원본이 그대로 보인다.
//  그리기: 투명 큐 + 알파 블렌드 + ZWrite Off + ZTest LEqual + Offset(-1,-1) 로 원본 위에 덧그린다(Z-fight 없음).
//        URP 일반 패스(UniversalForward)라 Flat Kit Per Object Outline 피처(LightMode=Outline)와 무관하다.
//  🔴 _BaseColor/_Color 를 두지 않는다 — HitFlash·사망 틴트 MPB 에 물들지 않게.
//  스키닝은 렌더러가 하므로 오브젝트 노멀을 월드로 바꾸기만 하면 된다.
// ----------------------------------------------------------------------------
Shader "Project/Enemy Hover Rim"
{
    Properties
    {
        _RimColor("Color", Color) = (1, 0.08, 0.08, 1)
        _RimThreshold("Threshold |N·V|", Range(0, 1)) = 0.25
        _RimSoftness("Softness", Range(0, 1)) = 0.1
        _RimIntensity("Intensity", Range(0, 8)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "HoverRim"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RimColor;
                half _RimThreshold;
                half _RimSoftness;
                half _RimIntensity;
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
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS); // 직교 카메라도 처리
                float ndv = abs(dot(n, v));

                // HoverRimSettings.RimAlpha 와 같은 식 — 임계값 ± 부드러움/2 구간에서 1→0.
                float half_ = max(_RimSoftness * 0.5, 1e-4);
                float rim = 1.0 - smoothstep(_RimThreshold - half_, _RimThreshold + half_, ndv);

                half alpha = saturate(rim * _RimColor.a);
                clip(alpha - 1e-3);
                return half4(_RimColor.rgb * _RimIntensity, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
