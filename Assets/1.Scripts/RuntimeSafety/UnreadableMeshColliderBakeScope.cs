using System;
using System.Collections.Generic;
using UnityEngine;

namespace VeyTrace.RuntimeSafety
{
    public sealed class UnreadableMeshColliderBakeScope : IDisposable
    {
        private readonly List<MeshCollider> disabledSources = new();
        private readonly List<BoxCollider> proxies = new();
        private bool disposed;

        private UnreadableMeshColliderBakeScope()
        {
        }

        public int ProxyCount => proxies.Count;

        public static UnreadableMeshColliderBakeScope BeginLoadedScenes()
        {
            MeshCollider[] colliders = UnityEngine.Object.FindObjectsByType<MeshCollider>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            return Begin(colliders);
        }

        public static UnreadableMeshColliderBakeScope Begin(
            IEnumerable<MeshCollider> colliders)
        {
            var scope = new UnreadableMeshColliderBakeScope();
            if (colliders == null)
                return scope;

            foreach (MeshCollider source in colliders)
                scope.TryAddProxy(source);

            return scope;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            for (int i = 0; i < proxies.Count; i++)
            {
                BoxCollider proxy = proxies[i];
                if (proxy == null)
                    continue;

                proxy.enabled = false;
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(proxy);
                else
                    UnityEngine.Object.DestroyImmediate(proxy);
            }

            for (int i = 0; i < disabledSources.Count; i++)
            {
                MeshCollider source = disabledSources[i];
                if (source != null)
                    source.enabled = true;
            }

            proxies.Clear();
            disabledSources.Clear();
        }

        private void TryAddProxy(MeshCollider source)
        {
            if (source == null ||
                !source.enabled ||
                !source.gameObject.activeInHierarchy ||
                source.isTrigger ||
                source.sharedMesh == null ||
                source.sharedMesh.isReadable)
                return;

            Mesh mesh = source.sharedMesh;
            BoxCollider proxy = source.gameObject.AddComponent<BoxCollider>();
            proxy.center = mesh.bounds.center;
            proxy.size = mesh.bounds.size;
            proxy.sharedMaterial = source.sharedMaterial;
            proxy.isTrigger = false;
            proxy.includeLayers = source.includeLayers;
            proxy.excludeLayers = source.excludeLayers;

            source.enabled = false;
            disabledSources.Add(source);
            proxies.Add(proxy);
        }
    }
}
