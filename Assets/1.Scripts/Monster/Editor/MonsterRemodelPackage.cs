using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Netcode;

public static class MonsterRemodelPackage
{
    static readonly string[] Ids={"MortarBot","HumanoidBot","ChompBot","PeekABot","TeslaBot","GauntletBot","SpinnerBot","WallBot"};
    const string Root="Assets/50.Art/Char/Monster/Redesign";
    internal static bool IsReviewPrefabPath(string path) => Ids.Any(id=>
        path==$"{Root}/{id}/{id}_DieselReview.prefab" ||
        path==$"{Root}/SurfaceV1/{id}/{id}_SurfaceReview.prefab");

    [MenuItem("Tools/Monster Remodel/Exclude Review Prefabs From Network List")]
    public static void ExcludeReviewPrefabsFromNetworkList()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        var list=AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
        if(!list)return;
        var reviewEntries=list.PrefabList.Where(entry=>entry.Prefab&&IsReviewPrefabPath(AssetDatabase.GetAssetPath(entry.Prefab))).ToArray();
        if(reviewEntries.Length==0)return;
        foreach(var entry in reviewEntries)list.Remove(entry);
        EditorUtility.SetDirty(list);
        AssetDatabase.SaveAssetIfDirty(list);
    }
    [MenuItem("Tools/Monster Remodel/Import Review Materials")]
    public static void ImportMaterials()
    {
        foreach (var id in Ids)
        {
            string source=$"Generated/monster-remodel-20260926/{id}/textures";
            if (!File.Exists(source+"/BaseColor.png")) continue;
            string folder=$"{Root}/{id}/textures";
            Directory.CreateDirectory(folder);
            foreach (var name in new[]{"BaseColor","MetallicSmoothness"})
            {
                string path=$"{folder}/{name}.png";
                File.Copy($"{source}/{name}.png",path,true);
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var ti=(TextureImporter)AssetImporter.GetAtPath(path);
                ti.textureType=TextureImporterType.Default;
                ti.sRGBTexture=name=="BaseColor";
                ti.alphaSource=TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency=false;
                ti.mipmapEnabled=true;
                ti.maxTextureSize=1024;
                ti.wrapMode=TextureWrapMode.Repeat;
                ti.textureCompression=TextureImporterCompression.Compressed;
                ti.SaveAndReimport();
            }
            string matPath=$"{Root}/{id}/{id}_Diesel.mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,matPath);}
            mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/BaseColor.png"));
            mat.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/MetallicSmoothness.png"));
            mat.SetColor("_BaseColor",Color.white);
            mat.SetFloat("_Metallic",1f);mat.SetFloat("_Smoothness",1f);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");mat.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            mat.enableInstancing=true;
            EditorUtility.SetDirty(mat);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Monster review materials imported: 2K authoring maps retained, 1K Unity review import. Original prefabs unchanged.");
    }

    [MenuItem("Tools/Monster Remodel/Build Review Prefabs")]
    public static void BuildReviewPrefabs()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("Build review prefabs in edit mode after compilation.");
        foreach(var id in Ids)
        {
            string reportPath=$"Generated/monster-remodel-20260926/{id}/unity-validation/candidate-report.json";
            if(!File.Exists(reportPath))continue;
            var report=JsonUtility.FromJson<Gate>(File.ReadAllText(reportPath));
            if(!string.IsNullOrEmpty(report.error)||!(report.sourceCoordinatesPassed||report.coordinatesPassed)||!(report.sourceAnimationsPassed||report.sourceAnimationSamplesPassed)||!(report.candidateSamplingCompleted||report.candidateSamplesPassed)||report.originalPrefabChanged||report.productionPrefabChanged)
                throw new InvalidOperationException("Candidate validation gate failed: "+id);
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>($"{Root}/{id}/{id}_DieselPreview.asset");
            var mat=AssetDatabase.LoadAssetAtPath<Material>($"{Root}/{id}/{id}_Diesel.mat");
            if(!mesh||!mat)continue;
            string source=$"Assets/2.Prefabs/Monster/{id}.prefab";
            GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(source);
            if(!prefab)continue;
            var previewScene=EditorSceneManager.NewPreviewScene();
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,previewScene);
            try
            {
                instance.name=id+"_DieselReview";
                var candidates=instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var skin=id=="GauntletBot"?candidates.First(s=>s.sharedMesh&&s.sharedMesh.name=="CombatBot"):
                    id=="SpinnerBot"?candidates.First(s=>s.sharedMesh&&s.sharedMesh.vertexCount>4):
                    candidates.First(s=>s.sharedMesh&&s.sharedMesh.vertexCount>100);
                skin.sharedMesh=mesh;skin.localBounds=mesh.bounds;
                var mats=skin.sharedMaterials;mats[0]=mat;skin.sharedMaterials=mats;
                if(id=="GauntletBot")
                {
                    if(report.staticMeshes==null||report.staticMeshes.Length!=2||report.staticMeshes.Any(s=>!s.coordinatesPassed||!s.animationSamplesPassed))
                        throw new InvalidOperationException("Both Gauntlet static meshes must pass verification.");
                    foreach(var record in report.staticMeshes)
                    {
                        var target=instance.transform.Find(record.path);
                        if(!target)throw new InvalidOperationException("Missing verified static path: "+record.path);
                        var staticMesh=AssetDatabase.LoadAssetAtPath<Mesh>(record.meshAssetPath);
                        if(!staticMesh||!target.GetComponent<MeshFilter>()||!target.GetComponent<MeshRenderer>())throw new InvalidOperationException("Invalid verified static mesh: "+record.meshAssetPath);
                        target.GetComponent<MeshFilter>().sharedMesh=staticMesh;
                        var renderer=target.GetComponent<MeshRenderer>();var staticMats=renderer.sharedMaterials;staticMats[0]=mat;renderer.sharedMaterials=staticMats;
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(instance,$"{Root}/{id}/{id}_DieselReview.prefab");
            }
            finally{UnityEngine.Object.DestroyImmediate(instance);EditorSceneManager.ClosePreviewScene(previewScene);}
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Review prefab variants created from proven meshes. Production monster prefabs unchanged.");
        ExcludeReviewPrefabsFromNetworkList();
    }
    [MenuItem("Tools/Monster Remodel/Verify Review Prefabs")]
    public static void VerifyReviewPrefabs()
    {
        var results=new List<PackageCheck>();
        foreach(var id in Ids)
        {
            var check=new PackageCheck {monster=id,reviewPath=$"{Root}/{id}/{id}_DieselReview.prefab"};results.Add(check);
            try
            {
                var source=AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/2.Prefabs/Monster/{id}.prefab");
                var review=AssetDatabase.LoadAssetAtPath<GameObject>(check.reviewPath);
                if(!source||!review)throw new InvalidOperationException("Missing source or review prefab.");
                check.isVariant=PrefabUtility.GetPrefabAssetType(review)==PrefabAssetType.Variant;
                check.basePrefabPath=AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(review));
                check.directOriginalBase=check.basePrefabPath==AssetDatabase.GetAssetPath(source);
                var report=JsonUtility.FromJson<Gate>(File.ReadAllText($"Generated/monster-remodel-20260926/{id}/unity-validation/candidate-report.json"));
                var oldBody=source.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(s=>s.bones.Length).ThenByDescending(s=>s.sharedMesh?s.sharedMesh.vertexCount:0).First();
                string bodyPath=AnimationUtility.CalculateTransformPath(oldBody.transform,source.transform);
                var body=review.transform.Find(bodyPath).GetComponent<SkinnedMeshRenderer>();
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>($"{Root}/{id}/{id}_DieselPreview.asset");
                var material=AssetDatabase.LoadAssetAtPath<Material>($"{Root}/{id}/{id}_Diesel.mat");
                check.bodyMeshAndMaterial=body.sharedMesh==mesh&&body.sharedMaterial==material;
                check.animationBounds=body.localBounds==mesh.bounds;
                check.bindposes=oldBody.sharedMesh.bindposes.SequenceEqual(mesh.bindposes);
                check.bones=oldBody.bones.Select(t=>AnimationUtility.CalculateTransformPath(t,source.transform)).SequenceEqual(body.bones.Select(t=>AnimationUtility.CalculateTransformPath(t,review.transform)))&&AnimationUtility.CalculateTransformPath(oldBody.rootBone,source.transform)==AnimationUtility.CalculateTransformPath(body.rootBone,review.transform);
                check.extraMaterialSlots=oldBody.sharedMaterials.Length==body.sharedMaterials.Length&&oldBody.sharedMaterials.Skip(1).SequenceEqual(body.sharedMaterials.Skip(1))&&oldBody.sharedMesh.subMeshCount==mesh.subMeshCount;
                var oldTransforms=source.GetComponentsInChildren<Transform>(true);var newTransforms=review.GetComponentsInChildren<Transform>(true);
                check.hierarchyAndSockets=oldTransforms.Length==newTransforms.Length&&oldTransforms.Select((t,i)=>AnimationUtility.CalculateTransformPath(t,source.transform)==AnimationUtility.CalculateTransformPath(newTransforms[i],review.transform)&&t.localPosition==newTransforms[i].localPosition&&t.localRotation==newTransforms[i].localRotation&&t.localScale==newTransforms[i].localScale).All(v=>v);
                check.animatorAndAvatar=source.GetComponentsInChildren<Animator>(true).Select(a=>new {path=AnimationUtility.CalculateTransformPath(a.transform,source.transform),a.avatar,a.runtimeAnimatorController}).SequenceEqual(review.GetComponentsInChildren<Animator>(true).Select(a=>new {path=AnimationUtility.CalculateTransformPath(a.transform,review.transform),a.avatar,a.runtimeAnimatorController}));
                check.otherRenderers=true;check.staticGauntlets=true;
                foreach(var oldRenderer in source.GetComponentsInChildren<Renderer>(true).Where(r=>r!=oldBody))
                {
                    string path=AnimationUtility.CalculateTransformPath(oldRenderer.transform,source.transform);
                    var renderer=review.transform.Find(path).GetComponent<Renderer>();
                    var staticRecord=report.staticMeshes?.FirstOrDefault(s=>s.path==path);
                    if(staticRecord!=null)
                    {
                        check.staticGauntlets&=staticRecord.coordinatesPassed&&staticRecord.animationSamplesPassed&&renderer.GetComponent<MeshFilter>().sharedMesh==AssetDatabase.LoadAssetAtPath<Mesh>(staticRecord.meshAssetPath)&&renderer.sharedMaterial==material&&oldRenderer.sharedMaterials.Skip(1).SequenceEqual(renderer.sharedMaterials.Skip(1));
                    }
                    else
                    {
                        check.otherRenderers&=renderer&&renderer.GetType()==oldRenderer.GetType()&&renderer.sharedMaterials.SequenceEqual(oldRenderer.sharedMaterials)&&renderer.enabled==oldRenderer.enabled;
                        if(oldRenderer is SkinnedMeshRenderer oldSkin)check.otherRenderers&=((SkinnedMeshRenderer)renderer).sharedMesh==oldSkin.sharedMesh;
                        var oldFilter=oldRenderer.GetComponent<MeshFilter>();if(oldFilter)check.otherRenderers&=renderer.GetComponent<MeshFilter>().sharedMesh==oldFilter.sharedMesh;
                    }
                }
                if(id=="GauntletBot")check.staticGauntlets&=report.staticMeshes?.Length==2;
                check.passed=check.isVariant&&check.directOriginalBase&&check.bodyMeshAndMaterial&&check.animationBounds&&check.bindposes&&check.bones&&check.extraMaterialSlots&&check.hierarchyAndSockets&&check.animatorAndAvatar&&check.otherRenderers&&check.staticGauntlets;
            }
            catch(Exception error){check.error=error.ToString();}
        }
        var summary=new PackageSummary {capturedAtUtc=DateTime.UtcNow.ToString("O"),packages=results.ToArray(),passed=results.Count==Ids.Length&&results.All(r=>r.passed)};
        File.WriteAllText("Generated/monster-remodel-20260926/package-verification.json",JsonUtility.ToJson(summary,true));
        if(!summary.passed)throw new InvalidOperationException("Review prefab verification failed; inspect package-verification.json.");
        Debug.Log("All eight review variants preserve source rig, controllers, sockets and unaffected renderers.");
    }
    [Serializable] sealed class PackageSummary {public string capturedAtUtc;public bool passed;public string scope="Saved review prefab asset structure only; gameplay and multiplayer were not run.";public PackageCheck[] packages;}
    [Serializable] sealed class PackageCheck {public string monster,reviewPath,basePrefabPath,error;public bool isVariant,directOriginalBase,bodyMeshAndMaterial,animationBounds,bindposes,bones,extraMaterialSlots,hierarchyAndSockets,animatorAndAvatar,otherRenderers,staticGauntlets,passed;}
    [Serializable] sealed class Gate {public string error;public bool sourceCoordinatesPassed,sourceAnimationsPassed,coordinatesPassed,sourceAnimationSamplesPassed,candidateSamplingCompleted,candidateSamplesPassed,originalPrefabChanged,productionPrefabChanged;public StaticGate[] staticMeshes;}
    [Serializable] sealed class StaticGate {public string path,meshAssetPath;public bool coordinatesPassed,animationSamplesPassed;}
}

// NGO automatically registers every imported NetworkObject prefab. Keep these
// eight authoring variants out of the game list, including after reimports.
sealed class MonsterRemodelReviewImportGuard : AssetPostprocessor
{
    static bool queued;
    static void OnPostprocessAllAssets(string[] importedAssets,string[] deletedAssets,string[] movedAssets,string[] movedFromAssetPaths)
    {
        if(queued||!importedAssets.Concat(movedAssets).Any(MonsterRemodelPackage.IsReviewPrefabPath))return;
        queued=true;
        EditorApplication.delayCall+=()=>
        {
            queued=false;
            MonsterRemodelPackage.ExcludeReviewPrefabsFromNetworkList();
        };
    }
}

