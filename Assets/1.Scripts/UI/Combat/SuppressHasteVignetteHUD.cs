using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 제압 가속 비네팅(할 일 91 T7, PLAN-91 §3.5) — 로컬 플레이어에게 제압 가속 대표 상태이상(<see cref="StatusEffectType.CooldownRateModifier"/>)이
/// 있는 동안 화면 가장자리 이미지를 페이드 인, 없어지면 페이드 아웃(순수 클라 연출).
/// - 표시 여부는 <see cref="StatusEffectController.EffectsChanged"/> 때만 다시 읽는다(매 프레임 탐색 없음). Update 는 페이드 중일 때만 알파를 옮긴다.
/// - 로컬 플레이어는 <see cref="Player.LocalPlayerChanged"/> 로 직접 받는다 — CombatHUD 코드는 건드리지 않고 프리팹 인스턴스만 둔다.
/// - 이미지 교체 = <see cref="vignetteSprite"/> 만 바꾼다(지금은 흰색 임시 텍스처 에셋). 페이드 시간은 xlsx <c>SuppressHaste</c> 시트(<see cref="SuppressHasteData"/>).
/// 페이드 계산은 실드 비네팅과 같은 <see cref="ShieldVignetteModel.StepAlpha"/>.
/// </summary>
public class SuppressHasteVignetteHUD : MonoBehaviour
{
    [SerializeField] private Image image;
    [Tooltip("비네팅 스프라이트(가장자리 불투명·중앙 투명). 이미지로 바꿀 땐 이것만 교체한다.")]
    [SerializeField] private Sprite vignetteSprite;

    [Header("비네팅")]
    [Tooltip("스프라이트에 곱하는 색. 흰색이면 스프라이트 색 그대로.")]
    [SerializeField] private Color vignetteColor = Color.white;
    [Tooltip("버프가 있을 때의 최대 알파.")]
    [SerializeField, Range(0f, 1f)] private float maxAlpha = 0.6f;

    [Header("페이드(시트 값)")]
    [Tooltip("페이드 인/아웃 시간을 읽는 시트 에셋. 비우면 Resources/" + SuppressHasteData.ResourcesPath + ".")]
    [SerializeField] private SuppressHasteData data;

    [Header("표시")]
    [Tooltip("알파가 이 값 이하이면 이미지를 꺼서 전체 화면 투명 드로우를 생략한다.")]
    [SerializeField, Range(0f, 0.1f)] private float visibleAlphaThreshold = 0.001f;

    private StatusEffectController effects;
    private bool show;
    private float alpha;

    private void Awake()
    {
        if (image == null)
            image = GetComponent<Image>();

        if (data == null)
            data = Resources.Load<SuppressHasteData>(SuppressHasteData.ResourcesPath);

        if (image == null)
            return;

        image.raycastTarget = false;
        if (vignetteSprite != null)
            image.sprite = vignetteSprite;
    }

    private void OnEnable()
    {
        Player.LocalPlayerChanged += Bind;
        Bind(Player.LocalPlayer);
    }

    private void OnDisable()
    {
        Player.LocalPlayerChanged -= Bind;
        Bind(null);
    }

    private void Bind(Player player)
    {
        if (effects != null)
            effects.EffectsChanged -= RefreshShow;

        effects = player != null ? player.GetComponent<StatusEffectController>() : null;

        if (effects != null)
            effects.EffectsChanged += RefreshShow;

        // 다른 플레이어로 바뀌면 페이드를 이어 받지 않고 현재 상태로 바로 맞춘다(첫 동기화엔 이벤트가 안 나므로 여기서 한 번 읽는다).
        RefreshShow();
        alpha = show ? maxAlpha : 0f;
        Apply();
    }

    private void RefreshShow()
    {
        show = effects != null && effects.Has(StatusEffectType.CooldownRateModifier);
    }

    private void Update()
    {
        float target = show ? maxAlpha : 0f;
        if (Mathf.Approximately(alpha, target))
            return;

        // 게임 시간으로 돈다 — 실드 비네팅과 같이 인터럽트 슬로우 동안 느려진다.
        float fadeIn = data != null ? data.VignetteFadeIn : 0.2f;
        float fadeOut = data != null ? data.VignetteFadeOut : 0.3f;
        alpha = ShieldVignetteModel.StepAlpha(alpha, show, maxAlpha, fadeIn, fadeOut, Time.deltaTime);
        Apply();
    }

    private void Apply()
    {
        if (image == null)
            return;

        bool visible = alpha > visibleAlphaThreshold;
        if (image.enabled != visible)
            image.enabled = visible;
        if (visible)
            image.color = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, vignetteColor.a * alpha);
    }
}
