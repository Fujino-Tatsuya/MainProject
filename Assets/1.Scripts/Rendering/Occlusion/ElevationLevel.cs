using System;
using System.Collections.Generic;
using UnityEngine;

namespace VeyTrace.Rendering.Occlusion
{
    [Serializable]
    public struct LocalXZArea
    {
        [SerializeField] private string label;
        [SerializeField] private Vector2 center;
        [SerializeField] private Vector2 size;
        [SerializeField] private float rotationDegrees;

        public string Label => label;
        public Vector2 Center => center;
        public Vector2 Size => size;
        public float RotationDegrees => rotationDegrees;

        public LocalXZArea(string label, Vector2 center, Vector2 size, float rotationDegrees = 0f)
        {
            this.label = label;
            this.center = center;
            this.size = new Vector2(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, size.y));
            this.rotationDegrees = rotationDegrees;
        }

        public bool Contains(Transform reference, Vector3 worldPosition)
        {
            if (reference == null)
                return false;

            Vector3 local = reference.InverseTransformPoint(worldPosition);
            Vector2 point = new(local.x - center.x, local.z - center.y);
            float radians = -rotationDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            Vector2 rotated = new(
                point.x * cos - point.y * sin,
                point.x * sin + point.y * cos);
            Vector2 half = new(
                Mathf.Max(0.005f, Mathf.Abs(size.x) * 0.5f),
                Mathf.Max(0.005f, Mathf.Abs(size.y) * 0.5f));
            return Mathf.Abs(rotated.x) <= half.x && Mathf.Abs(rotated.y) <= half.y;
        }

        public void Sanitize()
        {
            size.x = Mathf.Max(0.01f, Mathf.Abs(size.x));
            size.y = Mathf.Max(0.01f, Mathf.Abs(size.y));
            rotationDegrees = Mathf.Repeat(rotationDegrees + 180f, 360f) - 180f;
        }
    }

    [DisallowMultipleComponent]
    public sealed class ElevationLevel : MonoBehaviour
    {
        [SerializeField] private Transform contentRoot;
        [SerializeField] private Renderer[] contentRenderers = Array.Empty<Renderer>();
        [SerializeField] private Collider[] contentColliders = Array.Empty<Collider>();
        [SerializeField] private List<LocalXZArea> xzAreas = new();

        private ElevationStack stack;

        public Transform ContentRoot => contentRoot;
        public IReadOnlyList<Renderer> ContentRenderers => contentRenderers;
        public IReadOnlyList<Collider> ContentColliders => contentColliders;
        public IReadOnlyList<LocalXZArea> XZAreas => xzAreas;
        public ElevationStack Stack => stack != null ? stack : stack = GetComponentInParent<ElevationStack>();
        public float ReferenceWorldY => transform.position.y;

        public bool ContainsXZ(Vector3 worldPosition)
        {
            if (xzAreas == null)
                return false;

            for (int i = 0; i < xzAreas.Count; i++)
            {
                if (xzAreas[i].Contains(transform, worldPosition))
                    return true;
            }

            return false;
        }

        public bool IsRuntimeValid(out string reason)
        {
            if (Stack == null)
            {
                reason = "ElevationStack parent is missing.";
                return false;
            }

            if (!Stack.HasValidTransform(out reason))
                return false;

            if (!ApproximatelyOne(transform.localScale))
            {
                reason = "ElevationLevel scale must be (1,1,1).";
                return false;
            }

            Vector3 euler = transform.localEulerAngles;
            if (Mathf.Abs(euler.x) > 0.01f || Mathf.Abs(euler.z) > 0.01f)
            {
                reason = "ElevationLevel only allows Y-axis rotation.";
                return false;
            }

            if (transform.parent != Stack.transform)
            {
                reason = "ElevationLevel must be a direct child of ElevationStack.";
                return false;
            }

            if (contentRoot == null || contentRoot.parent != transform)
            {
                reason = "Content direct child is missing or not wired.";
                return false;
            }

            if (contentRenderers == null || CountAlive(contentRenderers) == 0)
            {
                reason = "Content has no registered Renderer.";
                return false;
            }

            if (contentColliders == null || CountAlive(contentColliders) == 0)
            {
                reason = "Content has no registered Collider.";
                return false;
            }

            if (xzAreas == null || xzAreas.Count == 0)
            {
                reason = "XZ Areas list is empty.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public void ConfigureAuthoring(
            Transform newContentRoot,
            Renderer[] renderers,
            Collider[] colliders,
            LocalXZArea[] areas = null)
        {
            contentRoot = newContentRoot;
            contentRenderers = renderers ?? Array.Empty<Renderer>();
            contentColliders = colliders ?? Array.Empty<Collider>();
            if (areas != null)
                xzAreas = new List<LocalXZArea>(areas);
            stack = null;
        }

        private void OnEnable()
        {
            stack = GetComponentInParent<ElevationStack>();
            WallOcclusionRegistry.Register(this);
        }

        private void OnDisable()
        {
            WallOcclusionRegistry.Unregister(this);
        }

        private void OnValidate()
        {
            stack = null;
            if (xzAreas == null)
                return;

            for (int i = 0; i < xzAreas.Count; i++)
            {
                LocalXZArea area = xzAreas[i];
                area.Sanitize();
                xzAreas[i] = area;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (xzAreas == null)
                return;

            Matrix4x4 previous = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.9f);
            for (int i = 0; i < xzAreas.Count; i++)
            {
                LocalXZArea area = xzAreas[i];
                Matrix4x4 localArea = Matrix4x4.TRS(
                    new Vector3(area.Center.x, 0f, area.Center.y),
                    Quaternion.Euler(0f, area.RotationDegrees, 0f),
                    new Vector3(area.Size.x, 0.05f, area.Size.y));
                Gizmos.matrix = transform.localToWorldMatrix * localArea;
                Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            }

            Gizmos.matrix = previous;
            Gizmos.color = previousColor;
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

        private static bool ApproximatelyOne(Vector3 value)
        {
            return Mathf.Abs(value.x - 1f) < 0.0001f &&
                Mathf.Abs(value.y - 1f) < 0.0001f &&
                Mathf.Abs(value.z - 1f) < 0.0001f;
        }
    }
}
