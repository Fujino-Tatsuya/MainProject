// 로비 바닥 안내 화살표(할 일 111) — 바닥에 눕힌 쿼드에 화살표 무늬가 +U(진행 방향)로 흐른다.
// 흐름은 셰이더가 _Time 으로 직접 민다(스크립트 매 프레임 갱신 없음). 수치는 LobbyFloorArrowStrip 이 MaterialPropertyBlock 으로 넣는다.
// 텍스처 = 50.Art arrow2.png(+U 를 가리키는 흰 화살표, Repeat) 재사용 — 참조만.
Shader "Lobby/FloorArrow"
{
    Properties
    {
        _BaseMap ("Arrow", 2D) = "white" {}
        [HDR] _BaseColor ("Color", Color) = (0.3, 0.85, 1, 0.9)
        _Tiling ("Arrows along length", Float) = 4
        _ScrollSpeed ("Scroll (tiles/s, + = forward)", Float) = 1
        _EndFade ("End fade (0~0.5 of length)", Range(0, 0.5)) = 0.15
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
                float _Tiling;
                float _ScrollSpeed;
                float _EndFade;
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
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // U = 길이(진행 방향) 0~1, V = 폭 0~1. 길이 방향으로 _Tiling 개 반복, 시간만큼 앞으로 민다.
                float2 uv = float2(i.uv.x * _Tiling - _Time.y * _ScrollSpeed, i.uv.y);
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;

                // 양 끝을 부드럽게 지운다 — 화살표가 갑자기 생기고 사라지지 않게.
                float fade = _EndFade > 0.0001
                    ? saturate(min(i.uv.x, 1.0 - i.uv.x) / _EndFade)
                    : 1.0;
                c.a *= fade;
                return c;
            }
            ENDHLSL
        }
    }
}
