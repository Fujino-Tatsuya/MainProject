// 중앙 모니터 화면(메시) — UI 렌더텍스처를 CRT 룩으로 보여 준다.
// 불투명 Unlit. TitleMonitorDisplay 가 런타임 인스턴스를 만들어 _MainTex = UI RT 를 넣는다.
Shader "Title/CRTScreen"
{
    Properties
    {
        _MainTex ("UI RT", 2D) = "black" {}
        _ChromaPx ("Chroma (texel)", Range(0, 8)) = 2
        _ScanCount ("Scanlines", Float) = 300
        _ScanStrength ("Scan Strength", Range(0, 1)) = 0.28
        _Glow ("Glow", Range(0, 2)) = 0.55
        _GlowRadiusPx ("Glow Radius (texel)", Range(0, 12)) = 4
        _Grain ("Grain", Range(0, 0.3)) = 0.05
        _Jitter ("Line Jitter", Range(0, 0.01)) = 0.0012
        _Brightness ("Brightness", Range(0, 3)) = 1.25
        _Vignette ("Vignette", Range(0, 2)) = 0.6
        _Tint ("Tint", Color) = (0.86, 0.96, 1.0, 1)
        _Glitch ("Glitch", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "TitleCRTCommon.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float4 _Tint;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 rawUv : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.rawUv = v.uv;
                return o;
            }

            half3 SampleChroma(float2 uv, float2 px)
            {
                float2 o = float2(_ChromaPx * (1.0 + _Glitch * 3.0), 0) * px;
                half r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + o).r;
                half g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).g;
                half b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - o).b;
                return half3(r, g, b);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 px = _MainTex_TexelSize.xy;
                float2 uv = TitleCrtDistort(i.uv);

                half3 col = SampleChroma(uv, px);

                // 좁은 글로우 — 8탭 링. 넓은 파란 글로우는 꺼짐 셰이더가 거리장으로 따로 그린다.
                half3 glow = 0;
                float r = _GlowRadiusPx;
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float a = k * 0.785398;
                    glow += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(cos(a), sin(a)) * r * px).rgb;
                }
                col += glow * (0.125 * _Glow);

                col *= _Tint.rgb;

                col *= TitleCrtScan(i.rawUv.y * _ScanCount);
                col *= 1.0 + (TitleHash21(i.rawUv * 1024.0 + floor(_FxTime * 60.0)) - 0.5) * 2.0 * _Grain;
                col *= TitleCrtVignette(i.rawUv) * _Brightness;
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            float4 vert(Attributes v) : SV_POSITION { return TransformObjectToHClip(v.positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
