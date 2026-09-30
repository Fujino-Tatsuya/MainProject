using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PRESS ANY KEY 를 "모니터에 그려지는" 룩으로 — 전용 카메라가 소형 RT 에 글자를 그리고,
/// 오버레이 RawImage 가 <c>Title/CRTUI</c> 로 그 RT 를 보여 준다(팀장 09-23: 단순 깜빡임 금지).
/// </summary>
/// <remarks>
/// 이 컴포넌트가 켜지고 꺼질 때 소스 캔버스·카메라도 같이 켜고 끈다 → 글자의 <see cref="TextScramble"/> 이 매번 다시 디코드된다.
/// 🔴 RawImage 와 그 부모에 Mask/RectMask2D 를 두지 말 것 — 셰이더가 스텐실을 지원하지 않는다.
/// </remarks>
[RequireComponent(typeof(RawImage))]
public sealed class TitleOverlayCrtImage : MonoBehaviour
{
    [SerializeField] private Camera _sourceCamera;
    [SerializeField] private GameObject _sourceCanvas;
    [SerializeField] private Material _materialSource;
    [SerializeField] private Vector2Int _size = new(1024, 256);

    private RawImage _image;
    private RenderTexture _rt;
    private Material _material;

    private void Awake()
    {
        _image = GetComponent<RawImage>();
        _rt = new RenderTexture(_size.x, _size.y, 24, RenderTextureFormat.ARGB32) { name = "TitlePressRT" };
        _rt.Create();
        if (_sourceCamera != null)
        {
            _sourceCamera.targetTexture = _rt;
            _sourceCamera.clearFlags = CameraClearFlags.SolidColor;
            _sourceCamera.backgroundColor = Color.clear; // 🔴 불투명 검정이면 글자 뒤에 검은 사각형이 생긴다
        }

        if (_materialSource != null)
        {
            _material = new Material(_materialSource) { name = "TitleCRTUI (Instance)" };
            _image.material = _material;
        }
        _image.texture = _rt;
        _image.raycastTarget = false;
    }

    private void OnEnable()
    {
        if (_sourceCanvas != null) _sourceCanvas.SetActive(true);
        if (_sourceCamera != null) _sourceCamera.enabled = true;
        if (TitleCrtFx.Active != null) TitleCrtFx.Active.Register(_material);
    }

    private void Start()
    {
        // OnEnable 시점엔 TitleCrtFx 가 아직 안 켜졌을 수 있다(씬 로드 순서).
        if (TitleCrtFx.Active != null) TitleCrtFx.Active.Register(_material);
    }

    private void OnDisable()
    {
        if (_sourceCanvas != null) _sourceCanvas.SetActive(false);
        if (_sourceCamera != null) _sourceCamera.enabled = false;
        if (TitleCrtFx.Active != null) TitleCrtFx.Active.Unregister(_material);
    }

    private void OnDestroy()
    {
        if (_sourceCamera != null) _sourceCamera.targetTexture = null;
        if (_rt != null) { _rt.Release(); Destroy(_rt); }
        if (_material != null) Destroy(_material);
    }
}
