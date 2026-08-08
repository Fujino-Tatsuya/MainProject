using UnityEngine;

namespace VeyTrace.Rendering.Occlusion
{
    [DisallowMultipleComponent]
    public sealed class ElevationStack : MonoBehaviour
    {
        public bool HasValidTransform(out string reason)
        {
            if (!ApproximatelyOne(transform.localScale))
            {
                reason = "ElevationStack scale must be (1,1,1).";
                return false;
            }

            Vector3 euler = transform.localEulerAngles;
            if (Mathf.Abs(euler.x) > 0.01f || Mathf.Abs(euler.z) > 0.01f)
            {
                reason = "ElevationStack only allows Y-axis rotation.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool ApproximatelyOne(Vector3 value)
        {
            return Mathf.Abs(value.x - 1f) < 0.0001f &&
                Mathf.Abs(value.y - 1f) < 0.0001f &&
                Mathf.Abs(value.z - 1f) < 0.0001f;
        }
    }
}
