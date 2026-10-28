// 경로형 로비 바닥 안내 화살표(할 일 111 후속) — 바닥에 눕힌 Quad 조각 하나가 아틀라스의 한 영역(점선 또는 삼각형)을 그린다.
// 조각별 값(_UvRect·_PathDistance·색·흐름)은 LobbyFloorArrowPath 가 MaterialPropertyBlock 으로 넣는다 — 머티리얼은 모든 조각이 공유.
// 흐름 = 경로 거리로 만든 불투명도 물결을 _Time 으로 앞으로 민다(스크립트 매 프레임 갱신 없음).
// 텍스처 = 50.Art/UI/Navigate/ArrowLineSet.png 참조만.
Shader "Lobby/FloorArrowPath"
{
    Properties
    {
        _BaseMap ("Atlas", 2D) = "white" {}
        [HDR] _BaseColor ("Color", Color) = (1, 0.86, 0.17, 0.9)
        _UvRect ("UV rect (x, y, w, h)", Vector) = (0, 0, 1, 1)
        _PathDistance ("Distance along path (m)", Float) = 0
        _FlowSpeed ("Flow speed (m/s, + = forward)", Float) = 2
        _FlowWavelength ("Flow wavelength (m)", Float) = 3
        _FlowMinAlpha ("Flow trough alpha", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float4 _UvRect;
                float _PathDistance;
                float _FlowSpeed;
                float _FlowWavelength;
                float _FlowMinAlpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half flow : TEXCOORD1;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = _UvRect.xy + v.uv * _UvRect.zw;

                // 물결 마루가 거리 = 시간 × 속도 위치에 있다 → 시간이 갈수록 앞으로 간다. 조각 하나는 한 값(조각 단위로 깜빡임).
                float phase = (_PathDistance - _Time.y * _FlowSpeed) / max(_FlowWavelength, 0.01);
                o.flow = lerp(_FlowMinAlpha, 1.0, 0.5 + 0.5 * cos(phase * TWO_PI));
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                c.a *= i.flow;
                return c;
            }
            ENDHLSL
        }
    }
}
