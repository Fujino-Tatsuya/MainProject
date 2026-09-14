// ----------------------------------------------------------------------------
//  PlayerSilhouetteComposite.shader — 마스크에서 윤곽선을 뽑아 카메라 컬러에 얹는다
//
//      윤곽선 = (F − erode(F)) × H
//
//  F = 캐릭터의 전체 투영 실루엣(마스크 R), H = 그 지점이 가려졌는가(마스크 G).
//  침식을 F 에 적용하고 그 결과에 H 를 곱한다. 순서를 바꾸면(= 가려진 영역만 남기고 침식하면)
//  벽이 몸을 자르는 경계에 가짜 선이 생긴다.
//
//  🔴 카메라 컬러를 샘플하지 않는다. 알파 블렌딩으로 그 위에 바로 얹으므로
//     복사 → 합성 → 되복사(MaskBlur 식 CopyBack)가 필요 없다.
//
//  🔴 마스크는 point 로 읽어야 한다. bilinear 로 섞인 값에 "== 0" 판정을 하면
//     경계에서 반값이 나와 선이 두 겹으로 번진다.
// ----------------------------------------------------------------------------
Shader "Hidden/Rendering/PlayerSilhouetteComposite"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "SilhouetteComposite"

            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _SilhouetteLocalColor;
            float4 _SilhouetteRemoteColor;
            // xy = 마스크 1텍셀 크기, z = 선 굵기(텍셀), w = 미사용
            float4 _SilhouetteOutline;

            // 마스크를 point 로 한 번 읽는다. Blit.hlsl 의 _BlitTexture 를 그대로 쓴다.
            half4 SampleMask(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 c = SampleMask(uv);

                // 캐릭터가 없는 픽셀은 애초에 선이 될 수 없다(안쪽 테두리 방식).
                if (c.r < 0.5h)
                    discard;

                // 맨 앞 표면이 가려지지 않았다면 그냥 평소대로 보이는 중이다 — 선을 그리지 않는다.
                if (c.g < 0.5h)
                    discard;

                float2 texStep = _SilhouetteOutline.xy * max(_SilhouetteOutline.z, 1.0);

                half4 l = SampleMask(uv + float2(-texStep.x, 0));
                half4 r = SampleMask(uv + float2( texStep.x, 0));
                half4 d = SampleMask(uv + float2(0, -texStep.y));
                half4 u = SampleMask(uv + float2(0,  texStep.y));

                // 안쪽 테두리: 이웃 중 하나라도 캐릭터가 없으면 내가 가장자리다.
                bool edge =
                    (l.r < 0.5h) || (r.r < 0.5h) || (d.r < 0.5h) || (u.r < 0.5h);

                // 두 플레이어가 겹친 자리도 경계로 친다 — 소속이 다르면 선을 넣어 서로 구분한다.
                // (완전한 분리는 플레이어별 마스크가 필요하다. 이건 싸게 얻는 부분 해결이다.)
                bool idEdge =
                    (l.r > 0.5h && abs(l.b - c.b) > 0.5h) ||
                    (r.r > 0.5h && abs(r.b - c.b) > 0.5h) ||
                    (d.r > 0.5h && abs(d.b - c.b) > 0.5h) ||
                    (u.r > 0.5h && abs(u.b - c.b) > 0.5h);

                if (!edge && !idEdge)
                    discard;

                half4 color = (c.b < 0.5h) ? _SilhouetteLocalColor : _SilhouetteRemoteColor;
                return color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
