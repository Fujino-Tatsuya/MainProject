using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 적중 라이트 연출(할 일 194) 에셋 저작 메뉴 — 수치 SO·월드 이펙트 프리팹을 만들고 NetworkManager.prefab 디렉터 칸에 잇는다.
/// 다시 실행하면 프리팹을 같은 경로에 다시 짓는다(guid 유지). SO 는 있으면 값을 그대로 둔다.
/// 런타임 생성 없음 — 메시·재질·프리팹은 전부 여기서 에셋으로 저장한다.
/// </summary>
internal static class InterruptHitLightAuthoring
{
    public const string Folder = "Assets/5.VFX/Interrupt/HitLight";
    public const string LookPath = Folder + "/InterruptHitLightLook.asset";
    public const string PrefabPath = Folder + "/FX_InterruptHitLight.prefab";
    const string BloomProfilePath = "Assets/5.VFX/Interrupt/HitFlash/VP_InterruptHitFlash_Bloom.asset";
    const string NetworkManagerPrefabPath = "Assets/2.Prefabs/Network/NetworkManager.prefab";

    [MenuItem("Tools/Interrupt/적중 라이트 연출 에셋 만들기 (194)")]
    public static void BuildAll()
    {
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();

        InterruptHitLightLook look = EnsureLook();
        GameObject prefab = BuildPrefab();
        WireDirector(look, prefab != null ? prefab.GetComponent<InterruptHitLightFx>() : null);
        AssetDatabase.SaveAssets();
        Debug.Log($"[InterruptHitLight] 에셋 저작 완료 — {LookPath}, {PrefabPath}");
    }

