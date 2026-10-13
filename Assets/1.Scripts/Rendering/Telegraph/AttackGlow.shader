// 공격 직전 부위 발광(Chomp 입 · Humanoid 도끼 — PLAN-monster-tells M2).
// 같은 메시를 한 번 더 **가산(Additive)** 으로 그린다(렌더러에 재질 슬롯을 하나 덧붙인다 — 인터럽트 몸 오버레이와 같은 방식).
// 🔴 FlatKit 의 _EMISSION 은 shader_feature 라 쓰는 재질이 없으면 빌드에서 변형이 빠진다 → 그쪽 대신 이 셰이더.
//    원래 재질은 건드리지 않으므로 피격 섬광(HitFlash 의 MPB _BaseColor)과도 부딪히지 않는다(속성명이 _GlowColor).
// 가장자리(림)는 진하게, 면 전체는 _Fill 만큼 — 형태가 읽히면서 빛나 보이게.
Shader "Monster/AttackGlow"
{
    Properties
    {
        [HDR] _GlowColor ("Glow Color", Color) = (1, 0.45, 0, 1)
        _Fill ("Fill (면 전체 밝기 0~1)", Range(0, 1)) = 0.45
        _RimPower ("Rim Power (클수록 테두리만)", Range(0.5, 8)) = 2
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Back

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
                half _Fill;
                half _RimPower;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rim = pow(1.0h - saturate(dot(n, viewDir)), _RimPower);
                half k = saturate(_Fill + (1.0h - _Fill) * rim);
                return half4(_GlowColor.rgb * k * _GlowColor.a, 0);
            }
            ENDHLSL
        }
    }
}
