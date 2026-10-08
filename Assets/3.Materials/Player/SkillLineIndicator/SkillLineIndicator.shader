Shader "VeyTrace/Player/Skill Line Indicator"
{
    Properties
    {
        _Color ("Color", Color) = (0.55, 0.95, 1, 1)
        _Alpha ("Alpha", Range(0, 1)) = 0.3
        _BorderWidth ("Border Width (World)", Float) = 0.06
        _WorldSize ("World Size", Vector) = (1, 1, 0, 0)
        [Toggle] _Arrow ("Arrow", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Alpha;
                float _BorderWidth;
                float4 _WorldSize;
                half _Arrow;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half ArrowMask(float2 uv)
            {
                const float bodyEnd = 0.62;
                const float bodyHalfWidth = 0.14;
                float headT = saturate((uv.y - bodyEnd) / (1.0 - bodyEnd));
                float halfWidth = uv.y < bodyEnd
                    ? bodyHalfWidth
                    : lerp(0.5, 0.0, headT);
                float signedDistance = halfWidth - abs(uv.x - 0.5);
                float antialias = max(fwidth(signedDistance), 0.001);
                return smoothstep(-antialias, antialias, signedDistance);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half opacity;
                if (_Arrow > 0.5h)
                {
                    opacity = ArrowMask(input.uv);
                }
                else
                {
                    float2 worldSize = max(_WorldSize.xy, float2(0.001, 0.001));
                    float2 edgeWorld = min(input.uv, 1.0 - input.uv) * worldSize;
                    float nearestEdge = min(edgeWorld.x, edgeWorld.y);
                    float antialias = max(fwidth(nearestEdge), 0.001);
                    half inside = smoothstep(0.0, antialias, nearestEdge);
                    half border = 1.0h - smoothstep(
                        max(0.0, _BorderWidth - antialias),
                        _BorderWidth + antialias,
                        nearestEdge);
                    opacity = inside * lerp(0.28h, 1.0h, border);
                }

                return half4(_Color.rgb, _Color.a * _Alpha * opacity);
            }
            ENDHLSL
        }
    }
}
