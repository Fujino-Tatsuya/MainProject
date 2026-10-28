using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 적중 섬광 오너 화면 오버레이(할 일 132 단계 5) — <see cref="IInterruptHitFlashOverlayOutput"/> 실제 출력.
/// 오너 암전 덮개(<see cref="InterruptOwnerHudCover"/>)와 같은 방식 — 최상위 Screen Space Overlay 캔버스 하나에 화면 전체 RawImage.
/// 월드·HUD·암전 덮개 전부 위에 그린다(<see cref="SortingOrder"/> = PerformanceOverlay 바로 아래, 암전 덮개 바로 위). 입력은 받지 않는다.
///
/// <list type="bullet">
/// <item>그림 = 재질(<c>BeaverLobby/UI/InterruptHitFlashOverlay</c>)이 단계·진행도·적중 지점 화면 위치로 절차 생성한다.
///       재질의 단계별 '덮어쓸 텍스처' 칸이 차면 그 단계는 그 텍스처(화면 전체 UV).</item>
/// <item>캔버스와 재질 사본은 첫 재생 때 한 번 만들어 재사용한다(매 재생 생성 없음 — 재질 에셋을 런타임에 바꾸지 않으려고 사본 하나).</item>
/// </list>
/// </summary>
public sealed class InterruptHitFlashOverlay : IInterruptHitFlashOverlayOutput, IDisposable
{
    public const int SortingOrder = short.MaxValue - 1;

    private static readonly int StageId = Shader.PropertyToID("_Stage");
    private static readonly int ProgressId = Shader.PropertyToID("_Progress");
    private static readonly int CenterId = Shader.PropertyToID("_Center");
    private static readonly int AspectId = Shader.PropertyToID("_Aspect");
    private static readonly int SpeedLinesId = Shader.PropertyToID("_SpeedLines");

    private readonly Func<Material> _material;
    private GameObject _root;
    private Canvas _canvas;
    private RawImage _image;
    private Material _instance;

    /// <param name="material">재질 에셋(디렉터 칸) — 처음 보일 때 읽는다. 비면 아무것도 안 그린다.</param>
    public InterruptHitFlashOverlay(Func<Material> material)
    {
        _material = material;
    }

    public void Show(InterruptHitFlashStage stage, float progress, Vector3 worldCenter, bool speedLines)
    {
        if (!EnsureCanvas())
            return;

        _instance.SetFloat(StageId, (float)stage);
        _instance.SetFloat(ProgressId, Mathf.Clamp01(progress));
        _instance.SetVector(CenterId, ScreenCenter(worldCenter));
        _instance.SetFloat(AspectId, Screen.height > 0 ? (float)Screen.width / Screen.height : 1f);
        _instance.SetFloat(SpeedLinesId, speedLines ? 1f : 0f);
        _canvas.enabled = true;
    }

    public void Hide()
    {
        if (_canvas != null)
            _canvas.enabled = false;
    }

    public void Dispose()
    {
        if (_root != null)
            UnityEngine.Object.Destroy(_root);
        if (_instance != null)
            UnityEngine.Object.Destroy(_instance);
        _root = null;
        _canvas = null;
        _image = null;
        _instance = null;
    }

    // 적중 지점의 메인 카메라 뷰포트. 카메라가 없거나 뒤면 화면 가운데.
    private static Vector2 ScreenCenter(Vector3 worldCenter)
    {
        Camera camera = Camera.main;
        if (camera == null)
            return new Vector2(0.5f, 0.5f);

        Vector3 point = camera.WorldToViewportPoint(worldCenter);
        if (point.z <= 0f || float.IsNaN(point.x) || float.IsNaN(point.y))
            return new Vector2(0.5f, 0.5f);

        return new Vector2(point.x, point.y);
    }

    private bool EnsureCanvas()
    {
        if (_canvas != null)
            return true;

        Material source = _material?.Invoke();
        if (source == null)
            return false;

        _instance = new Material(source) { name = source.name + " (Runtime)" };
        InterruptHitFlashRules.ApplyOverlayKeywords(_instance);

        _root = new GameObject("[InterruptHitFlashOverlay]");
        if (Application.isPlaying)
            UnityEngine.Object.DontDestroyOnLoad(_root);

        _canvas = _root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = SortingOrder;

        var imageObject = new GameObject("Flash", typeof(RectTransform));
        imageObject.transform.SetParent(_root.transform, false);
        var rect = (RectTransform)imageObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        _image = imageObject.AddComponent<RawImage>();
        _image.raycastTarget = false;
        _image.material = _instance;
        _canvas.enabled = false;
        return true;
    }
}
