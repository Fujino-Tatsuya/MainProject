using UnityEngine;

/// <summary>
/// 캔버스 스케일러를 바꾸지 않고 이 RectTransform 하나만 화면 크기에 맞춰 키우고 줄인다(할 일 283 후속 — 로비 상단 플레이어 슬롯).
/// 씬에 저장된 localScale·anchoredPosition = 기준 해상도(기본 1920×1080)에서의 값. Play 중 배율 = min(캔버스 가로/기준 가로, 캔버스 세로/기준 세로)
/// (CanvasScaler Expand 와 같은 규칙 — 기준 사각형이 어떤 화면비에서도 잘리지 않음)을 두 값에 곱한다.
/// 캔버스 크기는 캔버스 단위라, 부모 캔버스가 나중에 Scale With Screen Size 로 바뀌어도 배율이 1 로 돌아와 이중으로 커지지 않는다.
/// 편집 모드에선 아무것도 안 한다(씬 값이 Game 뷰 크기로 바뀌어 저장되는 것을 막음). 문서: Docs/tech/lobby-player-slots.md
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class ScreenFitScale : MonoBehaviour
{
    [Tooltip("씬에 저장된 크기·위치가 맞춰진 해상도.")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);

    private RectTransform _rect;
    private RectTransform _canvasRect;
    private Vector3 _baseScale;
    private Vector2 _basePosition;
    private Vector2 _appliedCanvasSize = new Vector2(-1f, -1f);
    private bool _captured;

    public Vector2 ReferenceResolution => referenceResolution;

    /// <summary>캔버스 크기(캔버스 단위) → 배율. 기준 사각형이 다 들어오는 가장 큰 배율(Expand).</summary>
    public static float Factor(Vector2 canvasSize, Vector2 reference)
    {
        if (reference.x <= 0f || reference.y <= 0f || canvasSize.x <= 0f || canvasSize.y <= 0f)
        {
            return 1f;
        }

        return Mathf.Min(canvasSize.x / reference.x, canvasSize.y / reference.y);
    }

    private void Awake()
    {
        Capture();
    }

    private void OnEnable()
    {
        _appliedCanvasSize = new Vector2(-1f, -1f);
        Refresh();
    }

    private void LateUpdate()
    {
        Refresh();
    }

    /// <summary>캔버스 크기가 바뀌었으면 다시 맞춘다. 캡처 도구가 편집 모드에서 직접 부를 수도 있다.</summary>
    public void Refresh()
    {
        Capture();
        if (_canvasRect == null)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                return;
            }

            _canvasRect = (RectTransform)canvas.rootCanvas.transform;
        }

        Vector2 size = _canvasRect.rect.size;
        if (size == _appliedCanvasSize)
        {
            return;
        }

        _appliedCanvasSize = size;
        float factor = Factor(size, referenceResolution);
        _rect.localScale = _baseScale * factor;
        _rect.anchoredPosition = _basePosition * factor;
    }

    /// <summary>씬 값으로 되돌린다(캡처 도구용).</summary>
    public void Restore()
    {
        if (!_captured)
        {
            return;
        }

        _rect.localScale = _baseScale;
        _rect.anchoredPosition = _basePosition;
        _appliedCanvasSize = new Vector2(-1f, -1f);
    }

    private void Capture()
    {
        if (_captured)
        {
            return;
        }

        _rect = (RectTransform)transform;
        _baseScale = _rect.localScale;
        _basePosition = _rect.anchoredPosition;
        _captured = true;
    }
}
