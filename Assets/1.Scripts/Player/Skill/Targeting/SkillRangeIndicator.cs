using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 조준 모드 사거리 표시 뷰. URP DecalProjector로 시전자 중심 바닥 원(반경 = castRange)을 투영하고,
/// GroundPoint용 지면 마커 데칼(선택)을 관리한다. 순수 로컬 연출 — 오너에서만 표시한다.
/// Player 루트 하위(중심 정렬)에 두며, 표시는 데칼 컴포넌트 enabled 토글로 제어한다
/// (이 컴포넌트가 같은 GameObject에 있으므로 GameObject 자체를 끄지 않는다).
/// </summary>
public class SkillRangeIndicator : MonoBehaviour
{
    [Tooltip("사거리 원 데칼(바닥 투영). size.x/y를 지름(2×반경)으로 맞춘다.")]
    [SerializeField] private DecalProjector rangeDecal;

    [Tooltip("GroundPoint 조준 시 확정 지점 마커 데칼. 선택 — 없으면 무시.")]
    [SerializeField] private DecalProjector groundMarkerDecal;

    [Tooltip("데칼 투영 깊이(size.z). 기존 값이 0 이하일 때만 이 값으로 채운다.")]
    [SerializeField, Min(0.01f)] private float decalDepth = 5f;

    private void Awake()
    {
        HideAll();
    }

    // 시전자 중심 사거리 원 표시. radius(m) 반경으로 데칼 크기(지름)를 맞춘다.
    public void ShowRange(float radius)
    {
        if (rangeDecal == null)
            return;

        Vector3 size = rangeDecal.size;
        size.x = radius * 2f;
        size.y = radius * 2f;
        if (size.z <= 0f)
            size.z = decalDepth;
        rangeDecal.size = size;

        rangeDecal.enabled = true;
    }

    // GroundPoint 확정 후보 지점 마커. show=false면 숨긴다.
    public void SetGroundMarker(bool show, Vector3 worldPoint)
    {
        if (groundMarkerDecal == null)
            return;

        if (show)
        {
            groundMarkerDecal.transform.position = worldPoint;
            groundMarkerDecal.enabled = true;
        }
        else
        {
            groundMarkerDecal.enabled = false;
        }
    }

    public void HideAll()
    {
        if (rangeDecal != null)
            rangeDecal.enabled = false;

        if (groundMarkerDecal != null)
            groundMarkerDecal.enabled = false;
    }
}
