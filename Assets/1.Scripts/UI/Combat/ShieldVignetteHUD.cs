using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로컬 플레이어 화면 가장자리 실드 비네팅. 체력 비네팅(<see cref="HealthVignetteHUD"/>)과 이미지·색을 공유하지 않는 별개 레이어다(순수 클라 연출).
/// - 실드가 0 보다 크면 고정 알파까지 페이드 인, 0 이 되면 페이드 아웃.
/// - 사망·Soul·관전(Alive 가 아닌 모든 상태)에선 숨긴다.
/// 표시 여부 판단과 적용은 <see cref="Apply"/> 한 곳에 있다. 계산은 <see cref="ShieldVignetteModel"/>.
/// </summary>
public class ShieldVignetteHUD : MonoBehaviour
{
    [SerializeField] private Image image;
    [Tooltip("실드 비네팅 스프라이트(가장자리 불투명·중앙 투명, 흰색 권장). 비어 있으면 코드로 만든 방사형 임시 텍스처를 쓴다.")]
    [SerializeField] private Sprite vignetteSprite;

    [Header("실드 비네팅")]
    [SerializeField] private Color vignetteColor = new Color(0.4f, 0.8f, 1f);
    [Tooltip("실드가 있을 때의 고정 알파.")]
    [SerializeField, Range(0f, 1f)] private float shownAlpha = 0.35f;
    [Tooltip("0 → 고정 알파까지 걸리는 시간(초).")]
    [SerializeField, Min(0f)] private float fadeInDuration = 0.25f;
    [Tooltip("고정 알파 → 0 까지 걸리는 시간(초).")]
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.35f;

    [Header("임시 텍스처(스프라이트가 비었을 때)")]
    [Tooltip("코드로 만드는 방사형 텍스처 한 변 픽셀 수.")]
    [SerializeField, Min(2)] private int fallbackTextureSize = 128;
    [Tooltip("이 거리까지 완전 투명(0 = 중앙, 1 = 변의 중점, √2 = 모서리).")]
    [SerializeField, Min(0f)] private float fallbackInnerRadius = 0.9f;
    [Tooltip("이 거리부터 완전 불투명.")]
    [SerializeField, Min(0f)] private float fallbackOuterRadius = 1.2f;

    [Header("표시")]
    [Tooltip("알파가 이 값 이하이면 이미지를 꺼서 전체 화면 투명 드로우를 생략한다.")]
    [SerializeField, Range(0f, 0.1f)] private float visibleAlphaThreshold = 0.001f;

    private Player player;
    private PlayerLifeCycleController lifeCycle;

    private float alpha;

    private Sprite generatedSprite;

    public void Bind(Player boundPlayer)
    {
        player = boundPlayer;
        lifeCycle = player != null ? player.GetComponent<PlayerLifeCycleController>() : null;

        // 다른 플레이어로 바뀌면 페이드를 이어 받지 않고 현재 상태로 바로 맞춘다.
        alpha = player != null && ShieldVignetteModel.ShouldShow(player.CurrentShield, IsAlive()) ? shownAlpha : 0f;
        Apply(0f);
    }

    private void Awake()
    {
        if (image == null)
            image = GetComponent<Image>();

        if (image == null)
            return;

        image.raycastTarget = false;
        if (vignetteSprite == null)
            generatedSprite = VignetteSpriteFactory.CreateRadial(
                "ShieldVignette_Generated", fallbackTextureSize, fallbackInnerRadius, fallbackOuterRadius);
        image.sprite = vignetteSprite != null ? vignetteSprite : generatedSprite;
    }

    private void OnDestroy()
    {
        VignetteSpriteFactory.Destroy(generatedSprite);
    }

    private void Update()
    {
        // 히트스톱 등 timeScale 변화와 무관하게 UI 연출 속도를 유지한다.
        Apply(Time.unscaledDeltaTime);
    }

    private void Apply(float deltaTime)
    {
        if (image == null)
            return;

        if (player == null)
        {
            image.enabled = false;
            return;
        }

        bool show = ShieldVignetteModel.ShouldShow(player.CurrentShield, IsAlive());
        alpha = ShieldVignetteModel.StepAlpha(alpha, show, shownAlpha, fadeInDuration, fadeOutDuration, deltaTime);

        bool visible = alpha > visibleAlphaThreshold;
        if (image.enabled != visible)
            image.enabled = visible;
        if (visible)
            image.color = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, alpha);
    }

    private bool IsAlive()
    {
        return lifeCycle == null || lifeCycle.State == PlayerLifeState.Alive;
    }
}
