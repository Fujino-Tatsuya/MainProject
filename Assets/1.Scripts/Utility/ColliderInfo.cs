using System;
using UnityEngine;

[Flags]
public enum OverlapCollider
{
    None = 0,
    Box = 1 << 0,
    Capsule = 1 << 1,
    Sphere = 1 << 2
}

public struct BoxColliderInfo
{
    public Vector3 center;
    public Vector3 halfExtents;
    public Quaternion orientation;
}

public struct CapsuleColliderInfo
{
    public Vector3 point0;
    public Vector3 point1;
    public float radius;
}

public struct SphereColliderInfo
{
    public Vector3 center;
    public float radius;
}

public class ColliderInfo : MonoBehaviour
{
    BoxCollider _boxCollider;
    CapsuleCollider _capsuleCollider;
    SphereCollider _sphereCollider;
    ColliderInfo _colliderInfo;

    OverlapCollider _overlapCollider;
    public OverlapCollider OverlapCollider { get { return _overlapCollider; } }

    void Awake()
    {
        _boxCollider = GetComponent<BoxCollider>();
        _capsuleCollider = GetComponent<CapsuleCollider>();
        _sphereCollider = GetComponent<SphereCollider>();
        _colliderInfo = GetComponent<ColliderInfo>();

        _overlapCollider = OverlapCollider.None;

        if (_boxCollider)
            _overlapCollider = BitMaskHelper<OverlapCollider>.Add(_overlapCollider, OverlapCollider.Box);
        if (_capsuleCollider)
            _overlapCollider = BitMaskHelper<OverlapCollider>.Add(_overlapCollider, OverlapCollider.Capsule);
        if (_sphereCollider)
            _overlapCollider = BitMaskHelper<OverlapCollider>.Add(_overlapCollider, OverlapCollider.Sphere);

        int overlapColliderValue = (int)_overlapCollider;
        if (overlapColliderValue == 0)
        {
            Debug.LogError("ColliderInfo requires one BoxCollider, CapsuleCollider, or SphereCollider.", this);
        }
        else if (overlapColliderValue != (int)OverlapCollider.Box &&
                 overlapColliderValue != (int)OverlapCollider.Capsule &&
                 overlapColliderValue != (int)OverlapCollider.Sphere)
        {
            Debug.LogError($"ColliderInfo supports only one collider type, but found {_overlapCollider}.", this);
        }

        _colliderInfo.enabled = false;
    }

    /// <summary>
    /// BoxCollider의 로컬 정보를 Physics.OverlapBoxNonAlloc에서 사용할 월드 기준 정보로 변환합니다.
    /// </summary>
    /// <param name="info">계산된 중심점, 반 크기, 회전값을 저장할 정보 구조체입니다.</param>
    public void GetBoxColliderInfo(ref BoxColliderInfo info)
    {
        Vector3 center = transform.TransformPoint(_boxCollider.center);
        Vector3 halfExtents = Vector3.Scale(_boxCollider.size * 0.5f, ColliderMathUtility.Abs(transform.lossyScale));
        Quaternion orientation = transform.rotation;

        info.center = center;
        info.halfExtents = halfExtents;
        info.orientation = orientation;
    }

    /// <summary>
    /// CapsuleCollider의 로컬 정보를 Physics.OverlapCapsuleNonAlloc에서 사용할 월드 기준 정보로 변환합니다.
    /// </summary>
    /// <param name="info">계산된 양 끝점과 반지름을 저장할 정보 구조체입니다.</param>
    public void GetCapsuleColliderInfo(ref CapsuleColliderInfo info)
    {
        Vector3 center = transform.TransformPoint(_capsuleCollider.center);
        Vector3 scale = ColliderMathUtility.Abs(transform.lossyScale);
        Vector3 localAxis = ColliderMathUtility.GetCapsuleLocalAxis(_capsuleCollider.direction);
        Vector3 axis = transform.TransformDirection(localAxis).normalized;

        float axisScale = ColliderMathUtility.GetAxisScale(scale, _capsuleCollider.direction);
        float radiusScale = ColliderMathUtility.GetCapsuleRadiusScale(scale, _capsuleCollider.direction);
        float radius = _capsuleCollider.radius * radiusScale;
        float height = Mathf.Max(_capsuleCollider.height * axisScale, radius * 2f);
        float halfSegment = Mathf.Max(0f, height * 0.5f - radius);

        info.point0 = center + axis * halfSegment;
        info.point1 = center - axis * halfSegment;
        info.radius = radius;
    }

    /// <summary>
    /// SphereCollider의 로컬 정보를 Physics.OverlapSphereNonAlloc에서 사용할 월드 기준 정보로 변환합니다.
    /// </summary>
    /// <param name="info">계산된 중심점과 반지름을 저장할 정보 구조체입니다.</param>
    public void GetSphereColliderInfo(ref SphereColliderInfo info)
    {
        Vector3 center = transform.TransformPoint(_sphereCollider.center);
        Vector3 scale = ColliderMathUtility.Abs(transform.lossyScale);
        float radius = _sphereCollider.radius * Mathf.Max(scale.x, scale.y, scale.z);

        info.center = center;
        info.radius = radius;
    }
}
