using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로컬 플레이어 화면 가장자리 체력 비네팅. 화면 전체를 덮는 UI Image 의 색·알파만 바꾼다(순수 클라 연출, 동기화 없음).
/// - 체력 비율이 시작 비율 이하로 내려가면 빨간 비네팅이 켜지고, 낮을수록 진해지며 빠르고 크게 맥동한다.
/// - 피격(ClientDamagedAmount, amount>0) 순간 짧게 번쩍인다 — 시작 비율 이상이면 흰색, 아래일수록 붉게.
/// - 사망·Soul·관전(Alive 가 아닌 모든 상태)에선 색에 검은색을 곱해 서서히 검게 바꾼다.
/// 계산은 <see cref="HealthVignetteModel"/>. 카메라의 URP HP 비네트(<see cref="CameraFeedback"/>)와는 별개다.
/// </summary>
public class HealthVignetteHUD : MonoBehaviour
{
    [SerializeField] private Image image;
    [Tooltip("가장자리 비네팅 스프라이트(가장자리 불투명·중앙 투명, 흰색 권장). 비어 있으면 코드로 만든 방사형 임시 텍스처를 쓴다.")]
    [SerializeField] private Sprite vignetteSprite;

    [Header("체력 비네팅")]
    [Tooltip("이 체력 비율 이하부터 비네팅이 켜진다. 0 에서 최대.")]
    [SerializeField, Range(0f, 1f)] private float startRatio = 0.8f;
    [SerializeField, Range(0f, 1f)] private float maxAlpha = 0.6f;
    [SerializeField] private Color vignetteColor = Color.red;

    [Header("맥동")]
    [Tooltip("시작 비율 바로 아래에서의 맥동 속도(초당 주기 수).")]
    [SerializeField, Min(0f)] private float minPulseSpeed = 0.8f;
    [Tooltip("체력 0 에서의 맥동 속도(초당 주기 수).")]
    [SerializeField, Min(0f)] private float maxPulseSpeed = 2.5f;
    [Tooltip("체력 0 에서의 맥동 진폭(알파). 체력이 높을수록 이 값에 비례해 작아진다.")]
    [SerializeField, Range(0f, 1f)] private float pulseAmplitude = 0.2f;

    [Header("피격 플래시")]
    [SerializeField, Min(0f)] private float flashDuration = 0.2f;
    [SerializeField, Range(0f, 1f)] private float flashAlpha = 0.7f;
    [Tooltip("시작 비율 이상에서 맞을 때 플래시 색.")]
    [SerializeField] private Color flashSafeColor = Color.white;
    [Tooltip("체력 0 에 가까울 때 플래시 색. 시작 비율 아래에서 안전색 → 이 색으로 보간.")]
    [SerializeField] private Color flashDangerColor = Color.red;

    [Header("사망·Soul·관전")]
    [Tooltip("검게 바뀌거나 돌아오는 데 걸리는 시간(초).")]
    [SerializeField, Min(0f)] private float darkenDuration = 0.5f;

    private Player player;
    private PlayerLifeCycleController lifeCycle;

    private float pulsePhase;
    private float flashElapsed = float.MaxValue;
    private float darken;

    private Texture2D generatedTexture;
    private Sprite generatedSprite;

    public void Bind(Player boundPlayer)
    {
        if (player != null)
            player.ClientDamagedAmount -= HandleDamaged;

        player = boundPlayer;
        lifeCycle = player != null ? player.GetComponent<PlayerLifeCycleController>() : null;

        if (player != null && isActiveAndEnabled)
            player.ClientDamagedAmount += HandleDamaged;

        // 다른 플레이어로 바뀌면 이전 연출을 이어 받지 않는다.
        pulsePhase = 0f;
        flashElapsed = float.MaxValue;
        darken = IsAlive() ? 0f : 1f;
        Apply(0f);
    }

    private void Awake()
    {
        if (image == null)
            image = GetComponent<Image>();

        if (image == null)
            return;

        image.raycastTarget = false;
        image.sprite = vignetteSprite != null ? vignetteSprite : CreateFallbackSprite();
    }

    private void OnEnable()
    {
        if (player == null)
            return;

        // 이미 구독된 상태에서 재진입해도 중복되지 않게 한 번 떼고 붙인다.
        player.ClientDamagedAmount -= HandleDamaged;
        player.ClientDamagedAmount += HandleDamaged;
    }

    private void OnDisable()
    {
        if (player != null)
            player.ClientDamagedAmount -= HandleDamaged;
    }

    private void OnDestroy()
    {
        if (generatedSprite != null)
            Destroy(generatedSprite);
        if (generatedTexture != null)
            Destroy(generatedTexture);
    }

    private void HandleDamaged(int amount, DamageChannel channel)
    {
        if (amount > 0)
            flashElapsed = 0f;
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

        bool alive = IsAlive();
        float darkenStep = darkenDuration > 0f ? deltaTime / darkenDuration : 1f;
        darken = Mathf.MoveTowards(darken, alive ? 0f : 1f, darkenStep);

        // 살아 있지 않으면 HUD 처럼 체력 0 으로 표현한다(Soul 의 실제 HP 복제값은 쓰지 않는다).
        float ratio = alive ? HealthVignetteModel.HealthRatio(player.CurrentHealth, player.FinalMaxHp) : 0f;
        float severity = HealthVignetteModel.Severity(ratio, startRatio);

        pulsePhase = Mathf.Repeat(
            pulsePhase + HealthVignetteModel.PulseSpeed(severity, minPulseSpeed, maxPulseSpeed) * deltaTime, 1f);
        float vignetteAlpha = HealthVignetteModel.VignetteAlpha(severity, maxAlpha, pulseAmplitude, pulsePhase);

        if (flashElapsed < float.MaxValue)
            flashElapsed += deltaTime;
        float currentFlashAlpha = HealthVignetteModel.FlashAlpha(flashElapsed, flashDuration, flashAlpha);
        Color currentFlashColor = HealthVignetteModel.FlashColor(ratio, startRatio, flashSafeColor, flashDangerColor);

        Color color = HealthVignetteModel.Compose(vignetteColor, vignetteAlpha, currentFlashColor, currentFlashAlpha, darken);

        // 알파 0 이면 전체 화면 투명 드로우를 아예 끈다.
        bool visible = color.a > 0.001f;
        if (image.enabled != visible)
            image.enabled = visible;
        if (visible)
            image.color = color;
    }

    private bool IsAlive()
    {
        return lifeCycle == null || lifeCycle.State == PlayerLifeState.Alive;
    }

    /// <summary>아트 스프라이트가 들어오기 전 임시 텍스처 — 중앙 투명, 가장자리로 갈수록 불투명한 흰색.</summary>
    private Sprite CreateFallbackSprite()
    {
        const int size = 128;
        generatedTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "HealthVignette_Generated",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };

        var pixels = new Color32[size * size];
        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 0 = 중앙, 1 = 변의 중점, √2 = 모서리.
                float dx = (x - half) / half;
                float dy = (y - half) / half;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1.2f, distance));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        generatedTexture.SetPixels32(pixels);
        generatedTexture.Apply(false, true);

        generatedSprite = Sprite.Create(generatedTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        generatedSprite.name = generatedTexture.name;
        generatedSprite.hideFlags = HideFlags.DontSave;
        return generatedSprite;
    }
}
