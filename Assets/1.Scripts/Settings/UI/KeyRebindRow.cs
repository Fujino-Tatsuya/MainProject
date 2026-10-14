using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 설정창 조작 탭의 키 변경 한 줄(PLAN-settings S7·S14) — 액션 이름 + 현재 키 버튼.
/// 버튼을 누르면 다음 키·마우스 버튼 입력으로 바꾼다. <b>Esc = 취소.</b>
/// 이미 다른 노출 액션이 쓰는 키면 **두 키를 맞바꾼다**(같은 키 두 액션이 동시에 나가는 상태를 만들지 않음).
/// 저장·적용은 <see cref="KeyBindingConfig"/>.
/// </summary>
public sealed class KeyRebindRow : MonoBehaviour
{
    /// <summary>키 변경 대기 중. 타이틀·맵 매니저가 이 동안 Esc 로 옵션 창을 닫지 않는다(Esc = 변경 취소).</summary>
    public static bool IsRebinding { get; private set; }

    /// <summary>맞바꿈 등 알릴 일이 생겼다 — <see cref="KeyRebindPanel"/> 이 상태 줄에 띄운다.</summary>
    public static event Action<string> Notice;

    [SerializeField] string actionPath = "Player/SkillMain";
    [SerializeField] TMP_Text actionLabel;
    [SerializeField] Button bindButton;
    [SerializeField] TMP_Text keyText;

    InputActionRebindingExtensions.RebindingOperation _op;

    public string ActionPath => actionPath;

    void Awake()
    {
        if (bindButton != null) bindButton.onClick.AddListener(StartRebind);
    }

    void OnEnable()
    {
        KeyBindingConfig.Changed += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        KeyBindingConfig.Changed -= Refresh;
        CancelRebind();
    }

    void Refresh()
    {
        if (keyText != null && _op == null) keyText.text = KeyBindingConfig.DisplayName(actionPath);
    }

    void StartRebind()
    {
        if (IsRebinding) return;
        InputAction action = InputSystem.actions != null ? InputSystem.actions.FindAction(actionPath) : null;
        int index = KeyBindingConfig.KeyboardMouseBindingIndex(action);
        if (index < 0)
        {
            Debug.LogWarning($"[KeyRebind] {actionPath} 에 키보드·마우스 바인딩이 없다 — 변경 불가.", this);
            return;
        }

        string oldPath = action.bindings[index].effectivePath;
        bool wasEnabled = action.enabled;
        action.Disable();   // 진행 중인 액션은 리바인드할 수 없다
        IsRebinding = true;
        if (keyText != null) keyText.text = SettingsUiStrings.ControlsPressKey.Text;

        _op = action.PerformInteractiveRebinding(index)
            .WithControlsHavingToMatchPath("<Keyboard>")
            .WithControlsHavingToMatchPath("<Mouse>")
            .WithControlsExcluding("<Mouse>/position")
            .WithControlsExcluding("<Mouse>/delta")
            .WithControlsExcluding("<Mouse>/scroll")
            .WithCancelingThrough("<Keyboard>/escape")
            .OnMatchWaitForAnother(0.1f)
            .OnComplete(op => Finish(action, index, oldPath, wasEnabled, completed: true))
            .OnCancel(op => Finish(action, index, oldPath, wasEnabled, completed: false))
            .Start();
    }

    void Finish(InputAction action, int index, string oldPath, bool wasEnabled, bool completed)
    {
        _op?.Dispose();
        _op = null;
        if (wasEnabled) action.Enable();

        if (completed)
        {
            string newPath = action.bindings[index].effectivePath;
            string other = KeyBindingConfig.FindConflict(actionPath, newPath);
            if (other != null)
            {
                // 맞바꾼다 — 상대 액션에 내 예전 키를 준다.
                InputAction otherAction = InputSystem.actions.FindAction(other);
                int otherIndex = KeyBindingConfig.KeyboardMouseBindingIndex(otherAction);
                if (otherIndex >= 0) otherAction.ApplyBindingOverride(otherIndex, oldPath);
                Notice?.Invoke(SettingsUiStrings.ControlsSwapped.Format(
                    new System.Collections.Generic.Dictionary<string, string> { ["action"] = ActionLabel(other) }));
            }
            KeyBindingConfig.Save();   // Changed → 모든 줄·게이트·플레이어 입력이 다시 읽는다
        }

        // 다음 프레임까지 막는다 — 취소에 쓴 Esc 가 같은 프레임에 매니저의 "옵션 닫기"로 새지 않게.
        // 패널이 닫히며 취소된 경우(비활성)는 코루틴을 못 돌리므로 바로 푼다.
        if (isActiveAndEnabled) StartCoroutine(ClearRebindingNextFrame());
        else IsRebinding = false;
        Refresh();
    }

    System.Collections.IEnumerator ClearRebindingNextFrame()
    {
        yield return null;
        IsRebinding = false;
    }

    void CancelRebind()
    {
        if (_op == null) return;
        _op.Cancel();   // OnCancel → Finish(비활성이라 IsRebinding 도 거기서 바로 풀린다)
        _op?.Dispose();
        _op = null;
        IsRebinding = false;
    }

    // "Player/SkillMain" → UI.Settings.Controls.SkillMain(지금 언어). 키가 없으면 액션 이름 그대로.
    public static string ActionLabel(string path)
    {
        int slash = path.LastIndexOf('/');
        string name = slash >= 0 ? path.Substring(slash + 1) : path;
        return SettingsUiStrings.TryGetAction(name, out UiString label) ? label.Text : name;
    }
}
