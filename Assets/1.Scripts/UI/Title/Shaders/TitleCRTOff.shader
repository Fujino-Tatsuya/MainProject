// 전체 화면 CRT 꺼짐(레퍼런스 0:23~0:25). 꺼짐 시작 순간 캡처한 화면을 재생한다.
// 구간(총 1.1초, _Progress 0..1 에 선형 매핑):
//   0.00–0.08 가로 찢김 1회 → 0.08–0.32 둥근 배럴 실루엣 + 파란 발광 + 굵은 가로 밴드
//   → 0.32–0.72 XY 수축(테두리 지글지글) → 0.72–0.96 가로 띠로 접힘 → 0.96–1.10 발광까지 0
// 🔴 영역 밖은 불투명 검정(알파 1) — 투명이면 뒤의 오피스가 비친다.
Shader "Title/CRTOff"
{
    Properties
    {
        [PerRendererData] _MainTex ("Capture", 2D) = "black" {}
        _Progress ("Progress", Range(0, 1)) = 0
        _FlipY ("Flip Y", Float) = 0
        _GlowColor ("Glow", Color) = (0.25, 0.62, 1.0, 1)
        _FxTime ("Time", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One Zero

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float _Progress, _FlipY, _FxTime;
            float4 _GlowColor;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            float Hash(float p) { p = frac(p * 0.1031); p *= p + 33.33; p *= p + p; return frac(p); }
            float Seg(float t, float a, float b) { return saturate((t - a) / (b - a)); }
            float Ease(float x) { return x * x * (3.0 - 2.0 * x); }

            // 둥근 사각형 거리(음수 = 안쪽)
            float RoundBox(float2 p, float2 halfSize, float r)
            {
                float2 q = abs(p) - halfSize + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Progress * 1.10; // 초
                float2 uv = i.uv;

                // 크기(화면 대비 반폭·반높이)
                float sil = Ease(Seg(t, 0.08, 0.32));
                float shr = Ease(Seg(t, 0.32, 0.72));
                float fold = Ease(Seg(t, 0.72, 0.96));
                float fade = 1.0 - Seg(t, 0.96, 1.10);

                float hx = lerp(lerp(0.5, 0.45, sil), 0.30, shr);
                hx = lerp(hx, 0.27, fold);
                float hy = lerp(lerp(0.5, 0.43, sil), 0.23, shr);
                hy = lerp(hy, 0.004, fold);

                float2 p = uv - 0.5;
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 pa = float2(p.x * aspect, p.y);
                float2 hs = float2(hx * aspect, hy);

                // 테두리 지글지글 — 행마다 다른 노이즈로 가장자리를 들쭉날쭉하게
                float row = floor(uv.y * 160.0);
                float jag = (Hash(row + floor(_FxTime * 40.0) * 13.0) - 0.5) * 0.018 * sil * (1.0 - fold);
                float corner = lerp(0.0, 0.07, sil) * (1.0 - fold);
                float dist = RoundBox(pa + float2(jag, 0), hs, min(corner, min(hs.x, hs.y)));

                // 안쪽 UV: 축소된 화면에 전체 영상을 다시 배치 + 배럴
                float2 local = p / float2(hx * 2.0, hy * 2.0);
                float r2 = dot(local, local);
                local *= 1.0 + 0.18 * sil * r2;
                float2 suv = local + 0.5;

                // 찢김 1회(0–0.08) + 꺼짐 동안 굵은 가로 밴드
                float tear = Seg(t, 0.0, 0.04) * (1.0 - Seg(t, 0.04, 0.08));
                float band = step(abs(uv.y - 0.58), 0.05);
                suv.x += tear * band * 0.06;

                if (_FlipY > 0.5) suv.y = 1.0 - suv.y;
                half3 img = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, saturate(suv)).rgb;

                // 파랗게 물들며 밝아진다
                float blue = saturate(sil * 0.8 + shr * 0.6 + fold);
                half3 tint = lerp(img, (img * 0.5 + 0.5 * dot(img, half3(0.3, 0.59, 0.11))) * _GlowColor.rgb * 1.6, blue);
                float bands = 1.0 + 0.35 * sil * sin(uv.y * 38.0 - _FxTime * 9.0);
                tint *= bands;
                tint = lerp(tint, _GlowColor.rgb * 2.2, fold * 0.85); // 접힐수록 가로 띠는 발광만 남는다

                float inside = saturate(-dist * 900.0 + 0.5);
                half3 col = tint * inside;

                // 바깥 파란 글로우(거리장) — 블러 없이
                float glow = exp(-max(dist, 0.0) * lerp(60.0, 25.0, fold)) * (0.25 * sil + 0.9 * fold);
                col += _GlowColor.rgb * glow * (1.0 - inside);

                col *= fade;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
