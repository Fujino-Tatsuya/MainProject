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
            IReadOnlyCollection<string> unmappedMaterialNames,
            int excludedRenderers = 0)
        {
            InspectedRenderers = inspectedRenderers;
            SwappedRenderers = swappedRenderers;
            SwappedSlots = swappedSlots;
            AlreadyBoundSlots = alreadyBoundSlots;
            UnmappedMaterialNames = unmappedMaterialNames ?? System.Array.Empty<string>();
            ExcludedRenderers = excludedRenderers;
        }

        public int InspectedRenderers { get; }
        public int SwappedRenderers { get; }
        public int SwappedSlots { get; }
        public int AlreadyBoundSlots { get; }

        // 이름 규칙으로 불투명하게 남긴 렌더러 수(밟는 면 등).
        public int ExcludedRenderers { get; }

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
            int excluded = 0;
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

                    // 밟고 다니는 면(참호 덮개·경사면)은 벽과 머티리얼을 공유하므로 매핑으로는
                    // 걸러낼 수 없다. 이름 규칙으로 교체 자체를 건너뛰어 불투명하게 남긴다.
                    if (IsExcludedByName(renderer.transform, root, settings))
                    {
                        excluded++;
                        continue;
                    }

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
                unmapped,
                excluded);
        }

        // 이름 판정은 렌더러 자신부터 바인딩 루트까지 올라가며 본다. 아트가 fbx를 그대로
        // 인스턴스화하므로 이름이 붙은 쪽이 렌더러가 아니라 모델 루트인 경우가 많다
        // (예: 루트 'Env_floor_Trenchcover' 밑의 메시 자식 이름은 'default').
        private static bool IsExcludedByName(
            Transform renderer,
            Transform root,
            WallOcclusionSettings settings)
        {
            for (Transform current = renderer; current != null; current = current.parent)
            {
                if (settings.IsExcludedByName(current.name))
                    return true;

                if (current == root)
                    break;
            }

            return false;
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
