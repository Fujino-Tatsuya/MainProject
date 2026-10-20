// 할 일 57 S2 픽셀 비교 전용(EnemyC3DecalShaderTests). 빌드에 들어가지 않는다(Editor 폴더 · 참조 없음).
// DecalProjector 는 RT 로 직접 찍기 어려워, 데칼 표면 출력(색·알파)을 같은 함수로 평면에 그려 비교한다.
//  pass 0 = 기존: Bake 텍스처(_OldTex) → TelegraphShade  (SG_TelegraphDecal 의 Sample Texture → Custom Function 과 같은 식)
//  pass 1 = 새것: EnemyC3DecalShade(uv, worldPos)        (SG_EnemyC3Decal 의 Custom Function 이 부르는 바로 그 함수)
// 속성 이름은 SG_EnemyC3Decal 블랙보드와 같다 — 값은 런타임과 같은 EnemyC3Visuals.SetDecalProperties 가 넣는다.
// 출력은 프리멀티(rgb·a) — 데칼의 직선 알파 블렌드를 투명 RT 에 옮긴 것. 두 패스가 같은 블렌드라 비교에 영향 없음.
Shader "Hidden/EnemyC3DecalCompare"
{
    Properties
    {
        _OldTex ("Old Bake texture", 2D) = "white" {}
        _BaseColor ("Base color", Color) = (1, 1, 1, 1)
        _MaskTex ("Mask", 2D) = "white" {}
        _MaskParams ("Mask params", Vector) = (0, 0, 1, 0)
        _MaskOrigin ("Mask origin", Vector) = (0, 0, 0, 0)
        _MaskAxes ("Mask axes", Vector) = (1, 0, 0, 1)
        _MaskExtent ("Mask extent", Vector) = (1, 1, 0, 0)
        _C3Shape ("C3 Shape", Vector) = (0, 0.03, 180, 0)
        _C3Extent ("C3 Extent", Vector) = (10, 10, 10.2, 10.2)
        _C3Path ("C3 Path", Vector) = (0, 0, 0, 5)
        _C3Geom ("C3 Geom", Vector) = (3.5, 2.5, 2, 57)
        _C3Fins ("C3 Fins", Vector) = (0, 10, 0, 0)
        _C3Outline ("C3 Outline", Vector) = (1, 1, 1, 0.95)
        _C3InnerLine ("C3 Inner Line", Vector) = (1, 1, 1, 1)
        _C3Band ("C3 Band", Vector) = (1, 1, 1, 0.25)
        _C3Markers ("C3 Markers", Vector) = (1, 1, 1, 0.42)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
        float4 _MaskParams, _MaskOrigin, _MaskAxes, _MaskExtent;
        float4 _C3Shape, _C3Extent, _C3Path, _C3Geom, _C3Fins;
        float4 _C3Outline, _C3InnerLine, _C3Band, _C3Markers;
    CBUFFER_END
    TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);
    TEXTURE2D(_OldTex); SAMPLER(sampler_OldTex);

    #include "Assets/1.Scripts/Rendering/Telegraph/EnemyC3Decal.hlsl"

    struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
    struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };

    Varyings Vert(Attributes v)
    {
        Varyings o;
        o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
        o.positionCS = TransformWorldToHClip(o.positionWS);
        o.uv = v.uv;
        return o;
    }
    ENDHLSL

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "Old"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float3 c; float a;
                TelegraphShade_float(SAMPLE_TEXTURE2D(_OldTex, sampler_OldTex, i.uv), i.positionWS, c, a);
                return half4(c * a, a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "New"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float3 c; float a;
                EnemyC3DecalShade_float(i.uv, i.positionWS, c, a);
                return half4(c * a, a);
            }
            ENDHLSL
        }
    }
}
