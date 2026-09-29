// S0 스파이크 — 에디트 모드 씬 뷰 오버레이.
// 구조는 NetVis 의 Hidden/NetVisFullscreenPass 와 같다(ObjectId 버퍼 + 풀스크린 blit).
// 다른 점 두 가지:
//   1) 색을 100% 치환하지 않고 원본과 blend 한다   → 벽 형상이 보인다 (PLAN 결정 15)
//   2) blend 세기를 색의 alpha 로 전달한다          → 활성/비활성 그룹을 한 버퍼로 표현 (PLAN 결정 17)
Shader "Hidden/Rendering/TransparentGroupOverlay"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma shader_feature TRANSPARENT_GROUP_OUTLINE
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };

            uniform sampler2D       _SceneRenderTex;
            uniform sampler2D_float _ObjectIdTex;
            fixed4                  _ObjectIdTex_ST;
            fixed4                  _ObjectIdTex_TexelSize;

            // rgb = 그룹 색, a = blend 세기(0 이면 그룹 아님)
            StructuredBuffer<float4> _ObjectIdToColorBuffer;

            uniform float _SceneSaturation;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _ObjectIdTex);
                return o;
            }

            int decodeObjectId(float4 color)
            {
                return (int)(color.r * 255) +
                      ((int)(color.g * 255) <<  8) +
                      ((int)(color.b * 255) << 16) +
                      ((int)(color.a * 255) << 24);
            }

#if TRANSPARENT_GROUP_OUTLINE
            float getEdgeOutlineMultiplier(float2 uv, int objectId, float blend)
            {
                const float2 offsetRight = float2(_ObjectIdTex_TexelSize.x, 0);
                const float2 offsetUp    = float2(0, _ObjectIdTex_TexelSize.y);

                const int idRight = decodeObjectId(tex2D(_ObjectIdTex, uv + offsetRight));
                const int idUp    = decodeObjectId(tex2D(_ObjectIdTex, uv + offsetUp));

                const bool isBoundary = objectId != idRight || objectId != idUp;
                const bool anyGrouped = blend > 0
                                     || _ObjectIdToColorBuffer.Load(idUp).a > 0
                                     || _ObjectIdToColorBuffer.Load(idRight).a > 0;

                return (isBoundary && anyGrouped) ? 0 : 1;
            }
#endif

            fixed4 desaturate(fixed4 color)
            {
                // Rec. 709
                const float grey = 0.2126 * color.x + 0.7152 * color.y + 0.0722 * color.z;
                return lerp(fixed4(grey, grey, grey, 1), color, _SceneSaturation);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                const int    objectId = decodeObjectId(tex2D(_ObjectIdTex, i.uv));
                const float4 groupColor = _ObjectIdToColorBuffer.Load(objectId);
                const fixed4 raw = tex2D(_SceneRenderTex, i.uv);

                // 그룹이면 원본과 blend(형상 유지), 아니면 탈색만.
                fixed4 result = groupColor.a > 0.0f
                    ? fixed4(lerp(raw.rgb, groupColor.rgb, groupColor.a), 1)
                    : desaturate(raw);

#if TRANSPARENT_GROUP_OUTLINE
                result.rgb *= getEdgeOutlineMultiplier(i.uv, objectId, groupColor.a);
#endif
                return result;
            }
            ENDCG
        }
    }
}
