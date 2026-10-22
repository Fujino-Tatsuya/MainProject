using TMPro;
using UnityEngine;

/// <summary>
/// "집결 n/m" + 카운트다운 남은 초 표시(할 일 86 PLAN §3.1). <see cref="PartyGatherZone"/> 의 NetworkVariable 만 읽으므로 모든 피어가 같은 값을 본다.
/// 대상 구역이 활성이고 확정 전일 때만 <see cref="root"/> 를 켠다(비활성 구역은 표시 없음). 남은 초는 서버 시각 기준 매 프레임 계산(옛 LobbyCountdownView 패턴).
/// </summary>
[DisallowMultipleComponent]
public sealed class PartyGatherStatusView : MonoBehaviour
{
    [Tooltip("표시할 구역. 비우면 활성·확정 전인 첫 구역(PartyGatherZone.FirstActive)을 따른다.")]
    [SerializeField] private PartyGatherZone zone;

    [Tooltip("켜고 끄는 표시 본체(이 컴포넌트가 붙은 오브젝트 말고 자식).")]
    [SerializeField] private GameObject root;

    [Tooltip("\"집결 n/m\".")]
    [SerializeField] private TMP_Text countLabel;

    [Tooltip("카운트다운 남은 초. 카운트다운이 없으면 끈다. 비워도 된다.")]
    [SerializeField] private TMP_Text countdownLabel;

    private int _shownGathered = -1;
    private int _shownTotal = -1;
    private int _shownSeconds = -1;

    private void OnEnable()
    {
        Loc.Changed += Invalidate;
    }

    private void OnDisable()
    {
        Loc.Changed -= Invalidate;
        SetVisible(false);
    }

    private void Update()
    {
        var target = zone != null ? zone : PartyGatherZone.FirstActive;
        var visible = target != null && target.IsActive && !target.IsConfirmed;
        SetVisible(visible);
        if (!visible)
            return;

        // 값이 바뀔 때만 문구를 만든다(매 프레임 Format 할당 회피).
        var gathered = target.GatheredCount;
        var total = target.ParticipantCount;
        if (countLabel != null && (gathered != _shownGathered || total != _shownTotal))
        {
            _shownGathered = gathered;
            _shownTotal = total;
            countLabel.text = LobbyUiStrings.FormatGatherCount(gathered, total);
        }

        if (countdownLabel == null)
            return;

        var remaining = target.IsCountdownActive ? target.CountdownRemainingSeconds : 0d;
        var seconds = LobbyUiStrings.CountdownDisplaySeconds(remaining);
        if (seconds == _shownSeconds)
            return;

        _shownSeconds = seconds;
        countdownLabel.enabled = seconds > 0;
        countdownLabel.text = LobbyUiStrings.FormatGatherCountdown(remaining);
    }

    private void Invalidate()
    {
        _shownGathered = -1;
        _shownTotal = -1;
        _shownSeconds = -1;
    }

    private void SetVisible(bool visible)
    {
        if (!visible)
            Invalidate();
        if (root != null && root.activeSelf != visible)
            root.SetActive(visible);
    }
}
