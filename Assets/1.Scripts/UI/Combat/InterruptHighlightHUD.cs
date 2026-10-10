using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로컬 플레이어 RMB(인터럽트) 슬롯 강조 — 카운터 창이 열린 대상이 내 인터럽트 사거리 안에 있으면
/// 슬롯 테두리에 노란 발광이 맥동한다. 조건이 꺼지면 페이드 아웃해 원래 슬롯으로 돌아간다.
/// <list type="bullet">
/// <item>각 클라가 자기 화면에서만 판단하는 순수 로컬 연출 — 네트워크 동기화 없음. 사거리 때문에 클라마다 다를 수 있다.</item>
/// <item>대상 2종(복제값을 읽기만): 23호 <see cref="TwentyThreeBoss.CounterWindowOpen"/>,
///       허수아비 <see cref="TrainingDummyInterrupt.IsInterruptible"/>. 중간보스는 범위 밖.</item>
/// <item>사거리 = 서버 슬로우 예측(PLAN-interrupt-slowmo D2)과 같은 판정 앵커 오버랩
///       (<see cref="PlayerInterruptSkillBase.HasTargetInHitbox"/>).</item>
/// <item>게임 시간(scaled)으로 돈다 — 인터럽트 슬로우 동안 같이 느려진다(D9).</item>
/// <item>화면 가장자리 비네팅은 같은 조건으로 켤 수 있지만 기본 꺼짐(<see cref="showVignette"/>).</item>
/// </list>
/// 판단·페이드·맥동 계산은 <see cref="InterruptHighlightModel"/>.
/// </summary>
public class InterruptHighlightHUD : MonoBehaviour
{
    [Header("RMB 슬롯 발광")]
    [Tooltip("RMB 슬롯 위에 겹치는 강조 이미지.")]
    [SerializeField] private Image slotGlow;
    [Tooltip("발광 스프라이트(가운데 투명·테두리 발광, 흰색 권장). 비어 있으면 코드로 만든 사각 발광 텍스처를 쓴다.")]
    [SerializeField] private Sprite slotGlowSprite;
    [Tooltip("발광 색. 기본 = 카운터 창(보스 텔레그래프·허수아비 틴트)과 같은 노란색.")]
    [SerializeField] private Color glowColor = new Color(1f, 0.85f, 0.2f);
    [Tooltip("맥동 최저 알파(최고 = 1).")]
    [SerializeField, Range(0f, 1f)] private float pulseMinAlpha = 0.45f;
    [Tooltip("맥동 한 주기(초, 게임 시간).")]
    [SerializeField, Min(0f)] private float pulsePeriod = 0.6f;
    [Tooltip("맥동 최고점의 추가 크기 비율(0.06 = 6% 커짐).")]
    [SerializeField, Range(0f, 0.3f)] private float pulseScale = 0.06f;
    [Tooltip("꺼짐 → 켜짐에 걸리는 시간(초).")]
    [SerializeField, Min(0f)] private float fadeInDuration = 0.08f;
    [Tooltip("켜짐 → 꺼짐에 걸리는 시간(초).")]
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.15f;

    [Header("화면 가장자리 비네팅 (기본 꺼짐)")]
    [Tooltip("켜면 같은 조건에서 화면 가장자리도 발광 색으로 맥동한다.")]
    [SerializeField] private bool showVignette;
    [SerializeField] private Image vignetteImage;
    [Tooltip("비네팅 스프라이트(가장자리 불투명·중앙 투명, 흰색 권장). 비어 있으면 코드로 만든 방사형 임시 텍스처를 쓴다.")]
    [SerializeField] private Sprite vignetteSprite;
    [Tooltip("비네팅 최고 알파.")]
    [SerializeField, Range(0f, 1f)] private float vignetteMaxAlpha = 0.3f;

    [Header("임시 텍스처(스프라이트가 비었을 때)")]
    [SerializeField, Min(2)] private int fallbackTextureSize = 128;
    [Tooltip("슬롯 발광: 이 거리까지 투명(사각 기준, 1 = 이미지 변).")]
    [SerializeField, Range(0f, 1f)] private float glowInnerEdge = 0.62f;
    [Tooltip("슬롯 발광: 가장 밝은 거리. 여기서 이미지 변까지 다시 투명해진다.")]
    [SerializeField, Range(0f, 1f)] private float glowPeakEdge = 0.8f;
    [Tooltip("비네팅: 이 거리까지 완전 투명(0 = 중앙, 1 = 변의 중점, √2 = 모서리).")]
    [SerializeField, Min(0f)] private float vignetteInnerRadius = 0.9f;
    [Tooltip("비네팅: 이 거리부터 완전 불투명.")]
    [SerializeField, Min(0f)] private float vignetteOuterRadius = 1.2f;

    [Header("표시")]
    [Tooltip("알파가 이 값 이하이면 이미지를 꺼서 투명 드로우를 생략한다.")]
    [SerializeField, Range(0f, 0.1f)] private float visibleAlphaThreshold = 0.001f;

