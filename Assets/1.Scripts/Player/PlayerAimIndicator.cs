using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class PlayerAimIndicator : NetworkBehaviour
{
    [Tooltip("바닥에 눕히는 얇은 쿼드(MeshRenderer). 플레이어 자식이지만 월드 위치·회전·크기는 매 프레임 여기서 다시 잡는다.")]
    [SerializeField] private Transform indicator;
    [SerializeField] private float indicator_rot_offset;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private LayerMask groundMask;

    [Tooltip("발밑 지면에서 띄우는 높이(m). 지면과 겹쳐 깜빡이거나 파묻히지 않게 한다.")]
    [SerializeField] private float heightOffset = 0.08f;
    [Tooltip("화살표 쿼드 한 변의 월드 크기(m). 예전 데칼(2m)의 60%.")]
    [SerializeField] private float arrowScale = 1.2f;
    [Tooltip("발밑에서 조준 방향으로 쿼드 중심을 미는 거리(m). 예전 데칼 오프셋(1m)의 60%.")]
    [SerializeField] private float arrowForwardOffset = 0.6f;

    [Tooltip("켜면 대시 재충전 중 인디케이터를 dashCooldownTint 로 어둡게 한다. 기본 꺼짐 — 대시 상태는 머리 위 대시 아이콘이 보여 준다.")]
    [SerializeField] private bool tintOnDashCooldown;
    [Tooltip("대시 재충전 중 인디케이터 원래 색에 곱하는 색. 충전되면 원래 색으로 돌아간다. tintOnDashCooldown 이 켜졌을 때만 쓴다.")]
    [SerializeField] private Color dashCooldownTint = new Color(0.3f, 0.3f, 0.3f, 1f);

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    // 발밑 지면 레이: 캐릭터 위치 조금 위에서 아래로. 계단·단차 위에서도 지면을 잡는다.
    private const float FootRayStartUp = 0.5f;
    private const float FootRayLength = 3f;

    private PlayerDashController dash;
    private Material indicatorMaterial; // 인스턴스 — 프리팹 애셋 오염 금지
    private Color indicatorBaseColor;
    private bool? lastDashDimmed;

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

        dash = GetComponent<PlayerDashController>();
        CacheIndicatorMaterial();
    }

    public override void OnDestroy()
    {
        if (indicatorMaterial != null)
            Destroy(indicatorMaterial);

        base.OnDestroy();
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
        UpdateDashTint();
    }

    // 이동이 끝난 뒤 배치해야 한 프레임 늦게 따라오지 않는다.
    private void LateUpdate()
    {
        if (IsNetworkActive && !IsOwner)
            return;

        PlaceIndicator();
    }

    private void CacheIndicatorMaterial()
    {
        if (indicator == null || !indicator.TryGetComponent(out MeshRenderer quad) || quad.sharedMaterial == null)
            return;

        indicatorMaterial = new Material(quad.sharedMaterial);
        quad.sharedMaterial = indicatorMaterial;
        indicatorBaseColor = indicatorMaterial.HasProperty(BaseColorId)
            ? indicatorMaterial.GetColor(BaseColorId)
            : Color.white;
    }

    /// <summary>인디케이터를 어둡게 칠할지 — 틴트가 켜졌고 대시 재충전 중일 때만.</summary>
    public static bool IsDashTintDimmed(bool tintOnDashCooldown, bool dashReady) => tintOnDashCooldown && !dashReady;

    // 재충전 중엔 어둡게, 충전되면 원래 색. 오너 예측 장부 기준이라 로컬 플레이어에만 의미가 있다.
    // 틴트를 끄면 어두운 상태가 남지 않도록 원래 색으로 되돌린다.
    private void UpdateDashTint()
    {
        if (indicatorMaterial == null || dash == null)
            return;

        bool dimmed = IsDashTintDimmed(tintOnDashCooldown, dash.IsReady);
        if (lastDashDimmed == dimmed)
            return;

        lastDashDimmed = dimmed;
        indicatorMaterial.SetColor(BaseColorId, dimmed ? indicatorBaseColor * dashCooldownTint : indicatorBaseColor);
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
    }

    // 발밑 지면 높이 + heightOffset 에 수평으로 눕히고, 조준 방향으로 Y축만 돌린다. 부모 회전과 무관하다.
    private void PlaceIndicator()
    {
        if (indicator == null)
            return;

        Vector3 foot = transform.position;
        bool hasGround = Physics.Raycast(
            foot + Vector3.up * FootRayStartUp, Vector3.down, out RaycastHit groundHit,
            FootRayStartUp + FootRayLength, groundMask, QueryTriggerInteraction.Ignore);

        Vector3 aim = AimDirection.sqrMagnitude > 0f ? AimDirection : transform.forward;
        float height = AimIndicatorPlacement.Height(hasGround, groundHit.point.y, foot.y, heightOffset);

        indicator.SetPositionAndRotation(
            AimIndicatorPlacement.Position(foot, height, aim, arrowForwardOffset),
            AimIndicatorPlacement.Rotation(aim, indicator_rot_offset));

        Transform parent = indicator.parent;
        indicator.localScale = AimIndicatorPlacement.LocalScale(arrowScale, parent != null ? parent.lossyScale.x : 1f);
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
