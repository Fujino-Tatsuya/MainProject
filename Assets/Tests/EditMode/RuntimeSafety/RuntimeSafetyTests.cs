using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VeyTrace.RuntimeSafety.Tests
{
    public sealed class RuntimeSafetyTests
    {
        private GameObject firstRoot;
        private GameObject secondRoot;

        [TearDown]
        public void TearDown()
        {
            RuntimeSceneServiceCoordinator.RestoreAll();
            if (firstRoot != null)
                Object.DestroyImmediate(firstRoot);
            if (secondRoot != null)
                Object.DestroyImmediate(secondRoot);
        }

        [Test]
        public void Reconcile_LeavesExactlyOneEnabledServiceOfEachType()
        {
            firstRoot = new GameObject("FirstServices");
            firstRoot.AddComponent<AudioListener>();
            firstRoot.AddComponent<EventSystem>();
            secondRoot = new GameObject("SecondServices");
            secondRoot.AddComponent<AudioListener>();
            secondRoot.AddComponent<EventSystem>();

            RuntimeSceneServiceReport report =
                RuntimeSceneServiceCoordinator.Reconcile();

            Assert.That(report.EnabledAudioListeners, Is.EqualTo(1));
            Assert.That(report.EnabledEventSystems, Is.EqualTo(1));
            Assert.That(report.SuppressedAudioListeners, Is.GreaterThanOrEqualTo(1));
            Assert.That(report.SuppressedEventSystems, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void ScenePriority_PrefersGameplayThenLobbyThenLoading()
        {
            int gameplay =
                RuntimeSceneServiceCoordinator.GetScenePriority("MapScene");
            int lobby =
                RuntimeSceneServiceCoordinator.GetScenePriority("3.LobbyScene");
            int loading =
                RuntimeSceneServiceCoordinator.GetScenePriority("LoadingScene");

            Assert.That(gameplay, Is.GreaterThan(lobby));
            Assert.That(lobby, Is.GreaterThan(loading));
        }

        [Test]
        public void UnreadableMeshColliderBakeScope_UsesTemporaryBoxAndRestoresSource()
        {
            firstRoot = new GameObject("UnreadableMesh");
            Mesh mesh = new()
            {
                vertices = new[]
                {
                    new Vector3(-2f, -1f, -3f),
                    new Vector3(2f, -1f, -3f),
                    new Vector3(0f, 1f, 3f)
                },
                triangles = new[] { 0, 1, 2 }
            };
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            MeshCollider source = firstRoot.AddComponent<MeshCollider>();
            source.sharedMesh = mesh;

            using (UnreadableMeshColliderBakeScope scope =
                   UnreadableMeshColliderBakeScope.Begin(
                       new[] { source }))
            {
                BoxCollider proxy = firstRoot.GetComponent<BoxCollider>();
                Assert.That(scope.ProxyCount, Is.EqualTo(1));
                Assert.That(source.enabled, Is.False);
                Assert.That(proxy, Is.Not.Null);
                Assert.That(proxy.enabled, Is.True);
                Assert.That(proxy.center, Is.EqualTo(mesh.bounds.center));
                Assert.That(proxy.size, Is.EqualTo(mesh.bounds.size));
            }

            Assert.That(source.enabled, Is.True);
            BoxCollider restoredProxy = firstRoot.GetComponent<BoxCollider>();
            Assert.That(
                restoredProxy == null || !restoredProxy.enabled,
                Is.True);

            Object.DestroyImmediate(mesh);
        }
    }
}
