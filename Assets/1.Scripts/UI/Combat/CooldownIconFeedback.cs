using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 쿨다운 아이콘 연출 튜닝 값(할 일 259). 스킬·대시·패시브 HUD 가 각자 인스펙터에 하나씩 든다.
/// 프리팹에 저장된 값이 없으면 아래 기본값을 쓴다(프리팹 수정 없이 동작).
/// </summary>
[System.Serializable]
public sealed class CooldownIconFeedbackSettings
{
    [Tooltip("쿨다운 중 아이콘 밝기(1 = 그대로, 0.6 = 60%). 아이콘 위에 검은 덮개를 (1 - 값) 알파로 얹는다 — 아이콘 색은 건드리지 않는다.")]
    [Range(0f, 1f)] public float cooldownBrightness = 0.6f;

    [Tooltip("쿨다운이 끝나는 순간 아이콘(마스크 창) 최대 배율. 원래 스케일 × 이 값까지 커졌다가 돌아온다.")]
    [Min(1f)] public float pulsePeakScale = 1.15f;

    [Tooltip("커졌다 작아지는 데 걸리는 시간(초, 슬로모션 무관 실시간).")]
    [Min(0f)] public float pulseSeconds = 0.25f;

    [Tooltip("끝나는 순간 아이콘 위 번쩍 덮개 최대 알파(0..1). 블룸·포스트 프로세싱 없이 덮개 알파만 쓴다.")]
    [Range(0f, 1f)] public float flashPeakAlpha = 0.85f;

    [Tooltip("번쩍 덮개가 최대 알파에서 0 으로 사라지는 시간(초, 슬로모션 무관 실시간).")]
    [Min(0f)] public float flashSeconds = 0.3f;

    [Tooltip("번쩍 덮개 색(알파는 위 값이 정한다).")]
    public Color flashColor = Color.white;
}

/// <summary>쿨다운 아이콘 연출의 순수 규칙(EditMode 고정 대상).</summary>
public static class CooldownIconFeedbackRules
{
    /// <summary>이번 프레임에 쿨다운이 끝났는가 — 기준 프레임이 있고 직전엔 쿨다운, 지금은 아님.</summary>
    public static bool Ended(bool hasBaseline, bool wasCooling, bool isCooling) =>
        hasBaseline && wasCooling && !isCooling;

    /// <summary>
    /// 대시 충전식 — 충전이 하나 이상 늘어난 프레임(0→1, 1→2 …). 최대 충전이 바뀐 프레임(재바인딩·설정 교체)은 연출하지 않는다.
    /// </summary>
    public static bool ChargeGained(bool hasBaseline, int prevCharge, int prevMaxCharge, int charge, int maxCharge) =>
        hasBaseline && prevMaxCharge == maxCharge && maxCharge > 0 && charge > prevCharge;

    /// <summary>쿨다운 중 검은 덮개 알파 = 1 - 밝기.</summary>
    public static float DimAlpha(float cooldownBrightness) => 1f - Mathf.Clamp01(cooldownBrightness);

    /// <summary>펄스 배율(원래 스케일 대비). 1 → 최대 → 1 을 반주기 사인으로. 범위 밖은 1.</summary>
    public static float PulseScale(float elapsed, float duration, float peakScale)
    {
        if (duration <= 0f || elapsed < 0f || elapsed >= duration)
            return 1f;

        return 1f + (peakScale - 1f) * Mathf.Sin(Mathf.PI * (elapsed / duration));
    }

    /// <summary>번쩍 덮개 알파. 시작 순간 최대, 이후 ease-out(2차)으로 0. 범위 밖은 0.</summary>
    public static float FlashAlpha(float elapsed, float duration, float peakAlpha)
    {
        if (duration <= 0f || elapsed < 0f || elapsed >= duration)
            return 0f;

        float remain = 1f - elapsed / duration;
        return Mathf.Clamp01(peakAlpha) * remain * remain;
    }
}

/// <summary>
/// 슬롯 하나의 쿨다운 아이콘 연출(할 일 259) — ① 쿨다운 중 어둡게 ② 끝나는 순간 펄스 ③ 끝나는 순간 번쩍(②③ 동시 한 번).
/// HUD 가 슬롯마다 하나씩 만들어 매 프레임 <see cref="Tick"/> 한다. MonoBehaviour 아님 — 프리팹에 컴포넌트를 더하지 않는다.
///
/// · 어둡게·번쩍 = 아이콘 마스크(<c>Icon_mask</c>) 아래 런타임 덮개 Image 하나(스프라이트 없음 → 마스크 모양 그대로)의 색·알파.
///   아이콘 Image.color 는 건드리지 않는다(차단 색·후속 tint·패시브 Ready 색과 안 싸운다).
/// · 펄스 = 마스크 창 RectTransform 스케일을 시작 순간 스케일 × 배율로. 끝나면 그 스케일로 되돌린다
///   (다른 곳에서 준 스케일 — 예: 덮개 1.02 — 을 덮어쓰지 않는다. 아이콘 자체(대시 -1 뒤집기)도 안 건드린다).
/// · 시간 = <c>Time.unscaledDeltaTime</c>(HUD 연출은 슬로모션 무관 — 보스 HP 인트로와 같은 규칙). 쿨다운 판정 값은 각 HUD 의 기존 출처 그대로.
/// </summary>
public sealed class CooldownIconFeedback
{
    private const string OverlayName = "CooldownFeedbackOverlay";

