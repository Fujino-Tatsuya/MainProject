// ----------------------------------------------------------------------------
//  FogVolume.cs - 로컬 박스/스피어 포그 볼륨
//  존 프리팹이나 임의 오브젝트에 붙여 영역 단위로 포그를 추가한다.
//  Transform(위치/회전/스케일)이 배치를 정의 → 슬롯 셔플돼도 따라 이동.
// ----------------------------------------------------------------------------
using UnityEngine;

public enum FogVolumeShape
{
    Box = 0,
    Sphere = 1
}

[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Rendering/Fog Volume")]
public sealed class FogVolume : MonoBehaviour
{
    public FogVolumeShape shape = FogVolumeShape.Box;

    [Tooltip("박스 전체 크기(로컬). 스케일과 곱해진다.")]
    public Vector3 boxSize = new Vector3(10f, 4f, 10f);

    [Tooltip("스피어 반지름(로컬). 스케일과 곱해진다.")]
    public float sphereRadius = 5f;

    [Range(0f, 2f)] public float density = 1f;

    [Tooltip("경계 부드러움(로컬 단위 비율). 클수록 가장자리가 넓게 흐려진다.")]
    [Range(0.001f, 1f)] public float softBorder = 0.25f;

    public bool overrideColor = false;
    [ColorUsage(true, true)] public Color color = Color.white;

    private void OnEnable() => FogManager.Register(this);
    private void OnDisable() => FogManager.Unregister(this);

    /// <summary>월드 좌표를 박스 [-0.5,0.5]^3 / 스피어 반지름 0.5 로컬 공간으로 보내는 역행렬.</summary>
    public Matrix4x4 GetWorldToLocal()
    {
        Vector3 lossy = transform.lossyScale;
        Vector3 scale;
        if (shape == FogVolumeShape.Box)
        {
            scale = new Vector3(
                Mathf.Max(1e-4f, boxSize.x * Mathf.Abs(lossy.x)),
                Mathf.Max(1e-4f, boxSize.y * Mathf.Abs(lossy.y)),
                Mathf.Max(1e-4f, boxSize.z * Mathf.Abs(lossy.z)));
        }
        else
        {
            float d = Mathf.Max(1e-4f, sphereRadius * 2f);
            scale = new Vector3(d * Mathf.Abs(lossy.x), d * Mathf.Abs(lossy.y), d * Mathf.Abs(lossy.z));
        }

        Matrix4x4 trs = Matrix4x4.TRS(transform.position, transform.rotation, scale);
        return trs.inverse;
    }

    public Vector4 GetParams0()
    {
        // x:type, y:density, z:softBorder, w:hasTint
        return new Vector4((float)shape, density, softBorder, overrideColor ? 1f : 0f);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = overrideColor ? color : new Color(0.5f, 0.7f, 1f, 1f);
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
        if (shape == FogVolumeShape.Box)
            Gizmos.DrawWireCube(Vector3.zero, boxSize);
        else
            Gizmos.DrawWireSphere(Vector3.zero, sphereRadius);
    }
#endif
}
