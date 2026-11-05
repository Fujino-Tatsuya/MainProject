using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 간파 연출 단계별 강화(할 일 219) 저작 메뉴 — 1단계(일반) V3 SO·블룸 프로필·프리팹을 2단계(대쉬)·3단계(제압)용으로 <b>복제</b>하고
/// PLAN 219 §3 배율(1단계 대비)을 절대값으로 넣은 뒤 NetworkManager.prefab 디렉터의 빈 단계 칸에 잇는다.
/// <list type="bullet">
/// <item><b>없을 때만 만든다</b> — 이미 있는 단계 에셋(손으로 튜닝한 값)은 건드리지 않는다. 디렉터 칸도 비어 있을 때만 잇는다.</item>
/// <item>1단계 V3 에셋(<see cref="InterruptHitLightReferenceAuthoring"/>)은 읽기만 한다. 재질·메시·셰이더·색 보정 프로필은 공유.</item>
/// </list>
/// 처음 에셋(브랜치 feature/InterruptTierScaling)은 같은 배율로 손으로 복제해 넣었다 — 이 메뉴는 지웠을 때 다시 만드는 용도다.
/// 문서: Docs/tech/interrupt-presentation.md §9.9.
/// </summary>
internal static class InterruptHitLightTierAuthoring
{
    public const string DashFolder = InterruptHitLightReferenceAuthoring.Folder + "/Dash";
    public const string SuppressFolder = InterruptHitLightReferenceAuthoring.Folder + "/Suppress";
    public const string DashLookPath = DashFolder + "/InterruptHitLightLook_V3_Dash.asset";
    public const string DashPrefabPath = DashFolder + "/FX_InterruptHitLight_V3_Dash.prefab";
    public const string DashBloomPath = DashFolder + "/VP_InterruptHitLight_V3_Dash_Bloom.asset";
    public const string DashGradePath = DashFolder + "/VP_InterruptHitLight_V3_Dash_Grade.asset";
    public const string SuppressLookPath = SuppressFolder + "/InterruptHitLightLook_V3_Suppress.asset";
    public const string SuppressPrefabPath = SuppressFolder + "/FX_InterruptHitLight_V3_Suppress.prefab";
    public const string SuppressBloomPath = SuppressFolder + "/VP_InterruptHitLight_V3_Suppress_Bloom.asset";
    public const string SuppressGradePath = SuppressFolder + "/VP_InterruptHitLight_V3_Suppress_Grade.asset";

    /// <summary>단계 배율 묶음(1단계 대비) — PLAN 219 §3 표.</summary>
    public readonly struct Scaling
    {
        public readonly float Sparks, Light, Bloom, DarkDuration, Streak, Rays, Core;
        public readonly int WhiteoutFrames;

        public Scaling(float sparks, float light, float bloom, float darkDuration, float streak, float rays,
            float core, int whiteoutFrames)
        {
            Sparks = sparks;
            Light = light;
            Bloom = bloom;
            DarkDuration = darkDuration;
            Streak = streak;
            Rays = rays;
            Core = core;
            WhiteoutFrames = whiteoutFrames;
        }
    }

    public static readonly Scaling Dash = new Scaling(1.6f, 1.5f, 1.4f, 1.3f, 1.5f, 1.5f, 1f, 0);
    public static readonly Scaling Suppress = new Scaling(2.5f, 2.5f, 2.5f, 2.5f, 2.5f, 2.5f, 2.5f, 3);

