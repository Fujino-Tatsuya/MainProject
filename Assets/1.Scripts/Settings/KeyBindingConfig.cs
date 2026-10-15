using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 핵심 키 리바인드(PLAN-settings.md S7·S14) — 저장·적용의 단일 창구. 저장은 PlayerPrefs(JSON 오버라이드).
///
/// 기준 에셋 = 프로젝트 전역 액션 <see cref="InputSystem.actions"/>(<c>InputSystem_Actions</c>).
/// 플레이어의 <see cref="PlayerInput"/> 이 다른 인스턴스(복제본)를 쓸 수 있어 <see cref="ApplyTo"/> 로 같은 오버라이드를 다시 건다.
/// 키보드·마우스 바인딩만 다룬다(패드 제외, 이동 WASD 고정).
/// </summary>
public static class KeyBindingConfig
{
    const string OverridesKey = "Settings.Input.BindingOverrides";

    /// <summary>설정창에 노출하는 액션(Player 맵). 순서 = 화면 순서.</summary>
    public static readonly string[] RebindableActions =
    {
        "Player/Attack", "Player/Interrupt", "Player/SkillMain", "Player/SkillSub", "Player/SkillUltimate",
        "Player/Dash", "Player/Interact",
    };

    /// <summary>오버라이드가 바뀌었다(리바인드·초기화). 입력을 따로 들고 있는 쪽(게이트·안내)이 다시 읽는다.</summary>
    public static event Action Changed;

    static InputActionAsset Asset => InputSystem.actions;

    /// <summary>키보드·마우스 바인딩 인덱스(첫 번째 비합성 바인딩). 없으면 -1.</summary>
    public static int KeyboardMouseBindingIndex(InputAction action)
    {
        if (action == null) return -1;
        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding b = action.bindings[i];
            if (b.isComposite || b.isPartOfComposite) continue;
            string p = b.path ?? string.Empty;
            if (p.StartsWith("<Keyboard>", StringComparison.Ordinal) || p.StartsWith("<Mouse>", StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    /// <summary>지금 적용된 키보드·마우스 경로(오버라이드 반영). 예: <c>&lt;Keyboard&gt;/f</c>.</summary>
    public static string EffectivePath(string actionPath)
    {
        InputAction a = Asset != null ? Asset.FindAction(actionPath) : null;
        int i = KeyboardMouseBindingIndex(a);
        return i >= 0 ? a.bindings[i].effectivePath : null;
    }

    /// <summary>사람이 읽는 키 이름(예: "F", "Space", "Right Button").</summary>
    public static string DisplayName(string actionPath)
    {
        InputAction a = Asset != null ? Asset.FindAction(actionPath) : null;
        int i = KeyboardMouseBindingIndex(a);
        // 🔴 인터랙션 표기를 뺀다 — Interact 는 액션에 Hold 가 걸려 있어 "Hold 1" 로 나왔다(팀장 10-13 빌드).
        return i >= 0 ? a.GetBindingDisplayString(i, InputBinding.DisplayStringOptions.DontIncludeInteractions) : "?";
    }

    /// <summary>같은 키를 이미 쓰는 다른 노출 액션(없으면 null). 리바인드 확인 창이 쓴다.</summary>
    public static string FindConflict(string actionPath, string candidatePath)
    {
        foreach (string other in RebindableActions)
        {
            if (other == actionPath) continue;
            if (string.Equals(EffectivePath(other), candidatePath, StringComparison.OrdinalIgnoreCase)) return other;
        }
        return null;
    }

    /// <summary>오버라이드를 저장하고 알린다. 리바인드 UI 가 완료 때 부른다.</summary>
    public static void Save()
    {
        if (Asset == null) return;
        PlayerPrefs.SetString(OverridesKey, Asset.SaveBindingOverridesAsJson());
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    /// <summary>모든 노출 액션을 에셋 기본값으로(설정창 "기본값 복원").</summary>
    public static void ResetAll()
    {
        if (Asset == null) return;
        Asset.RemoveAllBindingOverrides();
        PlayerPrefs.DeleteKey(OverridesKey);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    /// <summary>다른 에셋 인스턴스(플레이어의 PlayerInput 복제본 등)에 저장된 오버라이드를 건다.</summary>
    public static void ApplyTo(InputActionAsset target)
    {
        if (target == null) return;
        target.RemoveAllBindingOverrides();
        string json = PlayerPrefs.GetString(OverridesKey, string.Empty);
        if (!string.IsNullOrEmpty(json)) target.LoadBindingOverridesFromJson(json);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void LoadAtBoot() => ApplyTo(Asset);
}
