using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 로비 결과 패널(할 일 48) — 판이 끝나고 로비로 돌아왔을 때 직전 판 <see cref="SessionResult"/> 를 보여 준다.
/// 표시는 <see cref="ResultStatsView"/>(판 결과·플레이어 행)를 재사용하고, 소요 시간·열고 닫기·모달만 여기서 한다.
/// 열고 닫기 판단은 <see cref="LobbyResultPanelRules"/>, 행 순위·왕관은 <see cref="LobbyResultRanking"/>(계수 = 인스펙터).
/// 사용법 = Docs/tech/lobby-result-panel.md.
///
/// - 열려 있는 동안 <c>content</c> 의 <see cref="UiModalBlocker"/> 가 로컬 플레이어 입력을 막는다.
/// - 닫기 버튼·ESC 로 각자(로컬) 닫고, 열린 뒤 <c>autoCloseSeconds</c>(unscaled) 가 지나면 저절로 닫힌다.
/// - 🔴 결과를 비우지 않는다(<see cref="SessionResult.Clear"/> 는 호출 측 책임). 결과가 남아 있으면 Hide 뒤 다시 Show 할 수 있다.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyResultPanel : MonoBehaviour
{
    [Tooltip("켜고 끄는 본체(Dim·Panel 의 부모). UiModalBlocker 가 붙어 있다. 프리팹 루트(Canvas)는 늘 켜 둔다.")]
    [SerializeField] private GameObject content;
    [SerializeField] private ResultStatsView statsView;
    [Tooltip("소요 시간 값(mm:ss)만 들어간다. '소요시간 :' 라벨은 따로 둔 정적 텍스트.")]
    [SerializeField] private TMP_Text playTimeText;
    [SerializeField] private TMP_Text outcomeText;
    [SerializeField] private Button closeButton;

    [Header("Behaviour")]
    [Tooltip("열린 뒤 자동으로 닫히는 시간(초, unscaled). 0 이하면 자동으로 닫지 않는다.")]
    [SerializeField] private float autoCloseSeconds = LobbyResultPanelRules.DefaultAutoCloseSeconds;
    [SerializeField] private bool closeWithEscape = true;

    [Header("Ranking (score = 가한 피해 × 사망 배수 × min(간파 밑 ^ 간파 성공, 간파 상한))")]
    [Tooltip("사망 0회일 때 배수.")]
    [SerializeField] private float noDeathMultiplier = LobbyResultScoreWeights.DefaultNoDeathMultiplier;
    [Tooltip("1회 이상 사망했을 때 배수.")]
    [SerializeField] private float deathMultiplier = LobbyResultScoreWeights.DefaultDeathMultiplier;
    [Tooltip("간파 배수의 밑 — 간파 성공 1회마다 이만큼 곱한다.")]
    [SerializeField] private float counterBase = LobbyResultScoreWeights.DefaultCounterBase;
    [Tooltip("간파 배수 상한.")]
    [SerializeField] private float counterMultiplierCap = LobbyResultScoreWeights.DefaultCounterMultiplierCap;

    [Header("Outcome Colors")]
    [SerializeField] private Color clearedColor = new Color(1f, 0.8f, 0.25f, 1f);
    [SerializeField] private Color failedColor = new Color(1f, 0.42f, 0.38f, 1f);
    [SerializeField] private Color abortedColor = new Color(0.8f, 0.8f, 0.8f, 1f);

    private static int s_openCount;
    private static int s_escapeClaimedFrame = -1;

    private readonly List<LobbyResultRankEntry> _ranking = new List<LobbyResultRankEntry>();
    private LobbyResultPanelRules _rules;
    private bool _countedOpen;

    /// <summary>닫힐 때(버튼·ESC·자동·Hide) 한 번.</summary>
    public event Action<LobbyResultCloseReason> Closed;

    /// <summary>보여 줄 직전 판 결과가 있는가(= <see cref="SessionResult.HasValue"/>).</summary>
    public bool HasResult => SessionResult.HasValue;

    public bool IsOpen => Rules.IsOpen;

    /// <summary>
    /// 이 프레임 ESC 를 결과 패널이 쓰는가 — 열려 있거나, 이 프레임 ESC 로 닫혔다.
    /// 로비에서 ESC 를 쓰는 다른 메뉴는 이 값이 true 면 무시해야 같은 키로 두 개가 같이 움직이지 않는다.
    /// </summary>
    public static bool IsEscapeClaimed => s_openCount > 0 || s_escapeClaimedFrame == Time.frameCount;

    private LobbyResultPanelRules Rules => _rules ??= new LobbyResultPanelRules(autoCloseSeconds);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_openCount = 0;
        s_escapeClaimedFrame = -1;
    }

    private void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseButton);

        if (!Rules.IsOpen && content != null)
            content.SetActive(false);
    }

    private void OnDestroy()
    {
        if (closeButton != null)
            closeButton.onClick.RemoveListener(OnCloseButton);
        SetCountedOpen(false);
    }

    private void Update()
    {
        if (!Rules.IsOpen)
            return;

        if (closeWithEscape && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            s_escapeClaimedFrame = Time.frameCount;
            if (Rules.HandleEscape())
                ApplyClosed(LobbyResultCloseReason.Escape);
            return;
        }

        if (Rules.Tick(Time.unscaledTime))
            ApplyClosed(LobbyResultCloseReason.AutoClose);
    }

    /// <summary>
    /// 현재 <see cref="SessionResult"/> 로 채워서 연다. 결과가 없으면 아무것도 안 한다.
    /// 이미 열려 있으면 다시 채우고 자동 닫힘 타이머를 처음부터 잰다.
    /// </summary>
    public void Show()
    {
        if (!Rules.TryOpen(HasResult, Time.unscaledTime))
            return;

        if (content != null)
            content.SetActive(true);
        Refresh();
        SetCountedOpen(true);
    }

    /// <summary>닫는다(결과는 그대로 — 다시 Show 가능).</summary>
    public void Hide()
    {
        if (Rules.Close())
            ApplyClosed(LobbyResultCloseReason.Api);
    }

    /// <summary>
    /// 열고 닫기와 무관하게 내용만 현재 <see cref="SessionResult"/> 로 다시 그린다.
    /// <paramref name="localClientIdOverride"/> = "나" 강조 기준(에디터 미리보기용). null 이면 NetworkManager 의 LocalClientId.
    /// </summary>
    public void Refresh(ulong? localClientIdOverride = null)
    {
        if (statsView != null)
        {
            // 행은 순위 순서로 그린다(1위 = 왕관). 결과가 없으면 빈 순위 → 행 없음.
            LobbyResultRanking.Rank(SessionResult.HasValue ? SessionResult.Players : null, ScoreWeights, _ranking);
            statsView.Apply(localClientIdOverride ?? ResultStatsView.GetLocalClientId(), _ranking);
        }

        if (playTimeText != null)
            playTimeText.text = SessionResult.HasValue ? SessionResult.FormatSurvival() : "--:--";

        if (outcomeText != null && SessionResult.HasValue)
            outcomeText.color = OutcomeColor(SessionResult.Outcome);
    }

    /// <summary>인스펙터 순위 계수.</summary>
    public LobbyResultScoreWeights ScoreWeights => new LobbyResultScoreWeights
    {
        NoDeathMultiplier = noDeathMultiplier,
        DeathMultiplier = deathMultiplier,
        CounterBase = counterBase,
        CounterMultiplierCap = counterMultiplierCap
    };

    private Color OutcomeColor(SessionOutcome outcome)
    {
        switch (outcome)
        {
            case SessionOutcome.Cleared: return clearedColor;
            case SessionOutcome.Aborted: return abortedColor;
            default: return failedColor;
        }
    }

    private void OnCloseButton()
    {
        if (Rules.HandleCloseButton())
            ApplyClosed(LobbyResultCloseReason.Button);
    }

    private void ApplyClosed(LobbyResultCloseReason reason)
    {
        if (content != null)
            content.SetActive(false); // UiModalBlocker.OnDisable 가 입력 차단을 푼다
        SetCountedOpen(false);
        Closed?.Invoke(reason);
    }

    private void SetCountedOpen(bool open)
    {
        if (_countedOpen == open)
            return;

        _countedOpen = open;
        s_openCount += open ? 1 : -1;
        if (s_openCount < 0)
            s_openCount = 0;
    }
}
