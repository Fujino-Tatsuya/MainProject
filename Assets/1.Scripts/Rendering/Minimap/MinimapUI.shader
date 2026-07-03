// 미니맵 합성 셰이더 (RawImage용) — 스타크래프트식 3단계 시야.
//  미탐사(explored=0): 윤곽 실루엣만 (_SilColor, 베이크 알파 = 맵 모양)
//  탐사됨(explored=1, visible=0): 지형 그림 x _DimExplored (0.5 디밍)
//  현재 시야(visible=1): 지형 그림 풀 컬러
// _MaskTex: R=탐사 누적, G=현재 시야 (MinimapController가 CPU 스탬프로 갱신)
Shader "UI/MinimapComposite"
{
    Properties
    {
        _MainTex ("Bake (RGB=지형, A=맵 실루엣)", 2D) = "black" {}
        _MaskTex ("Mask (R=explored, G=visible)", 2D) = "black" {}
        _SilColor ("미탐사 실루엣 색", Color) = (0.22, 0.26, 0.33, 1)
        _DimExplored ("탐사 디밍", Range(0,1)) = 0.5
        _BgAlpha ("맵 밖 배경 알파", Range(0,1)) = 0.35
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

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 bake = tex2D(_MainTex, i.uv);
                fixed4 mask = tex2D(_MaskTex, i.uv);
                float explored = saturate(mask.r);
                float visible = saturate(mask.g);

                // 밝기: 미탐사 0 → 탐사 _DimExplored → 시야 1
                float lit = max(explored * _DimExplored, visible);
                fixed3 terrain = bake.rgb * lit;
                // 미탐사 영역은 실루엣 색으로
                fixed3 rgb = lerp(_SilColor.rgb, terrain, saturate(explored + visible));

                // 맵 모양(bake.a) 밖은 반투명 배경
                fixed4 col;
                col.rgb = lerp(fixed3(0.02, 0.02, 0.03), rgb, bake.a);
                col.a = max(bake.a, _BgAlpha) * i.color.a;
                return col;
            }
            ENDCG
        }
    }
}
