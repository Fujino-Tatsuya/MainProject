using UnityEngine;

/// <summary>
/// Distributes wrist axial rotation over the exported SD assassin forearm helpers.
/// Add to the model prefab and call CaptureBindPose in the importer before animation.
/// This is deterministic pose correction, not hair physics or an animation clip.
/// </summary>
[DefaultExecutionOrder(20000)]
[DisallowMultipleComponent]
public sealed class SDArmTwist : MonoBehaviour
{
    [System.Serializable]
    public sealed class Arm
    {
        public Transform forearm, hand, twist01, twist02;
        public Quaternion handBindLocal = Quaternion.identity;
        public Quaternion twist01BindLocal = Quaternion.identity;
        public Quaternion twist02BindLocal = Quaternion.identity;
        public Vector3 axis = Vector3.up;
        public bool captured;
    }
    [System.Serializable]
    public sealed class Knee
    {
        public Transform lowerLeg, shield;
        public Quaternion lowerBindLocal = Quaternion.identity;
        public Quaternion shieldBindLocal = Quaternion.identity;
        public bool captured;
    }
    public Knee leftKnee = new Knee(), rightKnee = new Knee();
    public Arm left = new Arm();
    public Arm right = new Arm();

    Transform FindBone(string boneName)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == boneName) return t;
        return null;
    }
    void Resolve(Arm a, string prefix)
    {
        if (!a.forearm) a.forearm = FindBone(prefix + "LowerArm");
        if (!a.hand) a.hand = FindBone(prefix + "Hand");
        if (!a.twist01) a.twist01 = FindBone(prefix + "ForearmTwist01");
        if (!a.twist02) a.twist02 = FindBone(prefix + "ForearmTwist02");
    }
    static void Capture(Arm a)
    {
        if (!a.forearm || !a.hand || !a.twist01 || !a.twist02) return;
        a.handBindLocal = a.hand.localRotation;
        a.twist01BindLocal = a.twist01.localRotation;
        a.twist02BindLocal = a.twist02.localRotation;
        a.axis = a.forearm.InverseTransformVector(a.hand.position - a.forearm.position).normalized;
        a.captured = a.axis.sqrMagnitude > 0.5f;
    }
    void ResolveKnee(Knee k, string prefix)
    {
        if (!k.lowerLeg) k.lowerLeg = FindBone(prefix + "LowerLeg");
        if (!k.shield) k.shield = FindBone(prefix + "KneeShield");
    }
    static void CaptureKnee(Knee k)
    {
        if (!k.lowerLeg || !k.shield) return;
        k.lowerBindLocal = k.lowerLeg.localRotation;
        k.shieldBindLocal = k.shield.localRotation;
        k.captured = true;
    }
    static void ApplyKnee(Knee k)
    {
        if (!k.captured || !k.lowerLeg || !k.shield) return;
        Quaternion delta = k.lowerLeg.localRotation * Quaternion.Inverse(k.lowerBindLocal);
        k.shield.localRotation = Quaternion.SlerpUnclamped(Quaternion.identity, delta, 0.5f) * k.shieldBindLocal;
    }
    [ContextMenu("Capture Bind Pose (before animation)")]
    public void CaptureBindPose()
    {
        Resolve(left, "Left"); Resolve(right, "Right");
        Capture(left); Capture(right);
        ResolveKnee(leftKnee, "Left"); ResolveKnee(rightKnee, "Right");
        CaptureKnee(leftKnee); CaptureKnee(rightKnee);
    }
    void Awake()
    {
        Resolve(left, "Left"); Resolve(right, "Right");
        if (!left.captured) Capture(left);
        if (!right.captured) Capture(right);
        ResolveKnee(leftKnee, "Left"); ResolveKnee(rightKnee, "Right");
        if (!leftKnee.captured) CaptureKnee(leftKnee);
        if (!rightKnee.captured) CaptureKnee(rightKnee);
    }
    static void Apply(Arm a)
    {
        if (!a.captured || !a.hand || !a.twist01 || !a.twist02) return;
        // Express the hand change in its forearm parent's coordinate frame.
        Quaternion delta = a.hand.localRotation * Quaternion.Inverse(a.handBindLocal);
        Vector3 imaginary = new Vector3(delta.x, delta.y, delta.z);
        Vector3 projected = a.axis * Vector3.Dot(imaginary, a.axis);
        float norm = Mathf.Sqrt(projected.sqrMagnitude + delta.w * delta.w);
        if (norm < 0.000001f) return;
        Quaternion axial = new Quaternion(projected.x / norm, projected.y / norm, projected.z / norm, delta.w / norm);
        if (axial.w < 0f) axial = new Quaternion(-axial.x, -axial.y, -axial.z, -axial.w);
        a.twist01.localRotation = Quaternion.SlerpUnclamped(Quaternion.identity, axial, 1f / 3f) * a.twist01BindLocal;
        a.twist02.localRotation = Quaternion.SlerpUnclamped(Quaternion.identity, axial, 2f / 3f) * a.twist02BindLocal;
    }
    void LateUpdate() { Apply(left); Apply(right); ApplyKnee(leftKnee); ApplyKnee(rightKnee); }
}

