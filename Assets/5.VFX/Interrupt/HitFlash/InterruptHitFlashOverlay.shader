// ----------------------------------------------------------------------------
//  InterruptHitFlashOverlay.shader — 간파 적중 섬광 오너 화면 오버레이 (할 일 132 §15 · §16)
//
//  최상위 Screen Space Overlay 캔버스의 화면 전체 RawImage 에 쓴다(InterruptHitFlashOverlay). 오너 암전 덮개 위.
//  외부 이미지 없이 절차 생성(레퍼런스 영상 프레임은 쓰지 않는다). 섞기 = 미리 곱한 알파(알파 0 = 가산):
//    _Stage 2 ② 스타버스트 = 플레어 층 RT(_FlareTex, URP Lens Flare (SRP) 를 렌더 기능이 따로 그림)를 부드럽게 포화 + 얇고 흐린 푸른 고리가 퍼지며 사라짐 — 가산, 배경 암전 그대로
//    _Stage 3 ③ 화이트아웃 = 화면 전체 밝은 회청색(불투명도 칸, 덜 덮음) + 적중 지점 쪽으로 번진 오너·대상 실루엣 자리는 더 비쳐 보임(유일하게 화면을 덮는 단계)
//    _Stage 4 ④ 스미어     = 검정 바탕 위 암전 덮개 RT(_CoverTex)의 남김 영역(오너·대상·빔)을 키워(팽창) 공격 방향으로 길게 늘인 흰 덩어리
//    _Stage 5·6 ⑤⑥        = 그림 없음(실제 화면 — 블러·색수차·블룸은 렌더 기능·Volume). 런타임이 캔버스를 끈다.
//  중심 = 적중 지점 화면 위치(_Center, 뷰포트 0~1). 거리는 화면 높이 단위(_Aspect = 너비/높이).
//  ④ 방향 = 런타임이 넣는 _SmearAxis(공격 방향을 화면에 투영, z = 1 이면 유효) — 모르면 _SmearDirection 칸.
//  단계별 '덮어쓸 텍스처' 칸(_StarburstTex/_WhiteoutTex/_SmearTex)을 채우면 그 단계는 그 텍스처(화면 전체 UV, RGBA — ②는 가산).
//  키워드는 셰이더 GUI·런타임 사본이 칸에 맞춰 켠다(InterruptHitFlashRules.ApplyOverlayKeywords).
// ----------------------------------------------------------------------------
Shader "BeaverLobby/UI/InterruptHitFlashOverlay"
{
    Properties
    {
        [HideInInspector] _MainTex ("RawImage 텍스처(쓰지 않음)", 2D) = "white" {}
        _FlareGain ("② 플레어 층 배율 (부드러운 포화 전)", Range(0, 4)) = 1
        [HDR] _RingColor ("② 충격파 고리 색 (푸름)", Color) = (0.45, 0.72, 1.6, 1)
        _RingIntensity ("② 충격파 고리 세기", Range(0, 2)) = 0.6
        _RingWidth ("② 충격파 고리 두께 (화면 높이 비율, 퍼질 때 이 값의 3배까지 흐려진다)", Range(0.002, 0.05)) = 0.01
        _RingRadius ("② 충격파 고리 끝 반지름 (화면 높이 비율)", Range(0.05, 1)) = 0.42
        _WhiteoutColor ("③ 화이트아웃 색 (밝은 회청색)", Color) = (0.82, 0.88, 0.96, 1)
        _WhiteoutOpacity ("③ 화이트아웃 불투명도", Range(0, 1)) = 0.75
        _WhiteoutSilhouette ("③ 실루엣 비침 (남김 영역 자리에서 덜 덮는 비율)", Range(0, 1)) = 0.6
        _WhiteoutSilhouetteBlur ("③ 실루엣 방사형 번짐 (적중 지점까지 거리 비율)", Range(0, 1)) = 0.35
        _StreakCount ("③ 방사형 줄무늬 갈래 수", Range(24, 360)) = 160
        _SmearColor ("④ 스미어 덩어리 색 (흰)", Color) = (1, 1, 1, 1)
        _SmearBackColor ("④ 스미어 바탕 색 (검정)", Color) = (0, 0, 0, 1)
        _SmearDirection ("④ 기본 방향 (공격 방향을 모를 때, 화면 xy)", Vector) = (1, 0.12, 0, 0)
        _SmearLength ("④ 공격 방향 늘임 길이 (화면 높이 비율)", Range(0, 0.6)) = 0.22
        _SmearGrow ("④ 실루엣 키움 (팽창 반지름, 화면 높이 비율)", Range(0, 0.06)) = 0.018
        [NoScaleOffset] _StarburstTex ("덮어쓸 텍스처 — ② 스타버스트 (가산, 비우면 플레어 층 + 절차 고리)", 2D) = "black" {}
        [NoScaleOffset] _WhiteoutTex ("덮어쓸 텍스처 — ③ 화이트아웃 (비우면 절차 생성)", 2D) = "black" {}
        [NoScaleOffset] _SmearTex ("덮어쓸 텍스처 — ④ 스미어 (비우면 암전 남김 영역 방향 블러)", 2D) = "black" {}

        // 런타임이 매 프레임 넣는다(InterruptHitFlashOverlay).
        [HideInInspector] _Stage ("단계", Float) = 0
        [HideInInspector] _Progress ("단계 진행도", Float) = 0
        [HideInInspector] _Center ("중심(뷰포트)", Vector) = (0.5, 0.5, 0, 0)
        [HideInInspector] _Aspect ("화면 너비/높이", Float) = 1.7777
        [HideInInspector] _SmearAxis ("④ 공격 방향(화면 높이 단위 xy, z = 1 유효)", Vector) = (1, 0, 0, 0)
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
            float _RingIntensity;
            float _RingWidth;
            float _RingRadius;
            fixed4 _WhiteoutColor;
            float _WhiteoutOpacity;
            float _WhiteoutSilhouette;
            float _WhiteoutSilhouetteBlur;
            float _StreakCount;
            fixed4 _SmearColor;
            fixed4 _SmearBackColor;
            float4 _SmearDirection;
            float _SmearLength;
            float _SmearGrow;
            float _Stage;
            float _Progress;
            float4 _Center;
            float _Aspect;
            float4 _SmearAxis;

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
            #define SILHOUETTE_SAMPLES 8

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

            // 암전 남김(1 − 덮개 알파) — 오너·대상·빔 자리 = 1.
            float Keep(float2 uv)
            {
                return 1.0 - tex2D(_CoverTex, uv).a;
            }

            // ② — 가산(알파 0). 플레어 층을 부드럽게 포화(1 − e^−x: HDR 세기가 1 에서 잘려 딱딱한 원판이 되지 않게) + 얇고 흐린 푸른 고리가 퍼지며 사라짐.
            float4 Starburst(float2 uv, float r, float t)
            {
            #if defined(_STARBURST_TEX_ON)
                float4 o = tex2D(_StarburstTex, uv);
                return float4(o.rgb * o.a, 0.0);
            #else
                float3 flare = 1.0 - exp(-tex2D(_FlareTex, uv).rgb * _FlareGain);
                float spread = EaseOut(t);
                float ring = Ring(r, lerp(0.03, _RingRadius, spread), _RingWidth * lerp(1.0, 3.0, t));
                float fade = (1.0 - t) * (1.0 - t);
                return float4(flare + _RingColor.rgb * ring * _RingIntensity * fade, 0.0);
            #endif
            }

            // ③ — 화면을 덮는다(불투명도 칸). 적중 지점 쪽으로 모이는 줄무늬 + 중심 쪽 더 밝게.
            //     남김 영역을 적중 지점 쪽으로 번지게 읽어 그 자리만 덜 덮는다 — 아래(방사형 블러가 걸린 실제 화면의 오너·대상)가 흐린 실루엣으로 비친다.
            float4 Whiteout(float2 uv, float r, float a)
            {
            #if defined(_WHITEOUT_TEX_ON)
                float4 o = tex2D(_WhiteoutTex, uv);
                return float4(o.rgb * o.a, o.a);
            #else
                float n = max(_StreakCount, 1.0);
                float streak = Hash11(floor(a / TAU * n)) * saturate(r * 2.0);
                float3 rgb = _WhiteoutColor.rgb * (0.9 + 0.12 * streak) + exp(-r * 4.0) * 0.25;

                float2 toCenter = _Center.xy - uv;
                float silhouette = 0.0;
                [unroll]
                for (int i = 0; i < SILHOUETTE_SAMPLES; i++)
                {
                    float k = (float)i / (SILHOUETTE_SAMPLES - 1);
                    silhouette += Keep(uv + toCenter * (k * _WhiteoutSilhouetteBlur));
                }
                silhouette = saturate(silhouette / SILHOUETTE_SAMPLES * 1.5);

                float alpha = saturate(_WhiteoutOpacity) * (1.0 - silhouette * saturate(_WhiteoutSilhouette));
                return float4(rgb * alpha, alpha);
            #endif
            }

            // ④ — 덮는다. 남김 영역을 팽창(_SmearGrow)해 키우고 공격 방향(_SmearAxis)으로 늘인 흰 덩어리. 앞쪽(공격 방향)으로 길고 뒤로는 짧다.
            float4 Smear(float2 uv, float r)
            {
            #if defined(_SMEAR_TEX_ON)
                float4 o = tex2D(_SmearTex, uv);
                return float4(o.rgb * o.a, o.a);
            #else
                float2 dir = _SmearAxis.z > 0.5 ? _SmearAxis.xy : _SmearDirection.xy;
                dir = dot(dir, dir) > 1e-6 ? normalize(dir) : float2(1.0, 0.0);
                float2 toUv = float2(1.0 / max(_Aspect, 1e-4), 1.0);      // 화면 높이 단위 → UV
                float2 span = dir * _SmearLength * toUv;
                float2 grow = _SmearGrow * toUv;
                float keep = 0.0;
                [unroll]
                for (int i = 0; i < SMEAR_SAMPLES; i++)
                {
                    // 픽셀에서 공격 반대쪽을 읽는다 = 덩어리가 공격 방향으로 늘어난다. 뒤로 25%, 앞으로 100%.
                    float k = lerp(-0.25, 1.0, (float)i / (SMEAR_SAMPLES - 1));
                    float2 p = uv - span * k;
                    float dilated = max(max(Keep(p), Keep(p + float2(grow.x, 0.0))),
                                        max(max(Keep(p - float2(grow.x, 0.0)), Keep(p + float2(0.0, grow.y))), Keep(p - float2(0.0, grow.y))));
                    keep = max(keep, dilated * (1.0 - 0.55 * saturate(k)));
                }
                float beam = exp(-r * r * 60.0) * 0.7;   // 적중 지점의 빔 덩어리
                float3 rgb = lerp(_SmearBackColor.rgb, _SmearColor.rgb, saturate(keep * 1.4 + beam));
                return float4(rgb, 1.0);
            #endif
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

                return color * input.color.a;
            }
            ENDCG
        }
    }

    CustomEditor "InterruptHitFlashOverlayShaderGUI"
}
