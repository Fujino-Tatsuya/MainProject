// ----------------------------------------------------------------------------
//  BossVictoryGlitch.shader — 보스 처치 연출 ZZZ 글리치(PLAN-boss-victory-glitch) 전체 화면 합성
//  반투명(이펙트) 렌더 뒤, 후처리 전. 입력 = _BlitTexture(씬색) + _GlitchIdTex(R 플레이어 · B 보스 마스크).
//  세기는 C#(BossVictoryGlitchRender.SetFrame)이 설정 SO 타임라인 곡선으로 매 프레임 넣는다.
//    글리치 = **가로줄 좌우 찢김만**(사용자 11-09 — 사각형·블록 노이즈 금지): 가는 가로줄(굵기 = 기본 px × 1~배수)마다 무작위로 켜져
//      좌우로 다른 길이만큼 밀린다(마스크도 같은 UV — 인물·보스 실루엣도 같이 찢긴다) + 찢긴 줄의 밝은 부분이 밀린 방향 뒤로 끌린다(스미어).
//    색(글리치 아님): 흑백(Desat) → 배경만 호박색(플레이어 제외) → 보스 = 흑백 노이즈 텍스처(UV 는 카메라 이동으로만)
//      → CRT 주사선.
//    발밑 회색 원(246 복원 — 267 후속): 배경 중 처치자 발밑(_GroundCircle.xyz)에서 **수평** 거리 _GroundCircle.w 안(원기둥)은 호박색 대신 흑백
//    CRT 주사선 = 벽·하늘에만(267 후속): 깊이로 복원한 표면 법선 위쪽 성분이 _ScanFloor.x~y 사이에서 벽 → 바닥으로 부드럽게 빠진다.
//      하늘(먼 평면)·플레이어·보스·RECTIFY 글자/안쪽 색 위는 예전처럼 전부 주사선.
//    배경 글자 RECTIFY(267 후속): 글자마다 화면 사각형(_RectifyScreen)·아틀라스 UV(_RectifyUV) → SDF 외곽선만(|거리| < 반두께, 안 비움).
//      플레이어·보스 마스크가 0 인 곳에만(인물·보스 뒤, 배경 앞) · 찢긴 UV(같이 찢김) · 들어오는 동안(알파 < 1) 가로줄이 무작위로 켜진다.
//      (원래 명도 × _GroundCircleLook.y), 가장자리 _GroundCircleLook.x(m). 픽셀 월드 위치 = _CameraDepthTexture(찢긴 UV 그대로 — 원도 같이 찢긴다). 반경 0 = 끔.
// ----------------------------------------------------------------------------
Shader "Hidden/Rendering/BossVictoryGlitch"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        ENDHLSL

        Pass
        {
            Name "BossVictoryGlitch"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            TEXTURE2D_X(_GlitchIdTex);
            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            float4 _GlitchA;      // x (미사용), y time, z desat, w amber
            float4 _GlitchB;      // x scanline, y tear, z smear, w (미사용)
            float4 _GlitchC;      // x bossNoise
            float4 _GlitchLook;   // x 주사선 줄 수, y 찢김 줄 기본 굵기(px), z 굵기 최대 배수, w 찢김 갱신(초당)
            float4 _GlitchTear;   // x 최대 밀림(화면 너비 비율), y 켜지는 줄 비율(세기 1 기준), z 끌림 길이(화면 너비 비율)
            float4 _BossScreen;   // xy 보스 중심(뷰포트), z 보스 화면 높이, w 공간(0 화면 · 1 보스 기준)
            float4 _AmberColor;
            float4 _NoiseST;      // xy 타일링, zw UV 오프셋(카메라 이동 누적 — 시간 흐름 없음)
            float4 _NoiseBright;
            float4 _NoiseDark;
            TEXTURE2D(_RectifyTex);
            SAMPLER(sampler_RectifyTex);
            float4 _RectifyScreen[8];  // 글자마다 xy 최소 · zw 최대(뷰포트)
            float4 _RectifyUV[8];      // 글자마다 아틀라스 xy 시작 · zw 크기
            float4 _RectifyParams;     // x 알파, y 외곽선 반두께(SDF 값), z 글자 수, w 아틀라스 높이 ÷ (패딩 + 1)
            float4 _RectifyColor;

            // 배경 글자 외곽선 가중치(0~1).
            // x = 외곽선, y = 글자 안(채움 마스크).
            half2 RectifyAt(float2 uv)
            {
                half a = 0.0h;
                half inside = 0.0h;
                int n = (int)_RectifyParams.z;
                [unroll] for (int i = 0; i < 8; i++)
                {
                    float4 r = _RectifyScreen[i];
                    float2 l = (uv - r.xy) / max(r.zw - r.xy, 1e-5);
                    if (i >= n || any(l < 0.0) || any(l > 1.0)) continue;
                    float2 auv = _RectifyUV[i].xy + l * _RectifyUV[i].zw;
                    float sd = SAMPLE_TEXTURE2D_LOD(_RectifyTex, sampler_RectifyTex, auv, 0).a - 0.5;
                    // 안티앨리어싱 폭 = 화면 1px 의 SDF 값(도함수 없이 — 루프 안 분기)
                    float w = max(0.75 * _RectifyUV[i].w * _RectifyParams.w / max((r.w - r.y) * _ScreenParams.y, 1.0), 1e-4);
                    a = max(a, (half)(1.0 - smoothstep(_RectifyParams.y - w, _RectifyParams.y + w, abs(sd))));
                    inside = max(inside, (half)smoothstep(-w, w, sd));
                }
                return half2(a, inside);
            }

            float4 _RectifyFillRings[3];     // 링 i — x 반지름(단어 높이 단위), y 알파
            float4 _RectifyFillColors[3];
            float4 _RectifyFillRect;         // 단어 사각형(뷰포트 min.xy · max.zw)
            float4 _RectifyFillParams;       // x 불투명도, y 경계(단어 높이), z 단어 종횡비, w 주사선 간격(px)
            float4 _RectifyFillLook;         // x 주사선 어둡기, y 그라데이션 세기, zw 중심(단어 UV)

            // 안쪽 색 Ring Wipe Fill — 링을 생성 순으로 덮는다(painter's order). 링 안 방사형 그라데이션(중심 = 원래 색 → 가장자리 어둡게).
            // 첫 링 전 = 비어 있음(a 0 → 외곽선만). rgb = 색, a = 덮인 정도.
            half4 RectifyFillAt(float2 uv)
            {
                float2 size = max(_RectifyFillRect.zw - _RectifyFillRect.xy, 1e-5);
                float2 wuv = (uv - _RectifyFillRect.xy) / size;
                float d = length((wuv - _RectifyFillLook.zw) * float2(_RectifyFillParams.z, 1.0));
                float soft = max(_RectifyFillParams.y, fwidth(d) * 1.5);
                half3 col = 0.0h;
                half cov = 0.0h;
                [unroll] for (int i = 0; i < 3; i++)
                {
                    float4 ring = _RectifyFillRings[i];
                    if (ring.y <= 0.0) continue;
                    half m = (half)(ring.y * (1.0 - smoothstep(ring.x - soft, ring.x + soft, d)));
                    half g = (half)saturate(d / max(ring.x, 1e-4));
                    half3 c = _RectifyFillColors[i].rgb;
                    c = lerp(c, c * (1.0h - (half)_RectifyFillLook.y), g);
                    col = lerp(col, c, m);
                    cov = cov + m * (1.0h - cov);
                }
                return half4(cov > 0.0001h ? col / cov : col, cov);   // 비어 있는 바탕 위 덮기(premultiplied) → 색 복원
            }

            float4 _GroundCircle;      // xyz 중심(월드), w 반경(m)
            float4 _GroundCircleLook;  // x 가장자리 부드러움(m), y 흑백 밝기
            float4 _ScanFloor;         // x 벽 normal.y(이하 = 주사선 그대로), y 바닥 normal.y(이상 = 바닥), z 바닥에 남길 비율

            float3 WorldAt(float2 uv, out float rawDepth)
            {
                rawDepth = SampleSceneDepth(saturate(uv));
                float d = rawDepth;
            #if !UNITY_REVERSED_Z
                d = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, d);
            #endif
                return ComputeWorldSpacePosition(saturate(uv), d, UNITY_MATRIX_I_VP);
            }

            // CRT 주사선 가중치(267 후속) — 1 = 벽·하늘(주사선), 0 = 바닥. 법선 = 이웃 깊이 4개 중 깊이 차가 작은 쪽으로
            // 복원(물체 가장자리에서 뒤 배경과 섞이지 않게). 찢긴 UV 그대로라 줄이 찢기면 판정도 같이 밀린다.
            half ScanSurfaceWeight(float2 uv)
            {
                float d0, dl, dr, dd, du;
                float3 p = WorldAt(uv, d0);
            #if UNITY_REVERSED_Z
                if (d0 <= 1e-6) return 1.0h;            // 하늘(먼 평면)
            #else
                if (d0 >= 1.0 - 1e-6) return 1.0h;
            #endif
                float2 px = 1.0 / _ScreenParams.xy;
                float3 pl = WorldAt(uv - float2(px.x, 0.0), dl);
                float3 pr = WorldAt(uv + float2(px.x, 0.0), dr);
                float3 pd = WorldAt(uv - float2(0.0, px.y), dd);
                float3 pu = WorldAt(uv + float2(0.0, px.y), du);
                float3 dx = abs(dl - d0) < abs(dr - d0) ? p - pl : pr - p;
                float3 dy = abs(dd - d0) < abs(du - d0) ? p - pd : pu - p;
                float3 n = normalize(cross(dy, dx));
                if (dot(n, _WorldSpaceCameraPos - p) < 0.0) n = -n;   // 카메라 쪽 면
                half floorW = (half)smoothstep(_ScanFloor.x, _ScanFloor.y, n.y);
                return lerp(1.0h, (half)_ScanFloor.z, floorW);
            }

            // 발밑 회색 원 가중치(0 = 밖, 1 = 안). 수평(XZ) 거리만 본다 — 원기둥.
            half GroundCircleMask(float2 uv)
            {
                float r = _GroundCircle.w;
                if (r <= 0.0) return 0.0h;
                float depth = SampleSceneDepth(saturate(uv));
            #if !UNITY_REVERSED_Z
                depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
            #endif
                float3 wp = ComputeWorldSpacePosition(saturate(uv), depth, UNITY_MATRIX_I_VP);
                float dist = distance(wp.xz, _GroundCircle.xz);
                return (half)(1.0 - smoothstep(max(0.0, r - _GroundCircleLook.x), r, dist));
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half3 SceneAt(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(uv)).rgb;
            }

            half4 MaskAt(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_GlitchIdTex, sampler_PointClamp, saturate(uv));
            }

            half Luma(half3 c) { return dot(c, half3(0.2126h, 0.7152h, 0.0722h)); }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv0 = input.texcoord;
                float t = _GlitchA.y;
                float2 res = _ScreenParams.xy;
                float aspect = res.x / res.y;
                float tick = floor(t * max(1.0, _GlitchLook.w));   // 찢김 난수 갱신

                // ── 가로줄 좌우 찢김 ─────────────────────────────────────────
                // 줄 굵기: 4줄 묶음마다 1~배수 중 하나(가는 줄·조금 굵은 줄이 섞인다). 픽셀 기준이라 해상도와 무관하게 가늘다.
                float ypx = uv0.y * res.y;
                float baseH = max(1.0, _GlitchLook.y);
                float group = floor(ypx / (baseH * 4.0 * max(1.0, _GlitchLook.z)));
                float rowH = baseH * (1.0 + floor(Hash(float2(group, tick + 17.0)) * max(1.0, _GlitchLook.z)));
                float row = floor(ypx / rowH) + group * 131.0;
                float tear = _GlitchB.y;
                float on = step(1.0 - saturate(_GlitchTear.y * tear), Hash(float2(row, tick)));
                float r = Hash(float2(row, tick + 3.0));
                float dir = Hash(float2(row, tick + 9.0)) < 0.5 ? -1.0 : 1.0;
                float shift = dir * r * r * _GlitchTear.x * tear * on;   // 줄마다 다른 길이 — 짧은 밀림이 많고 긴 밀림은 드물다
                float2 uv = uv0 + float2(shift, 0.0);

                // 마스크(같은 찢김 UV — 찢긴 줄에서만 실루엣이 같이 밀린다. 보스 외곽 지글은 11-09 사용자 요청으로 삭제).
                half4 id = MaskAt(uv);
                half boss = id.b;
                half player = id.r;

                half3 col = SceneAt(uv);

                // 끌림(스미어) — 찢긴 줄에서 밀린 방향 뒤쪽 픽셀 중 밝은 것을 끌어온다(밝은 부분이 가로로 늘어진다).
                float smear = _GlitchB.z * on;
                if (smear > 0.001)
                {
                    float len = _GlitchTear.z;
                    [unroll] for (int i = 1; i <= 6; i++)
                    {
                        half3 s = SceneAt(uv - float2(dir * len * (i / 6.0), 0.0));
                        half fade = (half)(1.0 - i / 7.0);
                        col = max(col, s * fade * (half)smear + col * (half)(1.0 - smear));
                    }
                }

                // 흑백 → 배경만 호박색. 플레이어(캐릭터)는 흑백 유지.
                half lum = Luma(col);
                half3 gray = lerp(col, lum.xxx, (half)_GlitchA.z);
                half3 amber = lum * _AmberColor.rgb * 1.6h;
                half3 graded = lerp(gray, amber, (half)_GlitchA.w);
                graded = lerp(graded, lerp(col, lum.xxx, (half)max(_GlitchA.z, _GlitchA.w)), player);
                // 발밑 회색 원 — 원 안 배경은 호박색 대신 흑백(밝기 배율). 흑백·호박이 들어오는 만큼만(플레이어는 위에서 이미 흑백).
                half circle = GroundCircleMask(uv) * (1.0h - player);
                graded = lerp(graded, lerp(col, (lum * (half)_GroundCircleLook.y).xxx, (half)max(_GlitchA.z, _GlitchA.w)), circle);

                half rectCover = 0.0h;   // RECTIFY 글자·안쪽 색이 덮은 정도 — 그 위는 주사선 예전 그대로
                // 배경 글자 RECTIFY — 인물·보스 뒤, 배경 앞. 들어오는 동안 가로줄(찢김 줄 굵기 × 2)이 알파 비율만큼 무작위로 켜진다.
                if (_RectifyParams.x > 0.0001)
                {
                    float revealRow = floor(ypx / (baseH * 2.0));
                    half reveal = _RectifyParams.x >= 0.999 ? 1.0h : (half)step(Hash(float2(revealRow, tick + 51.0)), _RectifyParams.x);
                    half2 rt = RectifyAt(uv);
                    half visible = reveal * (1.0h - player) * (1.0h - boss);
                    // 안쪽 색(원형 확장) — 글자 안에서만 · 가는 가로 주사선. 외곽선은 그 위에.
                    if (_RectifyFillParams.x > 0.0001)
                    {
                        half4 fill = RectifyFillAt(uv);
                        half scanOn = (half)step(0.5, frac(ypx / _RectifyFillParams.w));
                        half3 fillCol = fill.rgb * (1.0h - (half)_RectifyFillLook.x * scanOn);
                        half fillW = rt.y * fill.a * (half)_RectifyFillParams.x * visible;
                        graded = lerp(graded, fillCol, fillW);
                        rectCover = fillW;
                    }
                    half text = rt.x * visible;
                    rectCover = max(rectCover, text * (half)_RectifyColor.a);
                    graded = lerp(graded, _RectifyColor.rgb, text * (half)_RectifyColor.a);
                }

                // 보스 — 흑백 노이즈(흰 = 밝은 색, 검 = 어두운 적갈색)
                float2 nUV = _BossScreen.w > 0.5
                    ? (uv - _BossScreen.xy) * float2(aspect, 1.0) / _BossScreen.z   // 보스 기준(보스 화면 높이 = 1)
                    : uv * float2(aspect, 1.0);                                      // 화면 공간(세로 = 1)
                nUV = nUV * _NoiseST.xy + _NoiseST.zw;
                half n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nUV).r;
                half3 noiseCol = lerp(_NoiseDark.rgb, _NoiseBright.rgb, n);
                graded = lerp(graded, noiseCol, boss * (half)_GlitchC.x);

                // CRT 주사선 — 벽·하늘에만(바닥 뺌). 플레이어·보스·RECTIFY 위는 예전처럼 늘 주사선.
                if (_GlitchB.x > 0.0001)
                {
                    half surf = ScanSurfaceWeight(uv);
                    half keep = max(surf, max(max(player, boss), rectCover));
                    half scan = 0.5h + 0.5h * sin(uv0.y * _GlitchLook.x * 6.28318h);
                    graded *= lerp(1.0h, 0.72h + 0.28h * scan, (half)_GlitchB.x * keep);
                }

                return half4(graded, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
