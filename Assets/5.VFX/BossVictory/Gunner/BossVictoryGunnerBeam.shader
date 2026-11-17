// ----------------------------------------------------------------------------
//  BossVictoryGunnerBeam.shader — 보스 처치 연출 거너 레이저 뼈대 셰이더 (할 일 302)
//
//  언릿(premultiplied 블렌드 — _Opacity 0 = 가산, 1 = 밝은 곳이 배경을 가림), 외부 이미지 없이 절차 생성. 두 모드:
//   · 캡슐(기본) — 메시 = Mesh_BossVictoryGunnerBeam_Capsule(옆면 +Z 0~1m, 반지름 0.5, UV.x = 길이 0~1, UV.y = 둘레 0~1,
//     UV2.x = 반구 늘림 비율 — 셰이더가 실제 반지름만큼 밀어 길이를 늘여도 끝이 둥글다).
//     밝기를 텍스처 V 가 아니라 **시선과 표면의 각도(N·V)** 로 정한다 → 어느 방향에서 봐도 화면상 가운데가 가장 밝다
//     (300 원통에 판용 Trail 텍스처를 감았을 때 가운데가 비던 문제를 없앰). 앞면만 그린다(Cull Back — 앞뒤 겹침 없음).
//   · 빌보드(_BILLBOARD) — 메시 = Mesh_BossVictoryGunnerBeam_Quad, 오브젝트 원점에서 카메라를 향하는 판. 총구·끝 플레어.
//
//  코드(BossVictoryBeamRig)가 매 프레임 넣는 칸: _Color(밝기·페이드), _BeamLength(m), _BeamTime(초 — 멈춤 화면이면 멈춤).
//  흐름 = 노이즈가 총구 → 끝으로 _FlowSpeed(m/초). 색은 HDR — 1 을 넘으면 블룸.
// ----------------------------------------------------------------------------
Shader "BeaverLobby/VFX/BossVictoryGunnerBeam"
{
    Properties
    {
        [HDR] _Color ("전체 색·밝기 (코드가 페이드에 씀)", Color) = (1, 1, 1, 1)
        [HDR] _CoreColor ("중심 색 (HDR — 흰 심)", Color) = (8, 8, 8, 1)
        [HDR] _GlowColor ("둘레 색 (HDR)", Color) = (1, 2.5, 6, 1)
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
            #pragma shader_feature_local _BILLBOARD

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
                float capM : TEXCOORD3;   // 캡이 끝점 밖으로 나간 거리(m, 부호)
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
            #if defined(_BILLBOARD)
                // 오브젝트 원점에서 카메라를 향하는 판 — 크기 = 오브젝트 X 스케일.
                float3 center = TransformObjectToWorld(float3(0, 0, 0));
                float size = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float3 positionWS = center + (right * input.positionOS.x + up * input.positionOS.y) * size;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = float3(0, 0, 1);
                output.viewWS = float3(0, 0, 1);
                output.capM = 0;
            #else
                // 캡슐 — 반구 정점은 오브젝트 z 0·1 에 붙어 있고, 셰이더가 실제 반지름만큼 밖으로 민다.
                // 몸통 Z 스케일(= 길이)과 무관하게 반구가 둥글게 남는다(메시만 늘리면 길쭉한 타원이 된다).
                float3 c0 = float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x);
                float3 c1 = float3(UNITY_MATRIX_M[0].y, UNITY_MATRIX_M[1].y, UNITY_MATRIX_M[2].y);
                float3 c2 = float3(UNITY_MATRIX_M[0].z, UNITY_MATRIX_M[1].z, UNITY_MATRIX_M[2].z);
                float sx = max(length(c0), 1e-5), sy = max(length(c1), 1e-5), sz = max(length(c2), 1e-5);
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

            half4 Frag(Varyings input) : SV_Target
            {
                float pulse = 1.0 - _PulseAmount * (0.5 + 0.5 * sin(_BeamTime * _PulseFreq * 6.2831853));
            #if defined(_BILLBOARD)
                float r = saturate(length(input.uv - 0.5) * 2.0);
                float k = 1.0 - r;
                half3 col = _CoreColor.rgb * pow(k, _CorePower) + _GlowColor.rgb * pow(k, _GlowPower);
                col *= pulse;
            #else
                float ndv = saturate(dot(normalize(input.normalWS), normalize(input.viewWS)));   // 화면상 가운데 1 · 가장자리 0
                float meters = input.uv.x * _BeamLength + input.capM;   // 캡까지 이어진 길이 좌표 — 노이즈·흐름이 둥근 끝까지 이어진다
                // 흐름 — 패턴이 시간에 따라 +길이(끝) 쪽으로 이동. 둘레 방향은 각도의 cos·sin 으로(이음매 없음).
                float along = (meters - _BeamTime * _FlowSpeed) * _NoiseScale;
                float ang = input.uv.y * 6.2831853;
                float n = ValueNoise(float2(along, cos(ang) * _NoiseAround)) * 0.6
                        + ValueNoise(float2(along * 2.1 + 17.0, sin(ang) * _NoiseAround)) * 0.4;
                float noise = lerp(1.0, n * 1.6, _NoiseAmount);
                // 끝 부드러움(m) — 0 = 끄기(캡슐은 끝이 둥글어 보통 0).
                float ends = (_StartFade > 1e-4 ? smoothstep(0.0, _StartFade, meters) : 1.0)
                           * (_EndFade > 1e-4 ? smoothstep(0.0, _EndFade, _BeamLength - meters) : 1.0);
                half3 col = _CoreColor.rgb * pow(ndv, _CorePower) + _GlowColor.rgb * pow(ndv, _GlowPower) * noise;
                col *= ends * pulse;
            #endif
                // 가림 정도 = 밝기(가장 밝은 채널, 0~1) × 불투명도 — 흰 심은 배경을 가리고 둘레 빛은 투명하게 번진다.
                half fade = _Color.a * input.color.a;
                half cover = saturate(max(col.r, max(col.g, col.b))) * _Opacity * fade;
                col *= _Color.rgb * fade * input.color.rgb;
                return half4(col, cover);
            }
            ENDHLSL
        }
    }
}
