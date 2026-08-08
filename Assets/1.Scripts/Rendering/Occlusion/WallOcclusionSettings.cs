using System;
using UnityEngine;

namespace VeyTrace.Rendering.Occlusion
{
    [CreateAssetMenu(
        fileName = "WallOcclusionSettings",
        menuName = "Rendering/Wall Occlusion Settings")]
    public sealed class WallOcclusionSettings : ScriptableObject
    {
        [Header("Selection")]
        [Tooltip("Camera-to-player SphereCast layer mask. Registration remains the final authority.")]
        public LayerMask castMask = ~0;

        [Min(8)] public int maxCastHits = 256;
        [Min(0f)] public float castPadding = 0.08f;

        [Header("Screen Capsule")]
        [Min(0f)] public float holePaddingPixels = 8f;
        [Min(1f)] public float featherPixels = 42f;
        [Min(0.01f)] public float behindFalloff = 1.5f;

        [Header("Fade Timing")]
        [Min(0.01f)] public float fadeInDuration = 0.1f;
        [Min(0f)] public float releaseGraceDuration = 0.1f;
        [Min(0.01f)] public float restoreDuration = 0.2f;

        [Header("Elevation")]
        [Range(0f, 1f)] public float risingProgress = 0.2f;
        [Range(0f, 1f)] public float fallingProgress = 0.6f;

        [Header("Debug")]
        public bool drawRuntimeGizmos;

        [Header("Registered Material Variants")]
        [SerializeField] private Material[] sourceMaterials = Array.Empty<Material>();
        [SerializeField] private Material[] occlusionMaterials = Array.Empty<Material>();

        public Material[] SourceMaterials => sourceMaterials;
        public Material[] OcclusionMaterials => occlusionMaterials;

        public bool HasValidMaterialMappings =>
            sourceMaterials != null &&
            occlusionMaterials != null &&
            sourceMaterials.Length > 0 &&
            sourceMaterials.Length == occlusionMaterials.Length;

        public bool TryResolvePair(Material current, out Material source, out Material variant)
        {
            source = null;
            variant = null;
            if (current == null || !HasValidMaterialMappings)
                return false;

            for (int i = 0; i < sourceMaterials.Length; i++)
            {
                if (current != sourceMaterials[i] && current != occlusionMaterials[i])
                    continue;

                source = sourceMaterials[i];
                variant = occlusionMaterials[i];
                return source != null && variant != null;
            }

            return false;
        }

        public bool TryResolveOcclusionMaterial(Material current, out Material variant)
        {
            return TryResolvePair(current, out _, out variant);
        }

        public void ConfigureMaterialMappings(Material[] sources, Material[] variants)
        {
            sourceMaterials = sources ?? Array.Empty<Material>();
            occlusionMaterials = variants ?? Array.Empty<Material>();
        }

        private void OnValidate()
        {
            maxCastHits = Mathf.Clamp(maxCastHits, 8, 2048);
            castPadding = Mathf.Max(0f, castPadding);
            holePaddingPixels = Mathf.Max(0f, holePaddingPixels);
            featherPixels = Mathf.Max(1f, featherPixels);
            behindFalloff = Mathf.Max(0.01f, behindFalloff);
            fadeInDuration = Mathf.Max(0.01f, fadeInDuration);
            releaseGraceDuration = Mathf.Max(0f, releaseGraceDuration);
            restoreDuration = Mathf.Max(0.01f, restoreDuration);
            risingProgress = Mathf.Clamp01(risingProgress);
            fallingProgress = Mathf.Clamp01(fallingProgress);
        }
    }
}
