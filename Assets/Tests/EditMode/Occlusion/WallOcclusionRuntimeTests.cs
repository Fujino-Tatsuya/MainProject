using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace VeyTrace.Rendering.Occlusion.Tests
{
    // 재설계 후 C# 쪽에 남은 표면은 두 가지뿐이다.
    //   1) 설정값 -> 셰이더 전역 벡터 변환 (WallOcclusionGlobals)
    //   2) 머티리얼 스왑 (WallOcclusionMaterialBinder)
    // 불투명도 곡선과 벽/바닥 판정은 셰이더에 있으므로 여기서 검증하지 않는다.
    public sealed class WallOcclusionRuntimeTests
    {
        private readonly List<Object> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = objectsToDestroy.Count - 1; i >= 0; i--)
            {
                if (objectsToDestroy[i] != null)
                    Object.DestroyImmediate(objectsToDestroy[i]);
            }

            objectsToDestroy.Clear();
        }

        [Test]
        public void BuildRange_PacksRadiiAndEnableFlag()
        {
            WallOcclusionSettings settings = CreateSettings();
            settings.innerRadius = 1.5f;
            settings.outerRadius = 5f;
            settings.minimumOpacity = 0.2f;

            Vector4 range = WallOcclusionGlobals.BuildRange(settings, true);

            Assert.That(range.x, Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(range.y, Is.EqualTo(5f).Within(1e-4f));
            Assert.That(range.z, Is.EqualTo(0.2f).Within(1e-4f));
            Assert.That(range.w, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void BuildRange_ForcesOuterRadiusAboveInner()
        {
            WallOcclusionSettings settings = CreateSettings();
            settings.innerRadius = 4f;
            settings.outerRadius = 2f; // 잘못 설정해도 셰이더에서 0으로 나누지 않아야 한다.

            Vector4 range = WallOcclusionGlobals.BuildRange(settings, true);

            Assert.That(range.y, Is.GreaterThan(range.x));
        }

        [Test]
        public void BuildRange_DisabledFlagIsZero()
        {
            WallOcclusionSettings settings = CreateSettings();

            Vector4 range = WallOcclusionGlobals.BuildRange(settings, false);

            Assert.That(range.w, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void BuildRange_NullSettingsDisablesFade()
        {
            Vector4 range = WallOcclusionGlobals.BuildRange(null, true);

            Assert.That(range.w, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void BuildShape_ClampsThresholdAndFalloffs()
        {
            WallOcclusionSettings settings = CreateSettings();
            settings.floorNormalThreshold = 5f; // 1 이상이면 셰이더에서 0 나눗셈이 된다.
            settings.behindFalloff = 0f;
            settings.floorGuardDepth = 0f;

            Vector4 shape = WallOcclusionGlobals.BuildShape(settings);

            Assert.That(shape.x, Is.LessThanOrEqualTo(0.95f));
            Assert.That(shape.y, Is.GreaterThan(0f));
            Assert.That(shape.z, Is.GreaterThan(0f));
        }

        [Test]
        public void BuildShape_PacksFloorGuardDepth()
        {
            WallOcclusionSettings settings = CreateSettings();
            settings.floorNormalThreshold = 0.4f;
            settings.behindFalloff = 2f;
            settings.floorGuardDepth = 0.8f;

            Vector4 shape = WallOcclusionGlobals.BuildShape(settings);

            Assert.That(shape.x, Is.EqualTo(0.4f).Within(1e-4f));
            Assert.That(shape.y, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(shape.z, Is.EqualTo(0.8f).Within(1e-4f));
        }

        [Test]
        public void Bind_SwapsMappedMaterialAndLeavesOthersAlone()
        {
            WallOcclusionSettings settings = CreateSettingsWithMapping(
                out Material source,
                out Material variant);
            Material unrelated = CreateMaterial("Unrelated");

            GameObject root = CreateObject("GeneratedMap");
            MeshRenderer mapped = CreateRenderer(root.transform, "Env_Wall_basic", source);
            MeshRenderer untouched = CreateRenderer(root.transform, "Env_floor", unrelated);

            WallOcclusionBindReport report =
                WallOcclusionMaterialBinder.Bind(settings, new[] { root.transform });

            Assert.That(mapped.sharedMaterial, Is.SameAs(variant));
            Assert.That(untouched.sharedMaterial, Is.SameAs(unrelated));
            Assert.That(report.SwappedSlots, Is.EqualTo(1));
            Assert.That(report.SwappedRenderers, Is.EqualTo(1));
            Assert.That(report.InspectedRenderers, Is.EqualTo(2));
        }

        [Test]
        public void Bind_IsIdempotent()
        {
            WallOcclusionSettings settings = CreateSettingsWithMapping(
                out Material source,
                out Material variant);
            GameObject root = CreateObject("GeneratedMap");
            MeshRenderer renderer = CreateRenderer(root.transform, "wall", source);

            WallOcclusionMaterialBinder.Bind(settings, new[] { root.transform });
            WallOcclusionBindReport second =
                WallOcclusionMaterialBinder.Bind(settings, new[] { root.transform });

            Assert.That(renderer.sharedMaterial, Is.SameAs(variant));
            Assert.That(second.SwappedSlots, Is.Zero);
            Assert.That(second.AlreadyBoundSlots, Is.EqualTo(1));
            Assert.That(second.BoundSlots, Is.EqualTo(1));
        }

        [Test]
        public void Bind_ReportsUnmappedMaterialsByName()
        {
            WallOcclusionSettings settings = CreateSettingsWithMapping(out _, out _);
            Material unmapped = CreateMaterial("MA_prop03");

            GameObject root = CreateObject("GeneratedMap");
            CreateRenderer(root.transform, "Env_Wall_odd", unmapped);

            WallOcclusionBindReport report =
                WallOcclusionMaterialBinder.Bind(settings, new[] { root.transform });

            Assert.That(report.UnmappedMaterialNames, Does.Contain("MA_prop03"));
            Assert.That(report.BoundSlots, Is.Zero);
        }

        [Test]
        public void Bind_SwapsEveryMappedSlotOnMultiMaterialRenderer()
        {
            WallOcclusionSettings settings = CreateSettingsWithMapping(
                out Material source,
                out Material variant);
            Material unrelated = CreateMaterial("Unrelated");

            GameObject root = CreateObject("GeneratedMap");
            MeshRenderer renderer = CreateRenderer(root.transform, "wall", source);
            renderer.sharedMaterials = new[] { source, unrelated, source };

            WallOcclusionMaterialBinder.Bind(settings, new[] { root.transform });

            Material[] result = renderer.sharedMaterials;
            Assert.That(result[0], Is.SameAs(variant));
            Assert.That(result[1], Is.SameAs(unrelated));
            Assert.That(result[2], Is.SameAs(variant));
        }

        [Test]
        public void Bind_DeduplicatesRepeatedRoots()
        {
            WallOcclusionSettings settings = CreateSettingsWithMapping(out Material source, out _);
            GameObject root = CreateObject("GeneratedMap");
            CreateRenderer(root.transform, "wall", source);

            WallOcclusionBindReport report = WallOcclusionMaterialBinder.Bind(
                settings,
                new[] { root.transform, root.transform });

            Assert.That(report.InspectedRenderers, Is.EqualTo(1));
        }

        [Test]
        public void Bind_WithoutMappingsDoesNothing()
        {
            WallOcclusionSettings settings = CreateSettings();
            Material material = CreateMaterial("Any");
            GameObject root = CreateObject("GeneratedMap");
            MeshRenderer renderer = CreateRenderer(root.transform, "wall", material);

            WallOcclusionBindReport report =
                WallOcclusionMaterialBinder.Bind(settings, new[] { root.transform });

            Assert.That(renderer.sharedMaterial, Is.SameAs(material));
            Assert.That(report.InspectedRenderers, Is.Zero);
        }

        [Test]
        public void Bind_FindsRenderersOnInactiveChildren()
        {
            WallOcclusionSettings settings = CreateSettingsWithMapping(
                out Material source,
                out Material variant);
            GameObject root = CreateObject("GeneratedMap");
            MeshRenderer renderer = CreateRenderer(root.transform, "wall", source);
            renderer.gameObject.SetActive(false);

            WallOcclusionMaterialBinder.Bind(settings, new[] { root.transform });

            Assert.That(renderer.sharedMaterial, Is.SameAs(variant));
        }

        [Test]
        public void Bind_SkipsRenderersExcludedByName()
        {
            // 경사면·참호 덮개는 벽과 같은 머티리얼을 쓰므로 매핑으로는 구분되지 않는다.
            WallOcclusionSettings settings = CreateSettingsWithMapping(
                out Material source,
                out Material variant);

            GameObject root = CreateObject("GeneratedMap");
            MeshRenderer wall = CreateRenderer(root.transform, "Env_Wall_basic", source);
            MeshRenderer slope = CreateRenderer(root.transform, "Env_slope_1by2fbx", source);

            WallOcclusionBindReport report =
                WallOcclusionMaterialBinder.Bind(settings, new[] { root.transform });

            Assert.That(wall.sharedMaterial, Is.SameAs(variant));
            Assert.That(slope.sharedMaterial, Is.SameAs(source));
            Assert.That(report.ExcludedRenderers, Is.EqualTo(1));
            Assert.That(report.SwappedSlots, Is.EqualTo(1));
        }

        [Test]
        public void Bind_ExcludesRenderersUnderNamedModelRoot()
        {
            // fbx를 그대로 인스턴스화하면 이름은 모델 루트에 있고 렌더러는 그 자식이다.
            WallOcclusionSettings settings = CreateSettingsWithMapping(out Material source, out _);

            GameObject root = CreateObject("GeneratedMap");
            GameObject model = CreateObject("Env_floor_Trenchcover");
            model.transform.SetParent(root.transform, false);
            MeshRenderer mesh = CreateRenderer(model.transform, "default", source);

            WallOcclusionBindReport report =
                WallOcclusionMaterialBinder.Bind(settings, new[] { root.transform });

            Assert.That(mesh.sharedMaterial, Is.SameAs(source));
            Assert.That(report.ExcludedRenderers, Is.EqualTo(1));
        }

        private GameObject CreateObject(string name)
        {
            var gameObject = new GameObject(name);
            objectsToDestroy.Add(gameObject);
            return gameObject;
        }

        private Material CreateMaterial(string name)
        {
            Shader shader = Shader.Find("Sprites/Default");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader) { name = name };
            objectsToDestroy.Add(material);
            return material;
        }

        private MeshRenderer CreateRenderer(Transform parent, string name, Material material)
        {
            GameObject child = CreateObject(name);
            child.transform.SetParent(parent, false);
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        private WallOcclusionSettings CreateSettings()
        {
            var settings = ScriptableObject.CreateInstance<WallOcclusionSettings>();
            objectsToDestroy.Add(settings);
            return settings;
        }

        private WallOcclusionSettings CreateSettingsWithMapping(
            out Material source,
            out Material variant)
        {
            WallOcclusionSettings settings = CreateSettings();
            source = CreateMaterial("MA_Wall_basic");
            variant = CreateMaterial("MA_Wall_basic_Occlusion");
            settings.ConfigureMaterialMappings(
                new[] { source },
                new[] { variant });
            return settings;
        }
    }
}
