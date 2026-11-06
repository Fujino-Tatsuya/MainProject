using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// 제압 가속 비네팅(할 일 91 T7, PLAN-91 §3.5) — 로컬 플레이어에게 제압 가속 대표 상태이상(<see cref="StatusEffectType.CooldownRateModifier"/>)이
/// 있는 동안 화면 가장자리 연출을 페이드 인, 없어지면 페이드 아웃(순수 클라 연출).
/// - 249: 주인공 = 셰이더 절차 생성 스피드 라인(재질 MA_SpeedLines, 셰이더 UI/SpeedLines) — Awake 에서 만드는 자식 RawImage.
///   가장자리에서 중앙으로 향하는 방사형 선이 갱신 fps 마다 위치·길이·두께가 바뀌며 깜빡이고, 중앙(타원)은 비운다.
///   켜지는 순간 버스트 시간 동안 선 밀도·알파가 버스트 배율만큼 높았다가 은은하게 내려온다.
///   기존 흰 가장자리 스프라이트(이 오브젝트의 Image)는 은은한 채움으로 남는다(채움 알파 0 = 선만).
/// - 표시 여부는 <see cref="StatusEffectController.EffectsChanged"/> 때만 다시 읽는다(매 프레임 탐색 없음).
/// - 로컬 플레이어는 <see cref="Player.LocalPlayerChanged"/> 로 직접 받는다 — CombatHUD 코드는 건드리지 않고 프리팹 인스턴스만 둔다.
/// - 페이드 시간은 xlsx <c>SuppressHaste</c> 시트(<see cref="SuppressHasteData"/>). 스피드 라인·버스트 칸은 이 HUD 인스펙터(시트 대상 아님).
/// 페이드는 <see cref="SuppressHasteVignetteModel.StepAlpha"/>, 버스트·프레임도 <see cref="SuppressHasteVignetteModel"/>.
/// 에디터 미리보기: Play 가 아닐 때 "에디터 미리보기" 를 켜면 저장 안 되는 자식으로 그 강도·버스트 시점의 모습을 그린다
/// (<see cref="VignetteEditorPreview"/>). 실제 Image 는 건드리지 않고, Play·빌드에선 미리보기 칸을 무시한다.
/// </summary>
[ExecuteAlways]
public class SuppressHasteVignetteHUD : MonoBehaviour
{
    [SerializeField] private Image image;
    [Tooltip("가장자리 채움 스프라이트(가장자리 불투명·중앙 투명). 이미지로 바꿀 땐 이것만 교체한다.")]
    [SerializeField] private Sprite vignetteSprite;

    [Header("가장자리 채움")]
    [Tooltip("스프라이트에 곱하는 색. 흰색이면 스프라이트 색 그대로.")]
    [SerializeField] private Color vignetteColor = Color.white;
    [Tooltip("버프가 있을 때 채움 알파(0 = 스피드 라인만).")]
    [FormerlySerializedAs("maxAlpha")]
    [SerializeField, Range(0f, 1f)] private float fillAlpha = 0.25f;

