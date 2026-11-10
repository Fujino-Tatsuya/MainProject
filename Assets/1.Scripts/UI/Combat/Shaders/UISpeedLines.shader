// 제압 가속 스피드 라인(할 일 249) — 화면 가장자리에서 중앙으로 향하는 방사형 집중선을 절차 생성한다(텍스처 없음).
// 각도를 칸(선 개수)으로 나누고, 칸마다 프레임 번호 해시로 켜짐·위치·두께·안쪽 끝 반경을 정한다 → 프레임(갱신 fps)마다 바뀌며 깜빡인다.
// 반경 = 화면 중심 기준 정규화 좌표 길이(타원 — 화면비에 맞춘 중앙 빈 영역), 각도 = 화면비 보정 좌표로 재서 선이 고르게 퍼진다.
// 선은 바깥이 두껍고 안쪽 끝으로 갈수록 가늘어진다(만화 집중선). Graphic.color(정점 색) = 색·전체 알파.
// 값은 SuppressHasteVignetteHUD 가 넣는다(재질 인스턴스). 기본 UI 셰이더와 같은 스텐실·RectMask2D·알파 클립.
Shader "UI/SpeedLines"
{
    Properties
    {
        [PerRendererData] _MainTex ("Unused", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Lines ("Lines (count, thickness, innerMin, innerMax)", Vector) = (90, 0.35, 0.75, 1.05)
        _LineFrame ("Frame (frameSeed, density, clearRadius, aspect)", Vector) = (0, 0.55, 0.6, 1.7778)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 mask          : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            float4 _Lines;
            float4 _LineFrame;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float4 vPosition = UnityObjectToClipPos(v.vertex);
                OUT.worldPosition = v.vertex;
                OUT.vertex = vPosition;

                float2 pixelSize = vPosition.w;
                pixelSize /= float2(1, 1) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
                OUT.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw,
                    0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));

                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                    v.color.rgb = UIGammaToLinear(v.color.rgb);

                OUT.color = v.color * _Color;
                return OUT;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 p = IN.texcoord * 2.0 - 1.0;                       // -1..1 (화면 끝 = ±1)
                float radius = length(p);                                  // 타원 반경 — 변 가운데 1, 모서리 √2
                float2 pa = float2(p.x * _LineFrame.w, p.y);               // 화면비 보정 — 각도가 실제 화면에서 고르게
                float angle01 = atan2(pa.y, pa.x) / 6.2831853 + 0.5;       // 0..1

                float count = max(_Lines.x, 1.0);
                float slotPos = angle01 * count;
                float slot = floor(slotPos);
                float local = frac(slotPos);

                float2 key = float2(slot, _LineFrame.x);
                float active = step(Hash(key), _LineFrame.y);               // 이 프레임 이 칸에 선이 있나(밀도)
                float centre = 0.2 + 0.6 * Hash(key + 11.1);                // 칸 안 위치
                float inner = lerp(_Lines.z, _Lines.w, Hash(key + 23.7));   // 안쪽 끝 반경
                float width = _Lines.y * (0.35 + 0.65 * Hash(key + 37.3)) * 0.5;

                // 바깥이 두껍고 안쪽 끝으로 갈수록 가늘어진다.
                float taper = saturate((radius - inner) / 0.6);
                float halfWidth = width * taper;
                // 각도 칸 단위 안티에일리어싱 — atan2 이음매(왼쪽 수평선)에서 튀지 않게 화면비 보정 좌표의 미분으로 잰다.
                float aa = count * length(fwidth(pa)) / (6.2831853 * max(length(pa), 1e-3)) * 1.5;
                float lineMask = 1.0 - smoothstep(halfWidth, halfWidth + aa, abs(local - centre));
                lineMask *= active * step(inner, radius);

                // 중앙 빈 영역(타원).
                lineMask *= smoothstep(_LineFrame.z, _LineFrame.z + 0.15, radius);

                half4 color = half4(IN.color.rgb, saturate(lineMask) * IN.color.a);

                #ifdef UNITY_UI_CLIP_RECT
                half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(IN.mask.xy)) * IN.mask.zw);
                color.a *= m.x * m.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                color.rgb *= color.a;
                return color;
            }
        ENDCG
        }
    }
}
