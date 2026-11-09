// ----------------------------------------------------------------------------
//  BossVictoryGlitch.shader — 보스 처치 연출 ZZZ 글리치(PLAN-boss-victory-glitch 3단계) 전체 화면 합성
//  반투명(이펙트) 렌더 뒤, 후처리 전. 입력 = _BlitTexture(씬색) + _GlitchIdTex(R 플레이어 · B 보스 마스크).
//  세기는 C#(BossVictoryGlitchRender.SetFrame)이 설정 SO 타임라인 곡선으로 매 프레임 넣는다.
//    UV 왜곡: 가로 늘어남 → 가로줄 물결 → 계단식 세로 어긋남 → 슬라이스 → 블록 모자이크(같은 UV 로 마스크도 읽는다 — 실루엣이 같이 어긋난다)
//    색: RGB 분리 · 가로 스미어 → 흑백(Desat) → 배경만 호박색(Amber, 플레이어 제외, 플레이어는 흑백) → 보스 = 흑백 노이즈 텍스처 UV 애니메이션
//        (흰 = 과노출 밝은 색, 검 = 어두운 적갈색 · 화면 공간 기본 / 보스 기준 공간 토글) → 흑백 플래시 → CRT 주사선.
//  흰 링(충격파)은 없다(사용자 결정).
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

            float4 _GlitchA;      // x master, y time, z desat, w amber
            float4 _GlitchB;      // x scanline, y wave, z step, w stretch
            float4 _GlitchC;      // x block, y smear, z slice, w rgb
            float4 _GlitchD;      // x flash, y charGlitch, z bossNoise, w bossJiggle
            float4 _GlitchLook;   // x 주사선 줄 수, y 물결 주파수, z 블록 크기(px), w 슬라이스 줄 수
            float4 _BossScreen;   // xy 보스 중심(뷰포트), z 보스 화면 높이, w 공간(0 화면 · 1 보스 기준)
            float4 _AmberColor;
            float4 _NoiseST;      // xy 타일링, zw 스크롤(초당)
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
                float master = _GlitchA.x;
                if (master <= 0.0001)
                    return half4(SceneAt(uv0), 1.0h);

                float t = _GlitchA.y;
                float tick = floor(t * 24.0);            // 글리치 난수는 24fps 로 바뀐다(계단·슬라이스·블록)
                float2 res = _ScreenParams.xy;
                float aspect = res.x / res.y;
                float2 uv = uv0;

                // 가로 늘어남(화면 가운데 기준)
                uv.x = 0.5 + (uv.x - 0.5) / (1.0 + _GlitchB.w * 0.35);

                // 가로줄 물결
                uv.x += sin(uv.y * _GlitchLook.y + t * 18.0) * 0.012 * _GlitchB.y;

                // 계단식 세로 어긋남 — 가로 띠마다 위아래로
                float band = floor(uv.y * 14.0);
                uv.y += (Hash(float2(band, tick)) - 0.5) * 0.06 * _GlitchB.z;

                // 슬라이스 — 가는 띠 일부가 옆으로 크게 밀린다
                float slice = floor(uv.y * _GlitchLook.w);
                float sliceOn = step(0.7, Hash(float2(slice, tick + 7.0)));
                uv.x += (Hash(float2(slice, tick + 3.0)) - 0.5) * 0.25 * _GlitchC.z * sliceOn;

                // 블록 모자이크 — 무작위 블록이 덩어리째 옮겨진다
                float2 blockSize = _GlitchLook.z / res;
                float2 blockId = floor(uv / blockSize);
                float blockOn = step(1.0 - 0.35 * _GlitchC.x, Hash(blockId + tick)) * step(0.001, _GlitchC.x);
                float2 blockUV = (blockId + 0.5) * blockSize + (float2(Hash(blockId + 11.0), Hash(blockId + 23.0)) - 0.5) * blockSize * 4.0;
                uv = lerp(uv, blockUV, blockOn);

                // 마스크(같은 왜곡 UV). 보스 외곽 지글 = 줄마다 가로로 흔든 UV 로 보스만 다시 읽는다.
                half4 id = MaskAt(uv);
                float jig = (Hash(float2(floor(uv0.y * res.y / 3.0), tick)) - 0.5) * 0.02 * _GlitchD.w;
                half boss = max(id.b, MaskAt(uv + float2(jig, 0)).b);
                // 플레이어 실루엣 흔들림 — 플레이어 픽셀만 줄마다 가로로
                float charJ = (Hash(float2(floor(uv0.y * 40.0), tick + 5.0)) - 0.5) * 0.03 * _GlitchD.y;
                half player = max(id.r, MaskAt(uv + float2(charJ, 0)).r);
                float2 uvColor = uv + float2(charJ * id.r, 0);

                // RGB 분리 · 가로 스미어
                float rgb = 0.006 * _GlitchC.w;
                half3 col;
                col.r = SceneAt(uvColor + float2(rgb, 0)).r;
                col.g = SceneAt(uvColor).g;
                col.b = SceneAt(uvColor - float2(rgb, 0)).b;
                if (_GlitchC.y > 0.001)
                {
                    half3 acc = col;
                    [unroll] for (int i = 1; i <= 6; i++)
                        acc += SceneAt(uvColor - float2(0.008 * i * _GlitchC.y, 0));
                    col = lerp(col, acc / 7.0h, (half)_GlitchC.y);
                }

                // 흑백 → 배경만 호박색. 플레이어(캐릭터)는 흑백 유지.
                half lum = Luma(col);
                half3 gray = lerp(col, lum.xxx, (half)_GlitchA.z);
                half3 amber = lum * _AmberColor.rgb * 1.6h;
                half3 graded = lerp(gray, amber, (half)_GlitchA.w);
                graded = lerp(graded, lerp(col, lum.xxx, (half)max(_GlitchA.z, _GlitchA.w)), player);

                // 보스 — 흑백 노이즈 UV 애니메이션(흰 = 밝은 색, 검 = 어두운 적갈색)
                float2 nUV = _BossScreen.w > 0.5
                    ? (uv - _BossScreen.xy) * float2(aspect, 1.0) / _BossScreen.z   // 보스 기준(보스 화면 높이 = 1)
                    : uv * float2(aspect, 1.0);                                      // 화면 공간(세로 = 1)
                nUV = nUV * _NoiseST.xy + _NoiseST.zw * t;
                half n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nUV).r;
                half3 noiseCol = lerp(_NoiseDark.rgb, _NoiseBright.rgb, n);
                graded = lerp(graded, noiseCol, boss * (half)_GlitchD.z);

                // 흑백 플래시
                graded = lerp(graded, (Luma(graded) * 1.8h).xxx, (half)_GlitchD.x);

                // CRT 주사선
                half scan = 0.5h + 0.5h * sin(uv0.y * _GlitchLook.x * 6.28318h);
                graded *= lerp(1.0h, 0.72h + 0.28h * scan, (half)_GlitchB.x);

                half3 outCol = lerp(SceneAt(uv0), graded, (half)master);
                return half4(outCol, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
