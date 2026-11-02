// ----------------------------------------------------------------------------
//  SuppressFlash.shader — 제압 간파 '애니메이션 섬광' (할 일 179)
//
//  후처리 뒤(톤매핑 끝난 선형 컬러) 전체 화면 패스 1개. 텍스처 없이 전부 절차 생성.
//   화면 → 흑백(방사 블러) → 네거티브 바탕(어둡게) + 집중선 + 울퉁불퉁한 십자 빛기둥 + 들쭉날쭉 스파이크 별 + 실루엣(흰)
//   → 포지티브 컷은 통째로 뒤집기 → 대비 → 3단 색(짙은·중간·밝은) → 포지티브 실루엣 빗금 → 복귀 섞기 → 가장자리 색수차 → 레터박스.
//  🔴 완벽한 원·고리·가는 선 금지(132 결과가 레퍼런스와 달랐던 이유) — 별 핵도 칸마다 반지름이 달라 다각형으로 끊기고,
//     빛기둥은 최소 굵기 아래로 가늘어지면 지운다.
//  패스 1(SilhouetteMask) = 비트 7 렌더러를 덮어쓰기 재질로 흰색 칠(실루엣 마스크 RT).
//  179 후속 3(레퍼런스 B) — 방향 블러·흔들림·실루엣 밝기·고른 색수차·블룸 명암 컷. 칸이 0 이면 분기를 건너뛰어 A 결과 그대로.
// ----------------------------------------------------------------------------
Shader "Hidden/Rendering/SuppressFlash"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "SuppressFlashComposite"
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_SuppressFlashMask);

            float4 _SFCenter;     // xy=적중점 뷰포트, z=화면비, w=씨앗
            float4 _SFTone;       // x=뒤집기, y=대비, z=복귀, w=빗금
            float4 _SFFx;         // x=별 반지름, y=집중선, z=색수차 번짐, w=레터박스 띠 높이
            float4 _SFCross;      // x=종류(0/1/2), y=중심 굵기, z=가늘어짐, w=울퉁불퉁
            float4 _SFShape;      // x=스파이크 수, y=격자 칸, z=격자 별 반지름, w=최소 굵기
            float4 _SFLook;       // x=방사 블러, y=바탕 밝기, z=빗금 간격(px), w=집중선 수
            float4 _SFThresholds; // x=짙은|중간, y=중간|밝은
            float4 _SFDark;
            float4 _SFMid;
            float4 _SFLight;
            // 179 후속 3(레퍼런스 B) — A 는 전부 0 이라 아래 분기를 건너뛰고 예전 결과 그대로.
            float4 _SFExtra;      // x=블룸 명암(0/1), y=실루엣 밝기 낮춤, z=색수차 고르게
            float4 _SFDir;        // xy=방향 블러 방향(화면 높이 단위), z=길이(화면 높이)
            float4 _SFShake;      // xy=흔들림(UV), z=블룸 세기, w=블룸 반지름
            float4 _SFBloom;      // 블룸 색

            #define BLUR_SAMPLES 10
            #define DIR_SAMPLES 8

            float Hash11(float n) { return frac(sin(n * 127.1 + 311.7) * 43758.5453); }

            float Noise1(float x)
            {
                float i = floor(x);
                float f = frac(x);
                float u = f * f * (3.0 - 2.0 * f);
                return lerp(Hash11(i), Hash11(i + 1.0), u);
            }

            float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

            // 화면 높이 단위 좌표(적중점 원점).
            float2 ToLocal(float2 uv) { return (uv - _SFCenter.xy) * float2(_SFCenter.z, 1.0); }

            // 방향 블러 한 걸음(UV) — 공격 방향 뒤쪽으로 끌린다(0 = 없음).
            float2 DirStepUV() { return -_SFDir.xy * _SFDir.z / float2(max(_SFCenter.z, 1e-4), 1.0); }

            // 방사 블러한 흑백 — 적중점 쪽으로 샘플을 겹쳐 읽어 바깥으로 길게 번진다. 방향 블러가 있으면 그 방향으로도 끈다.
            float BlurredLuma(float2 uv)
            {
                float strength = saturate(_SFLook.x * (0.35 + 0.65 * _SFFx.y));
                float2 toCenter = _SFCenter.xy - uv;
                float2 dirStep = DirStepUV();
                float sum = 0;
                [unroll]
                for (int i = 0; i < BLUR_SAMPLES; i++)
                {
                    float k = (float)i / (BLUR_SAMPLES - 1);
                    sum += Luma(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + toCenter * (k * strength) + dirStep * k).rgb);
                }
                return sum / BLUR_SAMPLES;
            }

            // 실루엣 마스크 — 방향 블러가 있으면 공격 방향 뒤로 길게 끌린 덩어리(흑백 두 색이라 블러 대신 늘인다).
            float SilhouetteMask(float2 uv)
            {
                float sil = SAMPLE_TEXTURE2D(_SuppressFlashMask, sampler_LinearClamp, uv).r;
                if (_SFDir.z > 1e-5)
                {
                    float2 dirStep = DirStepUV();
                    [unroll]
                    for (int i = 1; i < DIR_SAMPLES; i++)
                    {
                        float k = (float)i / (DIR_SAMPLES - 1);
                        float m = SAMPLE_TEXTURE2D(_SuppressFlashMask, sampler_LinearClamp, uv + dirStep * k).r;
                        sil = max(sil, m * (1.0 - 0.45 * k));
                    }
                }
                return step(0.5, sil);
            }

            // 원래 색 — 방향 블러가 있으면 그 방향으로 겹쳐 읽는다(빨강·파랑 궤적 컷).
            float3 SceneColor(float2 uv)
            {
                float3 scene = 0;
                if (_SFDir.z <= 1e-5)
                {
                    scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;
                }
                else
                {
                    float2 dirStep = DirStepUV();
                    [unroll]
                    for (int i = 0; i < DIR_SAMPLES; i++)
                    {
                        float k = (float)i / (DIR_SAMPLES - 1);
                        scene += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + dirStep * k).rgb;
                    }
                    scene /= DIR_SAMPLES;
                }
                return scene;
            }

            // 빛기둥 하나 — along 축을 따라 중심에서 굵고 바깥으로 지수로 가늘어지며, 가장자리는 노이즈로 울퉁불퉁.
            float Pillar(float along, float across, float width, float taper, float rough, float minWidth, float seed)
            {
                float a = abs(along);
                float w = width * exp(-a * taper);
                float n = Noise1(along * 14.0 + seed * 7.31) * 0.65 + Noise1(along * 41.0 + seed * 3.17) * 0.35;
                w *= 1.0 + rough * (n * 2.0 - 1.0);
                w = max(w, 0.0);
                float fade = smoothstep(minWidth, minWidth * 2.0, w);   // 가는 선 금지
                float aa = max(fwidth(across), 1e-5);
                return (1.0 - smoothstep(w - aa, w + aa, abs(across))) * fade;
            }

            float CrossShape(float2 p, float width, float taper, float rough, float minWidth, float seed)
            {
                return max(Pillar(p.x, p.y, width, taper, rough, minWidth, seed),
                           Pillar(p.y, p.x, width * 0.85, taper, rough, minWidth, seed + 19.0));
            }

            // 스파이크 별 — 칸(스파이크)마다 길이·핵 반지름이 제각각이라 원도 고리도 아니다.
            float Star(float2 p, float radius, float spikes, float seed)
            {
                if (radius <= 1e-4)
                    return 0.0;

                float r = length(p);
                float sector = (atan2(p.y, p.x) / 6.2831853 + 0.5) * spikes;
                float idx = floor(sector);
                float f = frac(sector);
                float len = lerp(0.35, 1.0, Hash11(idx + seed * 13.1));
                float core = lerp(0.18, 0.36, Hash11(idx * 3.7 + seed * 5.3));
                float tri = 1.0 - abs(f - 0.5) * 2.0;
                float edge = radius * (core + (1.0 - core) * len * pow(tri, 2.5));
                float aa = max(fwidth(r), 1e-5);
                return 1.0 - smoothstep(edge - aa, edge + aa, r);
            }

            // 방사 집중선 — 쐐기(바깥으로 넓어짐)라 가는 선이 아니다. 시작 반지름·굵기가 줄마다 다르다.
            float SpeedLines(float2 p, float amount, float count, float seed)
            {
                if (amount <= 1e-4)
                    return 0.0;

                float r = length(p);
                float s = (atan2(p.y, p.x) / 6.2831853 + 0.5) * count;
                float idx = floor(s);
                float f = frac(s);
                float on = step(0.42, Hash11(idx + seed * 5.1));
                float halfWidth = lerp(0.12, 0.38, Hash11(idx * 1.7 + seed));
                float start = lerp(0.12, 0.5, Hash11(idx * 2.3 + seed * 0.7));
                float wedge = 1.0 - smoothstep(halfWidth - 0.04, halfWidth, abs(f - 0.5));
                return on * wedge * smoothstep(start, start + 0.12, r) * saturate(amount * 1.3);
            }

            // 블룸 명암 컷(179 후속 3) — 원래 색(방사 블러로 캐릭터가 뭉개짐) + 적중점 컬러 블룸·스파이크 별·집중선. 3단 색 없음.
            // 블룸 테두리는 각도마다 반지름이 달라 완벽한 원이 아니다. 대비(섬광 줄이기)는 블룸 세기에 이미 곱해져 온다.
            float3 BloomShade(float2 uv, float2 p, float seed)
            {
                float strength = saturate(_SFLook.x * (0.35 + 0.65 * _SFFx.y));
                float2 toCenter = _SFCenter.xy - uv;
                float3 col = 0;
                [unroll]
                for (int i = 0; i < BLUR_SAMPLES; i++)
                {
                    float k = (float)i / (BLUR_SAMPLES - 1);
                    col += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + toCenter * (k * strength)).rgb;
                }
                col /= BLUR_SAMPLES;

                float amount = _SFShake.z;
                float r = length(p);
                float a = atan2(p.y, p.x);
                float wobble = 1.0 + 0.22 * sin(a * 3.0 + seed) + 0.14 * sin(a * 7.0 + seed * 2.1) + 0.08 * sin(a * 13.0 + seed * 0.7);
                float rr = r / max(_SFShake.w * wobble, 1e-4);
                float glow = exp(-rr * rr * 2.5) * amount;
                float star = Star(p, _SFFx.x, max(5.0, _SFShape.x), seed) * saturate(amount);
                float lines = SpeedLines(p, _SFFx.y, _SFLook.w, seed) * 0.35 * saturate(amount);

                col += _SFBloom.rgb * (glow + lines);
                col = lerp(col, lerp(_SFBloom.rgb, 1.0, 0.7), star);
                return col;
            }

            // 3단 색 한 픽셀(색수차 전). 반환 = 최종 색.
            float3 ToneShade(float2 uv, float2 pixel, float2 p, float seed)
            {
                // 네거티브 바탕 — 흑백을 눌러 어둡게.
                float v = saturate(pow(saturate(BlurredLuma(uv)), 1.2) * _SFLook.y);

                v = max(v, SpeedLines(p, _SFFx.y, _SFLook.w, seed));

                int crossType = (int)round(_SFCross.x);
                if (crossType >= 1)
                    v = max(v, CrossShape(p, _SFCross.y, _SFCross.z, _SFCross.w, _SFShape.w, seed));
                if (crossType == 2)
                {
                    // 격자 — 칸마다 작은 십자 + 교차점 작은 별(가운데 칸은 위의 큰 십자).
                    float cell = max(_SFShape.y, 0.05);
                    float2 id = round(p / cell);
                    float2 q = p - id * cell;
                    float cellSeed = seed + id.x * 31.7 + id.y * 57.3;
                    float small = CrossShape(q, _SFCross.y * 0.38, _SFCross.z * 3.2 / cell, _SFCross.w,
                                             _SFShape.w, cellSeed);
                    small = max(small, Star(q, _SFShape.z, max(5.0, _SFShape.x - 3.0), cellSeed));
                    v = max(v, small);
                }

                v = max(v, Star(p, _SFFx.x, max(5.0, _SFShape.x), seed));

                // 실루엣 — 네거티브에서 흰색(뒤집으면 짙은색).
                float sil = SilhouetteMask(uv);
                v = lerp(v, 1.0 - _SFExtra.y, sil);

                if (_SFTone.x > 0.5)
                    v = 1.0 - v;

                v = lerp(0.5, v, saturate(_SFTone.y));

                float3 col = v < _SFThresholds.x ? _SFDark.rgb : (v < _SFThresholds.y ? _SFMid.rgb : _SFLight.rgb);

                // 빗금 — 실루엣 안 굵은 사선(화면 픽셀 기준).
                if (_SFTone.w > 0.5 && sil > 0.5)
                {
                    float period = max(_SFLook.z, 2.0);
                    float stripe = step(0.5, frac((pixel.x + pixel.y) / period));
                    col = lerp(col, _SFMid.rgb, stripe);
                }

                if (_SFTone.z > 0.0)
                {
                    float3 scene = SceneColor(uv);
                    col = lerp(col, scene, saturate(_SFTone.z));
                }

                return col;
            }

            // 한 픽셀(색수차 전) — 블룸 명암 컷(179 후속 3)이면 블룸, 아니면 3단 색(A 그대로).
            float3 Shade(float2 uv, float2 pixel)
            {
                float2 p = ToLocal(uv);
                float seed = _SFCenter.w;
                float3 col = 0;
                if (_SFExtra.x > 0.5)
                    col = BloomShade(uv, p, seed);
                else
                    col = ToneShade(uv, pixel, p, seed);
                return col;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUv = input.texcoord;
                float2 uv = screenUv + _SFShake.xy;   // 흔들림(179 후속 3, A = 0) — 화면·마스크를 같이 민다. 레터박스는 화면 고정
                float2 pixel = uv * _ScreenParams.xy;

                float3 col;
                float spread = _SFFx.z;
                if (spread > 1e-5)
                {
                    // 가장자리 색수차 — 화면 가운데는 0, 가장자리로 갈수록 R·B 가 적중점 반대/쪽으로 갈라진다.
                    // 고르게(179 후속 3) = 가운데도 같은 세기 → 캐릭터 외곽이 RGB 로 갈라진다.
                    float2 fromMid = (uv - 0.5) * 2.0;
                    float edge = saturate(dot(fromMid, fromMid) * 0.5);
                    edge = lerp(edge, 1.0, saturate(_SFExtra.z));
                    float2 dir = normalize(uv - _SFCenter.xy + 1e-5) * float2(1.0 / max(_SFCenter.z, 1e-4), 1.0);
                    float2 offset = dir * spread * edge;
                    col.r = Shade(uv + offset, pixel).r;
                    col.g = Shade(uv, pixel).g;
                    col.b = Shade(uv - offset, pixel).b;
                }
                else
                {
                    col = Shade(uv, pixel);
                }

                // 레터박스 — 위아래 검은 띠(들어온 정도만큼).
                float bar = _SFFx.w;
                if (screenUv.y < bar || screenUv.y > 1.0 - bar)
                    col = 0;

                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // 패스 1 — 실루엣 마스크. 덮어쓰기 재질로 비트 7 렌더러를 흰색으로 칠한다(깊이 판정 없음 — 벽 뒤도 납작 실루엣).
        Pass
        {
            Name "SilhouetteMask"
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex VertMask
            #pragma fragment FragMask

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct AttributesMask
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsMask
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            VaryingsMask VertMask(AttributesMask input)
            {
                VaryingsMask output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 FragMask(VaryingsMask input) : SV_Target
            {
                return half4(1.0, 1.0, 1.0, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
