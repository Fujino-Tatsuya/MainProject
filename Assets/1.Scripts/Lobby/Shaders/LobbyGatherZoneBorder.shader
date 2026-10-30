// 로비 집결 구역 민트 테두리(할 일 158, 명세 4.2) — 바닥에 눕힌 내장 Quad 하나가 구역 footprint 의 직사각 테두리만 그린다(내부 투명).
// 크기·테두리 폭은 월드 m — LobbyGatherZoneBorders 가 MaterialPropertyBlock(_Size·_BorderWidth)으로 넣는다. 텍스처 없음.
Shader "Lobby/GatherZoneBorder"
{
    Properties
    {
        [HDR] _BaseColor ("Color", Color) = (0.35, 1, 0.82, 0.9)
        _Size ("Quad size (m, x = width, y = depth)", Vector) = (4, 4, 0, 0)
        _BorderWidth ("Border width (m)", Float) = 0.18
        _InnerGlow ("Inner glow width (m)", Float) = 0.25
        _InnerGlowAlpha ("Inner glow alpha", Range(0, 1)) = 0.18
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

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _Size;
                float _BorderWidth;
                float _InnerGlow;
                float _InnerGlowAlpha;
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
                // 가장 가까운 변까지 거리(m).
                float2 size = max(_Size.xy, 0.01);
                float2 m = i.uv * size;
                float2 edge = min(m, size - m);
                float d = min(edge.x, edge.y);

                // 테두리 = 폭 안쪽 꽉 참(경계는 화면 픽셀 1개로 부드럽게), 그 안쪽은 옅게 사라지는 띠.
                float aa = max(fwidth(d), 1e-4);
                float border = 1.0 - smoothstep(_BorderWidth - aa, _BorderWidth + aa, d);
                float glow = (1.0 - saturate((d - _BorderWidth) / max(_InnerGlow, 1e-3))) * _InnerGlowAlpha;

                half4 c = _BaseColor;
                c.a *= saturate(max(border, glow));
                return c;
            }
            ENDHLSL
        }
    }
}
