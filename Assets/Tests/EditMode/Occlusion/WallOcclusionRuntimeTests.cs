using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace VeyTrace.Rendering.Occlusion.Tests
{
    public sealed class WallOcclusionRuntimeTests
    {
        private const string SerializationPrefabPath =
            "Assets/Tests/EditMode/Occlusion/__Temp_ComponentSerialization.prefab";
        private const string RegisterWirePrefabPath =
            "Assets/Tests/EditMode/Occlusion/PF_Prop_RegisterWireProbe.prefab";
        private const string RegisteredSourceMaterialPath =
            "Assets/50.Art/MapGen/MapObj/LevelDeliveryV3/Materials/Generic_01_A_V3.mat";

        private readonly List<Object> objectsToDestroy = new();

        [SetUp]
        public void SetUp()
        {
            WallOcclusionRegistry.ClearForTests();
            DeleteTemporaryAssets();
        }

        [TearDown]
        public void TearDown()
        {
            WallOcclusionRegistry.ClearForTests();
            for (int i = objectsToDestroy.Count - 1; i >= 0; i--)
            {
                if (objectsToDestroy[i] != null)
                    Object.DestroyImmediate(objectsToDestroy[i]);
            }

            objectsToDestroy.Clear();
            Selection.objects = Array.Empty<Object>();
            DeleteTemporaryAssets();
        }

        [Test]
        public void LocalXZArea_ContainsRotatedPoint()
        {
            GameObject root = CreateObject("Level_L01");
            var area = new LocalXZArea("Entry", Vector2.zero, new Vector2(2f, 6f), 90f);

            Assert.That(area.Contains(root.transform, new Vector3(2f, 0f, 0f)), Is.True);
            Assert.That(area.Contains(root.transform, new Vector3(0f, 0f, 2f)), Is.False);
        }

        [Test]
        public void Registry_AllowsLevelMembershipAndSectionOwnershipOnSameCollider()
        {
            TestHierarchy hierarchy = CreateHierarchy(0f);
            OcclusionSection section = hierarchy.SectionRoot.AddComponent<OcclusionSection>();
            section.ConfigureAuthoring(
                new Renderer[] { hierarchy.Renderer },
                new Collider[] { hierarchy.Collider });

            hierarchy.Root.SetActive(true);
            WallOcclusionRegistry.Register(hierarchy.Level);
            WallOcclusionRegistry.Register(section);

            Assert.That(
                WallOcclusionRegistry.TryGetLevel(hierarchy.Collider, out ElevationLevel level),
                Is.True);
            Assert.That(level, Is.SameAs(hierarchy.Level));
            Assert.That(
                WallOcclusionRegistry.TryGetSection(hierarchy.Collider, out OcclusionSection resolved),
                Is.True);
            Assert.That(resolved, Is.SameAs(section));
        }

        [Test]
        public void ElevationState_InitializesAtRisingTwentyPercent()
        {
            StackFixture fixture = CreateTwoLevelStack();
            var state = new ElevationStackState(fixture.Stack);

            state.Update(
                Vector3.zero,
                2.1f,
                OcclusionVerticalMotion.Stable,
                true,
                true,
                null,
                0.2f,
                0.6f);

            Assert.That(state.ActiveLevel, Is.SameAs(fixture.Upper));
            Assert.That(state.IsAboveActiveLevel(fixture.Upper), Is.False);
        }

        [Test]
        public void ElevationState_GroundedRiseSwitchesAtTwentyPercent()
        {
            StackFixture fixture = CreateTwoLevelStack();
            var state = new ElevationStackState(fixture.Stack);
            Update(state, 0f, OcclusionVerticalMotion.Stable, true);

            Update(state, 1.9f, OcclusionVerticalMotion.Rising, true);
            Assert.That(state.ActiveLevel, Is.SameAs(fixture.Lower));

            Update(state, 2f, OcclusionVerticalMotion.Rising, true);
            Assert.That(state.ActiveLevel, Is.SameAs(fixture.Upper));
        }

        [Test]
        public void ElevationState_RegisteredGroundLevelOverridesHeightFallback()
        {
            StackFixture fixture = CreateTwoLevelStack();
            var state = new ElevationStackState(fixture.Stack);

            state.Update(
                Vector3.zero,
                9f,
                OcclusionVerticalMotion.Stable,
                true,
                true,
                fixture.Lower,
                0.2f,
                0.6f);

            Assert.That(state.ActiveLevel, Is.SameAs(fixture.Lower));
        }

        [Test]
        public void ElevationState_AirborneRiseDoesNotChangeLevel()
        {
            StackFixture fixture = CreateTwoLevelStack();
            var state = new ElevationStackState(fixture.Stack);
            Update(state, 0f, OcclusionVerticalMotion.Stable, true);

            Update(state, 8f, OcclusionVerticalMotion.Rising, false);

            Assert.That(state.ActiveLevel, Is.SameAs(fixture.Lower));
            Assert.That(state.IsAboveActiveLevel(fixture.Upper), Is.True);
        }

        [Test]
        public void ElevationState_FallSwitchesAfterSixtyPercentDescent()
        {
            StackFixture fixture = CreateTwoLevelStack();
            var state = new ElevationStackState(fixture.Stack);
            Update(state, 10f, OcclusionVerticalMotion.Stable, true);

            Update(state, 4.1f, OcclusionVerticalMotion.Falling, false);
            Assert.That(state.ActiveLevel, Is.SameAs(fixture.Upper));

            Update(state, 4f, OcclusionVerticalMotion.Falling, false);
            Assert.That(state.ActiveLevel, Is.SameAs(fixture.Lower));
        }

        [Test]
        public void ElevationState_OutsideAreasTreatsEveryLevelAsAboveCandidate()
        {
            StackFixture fixture = CreateTwoLevelStack();
            var state = new ElevationStackState(fixture.Stack);

            state.Update(
                new Vector3(100f, 0f, 100f),
                0f,
                OcclusionVerticalMotion.Stable,
                true,
                true,
                null,
                0.2f,
                0.6f);

            Assert.That(state.IsInside, Is.False);
            Assert.That(state.IsAboveActiveLevel(fixture.Lower), Is.True);
            Assert.That(state.IsAboveActiveLevel(fixture.Upper), Is.True);
        }

        [Test]
        public void RendererController_KeepsOriginalMaterialAndRestoresOriginalPropertyBlock()
        {
            WallOcclusionSettings settings = CreateSettings();
            Material source = AssetDatabase.LoadAssetAtPath<Material>(RegisteredSourceMaterialPath);
            Assert.That(source, Is.Not.Null);
            Assert.That(
                source.HasProperty(WallOcclusionGlobals.StrengthPropertyId),
                Is.True,
                "The registered V3 source material must expose _WallOcclusionStrength.");
            TestHierarchy hierarchy = CreateHierarchy(0f, source);
            hierarchy.Root.SetActive(true);
            var originalBlock = new MaterialPropertyBlock();
            originalBlock.SetFloat(WallOcclusionGlobals.StrengthPropertyId, 0.37f);
            hierarchy.Renderer.SetPropertyBlock(originalBlock);
            var controller = new WallOcclusionRendererController(settings);

            controller.BeginFrame();
            Assert.That(controller.AddLevel(hierarchy.Level), Is.True);
            controller.EndFrame(settings.fadeInDuration);
            Assert.That(hierarchy.Renderer.sharedMaterial, Is.SameAs(source));
            AssertStrength(hierarchy.Renderer, 1f);

            controller.BeginFrame();
            controller.EndFrame(settings.releaseGraceDuration);
            Assert.That(hierarchy.Renderer.sharedMaterial, Is.SameAs(source));
            AssertStrength(hierarchy.Renderer, 1f);

            controller.BeginFrame();
            controller.EndFrame(settings.restoreDuration + 0.001f);

            Assert.That(hierarchy.Renderer.sharedMaterial, Is.SameAs(source));
            AssertStrength(hierarchy.Renderer, 0.37f);
            Assert.That(controller.ActiveRendererCount, Is.Zero);
        }

        [Test]
        public void RendererController_UnsupportedMaterialStaysOpaqueAndWarnsOwnerOnce()
        {
            WallOcclusionSettings settings = CreateSettings();
            Material unsupported = CreateMaterial("Unsupported");
            TestHierarchy hierarchy = CreateHierarchy(0f, unsupported);
            hierarchy.Root.SetActive(true);
            var controller = new WallOcclusionRendererController(settings);
            string warning =
                "[WallOcclusion] 'Level_L01' is kept opaque: Renderer 'WallSection_01' " +
                "uses material 'Unsupported' without _WallOcclusionStrength support.";

            LogAssert.Expect(LogType.Warning, warning);
            controller.BeginFrame();
            Assert.That(controller.AddLevel(hierarchy.Level), Is.False);
            Assert.That(controller.AddLevel(hierarchy.Level), Is.False);
            controller.EndFrame(settings.fadeInDuration);

            Assert.That(hierarchy.Renderer.sharedMaterial, Is.SameAs(unsupported));
            Assert.That(controller.ActiveRendererCount, Is.Zero);
        }

        [Test]
        public void BuildMask_DisabledFlagIsZeroAndValuesAreClamped()
        {
            Vector4 mask = WallOcclusionGlobals.BuildMask(-1f, 0f, false);

            Assert.That(mask.x, Is.Zero);
            Assert.That(mask.y, Is.GreaterThanOrEqualTo(1f));
            Assert.That(mask.z, Is.Zero);
        }

        [TestCase(20f, 32f)]
        [TestCase(100f, 150f)]
        [TestCase(400f, 192f)]
        public void FeatherWidth_ScalesWithProjectedRadiusAndClamps(
            float projectedRadiusPixels,
            float expectedPixels)
        {
            var settings = ScriptableObject.CreateInstance<WallOcclusionSettings>();
            objectsToDestroy.Add(settings);
            settings.featherRadiusScale = 1.5f;
            settings.minFeatherPixels = 32f;
            settings.maxFeatherPixels = 192f;

            Assert.That(
                settings.CalculateFeatherPixels(projectedRadiusPixels),
                Is.EqualTo(expectedPixels).Within(0.001f));
        }

        [Test]
        public void SightlineFilter_AcceptsColliderThatActuallyCoversCapsuleSamples()
        {
            Camera camera = CreateSightlineCamera();
            GameObject blocker = CreateObject("BlockingWall");
            blocker.transform.position = new Vector3(0f, 0f, -2f);
            BoxCollider collider = blocker.AddComponent<BoxCollider>();
            collider.size = new Vector3(1.5f, 3f, 0.5f);
            Physics.SyncTransforms();

            CreateSightlineSamples(camera, out Ray[] rays, out float[] distances, out int count);

            Assert.That(
                WallOcclusionSightlineFilter.BlocksAnySample(collider, rays, distances, count),
                Is.True);
        }

        [Test]
        public void SightlineFilter_RejectsAdjacentColliderThatDoesNotCoverCapsuleSamples()
        {
            Camera camera = CreateSightlineCamera();
            GameObject adjacent = CreateObject("AdjacentProp");
            adjacent.transform.position = new Vector3(2.5f, 0f, -2f);
            BoxCollider collider = adjacent.AddComponent<BoxCollider>();
            collider.size = new Vector3(1f, 2f, 0.5f);
            Physics.SyncTransforms();

            CreateSightlineSamples(camera, out Ray[] rays, out float[] distances, out int count);

            Assert.That(
                WallOcclusionSightlineFilter.BlocksAnySample(collider, rays, distances, count),
                Is.False);
        }

        [Test]
        public void AuthoringComponents_HaveDistinctScriptsAndSurvivePrefabRoundTrip()
        {
            GameObject root = CreateObject("PF_Zone_ComponentSerialization");
            root.SetActive(false);
            GameObject stackObject = CreateChild(root.transform, "ElevationStack_01");
            ElevationStack stack = stackObject.AddComponent<ElevationStack>();
            GameObject levelObject = CreateChild(stackObject.transform, "Level_L01");
            ElevationLevel level = levelObject.AddComponent<ElevationLevel>();
            GameObject content = CreateChild(levelObject.transform, "Content");
            CreateChild(content.transform, "OccludableProps");
            CreateChild(content.transform, "LevelOnlyProps");
            GameObject sectionObject = CreateChild(content.transform, "WallSection_01");
            MeshRenderer renderer = sectionObject.AddComponent<MeshRenderer>();
            BoxCollider collider = sectionObject.AddComponent<BoxCollider>();
            OcclusionSection section = sectionObject.AddComponent<OcclusionSection>();
            level.ConfigureAuthoring(
                content.transform,
                new Renderer[] { renderer },
                new Collider[] { collider },
                new[] { new LocalXZArea("Level", Vector2.zero, new Vector2(10f, 10f)) });
            section.ConfigureAuthoring(
                new Renderer[] { renderer },
                new Collider[] { collider });

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, SerializationPrefabPath, out bool success);
            Assert.That(success, Is.True);
            Assert.That(saved, Is.Not.Null);
            Object.DestroyImmediate(root);
            AssetDatabase.ImportAsset(SerializationPrefabPath, ImportAssetOptions.ForceSynchronousImport);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SerializationPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            ElevationStack savedStack = prefab.GetComponentInChildren<ElevationStack>(true);
            ElevationLevel savedLevel = prefab.GetComponentInChildren<ElevationLevel>(true);
            OcclusionSection savedSection = prefab.GetComponentInChildren<OcclusionSection>(true);
            Assert.That(savedStack, Is.Not.Null);
            Assert.That(savedLevel, Is.Not.Null);
            Assert.That(savedSection, Is.Not.Null);

            string stackGuid = AssertScriptBinding(savedStack, "ElevationStack.cs");
            string levelGuid = AssertScriptBinding(savedLevel, "ElevationLevel.cs");
            string sectionGuid = AssertScriptBinding(savedSection, "OcclusionSection.cs");
            Assert.That(new HashSet<string> { stackGuid, levelGuid, sectionGuid }.Count, Is.EqualTo(3));

            string yaml = File.ReadAllText(SerializationPrefabPath);
            Assert.That(yaml, Does.Not.Contain("m_Script: {fileID: 0}"));
            Assert.That(yaml, Does.Contain($"guid: {stackGuid}"));
            Assert.That(yaml, Does.Contain($"guid: {levelGuid}"));
            Assert.That(yaml, Does.Contain($"guid: {sectionGuid}"));
        }

        [Test]
        public void RegisterWireSelected_SavesStandaloneSectionWithValidScriptReference()
        {
            Material sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(RegisteredSourceMaterialPath);
            Assert.That(sourceMaterial, Is.Not.Null);

            GameObject root = CreateObject("PF_Prop_RegisterWireProbe");
            MeshRenderer renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = sourceMaterial;
            root.AddComponent<BoxCollider>();
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, RegisterWirePrefabPath, out bool success);
            Assert.That(success, Is.True);
            Assert.That(saved, Is.Not.Null);
            Object.DestroyImmediate(root);

            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(RegisterWirePrefabPath);
            Assert.That(
                EditorApplication.ExecuteMenuItem(
                    "Tools/Rendering/Wall Occlusion/Register-Wire Selected Prefabs"),
                Is.True);
            AssetDatabase.ImportAsset(RegisterWirePrefabPath, ImportAssetOptions.ForceSynchronousImport);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RegisterWirePrefabPath);
            Assert.That(prefab, Is.Not.Null);
            OcclusionSection section = prefab.GetComponent<OcclusionSection>();
            Assert.That(section, Is.Not.Null);
            Assert.That(section.Renderers.Count, Is.EqualTo(1));
            Assert.That(section.Colliders.Count, Is.EqualTo(1));

            string sectionGuid = AssertScriptBinding(section, "OcclusionSection.cs");
            string yaml = File.ReadAllText(RegisterWirePrefabPath);
            Assert.That(yaml, Does.Not.Contain("m_Script: {fileID: 0}"));
            Assert.That(yaml, Does.Contain($"m_Script: {{fileID: 11500000, guid: {sectionGuid}, type: 3}}"));
        }

        private void Update(
            ElevationStackState state,
            float footY,
            OcclusionVerticalMotion motion,
            bool grounded)
        {
            state.Update(
                Vector3.zero,
                footY,
                motion,
                grounded,
                true,
                null,
                0.2f,
                0.6f);
        }

        private StackFixture CreateTwoLevelStack()
        {
            GameObject root = CreateObject("PF_Zone_Test");
            root.SetActive(false);
            GameObject stackObject = CreateChild(root.transform, "ElevationStack_01");
            ElevationStack stack = stackObject.AddComponent<ElevationStack>();
            ElevationLevel lower = CreateLevel(stackObject.transform, "Level_L01", 0f);
            ElevationLevel upper = CreateLevel(stackObject.transform, "Level_L02", 10f);
            root.SetActive(true);
            WallOcclusionRegistry.Register(lower);
            WallOcclusionRegistry.Register(upper);
            return new StackFixture(stack, lower, upper);
        }

        private TestHierarchy CreateHierarchy(float levelY, Material material = null)
        {
            GameObject root = CreateObject("PF_Zone_Test");
            root.SetActive(false);
            GameObject stackObject = CreateChild(root.transform, "ElevationStack_01");
            stackObject.AddComponent<ElevationStack>();
            GameObject levelObject = CreateChild(stackObject.transform, "Level_L01");
            levelObject.transform.localPosition = new Vector3(0f, levelY, 0f);
            ElevationLevel level = levelObject.AddComponent<ElevationLevel>();
            GameObject content = CreateChild(levelObject.transform, "Content");
            CreateChild(content.transform, "OccludableProps");
            CreateChild(content.transform, "LevelOnlyProps");
            GameObject sectionRoot = CreateChild(content.transform, "WallSection_01");
            MeshRenderer renderer = sectionRoot.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material ?? CreateMaterial("Source");
            BoxCollider collider = sectionRoot.AddComponent<BoxCollider>();
            level.ConfigureAuthoring(
                content.transform,
                new Renderer[] { renderer },
                new Collider[] { collider },
                new[] { new LocalXZArea("Level", Vector2.zero, new Vector2(20f, 20f)) });
            return new TestHierarchy(root, level, sectionRoot, renderer, collider);
        }

        private ElevationLevel CreateLevel(Transform stack, string name, float y)
        {
            GameObject levelObject = CreateChild(stack, name);
            levelObject.transform.localPosition = new Vector3(0f, y, 0f);
            ElevationLevel level = levelObject.AddComponent<ElevationLevel>();
            GameObject content = CreateChild(levelObject.transform, "Content");
            CreateChild(content.transform, "OccludableProps");
            CreateChild(content.transform, "LevelOnlyProps");
            GameObject geometry = CreateChild(content.transform, "FloorMesh");
            MeshRenderer renderer = geometry.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateMaterial($"{name}_Material");
            BoxCollider collider = geometry.AddComponent<BoxCollider>();
            level.ConfigureAuthoring(
                content.transform,
                new Renderer[] { renderer },
                new Collider[] { collider },
                new[] { new LocalXZArea(name, Vector2.zero, new Vector2(20f, 20f)) });
            return level;
        }

        private WallOcclusionSettings CreateSettings()
        {
            var settings = ScriptableObject.CreateInstance<WallOcclusionSettings>();
            objectsToDestroy.Add(settings);
            return settings;
        }

        private static void AssertStrength(Renderer renderer, float expected)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Assert.That(
                block.GetFloat(WallOcclusionGlobals.StrengthPropertyId),
                Is.EqualTo(expected).Within(0.001f));
        }

        private Camera CreateSightlineCamera()
        {
            GameObject cameraObject = CreateObject("SightlineCamera");
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 60f;
            camera.aspect = 16f / 9f;
            camera.pixelRect = new Rect(0f, 0f, 1920f, 1080f);
            return camera;
        }

        private static void CreateSightlineSamples(
            Camera camera,
            out Ray[] rays,
            out float[] distances,
            out int count)
        {
            rays = new Ray[WallOcclusionSightlineFilter.RequiredSampleCapacity];
            distances = new float[WallOcclusionSightlineFilter.RequiredSampleCapacity];
            count = WallOcclusionSightlineFilter.BuildSamples(
                camera,
                new Vector3(0f, 1f, 0f),
                new Vector3(0f, -1f, 0f),
                60f,
                rays,
                distances);
        }

        private GameObject CreateObject(string name)
        {
            var gameObject = new GameObject(name);
            objectsToDestroy.Add(gameObject);
            return gameObject;
        }

        private GameObject CreateChild(Transform parent, string name)
        {
            GameObject child = CreateObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private Material CreateMaterial(string name)
        {
            Shader shader = Shader.Find("Sprites/Default");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader) { name = name };
            objectsToDestroy.Add(material);
            return material;
        }

        private static string AssertScriptBinding(MonoBehaviour component, string expectedFileName)
        {
            MonoScript script = MonoScript.FromMonoBehaviour(component);
            Assert.That(script, Is.Not.Null);
            string path = AssetDatabase.GetAssetPath(script);
            Assert.That(Path.GetFileName(path), Is.EqualTo(expectedFileName));
            string guid = AssetDatabase.AssetPathToGUID(path);
            Assert.That(guid, Is.Not.Empty);
            return guid;
        }

        private static void DeleteTemporaryAssets()
        {
            AssetDatabase.DeleteAsset(SerializationPrefabPath);
            AssetDatabase.DeleteAsset(RegisterWirePrefabPath);
        }

        private readonly struct StackFixture
        {
            public readonly ElevationStack Stack;
            public readonly ElevationLevel Lower;
            public readonly ElevationLevel Upper;

            public StackFixture(ElevationStack stack, ElevationLevel lower, ElevationLevel upper)
            {
                Stack = stack;
                Lower = lower;
                Upper = upper;
            }
        }

        private readonly struct TestHierarchy
        {
            public readonly GameObject Root;
            public readonly ElevationLevel Level;
            public readonly GameObject SectionRoot;
            public readonly MeshRenderer Renderer;
            public readonly BoxCollider Collider;

            public TestHierarchy(
                GameObject root,
                ElevationLevel level,
                GameObject sectionRoot,
                MeshRenderer renderer,
                BoxCollider collider)
            {
                Root = root;
                Level = level;
                SectionRoot = sectionRoot;
                Renderer = renderer;
                Collider = collider;
            }
        }
    }
}
