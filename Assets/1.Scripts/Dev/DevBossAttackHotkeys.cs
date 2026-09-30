// ----------------------------------------------------------------------------
//  DevBossAttackHotkeys.cs — 23호 다음 공격을 단축키로 강제 예약 (개발용 · 팀장 요청 2026-09-29)
//
//  목적: 특정 패턴(훅·어퍼·점프·돌진·잡기)을 룰렛 운에 맡기지 않고 바로 재현해 로직·로그를 빨리 확인한다.
//
//  키 — 비어 있던 F3·F4·F6·F11 + F9(LookToggle 룩 토글 키를 뺀 자리 — 팀장 09-29).
//       이미 쓰는 키: F1·F2(HitVFXDebugHUD) · F5(보스방 진입 워프) · F7(RetroCRT/RenderCostAB) · F8(ProfilerHUD)
//       · F10(디버그 부활) · F12(FragmentExploder).
//    F3   훅 (누를 때마다 좌 → 우 번갈아)
//    F4   어퍼컷
//    F6   점프어택
//    F9   잡기
//    F11  돌진
//    Shift+F3  예약 대기열 비우기
//
//  예약은 **대기열**이다 — 최대 TwentyThreeBoss.DevReservationCapacity(8)개, 넣은 순서대로 하나씩 발동.
//  가득 차면 새 예약은 무시된다(덮어쓰지 않는다). 토스트에 "대기 n/8" 이 뜬다.
//
//  동작: 예약은 **다음 행동 선택 시점**에 소비된다(TwentyThreeBoss.SelectAttackSlot). 진행 중 공격은 끊지 않는다.
//        페이즈 시퀀스(차징)가 대기 중이면 그쪽이 먼저다. 전역 간격·쿨다운·가중치는 무시하고 거리창만 지킨다 —
//        근접 패턴은 사거리 밖이면 예약을 든 채 추격하다 붙으면 나간다.
//
//  🔴 **호스트 창에서만** 동작한다 — 보스 FSM 은 서버에서만 돈다. 클라 창에서 누르면 토스트로 알린다.
//  ⚠️ DevBossEntranceWarp 와 같은 부류: 씬에 배치하지 않고 런타임에 스스로 생긴다. 릴리스 빌드에는 없다
//     (빌드에서 쓰려면 Development Build). 로그는 빌드에서도 남도록 Debug.Log.
// ----------------------------------------------------------------------------
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public sealed class DevBossAttackHotkeys : MonoBehaviour
{
    const float ToastSeconds = 2.5f;

    bool _nextHookRight;
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
        if (kb == null) return;
        bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
        if (kb.f3Key.wasPressedThisFrame) { if (shift) ClearAll(); else ReserveHook(); }
        else if (kb.f4Key.wasPressedThisFrame) Reserve(BossAttackId.Upper, "F4");
        else if (kb.f6Key.wasPressedThisFrame) Reserve(BossAttackId.Jump, "F6");
        else if (kb.f9Key.wasPressedThisFrame) Reserve(BossAttackId.Grab, "F9");
        else if (kb.f11Key.wasPressedThisFrame) Reserve(BossAttackId.Dash, "F11");
#else
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (Input.GetKeyDown(KeyCode.F3)) { if (shift) ClearAll(); else ReserveHook(); }
        else if (Input.GetKeyDown(KeyCode.F4)) Reserve(BossAttackId.Upper, "F4");
        else if (Input.GetKeyDown(KeyCode.F6)) Reserve(BossAttackId.Jump, "F6");
        else if (Input.GetKeyDown(KeyCode.F9)) Reserve(BossAttackId.Grab, "F9");
        else if (Input.GetKeyDown(KeyCode.F11)) Reserve(BossAttackId.Dash, "F11");
#endif
    }

    void ReserveHook()
    {
        BossAttackId id = _nextHookRight ? BossAttackId.RightHook : BossAttackId.LeftHook;
        if (Reserve(id, "F3")) _nextHookRight = !_nextHookRight;
    }

    bool Reserve(BossAttackId id, string key)
    {
        TwentyThreeBoss boss = ResolveBoss(key);
        if (boss == null) return false;

        int cap = TwentyThreeBoss.DevReservationCapacity;
        if (!boss.DevReserveNextAttack(id))
        {
            Toast($"{key}: 예약 가득 참({cap}/{cap}) — {id} 무시. Shift+F3 으로 비우기");
            return false;
        }
        Toast($"{key}: 예약 {id} · 대기 {boss.DevReservationCount}/{cap}");
        return true;
    }

    void ClearAll()
    {
        TwentyThreeBoss boss = ResolveBoss("Shift+F3");
        if (boss == null) return;
        boss.DevClearReservations();
        Toast($"Shift+F3: 예약 비움 (0/{TwentyThreeBoss.DevReservationCapacity})");
    }

    TwentyThreeBoss ResolveBoss(string key)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) { Toast($"{key}: 네트워크가 아직 안 떴다"); return null; }
        if (!nm.IsServer) { Toast($"{key}: 호스트 창에서만 예약할 수 있다(보스는 서버에서만 돈다)"); return null; }

        TwentyThreeBoss boss = FindAnyObjectByType<TwentyThreeBoss>();
        if (boss == null || !boss.IsSpawned) { Toast($"{key}: 23호가 아직 없다"); return null; }
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
