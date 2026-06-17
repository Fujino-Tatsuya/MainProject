using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAimIndicator : MonoBehaviour
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

    private void Update()
    {
        UpdateAimDirection();
    }

    private void UpdateAimDirection()
    {
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
}
