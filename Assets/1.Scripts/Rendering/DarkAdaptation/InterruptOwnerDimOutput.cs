using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>오너 마스크 암전의 색·불투명도 칸(할 일 132) — 디렉터가 구현하고, 강도가 바뀔 때마다 읽는다.</summary>
public interface IInterruptOwnerDimLook
{
    /// <summary>덮개 색(RGB만 쓴다). 기본 검정.</summary>
    Color OwnerDimColor { get; }
    /// <summary>강도 1 에서의 덮개 불투명도 [0, 1]. 기본 1(100%).</summary>
    float OwnerDimOpacity { get; }
}

/// <summary>
/// 오너 마스크 암전 실제 출력(할 일 132 단계 3) — <see cref="InterruptOwnerPresentation"/> 의 <see cref="IInterruptOwnerDimOutput"/>.
/// <list type="bullet">
/// <item>강도 0 → 양수: 남길 대상(오너 캐릭터·이번 시도 간파 대상·<see cref="KeepWhileDimmed"/> 로 넣은 루트)에 렌더링 레이어 비트 4 를 붙인다
/// (<see cref="InterruptOwnerMaskTagger"/> — 그 순간 한 번 렌더러를 모은다).</item>
/// <item>양수 동안: <see cref="DarkAdaptationState.MaskIntensity"/> 로 렌더 기능의 마스크 패스를 켜고, HUD 위 덮개 캔버스(<see cref="InterruptOwnerHudCover"/>)를 칸 색·불투명도 × 강도로 깐다.</item>
/// <item>양수 → 0: 비트 원복, 덮개 끔, 마스크 패스 끔(평소 비용 0).</item>
/// </list>
/// 오너 화면에서만 불린다(상태 기계가 남의 슬로우를 무시한다). 팀원 휘도 암순응(<see cref="InterruptDarkAdaptation"/>)과는 따로다.
/// </summary>
public sealed class InterruptOwnerDimOutput : IInterruptOwnerDimOutput, IDisposable
{
    private readonly IInterruptOwnerDimLook _look;
    private readonly InterruptOwnerHudCover _hud;
    private readonly InterruptOwnerMaskTagger _tagger = new InterruptOwnerMaskTagger();
    private readonly List<GameObject> _extras = new List<GameObject>();

    private GameObject _owner;
    private GameObject _target;

    /// <param name="useHudCover">false = 덮개 캔버스 없이(EditMode 테스트) — 마스크 패스도 대상이 없어 돌지 않는다.</param>
    public InterruptOwnerDimOutput(IInterruptOwnerDimLook look, bool useHudCover = true)
    {
        _look = look;
        _hud = useHudCover ? new InterruptOwnerHudCover() : null;
    }

    /// <summary>비트를 붙여 둔 상태(강도 양수).</summary>
    public bool IsActive { get; private set; }
    /// <summary>지금 비트를 붙여 둔 렌더러 수.</summary>
    public int TaggedCount => _tagger.Count;
    /// <summary>마지막으로 깐 덮개 색(알파 = 불투명도 × 강도).</summary>
    public Color CoverColor { get; private set; }

    /// <summary>
    /// [오너] 이번 시도의 남길 대상 — 강 예측 입력 순간 디렉터가 넘긴다. 다음 암전 시작(0 → 양수)에 태깅된다(이미 켜져 있으면 그다음 시작부터).
    /// <see cref="KeepWhileDimmed"/> 목록은 새 시도라 비운다.
    /// </summary>
    public void SetSubjects(GameObject owner, GameObject target)
    {
        _owner = owner;
        _target = target;
        if (!IsActive)
            _extras.Clear();
    }

    /// <summary>
    /// 이번 암전에 함께 남길 루트(빛 모임 등 — 단계 4 가 붙인다). 켜져 있으면 바로 태깅, 아니면 다음 시작에. 걷히면 목록에서 빠진다.
    /// </summary>
    public void KeepWhileDimmed(GameObject root)
    {
        if (root == null || _extras.Contains(root))
            return;

        _extras.Add(root);
        if (IsActive)
            _tagger.Tag(root);
    }

