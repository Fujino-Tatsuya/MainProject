using UnityEngine;

/// <summary>
/// UI 클릭 영역을 RectTransform 안의 삼각형으로 좁힌다(할 일 109 — 캐릭터 선택 패널 SELECT 셰브론 버튼).
/// 아트 텍스처가 Read/Write 꺼짐이라(아트 .meta 는 SVN 소관) <c>Image.alphaHitTestMinimumThreshold</c> 대신 쓴다.
/// 꼭짓점은 rect 정규화 좌표(0,0 = 왼쪽 아래, 1,1 = 오른쪽 위) — rect 크기·pivot 이 바뀌어도 그대로 맞는다. 판정 = <see cref="Contains"/>.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class UiTriangleRaycastFilter : MonoBehaviour, ICanvasRaycastFilter
{
    // 기본 = chevron.png(190×659) 삼각형 — 빗변 두 줄을 늘인 교점(왼쪽 끝은 둥글어 rect 밖 -7.5px ≈ -0.04)과 오른쪽 위·아래 모서리.
    // 이 값이면 그림 픽셀이 전부 안에 들고, 끝 둥근 곳 바깥 빈 픽셀 98개만 더 잡힌다(rect 밖 부분은 Graphic 판정이 먼저 거른다).
    [Tooltip("꼭짓점(rect 정규화 좌표, 0,0 = 왼쪽 아래). rect 밖 값도 된다. 기본 = chevron.png 삼각형.")]
    [SerializeField] private Vector2 vertexA = new Vector2(-0.04f, 0.5f);
    [SerializeField] private Vector2 vertexB = new Vector2(1f, 1f);
    [SerializeField] private Vector2 vertexC = new Vector2(1f, 0f);

    public Vector2 VertexA => vertexA;
    public Vector2 VertexB => vertexB;
    public Vector2 VertexC => vertexC;

    public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
    {
        var rect = (RectTransform)transform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPoint, eventCamera, out Vector2 local))
            return false;
        return Contains(local, rect.rect, vertexA, vertexB, vertexC);
    }

    /// <summary>
    /// <paramref name="localPoint"/>(RectTransform 로컬 좌표 — pivot 원점)가 <paramref name="rect"/> 안 정규화 삼각형 안인가. 변·꼭짓점 위는 안이다.
    /// 꼭짓점 순서(시계·반시계)는 상관없다. 넓이 0 삼각형은 늘 밖.
    /// </summary>
    public static bool Contains(Vector2 localPoint, Rect rect, Vector2 a, Vector2 b, Vector2 c)
    {
        if (rect.width <= 0f || rect.height <= 0f)
            return false;

        // rect 왼쪽 아래 원점의 실제 크기 좌표로 맞춘다(pivot 무관, 변 근처 오차가 rect 비율에 휘둘리지 않게).
        Vector2 size = rect.size;
        Vector2 p = localPoint - rect.min;
        a = Vector2.Scale(a, size);
        b = Vector2.Scale(b, size);
        c = Vector2.Scale(c, size);

        float area = Cross(a, b, c);
        if (Mathf.Approximately(area, 0f))
            return false;

        const float Epsilon = 1e-4f; // 변 위(부동소수 오차) = 안
        float sign = Mathf.Sign(area);
        float tolerance = Epsilon * Mathf.Abs(area);
        return Cross(a, b, p) * sign >= -tolerance &&
               Cross(b, c, p) * sign >= -tolerance &&
               Cross(c, a, p) * sign >= -tolerance;
    }

    static float Cross(Vector2 o, Vector2 u, Vector2 v) => (u.x - o.x) * (v.y - o.y) - (u.y - o.y) * (v.x - o.x);

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var rect = (RectTransform)transform;
        Rect r = rect.rect;
        Vector3 Corner(Vector2 n) => rect.TransformPoint(r.min + Vector2.Scale(n, r.size));
        Vector3 a = Corner(vertexA), b = Corner(vertexB), c = Corner(vertexC);
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(a, b);
        Gizmos.DrawLine(b, c);
        Gizmos.DrawLine(c, a);
    }
#endif
}
