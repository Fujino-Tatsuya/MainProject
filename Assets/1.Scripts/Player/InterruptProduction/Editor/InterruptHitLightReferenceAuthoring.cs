using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 적중 라이트 연출 V3 '레퍼런스 맞춤'(194 후속) 에셋 저작 메뉴 — V3 전용 수치 SO·Volume 프로필 2개(블룸·색 보정)·메시 2개(중심 점·가로 빛줄)·
/// 재질·월드 이펙트 프리팹을 만들고 NetworkManager.prefab 디렉터 V3 칸에 잇는다. V1·V2 에셋(SO·프리팹·재질)은 건드리지 않는다.
/// 레퍼런스(ZZZ 컷신 2:34 적중 프레임)는 보고 맞춘 것뿐 — 텍스처로 쓰지 않는다. 런타임 생성 없음.
/// 다시 실행하면 메시·프리팹은 같은 경로에 다시 짓고(guid 유지), SO·프로필·재질은 있으면 값을 그대로 둔다.
/// </summary>
internal static class InterruptHitLightReferenceAuthoring
{
    const string Folder = InterruptHitLightAuthoring.Folder;
    public const string LookPath = Folder + "/InterruptHitLightLook_V3.asset";
    public const string PrefabPath = Folder + "/FX_InterruptHitLight_V3.prefab";
    public const string BloomProfilePath = Folder + "/VP_InterruptHitLight_V3_Bloom.asset";
    public const string GradeProfilePath = Folder + "/VP_InterruptHitLight_V3_Grade.asset";
    public const string CoreMeshPath = Folder + "/Mesh_InterruptHitLight_Core.asset";
    public const string StreakMeshPath = Folder + "/Mesh_InterruptHitLight_Streak.asset";
    const string CoreMaterialPath = Folder + "/M_InterruptHitLight_Core.mat";
    const string StreakMaterialPath = Folder + "/M_InterruptHitLight_Streak.mat";
    const string SparkMaterialPath = Folder + "/M_InterruptHitLight_Spark_V3.mat";

    public const int CoreBladeCount = 16;

    [MenuItem("Tools/Interrupt/적중 라이트 V3 레퍼런스 에셋 만들기 (194)")]
    public static void BuildAll()
    {
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();

        VolumeProfile bloom = EnsureBloomProfile();
        VolumeProfile grade = EnsureGradeProfile();
        InterruptHitLightLook look = EnsureLook(bloom, grade);
        GameObject prefab = BuildPrefab();
        WireDirector(look, prefab != null ? prefab.GetComponent<InterruptHitLightFx>() : null);
        AssetDatabase.SaveAssets();
        Debug.Log($"[InterruptHitLight] V3 에셋 저작 완료 — {LookPath}, {PrefabPath}");
    }

    // ---- 수치 SO — 처음 만들 때만 V3 시작값을 넣는다(사람이 바꾼 값은 그대로, 비어 있는 프로필 칸만 잇는다) ----

    static InterruptHitLightLook EnsureLook(VolumeProfile bloom, VolumeProfile grade)
    {
        var look = AssetDatabase.LoadAssetAtPath<InterruptHitLightLook>(LookPath);
        bool create = look == null;
        if (create)
        {
            look = ScriptableObject.CreateInstance<InterruptHitLightLook>();
            AssetDatabase.CreateAsset(look, LookPath);
        }

        var so = new SerializedObject(look);
        if (create)
        {
            // 라이트 — 맞닿은 캐릭터 가장자리·발밑만 주황으로 물들 만큼(세기 ~4, 반경 ~3).
            so.FindProperty("lightColor").colorValue = new Color(1f, 0.6f, 0.3f, 1f);
            so.FindProperty("lightIntensity").floatValue = 4f;
            so.FindProperty("lightRange").floatValue = 3f;
            // 암순응 나 — 주변을 더 어둡게(V1 0.85/0.7).
            so.FindProperty("sunDimDepth").floatValue = 0.95f;
            so.FindProperty("ambientDimDepth").floatValue = 0.9f;
            // V3 는 화이트아웃이 없다(화면 전체를 하얗게 날리지 않는다).
            so.FindProperty("whiteoutFrames").intValue = 0;
        }

        SerializedProperty bloomProperty = so.FindProperty("bloomProfile");
        if (bloomProperty.objectReferenceValue == null)
            bloomProperty.objectReferenceValue = bloom;
        SerializedProperty gradeProperty = so.FindProperty("gradeProfile");
        if (gradeProperty.objectReferenceValue == null)
            gradeProperty.objectReferenceValue = grade;
        so.ApplyModifiedPropertiesWithoutUndo();
        return look;
    }

