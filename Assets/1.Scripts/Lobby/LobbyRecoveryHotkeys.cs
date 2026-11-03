// ----------------------------------------------------------------------------
//  LobbyRecoveryHotkeys.cs — 로비(3.LobbyScene) 예외 상황 단축키 Alt+숫자 (할 일 112, QA·부스 운영용)
//
//  Alt+1 튜토리얼 클리어 켜기/끄기(누른 사람) · Alt+2 멈춤 탈출(누른 사람) · Alt+3 로비 다시 불러오기(호스트만).
//  키 표 = LobbyRecoveryHotkeyRules.Table — 키 추가는 그 표 한 줄 + 서버 실행(LobbyTrainingDirector.Recovery.cs) 한 갈래.
//
//  부착: 씬에 배치하지 않는다 — 로비 훈련 진행기가 스폰되면(StepChanged) 그 오브젝트에 스스로 붙는다(MapSceneSkipController 와 같은 방식).
//  진행기는 로비 씬에만 있어 다른 씬(맵 튜토리얼 Alt+1~0)과 겹치지 않는다.
//
//  🔴 에디터·Development Build·DEV_HOTKEYS 빌드에만 있다. 심볼을 지운 릴리스 빌드엔 클래스·RPC 가 없어 입력이 무시된다 — 호스트·클라는 같은 심볼 빌드여야 한다.
//  🔴 로그는 Debug.Log — Edit.* 는 에디터 전용이라 빌드에서 사라진다.
//
//  화면: 결과 토스트(서버 응답) + Alt 를 누르는 동안 오른쪽 위에 키 목록(지금 클리어 플래그 상태 포함).
// ----------------------------------------------------------------------------
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public sealed class LobbyRecoveryHotkeys : MonoBehaviour
{
    const string Tag = "[LobbyRecovery]";
    const float ToastSeconds = 3f;

    LobbyTrainingDirector _director;
    NetworkManager _nm;

    string _toast;
    float _toastUntil;
    GUIStyle _toastStyle;

    // ─── 부착(모든 피어 — 입력·토스트는 각자, 실행은 서버) ───────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void InstallBootstrap()
    {
        LobbyTrainingDirector.StepChanged -= Ensure;
        LobbyTrainingDirector.StepChanged += Ensure;
    }

    static void Ensure()
    {
        LobbyTrainingDirector d = LobbyTrainingDirector.Active;
        if (d == null || !d.IsSpawned) return;
        if (d.GetComponent<LobbyRecoveryHotkeys>() != null) return;
        d.gameObject.AddComponent<LobbyRecoveryHotkeys>();
    }

    void OnEnable()
    {
        _director = GetComponent<LobbyTrainingDirector>();
        _nm = NetworkManager.Singleton;
        if (_director == null || _nm == null || !_nm.IsListening)
        {
            enabled = false;
            return;
        }

        LobbyTrainingDirector.LocalRecoveryResult += HandleResult;
    }

    void OnDisable()
    {
        LobbyTrainingDirector.LocalRecoveryResult -= HandleResult;
    }

    bool IsHost => _nm != null && _nm.IsServer && _nm.IsClient;

    // ─── 입력 ────────────────────────────────────────────────────────────

    void Update()
    {
        if (_director == null || !_director.IsSpawned) return;
        if (!IsAltHeld() || !TryReadDigit(out int digit)) return;
        if (LobbyRecoveryHotkeyRules.TryFromDigit(digit, out LobbyRecoveryHotkey hotkey)) Request(hotkey);
    }

    static bool IsAltHeld()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        return kb != null && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed);
#else
        return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
#endif
    }

    static bool TryReadDigit(out int digit)
    {
        for (digit = 0; digit <= 9; digit++)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null && kb[DigitKey(digit)].wasPressedThisFrame) return true;
#else
            if (Input.GetKeyDown(KeyCode.Alpha0 + digit)) return true;
#endif
        }
        digit = -1;
        return false;
    }

