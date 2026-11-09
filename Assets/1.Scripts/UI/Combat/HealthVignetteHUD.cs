using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// 로컬 플레이어 화면 가장자리 체력 비네팅. 화면 전체를 덮는 UI Image 의 색·알파만 바꾼다(순수 클라 연출, 동기화 없음).
/// 모양은 셰이더 UI/HealthVignette(재질 MA_HealthVignette)가 절차적으로 그린다 — 스프라이트·코드 생성 텍스처 없음.
/// - 체력 비율이 시작 비율 이하로 내려가면 빨간 비네팅이 켜지고, 낮을수록 진해지며 빠르고 크게 맥동한다.
/// - 피격(ClientDamagedAmount, amount>0) 순간 짧게 번쩍인다 — 시작 비율 이상이면 흰색, 아래일수록 붉게.
/// - 사망·Soul·관전(Alive 가 아닌 모든 상태)에선 색에 검은색을 곱해 서서히 검게 바꾸고, 맥동·피격 플래시는 끈다.
/// 계산은 <see cref="HealthVignetteModel"/>. 카메라의 URP HP 비네트(<see cref="CameraFeedback"/>)와는 별개다.
/// </summary>
public class HealthVignetteHUD : MonoBehaviour
{
    [SerializeField] private Image image;
    [Tooltip("비네팅 모양 재질 — MA_HealthVignette(셰이더 UI/HealthVignette). 런타임엔 인스턴스를 만들어 아래 모양 값을 매 프레임 넣는다. 비면 비네팅을 그리지 않는다.")]
    [SerializeField] private Material vignetteMaterial;

    [Header("모양(셰이더)")]
    [Tooltip("이 거리까지 완전 투명(0 = 중앙, 1 = 변의 중점, 타원이면 √2 = 모서리). 설정창 '비네트 크기'가 곱해진다.")]
    [FormerlySerializedAs("fallbackInnerRadius")]
    [SerializeField, Min(0f)] private float innerRadius = 0.9f;
    [Tooltip("이 거리부터 완전 불투명(안쪽 반경과의 차 = 부드러움 폭, 설정창 '비네트 경계'가 곱해진다).")]
    [FormerlySerializedAs("fallbackOuterRadius")]
    [SerializeField, Min(0f)] private float outerRadius = 1.2f;
    [Tooltip("모서리 사각 정도 — 0 = 타원(옛 방사형 텍스처와 같음), 1 = 둥근 사각(변을 따라 고르게 깔림).")]
    [SerializeField, Range(0f, 1f)] private float cornerSquareness;
    [Tooltip("화면비 보정 — 0 = 가로·세로를 각각 화면 끝 기준으로(옛 방식, 와이드 화면에선 좌우 띠가 더 넓다), 1 = 좌우 띠 폭을 위아래와 같은 픽셀 폭으로.")]
    [SerializeField, Range(0f, 1f)] private float aspectCorrection;
    [Tooltip("모양 세기(가장자리 최대 불투명도 배율). 색·알파(체력·맥동·플래시)는 아래 칸이 정하고 여기에 곱해진다.")]
    [SerializeField, Range(0f, 1f)] private float edgeStrength = 1f;

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
    [Tooltip("피격 플래시 알파 모양. 가로 = 피격 후 경과(0 = 맞은 순간, 1 = 플래시 시간 끝), 세로 = 플래시 알파 대비 배율(1 = 플래시 알파 그대로, 0 = 안 보임). " +
             "기본 = (0,1)→(1,0) 직선(선형으로 빠짐). 1 이 넘어도 최종 알파는 1 에서 잘린다. 키를 다 지우면 직선으로 돈다.")]
    [SerializeField] private AnimationCurve flashCurve = HealthVignetteModel.DefaultFlashCurve();
    [Tooltip("시작 비율 이상에서 맞을 때 플래시 색.")]
    [SerializeField] private Color flashSafeColor = Color.white;
    [Tooltip("체력 0 에 가까울 때 플래시 색. 시작 비율 아래에서 안전색 → 이 색으로 보간.")]
    [SerializeField] private Color flashDangerColor = Color.red;

    [Header("사망·Soul·관전")]
    [Tooltip("검게 바뀌거나 돌아오는 데 걸리는 시간(초). 이 상태에선 맥동·피격 플래시가 꺼진다.")]
    [SerializeField, Min(0f)] private float darkenDuration = 0.5f;

