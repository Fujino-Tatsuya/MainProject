using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

// Authoring commands for the four approved hand-painted weapon basecolors.
public static class WeaponTextureIntegration
{
    const string Art = "Assets/50.Art/Char/Weapons/Handpainted_20261026";
    static string Reports => Path.GetFullPath(Path.Combine(Application.dataPath, "../Generated/WeaponTexturing"));
    const string Paladin = "Assets/2.Prefabs/Player/Paladin/Player_Paladin.prefab";
    const string Assassin = "Assets/2.Prefabs/Player/Assassin/Assassin_Armature.prefab";
    const string Materials = "Assets/3.Materials/FlatKit/Player";
    const string GlowDir = Materials + "/WeaponOverlays";
    const string GlowShader = "Assets/1.Scripts/Effects/Shaders/WeaponOriginalUv1.shader";
    [Serializable] class Check
    {
        public string weapon, mesh;
        public float maxPositionError, maxEffectUvError;
        public int triangleCount, matchedTriangles, oppositeTriangles;
    }
    [Serializable] class Applied { public List<Check> weapons = new(); public bool success; }
    [Serializable] class Verification
    {
        public List<string> passed=new(), failed=new();
        public int shaderErrors;
    }

    [MenuItem("Tools/Art/Weapon Textures/Render Material Check")]
    public static void RenderMaterialCheck()
    {
        var preview=new PreviewRenderUtility();
        try
        {
            preview.camera.orthographic=true;preview.camera.orthographicSize=.9f;
            preview.camera.transform.position=new Vector3(0,0,-5);preview.camera.transform.LookAt(Vector3.zero);
            preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.12f,.14f,.17f);
            preview.lights[0].intensity=.9f;preview.lights[0].transform.rotation=Quaternion.Euler(35,20,0);
            preview.lights[1].intensity=.35f;preview.lights[1].transform.rotation=Quaternion.Euler(330,210,0);
            preview.ambientColor=new Color(.4f,.4f,.4f,1);
            preview.BeginStaticPreview(new Rect(0,0,1600,1000));
            var paladin=AssetDatabase.LoadAssetAtPath<GameObject>(Paladin);
            var assassin=AssetDatabase.LoadAssetAtPath<GameObject>(Assassin);
            var filters=new[]{Find(paladin,"SM_Wep_Sword_03"),Find(paladin,"SM_Wep_Shield_01"),Find(assassin,"Degger_R"),Find(assassin,"Degger_L")};
            var xs=new[]{-.95f,-.32f,.45f,.95f};
            for(int i=0;i<filters.Length;i++)
            {
                var mesh=filters[i].sharedMesh;float scale=1.3f/mesh.bounds.size.y;
                var rotation=i==0?Quaternion.Euler(0,90,0):Quaternion.identity;
                var matrix=Matrix4x4.TRS(new Vector3(xs[i],0,0),rotation,Vector3.one*scale)*Matrix4x4.Translate(-mesh.bounds.center);
                preview.DrawMesh(mesh,matrix,filters[i].GetComponent<Renderer>().sharedMaterials[0],0);
            }
            preview.Render(true);
            var image=preview.EndStaticPreview();Directory.CreateDirectory(Reports);
            File.WriteAllBytes(Reports+"/unity-material-check.png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
            Debug.Log("[Weapon Textures] Unity FlatKit material preview saved.");
        }
        finally {preview.Cleanup();}
    }

