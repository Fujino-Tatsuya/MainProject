using UnityEngine;

/// <summary>
/// 오너 화면에만 보이는 직선 스킬 인디케이터. 차지 표시가 HUD 호버 미리보기보다 우선한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class SkillLineIndicator : MonoBehaviour
{
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int WorldSizeId = Shader.PropertyToID("_WorldSize");
    private static readonly int LengthFadeOnId = Shader.PropertyToID(SkillLineLengthFade.EnabledProperty);
    private static readonly int LengthFadeSpanId = Shader.PropertyToID(SkillLineLengthFade.SpanProperty);
    private static readonly Vector4 WholeSpan = new(0f, 1f, 0f, 0f);

    [Header("Quad 층")]
    [SerializeField] private Transform fillQuad;
    [SerializeField] private Transform ghostQuad;
    [SerializeField] private Transform arrowQuad;

    [Header("표시")]
    [SerializeField, Min(0f)] private float groundOffset = 0.05f;
    [SerializeField, Min(0.05f)] private float arrowWidth = 0.9f;
    [SerializeField, Range(0f, 1f)] private float fillAlpha = 0.28f;
    [SerializeField, Range(0f, 1f)] private float ghostAlpha = 0.09f;
    [SerializeField, Range(0f, 1f)] private float arrowAlpha = 0.42f;

    // 필드 초기화에서 만들면 MonoBehaviour 생성자에서 CreateImpl 예외 — 처음 쓸 때 만든다.
    private MaterialPropertyBlock propertyBlock;
    private Player player;
    private PlayerSkillController skillController;
    private PlayerMovement movement;
    private Renderer fillRenderer;
    private Renderer ghostRenderer;
    private Renderer arrowRenderer;
    private ISkillPreviewSource previewSource;
    private bool chargeVisible;

    private void Awake()
    {
        player = GetComponentInParent<Player>();
        skillController = player != null ? player.GetComponent<PlayerSkillController>() : null;
        movement = player != null ? player.GetComponent<PlayerMovement>() : null;
        fillRenderer = ResolveRenderer(fillQuad);
        ghostRenderer = ResolveRenderer(ghostQuad);
        arrowRenderer = ResolveRenderer(arrowQuad);
        HideVisuals();
    }

    private void LateUpdate()
    {
        if (!HasLocalVisualAuthority())
        {
            HideVisuals();
            return;
        }

        if (chargeVisible)
            return;

        if (previewSource == null || skillController != null && skillController.IsSkillActive)
        {
            HideVisuals();
            return;
        }

        if (previewSource is Object unityObject && unityObject == null)
        {
            previewSource = null;
            HideVisuals();
            return;
        }

        // 플레이어 루트는 돌지 않는다 — 회전은 Armature 에 있다(PlayerMovement.CurrentFacing).
        Vector3 forward = movement != null ? movement.CurrentFacing : FlattenDirection(transform.forward);
        if (!previewSource.TryGetPreview(transform.position, forward, out SkillPreviewShape shape))
        {
            HideVisuals();
            return;
        }

        DrawPreview(transform.position, forward, shape);
    }

    public void ShowCharge(Vector3 origin, Vector3 direction, float startOffset, float length, float width)
    {
        if (!HasLocalVisualAuthority())
            return;

        chargeVisible = true;
        SetRendererVisible(ghostRenderer, false);
        SetRendererVisible(arrowRenderer, false);
        DrawSegment(fillQuad, fillRenderer, origin, FlattenDirection(direction),
            new SkillPreviewSegment(startOffset, length, width), fillAlpha, 0.004f, WholeSpan);
    }

    public void HideCharge()
    {
        chargeVisible = false;
        HideVisuals();
    }

    public void BeginPreview(ISkillPreviewSource source)
    {
        previewSource = source;
        if (!chargeVisible)
            HideVisuals();
    }

    public void EndPreview(ISkillPreviewSource source)
    {
        if (!ReferenceEquals(previewSource, source))
            return;

        previewSource = null;
        if (!chargeVisible)
            HideVisuals();
    }

    public void HideAll()
    {
        chargeVisible = false;
        previewSource = null;
        HideVisuals();
    }

    private void DrawPreview(Vector3 origin, Vector3 forward, SkillPreviewShape shape)
    {
        // 202: 거너 Q 미리보기는 채움(최소 사거리)·고스트(최대 사거리)를 한 사각형으로 보고 같은 길이 비율로 옅게.
        Vector4? fillSpan = null;
        Vector4? ghostSpan = null;
        if (shape.LengthFade)
        {
            float start = shape.Fill.StartOffset;
            float end = shape.Fill.StartOffset + shape.Fill.Length;
            if (shape.HasGhost)
            {
                start = Mathf.Min(start, shape.Ghost.StartOffset);
                end = Mathf.Max(end, shape.Ghost.StartOffset + shape.Ghost.Length);
            }
            fillSpan = SkillLineLengthFade.Span(shape.Fill, start, end);
            ghostSpan = SkillLineLengthFade.Span(shape.Ghost, start, end);
        }

        DrawSegment(fillQuad, fillRenderer, origin, forward, shape.Fill, fillAlpha, 0.004f, fillSpan);

        if (shape.HasGhost)
            DrawSegment(ghostQuad, ghostRenderer, origin, forward, shape.Ghost, ghostAlpha, 0.002f, ghostSpan);
        else
            SetRendererVisible(ghostRenderer, false);

        if (shape.HasArrow)
        {
            Vector3 arrowDirection = forward * shape.ArrowDirection;
            DrawSegment(arrowQuad, arrowRenderer, origin, arrowDirection,
                new SkillPreviewSegment(0f, shape.ArrowLength, arrowWidth), arrowAlpha, 0.006f);
        }
        else
        {
            SetRendererVisible(arrowRenderer, false);
        }
    }

    private void DrawSegment(
        Transform quad,
        Renderer targetRenderer,
        Vector3 origin,
        Vector3 direction,
        SkillPreviewSegment segment,
        float alpha,
        float layerOffset,
        Vector4? lengthFadeSpan = null)
    {
        if (quad == null || targetRenderer == null || !segment.IsVisible)
        {
            SetRendererVisible(targetRenderer, false);
            return;
        }

        direction = FlattenDirection(direction);
        float y = transform.position.y + groundOffset + layerOffset;
        Vector3 center = origin + direction * (segment.StartOffset + segment.Length * 0.5f);
        center.y = y;

        quad.SetPositionAndRotation(center, Quaternion.LookRotation(Vector3.up, direction));
        quad.localScale = new Vector3(segment.Width, segment.Length, 1f);

        propertyBlock ??= new MaterialPropertyBlock();
        propertyBlock.Clear();
        propertyBlock.SetFloat(AlphaId, alpha);
        propertyBlock.SetVector(WorldSizeId, new Vector4(segment.Width, segment.Length, 0f, 0f));
        // 202: 거너 Q(충전·미리보기)만 길이 비율로 옅어진다. 점 값은 머티리얼 칸(SkillLineLengthFade), 구간은 층마다.
        propertyBlock.SetFloat(LengthFadeOnId, lengthFadeSpan.HasValue ? 1f : 0f);
        propertyBlock.SetVector(LengthFadeSpanId, lengthFadeSpan ?? WholeSpan);
        targetRenderer.SetPropertyBlock(propertyBlock);
        SetRendererVisible(targetRenderer, true);
    }

    private bool HasLocalVisualAuthority()
    {
        if (player == null || !player.IsSpawned)
            return true;
        return player.IsOwner;
    }

    private static Vector3 FlattenDirection(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
    }

    private static Renderer ResolveRenderer(Transform quad) =>
        quad != null ? quad.GetComponent<Renderer>() : null;

    private void HideVisuals()
    {
        SetRendererVisible(fillRenderer, false);
        SetRendererVisible(ghostRenderer, false);
        SetRendererVisible(arrowRenderer, false);
    }

    private static void SetRendererVisible(Renderer target, bool visible)
    {
        if (target != null)
            target.enabled = visible;
    }

    private void OnDisable() => HideVisuals();
}
