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

        if (!Physics.Raycast(ray, out RaycastHit hit, 100f, groundMask))
            return;

        Vector3 direction = hit.point - transform.position;
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
