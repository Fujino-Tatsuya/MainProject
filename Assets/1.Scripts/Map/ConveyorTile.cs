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

    [Header("코너 방향")]
    [Tooltip("입구에서 타일 중심으로 진행하는 로컬 방향입니다.")]
    [SerializeField] private CardinalDirection inDir = CardinalDirection.Forward;
    [Tooltip("타일 중심에서 출구로 진행하는 로컬 방향입니다.")]
    [SerializeField] private CardinalDirection outDir = CardinalDirection.Right;

    [Header("그룹 미지정 폴백")]
    [SerializeField, Min(0f)] private float fallbackBeltSpeed = 3f;

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
            return Vector3.forward;

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
    }

    private void OnDrawGizmos()
    {
        Vector3 localIn = ToLocalDirection(inDir);
        Vector3 localOut = ToLocalDirection(outDir);

        if (tileKind == TileKind.Straight)
        {
            DrawArrow(transform.position, ToHorizontalWorld(Vector3.forward), Color.cyan);
            return;
        }

        Vector3 diagonalNormal = localIn + localOut;
        Vector3 entryCenter = transform.TransformPoint(-diagonalNormal * 0.2f);
        Vector3 exitCenter = transform.TransformPoint(diagonalNormal * 0.2f);
        DrawArrow(entryCenter, ToHorizontalWorld(localIn), new Color(1f, 0.75f, 0f));
        DrawArrow(exitCenter, ToHorizontalWorld(localOut), Color.cyan);
        Gizmos.color = Color.white;
        Gizmos.DrawLine(
            transform.TransformPoint(new Vector3(-diagonalNormal.z, 0.03f, diagonalNormal.x) * 0.5f),
            transform.TransformPoint(new Vector3(diagonalNormal.z, 0.03f, -diagonalNormal.x) * 0.5f));
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

    private static void DrawArrow(Vector3 origin, Vector3 direction, Color color)
    {
        if (direction.sqrMagnitude <= Mathf.Epsilon)
            return;

        const float shaftLength = 0.55f;
        const float headLength = 0.18f;
        const float headWidth = 0.12f;

        Vector3 start = origin - direction * shaftLength * 0.5f + Vector3.up * 0.05f;
        Vector3 tip = origin + direction * shaftLength * 0.5f + Vector3.up * 0.05f;
        Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;

        Gizmos.color = color;
        Gizmos.DrawLine(start, tip);
        Gizmos.DrawLine(tip, tip - direction * headLength + side * headWidth);
        Gizmos.DrawLine(tip, tip - direction * headLength - side * headWidth);
    }
}
