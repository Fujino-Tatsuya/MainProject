// ----------------------------------------------------------------------------
//  InterruptHitFlashOverlay.shader — 간파 적중 섬광 오너 화면 오버레이 (할 일 132 단계 5)
//
//  최상위 Screen Space Overlay 캔버스의 화면 전체 RawImage 에 쓴다(InterruptHitFlashOverlay). 외부 이미지 없이 절차 생성:
//    _Stage 1 = 검은 배경 위 흰 폭발(가시 돋친 가장자리 + 번짐, 진행도에 따라 커진다)
//    _Stage 2 = 검은 배경 위 빛 갈라짐(스타버스트 광선 + 뜨거운 중심) + 퍼지는 원형 충격파 링
//    _Stage 3 = 화면 전체 흰색 반전 + 검은 집중선(_SpeedLines)
//    _Stage 4 = 3단계 그림이 걷힌다(실제 화면 복귀)
//  중심 = 적중 지점 화면 위치(_Center, 뷰포트 0~1). 거리는 화면 높이 단위(_Aspect = 너비/높이).
//  단계별 '덮어쓸 텍스처' 칸(_BurstTex/_GlareTex/_InvertTex)을 채우면 그 단계는 그 텍스처(화면 전체 UV, RGBA 그대로).
//  키워드는 셰이더 GUI·런타임 사본이 칸에 맞춰 켠다(InterruptHitFlashRules.ApplyOverlayKeywords).
// ----------------------------------------------------------------------------
Shader "BeaverLobby/UI/InterruptHitFlashOverlay"
{
    Properties
    {
        [HideInInspector] _MainTex ("RawImage 텍스처(쓰지 않음)", 2D) = "white" {}
        _FlashColor ("섬광 색 (흰 폭발·광선·반전 바탕)", Color) = (1, 1, 1, 1)
        _BackColor ("배경 색 (1·2단계 바탕)", Color) = (0, 0, 0, 1)
        _LineColor ("집중선 색 (3단계)", Color) = (0, 0, 0, 1)
        _RayCount ("광선 갈래 수 (2단계)", Range(4, 24)) = 12
        _LineDensity ("집중선 촘촘함 (3단계, 바퀴당 칸)", Range(20, 240)) = 120
        [NoScaleOffset] _BurstTex ("덮어쓸 텍스처 — 1단계 폭발 (비우면 절차 생성)", 2D) = "black" {}
        [NoScaleOffset] _GlareTex ("덮어쓸 텍스처 — 2단계 빛 갈라짐 (비우면 절차 생성)", 2D) = "black" {}
        [NoScaleOffset] _InvertTex ("덮어쓸 텍스처 — 3·4단계 흰 반전 (비우면 절차 생성)", 2D) = "black" {}

        // 런타임이 매 프레임 넣는다(InterruptHitFlashOverlay).
        [HideInInspector] _Stage ("단계", Float) = 0
        [HideInInspector] _Progress ("단계 진행도", Float) = 0
        [HideInInspector] _Center ("중심(뷰포트)", Vector) = (0.5, 0.5, 0, 0)
        [HideInInspector] _Aspect ("화면 너비/높이", Float) = 1.7777
        [HideInInspector] _SpeedLines ("집중선", Float) = 1
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
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Default"

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma shader_feature_local_fragment _ _BURST_TEX_ON
            #pragma shader_feature_local_fragment _ _GLARE_TEX_ON
            #pragma shader_feature_local_fragment _ _INVERT_TEX_ON

            #include "UnityCG.cginc"

            sampler2D _BurstTex;
            sampler2D _GlareTex;
            sampler2D _InvertTex;
            fixed4 _FlashColor;
            fixed4 _BackColor;
            fixed4 _LineColor;
            float _RayCount;
            float _LineDensity;
            float _Stage;
            float _Progress;
            float4 _Center;
            float _Aspect;
            float _SpeedLines;

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

            float Hash11(float n)
            {
                return frac(sin(n * 127.1) * 43758.5453);
            }

            float EaseOut(float t)
            {
                float k = 1.0 - saturate(t);
                return 1.0 - k * k * k;
            }

            // 1단계 — 흰 폭발. 0 = 배경, 1 = 섬광.
            float BurstPattern(float r, float a, float t)
            {
                float radius = lerp(0.05, 0.42, EaseOut(t));
                float spikes = 0.5 + 0.5 * sin(a * 9.0 + 1.3);
                spikes = 0.55 * pow(spikes, 6.0) + 0.25 * (0.5 + 0.5 * sin(a * 17.0 + 4.1)) + 0.2 * (0.5 + 0.5 * sin(a * 31.0 + 2.7));
                float edge = radius * (0.75 + 0.55 * spikes);
                float body = 1.0 - smoothstep(edge * 0.82, edge, r);
                float glow = exp(-(r * r) / max(radius * radius * 0.9, 1e-4)) * 0.7;
                return saturate(body + glow);
            }

            // 2단계 — 스타버스트 광선 + 뜨거운 중심 + 퍼지는 링.
            float GlarePattern(float r, float a, float t)
            {
                float n = max(_RayCount, 1.0);
                float sector = floor(a / TAU * n + 0.5);
                float lengthJitter = lerp(0.55, 1.0, Hash11(sector + 3.7));
                float mainRays = pow(abs(cos(a * n * 0.5)), 80.0);
                float thinRays = pow(abs(cos(a * n + 0.9)), 220.0) * 0.6;
                float reach = lerp(5.0, 2.2, EaseOut(t)) / lengthJitter;
                float rays = (mainRays + thinRays) * exp(-r * reach) * (1.0 - 0.35 * t);

                float core = exp(-r * r * 140.0) + exp(-r * 9.0) * 0.45;

                float ringRadius = lerp(0.06, 0.85, EaseOut(t));
                float ringWidth = lerp(0.015, 0.05, t);
                float ring = exp(-pow((r - ringRadius) / ringWidth, 2.0)) * (1.0 - 0.7 * t);

                return saturate(core + rays + ring);
            }

            // 3단계 — 검은 집중선 마스크(1 = 선). 적중 지점 쪽은 비운다.
            float SpeedLinePattern(float r, float a, float t)
            {
                float k = max(_LineDensity, 1.0);
                float cell = a / TAU * k;
                float id = floor(cell);
                float h = Hash11(id + 11.3);
                float present = step(0.42, h);
                float width = lerp(0.08, 0.38, Hash11(id + 57.1)) * saturate(r * 1.6);
                float across = 1.0 - smoothstep(width * 0.6, width, abs(frac(cell) - 0.5));
                float inner = lerp(0.32, 0.22, t) + 0.18 * Hash11(id + 91.7);
                float radial = smoothstep(inner, inner + 0.12, r);
                return present * across * radial;
            }

            fixed4 InvertLook(float r, float a, float t, float2 uv)
            {
            #if defined(_INVERT_TEX_ON)
                return tex2D(_InvertTex, uv);
            #else
                float lines = _SpeedLines > 0.5 ? SpeedLinePattern(r, a, t) : 0.0;
                return fixed4(lerp(_FlashColor.rgb, _LineColor.rgb, lines), 1.0);
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

                fixed4 color = fixed4(0, 0, 0, 0);
                if (stage == 1)
                {
                #if defined(_BURST_TEX_ON)
                    color = tex2D(_BurstTex, uv);
                #else
                    color = fixed4(lerp(_BackColor.rgb, _FlashColor.rgb, BurstPattern(r, a, t)), 1.0);
                #endif
                }
                else if (stage == 2)
                {
                #if defined(_GLARE_TEX_ON)
                    color = tex2D(_GlareTex, uv);
                #else
                    color = fixed4(lerp(_BackColor.rgb, _FlashColor.rgb, GlarePattern(r, a, t)), 1.0);
                #endif
                }
                else if (stage == 3)
                {
                    color = InvertLook(r, a, t, uv);
                }
                else if (stage == 4)
                {
                    // 실제 화면 복귀 — 3단계 끝 그림이 걷힌다.
                    color = InvertLook(r, a, 1.0, uv);
                    color.a *= 1.0 - EaseOut(t);
                }

                color.a *= input.color.a;
                return color;
            }
            ENDCG
        }
    }

    CustomEditor "InterruptHitFlashOverlayShaderGUI"
}
