using TMPro;
using UnityEngine;

/// <summary>
/// 로비 카운트다운 표시(3.LobbyScene UI › Slot_Countdown) — PLAN-48 §4.6, C10.
/// <see cref="LobbySession.CountdownEndServerTime"/> 와 NetworkManager.ServerTime 으로 남은 초를 매 프레임 계산한다 — 모든 피어가 서버 시각 기준 같은 숫자.
/// 카운트다운이 없으면 <see cref="root"/> 를 끈다. 0초(시작 래치 뒤 로딩 전) = "출발!".
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyCountdownView : MonoBehaviour
{
    [Tooltip("켜고 끄는 표시 본체(이 컴포넌트가 붙은 오브젝트 말고 자식).")]
    [SerializeField] private GameObject root;

    [SerializeField] private TMP_Text label;

    private int _shownSeconds = -1;

    private void OnDisable()
    {
        SetVisible(false);
    }

    private void Update()
    {
        var session = LobbySession.Active;
        var active = session != null && session.IsCountdownActive;
        SetVisible(active);
        if (!active || label == null)
            return;

        // 초가 바뀔 때만 문구를 만든다(매 프레임 Format 할당 회피).
        var remaining = session.CountdownRemainingSeconds;
        var seconds = LobbyUiStrings.CountdownDisplaySeconds(remaining);
        if (seconds == _shownSeconds)
            return;

        _shownSeconds = seconds;
        label.text = LobbyUiStrings.FormatCountdown(remaining);
    }

    private void SetVisible(bool visible)
    {
        if (!visible)
            _shownSeconds = -1;
        if (root != null && root.activeSelf != visible)
            root.SetActive(visible);
    }
}