    static InterruptHitLightLook EnsureLook()
    {
        var look = AssetDatabase.LoadAssetAtPath<InterruptHitLightLook>(LookPath);
        if (look == null)
        {
            look = ScriptableObject.CreateInstance<InterruptHitLightLook>();
            AssetDatabase.CreateAsset(look, LookPath);
        }

        // 블룸 프로필이 비어 있을 때만 간파 전용 블룸을 잇는다(사람이 바꾼 값은 그대로).
        var so = new SerializedObject(look);
        SerializedProperty bloom = so.FindProperty("bloomProfile");
        if (bloom.objectReferenceValue == null)
        {
            bloom.objectReferenceValue = AssetDatabase.LoadAssetAtPath<VolumeProfile>(BloomProfilePath);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        return look;
    }

    static GameObject BuildPrefab()
    {
        Mesh debrisMesh = EnsureDebrisMesh();
        Material sparkMaterial = EnsureSparkMaterial();
        Material debrisMaterial = EnsureDebrisMaterial();

        var root = new GameObject("FX_InterruptHitLight");
        try
        {
            root.AddComponent<InterruptHitLightFx>();
            BuildSparks(root.transform, sparkMaterial);
            BuildDebris(root.transform, debrisMesh, debrisMaterial);
            return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // ---- 단계 4 — 쇳조각: 늘어난 불꽃(스트레치 빌보드, 주황) + 작은 메시 파편(중력·바닥 충돌) ----

    public const string DebrisMeshPath = Folder + "/Mesh_InterruptHitLight_Debris.asset";
    public const string SparkMaterialPath = Folder + "/M_InterruptHitLight_Spark.mat";
    public const string DebrisMaterialPath = Folder + "/M_InterruptHitLight_Debris.mat";
    const string SparkShaderName = "BeaverLobby/VFX/InterruptHitFlash"; // 간파 적중 섬광의 가우시안 빛 덩어리(가산) — 늘이면 불꽃 줄기
    const string DebrisShaderName = "Universal Render Pipeline/Particles/Unlit";

    static readonly Color SparkHot = new Color(1f, 0.82f, 0.45f, 1f);
    static readonly Color SparkOrange = new Color(1f, 0.45f, 0.1f, 1f);

    static ParticleSystem NewSystem(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        ParticleSystem system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.playOnAwake = false; // 디렉터가 성공 순간 튼다
        main.loop = false;
        main.duration = 1f;
        main.useUnscaledTime = true; // Q8 — 재생 때도 다시 켠다
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 0f;
        return system;
    }

    static void BuildSparks(Transform parent, Material material)
    {
        ParticleSystem system = NewSystem(parent, "Sparks");
        ParticleSystem.MainModule main = system.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(7f, 14f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
        main.startColor = new ParticleSystem.MinMaxGradient(SparkHot, SparkOrange);
        main.gravityModifier = 0.8f;
        main.maxParticles = 64;

        system.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 28, 36) });

        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.15f;

        ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
        color.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.6f, 0.3f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;

        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

        var renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.035f;
        renderer.lengthScale = 2f;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    static void BuildDebris(Transform parent, Mesh mesh, Material material)
    {
        ParticleSystem system = NewSystem(parent, "Debris");
        ParticleSystem.MainModule main = system.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6.5f);
        main.startSize3D = false;
        main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.2f); // 메시 자체가 약 0.12m
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(SparkHot, SparkOrange);
        main.gravityModifier = 2.5f;
        main.maxParticles = 16;

        system.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 5, 8) });

        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere; // 위쪽으로 튄다
        shape.radius = 0.1f;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        ParticleSystem.RotationOverLifetimeModule spin = system.rotationOverLifetime;
        spin.enabled = true;
        spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-12f, 12f);
        spin.y = new ParticleSystem.MinMaxCurve(-12f, 12f);
        spin.z = new ParticleSystem.MinMaxCurve(-12f, 12f);

        // 달군 쇳조각이 식는다 — 밝은 주황 → 어두운 빨강, 끝에 줄어 사라진다(불투명 재질이라 알파 대신 크기).
        ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
        color.enabled = true;
        var cool = new Gradient();
        cool.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.35f, 0.08f, 0.02f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        color.color = cool;

        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0f)));

        // 바닥 충돌 — 월드 콜라이더(파편 수가 적어 비용 작음). 튕기고 미끄러지다 멈춘다.
        ParticleSystem.CollisionModule collision = system.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.quality = ParticleSystemCollisionQuality.Medium;
        collision.dampen = 0.5f;
        collision.bounce = 0.3f;
        collision.lifetimeLoss = 0f;
        collision.radiusScale = 0.5f;

        var renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = mesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // 작은 쇳조각 — 찌그러진 쌍뿔(6 꼭짓점) 플랫 셰이딩, 약 0.12m. 정해진 값이라 다시 만들어도 같다.
    static Mesh EnsureDebrisMesh()
    {
        Vector3[] corners =
        {
            new Vector3(0f, 0.05f, 0f), new Vector3(0f, -0.035f, 0.01f),
            new Vector3(0.06f, 0f, 0.01f), new Vector3(-0.045f, 0.005f, 0.02f),
            new Vector3(0.01f, -0.005f, 0.04f), new Vector3(-0.01f, 0.01f, -0.05f),
        };
        int[] faces = { 0, 2, 4, 0, 4, 3, 0, 3, 5, 0, 5, 2, 1, 4, 2, 1, 3, 4, 1, 5, 3, 1, 2, 5 };

        var vertices = new Vector3[faces.Length];
        var triangles = new int[faces.Length];
        for (int i = 0; i < faces.Length; i++)
        {
            vertices[i] = corners[faces[i]];
            triangles[i] = i;
        }

        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(DebrisMeshPath);
        bool create = mesh == null;
        if (create)
            mesh = new Mesh();
        mesh.Clear();
        mesh.name = "Mesh_InterruptHitLight_Debris";
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        if (create)
            AssetDatabase.CreateAsset(mesh, DebrisMeshPath);
        else
            EditorUtility.SetDirty(mesh);
        return mesh;
    }

    static Material EnsureSparkMaterial()
    {
        Material material = EnsureMaterial(SparkMaterialPath, SparkShaderName);
        if (material == null)
            return null;
        material.SetColor("_Color", new Color(4f, 4f, 4f, 1f)); // HDR — 파티클 색(주황)을 곱해 블룸
        material.SetFloat("_Falloff", 6f);
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material EnsureDebrisMaterial()
    {
        Material material = EnsureMaterial(DebrisMaterialPath, DebrisShaderName);
        if (material == null)
            return null;
        material.SetColor("_BaseColor", new Color(2.5f, 1.4f, 0.7f, 1f)); // HDR 달군 쇠 — 파티클 색을 곱한다
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material EnsureMaterial(string path, string shaderName)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
            return material;

        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError($"[InterruptHitLight] 셰이더 '{shaderName}' 없음 — {path} 를 만들지 못했다");
            return null;
        }

        material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static void WireDirector(InterruptHitLightLook look, InterruptHitLightFx fx)
    {
        using (var scope = new PrefabUtility.EditPrefabContentsScope(NetworkManagerPrefabPath))
        {
            var director = scope.prefabContentsRoot.GetComponent<InterruptProductionDirector>();
            if (director == null)
            {
                Debug.LogError("[InterruptHitLight] NetworkManager.prefab 에 InterruptProductionDirector 가 없다");
                return;
            }

            var so = new SerializedObject(director);
            so.FindProperty("hitLightLook").objectReferenceValue = look;
            so.FindProperty("hitLightVfx").objectReferenceValue = fx;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
