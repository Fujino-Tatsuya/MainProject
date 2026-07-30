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

    [Tooltip("영역 포그 농도. 양수 = 추가, 음수 = 제거(자연스러운 클리어링).")]
    [Range(-1f, 2f)] public float density = 1f;

    [Tooltip("경계 페이드 폭(월드 단위, 미터). 클수록 가장자리가 부드럽게 사라진다. 볼륨 크기에 맞춰 키울 것(예: 3~10).")]
    [Min(0f)] public float softBorder = 3f;

    public bool overrideColor = false;
    [ColorUsage(true, true)] public Color color = Color.white;

    private void OnEnable() => FogManager.Register(this);
    private void OnDisable() => FogManager.Unregister(this);

    /// <summary>월드 좌표를 볼륨의 회전 프레임(원점 중심·월드 스케일 유지)으로 보내는 역행렬.
    /// 스케일을 굽지 않으므로 SDF를 월드 단위로 계산할 수 있다(경계 페이드가 방향마다 균질).</summary>
    public Matrix4x4 GetWorldToLocal()
    {
        return Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one).inverse;
    }

    /// <summary>월드 단위 경계. 박스 = half-extents(xyz), 스피어 = 반지름(x).</summary>
    public Vector4 GetBounds()
    {
        Vector3 lossy = transform.lossyScale;
        if (shape == FogVolumeShape.Box)
        {
            return new Vector4(
                0.5f * Mathf.Abs(boxSize.x * lossy.x),
                0.5f * Mathf.Abs(boxSize.y * lossy.y),
                0.5f * Mathf.Abs(boxSize.z * lossy.z), 0f);
        }
        float maxS = Mathf.Max(Mathf.Abs(lossy.x), Mathf.Max(Mathf.Abs(lossy.y), Mathf.Abs(lossy.z)));
        float r = Mathf.Max(1e-4f, sphereRadius * maxS);
        return new Vector4(r, r, r, 0f);
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