    [Header("스피드 라인(249)")]
    [Tooltip("스피드 라인 재질 — MA_SpeedLines(셰이더 UI/SpeedLines). 비면 스피드 라인 없음(채움만).")]
    [SerializeField] private Material speedLineMaterial;
    [Tooltip("선 색.")]
    [SerializeField] private Color lineColor = Color.white;
    [Tooltip("버프가 있을 때 선 알파(버스트 중엔 × 버스트 배율).")]
    [SerializeField, Range(0f, 1f)] private float lineAlpha = 0.55f;
    [Tooltip("한 바퀴를 나누는 칸 수 = 선이 놓일 수 있는 자리 수.")]
    [SerializeField, Range(8, 400)] private int lineCount = 90;
    [Tooltip("프레임마다 선이 켜지는 칸 비율(밀도, 버스트 중엔 × 버스트 배율).")]
    [SerializeField, Range(0f, 1f)] private float lineDensity = 0.5f;
    [Tooltip("선 두께 = 칸 폭 대비(바깥 끝 기준, 안쪽으로 갈수록 가늘어짐). 선마다 35~100% 랜덤.")]
    [SerializeField, Range(0.01f, 1f)] private float lineThickness = 0.35f;
    [Tooltip("선 안쪽 끝 반경 최소(0 = 화면 중심, 1 = 변 가운데, 1.41 = 모서리). 선마다 최소~최대 랜덤.")]
    [SerializeField, Range(0f, 1.5f)] private float lineInnerMin = 0.75f;
    [Tooltip("선 안쪽 끝 반경 최대.")]
    [SerializeField, Range(0f, 1.5f)] private float lineInnerMax = 1.05f;
    [Tooltip("선 배치가 바뀌는 횟수(초당). 0 = 고정.")]
    [SerializeField, Min(0f)] private float lineFps = 24f;
    [Tooltip("중앙 빈 영역 반경(타원, 화면비에 맞춤). 이 안엔 선이 없다.")]
    [SerializeField, Range(0f, 1.5f)] private float centerClearRadius = 0.6f;

    [Header("켜짐 버스트(249)")]
    [Tooltip("켜지는 순간 강하게 터지는 시간(초). 0 = 버스트 없음.")]
    [SerializeField, Min(0f)] private float burstDuration = 0.2f;
    [Tooltip("버스트 순간 선 밀도·알파 배율 — 버스트 시간 동안 1 까지 내려온다.")]
    [SerializeField, Min(1f)] private float burstMultiplier = 1.8f;

    [Header("페이드(시트 값)")]
    [Tooltip("페이드 인/아웃 시간을 읽는 시트 에셋. 비우면 Resources/" + SuppressHasteData.ResourcesPath + ".")]
    [SerializeField] private SuppressHasteData data;

    [Header("표시")]
    [Tooltip("알파가 이 값 이하이면 이미지를 꺼서 전체 화면 투명 드로우를 생략한다.")]
    [SerializeField, Range(0f, 0.1f)] private float visibleAlphaThreshold = 0.001f;

    // 빌드에선 읽는 곳이 없다(에디터 전용 미리보기) — 직렬화 배치를 에디터·빌드 같게 두려고 #if 밖에 둔다.
#pragma warning disable 0414, 0169
    [Header("에디터 미리보기(Play·빌드에선 무시)")]
    [Tooltip("켜면 Play 없이 Prefab Mode·씬뷰·게임뷰에서 채움+스피드 라인을 아래 값으로 보여 준다(저장 안 되는 임시 자식). 실제 Image 값은 바뀌지 않는다.")]
    [SerializeField] private bool editorPreview;
    [Tooltip("미리보기 강도 = 페이드 비율(0 = 안 보임, 1 = 버프가 있을 때 최종 모습).")]
    [SerializeField, Range(0f, 1f)] private float editorPreviewStrength = 1f;
    [Tooltip("미리보기 버스트 시점(0 = 켜진 순간, 1 = 버스트 끝 — 은은한 유지 상태).")]
    [SerializeField, Range(0f, 1f)] private float editorPreviewBurstTime = 1f;
    [Tooltip("미리보기 선 배치 번호 — 바꾸면 다른 프레임의 선 배치를 본다.")]
    [SerializeField] private int editorPreviewFrame;
#pragma warning restore 0414, 0169

    private StatusEffectController effects;
    private bool show;
    private float fade;                 // 0..1 — 채움 알파 = × fillAlpha, 선 알파 = × lineAlpha × 버스트
    private float burstElapsed = float.MaxValue;
    private float lineTime;

    private RawImage lineImage;
    private Material lineMaterialInstance;
    private RectTransform rectTransform;

    private static readonly int LinesId = Shader.PropertyToID("_Lines");
    private static readonly int LineFrameId = Shader.PropertyToID("_LineFrame");

