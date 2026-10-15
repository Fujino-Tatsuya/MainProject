// 바닥 판 예고(드론 착탄 원·전기 장판 사각) — PLAN-telegraph-style.
// 🔴 Sprites/Default 를 대체한다: 같은 블렌드(프리멀티 알파)·ZWrite Off·Cull Off·같은 속성명(_MainTex·_Color)이라
//    BossPatternVisuals.Paint(MaterialPropertyBlock) 호출이 그대로 동작한다. 마스크가 없으면(세기 0) 결과도 같다.
// 색·마스크 계산은 데칼(SG_TelegraphDecal)과 같은 TelegraphMask.hlsl.
Shader "Monster/TelegraphQuad"
{
    Properties
    {
        _MainTex ("Shape", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MaskTex ("Mask", 2D) = "white" {}
        _MaskParams ("Mask (mode, strength, tile m, rotation rad)", Vector) = (0, 0, 1, 0)
        _MaskOrigin ("Mask Origin", Vector) = (0, 0, 0, 0)
        _MaskAxes ("Mask Axes (right xz, forward xz)", Vector) = (1, 0, 0, 1)
        _MaskExtent ("Mask Extent (width, length m)", Vector) = (1, 1, 0, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float4 _MaskParams;
                float4 _MaskOrigin;
                float4 _MaskAxes;
                float4 _MaskExtent;
            CBUFFER_END

            #define _BaseColor _Color
            #include "TelegraphMask.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float4 shape = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color;
                float3 rgb; float a;
                TelegraphShade_float(shape, i.positionWS, rgb, a);
                return half4(rgb * a, a);   // 프리멀티(Sprites/Default 와 같은 블렌드)
            }
            ENDHLSL
        }
    }
}