    [MenuItem("Tools/Interrupt/간파 단계별 V3 강화 에셋 만들기 (219)")]
    public static void BuildAll()
    {
        var look = AssetDatabase.LoadAssetAtPath<InterruptHitLightLook>(InterruptHitLightReferenceAuthoring.LookPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(InterruptHitLightReferenceAuthoring.PrefabPath);
        if (look == null || prefab == null)
        {
            Debug.LogError("[InterruptHitLight] 1단계 V3 에셋이 없다 — 먼저 'Tools/Interrupt/적중 라이트 V3 레퍼런스 에셋 만들기 (194)'");
            return;
        }

        Build(DashFolder, DashBloomPath, DashGradePath, DashLookPath, DashPrefabPath, Dash);
        Build(SuppressFolder, SuppressBloomPath, SuppressGradePath, SuppressLookPath, SuppressPrefabPath, Suppress);
        WireDirector();
        AssetDatabase.SaveAssets();
        Debug.Log("[InterruptHitLight] 219 단계별 V3 에셋 확인 완료 — 없던 것만 만들고 빈 디렉터 칸만 이었다");
    }

    static void Build(string folder, string bloomPath, string gradePath, string lookPath, string prefabPath, Scaling scaling)
    {
        Directory.CreateDirectory(folder);
        AssetDatabase.Refresh();

        if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(bloomPath) == null &&
            AssetDatabase.CopyAsset(InterruptHitLightReferenceAuthoring.BloomProfilePath, bloomPath))
        {
            var bloom = AssetDatabase.LoadAssetAtPath<VolumeProfile>(bloomPath);
            if (bloom != null && bloom.TryGet(out Bloom component))
            {
                component.intensity.value *= scaling.Bloom;
                EditorUtility.SetDirty(bloom);
            }
        }

        // 색 보정(219 후속 11-05) — 일반 V3 프로필을 그대로 복사(시작값 = 일반, 동작 변화 없음). 있으면 손대지 않는다.
        if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(gradePath) == null)
            AssetDatabase.CopyAsset(InterruptHitLightReferenceAuthoring.GradeProfilePath, gradePath);
        var grade = AssetDatabase.LoadAssetAtPath<VolumeProfile>(gradePath);

