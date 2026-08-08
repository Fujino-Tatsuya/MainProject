using System.Collections.Generic;
using UnityEngine;

namespace VeyTrace.Rendering.Occlusion
{
    public static class WallOcclusionRegistry
    {
        private static readonly Dictionary<Collider, ElevationLevel> LevelsByCollider = new();
        private static readonly Dictionary<Collider, OcclusionSection> SectionsByCollider = new();
        private static readonly Dictionary<ElevationStack, List<ElevationLevel>> LevelsByStack = new();
        private static readonly HashSet<int> WarnedInvalidObjects = new();

        public static int Version { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            LevelsByCollider.Clear();
            SectionsByCollider.Clear();
            LevelsByStack.Clear();
            WarnedInvalidObjects.Clear();
            Version = 0;
        }

        public static void Register(ElevationLevel level)
        {
            string reason = "ElevationLevel reference is null.";
            if (level == null || !level.IsRuntimeValid(out reason))
            {
                WarnOnce(level, reason);
                return;
            }

            ElevationStack stack = level.Stack;
            if (!LevelsByStack.TryGetValue(stack, out List<ElevationLevel> levels))
            {
                levels = new List<ElevationLevel>();
                LevelsByStack.Add(stack, levels);
            }

            for (int i = 0; i < levels.Count; i++)
            {
                ElevationLevel existingLevel = levels[i];
                if (existingLevel != null && existingLevel != level &&
                    Mathf.Abs(existingLevel.ReferenceWorldY - level.ReferenceWorldY) < 0.001f)
                {
                    WarnOnce(
                        level,
                        $"ElevationLevel '{existingLevel.name}' already uses the same reference Y.");
                    return;
                }
            }

            if (!levels.Contains(level))
                levels.Add(level);

            IReadOnlyList<Collider> colliders = level.ContentColliders;
            for (int i = 0; i < colliders.Count; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                    continue;

                if (LevelsByCollider.TryGetValue(collider, out ElevationLevel existing) && existing != level)
                {
                    WarnOnce(
                        level,
                        $"Collider '{collider.name}' already belongs to ElevationLevel '{existing.name}'.");
                    continue;
                }

                LevelsByCollider[collider] = level;
            }

            levels.Sort(CompareLevelHeight);
            Version++;
        }

        public static void Unregister(ElevationLevel level)
        {
            if (level == null)
                return;

            IReadOnlyList<Collider> colliders = level.ContentColliders;
            for (int i = 0; i < colliders.Count; i++)
            {
                Collider collider = colliders[i];
                if (collider != null && LevelsByCollider.TryGetValue(collider, out ElevationLevel owner) && owner == level)
                    LevelsByCollider.Remove(collider);
            }

            ElevationStack stack = level.Stack;
            if (stack != null && LevelsByStack.TryGetValue(stack, out List<ElevationLevel> levels))
            {
                levels.Remove(level);
                if (levels.Count == 0)
                    LevelsByStack.Remove(stack);
            }

            Version++;
        }

        public static void Register(OcclusionSection section)
        {
            string reason = "OcclusionSection reference is null.";
            if (section == null || !section.IsRuntimeValid(out reason))
            {
                WarnOnce(section, reason);
                return;
            }

            IReadOnlyList<Collider> colliders = section.Colliders;
            for (int i = 0; i < colliders.Count; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                    continue;

                if (SectionsByCollider.TryGetValue(collider, out OcclusionSection existing) && existing != section)
                {
                    WarnOnce(
                        section,
                        $"Collider '{collider.name}' already belongs to OcclusionSection '{existing.name}'.");
                    continue;
                }

                SectionsByCollider[collider] = section;
            }

            Version++;
        }

        public static void Unregister(OcclusionSection section)
        {
            if (section == null)
                return;

            IReadOnlyList<Collider> colliders = section.Colliders;
            for (int i = 0; i < colliders.Count; i++)
            {
                Collider collider = colliders[i];
                if (collider != null && SectionsByCollider.TryGetValue(collider, out OcclusionSection owner) && owner == section)
                    SectionsByCollider.Remove(collider);
            }

            Version++;
        }

        public static bool TryGetLevel(Collider collider, out ElevationLevel level)
        {
            if (collider != null && LevelsByCollider.TryGetValue(collider, out level) && level != null)
                return true;

            level = null;
            return false;
        }

        public static bool TryGetSection(Collider collider, out OcclusionSection section)
        {
            if (collider != null && SectionsByCollider.TryGetValue(collider, out section) && section != null)
                return true;

            section = null;
            return false;
        }

        public static IEnumerable<KeyValuePair<ElevationStack, List<ElevationLevel>>> EnumerateStacks()
        {
            return LevelsByStack;
        }

        public static IReadOnlyList<ElevationLevel> GetLevels(ElevationStack stack)
        {
            return stack != null && LevelsByStack.TryGetValue(stack, out List<ElevationLevel> levels)
                ? levels
                : System.Array.Empty<ElevationLevel>();
        }

        private static int CompareLevelHeight(ElevationLevel a, ElevationLevel b)
        {
            if (a == null)
                return b == null ? 0 : -1;
            if (b == null)
                return 1;
            return a.ReferenceWorldY.CompareTo(b.ReferenceWorldY);
        }

        private static void WarnOnce(Object context, string reason)
        {
            if (context == null)
                return;

            int id = context.GetInstanceID();
            if (!WarnedInvalidObjects.Add(id))
                return;

            Debug.LogWarning(
                $"[WallOcclusion] '{context.name}' is excluded as opaque: {reason}",
                context);
        }

        internal static void ClearForTests()
        {
            ResetStatics();
        }
    }
}