    private void Awake()
    {
        rectTransform = transform as RectTransform;
        if (!Application.isPlaying)
            return;   // [ExecuteAlways] — 에디터에선 실제 Image 를 건드리지 않는다(미리보기는 별도 자식)
#if UNITY_EDITOR
        VignetteEditorPreview.Clear(transform);   // 씬 리로드 없이 Play 에 들어와 남은 미리보기 자식
#endif

        if (image == null)
            image = GetComponent<Image>();

        if (data == null)
            data = Resources.Load<SuppressHasteData>(SuppressHasteData.ResourcesPath);

        if (image != null)
        {
            image.raycastTarget = false;
            if (vignetteSprite != null)
                image.sprite = vignetteSprite;
        }

        CreateLineLayer();
    }

    // 자식으로 붙여 채움 위에 그린다. 프리팹엔 재질 참조만 두고 오브젝트는 런타임에 만든다(프리팹 변경 최소화).
    private void CreateLineLayer()
    {
        if (speedLineMaterial == null || lineImage != null)
            return;

        var go = new GameObject("SpeedLines", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        go.layer = gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        lineImage = go.GetComponent<RawImage>();
        lineImage.raycastTarget = false;
        lineImage.maskable = false;
        lineImage.enabled = false;
        // 값은 매 프레임 바꾸므로 공용 재질 에셋 대신 인스턴스에 쓴다.
        lineMaterialInstance = new Material(speedLineMaterial) { name = speedLineMaterial.name + " (Instance)", hideFlags = HideFlags.DontSave };
        lineImage.material = lineMaterialInstance;
    }

    private void OnEnable()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            RefreshPreview();
            return;
        }
#endif
        Player.LocalPlayerChanged += Bind;
        Bind(Player.LocalPlayer);
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            ClearPreview();
            return;
        }
#endif
        Player.LocalPlayerChanged -= Bind;
        Bind(null);
    }

    private void OnDestroy()
    {
        if (lineMaterialInstance != null)
            Destroy(lineMaterialInstance);
    }

    private void Bind(Player player)
    {
        if (effects != null)
            effects.EffectsChanged -= RefreshShow;

        effects = player != null ? player.GetComponent<StatusEffectController>() : null;

        if (effects != null)
            effects.EffectsChanged += RefreshShow;

        // 다른 플레이어로 바뀌면 페이드를 이어 받지 않고 현재 상태로 바로 맞춘다(첫 동기화엔 이벤트가 안 나므로 여기서 한 번 읽는다).
        // 이미 켜진 상태로 붙으면 버스트 없이 유지 상태로.
        show = effects != null && effects.Has(StatusEffectType.CooldownRateModifier);
        fade = show ? 1f : 0f;
        burstElapsed = float.MaxValue;
        Apply();
    }

    private void RefreshShow()
    {
        bool next = effects != null && effects.Has(StatusEffectType.CooldownRateModifier);
        if (SuppressHasteVignetteModel.IsTurnOn(show, next))
            burstElapsed = 0f;
        show = next;
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;

        if (!show && fade <= 0f)
            return;

        // 게임 시간으로 돈다 — 인터럽트 슬로우 동안 느려진다.
        float fadeIn = data != null ? data.VignetteFadeIn : 0.2f;
        float fadeOut = data != null ? data.VignetteFadeOut : 0.3f;
        fade = SuppressHasteVignetteModel.StepAlpha(fade, show, 1f, fadeIn, fadeOut, Time.deltaTime);
        if (burstElapsed < float.MaxValue)
            burstElapsed += Time.deltaTime;
        lineTime = Mathf.Repeat(lineTime + Time.deltaTime, 1000f);
        Apply();
    }

    private void Apply()
    {
        float burst = SuppressHasteVignetteModel.BurstFactor(burstElapsed, burstDuration, burstMultiplier);

        if (image != null)
            ApplyLayer(image, vignetteColor, vignetteColor.a * fade * fillAlpha);

        if (lineImage != null)
        {
            ApplyLayer(lineImage, lineColor, Mathf.Min(1f, fade * lineAlpha * burst));
            if (lineImage.enabled)
                ApplyLineProperties(lineMaterialInstance, SuppressHasteVignetteModel.LineFrame(lineTime, lineFps), burst);
        }
    }

    private void ApplyLayer(Graphic graphic, Color color, float alpha)
    {
        bool visible = alpha > visibleAlphaThreshold;
        if (graphic.enabled != visible)
            graphic.enabled = visible;
        if (visible)
            graphic.color = new Color(color.r, color.g, color.b, alpha);
    }

    // 스피드 라인 셰이더 값 — 런타임 인스턴스·에디터 미리보기 인스턴스 공용.
    private void ApplyLineProperties(Material material, int frame, float burst)
    {
        Vector2 size = rectTransform != null ? rectTransform.rect.size : new Vector2(Screen.width, Screen.height);
        float aspect = size.y > 0f ? size.x / size.y : 16f / 9f;
        float innerMin = Mathf.Min(lineInnerMin, lineInnerMax);
        material.SetVector(LinesId, new Vector4(lineCount, lineThickness, innerMin, Mathf.Max(lineInnerMin, lineInnerMax)));
        // 프레임 번호는 해시 입력 — 1000 주기로 감아 큰 float 정밀도 문제를 피한다.
        material.SetVector(LineFrameId, new Vector4(frame % 1000, Mathf.Clamp01(lineDensity * burst), centerClearRadius, aspect));
    }

