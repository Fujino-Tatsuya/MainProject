using UnityEngine;

/// <summary>
/// 밟은 플레이어 또는 시체를 지정된 수평 방향으로 운반하는 컨베이어 타일입니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ConveyorTile : MonoBehaviour, ISurfaceCarrier
{
    public enum TileKind
    {
        Straight,
        Corner
    }

    public enum CardinalDirection
    {
        Forward,
        Right,
        Back,
        Left
    }

    [Header("타일")]
    [SerializeField] private TileKind tileKind = TileKind.Straight;

    [Header("직선 방향")]
    [Tooltip("직선 타일이 탑승자를 밀어내는 로컬 방향입니다.")]
    [SerializeField] private CardinalDirection straightDir = CardinalDirection.Forward;

    [Header("코너 방향")]
    [Tooltip("입구에서 타일 중심으로 진행하는 로컬 방향입니다.")]
    [SerializeField] private CardinalDirection inDir = CardinalDirection.Forward;
    [Tooltip("타일 중심에서 출구로 진행하는 로컬 방향입니다.")]
    [SerializeField] private CardinalDirection outDir = CardinalDirection.Right;

    [Header("그룹 미지정 폴백")]
    [SerializeField, Min(0f)] private float fallbackBeltSpeed = 3f;

    private const float GizmoHalfLength = 0.35f;
    private const float GizmoHeightMultiplier = 2f;
    private const float GizmoThickness = 5f;

    private ConveyorGroup conveyorGroup;

    private void Awake()
    {
        ResolveGroup();
    }

    public Vector3 GetCarryDelta(Vector3 riderWorldPos, float dt)
    {
        if (dt <= 0f)
            return Vector3.zero;

        Vector3 localDirection = ResolveLocalDirection(riderWorldPos);
        Vector3 worldDirection = transform.TransformDirection(localDirection);
        worldDirection.y = 0f;

        if (worldDirection.sqrMagnitude <= Mathf.Epsilon)
            return Vector3.zero;

        float speed = conveyorGroup != null
            ? conveyorGroup.BeltSpeed
            : Mathf.Max(0f, fallbackBeltSpeed);
        return worldDirection.normalized * speed * dt;
    }

    private Vector3 ResolveLocalDirection(Vector3 riderWorldPos)
    {
        if (tileKind == TileKind.Straight)
            return ToLocalDirection(straightDir);

        Vector3 localIn = ToLocalDirection(inDir);
        Vector3 localOut = ToLocalDirection(outDir);

        // 직각 코너가 아니면 안전하게 입구 방향을 사용합니다.
        if (!Mathf.Approximately(Vector3.Dot(localIn, localOut), 0f))
            return localIn;

        Vector3 localRider = transform.InverseTransformPoint(riderWorldPos);
        Vector3 diagonalNormal = localIn + localOut;

        // 입구 가장자리 쪽 삼각형은 inDir, 출구 가장자리 쪽은 outDir입니다.
        return Vector3.Dot(localRider, diagonalNormal) < 0f
            ? localIn
            : localOut;
    }

    private void ResolveGroup()
    {
        conveyorGroup = GetComponentInParent<ConveyorGroup>();
    }

    private void OnValidate()
    {
        fallbackBeltSpeed = Mathf.Max(0f, fallbackBeltSpeed);
        ResolveGroup();

#if UNITY_EDITOR
        // 인스펙터 값 변경만으로는 씬 뷰가 다시 그려지지 않아 기즈모가 갱신되지 않습니다.
        UnityEditor.SceneView.RepaintAll();
#endif
    }

    private void OnDrawGizmos()
    {
        // 피벗이 아니라 에디터에 보이는 바운즈 중심을 기준으로, 오브젝트 높이의 GizmoHeightMultiplier배 만큼 띄워 그립니다.
        Bounds bounds = MeasureBounds();
        Vector3 center = bounds.center + Vector3.up * (bounds.size.y * GizmoHeightMultiplier);

        if (tileKind == TileKind.Straight)
        {
            Vector3 worldDirection = ToHorizontalWorld(ToLocalDirection(straightDir));
            DrawArrow(center - worldDirection * GizmoHalfLength, center + worldDirection * GizmoHalfLength, Color.cyan);
            return;
        }

        Vector3 localIn = ToLocalDirection(inDir);
        Vector3 localOut = ToLocalDirection(outDir);

        // 입구 → 중심 → 출구로 꺾이는 경로를 중심에서 이어 그립니다.
        DrawArrow(center - ToHorizontalWorld(localIn) * GizmoHalfLength, center, new Color(1f, 0.75f, 0f));
        DrawArrow(center, center + ToHorizontalWorld(localOut) * GizmoHalfLength, Color.cyan);

        // 분할선도 같은 중심 기준으로 옮깁니다.
        Vector3 diagonalNormal = localIn + localOut;
        Vector3 pivotToCenter = center - transform.position;
        DrawThickLine(
            transform.TransformPoint(new Vector3(-diagonalNormal.z, 0f, diagonalNormal.x) * 0.5f) + pivotToCenter,
            transform.TransformPoint(new Vector3(diagonalNormal.z, 0f, -diagonalNormal.x) * 0.5f) + pivotToCenter,
            Color.white);
    }

    /// <summary>에디터 선택 시 보이는 것과 같은 렌더러 월드 바운즈. 렌더러가 없으면 피벗과 스케일 y로 대체합니다.</summary>
    private Bounds MeasureBounds()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(transform.position, new Vector3(0f, Mathf.Abs(transform.lossyScale.y), 0f));

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds;
    }

    private Vector3 ToHorizontalWorld(Vector3 localDirection)
    {
        Vector3 worldDirection = transform.TransformDirection(localDirection);
        worldDirection.y = 0f;
        return worldDirection.sqrMagnitude > Mathf.Epsilon
            ? worldDirection.normalized
            : Vector3.zero;
    }

    private static Vector3 ToLocalDirection(CardinalDirection direction)
    {
        switch (direction)
        {
            case CardinalDirection.Right:
                return Vector3.right;
            case CardinalDirection.Back:
                return Vector3.back;
            case CardinalDirection.Left:
                return Vector3.left;
            default:
                return Vector3.forward;
        }
    }

    private static void DrawArrow(Vector3 start, Vector3 tip, Color color)
    {
        Vector3 direction = tip - start;
        if (direction.sqrMagnitude <= Mathf.Epsilon)
            return;

        const float headLength = 0.18f;
        const float headWidth = 0.12f;

        direction.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
        Vector3 headBase = tip - direction * headLength;

        DrawThickLine(start, tip, color);
        DrawThickLine(tip, headBase + side * headWidth, color);
        DrawThickLine(tip, headBase - side * headWidth, color);
    }

    /// <summary>Gizmos.DrawLine은 두께를 못 주므로 두꺼운 선은 Handles로 그립니다.</summary>
    private static void DrawThickLine(Vector3 from, Vector3 to, Color color)
    {
#if UNITY_EDITOR
        UnityEditor.Handles.matrix = Matrix4x4.identity;
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.DrawAAPolyLine(GizmoThickness, from, to);
#else
        Gizmos.color = color;
        Gizmos.DrawLine(from, to);
#endif
    }
}
