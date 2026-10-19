using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEditor.TestTools.TestRunner.Api;

public static class EnemyC3Validation
{
    static readonly string Output=Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/EnemyC3Validation"));
    [MenuItem("Tools/Enemy C3/Validate selected preset")]
    public static void Validate()
    {
        Directory.CreateDirectory(Output);
        foreach(string id in new[]{"01","02","03","04","05","06","07","08","09","10","11","12","14","15","16"})
            if(EnemyC3Visuals.Get(id).id!=id)throw new Exception("Missing C3 mapping: "+id);
        if(EnemyC3Visuals.Get("01").geometry.linearMarkerSpacing!=67||EnemyC3Visuals.Get("10").geometry.linearMarkerSpacing!=78||EnemyC3Visuals.Get("09").geometry.markerRadiusPercent!=81)
            throw new Exception("Selected per-attack overrides were lost.");
        var grab=EnemyC3Visuals.Decal("03",new Vector2(16,16),8,0,45,0,0);
        float grabPad=EnemyC3Visuals.DecalPadding("03",new Vector2(16,16),8,0,0);
        float sampleExtent=16+grabPad*2;
        for(int degrees=-180;degrees<=180;degrees+=15)
        {
            float angle=degrees*Mathf.Deg2Rad;
            float radius=8+grabPad*.5f;
            int px=Mathf.RoundToInt((Mathf.Sin(angle)*radius/sampleExtent+.5f)*(grab.width-1));
            int py=Mathf.RoundToInt((Mathf.Cos(angle)*radius/sampleExtent+.5f)*(grab.height-1));
            float alpha=grab.GetPixel(px,py).a;
            if(Mathf.Abs(degrees)>=60&&alpha>.01f)throw new Exception("Magnetic grab contains a phantom full-circle border.");
            if(Mathf.Abs(degrees)<=30&&alpha<.5f)throw new Exception("Magnetic grab sector arc was removed.");
        }
        File.WriteAllBytes(Output+"/magnetic-grab-sector-texture.png",grab.EncodeToPNG());
        var circle=EnemyC3Visuals.Circle("12");
        if(circle.GetPixel(0,0).a>.01f)throw new Exception("Circle leaks beyond radius.");
        if(circle.GetPixel(192,192).a>.01f)throw new Exception("Boundary layer fills center prematurely.");
        var fill=EnemyC3Visuals.Circle("12",true);
        if(Mathf.Abs(fill.GetPixel(192,192).a-.232f)>.01f)throw new Exception("Fill alpha mismatch.");
        var upper=EnemyC3Visuals.Decal("02",new Vector2(250,405),125,280,180,130,175);
        var dash=EnemyC3Visuals.Decal("04",new Vector2(114,370),0,0,0,114,370);
        var smash=EnemyC3Visuals.Circle("09");
        var longerDash=EnemyC3Visuals.Decal("04",new Vector2(114,500),0,0,0,114,500);
        float pinY=EnemyC3Visuals.Get("04").geometry.linearMarkerStart-5;
        float SamplePin(Texture2D texture,float length)
        {
            float pad=EnemyC3Visuals.DecalPadding("04",new Vector2(114,length),0,114,length);
            int px=Mathf.RoundToInt((15+57+pad)/(114+pad*2)*(texture.width-1));
            int py=Mathf.RoundToInt((pinY+pad)/(length+pad*2)*(texture.height-1));
            return texture.GetPixel(px,py).a;
        }
        if(SamplePin(dash,370)<.35f||SamplePin(longerDash,500)<.35f)
            throw new Exception("Changing path length stretched or shifted its first tapered fin.");
        int smashPinX=Mathf.RoundToInt((165+165*.81f-12+3.5f)/337*(smash.width-1));
        if(smash.GetPixel(smashPinX,192).a<.45f)throw new Exception("Smash circular fins are missing.");
        File.WriteAllBytes(Output+"/circle-texture.png",circle.EncodeToPNG());
        File.WriteAllBytes(Output+"/upper-texture.png",upper.EncodeToPNG());
        File.WriteAllBytes(Output+"/dash-texture.png",dash.EncodeToPNG());
        File.WriteAllBytes(Output+"/smash-texture.png",smash.EncodeToPNG());
        // The upper circle, beyond the straight path, has no marker alpha.
        // At (0,280) the circle center is empty in its boundary-only layer.
        int upperCenterY=Mathf.RoundToInt(280f/405*383);
        if(upper.GetPixel(192,upperCenterY).a>.01f)throw new Exception("Upper fins leak into its end circle.");
        var owner=new GameObject("C3ValidationOwner");
        var telegraph=owner.AddComponent<BossAttackConeTelegraph>();
        telegraph.Show(0,0,0,0,1,5,.7f);
        var decals=owner.GetComponentsInChildren<UnityEngine.Rendering.Universal.DecalProjector>(true);
        int enabled=0;
        foreach(var decal in decals)if(decal.enabled){enabled++;if(decal.material==null||decal.material.shader==null||!decal.material.shader.isSupported)throw new Exception("Unsupported decal shader.");}
        if(enabled!=2)throw new Exception("Path should have exactly outline and fill layers.");
        telegraph.Hide();foreach(var decal in decals)if(decal.enabled)throw new Exception("Telegraph Hide left an active projector.");
        UnityEngine.Object.DestroyImmediate(owner);
        string result="magnetic grab sector-only border OK; 15 preset mappings OK; 3 individual overrides OK; circle bounds/alpha OK; upper straight-only pins OK; dash fin world position unchanged across two lengths; smash circular pins OK; decal material/Show/Hide OK.";
        File.WriteAllText(Output+"/validation.txt",result);Debug.Log("[Enemy C3] "+result);
    }
    [MenuItem("Tools/Enemy C3/Run existing telegraph tests")]
    public static void RunTests()
    {
        Directory.CreateDirectory(Output);
        var api=ScriptableObject.CreateInstance<TestRunnerApi>();api.RegisterCallbacks(new Results());
        api.Execute(new ExecutionSettings(new Filter{testMode=TestMode.EditMode,testNames=new[]{"TelegraphStyleTests","MonsterSkyStrikeTests","BossElectricFloorPatternTests"}}));
    }
    class Results:ICallbacks
    {
        public void RunStarted(ITestAdaptor tests){}
        public void TestStarted(ITestAdaptor test){}
        public void TestFinished(ITestResultAdaptor result){}
        public void RunFinished(ITestResultAdaptor result){File.WriteAllText(Output+"/editmode-results.txt",$"Status: {result.TestStatus}; passed: {result.PassCount}; failed: {result.FailCount}; skipped: {result.SkipCount}\n{result.Message}");}
    }
}