#if UNITY_EDITOR
    // ── 에디터 미리보기 ── 저장 안 되는 자식 2장(채움 Image·스피드 라인 RawImage)에만 그린다. 칸을 바꾸면(OnValidate)·크기가 바뀌면 다시 칠한다.
    private const string PreviewFillName = "__Preview_SuppressHasteFill";
    private const string PreviewLineName = "__Preview_SuppressHasteLines";
    private Material previewMaterial;

    private void OnValidate()
    {
        if (!Application.isPlaying)
            VignetteEditorPreview.Defer(this, RefreshPreview);
    }

    private void OnRectTransformDimensionsChange()
    {
        if (!Application.isPlaying && editorPreview && isActiveAndEnabled)
            VignetteEditorPreview.Defer(this, RefreshPreview);
    }

    private void RefreshPreview()
    {
        if (!isActiveAndEnabled || !editorPreview)
        {
            ClearPreview();
            return;
        }

        float burst = SuppressHasteVignetteModel.BurstFactor(editorPreviewBurstTime * burstDuration, burstDuration, burstMultiplier);

        Image fill = VignetteEditorPreview.Ensure<Image>(transform, PreviewFillName);
        fill.sprite = vignetteSprite;
        fill.enabled = vignetteSprite != null;
        fill.color = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, vignetteColor.a * editorPreviewStrength * fillAlpha);

        RawImage lines = VignetteEditorPreview.Ensure<RawImage>(transform, PreviewLineName);
        if (speedLineMaterial == null)
        {
            lines.enabled = false;
            DestroyPreviewMaterial();
            return;
        }

        if (previewMaterial == null || previewMaterial.shader != speedLineMaterial.shader)
        {
            DestroyPreviewMaterial();
            previewMaterial = new Material(speedLineMaterial) { name = "SpeedLines_Preview", hideFlags = HideFlags.DontSave };
        }

        lines.enabled = true;
        lines.material = previewMaterial;
        lines.color = new Color(lineColor.r, lineColor.g, lineColor.b, Mathf.Min(1f, editorPreviewStrength * lineAlpha * burst));
        ApplyLineProperties(previewMaterial, Mathf.Abs(editorPreviewFrame), burst);
    }

    private void DestroyPreviewMaterial()
    {
        if (previewMaterial != null)
            DestroyImmediate(previewMaterial);
        previewMaterial = null;
    }

    private void ClearPreview()
    {
        VignetteEditorPreview.Clear(transform);
        DestroyPreviewMaterial();
    }
#endif
}
