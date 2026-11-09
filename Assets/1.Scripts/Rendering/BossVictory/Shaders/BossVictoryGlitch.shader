// ----------------------------------------------------------------------------
//  BossVictoryGlitch.shader — 보스 처치 연출 ZZZ 글리치(PLAN-boss-victory-glitch) 전체 화면 합성
//  반투명(이펙트) 렌더 뒤, 후처리 전. 입력 = _BlitTexture(씬색) + _GlitchIdTex(R 플레이어 · B 보스 마스크).
//  세기는 C#(BossVictoryGlitchRender.SetFrame)이 설정 SO 타임라인 곡선으로 매 프레임 넣는다.
//    글리치 = **가로줄 좌우 찢김만**(사용자 11-09 — 사각형·블록 노이즈 금지): 가는 가로줄(굵기 = 기본 px × 1~배수)마다 무작위로 켜져
//      좌우로 다른 길이만큼 밀린다(마스크도 같은 UV — 인물·보스 실루엣도 같이 찢긴다) + 찢긴 줄의 밝은 부분이 밀린 방향 뒤로 끌린다(스미어).
//    색(글리치 아님): 흑백(Desat) → 배경만 호박색(플레이어 제외) → 보스 = 흑백 노이즈 텍스처(UV 는 카메라 이동으로만)
//      → CRT 주사선.
// ----------------------------------------------------------------------------
Shader "Hidden/Rendering/BossVictoryGlitch"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
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

                // 보스 — 흑백 노이즈(흰 = 밝은 색, 검 = 어두운 적갈색)
                float2 nUV = _BossScreen.w > 0.5
                    ? (uv - _BossScreen.xy) * float2(aspect, 1.0) / _BossScreen.z   // 보스 기준(보스 화면 높이 = 1)
                    : uv * float2(aspect, 1.0);                                      // 화면 공간(세로 = 1)
                nUV = nUV * _NoiseST.xy + _NoiseST.zw;
                half n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nUV).r;
                half3 noiseCol = lerp(_NoiseDark.rgb, _NoiseBright.rgb, n);
                graded = lerp(graded, noiseCol, boss * (half)_GlitchC.x);

                // CRT 주사선
                half scan = 0.5h + 0.5h * sin(uv0.y * _GlitchLook.x * 6.28318h);
                graded *= lerp(1.0h, 0.72h + 0.28h * scan, (half)_GlitchB.x);

                return half4(graded, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
