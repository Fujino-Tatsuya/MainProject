// 오버레이 RawImage 용 CRT(PRESS ANY KEY). 소형 RT(투명 배경에 흰 글자)를 모니터와 같은 룩으로 띄운다.
// 🔴 마스크 없는 전용 RawImage 전제 — 스텐실·RectMask2D 는 지원하지 않는다(부모에도 마스크 금지).
// 🔴 알파: RT 는 투명(0) 위에 알파 블렌드로 그려져 RGB 가 사실상 premultiplied 다. 알파를 다시 곱하지 않고,
//    출력 알파는 밝기(max rgb)로 만든다 → 글로우가 잘리지 않는다. 블렌드는 premultiplied(One, OneMinusSrcAlpha).
Shader "Title/CRTUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("RT", 2D) = "black" {}
        _ChromaPx ("Chroma (texel)", Range(0, 8)) = 2.5
        _ScanCount ("Scanlines", Float) = 90
        _ScanStrength ("Scan Strength", Range(0, 1)) = 0.35
        _Glow ("Glow", Range(0, 3)) = 0.9
        _GlowRadiusPx ("Glow Radius (texel)", Range(0, 12)) = 3
        _Grain ("Grain", Range(0, 0.3)) = 0.06
        _Jitter ("Line Jitter", Range(0, 0.02)) = 0.002
        _Brightness ("Brightness", Range(0, 3)) = 1.2
        _Vignette ("Vignette", Range(0, 2)) = 0
        _Pulse ("Brightness Pulse", Range(0, 1)) = 0.18
        _Tint ("Tint", Color) = (0.9, 0.97, 1.0, 1)
        _Glitch ("Glitch", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "TitleCRTCommon.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;
            float _Pulse;
            float4 _Tint;

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 px = _MainTex_TexelSize.xy;
                float2 uv = TitleCrtDistort(i.uv);

                float2 o = float2(_ChromaPx * (1.0 + _Glitch * 3.0), 0) * px;
                half3 col;
                col.r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + o).r;
                col.g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).g;
                col.b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - o).b;

                half3 glow = 0;
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float a = k * 0.785398;
                    glow += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(cos(a), sin(a)) * _GlowRadiusPx * px).rgb;
                }
                col += glow * (0.125 * _Glow);
                col *= _Tint.rgb;

                col *= TitleCrtScan(i.uv.y * _ScanCount);
                col *= 1.0 + (TitleHash21(i.uv * 512.0 + floor(_FxTime * 60.0)) - 0.5) * 2.0 * _Grain;
                col *= _Brightness * (1.0 - _Pulse * (0.5 + 0.5 * sin(_FxTime * 3.1)));
                col *= i.color.rgb * i.color.a; // RawImage 틴트·페이드 (premultiplied 이므로 rgb 에도 a 를 곱한다)

                half a = saturate(max(col.r, max(col.g, col.b)));
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
