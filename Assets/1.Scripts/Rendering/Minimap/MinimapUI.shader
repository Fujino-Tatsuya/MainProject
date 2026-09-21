// 미니맵 합성 셰이더 (RawImage용) — 스타크래프트식 3단계 시야 + `minimap.png` 룩.
//
// 2026-09-20 개정 (PLAN-minimap D1·D2·D11·D13):
//   채움이 지형 사진이 아니라 **균일 회색(플랫)** 이고, 외곽선을 실루엣 엣지에서 뽑는다.
//   레이아웃이 어떻게 생성되든 같은 시각 언어가 나온다 — 아트 타일이 필요 없다.
//
// 3단계 시야:
//   미탐사        : **어두운 회색으로 채워진다**(_UnexploredDim). 맵 전체 모양이 늘 보여야 한다.
//   탐사됨        : 회색 채움 x _DimExplored 로 한 단계 밝아진다
//   현재 시야     : 회색 채움 풀 밝기
//
// 🔴 2026-09-20 팀장 실측 판정으로 뒤집힌 부분 — 한 번은 "미탐사=외곽선만(내부 투명)" 으로 만들었는데
//    실제 화면에서 **미니맵이 거의 안 보였다.** 레퍼런스의 회색은 "탐사된 방" 이 아니라
//    **어두운 바탕 + 탐사로 밝아짐** 이다.
//
// 맵 모양 바깥은 **완전 투명**이다(프레임·배경 패널 없음 — 레퍼런스 목업 실측).
// 🔴 `_BgAlpha` 기본값을 0 으로 두는 것만으로는 부족하다. 머티리얼(.mat)에 예전 값 0.35 가
//    저장돼 있으면 그쪽이 이긴다 — MinimapController 가 런타임에 명시적으로 덮어쓴다.
//
// _MaskTex: R=탐사 누적, G=현재 시야 (MinimapController가 CPU 스탬프로 갱신)
// _SilTex : 맵 모양 (슬롯 풋프린트 + 통로, CPU 생성. 코너는 둥글게 그려진다)
Shader "UI/MinimapComposite"
{
    Properties
    {
        _MainTex ("Bake (RGB=지형, 지금은 안 씀)", 2D) = "black" {}
        _MaskTex ("Mask (R=explored, G=visible)", 2D) = "black" {}
        _SilTex ("Silhouette (R=맵 모양, CPU 생성)", 2D) = "black" {}

        _FlatColor ("방 채움 색(플랫)", Color) = (0.58, 0.58, 0.58, 1)
        _OutlineColor ("외곽선 색", Color) = (0.13, 0.15, 0.17, 1)
        _OutlineWidth ("외곽선 두께(실루엣 텍셀)", Range(0.5, 4)) = 1.6
        _FillAlpha ("채움 불투명도", Range(0,1)) = 0.92
        _UnexploredDim ("미탐사 밝기(0=검정, 1=탐사와 동일)", Range(0,1)) = 0.34
        _UnexploredOutlineAlpha ("미탐사 외곽선 불투명도", Range(0,1)) = 0.7

        _SilColor ("(레거시) 미탐사 색 — 지금은 안 씀", Color) = (0.22, 0.26, 0.33, 1)
        _DimExplored ("탐사 디밍", Range(0,1)) = 0.62
        _BgAlpha ("맵 밖 배경 알파(0 = 완전 투명)", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _MaskTex;
            sampler2D _SilTex;
            float4 _SilTex_TexelSize;

            fixed4 _FlatColor;
            fixed4 _OutlineColor;
            float _OutlineWidth;
            float _FillAlpha;
            float _UnexploredDim;
            float _UnexploredOutlineAlpha;

            fixed4 _SilColor;
            float _DimExplored;
            float _BgAlpha;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float Shape(float2 uv) { return tex2D(_SilTex, uv).r; }

            fixed4 frag(v2f i) : SV_Target
            {
                float shape = Shape(i.uv);

                // 외곽선 — 이웃 4샘플 중 가장 작은 값과의 차. 모양 **안쪽** 가장자리에 그려지므로
                // 실루엣이 커지지 않는다. 레이아웃이 바뀌어도 공짜로 따라온다(D2).
                float2 o = _SilTex_TexelSize.xy * _OutlineWidth;
                float minN = min(min(Shape(i.uv + float2(o.x, 0)), Shape(i.uv - float2(o.x, 0))),
                                 min(Shape(i.uv + float2(0, o.y)), Shape(i.uv - float2(0, o.y))));
                float edge = saturate(shape - minN);

                fixed4 mask = tex2D(_MaskTex, i.uv);
                float explored = saturate(mask.r);
                float visible = saturate(mask.g);

                // 밝기 3단계: 미탐사 _UnexploredDim → 탐사 _DimExplored → 시야 1
                float lit = max(explored * _DimExplored, visible);
                float seen = saturate(explored + visible);
                float level = lerp(_UnexploredDim, 1.0, lit);

                // 🔴 채움은 **맵 전체**에 있다. seen 을 곱하지 않는다 —
                //    미탐사 구역이 투명해지면 미니맵이 사실상 안 보인다(2026-09-20 실측).
                float fillA = shape * _FillAlpha;

                // 외곽선은 탐사와 무관하게 항상 보인다 — 미탐사 구역은 더 희미하게.
                float outlineA = edge * lerp(_UnexploredOutlineAlpha, 1.0, seen);

                fixed3 fillRgb = _FlatColor.rgb * level;

                fixed4 col;
                col.rgb = lerp(fillRgb, _OutlineColor.rgb, saturate(outlineA));
                col.a = max(max(fillA, outlineA), (1.0 - shape) * _BgAlpha) * i.color.a;
                return col;
            }
            ENDCG
        }
    }
}
