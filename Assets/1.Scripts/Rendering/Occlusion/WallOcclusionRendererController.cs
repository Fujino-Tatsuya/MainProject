using System.Collections.Generic;
using UnityEngine;

namespace VeyTrace.Rendering.Occlusion
{
    public sealed class WallOcclusionRendererController
    {
        private sealed class RendererState
        {
            public Renderer Renderer;
            public MaterialPropertyBlock OriginalBlock;
            public MaterialPropertyBlock WorkingBlock;
            public float Strength;
            public float ReleaseTime;
        }

        private readonly WallOcclusionSettings settings;
        private readonly HashSet<Renderer> desiredRenderers = new();
        private readonly Dictionary<Renderer, RendererState> states = new();
        private readonly List<Renderer> iterationBuffer = new();
        private readonly HashSet<int> warnedOwners = new();

        public int ActiveRendererCount => states.Count;

        public WallOcclusionRendererController(WallOcclusionSettings settings)
        {
            this.settings = settings;
        }

        public void BeginFrame()
        {
            desiredRenderers.Clear();
        }

        public bool AddLevel(ElevationLevel level)
        {
            return level != null && TryAddOwner(level, level.ContentRenderers);
        }

        public bool AddSection(OcclusionSection section)
        {
            return section != null && TryAddOwner(section, section.Renderers);
        }

        public void EndFrame(float deltaTime)
        {
            foreach (Renderer renderer in desiredRenderers)
            {
                if (renderer == null || states.ContainsKey(renderer))
                    continue;

                if (TryCreateState(renderer, out RendererState state))
                    states.Add(renderer, state);
            }

            iterationBuffer.Clear();
            iterationBuffer.AddRange(states.Keys);
            for (int i = 0; i < iterationBuffer.Count; i++)
            {
                Renderer renderer = iterationBuffer[i];
                if (renderer == null || !states.TryGetValue(renderer, out RendererState state))
                {
                    states.Remove(renderer);
                    continue;
                }

                bool desired = desiredRenderers.Contains(renderer);
                if (desired)
                {
                    state.ReleaseTime = 0f;
                    state.Strength = Mathf.MoveTowards(
                        state.Strength,
                        1f,
                        deltaTime / Mathf.Max(0.01f, settings.fadeInDuration));
                    ApplyStrength(state, state.Strength);
                    continue;
                }

                state.ReleaseTime += deltaTime;
                if (state.ReleaseTime <= settings.releaseGraceDuration)
                {
                    ApplyStrength(state, state.Strength);
                    continue;
                }

                state.Strength = Mathf.MoveTowards(
                    state.Strength,
                    0f,
                    deltaTime / Mathf.Max(0.01f, settings.restoreDuration));
                ApplyStrength(state, state.Strength);
                if (state.Strength > 0f)
                    continue;

                RestoreRenderer(state);
                states.Remove(renderer);
            }
        }

        public void RestoreAllImmediate()
        {
            foreach (RendererState state in states.Values)
            {
                if (state.Renderer != null)
                    RestoreRenderer(state);
            }

            states.Clear();
            desiredRenderers.Clear();
            iterationBuffer.Clear();
        }

        private bool TryAddOwner(Object owner, IReadOnlyList<Renderer> renderers)
        {
            if (settings == null || renderers == null || renderers.Count == 0)
            {
                WarnOwnerOnce(owner, "no registered Renderer or settings asset is missing");
                return false;
            }

            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                Material[] materials = renderer.sharedMaterials;
                if (materials.Length == 0)
                {
                    WarnOwnerOnce(owner, $"Renderer '{renderer.name}' has no material");
                    return false;
                }

                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material material = materials[slot];
                    if (material == null ||
                        !material.HasProperty(WallOcclusionGlobals.StrengthPropertyId))
                    {
                        string materialName = material != null ? material.name : "<null>";
                        WarnOwnerOnce(
                            owner,
                            $"Renderer '{renderer.name}' uses material '{materialName}' without " +
                            "_WallOcclusionStrength support");
                        return false;
                    }
                }
            }

            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                    desiredRenderers.Add(renderer);
            }

            return true;
        }

        private bool TryCreateState(Renderer renderer, out RendererState state)
        {
            state = null;
            var originalBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(originalBlock);
            state = new RendererState
            {
                Renderer = renderer,
                OriginalBlock = originalBlock,
                WorkingBlock = new MaterialPropertyBlock(),
                Strength = 0f,
                ReleaseTime = 0f
            };
            ApplyStrength(state, 0f);
            return true;
        }

        private static void ApplyStrength(RendererState state, float strength)
        {
            state.Renderer.GetPropertyBlock(state.WorkingBlock);
            state.WorkingBlock.SetFloat(WallOcclusionGlobals.StrengthPropertyId, Mathf.Clamp01(strength));
            state.Renderer.SetPropertyBlock(state.WorkingBlock);
        }

        private static void RestoreOriginalBlock(RendererState state)
        {
            state.Renderer.SetPropertyBlock(state.OriginalBlock);
        }

        private static void RestoreRenderer(RendererState state)
        {
            RestoreOriginalBlock(state);
        }

        private void WarnOwnerOnce(Object owner, string reason)
        {
            if (owner == null || !warnedOwners.Add(owner.GetInstanceID()))
                return;

            Debug.LogWarning(
                $"[WallOcclusion] '{owner.name}' is kept opaque: {reason}.",
                owner);
        }
    }
}
