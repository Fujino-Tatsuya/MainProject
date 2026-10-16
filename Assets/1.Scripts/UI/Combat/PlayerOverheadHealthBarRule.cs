using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 플레이어 머리 위 체력바 규칙. 보는 사람 기준으로 내 캐릭터는 초록, 파티원은 파랑.
/// 사망·Soul 상태에서도 숨기지 않는다(체력 0 그대로 표시).
/// 체력바 옆 대시 아이콘(IOverheadDashIndicator)도 공급한다 — 로컬 플레이어가 살아 있을 때만, 준비 상태는 PlayerDashController.IsReady.
/// 이름 라벨은 상태이상·붙잡힘 중에 그 이름으로 바뀐다(할 일 50).
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerOverheadHealthBarRule : MonoBehaviour, IOverheadHealthBarRule, IOverheadDashIndicator
{
    [Tooltip("비워두면 부모에서 찾는다.")]
    [SerializeField] Player player;
    [SerializeField] Color localPlayerColor = new Color(0.2f, 0.8f, 0.25f, 1f);
    [SerializeField] Color partyMemberColor = new Color(0.25f, 0.55f, 1f, 1f);
    [Tooltip("{0} = 1부터 세는 플레이어 번호. Strings 시트에 UI.Overhead.PlayerLabel 행이 있으면 그쪽이 이긴다(이 값은 폴백).")]
    [SerializeField] string labelFormat = "P{0}";

    ulong _labelClientId = ulong.MaxValue;
    OverheadStatusLabel _labelStatus;
    string _label;
    PlayerDashController _dash;
    PlayerLifeCycleController _lifeCycle;
    PlayerEncounterLock _encounterLock;
    StatusEffectController _statusEffects;

    void Awake()
    {
        if (player == null)
            player = GetComponentInParent<Player>();
        if (player != null)
        {
            _dash = player.GetComponent<PlayerDashController>();
            _lifeCycle = player.GetComponent<PlayerLifeCycleController>();
            _encounterLock = player.GetComponent<PlayerEncounterLock>();
            _statusEffects = player.GetComponent<StatusEffectController>();
        }
    }

    // 연출 숨김(보스 엘리베이터) 동안만 감춘다.
    public bool ShouldShow => player != null && (_encounterLock == null || !_encounterLock.IsCinematicHidden);

    // 오프라인(IsSpawned=false)은 혼자 하는 로컬 플레이어로 본다.
    // 소유권은 스폰 후에 확정되므로 매 프레임 판정한다.
    bool IsLocalPlayer => player != null && (!player.IsSpawned || player.IsOwner);

    public Color FillColor => IsLocalPlayer ? localPlayerColor : partyMemberColor;

    // 예측 충전은 오너만 진행시키므로 원격 플레이어 머리 위엔 띄우지 않는다.
    public bool ShowDashIcon => _dash != null && OverheadDashIconRules.ShouldShow(
        IsLocalPlayer, _lifeCycle != null, _lifeCycle != null ? _lifeCycle.State : PlayerLifeState.Alive);

    public bool DashReady => _dash != null && _dash.IsReady;

    // 복제되는 이름이 없어 OwnerClientId 로 로컬 계산한다(권한·NetworkVariable 추가 없음).
    // 출시 때는 스팀 닉네임으로 교체 예정.
    // 상태이상(CC 6종)·붙잡힘 중에는 이름 대신 그 이름 하나를 띄운다(할 일 50, OverheadStatusLabelRules).
    // 상태이상 목록·구속 여부는 복제값이라 모든 피어가 같은 글자를 본다. 문자열은 (clientId, 표시)가 바뀔 때만 만든다.
    public string Label
    {
        get
        {
            if (player == null)
                return null;

            ulong clientId = player.IsSpawned ? player.OwnerClientId : 0UL;
            OverheadStatusLabel status = CurrentStatus();
            if (clientId != _labelClientId || status != _labelStatus)
            {
                _labelClientId = clientId;
                _labelStatus = status;
                _label = status == OverheadStatusLabel.None
                    ? LabelText(clientId + 1, labelFormat)
                    : StatusText(status);
            }
            return _label;
        }
    }

    // 오프라인(IsSpawned=false)은 상태이상이 걸리지 않으므로 지금처럼 이름만.
    OverheadStatusLabel CurrentStatus()
    {
        if (!player.IsSpawned)
            return OverheadStatusLabel.None;

        return OverheadStatusLabelRules.Select(
            _statusEffects != null ? _statusEffects.ActiveEffects : StatusEffectType.None,
            player.IsRestrainedReplicated,
            _lifeCycle != null,
            _lifeCycle != null ? _lifeCycle.State : PlayerLifeState.Alive);
    }

    void OnEnable() => Loc.Changed += ForceRelabel;
    void OnDisable() => Loc.Changed -= ForceRelabel;
    void ForceRelabel() => _labelClientId = ulong.MaxValue;

    /// <summary>머리 위 상태이상 문구 — 붙잡힘은 <see cref="UiStrings.StatusEffectGrabbed"/>, CC 는 HUD 와 같은 <c>UI.StatusEffect.&lt;enum&gt;</c>.</summary>
    static string StatusText(OverheadStatusLabel status) =>
        status == OverheadStatusLabel.Grabbed
            ? UiStrings.StatusEffectGrabbed.Text
            : StatusEffectHUD.DisplayName(OverheadStatusLabelRules.ToStatusEffect(status));

    /// <summary>
    /// 머리 위 이름 — Strings 시트에 <see cref="UiStrings.OverheadPlayerLabel"/>({n}) 행이 있으면 그 문구,
    /// 없으면 직렬화된 <paramref name="legacyFormat"/>({0}) 폴백(프리팹 값을 고치지 않고 키로 옮기기 위해서다).
    /// </summary>
    public static string LabelText(ulong number, string legacyFormat)
    {
        string n = number.ToString(CultureInfo.InvariantCulture);
        string template = Loc.Get(UiStrings.OverheadPlayerLabel.Key, null);
        if (template != null)
            return TutorialGuideText.Fill(template, new Dictionary<string, string> { ["n"] = n });

        return string.IsNullOrEmpty(legacyFormat)
            ? TutorialGuideText.Fill(UiStrings.OverheadPlayerLabel.Ko, new Dictionary<string, string> { ["n"] = n })
            : string.Format(legacyFormat, n);
    }
}
