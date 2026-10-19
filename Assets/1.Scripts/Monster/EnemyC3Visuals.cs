using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Selected enemy C3 art preset. Only presentation; hit volumes and clocks stay with their owners.</summary>
public static class EnemyC3Visuals
{
    [Serializable] public class Element { public string color; public float opacity; public Color Value { get { ColorUtility.TryParseHtmlString(color, out Color c); c.a=opacity; return c; } } }
    [Serializable] public class Elements { public Element outline,innerLine,band,fill,markers; }
    [Serializable] public class Geometry
    {
        public float outerWidth,innerWidth,gap,gradientStart,markerRadiusPercent;
        public float linearMarkerScalePercent,linearMarkerSpacing,linearMarkerStart;
        public bool showMarkers,dashShowMarkers,upperShowMarkers,smashShowMarkers;
    }
    [Serializable] public class Entry { public string id; public Elements elements; public Geometry geometry; }
    [Serializable] class Preset { public Entry[] entries; }
    static Dictionary<string,Entry> entries;
    static readonly Dictionary<string,Texture2D> textures=new();
    static readonly Dictionary<Sprite,Sprite> smashSprites=new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetForPlay()
    {
        // Reload the selected JSON on each Play, including when domain reload is disabled.
        foreach(var sprite in smashSprites.Values) if(sprite!=null) UnityEngine.Object.Destroy(sprite);
        foreach(var texture in textures.Values) if(texture!=null) UnityEngine.Object.Destroy(texture);
        smashSprites.Clear();textures.Clear();entries=null;
    }
    public static Entry Get(string id)
    {
        if(entries==null)
        {
            entries=new Dictionary<string,Entry>();
            var text=Resources.Load<TextAsset>("Telegraph/EnemyC3Selected");
            if(text==null) throw new InvalidOperationException("Enemy C3 preset is missing.");
            foreach(var e in JsonUtility.FromJson<Preset>(text.text).entries) entries[e.id]=e;
        }
        return entries[id];
    }
    public static float OuterScale(string id,bool square=false)=>1+Get(id).geometry.outerWidth/(square?150f:165f);
    public static float DecalPadding(string id,Vector2 extent,float coneRadius,float pathWidth,float pathLength)
    {
        bool union=coneRadius>0&&pathLength>0,band=pathLength>0&&!union;
        float unit=band?extent.x/(id=="04"||id=="05"||id=="11"?114f:200f):union?pathWidth/130f:coneRadius/165f;
        return Get(id).geometry.outerWidth*unit;
    }
    public static Color FillColor(string id)=>Get(id).elements.fill.Value;
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
    public static void PaintPlate(Renderer outer,Renderer fill,string id,bool square=false)
    {
        outer.sortingOrder=1;fill.sortingOrder=0;
        BossPatternVisuals.Paint(outer,Color.white,square?Square(id):Circle(id));
        BossPatternVisuals.Paint(fill,Color.white,square?Square(id,true):Circle(id,true));
    }
    public static void PaintActive(Renderer r,float alpha=1)
    { BossPatternVisuals.Paint(r,new Color(1,1,1,Mathf.Clamp01(alpha)),Square("16")); }
    public static void ApplySmash(GameObject go)
    {
        foreach(var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if(sr.sprite==null)continue;
            if(!sr.sprite.name.StartsWith("EnemyC3_"))
            {
                var old=sr.sprite;
                if(!smashSprites.TryGetValue(old,out var sprite))
                {
                    var tex=Circle("09");sprite=Sprite.Create(tex,new Rect(0,0,tex.width,tex.height),new Vector2(.5f,.5f),tex.width/(old.bounds.size.x*OuterScale("09")));
                    sprite.name="EnemyC3_Smash";smashSprites[old]=sprite;
                }
                sr.sprite=sprite;
            }
            sr.color=new Color(1,1,1,sr.color.a);
        }
    }
    public static GameObject CreateJump(Vector3 point,float radius,float duration)
    {
        var root=new GameObject("EnemyC3_JumpTelegraph");root.transform.position=point;
        var view=root.AddComponent<EnemyC3JumpView>();view.Begin(radius,duration);return root;
    }
}

// Uses the same local growth duration received by the original jump presentation RPC.
public sealed class EnemyC3JumpView : MonoBehaviour
{
    MeshRenderer outer,fill;float radius,duration,elapsed;
    public void Begin(float r,float d)
    {
        radius=r;duration=d;outer=BossPatternVisuals.CreateFlat("Boundary",transform);fill=BossPatternVisuals.CreateFlat("Growing",transform);
        EnemyC3Visuals.PaintPlate(outer,fill,"06");
        BossPatternVisuals.Paint(fill,Color.white,EnemyC3Visuals.Circle("07",true));
        outer.transform.localPosition=Vector3.up*.07f;fill.transform.localPosition=Vector3.up*.08f;
        float outerDiameter=r*2*EnemyC3Visuals.OuterScale("06");outer.transform.localScale=new Vector3(outerDiameter,1,outerDiameter);outer.gameObject.SetActive(true);fill.gameObject.SetActive(true);UpdateScale();
    }
    void Update(){elapsed+=Time.deltaTime;UpdateScale();}
    void UpdateScale(){float r=Mathf.Max(.001f,radius*(duration<=0?1:Mathf.Clamp01(elapsed/duration)));fill.transform.localScale=new Vector3(r*2,1,r*2);}
}
