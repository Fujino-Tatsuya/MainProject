using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static EnemyC3Visuals;

/// <summary>
/// Editor-only CPU reference for the enemy C3 art (moved out of the runtime EnemyC3Visuals in 할 일 57 S4b).
/// The runtime draws with EnemyC3Plate / SG_EnemyC3Decal and never builds textures; this 384² bake is kept only as
/// the pixel comparison baseline (EnemyC3*ShaderTests, EnemyC3Validation) and for the smash 09 sprite bake.
/// </summary>
public static class EnemyC3BakeReference
{
    static readonly Dictionary<string,Texture2D> textures=new();
    [InitializeOnEnterPlayMode]
    static void ResetForPlay()
    {
        // Reload with the selected JSON on each Play, as the runtime cache did before the move.
        foreach(var texture in textures.Values) if(texture!=null) UnityEngine.Object.DestroyImmediate(texture);
        textures.Clear();
    }
    static Color Over(Color under,Color over,float coverage=1f)
    {
        float a=Mathf.Clamp01(over.a*coverage), total=a+under.a*(1-a);
        if(total<=0) return Color.clear;
        return new Color((over.r*a+under.r*under.a*(1-a))/total,(over.g*a+under.g*under.a*(1-a))/total,
            (over.b*a+under.b*under.a*(1-a))/total,total);
    }
    static float Box(Vector2 p,Vector2 half)
    {
        Vector2 q=new Vector2(Mathf.Abs(p.x),Mathf.Abs(p.y))-half;
        return new Vector2(Mathf.Max(q.x,0),Mathf.Max(q.y,0)).magnitude+Mathf.Min(Mathf.Max(q.x,q.y),0);
    }
    static float Segment(Vector2 p,Vector2 a,Vector2 b)
    { Vector2 ab=b-a;return (p-a-ab*Mathf.Clamp01(Vector2.Dot(p-a,ab)/Mathf.Max(.000001f,ab.sqrMagnitude))).magnitude; }
    static float Polygon(Vector2 p,Vector2 a,Vector2 b,Vector2 c,Vector2 d,float aa)
    {
        bool inside=false;Vector2 prev=d;
        for(int i=0;i<4;i++) { Vector2 v=i==0?a:i==1?b:i==2?c:d; if((v.y>p.y)!=(prev.y>p.y)&&p.x<(prev.x-v.x)*(p.y-v.y)/(prev.y-v.y)+v.x)inside=!inside;prev=v; }
        float dist=Mathf.Min(Mathf.Min(Segment(p,a,b),Segment(p,b,c)),Mathf.Min(Segment(p,c,d),Segment(p,d,a)));
        return Mathf.Clamp01(.5f+(inside?dist:-dist)/aa);
    }
    static float Fins(Vector2 p,float width,float length,float unit,Geometry g,float aa)
    {
        float scale=Mathf.Max(0,g.linearMarkerScalePercent)/100;
        if(scale<=0||p.y<0||p.y>length)return 0;
        float step=Mathf.Max(10,g.linearMarkerSpacing)*unit;
        float index=Mathf.Round((p.y-g.linearMarkerStart*unit)/step);
        if(index<0)return 0;
        float center=g.linearMarkerStart*unit+index*step;
        float fs=38.4f/.146f*unit*scale,fw=width*.8f*scale;
        if(center+.047f*fs>=length-(g.gap+g.innerWidth)*unit)return 0;
        p.y-=center;float value=0;
        for(int side=-1;side<=1;side+=2) value=Mathf.Max(value,Polygon(p,
            new Vector2(.30f*side*fw,-.099f*fs),new Vector2(.013f*side*fw,.047f*fs),
            new Vector2(.145f*side*fw,.004f*fs),new Vector2(.35f*side*fw,-.058f*fs),aa));
        return value;
    }
    static float CircleFins(Vector2 p,float radius,Geometry g,float aa)
    {
        float unit=radius/165f,rad=radius*g.markerRadiusPercent/100,value=0;
        for(int i=0;i<4;i++)
        {
            float a=i*Mathf.PI/2;Vector2 outward=new Vector2(Mathf.Cos(a),Mathf.Sin(a)),side=new Vector2(-outward.y,outward.x);
            Vector2 q=new Vector2(Vector2.Dot(p,side),Vector2.Dot(p,outward)-rad)/unit;
            value=Mathf.Max(value,Polygon(q,new Vector2(-9,0),new Vector2(0,-16),new Vector2(9,0),new Vector2(0,-6),aa/unit));
        }
        return value;
    }
    // The core footprint stays fixed; the decorative outer stroke grows outside it, as on the design board.
    static Texture2D Bake(string key,string id,Vector2 extent,float unit,Func<Vector2,float> sdf,
        Func<Vector2,float,float> marker,bool fill=false,bool circle=false)
    {
        if(textures.TryGetValue(key,out var found)&&found!=null)return found;
        const int n=384;var tex=new Texture2D(n,n,TextureFormat.RGBA32,false){name="EnemyC3_"+key,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
        var pixels=new Color32[n*n];var e=Get(id).elements;var g=Get(id).geometry;
        Vector2 sampleExtent=extent+Vector2.one*(fill?0:2*g.outerWidth*unit);
        float aa=Mathf.Max(sampleExtent.x,sampleExtent.y)/n;
        for(int y=0;y<n;y++)for(int x=0;x<n;x++)
        {
            Vector2 p=new Vector2(((x+.5f)/n-.5f)*sampleExtent.x,((y+.5f)/n-.5f)*sampleExtent.y);
            float d=sdf(p),inside=Mathf.Clamp01(.5f-d/aa);Color c=Color.clear;
            if(fill)c=Over(c,e.fill.Value,inside);
            else
            {
                if(id=="09"||id=="16")c=Over(c,e.fill.Value,inside);
                float band=circle?Mathf.InverseLerp(g.gradientStart/100f,1,p.magnitude/(extent.x*.5f)):
                    Mathf.Clamp01(1+d/Mathf.Max(unit,51.16f*(1-g.gradientStart/100f)*unit));
                c=Over(c,e.band.Value,band*inside);
                if(marker!=null)c=Over(c,e.markers.Value,marker(p,aa)*inside);
                float innerStart=g.gap*unit,innerEnd=innerStart+g.innerWidth*unit;
                float inner=Mathf.Clamp01(.5f+(-d-innerStart)/aa)*Mathf.Clamp01(.5f+(d+innerEnd)/aa);
                c=Over(c,e.innerLine.Value,inner*inside);
                float rim=Mathf.Clamp01(.5f+d/aa)*Mathf.Clamp01(.5f+(g.outerWidth*unit-d)/aa);
                c=Over(c,e.outline.Value,rim);
            }
            pixels[y*n+x]=c;
        }
        tex.SetPixels32(pixels);tex.Apply(false,false);textures[key]=tex;return tex;
    }
    public static Texture2D Circle(string id,bool fill=false)
    {
        var g=Get(id).geometry;bool pins=id=="09"?g.smashShowMarkers:g.showMarkers;
        return Bake(id+"_circle_"+fill,id,new Vector2(330,330),1,p=>p.magnitude-165,
            pins?(p,aa)=>CircleFins(p,165,g,aa):null,fill,true);
    }
    public static Texture2D Square(string id,bool fill=false)=>Bake(id+"_square_"+fill,id,new Vector2(300,300),1,p=>Box(p,new Vector2(150,150)),null,fill);
    public static Texture2D Decal(string id,Vector2 extent,float coneRadius,float coneCenter,float halfAngle,float pathWidth,float pathLength)
    {
        bool union=coneRadius>0&&pathLength>0;bool band=pathLength>0&&!union;
        float unit=band?extent.x/(id=="04"||id=="05"||id=="11"?114f:200f):union?pathWidth/130f:coneRadius/165f;
        var g=Get(id).geometry;
        float Sdf(Vector2 p)
        {
            if(band)return Box(p,extent*.5f);
            if(union){p.y+=extent.y*.5f;return Mathf.Min(Box(p-new Vector2(0,pathLength*.5f),new Vector2(pathWidth*.5f,pathLength*.5f)),(p-new Vector2(0,coneCenter)).magnitude-coneRadius);}
            if(halfAngle>=179.9f)return p.magnitude-coneRadius;
            float a=halfAngle*Mathf.Deg2Rad;
            Vector2 edge=new Vector2(Mathf.Sin(a),Mathf.Cos(a))*coneRadius;
            // Distance to the finite sector arc, not the complete circle.
            // Outside the angular span the closest arc point is its endpoint.
            Vector2 folded=new Vector2(Mathf.Abs(p.x),p.y);
            float angle=Mathf.Atan2(folded.x,folded.y);
            float arcAngle=Mathf.Min(angle,a);
            Vector2 arcPoint=new Vector2(Mathf.Sin(arcAngle),Mathf.Cos(arcAngle))*coneRadius;
            float dist=Mathf.Min((folded-arcPoint).magnitude,Segment(folded,Vector2.zero,edge));
            bool inside=p.magnitude<=coneRadius&&angle<=a;
            return inside?-dist:dist;
        }
        Func<Vector2,float,float> marker=null;
        bool show=union?g.upperShowMarkers:id=="04"||id=="05"||id=="11"?g.dashShowMarkers:g.showMarkers;
        if(show&&(band||union))marker=(p,aa)=> {p.y+=extent.y*.5f;return (!union||Mathf.Abs(p.x)<=pathWidth*.5f)&&p.y<pathLength?Fins(p,band?extent.x:pathWidth,pathLength,unit,g,aa):0;};
        string key=FormattableString.Invariant($"{id}_{extent.x:F4}_{extent.y:F4}_{coneRadius:F4}_{coneCenter:F4}_{halfAngle:F2}_{pathWidth:F4}_{pathLength:F4}");
        return Bake(key,id,extent,unit,Sdf,marker,false,!band&&!union&&halfAngle>=179.9f);
    }
}
