// ----------------------------------------------------------------------------
//  BossVictoryComposite.shader — 보스 B 승리 연출(PLAN-boss-victory-cinematic §5.3·§4) 전체 화면 합성
//
//  _BlitTexture(조명·안개까지 끝난 씬색, 반투명 전) + _VictoryIdTex(마스크) →
//    · 마스크 밖  = 주황 세로 그라데이션으로 **틴트**(_BgTint, 기본 34% — 배경이 비친다. 단색으로 칠하지 않는다, 경석 10-27)
//    · 마스크 안  = 조명 결과 명도를 대상별 회색 램프로(어두운 쪽 _ShadowColor → 밝은 쪽 막타/팀원/보스) — 음영 유지(D6)
//  _Weight 로 원래 화면과 섞는다(0.72–0.80 들어옴, 보스 디졸브 시작에 원복).
//
//  글리치(_Glitch 0~1, 레퍼런스 ZZZ 1:30~1:33 · 경석 10-27 캡처): 카메라가 움직일 때 · 멈출 때 · 다시 움직일 때 터진다.
//    가로 띠가 조각나 좌우로 밀림 + 가로 잔상(스트릭) + 회색으로 밝게 날아감 + 얇은 밝은 스캔라인.
//  핵심 타격 VFX·타이포는 이 뒤 투명 패스라 원색이다. 색 입력은 C# 에서 linear 로 바꿔 넣는다(기획 HEX 는 sRGB).
// ----------------------------------------------------------------------------
Shader "Hidden/Rendering/BossVictoryComposite"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "BossVictoryComposite"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_VictoryIdTex);

            float _Weight;
            float _BgTint;
            float _Exposure;
            float _Gamma;
            float _Glitch;
            float _GlitchTime;
            float _GlitchShift;
            float _GlitchStreak;
            float _GlitchWash;
            float4 _BgTop;
            float4 _BgBottom;
            float4 _KillerColor;
            float4 _TeammateColor;
            float4 _BossColor;
            float4 _ShadowColor;

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            half3 Scene(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                float g = saturate(_Glitch);
                float frame = floor(_GlitchTime * 24.0);   // 글리치 패턴은 24fps 로 끊어 바뀐다(필름 지직거림)
                float2 suv = uv;
                float smear = 0.0;
                float scan = 0.0;

                if (g > 0.0001)
                {
                    // 가로 띠 — 프레임마다 띠 높이가 바뀐다. 일부 띠만 좌우로 밀린다(세기가 클수록 많이·멀리).
                    float rows = lerp(18.0, 70.0, Hash(float2(frame, 3.1)));
                    float row = floor(uv.y * rows);
                    float pick = Hash(float2(row, frame));
                    float moved = step(1.0 - 0.55 * g, pick);
                    suv.x += moved * (Hash(float2(row + 17.0, frame)) - 0.5) * _GlitchShift * g;
                    // 가로 잔상 — 밀린 띠일수록 길게 번진다.
                    smear = (0.004 + moved * 0.03) * _GlitchStreak * g;
                    // 얇은 밝은 스캔라인.
                    float fine = floor(uv.y * 360.0);
                    scan = step(0.985 - 0.02 * g, Hash(float2(fine, frame + 5.0))) * g;
                }

                half3 scene = Scene(suv);
                if (smear > 0.0)
                {
                    scene = (scene * 2.0
                        + Scene(suv + float2(smear, 0.0)) + Scene(suv - float2(smear, 0.0))
                        + Scene(suv + float2(smear * 2.0, 0.0)) + Scene(suv - float2(smear * 2.0, 0.0))) / 6.0;
                }

                half lum = dot(scene, half3(0.2126h, 0.7152h, 0.0722h));
                half3 outColor = scene;

                if (_Weight > 0.0001)
                {
                    half4 id = SAMPLE_TEXTURE2D_X(_VictoryIdTex, sampler_PointClamp, suv);

                    // 배경 — 주황 세로 그라데이션 쪽으로 틴트(배경이 비친다).
                    half3 orange = lerp(_BgBottom.rgb, _BgTop.rgb, saturate(uv.y));
                    half3 bg = lerp(scene, orange, (half)_BgTint);

                    // 인물·보스 — 명도 → 램프(회색 단색, 음영 유지). 겹치면 막타 > 팀원 > 보스.
                    half y = pow(saturate(lum * (half)_Exposure), (half)_Gamma);
                    half3 target = id.r > 0.5h ? _KillerColor.rgb : (id.g > 0.5h ? _TeammateColor.rgb : _BossColor.rgb);
                    half3 person = lerp(_ShadowColor.rgb, target, y);

                    half3 victory = id.a > 0.5h ? person : bg;
                    outColor = lerp(scene, victory, (half)_Weight);
                }

                if (g > 0.0001)
                {
                    // 회색으로 밝게 날아감 — 명도를 올린 회색(레퍼런스: 하얗게 번진 회색 화면).
                    half grey = saturate(dot(outColor, half3(0.2126h, 0.7152h, 0.0722h)) * 1.35h + 0.18h);
                    outColor = lerp(outColor, half3(grey, grey, grey), (half)(_GlitchWash * g));
                    outColor += scan * 0.35h;
                }

                return half4(outColor, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
