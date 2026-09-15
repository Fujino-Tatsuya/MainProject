// ----------------------------------------------------------------------------
//  WaterDark.shader - 어비스 불투명 어두운 물 (Unlit 1패스)
//
//  방식 확정(2026-07, FOG_NEXT_PLAN.md Task 2):
//  - Opaque + ZWrite On + Unlit → 물 안쪽/바닥 절대 안 보임, 쿼드 1장 드로우콜 +1
//  - 물결 = 절차 밸류노이즈 2겹(서로 다른 scale·스크롤) → _FlowColor 얇게 lerp
//  - fake 깊이 = 저주파 노이즈 명암(정적) + (per-hole 쿼드용) UV 가장자리 밝힘
//  - 금지: 실시간 반사/굴절/투명 블렌딩/버텍스 웨이브 (프레임 예산)
//    ⚠️ 2026-09-15 추가된 수면 하이라이트는 이 금지에 걸리지 않는다 — 씬을 읽지 않고
//       고정 광원 방향 + 절차 노멀로 만드는 툰 스펙큘러라 반사도 굴절도 아니다.
//       정점도 안 움직인다(노멀은 프래그먼트 유한차분).
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

        // 🔴 흐름의 절반은 이것이다(2026-09-15 레퍼런스 영상 0:29~0:31 프레임 분석).
        //    나머지 절반은 아래 _WaveAmp 쪽 — 이 얼룩만으로는 벽과 아무 상호작용이 없다.
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

        // 🔴 사인 하나면 마루가 전부 같은 크기·같은 방향으로 줄지어 행진해서 물이 아니라
        //    빨래판으로 보인다(2026-09-15 팀장). 방향·파장·속도가 다른 파를 겹친다.
        //    파장 배수를 0.5·2.0 같은 정수비로 두면 안 된다 — 마디가 제자리에 고정되어
        //    "겹쳤다"가 아니라 "더 복잡한 빨래판"이 된다. 아래 비율은 일부러 어긋나 있다.
        _Wave2Angle ("Wave 2 Angle (1번 대비 방향차, 도)", Range(-90, 90)) = 34
        _Wave2Len   ("Wave 2 Length (x Wave Length)", Float) = 0.58
        _Wave2Speed ("Wave 2 Speed (x Wave Speed)", Float) = 1.35
        _Wave2Amp   ("Wave 2 Amplitude (1번 대비)", Range(0, 1)) = 0.6
        _Wave3Angle ("Wave 3 Angle (1번 대비 방향차, 도)", Range(-90, 90)) = -57
        _Wave3Len   ("Wave 3 Length (x Wave Length)", Float) = 1.9
        _Wave3Speed ("Wave 3 Speed (x Wave Speed)", Float) = 0.7
        _Wave3Amp   ("Wave 3 Amplitude (1번 대비)", Range(0, 1)) = 0.45
        _WaveContrast ("Wave Contrast (열린 수면 띠 — 수심이 이미 움직이므로 기본 0)", Range(0, 0.3)) = 0

        // 🔴 여기부터가 "흐르는 텍스처"와 "물"을 가르는 부분이다(2026-09-15 팀장 지적).
        //    이전 판은 흐르는 얼룩과 벽을 치는 파도가 **서로 다른 값**이라 둘 사이에 인과가 없었다.
        //    이제 수면 높이 하나가 열린 수면·물가·거품을 전부 몰고 간다.
        _WaveAmp    ("Wave Amplitude (마루가 수심을 흔드는 폭, m)", Float) = 1.2
        _RunupAsym  ("Runup Asymmetry (1=대칭. 크면 철썩 밀려왔다 천천히 빠진다)", Range(1, 4)) = 2.2
        _ShoreGain  ("Shore Gain (물가에서 파도가 커지는 배수 — 천해 증폭)", Range(0, 6)) = 2.5
        _ShoreGainDepth ("Shore Gain Depth (이 깊이 안쪽부터 증폭, m)", Float) = 5
        _Windward   ("Windward Only (1=흐름이 부딪히는 벽만 친다. 0=사방 균일)", Range(0, 1)) = 0.85

        // ── 거품 (FlatKit 의 Foam) ──────────────────────────────────────────────
        [Header(Foam)]
        _FoamColor      ("Foam Color (거품색)", Color) = (0.85, 1.0, 0.97, 1)
        _FoamAmount     ("Foam Amount (수면 전체 거품량)", Range(0, 1)) = 0.24
        _FoamScale      ("Foam Scale (얼룩 크기 — 작을수록 큰 얼룩)", Float) = 0.30
        _FoamSharpness  ("Foam Sharpness (얼룩 경계 — 1 이면 뭉개짐)", Range(1, 24)) = 12
        _FoamSpeed      ("Foam Speed (xy=월드 xz/s)", Vector) = (0.05, 0.03, 0, 0)
        _FoamShoreDepth ("Foam Shore Depth (물가에서 거품이 몰리는 깊이, m)", Float) = 1.2
        _FoamShoreBlend ("Foam Shore Blend (물가 거품 가산량)", Range(0, 1)) = 0.5

        // ── 수면 하이라이트 (FlatKit 의 Specular) ───────────────────────────────
        // 🔴 반사가 **아니다.** 실시간 반사/굴절은 이 셰이더 스코프 밖이고 금지다(프레임 예산).
        //    고정 광원 방향 + 파도에서 뽑은 가짜 노멀로 툰 스펙큘러 띠만 얹는다.
        //    레퍼런스가 물로 읽히는 이유의 절반이 이 띠다 — 없으면 아무리 잘 흘러도
        //    수면이 아니라 바닥에 칠한 무늬로 보인다(2026-09-15 팀장: "결국 다른 뷰").
        [Header(Surface Highlight)]
        _HlColor    ("Highlight Color", Color) = (0.78, 1.0, 1.0, 1)
        _HlStrength ("Highlight Strength (0 이면 완전히 끈다)", Range(0, 2)) = 0.55
        _HlDir      ("Highlight Light Dir (월드 방향 — 씬 조명과 무관한 고정값)", Vector) = (0.45, 0.8, 0.4, 0)
        _HlPower    ("Highlight Power (띠 얇기)", Range(1, 256)) = 48
        _HlThreshold("Highlight Threshold (이 값 아래는 안 뜬다 — 계단화 문턱)", Range(0, 1)) = 0.3
        _HlSoft     ("Highlight Softness (0 에 가까울수록 툰다운 딱딱한 경계)", Range(0.001, 0.5)) = 0.07
        _HlBump     ("Highlight Bump (수면 기울기 과장 — 0 이면 거울처럼 평평)", Range(0, 4)) = 1.2
        _HlEps      ("Highlight Sample Step (기울기를 재는 간격, m)", Float) = 0.6
        _HlRipple   ("Ripple Height (잔물결 높이, m)", Float) = 0.25
        _HlRippleScale ("Ripple Scale (잔물결 얼룩 크기 — 클수록 잘다)", Float) = 0.5
        _HlCrestH   ("Crest Height (마루가 노멀에 기여하는 높이, m)", Float) = 0.35
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
                float  _Wave2Angle;
                float  _Wave2Len;
                float  _Wave2Speed;
                float  _Wave2Amp;
                float  _Wave3Angle;
                float  _Wave3Len;
                float  _Wave3Speed;
                float  _Wave3Amp;
                float  _WaveContrast;
                float  _WaveAmp;
                float  _RunupAsym;
                float  _ShoreGain;
                float  _ShoreGainDepth;
                float  _Windward;

                float4 _FoamColor;
                float  _FoamAmount;
                float  _FoamScale;
                float  _FoamSharpness;
                float4 _FoamSpeed;
                float  _FoamShoreDepth;
                float  _FoamShoreBlend;

                float4 _HlColor;
                float  _HlStrength;
                float4 _HlDir;
                float  _HlPower;
                float  _HlThreshold;
                float  _HlSoft;
                float  _HlBump;
                float  _HlEps;
                float  _HlRipple;
                float  _HlRippleScale;
                float  _HlCrestH;
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

            float2 Water_Rot(float2 v, float deg)
            {
                float s, c;
                sincos(radians(deg), s, c);
                return float2(v.x * c - v.y * s, v.x * s + v.y * c);
            }

            /// <summary>
            /// 방향·파장·속도가 다른 사인파 3개의 합. 결과는 대략 [-1, 1].
            /// 🔴 세 파가 **같은 jitter** 를 공유한다. 파마다 다른 지터를 주면 서로 다른 방향으로
            ///    구부러져서 합이 뭉개지고, 마루가 아니라 그냥 얼룩덜룩한 노이즈가 된다.
            /// 각도는 uniform 이라 sincos 는 컴파일러가 호출마다 다시 안 돈다.
            /// </summary>
            float Water_Crests(float2 pw, float2 fdir, float jitter)
            {
                float jt = jitter * _WaveJitter;
                float t  = _Time.y;
                float baseLen = max(_WaveLength, 0.01);

                float a1 = dot(pw, fdir) / baseLen - t * _WaveSpeed + jt;
                float w = sin(a1 * 6.2831);
                float norm = 1.0;

                float2 d2 = Water_Rot(fdir, _Wave2Angle);
                float  a2 = dot(pw, d2) / max(baseLen * _Wave2Len, 0.01)
                          - t * (_WaveSpeed * _Wave2Speed) + jt;
                w += _Wave2Amp * sin(a2 * 6.2831);
                norm += _Wave2Amp;

                float2 d3 = Water_Rot(fdir, _Wave3Angle);
                float  a3 = dot(pw, d3) / max(baseLen * _Wave3Len, 0.01)
                          - t * (_WaveSpeed * _Wave3Speed) + jt;
                w += _Wave3Amp * sin(a3 * 6.2831);
                norm += _Wave3Amp;

                return w / max(norm, 1e-4);
            }

            // 하이라이트용 수면 높이(m). 색을 정하는 h 와 달리 **잔물결 스케일**이다.
            // 🔴 자기완결형으로 둔다 — 세 지점에서 불러 유한차분으로 기울기를 뽑기 때문에,
            //    호출부에서 항을 빼먹으면 노멀이 실제 수면과 어긋나서 하이라이트만 따로 논다.
            //    마루(sin)를 섞는 이유: 잔물결만 쓰면 반짝이가 균일하게 흩뿌려져서
            //    "파도 면을 타고 흐르는 빛의 띠"가 안 생긴다.
            float Water_SurfaceH(float2 pw)
            {
                float2 fdir = normalize(_FlowDir.xy + float2(1e-5, 0));
                float2 q = pw - fdir * (_FlowMaster * _Time.y);

                float r = Water_ValueNoise(q * _HlRippleScale)
                        + 0.5 * Water_ValueNoise(q * (_HlRippleScale * 2.1) + 31.0);

                float jitter = Water_ValueNoise(q * _WaveJitterScale) - 0.5;
                float s = Water_Crests(pw, fdir, jitter);

                return r * _HlRipple + s * _HlCrestH;
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

                // ── 수면 높이 h — 모든 움직임의 단일 출처 ──────────────────────
                // 🔴 이전 판의 진짜 결함(2026-09-15 팀장): 흐르는 얼룩은 노이즈대로 흐르고
                //    물가 띠는 별도 사인파대로 맥박쳐서, **둘 사이에 아무 인과가 없었다.**
                //    그래서 "물이 흐르는 것"과 "벽에 부딪히는 것"이 따로 놀았다 —
                //    물이 아니라 그냥 흐르는 텍스처로 보이는 이유가 정확히 이것이다.
                //    이제 수면 높이 h(m) 하나를 만들고 열린 수면 색·물가 띠·거품을 전부
                //    거기서 파생시킨다. 열린 수면을 가로지르는 마루가 벽에 **도착한 그 순간**
                //    그 벽의 물가가 밀린다.

                // (a) 흐르는 얼룩 — 경계를 넝마처럼 만들고 통째로 밀고 간다.
                //     2겹인 이유: 1겹이면 얼룩이 한 가지 크기라 규칙적으로 보인다.
                float w1 = Water_ValueNoise(pf * _DepthWarpScale);
                float w2 = Water_ValueNoise(pf * (_DepthWarpScale * 2.3) + 17.0);
                float hNoise = ((w1 * 0.65 + w2 * 0.35) - 0.5) * 2.0 * _DepthWarpAmount;

                // (b) 물가가 어느 쪽인가 — 수심이 얕아지는 방향이다.
                //     화면 미분에서 월드 XZ 기울기를 2x2 역행렬로 풀어낸다(수면이 거의 수평이라
                //     행렬식이 안정적이다). 🔴 이게 있어야 "흐름이 부딪히는 벽"과 "흐름이
                //     떠나는 벽"을 구분할 수 있다. 없으면 사방이 똑같이 출렁여서 파도가 아니라
                //     수면 전체의 숨쉬기로 보인다.
                float2 dpx = ddx(p), dpy = ddy(p);
                float  ddxD = ddx(waterDepth), ddyD = ddy(waterDepth);
                float  det  = dpx.x * dpy.y - dpx.y * dpy.x;
                float2 grad = float2(dpy.y * ddxD - dpx.y * ddyD,
                                     dpx.x * ddyD - dpy.x * ddxD)
                            / (abs(det) > 1e-8 ? det : 1e-8);
                float  gradLen = length(grad);
                float2 upHill  = gradLen > 1e-4 ? -grad / gradLen : float2(0, 0);
                // 흐름이 얕은 쪽을 향할수록 1 = 정면으로 부딪히는 벽. 기울기를 못 구하면 중립.
                float  windward = gradLen > 1e-4 ? saturate(dot(flowDir, upHill)) : 0.5;
                float  facing   = lerp(1.0, windward, _Windward);

                // (c) 진행하는 마루. 위상 = (위치·흐름방향) − 시간 → 흐름 방향으로 이동한다.
                //     방향·파장이 다른 3개를 겹쳐서 마루가 길어졌다 끊겼다 하게 만든다.
                float jitter = Water_ValueNoise(pf * _WaveJitterScale) - 0.5;
                float s = Water_Crests(p, flowDir, jitter);
                // 🔴 사인 그대로면 오르내림이 대칭이라 "철썩"이 아니라 "숨쉬기"로 읽힌다.
                //    밀려올 땐 빠르게 꽉 차고 빠질 땐 천천히 — 그 비대칭이 파도의 정체다.
                float crest = s >= 0 ? pow(s, 1.0 / _RunupAsym) : -pow(-s, _RunupAsym);

                // (d) 천해 증폭 — 파도는 얕아질수록 커진다. 열린 수면에선 잔물결이지만
                //     벽 앞에서는 눈에 띄게 밀려 올라간다. 이 대비가 "부딪힌다"를 만든다.
                float shoal = 1.0 + _ShoreGain * (1.0 - saturate(waterDepth / max(_ShoreGainDepth, 1e-3)));
                float hWave = crest * _WaveAmp * shoal * facing;

                // 🔴 부호: h 가 클수록 **얕게** 친다. 마루가 도착하면 물이 벽에서 바깥으로
                //    밀려나와 밝은 물가가 넓어지고, 빠지면 벽에 붙어 좁아진다.
                //    반대 부호로 두면 마루에서 물가가 벽 속으로 숨어 거품만 터지는 모순이 된다.
                float h = hNoise + hWave;
                float shadedDepth = max(0.0, waterDepth - h);

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

                // ── 물가 응답 ──────────────────────────────────────────────────
                // 🔴 물가는 **자기 파형을 갖지 않는다.** 위에서 만든 수면 높이를 그대로 받는다 —
                //    그래야 열린 수면을 지나가는 그 마루가 벽에 닿는 순간 이 자리가 밀린다.
                //    예전처럼 여기서 사인파를 새로 만들면 아무리 맞춰도 둘은 영원히 남남이다.
                float shoreDepth = shadedDepth;

                // 열린 수면 색 대비는 선택 사항 — 수심이 이미 마루를 따라 움직인다(기본 0).
                col *= 1.0 + crest * _WaveContrast;

                // ── 거품 ───────────────────────────────────────────────────────
                // 경계가 또렷한 얼룩이 레퍼런스의 인상을 만든다 — 노이즈를 세게 계단화한다.
                // 물가에서는 _FoamShoreBlend 만큼 더 몰린다(파도를 따라 같이 움직인다).
                float foamN = Water_ValueNoise(pf * _FoamScale + _FoamSpeed.xy * _Time.y * _FoamScale);
                float shoreT = 1.0 - saturate(shoreDepth / max(_FoamShoreDepth, 1e-3));
                // 🔴 거품은 마루가 **부딪히는 순간** 터진다. 상시 같은 양이면 벽에 이끼가
                //    낀 것처럼 보이고, 그게 "물이 고여 있다"는 인상의 절반을 만든다.
                float surge = saturate(crest) * facing;
                float foamWant = saturate(_FoamAmount + shoreT * _FoamShoreBlend * (0.3 + 0.7 * surge));
                // foamWant 가 클수록 문턱이 낮아져 얼룩이 넓어진다.
                float foam = saturate((foamN - (1.0 - foamWant)) * _FoamSharpness);
                col = lerp(col, _FoamColor.rgb, foam * _FoamColor.a);

                // ── 수면 하이라이트 ────────────────────────────────────────────
                // 세 지점을 찍어 유한차분으로 수면 기울기 → 가짜 노멀. 정점을 움직이지 않고
                // 프래그먼트에서만 만든다(Opaque 1패스·드로우콜 유지).
                float hlEps = max(_HlEps, 1e-3);
                float h0 = Water_SurfaceH(p);
                float hx = Water_SurfaceH(p + float2(hlEps, 0));
                float hz = Water_SurfaceH(p + float2(0, hlEps));
                float2 slope = (float2(hx, hz) - h0) / hlEps * _HlBump;
                float3 N = normalize(float3(-slope.x, 1.0, -slope.y));

                float3 V  = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float3 L  = normalize(_HlDir.xyz + float3(0, 1e-5, 0));
                float3 Hv = normalize(L + V);
                float  spec = pow(saturate(dot(N, Hv)), max(_HlPower, 1.0));
                // 🔴 계단화가 툰의 정체다. smoothstep 없이 그대로 두면 뿌옇게 번져서
                //    물이 아니라 렌즈 더러운 것처럼 보인다.
                spec = smoothstep(_HlThreshold, _HlThreshold + max(_HlSoft, 1e-3), spec);

                // 🔴 먼 수면에서는 한 픽셀이 잔물결 한 칸보다 커진다. 그대로 두면 표본이
                //    프레임마다 튀어 화면 안쪽이 지직거린다(얇은 스펙큘러의 고질적 증상).
                //    픽셀이 덮는 월드 거리를 재서, 잔물결보다 커지기 시작하면 하이라이트를 끈다.
                //    dpx/dpy 는 위에서 물가 방향 구할 때 이미 뽑아 둔 값이다.
                float pxWorld = max(length(dpx), length(dpy));
                float aa = saturate(1.0 - pxWorld * _HlRippleScale * 2.0);

                // 거품 위에는 얹지 않는다 — 이미 흰색이라 겹치면 뭉개진다.
                col += _HlColor.rgb * (spec * _HlStrength * aa * (1.0 - foam));

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
