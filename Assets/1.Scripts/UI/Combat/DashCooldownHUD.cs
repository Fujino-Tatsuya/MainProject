using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 대시(Space) 재충전 표시. 로컬 플레이어의 <see cref="PlayerDashController"/>를 매 프레임 폴링한다.
/// 표현은 스킬 슬롯과 동일하다 — 남은 비율만큼 Filled 오버레이가 덮고, 남은 초를 텍스트로 쓴다.
///
/// 오너 예측 장부(<c>predictedLedger</c>)를 읽으므로 로컬 플레이어에만 유효하다.
/// CombatHUD가 <c>Player.LocalPlayer</c>로 바인딩한다.
/// </summary>
public class DashCooldownHUD : MonoBehaviour, ICombatUiBlockedStateView
{
    [Tooltip("남은 재충전 비율만큼 덮는 Filled 타입 오버레이 (스킬 쿨타임 HUD와 동일).")]
    [SerializeField] private Image cooldownFill;

    [Tooltip("덮개 마스크 본체 구간(1x px) — 남은 비율을 이 구간에 맞춘다.")]
    [SerializeField] private CooldownCoverBody cooldownBody = CooldownCoverBody.SideBody;

    [Tooltip("남은 재충전(초) 텍스트. 스킬 쿨타임 HUD와 동일 포맷. 선택.")]
    [SerializeField] private TMP_Text remainingText;

    [Tooltip("최대 충전이 2개 이상일 때 현재/최대 충전 수를 표시한다. 선택.")]
    [SerializeField] private TMP_Text chargeText;

    [SerializeField] private Color blockedColor = new Color(0.35f, 0.35f, 0.35f, 0.75f);

    [Tooltip("대시 아이콘(마스크 밑 Icon_Dash). 비면 슬롯 루트(cooldownFill 의 부모)의 Icon_mask 첫 자식을 쓴다. 쿨다운 아이콘 연출(할 일 259) 대상.")]
    [SerializeField] private Image icon;

    [Tooltip("쿨다운 아이콘 연출(할 일 259) — 충전 0 동안 어둡게, 충전이 하나 차는 순간 펄스·번쩍.")]
    [SerializeField] private CooldownIconFeedbackSettings cooldownFeedback = new CooldownIconFeedbackSettings();

    private PlayerDashController dash;
    private bool isBlocked;

    private CooldownIconFeedback feedback;
    private int lastCharge;
    private int lastMaxCharge;
    private bool hasChargeBaseline;

    // 슬롯 루트 아래 Graphic 원래 색. SetBlocked가 되돌릴 수 있도록 캐시한다.
    private Graphic[] graphics;
    private Color[] normalColors;

    private void Awake()
    {
        CacheSlotColors();
    }

    public void Bind(Player player)
    {
        dash = player != null ? player.GetComponent<PlayerDashController>() : null;
        hasChargeBaseline = false;   // 바인딩이 바뀐 프레임의 충전 변화는 연출하지 않는다
        Refresh();
    }

    private void OnDisable()
    {
        feedback?.Stop();
        hasChargeBaseline = false;
    }

    /// <summary>
    /// 사용 불가 색상만 전환한다. Refresh는 계속 실행되므로 Cooldown Fill은 멈추지 않는다.
    /// (SkillCooldownHUD와 동일 정책 — Soul 상태에서도 진행 표시는 살려 둔다.)
    /// </summary>
    public void SetBlocked(bool blocked)
    {
        if (isBlocked == blocked)
            return;

        CacheSlotColors();
        isBlocked = blocked;

        if (graphics == null)
            return;

        for (int i = 0; i < graphics.Length; i++)
        {
            if (graphics[i] != null)
                graphics[i].color = blocked ? blockedColor : normalColors[i];
        }
    }

    private void Update()
    {
        Refresh();
    }

    private void Refresh()
    {
        float remaining = 0f;
        float total = 0f;
        int charge = 0;
        int maxCharge = 0;

        if (dash != null)
        {
            // ⚠️ PredictedNextReadyTime에서 직접 빼지 않는다 — 그 값은 이 피어 쿨 시계(PlayerCooldownClock.DashNow)
            //    기준 시각이라 다른 시계로 빼면 엉킨다. 남은 초·분모 둘 다 쿨 시계 초라 비율은 배율과 무관하게 맞다.
            remaining = dash.RemainingRecharge;
            total = dash.RechargeDuration;
            charge = dash.PredictedCharge;
            maxCharge = dash.MaxCharge;
        }

        cooldownBody.Apply(cooldownFill, total > 0f ? remaining / total : 0f);
        TickCooldownFeedback(charge, maxCharge);

        if (remainingText != null)
        {
            // 스킬 쿨타임 HUD와 동일 포맷: 1초 미만은 0.1초 단위, 1초 이상은 올림 정수, 0이면 숨김
            remainingText.text = FormatRemaining(remaining);
        }

        if (chargeText != null)
        {
            bool showCharge = ShouldShowCharge(maxCharge);
            if (chargeText.gameObject.activeSelf != showCharge)
                chargeText.gameObject.SetActive(showCharge);

            chargeText.text = showCharge ? FormatCharge(charge, maxCharge) : string.Empty;
        }
    }

    // 충전식 기준(할 일 259): 어둡게 = 충전 0(대시 불가) 동안, 펄스·번쩍 = 충전이 하나 늘어난 순간(0→1·1→2 모두).
    // 충전 1개 이상 남은 재충전 중에는 어둡게 하지 않는다 — 쓸 수 있는 상태라서. 덮개 진행 표시는 그대로.
    private void TickCooldownFeedback(int charge, int maxCharge)
    {
        if (feedback == null)
        {
            Image target = icon != null
                ? icon
                : CooldownIconFeedback.FindIcon(cooldownFill != null ? cooldownFill.transform.parent : null);
            if (target == null)
                return;
            feedback = new CooldownIconFeedback(target);
        }

        bool gained = CooldownIconFeedbackRules.ChargeGained(hasChargeBaseline, lastCharge, lastMaxCharge, charge, maxCharge);
        bool dim = dash != null && maxCharge > 0 && charge <= 0;
        hasChargeBaseline = true;
        lastCharge = charge;
        lastMaxCharge = maxCharge;

        feedback.Tick(dim, gained, cooldownFeedback, Time.unscaledDeltaTime);
    }

    public static string FormatRemaining(float remaining) => remaining <= 0f
        ? string.Empty
        : remaining < 1f ? remaining.ToString("F1") : Mathf.CeilToInt(remaining).ToString();

    public static bool ShouldShowCharge(int maxCharge) => maxCharge > 1;

    public static string FormatCharge(int charge, int maxCharge) =>
        $"{Mathf.Clamp(charge, 0, Mathf.Max(0, maxCharge))}/{Mathf.Max(0, maxCharge)}";

    private void CacheSlotColors()
    {
        if (graphics != null || cooldownFill == null)
            return;

        // 기존 프리팹에서 CooldownFill의 부모가 슬롯 루트다(SkillCooldownHUD와 같은 규약).
        Transform slotRoot = cooldownFill.transform.parent;
        if (slotRoot == null)
            return;

        graphics = slotRoot.GetComponentsInChildren<Graphic>(true);
        normalColors = new Color[graphics.Length];

        for (int i = 0; i < graphics.Length; i++)
            normalColors[i] = graphics[i].color;
    }
}
