using TMPro;
using UnityEngine;

/// <summary>
/// TRAIN_02 일반 전투 표시(할 일 86 T4) — "일반 전투" + "남은 상대 n/m". <see cref="LobbyTrainingCombat"/> 의 복제 수만 읽으므로 늦게 그린 클라·재접속도 지금 상태를 본다.
/// 전투 진행 중이고 내가 훈련 명단일 때만 <see cref="root"/> 를 켠다(늦은 참가자는 표시 없음 — <see cref="LobbyWeaponCheckView"/> 와 같은 규칙).
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyTrainingCombatView : MonoBehaviour
{
    [Tooltip("켜고 끄는 표시 본체(이 컴포넌트가 붙은 오브젝트 말고 자식).")]
    [SerializeField] private GameObject root;

    [Tooltip("제목(\"일반 전투\"). 비워도 된다.")]
    [SerializeField] private TMP_Text titleLabel;

    [Tooltip("\"남은 상대 n/m\".")]
    [SerializeField] private TMP_Text remainingLabel;

    private int _shownRemaining = -1;
    private int _shownTotal = -1;

    private void OnEnable() => Loc.Changed += Invalidate;

    private void OnDisable()
    {
        Loc.Changed -= Invalidate;
        SetVisible(false);
    }

    private void Update()
    {
        var combat = LobbyTrainingCombat.Active;
        var director = LobbyTrainingDirector.Active;
        var nm = Unity.Netcode.NetworkManager.Singleton;
        var visible = combat != null && director != null && nm != null && combat.IsRunning && director.IsInTrainingRoster(nm.LocalClientId);
        SetVisible(visible);
        if (!visible)
            return;

        var remaining = combat.Remaining;
        var total = combat.Total;
        if (remaining == _shownRemaining && total == _shownTotal)
            return;

        _shownRemaining = remaining;
        _shownTotal = total;
        if (titleLabel != null)
            titleLabel.text = LobbyUiStrings.Train02Title.Text;
        if (remainingLabel != null)
            remainingLabel.text = LobbyUiStrings.FormatTrain02Remaining(remaining, total);
    }

    private void Invalidate()
    {
        _shownRemaining = -1;
        _shownTotal = -1;
    }

    private void SetVisible(bool visible)
    {
        if (!visible)
            Invalidate();
        if (root != null && root.activeSelf != visible)
            root.SetActive(visible);
    }
}
