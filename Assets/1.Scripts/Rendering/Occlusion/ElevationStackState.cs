using System.Collections.Generic;
using UnityEngine;

namespace VeyTrace.Rendering.Occlusion
{
    public enum OcclusionVerticalMotion
    {
        Stable,
        Rising,
        Falling
    }

    public sealed class ElevationStackState
    {
        private readonly List<ElevationLevel> orderedLevels = new();
        private ElevationLevel activeLevel;
        private int registryVersion = -1;

        public ElevationStack Stack { get; }
        public ElevationLevel ActiveLevel => activeLevel;
        public bool IsInside { get; private set; }

        public ElevationStackState(ElevationStack stack)
        {
            Stack = stack;
        }

        public void Update(
            Vector3 playerPosition,
            float footWorldY,
            OcclusionVerticalMotion motion,
            bool isGrounded,
            bool hasGroundSensor,
            ElevationLevel groundedLevel,
            float risingProgress,
            float fallingProgress)
        {
            RefreshLevelsIfNeeded();
            if (orderedLevels.Count == 0)
            {
                Clear();
                return;
            }

            IsInside = ContainsAnyXZ(playerPosition);
            if (!IsInside)
            {
                activeLevel = null;
                return;
            }

            if (isGrounded && groundedLevel != null && groundedLevel.Stack == Stack &&
                orderedLevels.Contains(groundedLevel))
            {
                activeLevel = groundedLevel;
                return;
            }

            int activeIndex = orderedLevels.IndexOf(activeLevel);
            if (activeIndex < 0)
            {
                activeIndex = ResolveInitialIndex(footWorldY, risingProgress);
                activeLevel = orderedLevels[activeIndex];
                return;
            }

            risingProgress = Mathf.Clamp01(risingProgress);
            fallingProgress = Mathf.Clamp01(fallingProgress);

            if (motion == OcclusionVerticalMotion.Rising && (!hasGroundSensor || isGrounded))
            {
                while (activeIndex + 1 < orderedLevels.Count)
                {
                    float lower = orderedLevels[activeIndex].ReferenceWorldY;
                    float upper = orderedLevels[activeIndex + 1].ReferenceWorldY;
                    float threshold = Mathf.Lerp(lower, upper, risingProgress);
                    if (footWorldY < threshold - 0.001f)
                        break;
                    activeIndex++;
                }
            }
            else if (motion == OcclusionVerticalMotion.Falling)
            {
                while (activeIndex > 0)
                {
                    float lower = orderedLevels[activeIndex - 1].ReferenceWorldY;
                    float upper = orderedLevels[activeIndex].ReferenceWorldY;
                    float threshold = Mathf.Lerp(upper, lower, fallingProgress);
                    if (footWorldY > threshold + 0.001f)
                        break;
                    activeIndex--;
                }
            }

            activeLevel = orderedLevels[activeIndex];
        }

        public bool IsAboveActiveLevel(ElevationLevel level)
        {
            if (level == null || level.Stack != Stack)
                return false;
            if (!IsInside || activeLevel == null)
                return true;
            return level.ReferenceWorldY > activeLevel.ReferenceWorldY + 0.001f;
        }

        public void Invalidate()
        {
            registryVersion = -1;
        }

        private void RefreshLevelsIfNeeded()
        {
            if (registryVersion == WallOcclusionRegistry.Version)
                return;

            orderedLevels.Clear();
            IReadOnlyList<ElevationLevel> levels = WallOcclusionRegistry.GetLevels(Stack);
            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i] != null && levels[i].isActiveAndEnabled)
                    orderedLevels.Add(levels[i]);
            }

            orderedLevels.Sort((a, b) => a.ReferenceWorldY.CompareTo(b.ReferenceWorldY));
            if (activeLevel != null && !orderedLevels.Contains(activeLevel))
                activeLevel = null;
            registryVersion = WallOcclusionRegistry.Version;
        }

        private bool ContainsAnyXZ(Vector3 playerPosition)
        {
            for (int i = 0; i < orderedLevels.Count; i++)
            {
                if (orderedLevels[i].ContainsXZ(playerPosition))
                    return true;
            }

            return false;
        }

        private int ResolveInitialIndex(float footWorldY, float risingProgress)
        {
            int index = 0;
            float progress = Mathf.Clamp01(risingProgress);
            while (index + 1 < orderedLevels.Count)
            {
                float lower = orderedLevels[index].ReferenceWorldY;
                float upper = orderedLevels[index + 1].ReferenceWorldY;
                if (footWorldY < Mathf.Lerp(lower, upper, progress))
                    break;
                index++;
            }

            return index;
        }

        private void Clear()
        {
            activeLevel = null;
            IsInside = false;
        }
    }
}
