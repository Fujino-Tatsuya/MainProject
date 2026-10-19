Shader "VeyTrace/Player/Skill Line Indicator"
{
    Properties
    {
        _Color ("Interior / Arrow Color", Color) = (0.7529412, 0.9607843, 0.8705882, 1)
        _OutlineColor ("C3 Outer Line", Color) = (0.9882353, 0.9882353, 0.9882353, 0.95)
        _InnerColor ("C3 Inner Line", Color) = (0.5921569, 0.7882353, 0.7019608, 1)
        _MarkerColor ("C3 Tapered Fins", Color) = (0.7529412, 0.9607843, 0.8705882, 0.42)
        _EndColor ("C3 Flat End", Color) = (0.9529412, 0.9568627, 0.8745098, 0.94)
        _Alpha ("Runtime Alpha", Range(0, 1)) = 0.28
        _ReferenceAlpha ("Full Visibility Alpha", Range(0.01, 1)) = 0.28
        _FillOpacity ("Interior Opacity", Range(0, 1)) = 0.232
        _DesignWidthPixels ("Source Width Reference", Float) = 100
        _OutlinePixels ("Outer Line Source Pixels", Float) = 3.5
        _InnerPixels ("Inner Line Source Pixels", Float) = 2.5
        _GapPixels ("Line Gap Source Pixels", Float) = 2
        _MarkerLength ("Fin Length (World Units)", Float) = 0.96
        _MarkerWidthScale ("Fin Width Scale", Float) = 0.8
        _MarkerSpacing ("Fin Spacing (World Units)", Float) = 2
        _MarkerStart ("First Fin Center (World Units)", Float) = 2.6
        _WorldSize ("World Size", Vector) = (1, 1, 0, 0)
        [Toggle] _Arrow ("Arrow", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionHCS:SV_POSITION; float2 uv:TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _OutlineColor;
                half4 _InnerColor;
                half4 _MarkerColor;
                half4 _EndColor;
                half _Alpha;
                half _ReferenceAlpha;
                half _FillOpacity;
                float _DesignWidthPixels;
                float _OutlinePixels;
                float _InnerPixels;
                float _GapPixels;
                float _MarkerLength;
                float _MarkerWidthScale;
                float _MarkerSpacing;
                float _MarkerStart;
                float4 _WorldSize;
                half _Arrow;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS=TransformObjectToHClip(input.positionOS.xyz);
                output.uv=input.uv;
                return output;
            }
            half ArrowMask(float2 uv)
            {
                const float bodyEnd=0.62;
                const float bodyHalfWidth=0.14;
                float headT=saturate((uv.y-bodyEnd)/(1.0-bodyEnd));
                float halfWidth=uv.y<bodyEnd?bodyHalfWidth:lerp(0.5,0.0,headT);
                float signedDistance=halfWidth-abs(uv.x-0.5);
                float antialias=max(fwidth(signedDistance),0.001);
                return smoothstep(-antialias,antialias,signedDistance);
            }
            float SegmentDistance(float2 p,float2 a,float2 b)
            {
                float2 edge=b-a;
                float t=saturate(dot(p-a,edge)/max(dot(edge,edge),0.0000001));
                return length(p-a-t*edge);
            }
            bool CrossesRay(float2 p,float2 a,float2 b)
            {
                if((a.y>p.y)==(b.y>p.y))return false;
                return p.x<a.x+(p.y-a.y)*(b.x-a.x)/(b.y-a.y);
            }
            half FinMask(float2 p,float2 a,float2 b,float2 c,float2 d,float antialias)
            {
                // One polygon: the internal triangulation diagonal is not an edge.
                float distance=min(min(SegmentDistance(p,a,b),SegmentDistance(p,b,c)),
                                   min(SegmentDistance(p,c,d),SegmentDistance(p,d,a)));
                bool inside=CrossesRay(p,a,b)!=CrossesRay(p,b,c);
                inside=inside!=CrossesRay(p,c,d);
                inside=inside!=CrossesRay(p,d,a);
                return smoothstep(-antialias,antialias,inside?distance:-distance);
            }
            void Over(inout half4 composite,half3 color,half opacity)
            {
                composite.rgb=color*opacity+composite.rgb*(1.0h-opacity);
                composite.a=opacity+composite.a*(1.0h-opacity);
            }
            half4 Frag(Varyings input):SV_Target
            {
                // Preserve movement-preview arrows and their original alpha semantics.
                if(_Arrow>0.5h)return half4(_Color.rgb,_Color.a*_Alpha*ArrowMask(input.uv));
                float2 size=max(_WorldSize.xy,float2(0.001,0.001));
                float2 samplePos=float2((input.uv.x-0.5)*size.x,input.uv.y*size.y);
                // Derivatives must use continuous coordinates, before the repeat wrap.
                // Differentiating finPos creates a false AA stripe at every cell boundary.
                float markerAA=max(0.5*(length(ddx(samplePos))+length(ddy(samplePos))),0.0001);
                float2 edges=min(input.uv,1.0-input.uv)*size;
                float nearest=min(edges.x,edges.y);
                float aa=max(fwidth(nearest),0.0001);
                half inside=smoothstep(0.0,aa,nearest);
                // Design pixels become a stable fraction of skill width. All graphics
                // remain inside the existing gameplay quad, including wall-clipped ends.
                float unit=size.x/max(_DesignWidthPixels,1.0);
                float outerWidth=max(0.0,_OutlinePixels)*unit;
                float innerStart=outerWidth+max(0.0,_GapPixels)*unit;
                float innerWidth=max(0.0,_InnerPixels)*unit;
                half outer=outerWidth>0.0?1.0h-smoothstep(outerWidth-aa,outerWidth+aa,nearest):0.0h;
                half inner=innerWidth>0.0?smoothstep(innerStart-aa,innerStart+aa,nearest)*(1.0h-smoothstep(innerStart+innerWidth-aa,innerStart+innerWidth+aa,nearest)):0.0h;
                half visibility=saturate(_Alpha/max(_ReferenceAlpha,0.01h));
                half rangeVisibility=visibility*visibility;
                half detailVisibility=rangeVisibility*visibility;
                half face=_FillOpacity*lerp(0.04h/0.145h,1.0h,saturate(input.uv.y));
                half4 result=half4(_Color.rgb*face,face);
                Over(result,_OutlineColor.rgb,outer*_OutlineColor.a);
                Over(result,_InnerColor.rgb,inner*_InnerColor.a);
                result*=rangeVisibility;
                half fins=0.0h;
                // Repeat in segment-local world distance, anchored to the caster.
                // Changing range reveals more fins without scaling or sliding them.
                float finLength=max(_MarkerLength,0.01);
                float finScale=finLength/0.146;
                float finWidth=size.x*max(_MarkerWidthScale,0.01);
                float spacing=max(_MarkerSpacing,finLength+unit*2.0);
                float firstCenter=max(_MarkerStart,0.099*finScale+innerStart+innerWidth);
                float index=floor((samplePos.y-firstCenter)/spacing+0.5);
                float center=firstCenter+index*spacing;
                float2 finPos=float2(samplePos.x,samplePos.y-center);
                [unroll]for(int side=-1;side<=1;side+=2)
                {
                    float2 a=float2(0.30*side*finWidth,-0.099*finScale);
                    float2 b=float2(0.013*side*finWidth,0.047*finScale);
                    float2 c=float2(0.145*side*finWidth,0.004*finScale);
                    float2 d=float2(0.35*side*finWidth,-0.058*finScale);
                    half shape=FinMask(finPos,a,b,c,d,markerAA);
                    fins=max(fins,shape);
                }
                // Fade in only once the complete fin fits before the end contour.
                float remaining=size.y-(center+0.047*finScale+innerStart+innerWidth);
                half room=index>=0.0?smoothstep(0.0,max(unit*8.0,0.01),remaining):0.0h;
                Over(result,_MarkerColor.rgb,fins*_MarkerColor.a*detailVisibility*room);
                float endDistance=(1.0-input.uv.y)*size.y;
                half endBar=(1.0h-smoothstep(outerWidth-aa,outerWidth+aa,endDistance))*(1.0h-smoothstep(size.x*0.435,size.x*0.445,abs(samplePos.x)));
                Over(result,_EndColor.rgb,endBar*_EndColor.a*rangeVisibility);
                return half4(result.rgb/max(result.a,0.0001h),result.a*inside*_Color.a);
            }
            ENDHLSL
        }
    }
}
