using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 설정창 조작 탭의 키 변경 묶음 — "Reset to Default" 버튼과 상태 줄(맞바꿈 알림). 행은 <see cref="KeyRebindRow"/>.
/// </summary>
public sealed class KeyRebindPanel : MonoBehaviour
{
    [SerializeField] Button resetButton;
    [SerializeField] TMP_Text statusText;

    void Awake()
    {
        if (resetButton != null) resetButton.onClick.AddListener(ResetAll);
    }

    void OnEnable()
    {
        KeyRebindRow.Notice += Show;
        Show(string.Empty);
    }

    void OnDisable() => KeyRebindRow.Notice -= Show;

    void ResetAll()
    {
        if (KeyRebindRow.IsRebinding) return;
        KeyBindingConfig.ResetAll();
        Show(SettingsUiStrings.ControlsResetDone.Text);
    }

    void Show(string message)
    {
        if (statusText != null) statusText.text = message;
    }
}
