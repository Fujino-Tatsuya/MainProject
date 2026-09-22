using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace VeyTrace.Rendering.Occlusion.Tests
{
    // EditMode 에서 검증하지 않는 것 — Play 로 본다:
    //  - 시간 기반 페이드(Update 가 돌지 않는다)
    //  - OnDestroy 의 원본 복원과 인스턴스 해제. EditMode 에서는 Awake 가 실행되지 않아
    //    Unity 가 이 컴포넌트를 깨어난 적 없는 것으로 보고 OnDestroy 도 부르지 않는다.
    //    검증하려면 프로덕션에서 정리 로직을 콜백 밖으로 빼내야 하는데, 그만한 값이 없다.
    public sealed class WallTransparencyGroupTests
    {
        private const string OcclusionShaderName =
            "Project/Environment/Wall Occlusion Dither";

        private static readonly FieldInfo TargetRenderersField =
            typeof(WallTransparencyGroup).GetField(
                "targetRenderers",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo RequestCountField =
            typeof(WallTransparencyGroup).GetField(
                "_transparencyRequestCount",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly List<GameObject> gameObjectsToDestroy = new();
        private readonly List<Material> sourceMaterialsToDestroy = new();
        private readonly List<Material> runtimeMaterialsToDestroy = new();
        private readonly Dictionary<Renderer, Material[]> originalMaterialsByRenderer = new();

        [TearDown]
        public void TearDown()
        {
            // 실패한 테스트도 그룹의 OnDestroy가 런타임 머티리얼에 Destroy를 예약하지 않도록
            // 원본을 먼저 복원하고 인스턴스를 즉시 정리한다.
            foreach (KeyValuePair<Renderer, Material[]> pair in originalMaterialsByRenderer)
            {
                if (pair.Key != null)
                    pair.Key.sharedMaterials = pair.Value;
            }

            for (int i = runtimeMaterialsToDestroy.Count - 1; i >= 0; i--)
            {
                if (runtimeMaterialsToDestroy[i] != null)
                    Object.DestroyImmediate(runtimeMaterialsToDestroy[i]);
            }

            for (int i = gameObjectsToDestroy.Count - 1; i >= 0; i--)
            {
                if (gameObjectsToDestroy[i] != null)
                    Object.DestroyImmediate(gameObjectsToDestroy[i]);
            }

            for (int i = sourceMaterialsToDestroy.Count - 1; i >= 0; i--)
            {
                if (sourceMaterialsToDestroy[i] != null)
                    Object.DestroyImmediate(sourceMaterialsToDestroy[i]);
            }

            originalMaterialsByRenderer.Clear();
            runtimeMaterialsToDestroy.Clear();
            gameObjectsToDestroy.Clear();
            sourceMaterialsToDestroy.Clear();
        }

        [Test]
        public void 투명화_요청은_모든_획득이_해제될_때까지_유지된다()
        {
            WallTransparencyGroup group = CreateGroup();

            group.AcquireTransparency();
            group.AcquireTransparency();
            group.ReleaseTransparency();

            Assert.That(GetRequestCount(group), Is.EqualTo(1));

            group.ReleaseTransparency();

            Assert.That(GetRequestCount(group), Is.Zero);
        }

        [Test]
        public void 짝이_없는_해제는_참조_카운트를_음수로_만들지_않는다()
        {
            WallTransparencyGroup group = CreateGroup();
            LogAssert.Expect(
                LogType.Warning,
                "[WallTransparencyGroup] 짝이 없는 투명화 해제 요청을 무시합니다.");

            group.ReleaseTransparency();

            Assert.That(GetRequestCount(group), Is.Zero);
        }

        [Test]
        public void 같은_원본을_쓰는_렌더러들은_하나의_인스턴스를_공유한다()
        {
            Material source = CreateOcclusionMaterial("공유 원본");
            MeshRenderer first = CreateRenderer("첫 번째 벽", source);
            MeshRenderer second = CreateRenderer("두 번째 벽", source);

            CreateGroup(first, second);

            Material instance = first.sharedMaterial;
            TrackRuntimeMaterial(instance);
            Assert.That(instance, Is.Not.SameAs(source));
            Assert.That(second.sharedMaterial, Is.SameAs(instance));
        }

        [Test]
        public void 서로_다른_원본마다_별도의_인스턴스를_만든다()
        {
            Material firstSource = CreateOcclusionMaterial("첫 번째 원본");
            Material secondSource = CreateOcclusionMaterial("두 번째 원본");
            MeshRenderer renderer = CreateRenderer(
                "두 원본 벽",
                firstSource,
                secondSource);

            CreateGroup(renderer);

            Material[] instances = renderer.sharedMaterials;
            TrackRuntimeMaterial(instances[0]);
            TrackRuntimeMaterial(instances[1]);
            Assert.That(instances[0], Is.Not.SameAs(firstSource));
            Assert.That(instances[1], Is.Not.SameAs(secondSource));
            Assert.That(instances[1], Is.Not.SameAs(instances[0]));
        }

        [Test]
        public void 여러_슬롯을_각각_교체하고_불투명도_프로퍼티가_없는_슬롯은_보존한다()
        {
            Material source = CreateOcclusionMaterial("교체 대상");
            Material unsupported = CreateUnsupportedMaterial("교체 제외");
            MeshRenderer renderer = CreateRenderer(
                "다중 슬롯 벽",
                source,
                unsupported,
                source);

            CreateGroup(renderer);

            Material[] result = renderer.sharedMaterials;
            TrackRuntimeMaterial(result[0]);
            Assert.That(result[0], Is.Not.SameAs(source));
            Assert.That(result[1], Is.SameAs(unsupported));
            Assert.That(result[2], Is.SameAs(result[0]));
        }

        [Test]
        public void 머티리얼을_교체해도_프로퍼티_블록은_설정하지_않는다()
        {
            Material source = CreateOcclusionMaterial("프로퍼티 블록 검사 원본");
            MeshRenderer renderer = CreateRenderer("프로퍼티 블록 검사 벽", source);

            CreateGroup(renderer);

            TrackRuntimeMaterial(renderer.sharedMaterial);
            var propertyBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(propertyBlock);
            Assert.That(propertyBlock.isEmpty, Is.True);
        }

        private WallTransparencyGroup CreateGroup(params Renderer[] renderers)
        {
            Assert.That(TargetRenderersField, Is.Not.Null);
            GameObject gameObject = CreateObject("벽 투명화 그룹");
            gameObject.SetActive(false);
            WallTransparencyGroup group = gameObject.AddComponent<WallTransparencyGroup>();
            TargetRenderersField.SetValue(group, renderers);
            gameObject.SetActive(true);

            // EditMode 에서는 일반 MonoBehaviour 의 Awake 가 실행되지 않으므로
            // (ExecuteAlways 가 아니다) 머티리얼 초기화가 일어나지 않는다. 프로덕션이 갖고 있는
            // 지연 초기화 경로(AcquireTransparency)를 한 번 태워 초기화만 유발하고, 참조
            // 카운트는 곧바로 되돌려 각 테스트가 0에서 시작하게 한다.
            group.AcquireTransparency();
            group.ReleaseTransparency();
            Assert.That(GetRequestCount(group), Is.Zero);

            return group;
        }

        private MeshRenderer CreateRenderer(string name, params Material[] materials)
        {
            GameObject gameObject = CreateObject(name);
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            originalMaterialsByRenderer.Add(renderer, materials);
            return renderer;
        }

        private GameObject CreateObject(string name)
        {
            var gameObject = new GameObject(name);
            gameObjectsToDestroy.Add(gameObject);
            return gameObject;
        }

        private Material CreateOcclusionMaterial(string name)
        {
            Shader shader = Shader.Find(OcclusionShaderName);
            Assert.That(shader, Is.Not.Null);
            return CreateMaterial(shader, name);
        }

        private Material CreateUnsupportedMaterial(string name)
        {
            Shader shader = Shader.Find("Sprites/Default");
            Assert.That(shader, Is.Not.Null);
            Material material = CreateMaterial(shader, name);
            Assert.That(material.HasProperty("_WallOcclusionOpacity"), Is.False);
            return material;
        }

        private Material CreateMaterial(Shader shader, string name)
        {
            var material = new Material(shader) { name = name };
            sourceMaterialsToDestroy.Add(material);
            return material;
        }

        private void TrackRuntimeMaterial(Material material)
        {
            if (material != null && !runtimeMaterialsToDestroy.Contains(material))
                runtimeMaterialsToDestroy.Add(material);
        }

        private static int GetRequestCount(WallTransparencyGroup group)
        {
            Assert.That(RequestCountField, Is.Not.Null);
            return (int)RequestCountField.GetValue(group);
        }
    }
}