    public void SetOwnerDim(float intensity)
    {
        float value = Mathf.Clamp01(intensity);
        if (value <= 0f)
        {
            Deactivate();
            return;
        }

        if (!IsActive)
        {
            IsActive = true;
            _tagger.Tag(_owner);
            _tagger.Tag(_target);
            for (int i = 0; i < _extras.Count; i++)
                _tagger.Tag(_extras[i]);
        }

        CoverColor = DarkAdaptationModes.CoverColor(
            _look != null ? _look.OwnerDimColor : Color.black, _look != null ? _look.OwnerDimOpacity : 1f, value);
        DarkAdaptationState.MaskIntensity = CoverColor.a;
        if (_hud != null)
        {
            _hud.Show(CoverColor);
            DarkAdaptationState.MaskCoverTarget = _hud.Target;
        }
    }

    public void Dispose()
    {
        Deactivate();
        _hud?.Dispose();
    }

    private void Deactivate()
    {
        CoverColor = Color.clear;
        DarkAdaptationState.ClearMask();
        _hud?.Hide();
        if (!IsActive)
            return;

        IsActive = false;
        _tagger.RestoreAll();
        _extras.Clear();
    }
}

/// <summary>
/// 오너 암전 HUD 덮개(할 일 132) — 최상위 Screen Space Overlay 캔버스 하나에 화면 전체 RawImage.
/// 텍스처 = 렌더 기능이 쓴 덮개 RT(흰색 + 알파 1 − 남김), 색 = 칸 색·불투명도 × 강도 → 남길 오브젝트 픽셀만 뚫리고 월드·HUD 전부 덮인다.
/// 오버레이 UI 는 URP 가 카메라 렌더 맨 끝에 그려서, 같은 프레임의 덮개 RT 를 그대로 쓴다.
/// 순서: <see cref="SortingOrder"/> = 적중 섬광 오버레이(<see cref="InterruptHitFlashOverlay.SortingOrder"/>) 바로 아래, 나머지 모든 UI 위.
/// 입력은 받지 않는다(레이캐스터 없음).
/// </summary>
public sealed class InterruptOwnerHudCover : IDisposable
{
    public const int SortingOrder = InterruptHitFlashOverlay.SortingOrder - 1;

    private GameObject _root;
    private Canvas _canvas;
    private RawImage _image;

    /// <summary>덮개 RT — 화면 크기, 첫 사용 때 만들고 화면 크기가 바뀌면 다시 만든다.</summary>
    public RenderTexture Target { get; private set; }

    public void Show(Color coverColor)
    {
        EnsureCanvas();
        EnsureTarget();
        _image.texture = Target;
        _image.color = coverColor;
        _canvas.enabled = coverColor.a > 0f;
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
        _root = null;
        _canvas = null;
        _image = null;

        if (Target != null)
        {
            Target.Release();
            UnityEngine.Object.Destroy(Target);
        }
        Target = null;
    }

    private void EnsureCanvas()
    {
        if (_canvas != null)
            return;

        _root = new GameObject("[InterruptOwnerHudCover]");
        if (Application.isPlaying)
            UnityEngine.Object.DontDestroyOnLoad(_root);

        _canvas = _root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = SortingOrder;

        var imageObject = new GameObject("Cover", typeof(RectTransform));
        imageObject.transform.SetParent(_root.transform, false);
        var rect = (RectTransform)imageObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        _image = imageObject.AddComponent<RawImage>();
        _image.raycastTarget = false;
        _canvas.enabled = false;
    }

    private void EnsureTarget()
    {
        int width = Mathf.Max(1, Screen.width);
        int height = Mathf.Max(1, Screen.height);
        if (Target != null && Target.width == width && Target.height == height)
            return;

        if (Target != null)
        {
            Target.Release();
            UnityEngine.Object.Destroy(Target);
        }

        // 알파만 쓴다(RGB 는 늘 흰색) — 선형 RGBA8.
        Target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        {
            name = "_InterruptOwnerDimCover",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        Target.Create();
    }
}
