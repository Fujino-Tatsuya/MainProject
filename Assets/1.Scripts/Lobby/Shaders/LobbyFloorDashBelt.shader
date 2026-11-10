// 로비 바닥 대시 벨트(할 일 257) — 바닥에 눕힌 긴 띠 위에 대시 아이콘(») 이 길이 방향(+U)으로 반복되며,
// 아이콘마다 밝기·크기가 맥동하고 아주 느리게 앞으로 흐른다. 흐름·맥동은 셰이더가 _Time 으로 직접 민다(스크립트 매 프레임 갱신 없음).
// 수치는 LobbyFloorDashBelt 가 MaterialPropertyBlock 으로 넣는다. 텍스처 = 50.Art/UI/HUD/Common_Skillicon/Common_DashIcon.png 참조만.
// 반복은 frac 으로 한다 — 텍스처 임포트(Clamp·밉맵 없음, SVN .meta)는 건드리지 않는다. 띠가 길어지면 아이콘 개수가 는다(늘어나지 않음).
// 아트 방향: 원본 » 는 +U(오른쪽)를 가리킨다 = 진행 방향. 그림이 바뀌면 _IconRotation(도, 반시계)만 고친다.
Shader "Lobby/FloorDashBelt"
{
    Properties
    {
        _BaseMap ("Icon", 2D) = "white" {}
        [HDR] _BaseColor ("Icon color", Color) = (0.3, 0.85, 1, 0.9)
        _BeltColor ("Belt fill color (a = 0 to hide)", Color) = (0.3, 0.85, 1, 0.12)
        _Tiling ("Icons along length (length / spacing)", Float) = 6
        _IconScaleUV ("Icon size in cell UV (x = along, y = across)", Vector) = (1, 1, 0, 0)
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

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _BeltColor;
                float _Tiling;
                float4 _IconScaleUV;
                float _IconRotation;
                float _ScrollSpeed;
                float _PulseSpeed;
                float _PulsePhase;
                float _PulseBrightness;
                float _PulseScale;
                float _EndFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // U = 길이(진행 방향) 0~1, V = 폭 0~1. 칸 좌표 = U * 칸 수 - 시간 * 속도(앞으로 흐름).
                float cellCoord = i.uv.x * _Tiling - _Time.y * _ScrollSpeed;
                float cellIndex = floor(cellCoord);
                float cellU = cellCoord - cellIndex; // frac — 텍스처 Clamp 와 무관하게 반복

                // 맥동 0~1 — 칸마다 위상을 조금씩 밀어 물결처럼(뒤 칸이 늦게). 같은 아이콘은 흘러도 같은 위상을 유지한다.
                float pulse = 0.5 + 0.5 * sin(6.2831853 * (_Time.y * _PulseSpeed - cellIndex * _PulsePhase));
                float sizeMul = 1.0 + _PulseScale * (pulse * 2.0 - 1.0);

                // 칸 중심 기준 아이콘 UV — 크기 비율은 컴포넌트가 m 단위로 계산해 정사각 아이콘이 찌그러지지 않는다.
                float2 scale = max(_IconScaleUV.xy * sizeMul, 1e-4);
                float2 p = float2(cellU - 0.5, i.uv.y - 0.5) / scale;
                float rad = radians(_IconRotation);
                float s = sin(rad), c = cos(rad);
                // 아이콘을 반시계로 돌리려면 샘플 좌표는 시계로 돈다.
                float2 iconUV = float2(c * p.x + s * p.y, -s * p.x + c * p.y) + 0.5;

                // 경사는 frac 이전 연속 좌표로 — 칸 경계에서 밉 선택이 튀지 않게(지금 텍스처는 밉맵 없음).
                float2 cont = float2(cellCoord, i.uv.y);
                float2 dx = ddx(cont) / scale;
                float2 dy = ddy(cont) / scale;
                half4 icon = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, iconUV, dx, dy);
                float inside = step(0.0, iconUV.x) * step(iconUV.x, 1.0) * step(0.0, iconUV.y) * step(iconUV.y, 1.0);
                icon.a *= inside;

                half brightness = lerp(1.0 - _PulseBrightness, 1.0, pulse);
                half4 iconCol = icon * _BaseColor;
                iconCol.rgb *= brightness;
                iconCol.a *= lerp(1.0 - _PulseBrightness * 0.5, 1.0, pulse);

                // 벨트 바탕 위에 아이콘을 덮는다(over).
                half a = iconCol.a + _BeltColor.a * (1.0 - iconCol.a);
                half3 rgb = a > 1e-4
                    ? (iconCol.rgb * iconCol.a + _BeltColor.rgb * _BeltColor.a * (1.0 - iconCol.a)) / a
                    : half3(0, 0, 0);

                // 양 끝을 부드럽게 지운다 — 아이콘이 갑자기 생기고 사라지지 않게.
                float fade = _EndFade > 0.0001
                    ? saturate(min(i.uv.x, 1.0 - i.uv.x) / _EndFade)
                    : 1.0;
                return half4(rgb, a * fade);
            }
            ENDHLSL
        }
    }
}
