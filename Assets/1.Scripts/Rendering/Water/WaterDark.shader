// ----------------------------------------------------------------------------
//  WaterDark.shader - 어비스 불투명 어두운 물 (Unlit 1패스)
//
//  방식 확정(2026-07, FOG_NEXT_PLAN.md Task 2):
//  - Opaque + ZWrite On + Unlit → 물 안쪽/바닥 절대 안 보임, 쿼드 1장 드로우콜 +1
//  - 물결 = 절차 밸류노이즈 2겹(서로 다른 scale·스크롤) → _FlowColor 얇게 lerp
//  - fake 깊이 = 저주파 노이즈 명암(정적) + (per-hole 쿼드용) UV 가장자리 밝힘
//  - 금지: 실시간 반사/굴절/투명 블렌딩/버텍스 웨이브 (프레임 예산)
//  - Geometry 큐에서 depth를 쓰므로 FullScreenFog의 FoW/LoS 디밍이 물 위에도 정상 적용
//
//  2026-07-09 튠(Dredge 레퍼런스 참고): 구조는 그대로 두고 기본값만 재조정.
//  목표 = 첫번째 참고 이미지(탁하고 흐린 저채도 늪물)의 질감 + 두번째 이미지(심연) 급 어둠.
//  - 노이즈 스케일↓(더 큰 뭉텅 패치) + Sharp↓(경계 흐리게) + Strength↓(저대비, 탁한 느낌)
//  - DeepColor를 거의 무채색 블랙으로, FlowColor도 채도·밝기 낮춰 반사감 제거
//  - DepthDarken↑로 불균일한 탁함(패치별 명암차) 강화
//  색수차/반사/굴절은 의도적으로 이 셰이더 스코프 밖(요청 시 별도 포스트프로세싱).
//
//  2026-07-09 추가(경석 요청 — "그냥 스크롤 텍스처"가 아니라 깊이감이 필요함):
//  fake 깊이 명암을 단일 저주파 노이즈 → 3겹 fBm(옥타브마다 스케일↑·진폭↓·드리프트 방향 다름)
//  으로 교체. 작은 탁한 얼룩이 큰 탁한 얼룩 안에 겹쳐 보이며 "layered murk" 깊이 착시를 만듦.
//  주변 조명/그림자/반사에는 여전히 반응하지 않는 순수 위치 기반 절차 노이즈(요청 확인됨:
//  "주변에 반응하지 않고 그림자도 안 비치는" 느낌 유지 — 유기적 왜곡/도메인 워프는 불필요).
// ----------------------------------------------------------------------------
Shader "Custom/WaterDark"
{
    Properties
    {
        _DeepColor    ("Deep Color (심연색)", Color) = (0.008, 0.010, 0.014, 1)
        _FlowColor    ("Flow Color (물결색)", Color) = (0.05, 0.07, 0.08, 1)
        _FlowSpeed1   ("Flow Speed 1 (xy=월드 xz/s)", Vector) = (0.3, 0.12, 0, 0)
        _FlowSpeed2   ("Flow Speed 2 (xy=월드 xz/s)", Vector) = (-0.2, 0.25, 0, 0)
        _FlowScale1   ("Flow Scale 1 (노이즈 UV 스케일)", Float) = 0.08
        _FlowScale2   ("Flow Scale 2", Float) = 0.18
        _FlowStrength ("Flow Strength (물결 세기)", Range(0, 1)) = 0.22
        _FlowSharp    ("Flow Sharpness (물결 얇기)", Range(1, 8)) = 1.2
        _DepthNoiseScale ("Fake Depth Noise Scale (저주파, 옥타브1 기준)", Float) = 0.015
        _DepthDarken  ("Fake Depth Darken (깊이 명암)", Range(0, 1)) = 0.5
        _DepthOctaveScale   ("Depth Octave Scale Mult (옥타브당 스케일 배율)", Float) = 2.1
        _DepthOctaveFalloff ("Depth Octave Falloff (옥타브당 진폭 감쇠)", Range(0, 1)) = 0.55
        _DepthDrift1  ("Depth Drift 1 (xy=월드 xz/s, 느리게)", Vector) = (0.03, 0.015, 0, 0)
        _DepthDrift2  ("Depth Drift 2", Vector) = (-0.02, 0.025, 0, 0)
        _DepthDrift3  ("Depth Drift 3", Vector) = (0.015, -0.02, 0, 0)
        _EdgeBrighten ("Edge Brighten (UV 가장자리, per-hole 쿼드용)", Range(0, 1)) = 0
        _EdgeWidth    ("Edge Width (UV 비율)", Range(0.01, 0.5)) = 0.08

        // ── 수심 (2026-09-15 추가 · FlatKit 의 Colors 그룹에 대응) ────────────────
        [Header(Depth)]
        [Toggle] _UseSceneDepth ("Use Scene Depth (끄면 전부 깊은 물로 취급)", Float) = 1
        _ShallowColor ("Shallow Color (얕은 물색)", Color) = (0.22, 0.75, 0.72, 1)
        _ShallowDepth ("Shallow Depth (이 깊이까지 얕은색, m)", Float) = 0.6
        _GradientSize ("Gradient Size (얕은→깊은 전환 거리, m)", Float) = 6.0

        // ── 물가 띠 (FlatKit 의 Crest) ──────────────────────────────────────────
        [Header(Shore)]
        _ShoreColor    ("Shore Color (물가 띠 색)", Color) = (0.75, 0.98, 0.95, 1)
        _ShoreWidth    ("Shore Width (띠 폭, m)", Float) = 0.35
        _ShoreSharp    ("Shore Sharpness (띠 경계 날카로움)", Range(1, 8)) = 2.5
        _ShoreStrength ("Shore Strength (띠 세기)", Range(0, 1)) = 0.9

        // ── 흐름 + 파도 (Current) ──────────────────────────────────────────────
        // 🔴 물 전체가 **한 방향으로** 흘러야 한다(2026-09-15 팀장). 이전 판은 위상을
        //    위치 노이즈로만 흔들어서 "해안선을 따라 순서대로 깜빡이는" 그림이 됐다 —
        //    흐르는 게 아니라 제자리에서 맥박치는 것이다.
        //    아래 방향 하나가 깊은 물 무늬·거품·파도 마루를 **전부** 끌고 간다.
        [Header(Current)]
        _FlowDir    ("Flow Direction (xy = 월드 XZ. 크기는 무시)", Vector) = (1, 0.35, 0, 0)
        _FlowMaster ("Flow Speed (수면 전체가 흘러가는 속도, m/s)", Float) = 0.6

        // 🔴 흐름의 정체는 이것이다(2026-09-15 레퍼런스 영상 0:29~0:31 프레임 분석).
        //    FlatKit 은 **수심 값 자체를 흐르는 노이즈로 왜곡한 뒤** 색을 고른다.
        //    그래서 얕은/깊은 경계가 넝마처럼 들쭉날쭉하고, 그 경계가 통째로 밀리면서
        //    모양이 변한다 — 손가락처럼 뻗은 얕은 물이 깊은 쪽으로 들어가고 반대도 생긴다.
        //    수심을 지오메트리 그대로 쓰면 경계가 깔끔한 등고선이 되어 절대 안 움직인다.
        _DepthWarpAmount ("Depth Warp (수심을 흔드는 폭, m — 흐름의 핵심)", Float) = 2.5
        _DepthWarpScale  ("Depth Warp Scale (왜곡 얼룩 크기)", Float) = 0.06

        _WaveLength ("Wave Length (벽을 치는 마루 간격, m)", Float) = 18
        _WaveSpeed  ("Wave Speed (마루가 지나가는 빈도, 초당)", Float) = 0.22
        _WaveJitter ("Wave Jitter (마루 구부러짐 — 0 이면 자로 잰 직선)", Range(0, 1)) = 0.35
        _WaveJitterScale ("Wave Jitter Scale (구부러짐 크기)", Float) = 0.03
        _WaveContrast ("Wave Contrast (열린 수면 띠 — 왜곡이 주역이라 기본 0)", Range(0, 0.3)) = 0
        _LapAmount  ("Lap Amount (벽에 철썩일 때 물가가 밀리는 폭, m)", Float) = 0.5

        // ── 거품 (FlatKit 의 Foam) ──────────────────────────────────────────────
        [Header(Foam)]
        _FoamColor      ("Foam Color (거품색)", Color) = (0.85, 1.0, 0.97, 1)
        _FoamAmount     ("Foam Amount (수면 전체 거품량)", Range(0, 1)) = 0.24
        _FoamScale      ("Foam Scale (얼룩 크기 — 작을수록 큰 얼룩)", Float) = 0.30
        _FoamSharpness  ("Foam Sharpness (얼룩 경계 — 1 이면 뭉개짐)", Range(1, 24)) = 12
        _FoamSpeed      ("Foam Speed (xy=월드 xz/s)", Vector) = (0.05, 0.03, 0, 0)
        _FoamShoreDepth ("Foam Shore Depth (물가에서 거품이 몰리는 깊이, m)", Float) = 1.2
        _FoamShoreBlend ("Foam Shore Blend (물가 거품 가산량)", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "WaterDarkUnlit"
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // 🔴 수심을 재려면 물 **뒤**의 깊이가 필요하다.
            //    이 셰이더는 패스가 하나뿐이고 LightMode 태그가 없어서(= SRPDefaultUnlit)
            //    뎁스(노멀) 프리패스에 **들어가지 않는다.** 그래서 _CameraDepthTexture 를 읽으면
            //    자기 자신이 아니라 물 뒤 바닥·벽의 깊이가 나온다 — Transparent 큐로 옮기지 않고도
            //    수심을 구할 수 있는 이유가 이것이다.
            //    ⚠️ 프리패스는 SSAO(AfterOpaque=0)가 만든다. SSAO 를 끄면 이 전제가 무너지고
            //       수심이 전부 0 으로 읽혀 화면이 통째로 얕은색이 된다 — 그때는 _UseSceneDepth 를 끈다.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor;
                float4 _FlowColor;
                float4 _FlowSpeed1;
                float4 _FlowSpeed2;
                float  _FlowScale1;
                float  _FlowScale2;
                float  _FlowStrength;
                float  _FlowSharp;
                float  _DepthNoiseScale;
                float  _DepthDarken;
                float  _DepthOctaveScale;
                float  _DepthOctaveFalloff;
                float4 _DepthDrift1;
                float4 _DepthDrift2;
                float4 _DepthDrift3;
                float  _EdgeBrighten;
                float  _EdgeWidth;

                float  _UseSceneDepth;
                float4 _ShallowColor;
                float  _ShallowDepth;
                float  _GradientSize;

                float4 _ShoreColor;
                float  _ShoreWidth;
                float  _ShoreSharp;
                float  _ShoreStrength;

                float4 _FlowDir;
                float  _FlowMaster;
                float  _DepthWarpAmount;
                float  _DepthWarpScale;
                float  _WaveLength;
                float  _WaveSpeed;
                float  _WaveJitter;
                float  _WaveJitterScale;
                float  _WaveContrast;
                float  _LapAmount;

                float4 _FoamColor;
                float  _FoamAmount;
                float  _FoamScale;
                float  _FoamSharpness;
                float4 _FoamSpeed;
                float  _FoamShoreDepth;
                float  _FoamShoreBlend;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float2 uv          : TEXCOORD1;
            };

            // FogCore.hlsl 의 Fog_Hash21/Fog_ValueNoise 와 동일 패턴(로컬 복제 —
            // FogCore 는 전역 프로퍼티 선언이 많아 include 하지 않는다).
            float Water_Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Water_ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = Water_Hash21(i);
                float b = Water_Hash21(i + float2(1, 0));
                float c = Water_Hash21(i + float2(0, 1));
                float d = Water_Hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = IN.positionWS.xz;

                // ── 수심 ───────────────────────────────────────────────────────
                // 🔴 **수직** 수심을 쓴다(월드 Y 차이). 시선 방향 거리가 아니다.
                //    시선 거리로 재면 수평선 쪽에서 시선이 수면과 나란해져 거리가 발산한다 —
                //    "가까운 물은 얕고 먼 물은 깊다"는 틀린 그림이 나온다(2026-09-15 실측으로 확인).
                //    수직 수심은 카메라 각도·거리와 무관해서 같은 깊이면 화면 어디서나 같은 색이다.
                float waterDepth = 1e6;
                if (_UseSceneDepth > 0.5)
                {
                    float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionHCS);
                    float  sceneRaw = SampleSceneDepth(screenUV);
                    float3 sceneWS  = ComputeWorldSpacePosition(screenUV, sceneRaw, UNITY_MATRIX_I_VP);
                    waterDepth = max(0.0, IN.positionWS.y - sceneWS.y);
                }

                // ── 흐름 ───────────────────────────────────────────────────────
                // 🔴 방향 하나가 무늬·거품·파도를 전부 끌고 간다. 요소마다 따로 움직이면
                //    "흐른다"가 아니라 "각자 논다"로 보인다.
                //    p 자체를 흐름 반대로 밀어 두면, 아래 모든 샘플이 자동으로 같이 흘러간다.
                float2 flowDir = normalize(_FlowDir.xy + float2(1e-5, 0));
                float2 pf = p - flowDir * (_FlowMaster * _Time.y);

                // ── 수심 왜곡 ──────────────────────────────────────────────────
                // 🔴 흐름의 정체. 수심 값을 흐르는 노이즈로 흔든 뒤 색을 고른다.
                //    지오메트리 수심을 그대로 쓰면 얕은/깊은 경계가 깔끔한 등고선이 되어
                //    영원히 안 움직인다. 흔들면 경계가 넝마처럼 들쭉날쭉해지고,
                //    노이즈가 흐르므로 그 경계가 통째로 밀리면서 모양이 변한다.
                //    (레퍼런스 영상 0:29~0:31 프레임 대조로 확인한 구조다.)
                // 2겹인 이유: 1겹이면 얼룩이 한 가지 크기라 규칙적으로 보인다.
                float w1 = Water_ValueNoise(pf * _DepthWarpScale);
                float w2 = Water_ValueNoise(pf * (_DepthWarpScale * 2.3) + 17.0);
                float warp = ((w1 * 0.65 + w2 * 0.35) - 0.5) * 2.0 * _DepthWarpAmount;
                float shadedDepth = max(0.0, waterDepth + warp);

                // 얕은 → 깊은 전환. _ShallowDepth 까지는 온전히 얕은색, 그 뒤 _GradientSize 만큼 섞인다.
                float depthT = saturate((shadedDepth - _ShallowDepth) / max(_GradientSize, 1e-3));

                // ── 물결 ───────────────────────────────────────────────────────
                // 노이즈 2겹(서로 다른 스케일·방향 스크롤). 두 겹이 겹치는 곳만 얇게
                // 밝아지도록 곱 + 샤프닝 → 흐르는 줄기 느낌.
                float n1 = Water_ValueNoise(pf * _FlowScale1 + _FlowSpeed1.xy * (_Time.y * 2) * _FlowScale1);
                float n2 = Water_ValueNoise(pf * _FlowScale2 + _FlowSpeed2.xy * (_Time.y * 2) * _FlowScale2);
                float flow = pow(saturate(n1 * n2 * 2.2), _FlowSharp);

                float3 baseCol = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthT);
                float3 col = lerp(baseCol, _FlowColor.rgb, saturate(flow * _FlowStrength));

                // fake 깊이감: 3옥타브 fBm — 옥타브마다 스케일↑·진폭↓·드리프트 방향이 달라서
                // 작은 탁한 얼룩이 큰 탁한 얼룩 안에 겹쳐 보이는 "layered murk" 착시를 만듦.
                // 조명/카메라 각도와 무관한 순수 월드 XZ 기반 — 반사·그림자 반응 없음(의도).
                // 🔴 pf(흐름이 적용된 좌표)를 쓴다. p 로 두면 탁함만 제자리에 붙박여
                //    나머지가 흘러도 물이 고여 보인다.
                float depthFreq = _DepthNoiseScale;
                float depthAmp  = 1.0;
                float depthSum  = 0.0;
                float depthNorm = 0.0;

                float2 dp1 = pf * depthFreq + _DepthDrift1.xy * _Time.y * depthFreq;
                depthSum  += depthAmp * Water_ValueNoise(dp1);
                depthNorm += depthAmp;
                depthFreq *= _DepthOctaveScale;
                depthAmp  *= _DepthOctaveFalloff;

                float2 dp2 = pf * depthFreq + _DepthDrift2.xy * _Time.y * depthFreq;
                depthSum  += depthAmp * Water_ValueNoise(dp2);
                depthNorm += depthAmp;
                depthFreq *= _DepthOctaveScale;
                depthAmp  *= _DepthOctaveFalloff;

                float2 dp3 = pf * depthFreq + _DepthDrift3.xy * _Time.y * depthFreq;
                depthSum  += depthAmp * Water_ValueNoise(dp3);
                depthNorm += depthAmp;

                float dn = depthSum / max(1e-4, depthNorm);
                // 🔴 탁함은 깊은 곳에만 얹는다(depthT 로 가중). 얕은 물가까지 어둡게 하면
                //    레퍼런스의 "가장자리가 밝다"가 죽는다.
                col *= lerp(1.0, 1.0 - _DepthDarken * depthT, dn);

                // ── 진행 파도 ──────────────────────────────────────────────────
                // 🔴 위상 = (위치·흐름방향) − 시간. 이래야 마루가 **흐름 방향으로 이동**한다.
                //    이전 판은 위상이 위치 노이즈뿐이라 마루가 제자리에서 깜빡였다
                //    ("해안선을 따라 순서대로 지나가는" 그림의 원인).
                // 지터는 마루를 구부리는 용도다 — 0 이면 자로 잰 직선이 되어 인공적이다.
                float along = dot(p, flowDir) / max(_WaveLength, 0.01);
                float jitter = Water_ValueNoise(pf * _WaveJitterScale) - 0.5;
                float lapWave = sin((along - _Time.y * _WaveSpeed + jitter * _WaveJitter) * 6.2831);

                // 마루가 벽에 닿는 순간 물가가 밀려 올라가고, 지나가면 내려간다.
                // 🔴 왜곡된 수심(shadedDepth) 위에 얹는다. 원래 수심에 얹으면 물가 띠만
                //    매끈한 등고선으로 남아 왜곡된 색 경계와 따로 놀아 두 겹으로 보인다.
                float shoreDepth = max(0.0, shadedDepth + lapWave * _LapAmount);

                // 열린 수면에도 같은 마루를 약하게 얹는다 — 이게 있어야 깊은 물도 함께 흐른다.
                // 🔴 약하게(_WaveContrast 기본 0.06). 세게 걸면 수면 전체가 맥박친다.
                col *= 1.0 + lapWave * _WaveContrast;

                // ── 거품 ───────────────────────────────────────────────────────
                // 경계가 또렷한 얼룩이 레퍼런스의 인상을 만든다 — 노이즈를 세게 계단화한다.
                // 물가에서는 _FoamShoreBlend 만큼 더 몰린다(파도를 따라 같이 움직인다).
                float foamN = Water_ValueNoise(pf * _FoamScale + _FoamSpeed.xy * _Time.y * _FoamScale);
                float shoreT = 1.0 - saturate(shoreDepth / max(_FoamShoreDepth, 1e-3));
                float foamWant = saturate(_FoamAmount + shoreT * _FoamShoreBlend);
                // foamWant 가 클수록 문턱이 낮아져 얼룩이 넓어진다.
                float foam = saturate((foamN - (1.0 - foamWant)) * _FoamSharpness);
                col = lerp(col, _FoamColor.rgb, foam * _FoamColor.a);

                // ── 물가 띠 ────────────────────────────────────────────────────
                // 물이 벽·바닥과 만나는 선. 이게 있어야 "물이 차 있다"로 읽힌다.
                float shoreLine = pow(1.0 - saturate(shoreDepth / max(_ShoreWidth, 1e-3)), _ShoreSharp);
                col = lerp(col, _ShoreColor.rgb, shoreLine * _ShoreStrength);

                // UV 가장자리 밝힘 — 구멍에 딱 맞춘 쿼드에서만 의미 있음(메가 플레인은 0 유지).
                float2 e = min(IN.uv, 1.0 - IN.uv);
                float rim = 1.0 - saturate(min(e.x, e.y) / max(1e-4, _EdgeWidth));
                col = lerp(col, col + _FlowColor.rgb, rim * rim * _EdgeBrighten);

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