#if ENABLE_INPUT_SYSTEM
    // Key.Digit1..Digit9 가 연속이고 Digit0 은 그 뒤에 있다.
    static Key DigitKey(int digit) => digit == 0 ? Key.Digit0 : Key.Digit1 + (digit - 1);
#endif

    void Request(LobbyRecoveryHotkey hotkey)
    {
        if (!LobbyRecoveryHotkeyRules.CanRequest(hotkey.Action, IsHost))
        {
            Toast(LobbyRecoveryHotkeyRules.Describe(hotkey.Action, LobbyRecoveryResult.HostOnly));
            Debug.LogWarning($"{Tag} Alt+{hotkey.Digit} {hotkey.Label} 은 호스트만 — 이 피어(client {_nm.LocalClientId})는 보내지 않는다.", this);
            return;
        }

        Debug.Log($"{Tag} Alt+{hotkey.Digit} {hotkey.Label} 요청 (client {_nm.LocalClientId})", this);
        _director.RequestRecovery(hotkey.Action);
    }

    // [요청한 피어] 서버 결과. 멈춤 탈출이면 오너 쪽 대시·넉백도 끊는다(보스방 이동과 같은 순서 — 서버 순간이동 RPC 가 먼저 온다).
    void HandleResult(LobbyRecoveryAction action, LobbyRecoveryResult result)
    {
        if (action == LobbyRecoveryAction.Unstuck && result == LobbyRecoveryResult.Unstuck) ClearOwnerMotion();

        string message = LobbyRecoveryHotkeyRules.Describe(action, result);
        if (result == LobbyRecoveryResult.Failed) Debug.LogWarning($"{Tag} {message}", this);
        else Debug.Log($"{Tag} {message}", this);
        Toast(message);
    }

    static void ClearOwnerMotion()
    {
        Player local = Player.LocalPlayer;
        if (local == null) return;
        if (local.TryGetComponent(out PlayerStateController state)) state.EndDash();
        if (local.TryGetComponent(out PlayerMotor motor)) motor.ClearKnockbackVelocity();
    }

    // ─── 화면(로컬) ──────────────────────────────────────────────────────

    void Toast(string message)
    {
        _toast = message;
        _toastUntil = Time.unscaledTime + ToastSeconds;
    }

    void OnGUI()
    {
        if (!string.IsNullOrEmpty(_toast) && Time.unscaledTime <= _toastUntil)
        {
            _toastStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = Color.yellow } };
            GUI.Label(new Rect(12f, 44f, 900f, 28f), _toast, _toastStyle);
        }

        if (_director == null || !_director.IsSpawned || !IsAltHeld()) return;
        DrawPanel();
    }

    // Alt 를 누르는 동안만 — 키 목록(버튼 아님, 단축키로만 실행). 호스트 전용 줄은 클라에서 흐리게.
    void DrawPanel()
    {
        const float w = 300f, h = 22f, pad = 6f;
        LobbyRecoveryHotkey[] table = LobbyRecoveryHotkeyRules.Table;
        float x = Screen.width - w - 12f, y = 12f;
        GUI.Box(new Rect(x - pad, y - pad, w + pad * 2f, h * (table.Length + 1) + pad * 2f), GUIContent.none);

        bool cleared = _director.IsTrainingCleared(_nm.LocalClientId);
        GUI.Label(new Rect(x, y, w, h), $"로비 예외 상황 (튜토리얼 클리어: {(cleared ? "켜짐" : "꺼짐")})");

        bool enabledBefore = GUI.enabled;
        for (int i = 0; i < table.Length; i++)
        {
            GUI.enabled = LobbyRecoveryHotkeyRules.CanRequest(table[i].Action, IsHost);
            GUI.Label(new Rect(x, y + h * (i + 1), w, h), $"Alt+{table[i].Digit}  {table[i].Label}");
        }
        GUI.enabled = enabledBefore;
    }
}
#endif
