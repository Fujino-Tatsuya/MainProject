using UnityEngine;

[DisallowMultipleComponent]
public sealed class FloatingDamageAnchor : MonoBehaviour
{
    [SerializeField] Transform anchorTransform;
    [SerializeField] Vector3 localOffset;

    public Vector3 WorldPosition =>
        anchorTransform != null ? anchorTransform.position : transform.TransformPoint(localOffset);
}
