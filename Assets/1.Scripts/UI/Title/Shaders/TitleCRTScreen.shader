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
        [Header(TV Roll (wall monitors only. speed and timing live in TitleScreenFeed))]
        _Roll ("Sync Bar Strength", Range(0, 1)) = 0
        _RollWidth ("Sync Bar Height", Range(0.02, 0.5)) = 0.14
        _Flicker ("Glitch Visual Strength", Range(0, 1)) = 0
        _SeamBlend ("Seam Blend", Range(0.05, 1)) = 0.4
        [Header(Power for center monitor start dive. 1 is on)]
        _Power ("Power", Range(0, 1)) = 1
        _ScreenAspect ("Screen Aspect (w/h)", Float) = 1.333
        _OffGlowColor ("Power Off Glow", Color) = (0.25, 0.62, 1.0, 1)
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
            float _Roll, _RollWidth, _Flicker, _SeamBlend;
            float _Power;      // 1 = 켜짐. 1→0 = CRT 꺼짐(Title/CRTOff 와 같은 연출을 화면 메시 안에서). TitleMonitorDisplay.PowerOff 가 구동
            float _ScreenAspect;
            float4 _OffGlowColor;

            float OffHash(float p) { p = frac(p * 0.1031); p *= p + 33.33; p *= p + p; return frac(p); }
            float OffSeg(float t, float a, float b) { return saturate((t - a) / (b - a)); }
            float OffEase(float x) { return x * x * (3.0 - 2.0 * x); }
            float OffRoundBox(float2 p, float2 halfSize, float r)
            {
                float2 q = abs(p) - halfSize + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            // 🔴 Title/CRTOff(전체 화면 꺼짐, 팀장 09-23 연출)의 수식을 그대로 옮겼다 — 화면 비율만 _ScreenParams 대신 화면 메시 비율.
            //    팀장 10-03: "전체 화면을 끄지 말고 모니터 객체의 화면만 처음 작업한 끄는 연출로".
            half3 PowerOffColor(float2 uv, float progress)
            {
                float t = progress * 1.10;
                float sil = OffEase(OffSeg(t, 0.08, 0.32));
                float shr = OffEase(OffSeg(t, 0.32, 0.72));
                float fold = OffEase(OffSeg(t, 0.72, 0.96));
                float fade = 1.0 - OffSeg(t, 0.96, 1.10);

                float hx = lerp(lerp(0.5, 0.45, sil), 0.30, shr);
                hx = lerp(hx, 0.27, fold);
                float hy = lerp(lerp(0.5, 0.43, sil), 0.23, shr);
                hy = lerp(hy, 0.004, fold);

                float2 p = uv - 0.5;
                float aspect = max(0.1, _ScreenAspect);
                float2 pa = float2(p.x * aspect, p.y);
                float2 hs = float2(hx * aspect, hy);

                float row = floor(uv.y * 160.0);
                float jag = (OffHash(row + floor(_FxTime * 40.0) * 13.0) - 0.5) * 0.018 * sil * (1.0 - fold);
                float corner = lerp(0.0, 0.07, sil) * (1.0 - fold);
                float dist = OffRoundBox(pa + float2(jag, 0), hs, min(corner, min(hs.x, hs.y)));

                float2 local = p / float2(hx * 2.0, hy * 2.0);
                float r2 = dot(local, local);
                local *= 1.0 + 0.18 * sil * r2;
                float2 suv = local + 0.5;

                float tear = OffSeg(t, 0.0, 0.04) * (1.0 - OffSeg(t, 0.04, 0.08));
                float band = step(abs(uv.y - 0.58), 0.05);
                suv.x += tear * band * 0.06;

                suv = saturate(suv) * _MainTex_ST.xy + _MainTex_ST.zw;   // 머티리얼 뒤집기(UV 보정) 반영
                half3 img = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, suv).rgb * _Tint.rgb * _Brightness;

                float blue = saturate(sil * 0.8 + shr * 0.6 + fold);
                half3 tint = lerp(img, (img * 0.5 + 0.5 * dot(img, half3(0.3, 0.59, 0.11))) * _OffGlowColor.rgb * 1.6, blue);
                float bands = 1.0 + 0.35 * sil * sin(uv.y * 38.0 - _FxTime * 9.0);
                tint *= bands;
                tint = lerp(tint, _OffGlowColor.rgb * 2.2, fold * 0.85);

                float inside = saturate(-dist * 900.0 + 0.5);
                half3 col = tint * inside;
                float glow = exp(-max(dist, 0.0) * lerp(60.0, 25.0, fold)) * (0.25 * sil + 0.9 * fold);
                col += _OffGlowColor.rgb * glow * (1.0 - inside);
                return col * fade;
            }
            // 🔴 모니터마다 다른 값 — TitleScreenFeed 가 MaterialPropertyBlock 으로 넣는다(머티리얼 2장을 13대가 공유).
            float _WallRoll;   // 누적 세로 흐름(0..1, 감김). 속도 변화·미끄러짐은 C# 이 적분한다
            float _WallGlitch; // 이 모니터의 지직 세기 0..1

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

            half3 SampleChroma(float2 uv, float2 px, float extra)
            {
                float2 o = float2(_ChromaPx * (1.0 + _Glitch * 3.0 + extra), 0) * px;
                half r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + o).r;
                half g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).g;
                half b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - o).b;
                return half3(r, g, b);
            }

            // 위아래가 안 이어지는 그림을 감아도 끊긴 줄이 안 보이게 — 이음새 근처만 반 화면 떨어진 같은 그림과 섞는다.
            // 섞는 쪽(B)의 이음새는 f = 0.5 에 있고 그곳의 가중치는 0 이라 어디에도 불연속이 없다.
            half3 SampleSeamless(float2 uv, float2 px, float extra)
            {
                float w = smoothstep(1.0 - _SeamBlend, 1.0, abs(2.0 * uv.y - 1.0));
                half3 a = SampleChroma(uv, px, extra);
                half3 b = SampleChroma(float2(uv.x, frac(uv.y + 0.5)), px, extra);
                return lerp(a, b, w);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 px = _MainTex_TexelSize.xy;

                // ── 전원 꺼짐(월드 화면판). UI용 TitleCRTOff 는 투명·ZWrite Off 라 여기선 못 쓴다 —
                //    같은 수식(PowerOffColor)을 불투명·깊이 기록 그대로 화면 메시 안에서 그린다. 끝 = 검정.
                //    꺼지는 동안에도 노이즈(지터·찢김·글리치 + 스캔라인·그레인)는 켜진 화면과 같게 얹는다(은희 10-07).
                if (_Power < 0.999)
                {
                    half3 off = PowerOffColor(TitleCrtDistort(i.rawUv), 1.0 - saturate(_Power));
                    off *= TitleCrtScan(i.rawUv.y * _ScanCount);
                    off *= 1.0 + (TitleHash21(i.rawUv * 1024.0 + floor(_FxTime * 60.0)) - 0.5) * 2.0 * _Grain;
                    return half4(off, 1);
                }

                float2 uv = TitleCrtDistort(i.uv);

                // ── 벽 모니터 TV 연출(팀장 09-28 2판). _Roll = _Flicker = 0 이면 중앙 모니터와 동일.
                //  흐름: 그림 자체가 세로로 계속 감긴다(수직 동기 풀림). 이음새엔 검은 동기 띠가 따라간다.
                //  지직: 가로 블록 찢김 + 색 번짐.
                //  상호작용: ① 동기 띠 근처 줄은 가로로 끌려 휜다 ② 지직은 띠 근처에서 더 세다
                //           ③ (C#) 띠가 화면 중앙을 지날 때 지직 확률↑, 지직이 터지면 흐름이 미끄러지고 잠깐 빨라진다.
                bool wall = _Roll > 0.0 || _Flicker > 0.0;
                float f = frac(uv.y + _WallRoll);                  // 감긴 텍스처 좌표
                float dT = f - round(f);                           // 이음새(동기 띠)까지 부호 있는 거리 — 텍스처 기준이라 메시 UV 범위와 무관
                float nd = dT / _RollWidth;
                float bar = exp(-nd * nd * 2.5);                   // 🔴 가우시안 — 사각 띠는 가장자리가 경계선처럼 보였다
                float core = exp(-nd * nd * 10.0);

                float g = saturate(_WallGlitch * _Flicker * 2.0) * (1.0 + 1.5 * bar);

                // ① 동기 띠에 끌려 휘는 줄 — 부호 대신 연속 함수(nd·가우시안). sign() 은 띠 중심에서 가로로 딱 끊겼다
                uv.x += _Roll * (0.05 * nd * exp(-nd * nd * 1.5) +
                                 bar * (TitleHash21(float2(floor(i.rawUv.y * 160.0), floor(_FxTime * 24.0))) - 0.5) * 0.012);

                // 지직 — 가로 블록 단위로 튄다
                float key = floor(_FxTime * 20.0);
                float blk = floor(i.rawUv.y * 28.0);
                float on = step(1.0 - saturate(g) * 0.6, TitleHash21(float2(blk, key)));
                uv.x += on * (TitleHash21(float2(blk + 7.0, key)) - 0.5) * 0.16 * saturate(g);

                // 흐름 — 그림만 감기고 스캔라인·비네트는 화면(rawUv)에 남는다
                if (wall) uv.y = f;

                half3 col = wall ? SampleSeamless(uv, px, g * 4.0) : SampleChroma(uv, px, 0.0);

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
                col *= 1.0 - core * _Roll * 0.6;                           // 동기 띠(부드럽게 어두워진다)
                col *= 1.0 + (bar - core) * _Roll * 0.35;                  // 띠 가장자리 번짐
                col *= 1.0 - saturate(g) * 0.3 * TitleHash11(key * 3.1);   // 지직 순간 밝기 요동
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
