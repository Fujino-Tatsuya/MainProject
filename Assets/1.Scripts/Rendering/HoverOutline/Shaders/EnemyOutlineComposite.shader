// ----------------------------------------------------------------------------
//  EnemyOutlineComposite.shader — 마스크에서 바깥 윤곽만 뽑아 카메라 컬러에 얹는다
//
//      윤곽선 = dilate(F, N px 원판) − F,  단 윤곽 픽셀의 씬 깊이가 대상보다 앞이면(가려짐) 버린다.
//
//  F = 대상 전체의 투영 실루엣(마스크 R). 안쪽 픽셀(F=1)은 전부 버리므로 렌더러·서브메시 경계에 안쪽 선이 없다.
//  색·깊이는 반경 안 대상 픽셀 중 **가장 앞(eye 깊이 최소)** 것을 따른다 — 겹친 두 대상이면 앞 대상 색.
//
//  🔴 원판(dx²+dy² ≤ N²+N) 이라 대각선도 굵기가 같다(정사각 커널이면 대각선이 √2 배 두껍다).
//     N=1(기본) 이면 반경² 2 → 대각 (±1,±1) 까지 든 3x3 전체 = 8-이웃 팽창. 십자(4-이웃)가 아니라 대각 계단에서도 선이 안 끊긴다.
//  🔴 마스크는 point 로 읽는다(bilinear 로 섞인 커버리지에 임계 판정을 하면 경계가 번진다).
//  🔴 카메라 컬러는 샘플하지 않는다 — 알파 블렌딩으로 그 위에 바로 얹는다(복사·되복사 없음).
// ----------------------------------------------------------------------------
Shader "Hidden/Rendering/EnemyOutlineComposite"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "EnemyOutlineComposite"

            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // EnemyOutlineDrawList.MaxWidthPixels 와 같게 둔다(C# 이 이미 클램프한다 — 셰이더는 루프 상한만).
            #define MAX_WIDTH 8

            float4 _OutlinePrimaryColor;
            float4 _OutlineAggroColor;
            // xy = 마스크 1텍셀 크기, z = 선 굵기(픽셀), w = 가림 여유(m)
            float4 _OutlineParams;

            half4 SampleMask(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
            }

            float SceneEyeDepth(float2 uv)
            {
                float raw = SampleSceneDepth(uv);
                return (unity_OrthoParams.w == 0) ? LinearEyeDepth(raw, _ZBufferParams) : LinearDepthToEyeDepth(raw);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;

                // 대상 안쪽은 선이 될 수 없다(바깥 테두리 방식) — 안쪽 선 없음.
                if (SampleMask(uv).r > 0.5h)
                    discard;

                int width = clamp((int)_OutlineParams.z, 1, MAX_WIDTH);
                int radiusSq = width * width + width;   // ≈ (N + 0.5)² — 원판 가장자리를 둥글게

                float bestDepth = 1e20;
                half bestIndex = 0;

                [loop]
                for (int y = -width; y <= width; y++)
                {
                    [loop]
                    for (int x = -width; x <= width; x++)
                    {
                        if (x * x + y * y > radiusSq)
                            continue;

                        half4 m = SampleMask(uv + float2(x, y) * _OutlineParams.xy);
                        if (m.r > 0.5h && m.b < bestDepth)
                        {
                            bestDepth = m.b;
                            bestIndex = m.g;
                        }
                    }
                }

                if (bestDepth > 1e19)
                    discard;

                // 가림: 이 윤곽 픽셀에 대상보다 앞선 씬(벽·기둥·다른 물체)이 있으면 안 그린다(ZTest LEqual 대응).
                if (SceneEyeDepth(uv) < bestDepth - _OutlineParams.w)
                    discard;

                return (bestIndex < 0.5h) ? _OutlinePrimaryColor : _OutlineAggroColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
