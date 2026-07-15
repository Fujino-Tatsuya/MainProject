using UnityEngine;

public static class ColliderMathUtility
{
    public static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    public static Vector3 GetCapsuleLocalAxis(int direction)
    {
        if (direction == 0)
            return Vector3.right;
        if (direction == 1)
            return Vector3.up;

        return Vector3.forward;
    }

    public static float GetAxisScale(Vector3 scale, int direction)
    {
        if (direction == 0)
            return scale.x;
        if (direction == 1)
            return scale.y;

        return scale.z;
    }

    public static float GetCapsuleRadiusScale(Vector3 scale, int direction)
    {
        if (direction == 0)
            return Mathf.Max(scale.y, scale.z);
        if (direction == 1)
            return Mathf.Max(scale.x, scale.z);

        return Mathf.Max(scale.x, scale.y);
    }
}