    [MenuItem("Tools/Art/Weapon Textures/Validate Applied Weapons")]
    public static void ValidateApplied()
    {
        var results=new Verification();
        var tests=new AssassinVisualPrefabTests();
        foreach(var method in typeof(AssassinVisualPrefabTests).GetMethods())
        {
            var cases=method.GetCustomAttributes(typeof(NUnit.Framework.TestCaseAttribute),true)
                .Cast<NUnit.Framework.TestCaseAttribute>().Select(c=>c.Arguments).ToList();
            if(method.IsDefined(typeof(NUnit.Framework.TestAttribute),true))cases.Add(Array.Empty<object>());
            foreach(var arguments in cases)
            {
                string label=method.Name+"("+string.Join(",",arguments)+")";
                try {method.Invoke(tests,arguments);results.passed.Add(label);}
                catch(Exception e) {results.failed.Add(label+": "+(e.InnerException??e).Message);}
            }
        }
        foreach(var path in new[]{Paladin,"Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab"})
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach(var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if(!mf.sharedMesh || !AssetDatabase.GetAssetPath(mf.sharedMesh).StartsWith(Art+"/Models/"))continue;
                var materials=mf.GetComponent<Renderer>().sharedMaterials;
                string label=path+":"+mf.name;
                if(materials.Length==2 && materials[0].GetTexture("_BaseMap") && materials[0].GetColor("_BaseColor")==Color.white
                    && materials[1].shader==AssetDatabase.LoadAssetAtPath<Shader>(GlowShader) && mf.sharedMesh.uv2.Length==mf.sharedMesh.vertexCount)
                    results.passed.Add(label);
                else results.failed.Add(label+": basecolor/effect binding invalid");
            }
        }
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(GlowShader);
        var errors=ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToArray();
        results.shaderErrors=errors.Length;
        results.failed.AddRange(errors.Select(m=>m.message));
        Directory.CreateDirectory(Reports);File.WriteAllText(Reports+"/validation.json",JsonUtility.ToJson(results,true));
        Debug.Log("[Weapon Textures] Validation: "+results.passed.Count+" passed, "+results.failed.Count+" failed.");
    }

    static Mesh LoadMesh(string file) => AssetDatabase.LoadAllAssetsAtPath(Art+"/Models/"+file).OfType<Mesh>().Single();
    static MeshFilter Find(GameObject root,string name) => root.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name==name);
    static string VertexKey(Vector3 v) => $"{Mathf.RoundToInt(v.x*1000000)},{Mathf.RoundToInt(v.y*1000000)},{Mathf.RoundToInt(v.z*1000000)}";
    static string TriangleKey(Vector3 a,Vector3 b,Vector3 c) => string.Join(";",new[]{VertexKey(a),VertexKey(b),VertexKey(c)}.OrderBy(x=>x));

    static Check CheckMesh(string key,Mesh previous,Mesh next)
    {
        var result=new Check { weapon=key, mesh=AssetDatabase.GetAssetPath(next), triangleCount=next.triangles.Length/3 };
        if(previous==next) return result;
        var oldPositions=previous.vertices; var oldUv=previous.uv; var positions=next.vertices;var effectUv=next.uv2;
        if(effectUv.Length!=positions.Length) throw new InvalidOperationException(key+": missing preserved effect UV1.");
        for(int i=0;i<positions.Length;i++)
        {
            float best=float.PositiveInfinity,bestUv=float.PositiveInfinity;
            for(int j=0;j<oldPositions.Length;j++)
            {
                float distance=(positions[i]-oldPositions[j]).sqrMagnitude;
                if(distance<best) best=distance;
                if(distance<1e-10f) bestUv=Mathf.Min(bestUv,(effectUv[i]-oldUv[j]).magnitude);
            }
            result.maxPositionError=Mathf.Max(result.maxPositionError,Mathf.Sqrt(best));
            result.maxEffectUvError=Mathf.Max(result.maxEffectUvError,bestUv);
        }
        var normals=new Dictionary<string,List<Vector3>>();var oldTriangles=previous.triangles;
        for(int i=0;i<oldTriangles.Length;i+=3)
        {
            var a=oldPositions[oldTriangles[i]];var b=oldPositions[oldTriangles[i+1]];var c=oldPositions[oldTriangles[i+2]];
            string triangleName=TriangleKey(a,b,c);
            if(!normals.TryGetValue(triangleName,out var list))normals[triangleName]=list=new List<Vector3>();
            list.Add(Vector3.Cross(b-a,c-a).normalized);
        }
        var triangles=next.triangles;
        for(int i=0;i<triangles.Length;i+=3)
        {
            var a=positions[triangles[i]];var b=positions[triangles[i+1]];var c=positions[triangles[i+2]];
            if(normals.TryGetValue(TriangleKey(a,b,c),out var candidates))
            {
                result.matchedTriangles++;
                var normal=Vector3.Cross(b-a,c-a).normalized;
                if(candidates.Max(n=>Vector3.Dot(n,normal))<-.9f)result.oppositeTriangles++;
            }
        }
        if(result.maxPositionError>0.00001f || result.maxEffectUvError>.0001f || result.oppositeTriangles>0)
            throw new InvalidOperationException(key+": geometry/effect UV mismatch: "+JsonUtility.ToJson(result));
        return result;
    }

    static Material BaseMaterial(string path,string textureName)
    {
        var parent=AssetDatabase.LoadAssetAtPath<Material>(Materials+"/FK_Paladin_Toon.mat");
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material)
        {
            material=new Material(parent){ name=Path.GetFileNameWithoutExtension(path), parent=parent };
            AssetDatabase.CreateAsset(material,path);
        }
        material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"/Textures/"+textureName));
        material.SetColor("_BaseColor",Color.white);EditorUtility.SetDirty(material);
        return material;
    }

    static Material EffectMaterial(string originalName)
    {
        var path=GlowDir+"/"+originalName+"_UV1.mat";
        var source=AssetDatabase.LoadAssetAtPath<Material>("Assets/50.Art/VFX/_Materials/"+originalName+".mat");
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material){ material=new Material(source){name=originalName+"_UV1"};AssetDatabase.CreateAsset(material,path); }
        material.shader=AssetDatabase.LoadAssetAtPath<Shader>(GlowShader);EditorUtility.SetDirty(material);
        return material;
    }

    static void Assign(MeshFilter filter,Mesh mesh,Material material,Material glow)
    {
        filter.sharedMesh=mesh;
        var renderer=filter.GetComponent<MeshRenderer>();
        if(renderer.sharedMaterials.Length!=2)throw new InvalidOperationException(filter.name+": unexpected material slots.");
        renderer.sharedMaterials=new[]{material,glow};
        PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
    }

    [MenuItem("Tools/Art/Weapon Textures/Apply Handpainted Weapons")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play mode first.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var sword=LoadMesh("Paladin_Sword_Textured_v02.fbx");var shield=LoadMesh("Paladin_Shield_Textured_v02.fbx");
        var right=LoadMesh("Assassin_Dagger_R_Textured_v01.fbx");var left=LoadMesh("Assassin_Dagger_L_Textured_v01.fbx");
        var report=new Applied();
        var paladin=AssetDatabase.LoadAssetAtPath<GameObject>(Paladin);var assassin=AssetDatabase.LoadAssetAtPath<GameObject>(Assassin);
        report.weapons.Add(CheckMesh("PaladinSword",Find(paladin,"SM_Wep_Sword_03").sharedMesh,sword));
        report.weapons.Add(CheckMesh("PaladinShield",Find(paladin,"SM_Wep_Shield_01").sharedMesh,shield));
        report.weapons.Add(CheckMesh("DaggerR",Find(assassin,"Degger_R").sharedMesh,right));
        report.weapons.Add(CheckMesh("DaggerL",Find(assassin,"Degger_L").sharedMesh,left));
        Directory.CreateDirectory(Reports);
        File.WriteAllText(Reports+"/preflight.json",JsonUtility.ToJson(report,true));
        if(!AssetDatabase.IsValidFolder(GlowDir))AssetDatabase.CreateFolder(Materials,"WeaponOverlays");
        foreach(var guid in AssetDatabase.FindAssets("t:Texture2D",new[]{Art+"/Textures"}))
        {
            var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;importer.alphaSource=TextureImporterAlphaSource.None;
            importer.wrapMode=TextureWrapMode.Clamp;importer.mipmapEnabled=true;importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=4;
            importer.maxTextureSize=2048;importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();
        }
        var swordMat=BaseMaterial(Materials+"/FK_Paladin_Sword_Toon.mat","Paladin_Sword_BaseColor.png");
        var shieldMat=BaseMaterial(Materials+"/FK_Paladin_Shield_Toon.mat","Paladin_Shield_BaseColor.png");
        var rightMat=BaseMaterial(Materials+"/Assassin/FK_Assassin_Dagger_R.mat","Assassin_Dagger_R_BaseColor.png");
        var leftMat=BaseMaterial(Materials+"/Assassin/FK_Assassin_Dagger_L.mat","Assassin_Dagger_L_BaseColor.png");
        var swordGlow=EffectMaterial("Sword_Blade_Glow");var shieldGlow=EffectMaterial("Shield_Interrupt_Glow");var daggerGlow=EffectMaterial("Dagger_Blade_Glow");
        var root=PrefabUtility.LoadPrefabContents(Paladin);
        try { Assign(Find(root,"SM_Wep_Sword_03"),sword,swordMat,swordGlow);Assign(Find(root,"SM_Wep_Shield_01"),shield,shieldMat,shieldGlow);PrefabUtility.SaveAsPrefabAsset(root,Paladin); }
        finally {PrefabUtility.UnloadPrefabContents(root);}
        root=PrefabUtility.LoadPrefabContents(Assassin);
        try { Assign(Find(root,"Degger_R"),right,rightMat,daggerGlow);Assign(Find(root,"Degger_L"),left,leftMat,daggerGlow);PrefabUtility.SaveAsPrefabAsset(root,Assassin); }
        finally {PrefabUtility.UnloadPrefabContents(root);}
        AssetDatabase.SaveAssets();report.success=true;
        File.WriteAllText(Reports+"/applied.json",JsonUtility.ToJson(report,true));
        Debug.Log("[Weapon Textures] Applied sword/shield v02 and dagger R/L v01; sockets and original VFX materials preserved.");
    }

    [Serializable] class MeshInfo
    {
        public string name, path, renderer, meshPath;
        public int vertices, triangles, uv0, uv1, submeshes;
        public Vector3 boundsCenter, boundsSize, localScale, localPosition;
        public Matrix4x4 meshToRoot;
        public string[] materials;
    }
    [Serializable] class Inspection { public List<MeshInfo> meshes = new(); }

    static MeshInfo Info(Transform root, MeshFilter filter)
    {
        var mesh = filter.sharedMesh;
        var renderer = filter.GetComponent<Renderer>();
        return new MeshInfo {
            name = filter.name, path = AnimationUtility.CalculateTransformPath(filter.transform, root),
            renderer = renderer ? renderer.GetType().Name : "none", meshPath = AssetDatabase.GetAssetPath(mesh),
            vertices = mesh.vertexCount, triangles = mesh.triangles.Length/3, uv0 = mesh.uv.Length,
            uv1 = mesh.uv2.Length, submeshes = mesh.subMeshCount,
            boundsCenter = mesh.bounds.center, boundsSize = mesh.bounds.size,
            localScale = filter.transform.localScale, localPosition = filter.transform.localPosition,
            meshToRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix,
            materials = renderer ? renderer.sharedMaterials.Select(m => m ? AssetDatabase.GetAssetPath(m) : "null").ToArray() : Array.Empty<string>()
        };
    }

    [MenuItem("Tools/Art/Weapon Textures/Inspect Integration")]
    public static void Inspect()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Directory.CreateDirectory(Reports);
        var report = new Inspection();
        foreach (var path in new[] { Paladin, Assassin })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh && (mf.name.Contains("Wep_") || mf.name.Contains("Degger") || mf.name.Contains("Dagger")))
                    report.meshes.Add(Info(prefab.transform, mf));
        }
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[]{Art+"/Models"}))
        {
            var path=AssetDatabase.GUIDToAssetPath(guid);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                if(mf.sharedMesh) report.meshes.Add(Info(prefab.transform,mf));
        }
        File.WriteAllText(Reports+"/inspection.json", JsonUtility.ToJson(report,true));
        Debug.Log("[Weapon Textures] Inspection saved: "+report.meshes.Count+" mesh filters.");
    }
}
