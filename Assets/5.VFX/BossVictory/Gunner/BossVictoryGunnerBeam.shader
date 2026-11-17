// ----------------------------------------------------------------------------
//  BossVictoryGunnerBeam.shader — 보스 처치 연출 거너 레이저 뼈대 셰이더 (할 일 302)
//
//  언릿(premultiplied 블렌드 — _Opacity 0 = 가산, 1 = 밝은 곳이 배경을 가림), 외부 이미지 없이 절차 생성. 세 모드:
//   · 캡슐(기본) — 메시 = Mesh_BossVictoryGunnerBeam_Capsule(옆면 +Z 0~1m, 반지름 0.5, UV.x = 길이 0~1, UV.y = 둘레 0~1,
//     UV2.x = 반구 늘림 비율 — 셰이더가 실제 반지름만큼 밀어 길이를 늘여도 끝이 둥글다).
//     밝기를 텍스처 V 가 아니라 **시선과 표면의 각도(N·V)** 로 정한다 → 어느 방향에서 봐도 화면상 가운데가 가장 밝다
//     (300 원통에 판용 Trail 텍스처를 감았을 때 가운데가 비던 문제를 없앰). 앞면만 그린다(Cull Back — 앞뒤 겹침 없음).
//     결 노이즈(_Streak*) = 길이 방향으로 길게 늘인 노이즈로 가장자리를 깎아 연기 같은 결을 만든다(가운데는 _StreakKeep 만큼 남김).
//     만화 선(_Line*) = 둘레 칸마다 길이 방향 붓 획(끝 뾰족·가장자리 흔들림·경계 날카로움)을 흘린다. 밝은 색 = 빛줄기, 어두운 색 + 가림도 = 먹선.
//   · 빌보드(_BILLBOARD) — 메시 = Mesh_BossVictoryGunnerBeam_Quad, 오브젝트 원점에서 카메라를 향하는 판. 총구·끝 플레어.
//   · 번개(_LIGHTNING) — 메시 = Mesh_BossVictoryGunnerBeam_Bolts(줄기 4개 × 축 따라 점 65개 띠, UV2.x = 줄기 번호). 정점마다 축 둘레 3D 꺾은선 위로
//     옮기고 그 선의 진행 방향·시선에 수직으로 번짐 폭만큼만 편다(평면 없음). _BoltRate 번/초 마다 모양이 새로 뽑힌다(멈춤 화면이면 멈춤). 오프셋 단위 = 오브젝트 X 스케일의 반.
//
//  코드(BossVictoryBeamRig)가 매 프레임 넣는 칸: _Color(밝기·페이드), _BeamLength(m), _BeamTime(초 — 멈춤 화면이면 멈춤).
//  흐름 = 노이즈가 총구 → 끝으로 _FlowSpeed(m/초). 색은 HDR — 1 을 넘으면 블룸.
// ----------------------------------------------------------------------------
Shader "BeaverLobby/VFX/BossVictoryGunnerBeam"
{
    Properties
    {
        [HDR] _Color ("전체 색·밝기 (코드가 페이드에 씀)", Color) = (1, 1, 1, 1)
        [HDR] _CoreColor ("중심 색 (HDR — 흰 심 · 번개 = 줄기 심)", Color) = (8, 8, 8, 1)
        [HDR] _GlowColor ("둘레 색 (HDR · 번개 = 줄기 번짐)", Color) = (1, 2.5, 6, 1)
        _CorePower ("중심 날카로움 (클수록 흰 심이 가늘다)", Range(0.5, 32)) = 8
        _GlowPower ("둘레 퍼짐 (작을수록 가장자리까지 밝다)", Range(0.1, 8)) = 1.5
        _NoiseScale ("노이즈 촘촘함 (1m 당)", Range(0, 10)) = 1.2
        _NoiseAround ("노이즈 둘레 촘촘함", Range(0, 16)) = 3
        _NoiseAmount ("노이즈 세기 (0 = 없음)", Range(0, 1)) = 0.45
        _FlowSpeed ("흐름 속도 (m/초, + = 총구 → 끝)", Range(-60, 60)) = 12
        _PulseAmount ("깜빡임 세기", Range(0, 1)) = 0.12
        _PulseFreq ("깜빡임 빠르기 (회/초)", Range(0, 40)) = 14
        _StartFade ("시작 부드러움 (m, 0 = 끔)", Range(0, 5)) = 0
        _EndFade ("끝 부드러움 (m, 0 = 끔)", Range(0, 10)) = 0
        _Opacity ("불투명도 (0 = 빛만 더함 · 1 = 밝은 곳이 배경을 가림 — 몸통이 투명해 보이면 올림)", Range(0, 1)) = 0
        [Toggle(_BILLBOARD)] _Billboard ("빌보드 판 (총구·끝 플레어)", Float) = 0
        [Toggle(_LIGHTNING)] _Lightning ("3D 번개 (빌보드와 같이 켜지 말 것)", Float) = 0

        [Header(Streak)]
        _StreakAmount ("결 세기 (캡슐 — 가장자리를 길게 깎아 연기 결 · 0 = 끔)", Range(0, 1)) = 0
        _StreakScale ("결 길이 방향 촘촘함 (1m 당 — 작을수록 길게 늘어진 결)", Range(0.02, 4)) = 0.3
        _StreakAround ("결 둘레 촘촘함 (클수록 가는 결이 많다)", Range(0, 16)) = 2.5
        _StreakCut ("깎는 기준 (클수록 많이 깎임)", Range(0, 1.5)) = 0.5
        _StreakSoft ("깎인 가장자리 부드러움", Range(0.01, 1)) = 0.15
        _StreakKeep ("가운데 남김 (클수록 화면 가운데는 안 깎임)", Range(0, 2)) = 0.6
        _StreakFlow ("결 흐름 배율 (흐름 속도 × 이 값)", Range(0, 3)) = 0.6

        [Header(Comic Lines)]
        _LineAmount ("만화 선 세기 (캡슐 — 길이 방향 거친 붓선 · 0 = 끔)", Range(0, 1)) = 0
        [HDR] _LineColor ("만화 선 색 (밝게 = 빛줄기 · 어둡게 = 먹선, 어두운 선은 선 가림도 올림)", Color) = (3, 3, 3, 1)
        _LineCover ("선 가림도 (어두운 선이 배경·안쪽을 가리게)", Range(0, 1)) = 0
        _LineCount ("선 둘레 칸 수 (둘레 한 바퀴에 들어가는 선 줄 수)", Range(1, 48)) = 12
        _LineWidth ("선 굵기 (칸 폭 비율)", Range(0.01, 1)) = 0.12
        _LineLength ("선 한 획 길이 (m)", Range(0.1, 20)) = 2.5
        _LineFill ("획이 그려질 확률 (클수록 빽빽)", Range(0, 1)) = 0.5
        _LineTaper ("획 끝 뾰족함 (클수록 붓처럼 끝이 가늘다)", Range(0, 4)) = 0.7
        _LineRough ("선 가장자리 거칠기 (흔들림)", Range(0, 2)) = 0.6
        _LineFlow ("선 흐름 배율 (흐름 속도 × 이 값)", Range(0, 3)) = 1.3

        [Header(Lightning)]
        _BoltCount ("번개 줄기 수 (0~4)", Range(0, 4)) = 3
        _BoltRate ("번개 모양 바뀌는 빠르기 (번/초)", Range(0, 60)) = 12
        _BoltChance ("줄기마다 보일 확률 (매 번)", Range(0, 1)) = 0.75
        _BoltSegment ("꺾임 간격 (m)", Range(0.1, 10)) = 1.4
        _BoltJag ("꺾임 크기 (오브젝트 X 스케일 반 비율)", Range(0, 1)) = 0.35
        _BoltDetail ("잔 꺾임 세기 (오브젝트 X 스케일 반 비율)", Range(0, 0.5)) = 0.12
        _BoltInner ("줄기 중심 최소 거리 (축에서 · 오브젝트 X 스케일 반 비율)", Range(0, 1)) = 0.3
        _BoltSpread ("줄기 중심 최대 거리 (축에서 · 오브젝트 X 스케일 반 비율)", Range(0, 1)) = 0.7
        _BoltWidth ("줄기 굵기 (m)", Range(0.002, 0.3)) = 0.035
        _BoltGlowWidth ("줄기 번짐 폭 (굵기 배수)", Range(1, 20)) = 5
        _BoltCoverage ("줄기 길이 (레이저 길이 비율)", Range(0.05, 1)) = 0.75

        [HideInInspector] _BeamLength ("레이저 길이 m (코드)", Float) = 10
        [HideInInspector] _BeamTime ("시계 초 (코드)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha   // premultiplied — 알파 0 = 가산, 알파 1 = 배경을 가림(_Opacity)
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _ _BILLBOARD _LIGHTNING

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _CoreColor;
                half4 _GlowColor;
                half _CorePower;
                half _GlowPower;
                half _NoiseScale;
                half _NoiseAround;
                half _NoiseAmount;
                half _FlowSpeed;
                half _PulseAmount;
                half _PulseFreq;
                half _StartFade;
                half _EndFade;
                half _Opacity;
                half _StreakAmount;
                half _StreakScale;
                half _StreakAround;
                half _StreakCut;
                half _StreakSoft;
                half _StreakKeep;
                half _StreakFlow;
                half4 _LineColor;
                half _LineAmount;
                half _LineCover;
                half _LineCount;
                half _LineWidth;
                half _LineLength;
                half _LineFill;
                half _LineTaper;
                half _LineRough;
                half _LineFlow;
                half _BoltCount;
                half _BoltRate;
                half _BoltChance;
                half _BoltSegment;
                half _BoltJag;
                half _BoltDetail;
                half _BoltInner;
                half _BoltSpread;
                half _BoltWidth;
                half _BoltGlowWidth;
                half _BoltCoverage;
                float _BeamLength;
                float _BeamTime;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;   // x = 캡(반구) 늘림 비율 −1~1(− = 총구 쪽, + = 끝 쪽, 0 = 옆면)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewWS : TEXCOORD2;
                float capM : TEXCOORD3;   // 캡슐 = 캡이 끝점 밖으로 나간 거리(m, 부호) · 번개 = 띠 반폭(m)
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // 꺾은선 — 간격마다 −1~1 무작위 값을 직선으로 잇는다.
            float Polyline(float s, float key)
            {
                float i = floor(s);
                float y0 = Hash21(float2(i, key)) * 2.0 - 1.0;
                float y1 = Hash21(float2(i + 1.0, key)) * 2.0 - 1.0;
                return lerp(y0, y1, frac(s));
            }

            // 번개 줄기 b 의 축 기준 3D 옆 오프셋(띠 반폭 단위, x·y) — 축 둘레 무작위 방향으로 _BoltInner~_BoltSpread 만큼 떨어진 뒤
            // 큰 꺾임·잔 꺾임 꺾은선을 x·y 두 방향으로 따로 더한다(평면이 아니라 공간에서 휘는 선).
            float2 BoltOffset(float meters, float2 key)
            {
                float lineKey = Hash21(key + 1.7) * 97.0;
                float side = Hash21(key + 9.7);
                float ang = side * 6.2831853;
                float radius = lerp(_BoltInner, _BoltSpread, Hash21(key + 4.3));
                float fine = _BoltSegment * 0.3;
                float2 o = float2(cos(ang), sin(ang)) * radius;
                o += float2(Polyline(meters / _BoltSegment, lineKey), Polyline(meters / _BoltSegment, lineKey + 13.0)) * _BoltJag;
                o += float2(Polyline(meters / fine, lineKey + 41.0), Polyline(meters / fine, lineKey + 59.0)) * _BoltDetail;
                return o;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 c0 = float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x);
                float3 c1 = float3(UNITY_MATRIX_M[0].y, UNITY_MATRIX_M[1].y, UNITY_MATRIX_M[2].y);
                float3 c2 = float3(UNITY_MATRIX_M[0].z, UNITY_MATRIX_M[1].z, UNITY_MATRIX_M[2].z);
                float sx = max(length(c0), 1e-5), sy = max(length(c1), 1e-5), sz = max(length(c2), 1e-5);
            #if defined(_BILLBOARD)
                // 오브젝트 원점에서 카메라를 향하는 판 — 크기 = 오브젝트 X 스케일.
                float3 center = TransformObjectToWorld(float3(0, 0, 0));
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float3 positionWS = center + (right * input.positionOS.x + up * input.positionOS.y) * sx;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = float3(0, 0, 1);
                output.viewWS = float3(0, 0, 1);
                output.capM = 0;
            #elif defined(_LIGHTNING)
                // 3D 번개 — 메시 = 줄기마다 축 따라 점 N 개인 띠(UV.x = 길이 0~1, UV.y = 띠 좌우 0·1, UV2.x = 줄기 번호).
                // 점을 축 둘레 공간의 꺾은선 위로 옮기고, 그 선의 진행 방향·시선에 수직으로 가늘게(번짐 폭만큼) 편다 → 넓은 평면 없음.
                float t = input.uv.x;
                float meters = t * _BeamLength;
                float b = input.uv1.x;
                float seed = floor(_BeamTime * _BoltRate);
                float2 key = float2(b * 7.13 + 0.5, seed * 1.37 + 0.25);
                float on = (b < round(_BoltCount) && Hash21(key + 3.1) <= _BoltChance) ? 1.0 : 0.0;
                // 줄기 길이 창 — 레이저 안 무작위 구간, 양 끝 0.6m 에 걸쳐 가늘어지며 사라짐.
                float span = _BoltCoverage * _BeamLength;
                float start = Hash21(key + 5.5) * max(0.0, _BeamLength - span);
                float window = smoothstep(start, start + 0.6, meters) * (1.0 - smoothstep(start + span - 0.6, start + span, meters));
                float flicker = 0.55 + 0.45 * Hash21(float2(b * 3.7 + 0.3, floor(_BeamTime * _BoltRate * 3.0)));
                float w = on * window;

                float halfW = 0.5 * sx;   // 오프셋 단위(m) = 오브젝트 X 스케일의 반
                float3 ex = c0 / sx, ey = c1 / sy;
                float dm = _BeamLength / 64.0;
                float2 o0 = BoltOffset(meters, key);
                float2 o1 = BoltOffset(meters + dm, key);
                float2 om = BoltOffset(meters - dm, key);
                float3 p = TransformObjectToWorld(float3(0, 0, t)) + (ex * o0.x + ey * o0.y) * halfW;
                float3 axisWS = c2 / sz;
                float3 tangent = axisWS * (2.0 * dm) + (ex * (o1.x - om.x) + ey * (o1.y - om.y)) * halfW;
                float3 toCam = GetCameraPositionWS() - p;
                float3 side = cross(tangent, toCam);
                side = dot(side, side) > 1e-10 ? normalize(side) : ex;
                float stripHalf = _BoltWidth * _BoltGlowWidth * 1.6;
                float3 positionWS = p + side * ((input.uv.y * 2.0 - 1.0) * stripHalf * (w > 0.0 ? 1.0 : 0.0));
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = float3(0, 0, 1);
                output.viewWS = float3(0, 0, 1);
                output.capM = stripHalf;
                input.color.a *= w * flicker;
            #else
                // 캡슐 — 반구 정점은 오브젝트 z 0·1 에 붙어 있고, 셰이더가 실제 반지름만큼 밖으로 민다.
                // 몸통 Z 스케일(= 길이)과 무관하게 반구가 둥글게 남는다(메시만 늘리면 길쭉한 타원이 된다).
                float capWorld = input.uv1.x * 0.5 * sx;
                float3 pos = input.positionOS.xyz;
                pos.z += capWorld / sz;
                float3 positionWS = TransformObjectToWorld(pos);
                output.positionCS = TransformWorldToHClip(positionWS);
                // 법선 = 축 방향만 돌림(늘림 없이) — 반구 법선이 길이 스케일에 찌그러지지 않게.
                output.normalWS = normalize(c0 / sx * input.normalOS.x + c1 / sy * input.normalOS.y + c2 / sz * input.normalOS.z);
                output.viewWS = GetWorldSpaceViewDir(positionWS);
                output.capM = capWorld;
            #endif
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half extraCover = 0;   // 만화 선 가림도(캡슐만)
                float pulse = 1.0 - _PulseAmount * (0.5 + 0.5 * sin(_BeamTime * _PulseFreq * 6.2831853));
            #if defined(_BILLBOARD)
                float r = saturate(length(input.uv - 0.5) * 2.0);
                float k = 1.0 - r;
                half3 col = _CoreColor.rgb * pow(k, _CorePower) + _GlowColor.rgb * pow(k, _GlowPower);
                col *= pulse;
            #elif defined(_LIGHTNING)
                // 줄기 하나의 가는 띠 — 가로 거리(m)로 심·가우스 번짐. 창·깜빡임은 정점 색 알파(fade)로 들어온다.
                float meters = input.uv.x * _BeamLength;
                float v = input.uv.y * 2.0 - 1.0;   // 띠 가로 −1~1
                float dist = abs(v) * max(input.capM, 1e-5);
                float core = saturate(1.0 - dist / _BoltWidth);
                core *= core;
                float g = dist / (_BoltWidth * _BoltGlowWidth);
                float glow = exp(-g * g * 2.0);
                float edge = 1.0 - smoothstep(0.8, 1.0, abs(v));
                float ends = (_StartFade > 1e-4 ? smoothstep(0.0, _StartFade, meters) : 1.0)
                           * (_EndFade > 1e-4 ? smoothstep(0.0, _EndFade, _BeamLength - meters) : 1.0);
                half3 col = (_CoreColor.rgb * core + _GlowColor.rgb * glow) * (edge * ends);
            #else
                float ndv = saturate(dot(normalize(input.normalWS), normalize(input.viewWS)));   // 화면상 가운데 1 · 가장자리 0
                float meters = input.uv.x * _BeamLength + input.capM;   // 캡까지 이어진 길이 좌표 — 노이즈·흐름이 둥근 끝까지 이어진다
                // 흐름 — 패턴이 시간에 따라 +길이(끝) 쪽으로 이동. 둘레 방향은 각도의 cos·sin 으로(이음매 없음).
                float along = (meters - _BeamTime * _FlowSpeed) * _NoiseScale;
                float ang = input.uv.y * 6.2831853;
                float n = ValueNoise(float2(along, cos(ang) * _NoiseAround)) * 0.6
                        + ValueNoise(float2(along * 2.1 + 17.0, sin(ang) * _NoiseAround)) * 0.4;
                float noise = lerp(1.0, n * 1.6, _NoiseAmount);
                // 결 — 길이 방향으로 늘인 노이즈로 가장자리를 깎는다. 화면 가운데(ndv 큼)는 _StreakKeep 만큼 남긴다.
                float alongS = (meters - _BeamTime * _FlowSpeed * _StreakFlow) * _StreakScale;
                float st = ValueNoise(float2(alongS, cos(ang) * _StreakAround + 5.0)) * 0.6
                         + ValueNoise(float2(alongS * 2.3 + 31.0, sin(ang) * _StreakAround * 1.7 + 11.0)) * 0.4;
                float mask = smoothstep(_StreakCut - _StreakSoft, _StreakCut + _StreakSoft, st + ndv * _StreakKeep);
                float erode = lerp(1.0, mask, _StreakAmount);
                // 끝 부드러움(m) — 0 = 끄기(캡슐은 끝이 둥글어 보통 0).
                float ends = (_StartFade > 1e-4 ? smoothstep(0.0, _StartFade, meters) : 1.0)
                           * (_EndFade > 1e-4 ? smoothstep(0.0, _EndFade, _BeamLength - meters) : 1.0);
                half3 col = _CoreColor.rgb * pow(ndv, _CorePower) + _GlowColor.rgb * pow(ndv, _GlowPower) * noise;
                col *= ends * pulse * erode;
                // 만화 선 — 둘레를 _LineCount 칸으로 나눠 칸마다 길이 방향 붓 획(끝이 뾰족, 가장자리가 흔들리는 딱 끊긴 선)을 흘린다.
                // 결 노이즈(부드러운 연기)와 달리 경계가 날카롭다(화면 1픽셀 안티에일리어싱만).
                if (_LineAmount > 1e-4)
                {
                    float count = max(1.0, round(_LineCount));
                    float a = input.uv.y * count;
                    float cell = floor(a);
                    float fa = frac(a);
                    float flowM = meters - _BeamTime * _FlowSpeed * _LineFlow;
                    float sL = flowM / _LineLength + Hash21(float2(cell, 3.7)) * 7.0;
                    float dash = floor(sL);
                    float fd = frac(sL);
                    float h = Hash21(float2(cell + 0.31, dash));
                    float on = h < _LineFill ? 1.0 : 0.0;
                    float center = 0.3 + 0.4 * Hash21(float2(cell + 0.5, dash + 0.7));
                    float w = _LineWidth * lerp(0.5, 1.5, Hash21(float2(dash + 0.13, cell + 9.0)));
                    float taper = pow(saturate(sin(fd * 3.14159265)), _LineTaper);
                    float wob = (ValueNoise(float2(flowM * 5.0, cell * 3.1)) - 0.5) * _LineRough * w;
                    float halfw = 0.5 * w * taper;
                    float d = abs(fa - center - wob);
                    float aa = max(fwidth(fa), 1e-4);
                    float capFade = 1.0 - smoothstep(0.0, 0.35, abs(input.capM));   // 둥근 끝(반구)에선 선이 극으로 모여 공 무늬가 되므로 흐린다
                    float lineMask = on * (1.0 - smoothstep(halfw - aa, halfw + aa, d)) * _LineAmount * capFade;
                    col = lerp(col, _LineColor.rgb * (ends * pulse), lineMask);
                    extraCover = lineMask * _LineCover;
                }
            #endif
                // 가림 정도 = 밝기(가장 밝은 채널, 0~1) × 불투명도 — 흰 심은 배경을 가리고 둘레 빛은 투명하게 번진다.
                half fade = _Color.a * input.color.a;
                half cover = max(saturate(max(col.r, max(col.g, col.b))) * _Opacity, extraCover) * fade;
                col *= _Color.rgb * fade * input.color.rgb;
                return half4(col, cover);
            }
            ENDHLSL
        }
    }
}
