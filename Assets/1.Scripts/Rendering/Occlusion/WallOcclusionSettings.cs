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
        [Range(0.1f, 1f)] public float screenCapsuleRadiusScale = 0.8f;
        [Min(0f)] public float holePaddingPixels = 2f;
        [Range(0f, 3f)] public float featherRadiusScale = 0.5f;
        [Min(1f)] public float minFeatherPixels = 32f;
        [Min(1f)] public float maxFeatherPixels = 192f;
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

        public float CalculateFeatherPixels(float projectedRadiusPixels)
        {
            float minimum = Mathf.Max(1f, minFeatherPixels);
            float maximum = Mathf.Max(minimum, maxFeatherPixels);
            return Mathf.Clamp(
                Mathf.Max(0f, projectedRadiusPixels) * Mathf.Clamp(featherRadiusScale, 0f, 3f),
                minimum,
                maximum);
        }

        private void OnValidate()
        {
            maxCastHits = Mathf.Clamp(maxCastHits, 8, 2048);
            castPadding = Mathf.Max(0f, castPadding);
            screenCapsuleRadiusScale = Mathf.Clamp(screenCapsuleRadiusScale, 0.1f, 1f);
            holePaddingPixels = Mathf.Max(0f, holePaddingPixels);
            featherRadiusScale = Mathf.Clamp(featherRadiusScale, 0f, 3f);
            minFeatherPixels = Mathf.Max(1f, minFeatherPixels);
            maxFeatherPixels = Mathf.Max(minFeatherPixels, maxFeatherPixels);
            behindFalloff = Mathf.Max(0.01f, behindFalloff);
            fadeInDuration = Mathf.Max(0.01f, fadeInDuration);
            releaseGraceDuration = Mathf.Max(0f, releaseGraceDuration);
            restoreDuration = Mathf.Max(0.01f, restoreDuration);
            risingProgress = Mathf.Clamp01(risingProgress);
            fallingProgress = Mathf.Clamp01(fallingProgress);
        }
    }
}