        if (AssetDatabase.LoadAssetAtPath<InterruptHitLightLook>(lookPath) == null &&
            AssetDatabase.CopyAsset(InterruptHitLightReferenceAuthoring.LookPath, lookPath))
        {
            var look = AssetDatabase.LoadAssetAtPath<InterruptHitLightLook>(lookPath);
            var so = new SerializedObject(look);
            Scale(so, "lightIntensity", scaling.Light);
            so.FindProperty("sunDimDepth").floatValue = 1f;     // 암순응 나 깊이 = 최대(2·3단계)
            so.FindProperty("ambientDimDepth").floatValue = 1f;
            Scale(so, "sceneDimDuration", scaling.DarkDuration);
            Scale(so, "darkAdaptDuration", scaling.DarkDuration);
            SerializedProperty rays = so.FindProperty("gapRayCount");
            rays.intValue = Mathf.Clamp(Mathf.RoundToInt(rays.intValue * scaling.Rays), 1, 16);
            Scale(so, "rayScreenBrightness", scaling.Rays);
            so.FindProperty("whiteoutFrames").intValue = scaling.WhiteoutFrames;
            so.FindProperty("bloomProfile").objectReferenceValue = AssetDatabase.LoadAssetAtPath<VolumeProfile>(bloomPath);
            so.FindProperty("gradeProfile").objectReferenceValue = grade;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        else
        {
            // 219 때 만든 단계 SO 는 일반 색 보정을 같이 가리킨다 — 그 경우만 단계 프로필로 바꾼다(다른 프로필로 손 튜닝한 칸은 그대로).
            var existing = AssetDatabase.LoadAssetAtPath<InterruptHitLightLook>(lookPath);
            var shared = AssetDatabase.LoadAssetAtPath<VolumeProfile>(InterruptHitLightReferenceAuthoring.GradeProfilePath);
            if (existing != null && grade != null && existing.GradeProfile == shared)
            {
                var so = new SerializedObject(existing);
                so.FindProperty("gradeProfile").objectReferenceValue = grade;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null &&
            AssetDatabase.CopyAsset(InterruptHitLightReferenceAuthoring.PrefabPath, prefabPath))
        {
            using (var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
            {
                foreach (ParticleSystem system in scope.prefabContentsRoot.GetComponentsInChildren<ParticleSystem>(true))
                    ScaleParticles(system, scaling);
            }
        }
    }

    static void Scale(SerializedObject so, string field, float factor)
    {
        SerializedProperty property = so.FindProperty(field);
        property.floatValue *= factor;
    }

    // 자식 이름(Rays·Sparks·Streak·Core — 파편 Debris 는 11-05 삭제)마다 PLAN §3 칸만 배율.
    static void ScaleParticles(ParticleSystem system, Scaling scaling)
    {
        ParticleSystem.MainModule main = system.main;
        switch (system.gameObject.name)
        {
            case "Rays":
                ScaleBurst(system, scaling.Rays);
                main.maxParticles = Mathf.Max(main.maxParticles, 16); // 화면 빛줄기 줄 수(gapRayCount)보다 넉넉히
                break;
            case "Sparks":
                ScaleBurst(system, scaling.Sparks);
                main.maxParticles = Mathf.CeilToInt(main.maxParticles * scaling.Sparks / 8f) * 8;
                break;
            case "Streak":
                main.startSize = ScaleCurve(main.startSize, scaling.Streak);
                break;
            case "Core":
                main.startSize = ScaleCurve(main.startSize, scaling.Core);
                break;
        }
    }

    static void ScaleBurst(ParticleSystem system, float factor)
    {
        ParticleSystem.EmissionModule emission = system.emission;
        for (int i = 0; i < emission.burstCount; i++)
        {
            ParticleSystem.Burst burst = emission.GetBurst(i);
            ParticleSystem.MinMaxCurve count = burst.count;
            count.constant = Mathf.Round(count.constant * factor);
            count.constantMin = Mathf.Round(count.constantMin * factor);
            count.constantMax = Mathf.Round(count.constantMax * factor);
            burst.count = count;
            emission.SetBurst(i, burst);
        }
    }

    static ParticleSystem.MinMaxCurve ScaleCurve(ParticleSystem.MinMaxCurve curve, float factor)
    {
        curve.constant *= factor;
        curve.constantMin *= factor;
        curve.constantMax *= factor;
        curve.curveMultiplier *= factor;
        return curve;
    }

    // 빈 칸만 잇는다(사람이 비운 칸·다른 에셋을 넣은 칸은 비어 있지 않으면 그대로).
    static void WireDirector()
    {
        using (var scope = new PrefabUtility.EditPrefabContentsScope(InterruptHitLightAuthoring.NetworkManagerPrefabPath))
        {
            var director = scope.prefabContentsRoot.GetComponent<InterruptProductionDirector>();
            if (director == null)
            {
                Debug.LogError("[InterruptHitLight] NetworkManager.prefab 에 InterruptProductionDirector 가 없다");
                return;
            }

            var so = new SerializedObject(director);
            WireIfEmpty(so, "hitLightLookDash", AssetDatabase.LoadAssetAtPath<InterruptHitLightLook>(DashLookPath));
            WireIfEmpty(so, "hitLightVfxDash", LoadFx(DashPrefabPath));
            WireIfEmpty(so, "hitLightLookSuppress", AssetDatabase.LoadAssetAtPath<InterruptHitLightLook>(SuppressLookPath));
            WireIfEmpty(so, "hitLightVfxSuppress", LoadFx(SuppressPrefabPath));
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static InterruptHitLightFx LoadFx(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        return prefab != null ? prefab.GetComponent<InterruptHitLightFx>() : null;
    }

    static void WireIfEmpty(SerializedObject so, string field, Object value)
    {
        SerializedProperty property = so.FindProperty(field);
        if (property != null && property.objectReferenceValue == null && value != null)
            property.objectReferenceValue = value;
    }
}
