using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// <see cref="UiTriangleRaycastFilter"/> 삼각형 바깥을 두르는 테두리 선(할 일 109 — SELECT 셰브론 hover 아웃라인).
/// 텍스처 없이 정점 색으로만 그려 <c>color</c> 가 그대로 나온다(셰브론 아트는 회색이라 Image 색을 곱해선 흰색이 안 된다).
/// 선은 삼각형 밖(<see cref="gap"/> 만큼 띄우고 <see cref="thickness"/> 두께)에만 그려져, 자식으로 위에 그려도 셰브론을 가리지 않는다.
/// 모서리 = 마이터 이음. 켜고 끄기 = <see cref="UiHoverOutline"/>. 클릭·hover 판정에 끼지 않게 Raycast Target 은 끈다.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UiTriangleOutline : MaskableGraphic
{
    [Tooltip("모양 원본 — 이 필터의 꼭짓점·rect 를 따른다(보통 부모 셰브론).")]
    [SerializeField] private UiTriangleRaycastFilter shape;
    [Tooltip("선 두께(px, 캔버스 기준 해상도).")]
    [Min(0f)] [SerializeField] private float thickness = 6f;
    [Tooltip("삼각형 변에서 선 안쪽 가장자리까지 띄우는 거리(px).")]
    [Min(0f)] [SerializeField] private float gap = 3f;

    public UiTriangleRaycastFilter Shape
    {
        get => shape;
        set { shape = value; SetVerticesDirty(); }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (shape == null || thickness <= 0f)
            return;

        var shapeRect = (RectTransform)shape.transform;
        Rect r = shapeRect.rect;
        if (r.width <= 0f || r.height <= 0f)
            return;

        // 필터 rect 정규화 꼭짓점 → 이 Graphic 로컬 좌표(부모·자식 배치가 달라도 맞는다)
        Matrix4x4 shapeToLocal = ShapeToLocal(shapeRect);
        Vector2 Local(Vector2 n) => shapeToLocal.MultiplyPoint3x4(r.min + Vector2.Scale(n, r.size));
        Vector2[] v = { Local(shape.VertexA), Local(shape.VertexB), Local(shape.VertexC) };

        float area = (v[1].x - v[0].x) * (v[2].y - v[0].y) - (v[1].y - v[0].y) * (v[2].x - v[0].x);
        if (Mathf.Approximately(area, 0f))
            return;
        float outward = area > 0f ? 1f : -1f; // 반시계면 변 p→q 의 (dy, -dx) 가 바깥

        var inner = new Vector2[3];
        var outer = new Vector2[3];
        for (int i = 0; i < 3; i++)
        {
            Vector2 n0 = Normal(v[(i + 2) % 3], v[i], outward);
            Vector2 n1 = Normal(v[i], v[(i + 1) % 3], outward);
            Vector2 miter = (n0 + n1) / (1f + Vector2.Dot(n0, n1)); // 두 변을 각각 1 만큼 밀었을 때 꼭짓점 이동
            inner[i] = v[i] + miter * gap;
            outer[i] = v[i] + miter * (gap + thickness);
        }

        Color32 c = color;
        for (int i = 0; i < 3; i++)
        {
            int j = (i + 1) % 3;
            int start = vh.currentVertCount;
            vh.AddVert(inner[i], c, Vector4.zero);
            vh.AddVert(outer[i], c, Vector4.zero);
            vh.AddVert(outer[j], c, Vector4.zero);
            vh.AddVert(inner[j], c, Vector4.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }

    /// <summary>
    /// 필터 로컬 → 이 Graphic 로컬. 필터가 조상이면 로컬 TRS 만 거슬러 곱한다 —
    /// 저장된 Overlay Canvas 루트는 scale 0 이라(프리뷰 씬·켜지기 전) 월드 좌표를 거치면 깨진다.
    /// </summary>
    Matrix4x4 ShapeToLocal(Transform shapeTransform)
    {
        Matrix4x4 m = Matrix4x4.identity;
        for (Transform t = transform; t != null; t = t.parent)
        {
            if (t == shapeTransform)
                return m;
            m *= Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale).inverse;
        }
        return transform.worldToLocalMatrix * shapeTransform.localToWorldMatrix;
    }

    static Vector2 Normal(Vector2 p, Vector2 q, float outward)
    {
        Vector2 d = (q - p).normalized;
        return new Vector2(d.y, -d.x) * outward;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        SetVerticesDirty();
    }
#endif
}
