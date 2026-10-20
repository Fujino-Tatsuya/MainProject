// ----------------------------------------------------------------------------
//  DevBossAttackHotkeys.cs — 23호 다음 행동을 단축키로 예약 (개발용)
//
//  F3  = 다음 행동 **점프·점프·점프 콤보** 예약(1회). 쿨·가중치 무시, 거리창만 지킨다.
//        3번째 착지 간파 창(콤보 타 landingCounterWindow)을 바로 확인하려고 둔다.
//
//  🔸 경석 10-20: 예전 단타 예약 5종(F3 훅·Shift+F3 비우기·F4 어퍼·F6 점프·F9 잡기·F11 돌진)은 걷어 냈다.
//     단타 점프 예약으로는 콤보 마지막 타의 간파 창이 안 나와서 확인이 안 됐다. F4·F6·F9·F11 은 이제 비어 있다
//     (F9 는 DevMonsterKill 과 겹치던 것도 함께 풀렸다).
//
//  동작: 예약은 **다음 행동 선택 시점**에 소비된다(TwentyThreeBoss.SelectAttackSlot). 진행 중 공격·콤보는 끊지 않는다.
//        페이즈 시퀀스(차징·레이드 기믹)가 대기 중이면 그쪽이 먼저다.
//
//  🔴 **호스트 창에서만** 동작한다 — 보스 FSM 은 서버에서만 돈다. 클라 창에서 누르면 토스트로 알린다.
//  ⚠️ DevBossEntranceWarp 와 같은 부류: 씬에 배치하지 않고 런타임에 스스로 생긴다. 빌드에는 Standalone 심볼
//     DEV_HOTKEYS(또는 Development Build)가 있을 때만 들어간다. 로그는 빌드에서도 남도록 Debug.Log.
// ----------------------------------------------------------------------------
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public sealed class DevBossAttackHotkeys : MonoBehaviour
{
    const float ToastSeconds = 2.5f;
    const string Key = "F3";

    string _toast;
    float _toastUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var go = new GameObject("[Dev] BossAttackHotkeys");
        go.AddComponent<DevBossAttackHotkeys>();
        DontDestroyOnLoad(go);
    }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.f3Key.wasPressedThisFrame) ReserveJumpCombo();
#else
        if (Input.GetKeyDown(KeyCode.F3)) ReserveJumpCombo();
#endif
    }

    void ReserveJumpCombo()
    {
        TwentyThreeBoss boss = ResolveBoss();
        if (boss == null) return;

        Toast(boss.DevReserveJumpCombo()
            ? $"{Key}: 다음 행동 = 점프·점프·점프 콤보 예약"
            : $"{Key}: 점프·점프·점프 콤보가 데이터에 없다 — 예약 안 함");
    }

    TwentyThreeBoss ResolveBoss()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) { Toast($"{Key}: 네트워크가 아직 안 떴다"); return null; }
        if (!nm.IsServer) { Toast($"{Key}: 호스트 창에서만 예약할 수 있다(보스는 서버에서만 돈다)"); return null; }

        TwentyThreeBoss boss = FindAnyObjectByType<TwentyThreeBoss>();
        if (boss == null || !boss.IsSpawned) { Toast($"{Key}: 23호가 아직 없다"); return null; }
        return boss;
    }

    void Toast(string message)
    {
        _toast = message;
        _toastUntil = Time.unscaledTime + ToastSeconds;
    }

    void OnGUI()
    {
        if (string.IsNullOrEmpty(_toast) || Time.unscaledTime > _toastUntil) return;
        // F5 워프 토스트(12,12)와 겹치지 않게 한 줄 아래.
        var style = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
        style.normal.textColor = Color.cyan;
        GUI.Label(new Rect(12f, 44f, 720f, 28f), _toast, style);
    }
}
#endif
