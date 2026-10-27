// 연출용 화면 공간 실루엣 외곽선(PLAN-zone4-intro-cinematic — 4구역 소개 다리 샷, 경석 10-27).
// 근접 하이라이트(Zone/InteractOutline)는 반전 헐 + 깊이 판정이라, 바닥·벽 홈에 붙은 다리는 부풀린 테두리가 바닥·벽 속으로 들어가
// 가려져 조각만 보였다. 여기서는 깊이와 무관하게 대상 실루엣 둘레에만 일정한 두께의 테두리를 그린다:
//   ① Mask(SRPDefaultUnlit) — 대상 메시가 화면에 차지하는 영역에 스텐실 비트를 켠다(색·깊이 안 씀, 가려져도 표시 = ZTest Always).
//   ② Outline(UniversalForward) — 메시를 화면 공간에서 부풀려 맨 위에 그리되(ZTest Always) 그 비트가 켜진 안쪽은 뺀다 → 실루엣 바깥 테두리만.
// URP 는 한 물체의 패스를 SRPDefaultUnlit → UniversalForward 순서로 그린다. 큐 Overlay 라 모든 불투명·반투명 뒤.
// 대상 렌더러의 머티리얼 배열 끝에 덧붙여 쓴다(본체 머티리얼 무수정). 스텐실 비트는 128 하나만 읽고 쓴다(다른 사용과 충돌 회피).
Shader "Zone/CinematicSilhouetteOutline"
{
    Properties
    {
        _OutlineColor ("Color", Color) = (1, 0.92, 0.55, 1)
        _OutlineWidth ("Width (screen)", Range(0, 20)) = 7
        _OutlineScale ("Mesh Scale", Range(1, 1.1)) = 1.0
        [IntRange] _StencilBit ("Stencil Bit", Range(1, 255)) = 128
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
        half4 _OutlineColor;
        float _OutlineWidth;
        float _OutlineScale;
        float _StencilBit;
        CBUFFER_END

        // 범위 밖 버리기(전역 — 연출이 매번 넣는다, 머티리얼 에셋에 안 남게). 존 중심(xz)에서 반경 밖 픽셀은 외곽선 계산에서 뺀다 —
        // 이동식 다리 중 2층 바닥 아래로 들어간 부분까지 투시해 테두리가 바닥 위로 삐져나왔다(경석 10-27). 반경 0 = 끔.
        // 영역 표시는 반경에서, 테두리는 반경 + _CinematicClipEdge 까지 — 그 띠가 바닥 가장자리를 따라 선으로 남는다.
        float4 _CinematicClipCenter;
        float _CinematicClipRadius;
        float _CinematicClipEdge;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        void ClipOutside(float3 positionWS, float extra)
        {
            if (_CinematicClipRadius <= 0) return;
            float d = length(positionWS.xz - _CinematicClipCenter.xz);
            clip(_CinematicClipRadius + extra - d);
        }
        ENDHLSL

        Pass
        {
            Name "Mask"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ColorMask 0
            ZWrite Off
            ZTest Always
            Cull Off
            Stencil
            {
                Ref [_StencilBit]
                WriteMask [_StencilBit]
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                ClipOutside(i.positionWS, 0);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil
            {
                Ref [_StencilBit]
                ReadMask [_StencilBit]
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            // 근접 외곽선(Zone/InteractOutline)과 같은 화면 공간 법선 밀기 — 두께가 거리와 무관하다.
            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.positionWS = TransformObjectToWorld(v.positionOS.xyz * _OutlineScale);
                float4 clip = TransformWorldToHClip(o.positionWS);
                float3 clipNormal = mul((float3x3)UNITY_MATRIX_VP, TransformObjectToWorldDir(v.normalOS, false));
                float2 aspect = float2(_ScreenParams.x / _ScreenParams.y, 1);
                float2 dir = clipNormal.xy;
                float len = length(dir);
                if (len > 1e-5) dir /= len;
                clip.xy += dir / aspect * _OutlineWidth * clip.w * 0.005;
                o.positionCS = clip;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                ClipOutside(i.positionWS, _CinematicClipEdge);
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
