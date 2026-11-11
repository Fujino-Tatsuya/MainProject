// 로비 바닥 대시 벨트(할 일 257) — 한 메시에 두 가지 꼭짓점이 들어 있다(TEXCOORD1.x = 0 띠 / 1 아이콘).
//  · 띠 바탕: 경로를 따라 깐 띠(U = 누적 경로 길이 비율, V = 폭). 바탕색 + 양 끝 페이드만.
//  · 아이콘(257 보완): 아이콘마다 따로인 정사각 쿼드. 정점 셰이더가 경로 표본 배열(_PathPoints·_PathTangents)에서
//    호 길이 s 자리의 위치·접선을 찾아 쿼드를 그 방향으로 돌려 세운다 — 띠 UV 로 찍지 않으므로 원호에서 찌그러지지 않고 회전만 한다.
//    s 는 _Time 으로 민다(스크립트 매 프레임 갱신·메시 재생성 없음). 쿼드 수는 고정이고 흐르면 빙 돌아 다시 쓴다.
//  인덱스 순서 = 띠 → 아이콘이라 한 번의 그리기에서 아이콘이 바탕 위에 덮인다.
// 배치 규칙의 C# 판 = LobbyFloorDashBeltLayout.IconCenterCells·SamplePath·IconCorner(EditMode 테스트). 바꿀 때 둘을 같이.
// 수치는 LobbyFloorDashBelt 가 MaterialPropertyBlock 으로 넣는다. 텍스처 = 50.Art/UI/HUD/Common_Skillicon/Common_DashIcon.png 참조만(임포트 안 건드림).
// 아트 방향: 원본 » 는 진행 방향을 가리킨다. 그림이 바뀌면 _IconRotation(도, 반시계)만 고친다.
Shader "Lobby/FloorDashBelt"
{
    Properties
    {
        _BaseMap ("Icon", 2D) = "white" {}
        [HDR] _BaseColor ("Icon color", Color) = (0.3, 0.85, 1, 0.9)
        _BeltColor ("Belt fill color (a = 0 to hide)", Color) = (0.3, 0.85, 1, 0.12)
        _PathLength ("Path length (m)", Float) = 6
        _Spacing ("Icon spacing (m)", Float) = 1
        _IconSize ("Icon size (m)", Float) = 1
        _Width ("Belt width (m)", Float) = 1
        _IconRotation ("Icon rotation (deg, CCW)", Float) = 0
        _ScrollSpeed ("Scroll (cells/s, + = forward)", Float) = 0.15
        _PulseSpeed ("Pulse (cycles/s)", Float) = 0.6
        _PulsePhase ("Pulse phase step per icon (cycles)", Float) = 0.12
        _PulseBrightness ("Pulse brightness depth (0~1)", Range(0, 1)) = 0.5
        _PulseScale ("Pulse size depth (0~0.5)", Range(0, 0.5)) = 0.12
        _EndFade ("End fade (0~0.5 of length)", Range(0, 0.5)) = 0.1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // LobbyFloorDashBeltLayout.MaxPathSamples 와 같아야 한다.
            #define MAX_PATH_SAMPLES 128

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _BeltColor;
                float _PathLength;
                float _Spacing;
                float _IconSize;
                float _Width;
                float _IconRotation;
                float _ScrollSpeed;
                float _PulseSpeed;
                float _PulsePhase;
                float _PulseBrightness;
                float _PulseScale;
                float _EndFade;
            CBUFFER_END

            // 경로 표본(루트 로컬 = 오브젝트 공간). xyz = 위치, w = 누적 거리 / xyz = 진행 방향. 개수 = _PathCount.
            float4 _PathPoints[MAX_PATH_SAMPLES];
            float4 _PathTangents[MAX_PATH_SAMPLES];
            float _PathCount;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;  // 띠 = (U, V) / 아이콘 = 꼭짓점 부호(±1, ±1)
                float2 kind : TEXCOORD1; // x = 0 띠 / 1 아이콘, y = 아이콘 쿼드 번호
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 data : TEXCOORD0; // 띠 = (U, V, -, 0) / 아이콘 = (진행 방향 m, 왼쪽 m, 경로 U, 1)
                float2 pulse : TEXCOORD1; // 아이콘 = (맥동 0~1, 크기 배율)
            };

            // LobbyFloorDashBeltLayout.SamplePath 와 같은 규칙 — 양 끝 밖은 끝 방향으로 곧게 늘인다.
            void SamplePath(float s, out float3 position, out float3 tangent)
            {
                int count = (int)_PathCount;
                int last = max(count - 1, 0);
                if (count <= 1 || s <= _PathPoints[0].w)
                {
                    tangent = normalize(_PathTangents[0].xyz);
                    position = _PathPoints[0].xyz + tangent * (s - _PathPoints[0].w);
                    return;
                }
                if (s >= _PathPoints[last].w)
                {
                    tangent = normalize(_PathTangents[last].xyz);
                    position = _PathPoints[last].xyz + tangent * (s - _PathPoints[last].w);
                    return;
                }

                int idx = last;
                [loop] for (int k = 1; k < MAX_PATH_SAMPLES; k++)
                {
                    if (k >= last || _PathPoints[k].w >= s)
                    {
                        idx = k;
                        break;
                    }
                }

                float4 a = _PathPoints[idx - 1];
                float4 b = _PathPoints[idx];
                float t = saturate((s - a.w) / max(b.w - a.w, 1e-6));
                position = lerp(a.xyz, b.xyz, t);
                float3 blended = lerp(_PathTangents[idx - 1].xyz, _PathTangents[idx].xyz, t);
                tangent = dot(blended, blended) > 1e-12 ? normalize(blended) : normalize(_PathTangents[idx].xyz);
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;
                if (v.kind.x < 0.5)
                {
                    o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                    o.data = float4(v.uv, 0, 0);
                    o.pulse = float2(0, 1);
                    return o;
                }

                // LobbyFloorDashBeltLayout.IconCenterCells — 예전 띠 UV 방식과 같은 자리·같은 아이콘 번호.
                float flow = _Time.y * _ScrollSpeed;
                float whole = floor(flow);
                float iconIndex = v.kind.y - 1.0 - whole;
                float s = (v.kind.y - 0.5 + (flow - whole)) * _Spacing;

                // 맥동 0~1 — 아이콘마다 위상을 조금씩 밀어 물결처럼(뒤 아이콘이 늦게). 흘러도 같은 아이콘은 같은 위상.
                float pulse = 0.5 + 0.5 * sin(6.2831853 * (_Time.y * _PulseSpeed - iconIndex * _PulsePhase));
                float sizeMul = 1.0 + _PulseScale * (pulse * 2.0 - 1.0);

                float3 center, forward;
                SamplePath(s, center, forward);
                float3 left = float3(-forward.z, 0, forward.x);
                left = dot(left, left) > 1e-12 ? normalize(left) : float3(-1, 0, 0);

                // 쿼드 반크기 = 돌아간 아이콘을 다 덮는 크기(√½·크기), 단 예전처럼 간격·폭 밖은 잘리므로 그 이상은 안 그린다.
                float reach = _IconSize * sizeMul * 0.7072;
                float2 halfExtent = float2(min(_Spacing * 0.5, reach), min(_Width * 0.5, reach));
                float2 local = v.uv * halfExtent;
                float3 positionOS = center + forward * local.x + left * local.y;

                o.positionCS = TransformObjectToHClip(positionOS);
                o.data = float4(local, (s + local.x) / max(_PathLength, 1e-4), 1);
                o.pulse = float2(pulse, sizeMul);
                return o;
            }

            float EndFade(float u)
            {
                float inside = step(0.0, u) * step(u, 1.0);
                return inside * (_EndFade > 0.0001 ? saturate(min(u, 1.0 - u) / _EndFade) : 1.0);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                if (i.data.w < 0.5)
                    return half4(_BeltColor.rgb, _BeltColor.a * EndFade(i.data.x));

                // 아이콘 로컬 좌표(m) — 진행 방향·왼쪽. 크기는 m 단위라 정사각 아이콘이 어디서나 정사각이다.
                float2 local = i.data.xy;
                float2 p = local / max(_IconSize * i.pulse.y, 1e-4);
                float rad = radians(_IconRotation);
                float sn = sin(rad), cs = cos(rad);
                // 아이콘을 반시계로 돌리려면 샘플 좌표는 시계로 돈다.
                float2 iconUV = float2(cs * p.x + sn * p.y, -sn * p.x + cs * p.y) + 0.5;

                half4 icon = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, iconUV);
                float inside = step(0.0, iconUV.x) * step(iconUV.x, 1.0) * step(0.0, iconUV.y) * step(iconUV.y, 1.0);
                // 예전과 같이 간격(앞뒤)·띠 폭(좌우) 밖은 자른다.
                inside *= step(abs(local.x), _Spacing * 0.5) * step(abs(local.y), _Width * 0.5);
                icon.a *= inside;

                half pulse = i.pulse.x;
                half brightness = lerp(1.0 - _PulseBrightness, 1.0, pulse);
                half4 iconCol = icon * _BaseColor;
                iconCol.rgb *= brightness;
                iconCol.a *= lerp(1.0 - _PulseBrightness * 0.5, 1.0, pulse);

                // 양 끝을 부드럽게 지운다 — 경로 밖으로 늘인 부분은 잘린다.
                return half4(iconCol.rgb, iconCol.a * EndFade(i.data.z));
            }
            ENDHLSL
        }
    }
}