    [Header("표시")]
    [Tooltip("최종 알파가 이 값 이하이면 이미지를 꺼서 전체 화면 투명 드로우를 생략한다.")]
    [SerializeField, Range(0f, 0.1f)] private float visibleAlphaThreshold = 0.001f;

    private Player player;
    private PlayerLifeCycleController lifeCycle;

    private float pulsePhase;
    private float flashElapsed = float.MaxValue;
    private float darken;

    private Material materialInstance;
    private RectTransform rectTransform;

    private static readonly int VignetteShapeId = Shader.PropertyToID("_VignetteShape");
    private static readonly int VignetteAspectId = Shader.PropertyToID("_VignetteAspect");

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

        rectTransform = image.rectTransform;
        image.raycastTarget = false;
        image.sprite = null;   // 스프라이트 없음 → uv 0..1 전체 화면, 모양은 셰이더가 그린다
        if (vignetteMaterial != null)
        {
            // 모양 값을 매 프레임 바꾸므로 공용 재질 에셋 대신 인스턴스에 쓴다.
            materialInstance = new Material(vignetteMaterial) { name = vignetteMaterial.name + " (Instance)", hideFlags = HideFlags.DontSave };
            image.material = materialInstance;
        }
        else
        {
            Debug.LogWarning($"[HealthVignetteHUD] {name}: vignetteMaterial 이 비어 체력 비네팅을 그리지 않는다.", this);
        }
    }

    // 설정창 그래픽 탭 "비네트 크기/경계"(팀장 10-13 — 안개 시야 디밍과 같이 조절).
    // 크기 = 어두운 테두리 크기(클수록 테두리가 안쪽까지 — 투명 반경 × 1/크기, 안개 시야와 같은 방향), 경계 = 투명→불투명 폭 배율.
    // ⚠️ 10-13 정정: 처음엔 투명 반경에 크기를 그대로 곱해 "줄이면 테두리가 커지는" 반대 방향이었다.
    // 셰이더라 설정값을 매 프레임 그대로 넣는다(옛 방식은 값이 바뀐 프레임에 텍스처를 다시 만들었다).
    private void ApplyShape()
    {
        if (materialInstance == null)
            return;

        VignetteSpriteFactory.ApplySettings(innerRadius, outerRadius, out float inner, out float outer);
        materialInstance.SetVector(VignetteShapeId, new Vector4(inner, outer, cornerSquareness, edgeStrength));

        Rect rect = rectTransform != null ? rectTransform.rect : default;
        float aspect = rect.height > 0f ? rect.width / rect.height : 16f / 9f;
        materialInstance.SetVector(VignetteAspectId, new Vector4(aspect, aspectCorrection, 0f, 0f));
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
        if (materialInstance != null)
            Destroy(materialInstance);
    }

    private void HandleDamaged(int amount, DamageChannel channel)
    {
        // 사망·Soul·관전 중 피격은 무시 — 부활 직후 남은 플래시가 터지지 않게 시작 자체를 막는다.
        if (amount > 0 && IsAlive())
            flashElapsed = 0f;
    }

    private void Update()
    {
        // 게임 시간으로 돈다 — 인터럽트 슬로우(전역 timeScale) 동안 비네팅 연출도 같이 느려진다(PLAN-interrupt-slowmo D9).
        Apply(Time.deltaTime);
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
        float vignetteAlpha = HealthVignetteModel.VignetteAlpha(
            severity, maxAlpha, HealthVignetteModel.ActivePulseAmplitude(alive, pulseAmplitude), pulsePhase);

        if (flashElapsed < float.MaxValue)
            flashElapsed += deltaTime;
        float currentFlashAlpha = HealthVignetteModel.ActiveFlashAlpha(
            alive, HealthVignetteModel.FlashAlpha(flashElapsed, flashDuration, flashAlpha, flashCurve));
        Color currentFlashColor = HealthVignetteModel.FlashColor(ratio, startRatio, flashSafeColor, flashDangerColor);

        Color color = HealthVignetteModel.Compose(vignetteColor, vignetteAlpha, currentFlashColor, currentFlashAlpha, darken);

        // 알파 0 이면 전체 화면 투명 드로우를 아예 끈다.
        bool visible = color.a > visibleAlphaThreshold;
        if (image.enabled != visible)
            image.enabled = visible;
        if (visible)
        {
            image.color = color;
            ApplyShape();   // 보일 때만 — 설정창 크기/경계·인스펙터 모양 값이 바로 반영된다
        }
    }

    private bool IsAlive()
    {
        return lifeCycle == null || lifeCycle.State == PlayerLifeState.Alive;
    }
}
