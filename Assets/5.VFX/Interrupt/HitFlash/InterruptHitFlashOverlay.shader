// ----------------------------------------------------------------------------
//  InterruptHitFlashOverlay.shader — 간파 적중 섬광 오너 화면 오버레이 (할 일 132 §15)
//
//  최상위 Screen Space Overlay 캔버스의 화면 전체 RawImage 에 쓴다(InterruptHitFlashOverlay). 오너 암전 덮개 위.
//  외부 이미지 없이 절차 생성(레퍼런스 영상 프레임은 쓰지 않는다). 섞기 = 미리 곱한 알파(알파 0 = 가산):
//    _Stage 2 ② 스타버스트 = 플레어 층 RT(_FlareTex, URP Lens Flare (SRP) 를 렌더 기능이 따로 그림) + 푸른 충격파 고리 — 가산, 배경 암전 그대로
//    _Stage 3 ③ 화이트아웃 = 화면 전체 밝은 회청색 + 적중 지점 쪽으로 모이는 방사형 줄무늬(유일하게 화면을 덮는 단계)
//    _Stage 4 ④ 스미어     = 검정 바탕 위 암전 덮개 RT(_CoverTex)의 남김 영역(오너·대상·빔)을 흰색으로 방향 블러
//    _Stage 5 ⑤ 색수차 블러 = 그림 없음(실제 화면 — 블러·색수차·블룸은 렌더 기능·Volume)
//    _Stage 6 ⑥ 가라앉음   = 무지개빛 충격파 고리 퍼짐 — 가산
//  중심 = 적중 지점 화면 위치(_Center, 뷰포트 0~1). 거리는 화면 높이 단위(_Aspect = 너비/높이).
//  단계별 '덮어쓸 텍스처' 칸(_StarburstTex/_WhiteoutTex/_SmearTex)을 채우면 그 단계는 그 텍스처(화면 전체 UV, RGBA — ②는 가산).
//  키워드는 셰이더 GUI·런타임 사본이 칸에 맞춰 켠다(InterruptHitFlashRules.ApplyOverlayKeywords).
// ----------------------------------------------------------------------------
Shader "BeaverLobby/UI/InterruptHitFlashOverlay"
{
    Properties
    {
        [HideInInspector] _MainTex ("RawImage 텍스처(쓰지 않음)", 2D) = "white" {}
        _FlareGain ("② 플레어 층 배율", Range(0, 4)) = 1
        [HDR] _RingColor ("② 충격파 고리 색 (푸름)", Color) = (0.45, 0.72, 1.6, 1)
        _WhiteoutColor ("③ 화이트아웃 색 (밝은 회청색)", Color) = (0.82, 0.88, 0.96, 1)
        _WhiteoutOpacity ("③ 화이트아웃 불투명도", Range(0, 1)) = 0.94
        _StreakCount ("③ 방사형 줄무늬 갈래 수", Range(24, 360)) = 160
        _SmearColor ("④ 스미어 덩어리 색 (흰)", Color) = (1, 1, 1, 1)
        _SmearBackColor ("④ 스미어 바탕 색 (검정)", Color) = (0, 0, 0, 1)
        _SmearDirection ("④ 방향 블러 방향 (화면 xy)", Vector) = (1, 0.12, 0, 0)
        _SmearLength ("④ 방향 블러 길이 (화면 높이 비율)", Range(0, 0.4)) = 0.12
        _RainbowIntensity ("⑥ 무지개 고리 세기", Range(0, 4)) = 1.2
        [NoScaleOffset] _StarburstTex ("덮어쓸 텍스처 — ② 스타버스트 (가산, 비우면 플레어 층 + 절차 고리)", 2D) = "black" {}
        [NoScaleOffset] _WhiteoutTex ("덮어쓸 텍스처 — ③ 화이트아웃 (비우면 절차 생성)", 2D) = "black" {}
        [NoScaleOffset] _SmearTex ("덮어쓸 텍스처 — ④ 스미어 (비우면 암전 남김 영역 방향 블러)", 2D) = "black" {}

        // 런타임이 매 프레임 넣는다(InterruptHitFlashOverlay).
        [HideInInspector] _Stage ("단계", Float) = 0
        [HideInInspector] _Progress ("단계 진행도", Float) = 0
        [HideInInspector] _Center ("중심(뷰포트)", Vector) = (0.5, 0.5, 0, 0)
        [HideInInspector] _Aspect ("화면 너비/높이", Float) = 1.7777
        [HideInInspector] _FlareTex ("플레어 층 RT", 2D) = "black" {}
        [HideInInspector] _CoverTex ("오너 암전 덮개 RT (알파 = 1 − 남김)", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "False"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha

        Pass
        {
            Name "Default"

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma shader_feature_local_fragment _ _STARBURST_TEX_ON
            #pragma shader_feature_local_fragment _ _WHITEOUT_TEX_ON
            #pragma shader_feature_local_fragment _ _SMEAR_TEX_ON

            #include "UnityCG.cginc"

            sampler2D _StarburstTex;
            sampler2D _WhiteoutTex;
            sampler2D _SmearTex;
            sampler2D _FlareTex;
            sampler2D _CoverTex;
            float _FlareGain;
            float4 _RingColor;
            fixed4 _WhiteoutColor;
            float _WhiteoutOpacity;
            float _StreakCount;
            fixed4 _SmearColor;
            fixed4 _SmearBackColor;
            float4 _SmearDirection;
            float _SmearLength;
            float _RainbowIntensity;
            float _Stage;
            float _Progress;
            float4 _Center;
            float _Aspect;

            struct Attributes
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            static const float TAU = 6.28318530718;
            #define SMEAR_SAMPLES 12

            float Hash11(float n)
            {
                return frac(sin(n * 127.1) * 43758.5453);
            }

            float EaseOut(float t)
            {
                float k = 1.0 - saturate(t);
                return 1.0 - k * k * k;
            }

            float Ring(float r, float radius, float width)
            {
                float d = (r - radius) / max(width, 1e-4);
                return exp(-d * d);
            }

            float3 Hue(float h)
            {
                return saturate(abs(frac(h + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
            }

            // ② — 가산(알파 0). 플레어 층 + 퍼지는 푸른 고리.
            float4 Starburst(float2 uv, float r, float t)
            {
            #if defined(_STARBURST_TEX_ON)
                float4 o = tex2D(_StarburstTex, uv);
                return float4(o.rgb * o.a, 0.0);
            #else
                float3 flare = tex2D(_FlareTex, uv).rgb * _FlareGain;
                float ring = Ring(r, lerp(0.04, 0.34, EaseOut(t)), lerp(0.012, 0.03, t)) * (1.0 - 0.5 * t);
                return float4(flare + _RingColor.rgb * ring, 0.0);
            #endif
            }

            // ③ — 화면을 덮는다. 적중 지점 쪽으로 모이는 줄무늬(방사형 블러 느낌) + 중심 쪽 더 밝게.
            float4 Whiteout(float2 uv, float r, float a)
            {
            #if defined(_WHITEOUT_TEX_ON)
                float4 o = tex2D(_WhiteoutTex, uv);
                return float4(o.rgb * o.a, o.a);
            #else
                float n = max(_StreakCount, 1.0);
                float streak = Hash11(floor(a / TAU * n)) * saturate(r * 2.0);
                float3 rgb = _WhiteoutColor.rgb * (0.9 + 0.12 * streak) + exp(-r * 4.0) * 0.25;
                float alpha = saturate(_WhiteoutOpacity);
                return float4(rgb * alpha, alpha);
            #endif
            }

            // ④ — 덮는다. 암전 남김 영역(1 − 덮개 알파)을 방향을 따라 여러 번 읽어 흰 덩어리로 번지게.
            float4 Smear(float2 uv, float r)
            {
            #if defined(_SMEAR_TEX_ON)
                float4 o = tex2D(_SmearTex, uv);
                return float4(o.rgb * o.a, o.a);
            #else
                float2 dir = _SmearDirection.xy;
                dir = dot(dir, dir) > 1e-6 ? normalize(dir) : float2(1.0, 0.0);
                float2 span = dir * _SmearLength * float2(1.0 / max(_Aspect, 1e-4), 1.0);
                float keep = 0.0;
                [unroll]
                for (int i = 0; i < SMEAR_SAMPLES; i++)
                {
                    float k = (float)i / (SMEAR_SAMPLES - 1) - 0.5;
                    keep += 1.0 - tex2D(_CoverTex, uv + span * k).a;
                }
                keep = saturate(keep / SMEAR_SAMPLES * 1.8);
                float beam = exp(-r * r * 60.0) * 0.7;   // 적중 지점의 빔 덩어리
                float3 rgb = lerp(_SmearBackColor.rgb, _SmearColor.rgb, saturate(keep + beam));
                return float4(rgb, 1.0);
            #endif
            }

            // ⑥ — 가산. 반지름 방향으로 색이 바뀌는 무지개 고리가 퍼지며 옅어진다.
            float4 Settle(float r, float t)
            {
                float radius = lerp(0.1, 0.9, EaseOut(t));
                float width = lerp(0.03, 0.08, t);
                float band = (r - radius) / width;
                float ring = exp(-band * band);
                float3 rainbow = Hue(saturate(band * 0.25 + 0.5) * 0.8);
                return float4(rainbow * ring * _RainbowIntensity * (1.0 - t), 0.0);
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float2 p = (uv - _Center.xy) * float2(_Aspect, 1.0);
                float r = length(p);
                float a = atan2(p.y, p.x) + 3.14159265359;   // 0 ~ 2π
                float t = saturate(_Progress);
                int stage = (int)round(_Stage);

                float4 color = float4(0, 0, 0, 0);
                if (stage == 2)
                    color = Starburst(uv, r, t);
                else if (stage == 3)
                    color = Whiteout(uv, r, a);
                else if (stage == 4)
                    color = Smear(uv, r);
                else if (stage == 6)
                    color = Settle(r, t);

                return color * input.color.a;
            }
            ENDCG
        }
    }

    CustomEditor "InterruptHitFlashOverlayShaderGUI"
}
