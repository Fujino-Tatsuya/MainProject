// ----------------------------------------------------------------------------
//  EnemyOutlineComposite.shader — 마스크에서 바깥 윤곽만 뽑아 카메라 컬러에 얹는다
//
//      윤곽선 = dilate(F, N px 원판) − F,  단 윤곽 픽셀의 씬 깊이가 대상보다 앞이면(가려짐) 버린다.
//
//  F = 대상 전체의 투영 실루엣(마스크 R). 안쪽 픽셀(F=1)은 전부 버리므로 렌더러·서브메시 경계에 안쪽 선이 없다.
//  색·깊이는 반경 안 대상 픽셀 중 **가장 앞(eye 깊이 최소)** 것을 따른다 — 겹친 두 대상이면 앞 대상 색.
//
//  🔴 N 은 색 묶음별이다(빨강 N0 · 회색 N1). 루프는 큰 쪽 반경으로 돌고, 이웃 픽셀마다 **그 픽셀의 색 인덱스(G)** 반경
//     원판 안일 때만 후보로 센다 → 한 마스크·한 합성으로 빨강 3px · 회색 2px. 앞 대상 우선 규칙은 후보끼리 그대로.
//  🔴 원판(dx²+dy² ≤ N²+N) 이라 대각선도 굵기가 같다(정사각 커널이면 대각선이 √2 배 두껍다).
//     N=1 이면 반경² 2 → 대각 (±1,±1) 까지 든 3x3 전체 = 8-이웃 팽창. 십자(4-이웃)가 아니라 대각 계단에서도 선이 안 끊긴다.
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
            // xy = 마스크 1텍셀 크기, z = 안 씀, w = 가림 여유(m)
            float4 _OutlineParams;
            // x = 빨강(색 인덱스 0) 굵기, y = 회색(색 인덱스 1) 굵기 (픽셀)
            float4 _OutlineWidths;

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

                int primaryWidth = clamp((int)_OutlineWidths.x, 1, MAX_WIDTH);
                int aggroWidth = clamp((int)_OutlineWidths.y, 1, MAX_WIDTH);
                // ≈ (N + 0.5)² — 원판 가장자리를 둥글게
                int primaryRadiusSq = primaryWidth * primaryWidth + primaryWidth;
                int aggroRadiusSq = aggroWidth * aggroWidth + aggroWidth;
                int width = max(primaryWidth, aggroWidth);
                int radiusSq = max(primaryRadiusSq, aggroRadiusSq);

                float bestDepth = 1e20;
                half bestIndex = 0;

                [loop]
                for (int y = -width; y <= width; y++)
                {
                    [loop]
                    for (int x = -width; x <= width; x++)
                    {
                        int d2 = x * x + y * y;
                        if (d2 > radiusSq)
                            continue;

                        half4 m = SampleMask(uv + float2(x, y) * _OutlineParams.xy);
                        // 이 이웃 픽셀이 속한 색 묶음의 원판 밖이면 후보가 아니다(빨강 3px · 회색 2px).
                        int ownRadiusSq = (m.g < 0.5h) ? primaryRadiusSq : aggroRadiusSq;
                        if (m.r > 0.5h && d2 <= ownRadiusSq && m.b < bestDepth)
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
