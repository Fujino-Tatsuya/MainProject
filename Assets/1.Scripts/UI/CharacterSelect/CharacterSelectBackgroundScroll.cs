using UnityEngine;

/// <summary>
/// 캐릭터 선택 배경 타이포 패턴 흐름(할 일 48.2) — 이음새 없는 루프 타일(bg_typography_loop 2900×1888, 가로 줄무늬)을 2×2 로 깔고
/// 묶음 위치를 <c>Time.unscaledTime</c> 으로 옮긴다(아트 참고 영상 scroll_40px_per_s = 40px/s).
/// 아트 텍스처가 Wrap=Clamp 라(.meta 는 SVN 소관이라 못 바꾼다) RawImage uvRect 대신 타일 4장을 움직인다.
/// 목업처럼 비스듬히 보이는 건 부모(Bg_Rotate)의 Z 회전 — 이 컴포넌트는 부모의 회전된 좌표에서 움직인다.
/// 이 컴포넌트가 붙은 RectTransform = 타일 묶음(부모 가운데 앵커, 피벗 좌상단).
/// </summary>
[DisallowMultipleComponent]
public sealed class CharacterSelectBackgroundScroll : MonoBehaviour
{
    [Tooltip("타일 한 장 크기(캔버스 px). 저작 메뉴가 Sprite 원래 크기로 채운다.")]
    [SerializeField] private Vector2 tileSize = new Vector2(2900f, 1888f);
    [Tooltip("위상 0 일 때 묶음 좌상단에서 타일 한 장 뺀 기준점(부모 가운데 기준). 회전된 화면을 어느 위상에서도 덮게 저작 메뉴가 정한다.")]
    [SerializeField] private Vector2 gridOrigin = new Vector2(-1450f, 944f);
    [Tooltip("흐르는 방향(부모 회전 좌표 — x = 줄무늬 방향, y 위). 기본 = 줄무늬를 따라 오른쪽.")]
    [SerializeField] private Vector2 direction = new Vector2(1f, 0f);
    [Tooltip("초당 px(unscaled). 0 이면 멈춘다.")]
    [SerializeField] private float speed = 40f;

    private RectTransform _rect;
    private float _startTime;

    private void OnEnable()
    {
        _rect = (RectTransform)transform;
        _startTime = Time.unscaledTime;
        Apply(0f);
    }

    private void Update() => Apply(Time.unscaledTime - _startTime);

    private void Apply(float elapsed)
    {
        Vector2 velocity = direction.sqrMagnitude > 0f ? direction.normalized * speed : Vector2.zero;
        _rect.anchoredPosition = GridPosition(Phase(velocity, elapsed, tileSize), tileSize, gridOrigin);
    }

    /// <summary>흐른 거리를 타일 크기로 접은 값 — 축마다 [0, tile).</summary>
    public static Vector2 Phase(Vector2 velocity, float elapsed, Vector2 tile) =>
        new Vector2(Repeat(velocity.x * elapsed, tile.x), Repeat(velocity.y * elapsed, tile.y));

    /// <summary>
    /// 2×2 묶음 좌상단 위치(부모 가운데 기준, y 위). x ∈ [origin.x - tile.x, origin.x), y ∈ [origin.y, origin.y + tile.y).
    /// </summary>
    public static Vector2 GridPosition(Vector2 phase, Vector2 tile, Vector2 origin) =>
        new Vector2(phase.x - tile.x + origin.x, phase.y + origin.y);

    /// <summary>
    /// <paramref name="screen"/> 를 <paramref name="degrees"/> 돌렸을 때 그 회전 좌표에서 화면을 감싸는 상자의 반 크기.
    /// 묶음이 덮어야 하는 범위 = 가운데 ± 이 값.
    /// </summary>
    public static Vector2 RotatedHalfExtents(Vector2 screen, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float c = Mathf.Abs(Mathf.Cos(rad)), s = Mathf.Abs(Mathf.Sin(rad));
        return new Vector2(screen.x * c + screen.y * s, screen.x * s + screen.y * c) * 0.5f;
    }

    /// <summary>
    /// 어느 위상에서도 덮는 기준점 — 가능한 범위의 가운데. 타일이 모자라면(2×타일 &lt; 타일 + 감싸는 상자) false.
    /// x: origin ≤ -half.x 이고 origin + tile ≥ half.x. y: origin ≥ half.y 이고 origin - tile ≤ -half.y.
    /// </summary>
    public static bool TryCoveringOrigin(Vector2 tile, Vector2 half, out Vector2 origin)
    {
        float xMin = half.x - tile.x, xMax = -half.x;
        float yMin = half.y, yMax = tile.y - half.y;
        origin = new Vector2((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f);
        return xMin <= xMax && yMin <= yMax;
    }

    private static float Repeat(float value, float length) => length > 0f ? Mathf.Repeat(value, length) : 0f;
}
