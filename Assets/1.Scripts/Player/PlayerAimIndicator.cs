using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class PlayerAimIndicator : NetworkBehaviour
{
    [SerializeField] private Transform indicator;
    [SerializeField] private float indicator_rot_offset;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private LayerMask groundMask;

    public Vector3 AimDirection { get; private set; }

    // 마우스 아래 지면 히트 지점(월드). 타겟팅 컨트롤러가 GroundPoint 조준·사거리 판정에 재사용한다.
    public Vector3 AimGroundPoint { get; private set; }
    public bool HasAimGroundPoint { get; private set; }

    // 조준에 쓰는 게임플레이 카메라. 소멸/미존재 시 지연 재해석되므로 매번 프로퍼티로 조회할 것.
    public Camera TargetCamera => targetCamera;

    // groundMask 공유 — 타겟팅 컨트롤러가 별도 마스크를 들지 않도록 노출한다.
    public LayerMask GroundMask => groundMask;

    private void Awake()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        indicator_rot_offset = 90.0f;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner)
            SetIndicatorActive(false);
    }

    private void Update()
    {
        if (IsNetworkActive && !IsOwner)
            return;

        UpdateAimDirection();
    }

    private void UpdateAimDirection()
    {
        // Re-resolve lazily: the gameplay camera may not exist yet at spawn time,
        // and a cached camera can be destroyed on scene unload (Unity treats a
        // destroyed object as == null). Recover here instead of stalling forever.
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCamera == null || indicator == null || Mouse.current == null)
            return;

        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = targetCamera.ScreenPointToRay(mousePosition);

        Vector3 aimPoint;
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, groundMask))
        {
            aimPoint = hit.point;
        }
        else
        {
            // 바닥이 Ground 레이어가 아닌 씬(생성맵 등)에서는 레이가 영원히 미스 →
            // 조준이 마지막 값으로 고정된다. 플레이어 높이 수평면과 교차시켜 폴백 —
            // 씬 레이어 구성과 무관하게 조준을 유지한다.
            Plane aimPlane = new Plane(Vector3.up, transform.position);
            if (!aimPlane.Raycast(ray, out float enter))
            {
                HasAimGroundPoint = false;
                return;
            }
            aimPoint = ray.GetPoint(enter);
        }

        // 지면 타게팅 스킬(PlayerSkillTargeting)이 이 값으로 시전 지점을 잡는다.
        // 폴백으로 구한 점도 유효 조준점으로 취급한다 — 생성맵처럼 Ground 레이어가
        // 없는 씬에서 지면 스킬이 통째로 막히면 안 되기 때문.
        AimGroundPoint = aimPoint;
        HasAimGroundPoint = true;

        Vector3 direction = aimPoint - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
            return;

        AimDirection = direction.normalized;

        float yaw = Mathf.Atan2(AimDirection.x, AimDirection.z) * Mathf.Rad2Deg;
        indicator.rotation = Quaternion.Euler(90.0f, yaw + indicator_rot_offset, 0f);
    }

    private bool IsNetworkActive =>
        Unity.Netcode.NetworkManager.Singleton != null &&
        Unity.Netcode.NetworkManager.Singleton.IsListening &&
        IsSpawned;

    private void SetIndicatorActive(bool isActive)
    {
        if (indicator != null)
            indicator.gameObject.SetActive(isActive);
    }
}
