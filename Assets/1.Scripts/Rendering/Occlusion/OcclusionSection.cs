using System;
using System.Collections.Generic;
using UnityEngine;

namespace VeyTrace.Rendering.Occlusion
{
    [DisallowMultipleComponent]
    public sealed class OcclusionSection : MonoBehaviour
    {
        [SerializeField] private Renderer[] renderers = Array.Empty<Renderer>();
        [SerializeField] private Collider[] colliders = Array.Empty<Collider>();

        private ElevationLevel level;

        public IReadOnlyList<Renderer> Renderers => renderers;
        public IReadOnlyList<Collider> Colliders => colliders;
        public ElevationLevel Level => level != null ? level : level = GetComponentInParent<ElevationLevel>();

        public bool IsRuntimeValid(out string reason)
        {
            ElevationLevel ownerLevel = Level;
            if (ownerLevel == null || ownerLevel.ContentRoot == null ||
                !transform.IsChildOf(ownerLevel.ContentRoot))
            {
                reason = "OcclusionSection must belong to one ElevationLevel/Content.";
                return false;
            }

            if (renderers == null || CountAlive(renderers) == 0)
            {
                reason = "OcclusionSection has no registered Renderer.";
                return false;
            }

            if (colliders == null || CountAlive(colliders) == 0)
            {
                reason = "OcclusionSection has no registered Collider.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public void ConfigureAuthoring(Renderer[] newRenderers, Collider[] newColliders)
        {
            renderers = newRenderers ?? Array.Empty<Renderer>();
            colliders = newColliders ?? Array.Empty<Collider>();
            level = null;
        }

        private void OnEnable()
        {
            level = GetComponentInParent<ElevationLevel>();
            WallOcclusionRegistry.Register(this);
        }

        private void OnDisable()
        {
            WallOcclusionRegistry.Unregister(this);
        }

        private static int CountAlive<T>(T[] values) where T : UnityEngine.Object
        {
            int count = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != null)
                    count++;
            }

            return count;
        }
    }
}
