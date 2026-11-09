using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보스 처치 연출 6단계 — 화면 CRT 꺼짐 암전(PLAN-boss-victory-glitch Q6). 타이틀 → 로비 때 쓰는 전체 화면 꺼짐 셰이더 <c>Title/CRTOff</c>
/// (TitleMonitorDisplay 화면 메시 꺼짐과 같은 수식)를 그대로 쓴다: 시작 순간 화면을 캡처해 맨 위 캔버스에 띄우고 <c>_Progress</c> 0 → 1.
/// 끝나면 불투명 검정이 남는다(그 아래로 로비 전환). 로컬 표시 — 각 피어가 자기 화면을 끈다.
/// </summary>
public sealed class BossVictoryCrtOff
{
    public const string ShaderName = "Title/CRTOff";
    const int SortingOrder = 900;   // 연출 오버레이(450)·HUD 위

    static readonly int IdProgress = Shader.PropertyToID("_Progress");
    static readonly int IdFxTime = Shader.PropertyToID("_FxTime");
    static readonly int IdGlowColor = Shader.PropertyToID("_GlowColor");
    static readonly int IdFlipY = Shader.PropertyToID("_FlipY");

    readonly GameObject _root;
    readonly Material _material;
    readonly Texture2D _capture;
    float _time;

    public bool Finished { get; private set; }

    /// <summary>프레임 끝(WaitForEndOfFrame 뒤)에 만든다 — 그 순간 화면이 캡처된다. 셰이더가 없으면 캡처 없이 검정으로 페이드한다.</summary>
    public BossVictoryCrtOff(string name, Shader shader, Color glowColor)
    {
        _root = new GameObject(name + "CrtOff", typeof(RectTransform));
        Canvas canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        var imageGo = new GameObject("Screen", typeof(RectTransform), typeof(RawImage));
        imageGo.transform.SetParent(_root.transform, false);
        var image = imageGo.GetComponent<RawImage>();
        image.raycastTarget = false;
        CinematicOverlay.Stretch(image.rectTransform);

        if (shader != null)
        {
            _capture = ScreenCapture.CaptureScreenshotAsTexture();
            _material = new Material(shader) { name = name + " CRT Off (runtime)", hideFlags = HideFlags.DontSave };
            _material.SetColor(IdGlowColor, glowColor);
            _material.SetFloat(IdFlipY, 0f);
            _material.SetFloat(IdProgress, 0f);
            image.texture = _capture;
            image.material = _material;
            image.color = Color.white;
        }
        else
        {
            image.texture = null;
            image.color = new Color(0f, 0f, 0f, 0f);
        }
    }

    /// <summary>진행. <paramref name="duration"/> 초에 끝(검정).</summary>
    public void Tick(float deltaTime, float duration)
    {
        if (Finished) return;
        _time += Mathf.Max(0f, deltaTime);
        float p = Mathf.Clamp01(_time / Mathf.Max(0.01f, duration));
        if (_material != null)
        {
            _material.SetFloat(IdProgress, p);
            _material.SetFloat(IdFxTime, _time);
        }
        else
        {
            RawImage image = _root.GetComponentInChildren<RawImage>();
            image.color = new Color(0f, 0f, 0f, p);
        }
        if (p >= 1f) Finished = true;
    }

    public void Dispose()
    {
        if (_root != null) Object.Destroy(_root);
        if (_material != null) Object.Destroy(_material);
        if (_capture != null) Object.Destroy(_capture);
    }
}
