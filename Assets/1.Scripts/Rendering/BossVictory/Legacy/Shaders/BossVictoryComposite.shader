// ----------------------------------------------------------------------------
//  BossVictoryComposite.shader — 보스 B 승리 연출(PLAN-boss-victory-cinematic §5.3·§4) 전체 화면 합성
//
//  Pass 0 "BossVictoryComposite" — 반투명(이펙트) 렌더 **뒤**(AfterRenderingTransparents):
//    _BlitTexture(이펙트까지 그려진 씬색) + _VictoryPreTex(반투명 전 씬색) + _VictoryIdTex(인물·보스 마스크) →
//    · 마스크 안          = 조명 결과 명도를 대상별 회색 램프로(어두운 쪽 _ShadowColor → 밝은 쪽 막타/팀원/보스) — 음영 유지(D6). 무기 포함.
//    · 반투명이 바꾼 픽셀  = 이펙트·무기 궤적 → 같은 방식의 단색 램프(_EffectColor) — 할 일 200 D2 "이펙트까지 전부 단색"
//                          (차이 = |씬 − 반투명 전| 최대 채널 × _EffectGain, 옅은 이펙트는 그만큼만 섞인다)
//    · 그 밖(배경)        = 단색 _BgColor(#EAAD70)로 **틴트**(_BgTint, 기본 34% — 배경이 비친다). 할 일 200 D1: 위/아래 그라데이션 두 색 → 단색.
//    · 발밑 회색 원(할 일 246) = 배경 중 막타 발밑(_GroundCircle.xyz)에서 **수평** 거리 _GroundCircle.w 안(높이 한도 없음 — 원기둥)은
//                          주황 틴트 대신 흑백(원래 명도 × _GroundCircleLook.y). 가장자리 _GroundCircleLook.x(m)에 걸쳐 섞는다.
//                          픽셀 월드 위치 = _CameraDepthTexture 로 되살림. 반경 0 = 끔. 인물·보스·이펙트 단색은 그대로.
//  _Weight 로 원래 화면과 섞는다(단계 0.72–0.80 들어옴, 이후 씬 끝까지 유지 — 할 일 200 D3).
//  글리치는 할 일 200 D4 로 뺐다(나중에 TV 글리치로 교체 예정 — 넣을 자리 = 이 셰이더 Frag 맨 끝).
//
//  Pass 1 "BossVictoryCopy" — 반투명 전 씬색을 _VictoryPreTex 로 복사(BeforeRenderingTransparents, 안개 다음).
//  색 입력은 C# 에서 linear 로 바꿔 넣는다(기획 HEX 는 sRGB).
// ----------------------------------------------------------------------------
Shader "Hidden/Rendering/BossVictoryComposite"
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
            Name "BossVictoryComposite"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D_X(_VictoryIdTex);
            TEXTURE2D_X(_VictoryPreTex);

            float _Weight;
            float _BgTint;
            float _Exposure;
            float _Gamma;
            float _EffectGain;
            float4 _BgColor;
            float4 _KillerColor;
            float4 _TeammateColor;
            float4 _BossColor;
            float4 _EffectColor;
            float4 _ShadowColor;
            float4 _GroundCircle;      // xyz 중심(월드), w 반경(m)
            float4 _GroundCircleLook;  // x 가장자리 부드러움(m), y 흑백 밝기

            // 발밑 회색 원 가중치(0 = 밖, 1 = 안). 수평(XZ) 거리만 본다 — 원기둥.
            half GroundCircleMask(float2 uv)
            {
                float r = _GroundCircle.w;
                if (r <= 0.0) return 0.0h;
                float depth = SampleSceneDepth(uv);
            #if !UNITY_REVERSED_Z
                depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
            #endif
                float3 wp = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float dist = distance(wp.xz, _GroundCircle.xz);
                return (half)(1.0 - smoothstep(max(0.0, r - _GroundCircleLook.x), r, dist));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                half3 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;
                if (_Weight <= 0.0001)
                    return half4(scene, 1.0h);

                half3 pre = SAMPLE_TEXTURE2D_X(_VictoryPreTex, sampler_PointClamp, uv).rgb;
                half4 id = SAMPLE_TEXTURE2D_X(_VictoryIdTex, sampler_PointClamp, uv);

                // 명도 → 램프(단색, 음영 유지).
                half lum = dot(scene, half3(0.2126h, 0.7152h, 0.0722h));
                half y = pow(saturate(lum * (half)_Exposure), (half)_Gamma);

                // 인물·보스(무기 포함) — 겹치면 막타 > 팀원 > 보스.
                half3 target = id.r > 0.5h ? _KillerColor.rgb : (id.g > 0.5h ? _TeammateColor.rgb : _BossColor.rgb);
                half3 person = lerp(_ShadowColor.rgb, target, y);

                // 이펙트 — 반투명 패스가 바꾼 만큼.
                half3 d = abs(scene - pre);
                half effect = saturate(max(d.r, max(d.g, d.b)) * (half)_EffectGain);
                half3 effectMono = lerp(_ShadowColor.rgb, _EffectColor.rgb, y);

                // 배경 — 단색 주황 쪽으로 틴트(배경이 비친다).
                half3 bg = lerp(scene, _BgColor.rgb, (half)_BgTint);
                // 발밑 회색 원(246) — 원 안 배경은 주황 대신 흑백(원래 무늬 유지).
                half3 gray = (lum * (half)_GroundCircleLook.y).xxx;
                bg = lerp(bg, gray, GroundCircleMask(uv));

                half3 victory = id.a > 0.5h ? person : lerp(bg, effectMono, effect);
                return half4(lerp(scene, victory, (half)_Weight), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "BossVictoryCopy"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCopy

            half4 FragCopy(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord).rgb, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
