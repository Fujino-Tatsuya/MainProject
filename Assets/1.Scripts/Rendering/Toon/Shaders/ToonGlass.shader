// ----------------------------------------------------------------------------
//  ToonGlass.shader — 은은한 투명 안경 렌즈 (URP17)
//  반투명 알파 블렌드 + 프레넬(가장자리만 살짝 빛남)로 "투명 유리" 느낌.
//  안경 렌즈 메시 전용. 양면(Cull Off).
// ----------------------------------------------------------------------------
Shader "Project/ToonGlass"
{
    Properties
    {
        _TintColor ("Tint (rgb) / Alpha (a)", Color) = (0.62,0.70,0.85,0.20)
        _FresnelPower ("Fresnel Power", Range(0.5,8)) = 3.0
        _EdgeBrightness ("Edge Brightness", Range(0,2)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "GlassForward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TintColor;
                half  _FresnelPower;
                half  _EdgeBrightness;
            CBUFFER_END

            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings   { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; float3 positionWS:TEXCOORD1; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS   = GetVertexNormalInputs(IN.normalOS).normalWS;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));
                // abs: 양면이라 뒷면도 동일 처리
                half fres = pow(1.0h - saturate(abs(dot(N, V))), _FresnelPower);
                half3 col = _TintColor.rgb + fres * _EdgeBrightness;
                half  a   = saturate(_TintColor.a + fres * _EdgeBrightness * 0.5h);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
