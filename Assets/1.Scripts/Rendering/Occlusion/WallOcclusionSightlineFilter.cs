using UnityEngine;

namespace VeyTrace.Rendering.Occlusion
{
    public static class WallOcclusionSightlineFilter
    {
        private const float WidthSampleScale = 0.55f;

        public const int RequiredSampleCapacity = 11;

        public static int BuildSamples(
            Camera camera,
            Vector3 endpointA,
            Vector3 endpointB,
            float radiusPixels,
            Ray[] rays,
            float[] maxDistances)
        {
            if (camera == null || rays == null || maxDistances == null ||
                rays.Length < RequiredSampleCapacity ||
                maxDistances.Length < RequiredSampleCapacity)
            {
                return 0;
            }

            Vector3 screenA = camera.WorldToScreenPoint(endpointA);
            Vector3 screenB = camera.WorldToScreenPoint(endpointB);
            if (screenA.z <= 0f || screenB.z <= 0f)
                return 0;

            Vector2 segment = new(screenB.x - screenA.x, screenB.y - screenA.y);
            Vector2 normal = segment.sqrMagnitude > 0.0001f
                ? new Vector2(-segment.y, segment.x).normalized
                : Vector2.right;
            float widthOffset = Mathf.Max(0f, radiusPixels) * WidthSampleScale;

            int count = 0;
            AddSample(camera, screenA, screenB, normal, 0f, 0f, rays, maxDistances, ref count);
            AddRow(camera, screenA, screenB, normal, 0.25f, widthOffset, rays, maxDistances, ref count);
            AddRow(camera, screenA, screenB, normal, 0.5f, widthOffset, rays, maxDistances, ref count);
            AddRow(camera, screenA, screenB, normal, 0.75f, widthOffset, rays, maxDistances, ref count);
            AddSample(camera, screenA, screenB, normal, 1f, 0f, rays, maxDistances, ref count);
            return count;
        }

        public static bool BlocksAnySample(
            Collider collider,
            Ray[] rays,
            float[] maxDistances,
            int sampleCount)
        {
            if (collider == null || rays == null || maxDistances == null)
                return false;

            int count = Mathf.Min(sampleCount, Mathf.Min(rays.Length, maxDistances.Length));
            for (int i = 0; i < count; i++)
            {
                if (maxDistances[i] > 0f &&
                    collider.Raycast(rays[i], out _, maxDistances[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddRow(
            Camera camera,
            Vector3 screenA,
            Vector3 screenB,
            Vector2 normal,
            float axisProgress,
            float widthOffset,
            Ray[] rays,
            float[] maxDistances,
            ref int count)
        {
            AddSample(
                camera,
                screenA,
                screenB,
                normal,
                axisProgress,
                -widthOffset,
                rays,
                maxDistances,
                ref count);
            AddSample(
                camera,
                screenA,
                screenB,
                normal,
                axisProgress,
                0f,
                rays,
                maxDistances,
                ref count);
            AddSample(
                camera,
                screenA,
                screenB,
                normal,
                axisProgress,
                widthOffset,
                rays,
                maxDistances,
                ref count);
        }

        private static void AddSample(
            Camera camera,
            Vector3 screenA,
            Vector3 screenB,
            Vector2 normal,
            float axisProgress,
            float widthOffset,
            Ray[] rays,
            float[] maxDistances,
            ref int count)
        {
            Vector3 screenPoint = Vector3.Lerp(screenA, screenB, axisProgress);
            screenPoint.x += normal.x * widthOffset;
            screenPoint.y += normal.y * widthOffset;

            Ray ray = camera.ScreenPointToRay(screenPoint);
            Vector3 targetPoint = camera.ScreenToWorldPoint(screenPoint);
            float maxDistance = Vector3.Dot(targetPoint - ray.origin, ray.direction);
            if (maxDistance <= 0.001f)
                return;

            rays[count] = ray;
            maxDistances[count] = maxDistance;
            count++;
        }
    }
}