    // 판정 앵커 오버랩에 잡힌 Unit 은 이미 사거리 안이다 — 창 열림만 더 보면 된다.
    private static readonly System.Predicate<Unit> IsHighlightTargetInRange =
        unit => InterruptHighlightModel.IsHighlightTarget(IsCounterWindowOpen(unit), inInterruptRange: true);

    private Player player;
    private PlayerLifeCycleController lifeCycle;
    private PlayerSkillController skills;

    private float intensity;
    private float pulsePhase;

    private Vector3 glowBaseScale = Vector3.one;
    private Sprite generatedGlow;
    private Sprite generatedVignette;

    public void Bind(Player boundPlayer)
    {
        player = boundPlayer;
        lifeCycle = player != null ? player.GetComponent<PlayerLifeCycleController>() : null;
        skills = player != null ? player.GetComponent<PlayerSkillController>() : null;

        // 다른 플레이어로 바뀌면 페이드를 이어 받지 않고 꺼진 상태에서 다시 판단한다.
        intensity = 0f;
        pulsePhase = 0f;
        Apply(0f);
    }

    private void Awake()
    {
        if (slotGlow == null)
            slotGlow = GetComponent<Image>();

        if (slotGlow != null)
        {
            slotGlow.raycastTarget = false;
            glowBaseScale = slotGlow.rectTransform.localScale;
            if (slotGlowSprite == null)
                generatedGlow = VignetteSpriteFactory.CreateSquareGlow(
                    "InterruptGlow_Generated", fallbackTextureSize, glowInnerEdge, glowPeakEdge);
            slotGlow.sprite = slotGlowSprite != null ? slotGlowSprite : generatedGlow;
            slotGlow.enabled = false;
        }

        if (vignetteImage != null)
        {
            vignetteImage.raycastTarget = false;
            if (vignetteSprite == null)
                generatedVignette = VignetteSpriteFactory.CreateRadial(
                    "InterruptVignette_Generated", fallbackTextureSize, vignetteInnerRadius, vignetteOuterRadius);
            vignetteImage.sprite = vignetteSprite != null ? vignetteSprite : generatedVignette;
            vignetteImage.enabled = false;
        }
    }

    private void OnDestroy()
    {
        VignetteSpriteFactory.Destroy(generatedGlow);
        VignetteSpriteFactory.Destroy(generatedVignette);
    }

    private void Update()
    {
        // 전투 HUD = 게임 시간(PLAN-interrupt-slowmo D9) — 슬로우 중엔 맥동·페이드도 같이 느려진다.
        Apply(Time.deltaTime);
    }

    private void Apply(float deltaTime)
    {
        bool show = player != null &&
                    InterruptHighlightModel.ShouldHighlight(AnyHighlightTargetInRange(), IsAlive());

        intensity = InterruptHighlightModel.StepIntensity(intensity, show, fadeInDuration, fadeOutDuration, deltaTime);
        // 꺼져 있다 켜질 때는 가장 밝은 위상부터 시작한다.
        pulsePhase = intensity > 0f ? InterruptHighlightModel.AdvancePhase(pulsePhase, pulsePeriod, deltaTime) : 0f;
        float wave = InterruptHighlightModel.PulseWave(pulsePhase);

        ApplySlotGlow(wave);
        ApplyVignette(wave);
    }

    private void ApplySlotGlow(float wave)
    {
        if (slotGlow == null)
            return;

        float alpha = InterruptHighlightModel.GlowAlpha(intensity, wave, pulseMinAlpha);
        bool visible = alpha > visibleAlphaThreshold;
        if (slotGlow.enabled != visible)
            slotGlow.enabled = visible;
        if (!visible)
            return;

        slotGlow.color = new Color(glowColor.r, glowColor.g, glowColor.b, alpha);
        slotGlow.rectTransform.localScale = glowBaseScale * (1f + pulseScale * wave * intensity);
    }

    private void ApplyVignette(float wave)
    {
        if (vignetteImage == null)
            return;

        float alpha = showVignette
            ? InterruptHighlightModel.GlowAlpha(intensity, wave, pulseMinAlpha) * vignetteMaxAlpha
            : 0f;
        bool visible = alpha > visibleAlphaThreshold;
        if (vignetteImage.enabled != visible)
            vignetteImage.enabled = visible;
        if (visible)
            vignetteImage.color = new Color(glowColor.r, glowColor.g, glowColor.b, alpha);
    }

    private bool AnyHighlightTargetInRange()
    {
        PlayerInterruptSkillBase interrupt =
            skills != null ? skills.GetSkill(PlayerSkillSlot.Interrupt) as PlayerInterruptSkillBase : null;
        return interrupt != null && interrupt.HasTargetInHitbox(IsHighlightTargetInRange);
    }

    // 강조 대상 2종의 복제값만 읽는다(보스·허수아비 코드는 수정하지 않는다).
    private static bool IsCounterWindowOpen(Unit unit)
    {
        if (unit is TwentyThreeBoss boss)
            return boss.CounterWindowOpen;

        return unit.TryGetComponent(out TrainingDummyInterrupt dummy) && dummy.IsInterruptible;
    }

    private bool IsAlive()
    {
        return lifeCycle == null || lifeCycle.State == PlayerLifeState.Alive;
    }
}