    // 블룸 — 중심(HDR 1.5 넘는 픽셀)만 번진다. V1 프로필(threshold 0.8 · intensity 6)은 그대로.
    static VolumeProfile EnsureBloomProfile() =>
        EnsureProfile(BloomProfilePath, profile =>
        {
            Bloom bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.5f);
            bloom.intensity.Override(1.5f);
            bloom.scatter.Override(0.6f);
            bloom.tint.Override(new Color(1f, 0.86f, 0.72f, 1f));
        });

    // 색 보정 — 채도 낮춤 + 약한 보랏빛 색 필터 + 노출 살짝 낮춤. 가중치 곡선은 Look 의 grade 칸.
    static VolumeProfile EnsureGradeProfile() =>
        EnsureProfile(GradeProfilePath, profile =>
        {
            ColorAdjustments color = profile.Add<ColorAdjustments>(true);
            color.saturation.Override(-35f);
            color.colorFilter.Override(new Color(0.84f, 0.78f, 1f, 1f));
            color.postExposure.Override(-0.3f);
        });

    static VolumeProfile EnsureProfile(string path, System.Action<VolumeProfile> fill)
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile != null)
            return profile;

        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(profile, path);
        fill(profile);
        // Volume 컴포넌트는 프로필 에셋의 하위 에셋으로 저장해야 다시 열어도 남는다.
        foreach (VolumeComponent component in profile.components)
        {
            component.name = component.GetType().Name;
            AssetDatabase.AddObjectToAsset(component, profile);
        }

        EditorUtility.SetDirty(profile);
        return profile;
    }

    // ---- 월드 이펙트 프리팹 ----

    static GameObject BuildPrefab()
    {
        Mesh coreMesh = EnsureBladeMesh(CoreMeshPath, "Mesh_InterruptHitLight_Core", CoreBlades());
        Mesh streakMesh = EnsureBladeMesh(StreakMeshPath, "Mesh_InterruptHitLight_Streak", StreakBlades());
        Material coreMaterial = EnsureRaysMaterial(CoreMaterialPath, new Color(16f, 15f, 14f, 1f), new Color(6f, 2.4f, 0.6f, 1f), 1f, 2f, 0f);
        Material streakMaterial = EnsureRaysMaterial(StreakMaterialPath, new Color(12f, 10f, 8f, 1f), new Color(5f, 2f, 0.5f, 1f), 0.9f, 6f, 0.2f);
        Material sparkMaterial = EnsureSparkMaterial();
        Material raysMaterial = InterruptHitLightAuthoring.EnsureMaterial(InterruptHitLightAuthoring.RaysMaterialPath, InterruptHitLightAuthoring.RaysShaderName);
        Mesh raysMesh = AssetDatabase.LoadAssetAtPath<Mesh>(InterruptHitLightAuthoring.RaysMeshPath);
        if (raysMesh == null)
            raysMesh = InterruptHitLightAuthoring.EnsureRaysMesh();
        Mesh debrisMesh = AssetDatabase.LoadAssetAtPath<Mesh>(InterruptHitLightAuthoring.DebrisMeshPath);
        if (debrisMesh == null)
            debrisMesh = InterruptHitLightAuthoring.EnsureDebrisMesh();
        Material debrisMaterial = InterruptHitLightAuthoring.EnsureDebrisMaterial();

        var root = new GameObject("FX_InterruptHitLight_V3");
        try
        {
            root.AddComponent<InterruptHitLightFx>();
            BuildCore(root.transform, coreMesh, coreMaterial);
            BuildStreak(root.transform, streakMesh, streakMaterial);
            BuildRays(root.transform, raysMesh, raysMaterial);
            BuildSparks(root.transform, sparkMaterial);
            InterruptHitLightAuthoring.BuildDebris(root.transform, debrisMesh, debrisMaterial);
            return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // 중심 점 — 작고 아주 밝은 별 모양 점(HDR, 카메라 정렬). 잠깐 최대였다가 줄며 사라진다.
    static void BuildCore(Transform parent, Mesh mesh, Material material)
    {
        ParticleSystem system = InterruptHitLightAuthoring.NewSystem(parent, "Core");
        ParticleSystem.MainModule main = system.main;
        main.startLifetime = 0.35f;
        main.startSpeed = 0f;
        main.startSize = 0.45f;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = Color.white;
        main.maxParticles = 2;
        system.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;

        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.6f, 0f, 8f), new Keyframe(0.12f, 1f), new Keyframe(1f, 0.5f)));
        SetAlphaFade(system, 0.4f, 0.85f);
        SetViewMesh(system, mesh, material);
    }

    // 가로 얇은 빛줄(아나모픽 느낌) — 화면 가로로 길게, 아주 짧게. 회전 없음(늘 화면 가로).
    static void BuildStreak(Transform parent, Mesh mesh, Material material)
    {
        ParticleSystem system = InterruptHitLightAuthoring.NewSystem(parent, "Streak");
        ParticleSystem.MainModule main = system.main;
        main.startLifetime = 0.22f;
        main.startSpeed = 0f;
        main.startSize = 2.6f;
        main.startRotation = 0f;
        main.startColor = Color.white;
        main.maxParticles = 2;
        system.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;

        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.5f, 0f, 6f), new Keyframe(0.2f, 1f), new Keyframe(1f, 1.15f)));
        SetAlphaFade(system, 0.3f, 0.8f);
        SetViewMesh(system, mesh, material);
    }

    // 빛줄기 — V1 날개 메시·재질 그대로, 크기·알파만 줄여 보조로.
    static void BuildRays(Transform parent, Mesh mesh, Material material)
    {
        ParticleSystem system = InterruptHitLightAuthoring.BuildRays(parent, mesh, material);
        ParticleSystem.MainModule main = system.main;
        main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
        main.startColor = new Color(1f, 1f, 1f, 0.4f);
    }

    // 불꽃 — 더 가늘고 길게, 더 많이, 밝은 주황(HDR 재질).
    static void BuildSparks(Transform parent, Material material)
    {
        ParticleSystem system = InterruptHitLightAuthoring.BuildSparks(parent, material);
        ParticleSystem.MainModule main = system.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(9f, 18f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.045f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.7f, 0.35f, 1f), new Color(1f, 0.42f, 0.08f, 1f));
        main.gravityModifier = 0.4f;
        main.maxParticles = 96;
        system.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 55, 70) });

        var renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.velocityScale = 0.06f;
        renderer.lengthScale = 4f;
    }

    static void SetAlphaFade(ParticleSystem system, float holdTime, float holdAlpha)
    {
        ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
        color.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(holdAlpha, holdTime), new GradientAlphaKey(0f, 1f) });
        color.color = fade;
    }

    static void SetViewMesh(ParticleSystem system, Mesh mesh, Material material)
    {
        var renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = mesh;
        renderer.alignment = ParticleSystemRenderSpace.View; // 카메라 정렬 — 메시 XY 평면이 화면을 향한다(X = 화면 가로)
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // ---- 날개 메시(InterruptHitLightRays 셰이더 형식 — UV.x = 날개 번호 + 가로, UV.y = 중심 0 → 끝 1) ----

    struct Blade
    {
        public float Angle, Length, BaseHalf, TipHalf;

        public Blade(float angle, float length, float baseHalf, float tipHalf)
        {
            Angle = angle;
            Length = length;
            BaseHalf = baseHalf;
            TipHalf = tipHalf;
        }
    }

    // 중심 점 — 넓은 날개 16개가 겹쳐 가운데가 가장 밝은 작은 별(반경 1, 파티클 크기로 줄인다).
    static Blade[] CoreBlades()
    {
        var blades = new Blade[CoreBladeCount];
        for (int i = 0; i < CoreBladeCount; i++)
            blades[i] = new Blade(i * Mathf.PI * 2f / CoreBladeCount, i % 2 == 0 ? 1f : 0.7f, 0.18f, 0.03f);
        return blades;
    }

    // 가로 빛줄 — 좌우 날개 2개, 아주 얇고 끝이 가늘다.
    static Blade[] StreakBlades() => new[]
    {
        new Blade(0f, 1f, 0.025f, 0.002f),
        new Blade(Mathf.PI, 1f, 0.025f, 0.002f),
    };

    static Mesh EnsureBladeMesh(string path, string name, Blade[] blades)
    {
        var vertices = new Vector3[blades.Length * 4];
        var uvs = new Vector2[blades.Length * 4];
        var triangles = new int[blades.Length * 6];
        for (int i = 0; i < blades.Length; i++)
        {
            Blade b = blades[i];
            var dir = new Vector3(Mathf.Cos(b.Angle), Mathf.Sin(b.Angle), 0f);
            var side = new Vector3(-dir.y, dir.x, 0f);
            int v = i * 4;
            vertices[v] = -side * b.BaseHalf;
            vertices[v + 1] = side * b.BaseHalf;
            vertices[v + 2] = dir * b.Length - side * b.TipHalf;
            vertices[v + 3] = dir * b.Length + side * b.TipHalf;
            uvs[v] = new Vector2(i, 0f);
            uvs[v + 1] = new Vector2(i + 0.999f, 0f);
            uvs[v + 2] = new Vector2(i, 1f);
            uvs[v + 3] = new Vector2(i + 0.999f, 1f);

            int t = i * 6;
            triangles[t] = v;
            triangles[t + 1] = v + 2;
            triangles[t + 2] = v + 1;
            triangles[t + 3] = v + 1;
            triangles[t + 4] = v + 2;
            triangles[t + 5] = v + 3;
        }

        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool create = mesh == null;
        if (create)
            mesh = new Mesh();
        mesh.Clear();
        mesh.name = name;
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        if (create)
            AssetDatabase.CreateAsset(mesh, path);
        else
            EditorUtility.SetDirty(mesh);
        return mesh;
    }

    // ---- 재질 — 처음 만들 때만 값을 넣는다 ----

    static Material EnsureRaysMaterial(string path, Color core, Color edge, float edgeSoftness, float coreSharpness, float tipFade)
    {
        bool create = AssetDatabase.LoadAssetAtPath<Material>(path) == null;
        Material material = InterruptHitLightAuthoring.EnsureMaterial(path, InterruptHitLightAuthoring.RaysShaderName);
        if (material == null || !create)
            return material;

        material.SetColor("_CoreColor", core);
        material.SetColor("_EdgeColor", edge);
        material.SetFloat("_EdgeSoftness", edgeSoftness);
        material.SetFloat("_CoreSharpness", coreSharpness);
        material.SetFloat("_NoiseAmount", 0f);
        material.SetFloat("_TipFade", tipFade);
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material EnsureSparkMaterial()
    {
        bool create = AssetDatabase.LoadAssetAtPath<Material>(SparkMaterialPath) == null;
        Material material = InterruptHitLightAuthoring.EnsureMaterial(SparkMaterialPath, InterruptHitLightAuthoring.SparkShaderName);
        if (material == null || !create)
            return material;

        material.SetColor("_Color", new Color(7f, 7f, 7f, 1f)); // HDR — V1(4)보다 밝게, 파티클 주황을 곱한다
        material.SetFloat("_Falloff", 8f);                      // V1(6)보다 가장자리가 빨리 빠져 가늘어 보인다
        EditorUtility.SetDirty(material);
        return material;
    }

    static void WireDirector(InterruptHitLightLook look, InterruptHitLightFx fx)
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
            so.FindProperty("hitLightLookReference").objectReferenceValue = look;
            so.FindProperty("hitLightVfxReference").objectReferenceValue = fx;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
