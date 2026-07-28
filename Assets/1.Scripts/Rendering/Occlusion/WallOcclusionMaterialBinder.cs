using System.Collections.Generic;
using UnityEngine;

namespace VeyTrace.Rendering.Occlusion
{
    public readonly struct WallOcclusionBindReport
    {
        public WallOcclusionBindReport(
            int inspectedRenderers,
            int swappedRenderers,
            int swappedSlots,
            int alreadyBoundSlots,
            IReadOnlyCollection<string> unmappedMaterialNames)
        {
            InspectedRenderers = inspectedRenderers;
            SwappedRenderers = swappedRenderers;
            SwappedSlots = swappedSlots;
            AlreadyBoundSlots = alreadyBoundSlots;
            UnmappedMaterialNames = unmappedMaterialNames ?? System.Array.Empty<string>();
        }

        public int InspectedRenderers { get; }
        public int SwappedRenderers { get; }
        public int SwappedSlots { get; }
        public int AlreadyBoundSlots { get; }

        // 매핑이 없어 페이드되지 않는 머티리얼. 아트 교체 후 누락을 잡는 용도다.
        public IReadOnlyCollection<string> UnmappedMaterialNames { get; }

        public int BoundSlots => SwappedSlots + AlreadyBoundSlots;
    }

    // 벽 렌더러의 머티리얼을 오클루전 변종으로 교체한다.
    //
    // 렌더러 이름으로 벽을 고르지 않는다. 어떤 머티리얼이 대상인지는 설정의 매핑 목록이
    // 정하고, 벽/바닥 구분은 셰이더가 노멀로 한다(바닥은 매핑돼 있어도 페이드되지 않는다).
    // 콜라이더, 전용 컴포넌트, 물리 레이어를 만들지 않으며 여러 번 호출해도 안전하다.
    public static class WallOcclusionMaterialBinder
    {
        public static WallOcclusionBindReport Bind(
            WallOcclusionSettings settings,
            IEnumerable<Transform> roots)
        {
            var unmapped = new HashSet<string>();
            if (settings == null || roots == null || !settings.HasValidMaterialMappings)
                return new WallOcclusionBindReport(0, 0, 0, 0, unmapped);

            int inspected = 0;
            int swappedRenderers = 0;
            int swappedSlots = 0;
            int alreadyBound = 0;
            var visitedRoots = new HashSet<Transform>();

            foreach (Transform root in roots)
            {
                if (root == null || !visitedRoots.Add(root))
                    continue;

                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null)
                        continue;

                    inspected++;
                    if (TrySwapRenderer(
                            renderer,
                            settings,
                            unmapped,
                            ref swappedSlots,
                            ref alreadyBound))
                        swappedRenderers++;
                }
            }

            return new WallOcclusionBindReport(
                inspected,
                swappedRenderers,
                swappedSlots,
                alreadyBound,
                unmapped);
        }

        private static bool TrySwapRenderer(
            Renderer renderer,
            WallOcclusionSettings settings,
            HashSet<string> unmapped,
            ref int swappedSlots,
            ref int alreadyBound)
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;

            for (int slot = 0; slot < materials.Length; slot++)
            {
                Material current = materials[slot];
                if (current == null)
                    continue;

                if (!settings.TryResolveOcclusionMaterial(current, out Material variant))
                {
                    unmapped.Add(current.name);
                    continue;
                }

                if (current == variant)
                {
                    alreadyBound++;
                    continue;
                }

                materials[slot] = variant;
                swappedSlots++;
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = materials;

            return changed;
        }

        public static string DescribeUnmapped(
            IReadOnlyCollection<string> names,
            int maxReported = 6)
        {
            if (names == null || names.Count == 0)
                return "<none>";

            var builder = new System.Text.StringBuilder();
            int reported = 0;
            foreach (string name in names)
            {
                if (reported >= maxReported)
                    break;

                if (reported > 0)
                    builder.Append(", ");
                builder.Append(name);
                reported++;
            }

            int remaining = names.Count - reported;
            if (remaining > 0)
                builder.Append($", +{remaining} more");

            return builder.ToString();
        }
    }
}
