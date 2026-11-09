// 체력 비네팅(HealthVignetteHUD) — 화면 가장자리 → 안쪽으로 옅어지는 비네팅을 절차적으로 그린다(텍스처 없음).
// 옛 방식(VignetteSpriteFactory.CreateRadial 128px 방사형 스프라이트를 화면 전체로 늘림)과 기본값에서 같은 모양:
//   uv → (-1..1) 축별 정규화, 거리 = 초타원 노름(모서리 사각 정도 0 = 타원 = 옛 방식), 알파 = smoothstep(안쪽, 바깥, 거리).
// _VignetteShape = (안쪽 반경, 바깥 반경, 모서리 사각 정도 0..1, 세기 0..1) — 반경: 0 = 중앙, 1 = 변의 중점, √2 = 모서리(타원일 때).
// _VignetteAspect = (화면 가로/세로, 화면비 보정 0..1, -, -) — 보정 1 = 좌우 띠 폭이 위아래와 같은 픽셀 폭.
// 색·알파(체력 비율·맥동·피격 플래시·사망 어둡게)는 Graphic.color(정점 색)로 온다. 값은 HUD 가 매 프레임 재질 인스턴스에 넣는다.
// 기본 UI 셰이더(UI/Default)와 같은 스텐실·RectMask2D·알파 클립.
Shader "UI/HealthVignette"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _VignetteShape ("Shape (inner, outer, squareness, strength)", Vector) = (0.9, 1.2, 0, 1)
        _VignetteAspect ("Aspect (w/h, correction, -, -)", Vector) = (1.7777778, 0, 0, 0)

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
            #pragma target 2.0

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

            fixed4 _Color;
            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            float4 _VignetteShape;
            float4 _VignetteAspect;

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
                OUT.texcoord = v.texcoord.xy;   // 스프라이트 없음 → Image 가 0..1 을 준다
                OUT.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw,
                    0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));

                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                    v.color.rgb = UIGammaToLinear(v.color.rgb);

                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // HealthVignetteModel.EdgeMask 와 같은 식(EditMode 테스트가 옛 방사형 텍스처와 비교한다).
                float2 p = abs(IN.texcoord * 2.0 - 1.0);
                // 화면비 보정 — 가로 거리를 가장자리 기준으로 화면비만큼 늘려 좌우 띠 폭을 위아래와 같은 픽셀로.
                float stretch = lerp(1.0, max(_VignetteAspect.x, 1e-3), saturate(_VignetteAspect.y));
                p.x = max(0.0, 1.0 - (1.0 - p.x) * stretch);
                // 초타원 노름 — 사각 정도 0 = 타원(p=2), 1 = 둥근 사각(p=12).
                float n = lerp(2.0, 12.0, saturate(_VignetteShape.z));
                p = max(p, 1e-5);
                float d = pow(pow(p.x, n) + pow(p.y, n), 1.0 / n);
                float edge = smoothstep(_VignetteShape.x, max(_VignetteShape.y, _VignetteShape.x + 1e-4), d) * saturate(_VignetteShape.w);

                half4 color = IN.color;
                color.a *= edge;

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
