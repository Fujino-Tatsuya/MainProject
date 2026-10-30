// 몸·지형에 가려지지 않는 월드 이펙트 쿼드(할 일 149 후속 6 — 잔류 포격 에너지 폭탄).
// 텍스처 × HDR 색 → 강도 > 1 이면 URP 블룸이 먹는다(투명 큐라 후처리 전에 그려진다).
// 🔴 ZTest Always(기본값) — 대상 몸 메시 안·뒤에 있어도 보인다. URP Unlit/Particles Unlit 은 ZTest 칸이 없어 이 셰이더를 따로 둔다.
//    대신 벽·지형 뒤에서도 보인다. 가려져야 하는 이펙트면 _ZTest 를 LessEqual(4)로.
// 빌보드는 정점에서 한다(컴포넌트 없음) — 쿼드(-0.5~0.5 XY)를 오브젝트 원점 기준으로 카메라를 향해 편다. 크기 = 트랜스폼 X/Y 스케일.
Shader "Effects/OverlayBillboard"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Color (HDR)", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5 // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend (One = 가산)", Float) = 10 // OneMinusSrcAlpha
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest (Always = 안 가려짐)", Float) = 8 // Always
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        ZTest [_ZTest]
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));
                float scaleX = length(UNITY_MATRIX_M._m00_m10_m20);
                float scaleY = length(UNITY_MATRIX_M._m01_m11_m21);
                float3 rightWS = UNITY_MATRIX_V._m00_m01_m02;
                float3 upWS = UNITY_MATRIX_V._m10_m11_m12;
                float3 positionWS = centerWS + rightWS * (v.positionOS.x * scaleX) + upWS * (v.positionOS.y * scaleY);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Color * i.color;
                return c;
            }
            ENDHLSL
        }
    }
}