    private readonly Image icon;
    private RectTransform pulseTarget;
    private Image overlay;

    private bool hasBaseline;
    private bool wasCooling;
    private float elapsed = -1f;   // 끝 연출 경과(초). 음수 = 재생 안 함
    private Vector3 pulseBaseScale;

    public CooldownIconFeedback(Image icon)
    {
        this.icon = icon;
    }

    public bool IsValid => icon != null;

    /// <summary>바인딩이 바뀌었을 때 — 다음 프레임을 새 기준으로 삼아 가짜 '끝남' 연출을 막는다.</summary>
    public void ResetBaseline()
    {
        hasBaseline = false;
    }

    /// <summary>남은 쿨다운으로 판정하는 일반 슬롯(스킬·패시브). 쿨다운 → 끝 전이에서 연출한다.</summary>
    public void Tick(bool cooling, CooldownIconFeedbackSettings settings, float deltaTime)
    {
        bool ended = CooldownIconFeedbackRules.Ended(hasBaseline, wasCooling, cooling);
        hasBaseline = true;
        wasCooling = cooling;
        Tick(cooling, ended, settings, deltaTime);
    }

    /// <summary>어둡게 여부와 끝 연출 시작을 부르는 쪽이 정하는 슬롯(대시 충전식).</summary>
    public void Tick(bool dim, bool ended, CooldownIconFeedbackSettings settings, float deltaTime)
    {
        if (icon == null || settings == null)
            return;

        if (ended)
            StartEndFeedback();

        float pulse = 1f;
        float flash = 0f;
        if (elapsed >= 0f)
        {
            elapsed += deltaTime;
            pulse = CooldownIconFeedbackRules.PulseScale(elapsed, settings.pulseSeconds, settings.pulsePeakScale);
            flash = CooldownIconFeedbackRules.FlashAlpha(elapsed, settings.flashSeconds, settings.flashPeakAlpha);
            if (elapsed >= Mathf.Max(settings.pulseSeconds, settings.flashSeconds))
                StopPulse();
        }

        if (elapsed >= 0f && pulseTarget != null)
            pulseTarget.localScale = pulseBaseScale * pulse;

        // 쿨다운이 다시 시작되면 어둡게가 번쩍보다 먼저다(둘은 보통 겹치지 않는다).
        if (dim)
            SetOverlay(Color.black, CooldownIconFeedbackRules.DimAlpha(settings.cooldownBrightness));
        else
            SetOverlay(settings.flashColor, flash);
    }

    /// <summary>HUD 가 꺼질 때 — 스케일을 원래대로, 덮개를 숨긴다.</summary>
    public void Stop()
    {
        StopPulse();
        if (overlay != null)
            overlay.enabled = false;
        hasBaseline = false;
    }

    private void StartEndFeedback()
    {
        EnsurePulseTarget();
        if (elapsed < 0f && pulseTarget != null)
            pulseBaseScale = pulseTarget.localScale;   // 재생 중 다시 끝나면 처음 기준 스케일 유지
        elapsed = 0f;
    }

    private void StopPulse()
    {
        if (elapsed >= 0f && pulseTarget != null)
            pulseTarget.localScale = pulseBaseScale;
        elapsed = -1f;
    }

    private void EnsurePulseTarget()
    {
        if (pulseTarget == null && icon != null)
            pulseTarget = icon.rectTransform.parent as RectTransform ?? icon.rectTransform;
    }

    private void SetOverlay(Color color, float alpha)
    {
        if (alpha <= 0f)
        {
            if (overlay != null && overlay.enabled)
                overlay.enabled = false;
            return;
        }

        EnsureOverlay();
        if (overlay == null)
            return;

        color.a = alpha;
        overlay.color = color;
        if (!overlay.enabled)
            overlay.enabled = true;
    }

    // 아이콘과 같은 부모(마스크) 아래 맨 뒤에 늘림 앵커로 만든다 — 마스크가 아이콘 모양으로 잘라 준다.
    private void EnsureOverlay()
    {
        if (overlay != null || icon == null)
            return;

        Transform parent = icon.rectTransform.parent != null ? icon.rectTransform.parent : icon.rectTransform;
        var go = new GameObject(OverlayName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = icon.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.SetAsLastSibling();

        overlay = go.GetComponent<Image>();
        overlay.sprite = null;
        overlay.raycastTarget = false;
        overlay.color = Color.clear;
        overlay.enabled = false;
    }

    /// <summary>슬롯 루트 아래 <c>Icon_mask</c> 의 첫 자식 Image(대시처럼 아이콘 참조가 없는 HUD 용).</summary>
    public static Image FindIcon(Transform slotRoot)
    {
        Transform mask = slotRoot != null ? slotRoot.Find("Icon_mask") : null;
        return mask != null && mask.childCount > 0 ? mask.GetChild(0).GetComponent<Image>() : null;
    }
}
