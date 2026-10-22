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
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetForPlay()
    {
        // Reload the selected JSON on each Play, including when domain reload is disabled.
        WellsDroneVisualPool.ResetForPlay();
        plateBlocks.Clear();entries=null;smashSprite=null;prewarmed=false;
    }
    static bool prewarmed;
    /// <summary>
    /// Loading-screen prewarm (할 일 57 R 예열) — data, materials, plate blocks, smash sprite, C3 shader variants and the
    /// C3 paint paths on throw-away objects, so the first telegraph in the map does not pay for them. Once per Play
    /// (reset with the caches above); later calls do nothing and return false. Never throws — a failure only logs.
    /// Called from NetworkLoadingFlowController when the target scene load starts. Details: EnemyC3Prewarm.
    /// </summary>
    public static bool Prewarm()
    {
        if(prewarmed)return false;
        prewarmed=true;
        try{EnemyC3Prewarm.Run();}
        catch(Exception e){Debug.LogException(e);}
        return true;
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
    public static float DecalPadding(string id,Vector2 extent,float coneRadius,float pathWidth,float pathLength)=>
        Get(id).geometry.outerWidth*DecalUnit(id,extent,coneRadius,pathWidth,pathLength);
    // Design units → metres for a decal shape (same as the editor EnemyC3BakeReference.Decal()).
    static float DecalUnit(string id,Vector2 extent,float coneRadius,float pathWidth,float pathLength)
    {
        bool union=coneRadius>0&&pathLength>0,band=pathLength>0&&!union;
        return band?extent.x/(id=="04"||id=="05"||id=="11"?114f:200f):union?pathWidth/130f:coneRadius/165f;
    }
    public static Color FillColor(string id)=>Get(id).elements.fill.Value;
    // The CPU 384² Bake (Circle/Square/Decal) is editor-only: Monster/Editor/EnemyC3BakeReference.cs (pixel comparison
    // baseline and smash 09 sprite bake). The runtime below never creates textures.
    // Floor plates (12·14·15·16, jump 06·07) are drawn by the EnemyC3Plate shader — no runtime texture.
    static readonly Dictionary<(string,bool,bool),MaterialPropertyBlock> plateBlocks=new();
    static Material plateMaterial;
    static readonly int TintId=Shader.PropertyToID("_Color"),ModeId=Shader.PropertyToID("_C3Mode"),SizeId=Shader.PropertyToID("_C3Size"),
        GeomId=Shader.PropertyToID("_C3Geom"),OutlineId=Shader.PropertyToID("_C3Outline"),InnerLineId=Shader.PropertyToID("_C3InnerLine"),
        BandId=Shader.PropertyToID("_C3Band"),FillId=Shader.PropertyToID("_C3Fill"),MarkersId=Shader.PropertyToID("_C3Markers");
    public static Material PlateMaterial
    {
        get
        {
            if(plateMaterial!=null)return plateMaterial;
            // Resources asset keeps the shader in builds; its render queue (3100) matches BossPatternVisuals plates.
            plateMaterial=Resources.Load<Material>("Telegraph/MA_EnemyC3Plate");
            if(plateMaterial==null)plateMaterial=new Material(Shader.Find("Monster/EnemyC3Plate")){name="EnemyC3Plate",renderQueue=3100};
            return plateMaterial;
        }
    }
    /// <summary>Plate layer values for the EnemyC3Plate shader — same inputs as EnemyC3BakeReference.Circle()/Square(). Colors stay gamma (SetVector).</summary>
    public static void SetPlateProperties(MaterialPropertyBlock block,string id,bool square,bool fill)
    {
        var e=Get(id).elements;var g=Get(id).geometry;
        bool pins=!square&&(id=="09"?g.smashShowMarkers:g.showMarkers);
        float core=square?150:165;
        block.SetColor(TintId,Color.white);
        block.SetVector(ModeId,new Vector4(square?1:0,fill?1:0,id=="09"||id=="16"?1:0,pins?1:0));
        block.SetVector(SizeId,new Vector4(core,core*2+(fill?0:2*g.outerWidth),g.markerRadiusPercent,0));
        block.SetVector(GeomId,new Vector4(g.outerWidth,g.innerWidth,g.gap,g.gradientStart));
        block.SetVector(OutlineId,e.outline.Value);block.SetVector(InnerLineId,e.innerLine.Value);
        block.SetVector(BandId,e.band.Value);block.SetVector(FillId,e.fill.Value);block.SetVector(MarkersId,e.markers.Value);
    }
    internal static void PaintLayer(Renderer r,string id,bool square,bool fill,float alpha=1)
    {
        if(r==null)return;
        var mat=PlateMaterial;if(r.sharedMaterial!=mat)r.sharedMaterial=mat;
        if(!plateBlocks.TryGetValue((id,square,fill),out var block))
        { block=new MaterialPropertyBlock();SetPlateProperties(block,id,square,fill);plateBlocks[(id,square,fill)]=block; }
        block.SetColor(TintId,new Color(1,1,1,Mathf.Clamp01(alpha)));
        r.SetPropertyBlock(block);
    }
    public static void PaintPlate(Renderer outer,Renderer fill,string id,bool square=false)
    {
        outer.sortingOrder=1;fill.sortingOrder=0;
        PaintLayer(outer,id,square,false);
        PaintLayer(fill,id,square,true);
    }
    public static void PaintActive(Renderer r,float alpha=1)=>PaintLayer(r,"16",true,false,alpha);
    // Attack decal outlines (01~05·08·10·11) are drawn by SG_EnemyC3Decal — no runtime texture.
    // DecalProjector has no property block, so values go into each projector's own material instance
    // (BossAttackConeTelegraph already makes one per projector). Size changes only rewrite these vectors.
    // The CPU reference for the pixel comparison test is the editor EnemyC3BakeReference.Decal().
    static Material decalMaterial;
    static readonly int ShapeId=Shader.PropertyToID("_C3Shape"),ExtentId=Shader.PropertyToID("_C3Extent"),
        PathId=Shader.PropertyToID("_C3Path"),FinsId=Shader.PropertyToID("_C3Fins");
    public static Material DecalMaterial
    {
        get
        {
            if(decalMaterial!=null)return decalMaterial;
            // Resources asset keeps the shader graph in builds.
            decalMaterial=Resources.Load<Material>("Telegraph/MA_EnemyC3Decal");
            if(decalMaterial==null){var shader=Shader.Find("Shader Graphs/SG_EnemyC3Decal");if(shader!=null)decalMaterial=new Material(shader){name="EnemyC3Decal"};}
            return decalMaterial;
        }
    }
    /// <summary>Decal outline values for SG_EnemyC3Decal — same arguments and result as EnemyC3BakeReference.Decal(). Colors stay gamma (SetVector).</summary>
    public static void SetDecalProperties(Material m,string id,Vector2 extent,float coneRadius,float coneCenter,float halfAngle,float pathWidth,float pathLength)
    {
        if(m==null)return;
        bool union=coneRadius>0&&pathLength>0,band=pathLength>0&&!union;
        float unit=DecalUnit(id,extent,coneRadius,pathWidth,pathLength);
        var e=Get(id).elements;var g=Get(id).geometry;
        bool show=(band||union)&&(union?g.upperShowMarkers:id=="04"||id=="05"||id=="11"?g.dashShowMarkers:g.showMarkers);
        Vector2 sample=extent+Vector2.one*(2*g.outerWidth*unit);
        m.SetVector(ShapeId,new Vector4(band?1:union?2:0,unit,halfAngle,show?1:0));
        m.SetVector(ExtentId,new Vector4(extent.x,extent.y,sample.x,sample.y));
        m.SetVector(PathId,new Vector4(band?extent.x:pathWidth,pathLength,coneCenter,coneRadius));
        m.SetVector(GeomId,new Vector4(g.outerWidth,g.innerWidth,g.gap,g.gradientStart));
        m.SetVector(FinsId,new Vector4(g.linearMarkerScalePercent,g.linearMarkerSpacing,g.linearMarkerStart,0));
        m.SetVector(OutlineId,e.outline.Value);m.SetVector(InnerLineId,e.innerLine.Value);
        m.SetVector(BandId,e.band.Value);m.SetVector(MarkersId,e.markers.Value);
    }
    // Attack decal fills (BossAttackConeTelegraph, S4a) — the same SG_EnemyC3Decal redraws the old 256² CPU fill textures
    // (BuildArcTexture / BuildRectTexture / BuildUnionTexture) in the shader. Progress changes only rewrite one vector.
    // S4c1: the same fill modes also draw the boss direction marker arcs (BossDirectionIndicator) and the AoeTelegraph disc.
    // The old CPU bakes live in the editor-only LegacyDecalTextureReference (pixel comparison baseline).
    /// <summary>Sector fill (outer = 1). inner 0 = full sector, 1 → 0 = grab ring closing in.
    /// isBack = the boss direction marker's rear arc (|angle| ≥ 180 − halfAngle), as the old BuildArcTexture(isBack).</summary>
    public static void SetArcFill(Material m,float halfAngle,float inner,bool isBack=false)
    {
        if(m==null)return;
        m.SetVector(ShapeId,new Vector4(3,isBack?1:0,halfAngle,Mathf.Clamp01(inner)));
    }
    /// <summary>AoeTelegraph disc (S4c1) — the old 128² BuildDiscTexture: fill alpha inside, rising to 1 across the rim band, feathered edge.</summary>
    public static void SetDiscFill(Material m,float fill,float rim)
    {
        if(m==null)return;
        m.SetVector(ShapeId,new Vector4(6,0,Mathf.Clamp01(fill),Mathf.Clamp(rim,0,.5f)));
    }
    static readonly int BaseColorId=Shader.PropertyToID("_BaseColor"),DrawOrderId=Shader.PropertyToID("_DrawOrder"),
        MaskTexId=Shader.PropertyToID("_MaskTex"),MaskParamsId=Shader.PropertyToID("_MaskParams");
    static readonly string[] DecalSettings={"Normal_Blend","_DecalAngleFadeSupported","_DecalMeshBiasType","_DecalMeshDepthBias","_DecalMeshViewBias"};
    /// <summary>
    /// A C3 decal material instance that replaces a texture-based floor decal material (<paramref name="source"/>, e.g.
    /// SG_DecalFloorOnly) without changing how it draws: render queue, _DrawOrder and the decal target settings
    /// (normal blend, angle fade, mesh bias) are copied from the source; the mask is off; _BaseColor = <paramref name="color"/>.
    /// The caller owns (and destroys) the instance. Null when the C3 decal material is missing.
    /// </summary>
    public static Material NewDecalInstance(Material source,Color color)
    {
        var shape=DecalMaterial;
        if(shape==null)return null;
        var m=new Material(shape){name=shape.name+" (Instance)"};
        if(source!=null)
        {
            m.renderQueue=source.renderQueue;
            if(source.HasProperty(DrawOrderId))m.SetFloat(DrawOrderId,source.GetFloat(DrawOrderId));
            foreach(var p in DecalSettings)if(source.HasProperty(p)&&m.HasProperty(p))m.SetFloat(p,source.GetFloat(p));
        }
        m.SetTexture(MaskTexId,Texture2D.whiteTexture);
        m.SetVector(MaskParamsId,new Vector4(0,0,1,0));
        m.SetColor(BaseColorId,color);
        return m;
    }
    /// <summary>Path band fill (whole rectangle, 1.5 texel edge feather).</summary>
    public static void SetRectFill(Material m)
    {
        if(m==null)return;
        m.SetVector(ShapeId,new Vector4(4,0,0,0));
    }
    /// <summary>Union (band + end circle) fill up to <paramref name="front"/> m from the boss. Negative front = whole shape.</summary>
    public static void SetUnionFill(Material m,Vector2 extent,float pathRadius,float pathLength,float coneCenter,float coneRadius,float front)
    {
        if(m==null)return;
        m.SetVector(ShapeId,new Vector4(5,0,0,front));
        m.SetVector(ExtentId,new Vector4(extent.x,extent.y,extent.x,extent.y));
        m.SetVector(PathId,new Vector4(pathRadius*2,pathLength,coneCenter,coneRadius));
    }
    // Smash 09 is a SpriteRenderer (FX_Mob_Smah_Warning/L_circle, FadeInHoldEffect drives sr.color alpha), so it needs a sprite:
    // EnemyC3BakeReference.Circle("09") pre-baked in the editor (Tools/Enemy C3/Bake smash 09 sprite) — no runtime Sprite.Create.
    // Its import PPU makes it cover the original sprite × OuterScale("09"), so no transform scaling is needed.
    public const string SmashSpritePath="Telegraph/EnemyC3_Smash09";
    static Sprite smashSprite;
    public static Sprite SmashSprite=>smashSprite!=null?smashSprite:smashSprite=Resources.Load<Sprite>(SmashSpritePath);
    public static void ApplySmash(GameObject go)
    {
        var sprite=SmashSprite;
        if(sprite==null){Debug.LogError("[Enemy C3] Missing smash sprite Resources/"+SmashSpritePath);return;}
        foreach(var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if(sr.sprite==null)continue;
            if(sr.sprite!=sprite)sr.sprite=sprite;
            sr.color=new Color(1,1,1,sr.color.a);
        }
    }
    static readonly Unity.Profiling.ProfilerMarker Diag57CreateJump=new Unity.Profiling.ProfilerMarker("Diag57.CreateJump");   // 할 일 57.2 진단 마커(QA 브랜치 전용)
    public static GameObject CreateJump(Vector3 point,float radius,float duration)
    {
        using var diag57=Diag57CreateJump.Auto();
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
        EnemyC3Visuals.PaintLayer(fill,"07",false,true);
        outer.transform.localPosition=Vector3.up*.07f;fill.transform.localPosition=Vector3.up*.08f;
        float outerDiameter=r*2*EnemyC3Visuals.OuterScale("06");outer.transform.localScale=new Vector3(outerDiameter,1,outerDiameter);outer.gameObject.SetActive(true);fill.gameObject.SetActive(true);UpdateScale();
    }
    void Update(){elapsed+=Time.deltaTime;UpdateScale();}
    void UpdateScale(){float r=Mathf.Max(.001f,radius*(duration<=0?1:Mathf.Clamp01(elapsed/duration)));fill.transform.localScale=new Vector3(r*2,1,r*2);}
}
