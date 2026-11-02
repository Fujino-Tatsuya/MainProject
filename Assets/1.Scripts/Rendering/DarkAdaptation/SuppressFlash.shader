// ----------------------------------------------------------------------------
//  SuppressFlash.shader — 제압 간파 '애니메이션 섬광' (할 일 179)
//
//  후처리 뒤(톤매핑 끝난 선형 컬러) 전체 화면 패스 1개. 텍스처 없이 전부 절차 생성.
//   화면 → 흑백(방사 블러) → 네거티브 바탕(어둡게) + 집중선 + 울퉁불퉁한 십자 빛기둥 + 들쭉날쭉 스파이크 별 + 실루엣(흰)
//   → 포지티브 컷은 통째로 뒤집기 → 대비 → 3단 색(짙은·중간·밝은) → 포지티브 실루엣 빗금 → 복귀 섞기 → 가장자리 색수차 → 레터박스.
//  🔴 완벽한 원·고리·가는 선 금지(132 결과가 레퍼런스와 달랐던 이유) — 별 핵도 칸마다 반지름이 달라 다각형으로 끊기고,
//     빛기둥은 최소 굵기 아래로 가늘어지면 지운다.
//  패스 1(SilhouetteMask) = 비트 7 렌더러를 덮어쓰기 재질로 흰색 칠(실루엣 마스크 RT).
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

            #define BLUR_SAMPLES 10

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

            // 방사 블러한 흑백 — 적중점 쪽으로 샘플을 겹쳐 읽어 바깥으로 길게 번진다.
            float BlurredLuma(float2 uv)
            {
                float strength = saturate(_SFLook.x * (0.35 + 0.65 * _SFFx.y));
                float2 toCenter = _SFCenter.xy - uv;
                float sum = 0;
                [unroll]
                for (int i = 0; i < BLUR_SAMPLES; i++)
                {
                    float k = (float)i / (BLUR_SAMPLES - 1);
                    sum += Luma(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + toCenter * (k * strength)).rgb);
                }
                return sum / BLUR_SAMPLES;
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

            // 3단 색 한 픽셀(색수차 전). 반환 = 최종 색.
            float3 Shade(float2 uv, float2 pixel)
            {
                float2 p = ToLocal(uv);
                float seed = _SFCenter.w;

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
                float sil = SAMPLE_TEXTURE2D(_SuppressFlashMask, sampler_LinearClamp, uv).r;
                sil = step(0.5, sil);
                v = lerp(v, 1.0, sil);

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
                    float3 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;
                    col = lerp(col, scene, saturate(_SFTone.z));
                }

                return col;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 pixel = uv * _ScreenParams.xy;

                float3 col;
                float spread = _SFFx.z;
                if (spread > 1e-5)
                {
                    // 가장자리 색수차 — 화면 가운데는 0, 가장자리로 갈수록 R·B 가 적중점 반대/쪽으로 갈라진다.
                    float2 fromMid = (uv - 0.5) * 2.0;
                    float edge = saturate(dot(fromMid, fromMid) * 0.5);
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
                if (uv.y < bar || uv.y > 1.0 - bar)
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
