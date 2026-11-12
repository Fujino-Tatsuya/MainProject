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
//      하늘(먼 평면)·플레이어·보스 위는 전부 주사선. RECTIFY 글자 마스크(외곽선 포함) 위는 주사선 없음(267 후속).
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

            // ── 공용 선언·함수(합성 패스 + RECTIFY 글로우 패스 1~4가 같이 쓴다, 267 후속) ──
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
            // x = 외곽선, y = 글자 안(채움 마스크), z = 외곽선 바깥 경계까지(안 ∪ 외곽선 — 색이 외곽선을 덮을 때 채움 마스크, 바깥 가장자리 AA 유지).
            half3 RectifyAt(float2 uv)
            {
                half a = 0.0h;
                half inside = 0.0h;
                half outer = 0.0h;
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
                    outer = max(outer, (half)smoothstep(-_RectifyParams.y - w, -_RectifyParams.y + w, sd));
                }
                return half3(a, inside, outer);
            }

            float4 _RectifyFillRings[3];     // 원 i(0~2 — 마지막 = 흰 원) — x 반지름(단어 높이 단위), y 알파, zw 중심(단어 UV)
            float4 _RectifyFillColors[3];    // 원 i 왼쪽 색(rgb) — 단어 UV x = 0
            float4 _RectifyFillColorsR[3];   // 원 i 오른쪽 색(rgb) — x = 1
            float4 _RectifyCircleGray;       // 마지막 원 회색(rgb), w = 흰 섞임(0 회색 ~ 1 좌우 색)
            float4 _RectifyOrange;           // x 와이프 진행, y 들쭉날쭉(단어 폭), z 오른쪽→왼쪽(1), w 줄 굵기(px)
            float4 _RectifyOrangeLeft;
            float4 _RectifyOrangeRight;      // a = 좌→우 보간 세기
            float4 _RectifyFillRect;         // 단어 사각형(뷰포트 min.xy · max.zw)
            float4 _RectifyFillParams;       // x 불투명도, y 경계(단어 높이), z 단어 종횡비, w 주사선 간격(px)
            float4 _RectifyFillLook;         // x 주사선 어둡기, y 색이 외곽선도 덮음(1 켬 · 0 = 안쪽만, 외곽선은 늘 흰색), z 마지막(흰) 원 번호

            // 원 i 색 — 단어 UV x 기준 왼쪽 → 오른쪽. 마지막 원은 회색 → (좌우 색) 섞임.
            half3 RectifyCircleColor(int i, float wx)
            {
                half3 c = lerp((half3)_RectifyFillColors[i].rgb, (half3)_RectifyFillColorsR[i].rgb, (half)saturate(wx));
                return abs((float)i - _RectifyFillLook.z) < 0.5 ? lerp((half3)_RectifyCircleGray.rgb, c, (half)_RectifyCircleGray.w) : c;
            }

            // 원 i 마스크(알파 × 원 안, 경계 AA).
            half RectifyCircleMask(int i, float2 wuv)
            {
                float4 ring = _RectifyFillRings[i];
                if (ring.y <= 0.0) return 0.0h;
                float d = length((wuv - ring.zw) * float2(_RectifyFillParams.z, 1.0));
                float soft = max(_RectifyFillParams.y, fwidth(d) * 1.5);
                return (half)(ring.y * (1.0 - smoothstep(ring.x - soft, ring.x + soft, d)));
            }

            // 안쪽 원 3개(267 후속 — 원 통합) — 원을 순서대로 덮는다(painter's order). 원 안 색 = 좌 → 우.
            // 첫 원 전 = 비어 있음(a 0 → 외곽선만). rgb = 색, a = 덮인 정도.
            half4 RectifyFillAt(float2 uv)
            {
                float2 size = max(_RectifyFillRect.zw - _RectifyFillRect.xy, 1e-5);
                float2 wuv = (uv - _RectifyFillRect.xy) / size;
                half3 col = 0.0h;
                half cov = 0.0h;
                [unroll] for (int i = 0; i < 3; i++)
                {
                    half m = RectifyCircleMask(i, wuv);
                    col = lerp(col, RectifyCircleColor(i, wuv.x), m);
                    cov = cov + m * (1.0h - cov);
                }
                return half4(cov > 0.0001h ? col / cov : col, cov);   // 비어 있는 바탕 위 덮기(premultiplied) → 색 복원
            }

            float4 _GroundCircle;      // xyz 중심(월드), w 반경(m)
            float4 _GroundCircleLook;  // x 가장자리 부드러움(m), y 흑백 밝기
            float4 _ScanFloor;         // x 벽 normal.y(이하 = 주사선 그대로), y 바닥 normal.y(이상 = 바닥), z 바닥에 남길 비율, w 하늘 호박 명도 하한

            float3 WorldAt(float2 uv, out float rawDepth)
            {
                rawDepth = SampleSceneDepth(saturate(uv));
                float d = rawDepth;
            #if !UNITY_REVERSED_Z
                d = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, d);
            #endif
                return ComputeWorldSpacePosition(saturate(uv), d, UNITY_MATRIX_I_VP);
            }

            // 하늘 = 먼 평면 깊이(지운 채 남은 곳). 1 = 하늘.
            half SkyMask(float2 uv)
            {
                float d = SampleSceneDepth(saturate(uv));
            #if UNITY_REVERSED_Z
                return d <= 1e-6 ? 1.0h : 0.0h;
            #else
                return d >= 1.0 - 1e-6 ? 1.0h : 0.0h;
            #endif
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

            // 주황 채움(흰 원 뒤 줄 단위 들쭉날쭉 와이프, 레퍼런스) — rgb = 색(왼쪽 주황 → 오른쪽 노랑), a = 덮였나(0/1). 진행 0 = 0.
            half4 RectifyOrangeAt(float2 uv, float ypx)
            {
                if (_RectifyOrange.x <= 0.0001) return 0.0h;
                float wx = (uv.x - _RectifyFillRect.x) / max(_RectifyFillRect.z - _RectifyFillRect.x, 1e-5);
                float oRow = floor(ypx / _RectifyOrange.w);
                float oOff = Hash(float2(oRow, floor(_RectifyOrange.x * 6.0) + 91.0)) - 0.5;
                float sx = _RectifyOrange.z > 0.5 ? 1.0 - wx : wx;
                float edge = lerp(-_RectifyOrange.y, 1.0 + _RectifyOrange.y, saturate(_RectifyOrange.x));
                half covered = (half)step(sx + oOff * _RectifyOrange.y, edge);
                half3 lr = lerp(_RectifyOrangeLeft.rgb, _RectifyOrangeRight.rgb, saturate(wx));
                half3 orangeCol = lerp(_RectifyOrangeLeft.rgb, lr, (half)_RectifyOrangeRight.a);
                return half4(orangeCol, covered);
            }

            // 흰 원(마지막 원 — 덮인 만큼 그대로 보인다) 색 × 마스크. 글로우 소스용(회색 단계면 회색으로 약하게).
            half3 RectifyWhiteCircle(float2 uv)
            {
                int last = (int)(_RectifyFillLook.z + 0.5);
                float2 size = max(_RectifyFillRect.zw - _RectifyFillRect.xy, 1e-5);
                float2 wuv = (uv - _RectifyFillRect.xy) / size;
                return RectifyCircleMask(last, wuv) * RectifyCircleColor(last, wuv.x);
            }
            // ── RECTIFY 전용 글로우(267 후속 — 맵 블룸과 무관) ──
            float4 _RectifyGlowParams;   // x 흰 원 고름(0/1), y 주황 채움 고름(0/1), z (미사용), w 세기
            float4 _RectifyGlowStep;     // xy 블러 한 칸 간격(글로우 텍스처 UV — C# 이 반경 ÷ 4탭으로 계산)
            float4 _RectifyGlowColor;    // rgb 틴트(곱하기)
            float4 _GlitchGrade;         // x 배경 호박 명도 리프트, y CRT 주사선 어둡기
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
                // 하늘(먼 평면)은 명도 하한 — 곱하기 틴트라 검은 하늘이 칙칙한 겨자색이 되던 것(267 후속). 벽·바닥 톤의 밝은 노랑으로.
                // 배경 명도 리프트(267 후속) — 하이키: 어두운 벽도 밝은 호박색으로(lift + (1 − lift) × 명도). 하늘 하한은 그 위에.
                half lumLift = (half)_GlitchGrade.x + (1.0h - (half)_GlitchGrade.x) * lum;
                half lumAmber = lerp(lumLift, max(lumLift, (half)_ScanFloor.w), SkyMask(uv));
                half3 amber = lumAmber * _AmberColor.rgb * 1.6h;
                half3 graded = lerp(gray, amber, (half)_GlitchA.w);
                graded = lerp(graded, lerp(col, lum.xxx, (half)max(_GlitchA.z, _GlitchA.w)), player);
                // 발밑 회색 원 — 원 안 배경은 호박색 대신 흑백(밝기 배율). 흑백·호박이 들어오는 만큼만(플레이어는 위에서 이미 흑백).
                half circle = GroundCircleMask(uv) * (1.0h - player);
                graded = lerp(graded, lerp(col, (lum * (half)_GroundCircleLook.y).xxx, (half)max(_GlitchA.z, _GlitchA.w)), circle);

                half rectMask = 0.0h;    // RECTIFY 글자 마스크(외곽선 포함, 보이는 만큼) — 그 위는 CRT 주사선을 뺀다(267 후속)
                // 배경 글자 RECTIFY — 인물·보스 뒤, 배경 앞. 들어오는 동안 가로줄(찢김 줄 굵기 × 2)이 알파 비율만큼 무작위로 켜진다.
                if (_RectifyParams.x > 0.0001)
                {
                    float revealRow = floor(ypx / (baseH * 2.0));
                    half reveal = _RectifyParams.x >= 0.999 ? 1.0h : (half)step(Hash(float2(revealRow, tick + 51.0)), _RectifyParams.x);
                    half3 rt = RectifyAt(uv);
                    half fillCover = 0.0h;   // 색이 닿은 정도(외곽선 덮기 켬일 때만) — 외곽선은 남은 만큼만 흰색
                    half visible = reveal * (1.0h - player) * (1.0h - boss);
                    rectMask = max(rt.x, rt.z) * visible;
                    // 안쪽 색(원형 확장) — 글자 안에서만 · 가는 가로 주사선(rectifyScanlineDark, 기본 0 = 끔). 외곽선은 그 위에.
                    if (_RectifyFillParams.x > 0.0001)
                    {
                        half4 fill = RectifyFillAt(uv);
                        // 주황 채움 — 흰 원 뒤 줄 단위 들쭉날쭉 와이프(레퍼런스), 색 = 왼쪽 주황 → 오른쪽 노랑. 다 덮으면 불투명.
                        half4 orange = RectifyOrangeAt(uv, ypx);
                        fill = half4(lerp(fill.rgb, orange.rgb, orange.a), lerp(fill.a, 1.0h, orange.a));
                        half scanOn = (half)step(0.5, frac(ypx / _RectifyFillParams.w));
                        half3 fillCol = fill.rgb * (1.0h - (half)_RectifyFillLook.x * scanOn);
                        half coverOutline = (half)step(0.5, _RectifyFillLook.y);
                        half fillShape = lerp(rt.y, rt.z, coverOutline);   // 덮기 켬 = 외곽선 바깥 경계까지
                        half fillW = fillShape * fill.a * (half)_RectifyFillParams.x * visible;
                        fillCover = fill.a * (half)_RectifyFillParams.x * coverOutline;
                        graded = lerp(graded, fillCol, fillW);
                    }
                    half text = rt.x * visible * (1.0h - fillCover);   // 색이 지나간 자리는 외곽선도 그 색(첫 링 전 = 흰 외곽선만)
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

                // CRT 주사선 — 벽·하늘에만(바닥 뺌). 플레이어·보스 위는 늘 주사선, RECTIFY 글자 마스크 위는 뺀다.
                if (_GlitchB.x > 0.0001)
                {
                    half surf = ScanSurfaceWeight(uv);
                    half keep = max(surf, max(player, boss)) * (1.0h - rectMask);
                    half scan = 0.5h + 0.5h * sin(uv0.y * _GlitchLook.x * 6.28318h);
                    half dark = (half)_GlitchGrade.y;   // 어두운 줄 = 1 − 어둡기(예전 고정 0.28)
                    graded *= lerp(1.0h, (1.0h - dark) + dark * scan, (half)_GlitchB.x * keep);
                }

                return half4(graded, 1.0h);
            }
            ENDHLSL
        }

        // ── RECTIFY 전용 글로우(267 후속) — 1 소스(절반 해상도) → 2 가로 블러 → 3 세로 블러 → 4 더하기(전체 해상도). ──
        // 소스 = 고른 채움(흰 원·주황) × 글자 마스크(외곽선 바깥 경계까지) × 채움 불투명도 × 글자 알파 × (1 − 플레이어·보스).
        // 찢김 UV·등장 가로줄은 따르지 않는다(블러로 뭉개지는 빛이라).
        Pass
        {
            Name "RectifyGlowSource"
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragGlowSource

            half4 FragGlowSource(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                if (_RectifyParams.x <= 0.0001 || _RectifyFillParams.x <= 0.0001) return 0.0h;
                half4 id = MaskAt(uv);
                half visible = (half)_RectifyParams.x * (1.0h - id.r) * (1.0h - id.b);
                half glyph = RectifyAt(uv).z;
                half4 orange = RectifyOrangeAt(uv, uv.y * _ScreenParams.y);
                half3 white = RectifyWhiteCircle(uv) * (1.0h - orange.a);
                half3 src = (half)_RectifyGlowParams.x * white
                          + (half)_RectifyGlowParams.y * orange.a * orange.rgb;
                return half4(src * glyph * visible * (half)_RectifyFillParams.x, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "RectifyGlowBlurH"
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlurH

            // 9탭 가우스(시그마 ≈ 2탭). 간격 = _RectifyGlowStep(UV).
            half3 Blur9(float2 uv, float2 stepUV)
            {
                const half w[5] = { 0.2270h, 0.1946h, 0.1216h, 0.0541h, 0.0162h };
                half3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb * w[0];
                [unroll] for (int i = 1; i < 5; i++)
                {
                    c += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + stepUV * i).rgb * w[i];
                    c += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - stepUV * i).rgb * w[i];
                }
                return c;
            }

            half4 FragBlurH(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(Blur9(input.texcoord, float2(_RectifyGlowStep.x, 0.0)), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "RectifyGlowBlurV"
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlurV

            half3 Blur9(float2 uv, float2 stepUV)
            {
                const half w[5] = { 0.2270h, 0.1946h, 0.1216h, 0.0541h, 0.0162h };
                half3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb * w[0];
                [unroll] for (int i = 1; i < 5; i++)
                {
                    c += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + stepUV * i).rgb * w[i];
                    c += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - stepUV * i).rgb * w[i];
                }
                return c;
            }

            half4 FragBlurV(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(Blur9(input.texcoord, float2(0.0, _RectifyGlowStep.y)), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "RectifyGlowAdd"
            ZWrite Off ZTest Always Cull Off
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragGlowAdd

            half4 FragGlowAdd(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb;
                return half4(g * _RectifyGlowColor.rgb * (half)_RectifyGlowParams.w, 0.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
