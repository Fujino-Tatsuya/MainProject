// 로비 벽 활용 표식(할 일 158, 명세 4.3) — 지정 벽면에 붙인 얇은 판(큐브)에 빛나는 금색 테두리 + 낮은 밝기의 사선 경고 줄무늬.
// 판의 가장 얇은 축을 두께로 보고 나머지 두 축(m)으로 테두리·줄무늬를 계산한다 — 오브젝트 스케일만 바뀌어도 폭·간격이 m 단위로 유지된다.
// 텍스처 없음. MA_Train03DesignatedWall.mat 이 쓴다(머티리얼 guid 유지, 셰이더·값만 교체).
Shader "Lobby/WallTargetMarker"
{
    Properties
    {
        [HDR] _BorderColor ("Border color", Color) = (1.6, 1.2, 0.45, 1)
        _StripeColor ("Stripe color", Color) = (0.89, 0.72, 0.35, 0.35)
        _FillAlpha ("Fill alpha (between stripes)", Range(0, 1)) = 0.06
        _BorderWidth ("Border width (m)", Float) = 0.12
        _StripeSpacing ("Stripe spacing (m)", Float) = 0.5
        _StripeDuty ("Stripe width ratio", Range(0.05, 0.95)) = 0.45
        _PulseSpeed ("Border pulse speed (Hz)", Float) = 0.8
        _PulseMin ("Border pulse min", Range(0, 1)) = 0.7
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BorderColor;
                half4 _StripeColor;
                float _FillAlpha;
                float _BorderWidth;
                float _StripeSpacing;
                float _StripeDuty;
                float _PulseSpeed;
                float _PulseMin;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 planeM : TEXCOORD0;   // 판 평면 좌표(m, 중심 0)
                float2 halfM : TEXCOORD1;    // 판 반폭(m)
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);

                float3 s = float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21), length(UNITY_MATRIX_M._m02_m12_m22));
                float3 m = v.positionOS.xyz * s;
                float3 h = 0.5 * s;

                // 가장 얇은 축(두께)을 버리고 나머지 두 축을 평면으로.
                if (s.x <= s.y && s.x <= s.z)      { o.planeM = m.zy; o.halfM = h.zy; }
                else if (s.y <= s.x && s.y <= s.z) { o.planeM = m.xz; o.halfM = h.xz; }
                else                               { o.planeM = m.xy; o.halfM = h.xy; }
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 edge = i.halfM - abs(i.planeM);
                float d = min(edge.x, edge.y);
                float aa = max(fwidth(d), 1e-4);
                float border = 1.0 - smoothstep(_BorderWidth - aa, _BorderWidth + aa, d);

                float t = frac((i.planeM.x + i.planeM.y) / max(_StripeSpacing, 0.01));
                float taa = max(fwidth(t), 1e-4);
                float stripe = smoothstep(0.0, taa, t) * (1.0 - smoothstep(_StripeDuty - taa, _StripeDuty, t));

                float pulse = lerp(_PulseMin, 1.0, 0.5 + 0.5 * sin(_Time.y * _PulseSpeed * TWO_PI));

                half4 inner = lerp(half4(_StripeColor.rgb, _FillAlpha), _StripeColor, stripe);
                half4 rim = half4(_BorderColor.rgb * pulse, _BorderColor.a);
                return lerp(inner, rim, border);
            }
            ENDHLSL
        }
    }
}
