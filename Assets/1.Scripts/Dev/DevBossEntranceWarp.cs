// ----------------------------------------------------------------------------
//  DevBossEntranceWarp.cs — F5 로 보스방 진입 패드까지 순간이동 (개발용)
//
//  목적: 보스 검증마다 스폰 지점에서 보스방까지 걸어가는 시간을 없앤다(팀장 요청 2026-09-03).
//
//  🔴 **도착지는 보스방 안이 아니라 진입 패드다.** 패드에 서면 기존 흐름이 그대로 돈다 —
//     점유 → 3초 카운트다운 → 전원 산개 텔레포트 → 등장 연출 → 전투. 그 흐름 자체가 검증
//     대상이므로 건너뛰지 않는다. 걸어가는 구간만 없앤다.
//
//  ⚠️ 이 컴포넌트는 결과물이 아니라 도구다. `#if` 로 감싸 릴리스 빌드에서는 클래스가 아예 없다
//     (ProfilerHUD·LookToggle 과 같은 부류). 다만 **씬에 배치하지 않는다** — 런타임에 스스로
//     생기므로 씬에 missing script 가 남을 일도 없고, 어떤 씬에서 Play 해도 붙는다.
//
//  ⚠️ 빌드에서 쓰려면 Build Settings 의 **Development Build 를 켜야 한다**(DEVELOPMENT_BUILD).
//     끄고 뽑으면 이 클래스가 컴파일되지 않아 F5 가 아무 일도 하지 않는다.
//
//  🔴 로그는 `Edit.Log` 가 아니라 `Debug.Log` 다. `Edit.*` 는 [Conditional("UNITY_EDITOR")] 라
//     빌드에서 통째로 사라진다 — 빌드로 검증하는 도구가 빌드에서 말을 못 하면 안 된다.
// ----------------------------------------------------------------------------
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;   // 신 Input System (이 프로젝트: Active Input Handling = New)
#endif

[DisallowMultipleComponent]
public sealed class DevBossEntranceWarp : MonoBehaviour
{
    // 이미 쓰이는 키 — F1·F2(HitVFXDebugHUD) · F8(ProfilerHUD) · F9(LookToggle) · F10(디버그 부활)
    //                M(맵 오버뷰) · [ ](카메라 타겟 전환). F5 가 비어 있어 쓴다.
#if ENABLE_INPUT_SYSTEM
    const Key WarpKey = Key.F5;
#else
    const KeyCode WarpKey = KeyCode.F5;
#endif

    // 같은 지점에 겹쳐 놓으면 리지드바디 디페네트레이션이 서로를 밀어낸다 — 클라별로 조금 흩는다.
    // 패드 기본 크기가 6×6m 이라 이 반경은 항상 패드 안이다(반경 1.2 < 3).
    const float ScatterRadius = 1.2f;

    // 바닥을 찾은 뒤 살짝 띄운다 — 바닥면에 정확히 놓으면 콜라이더가 파고든 상태로 시작한다.
    const float GroundClearance = 0.1f;

    const float ToastSeconds = 2.5f;

    string _toast;
    float _toastUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        // 씬 배치를 전제하지 않는다 — 배치를 전제한 기능이 배치가 없어 조용히 꺼지는 사고를 막는다.
        var go = new GameObject("[Dev] BossEntranceWarp");
        go.AddComponent<DevBossEntranceWarp>();
        DontDestroyOnLoad(go);
    }

    void Update()
    {
        if (!WarpKeyPressed()) return;
        Warp();
    }

    static bool WarpKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        // Keyboard.current 는 키보드가 없거나 아직 초기화되지 않으면 null 이다.
        Keyboard kb = Keyboard.current;
        return kb != null && kb[WarpKey].wasPressedThisFrame;
#else
        return Input.GetKeyDown(WarpKey);
#endif
    }

    void Warp()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
        {
            Toast("F5: 네트워크가 아직 안 떴다 — 워프 없음");
            return;
        }

        NetworkObject player = nm.LocalClient?.PlayerObject;
        if (player == null)
        {
            Toast("F5: 로컬 플레이어가 아직 없다 — 워프 없음");
            return;
        }

        // 🔴 연출 구간에는 옮기지 않는다. 그 구간은 서버가 참가자를 잠그고 도착 ACK 를 기다리는
        //    중이라, 여기서 위치를 흔들면 도착 판정·잠금 해제가 어긋난다. 전투 중·대기 중은 허용.
        BossEncounterPhase phase = BossEncounterDirector.Instance != null
            ? BossEncounterDirector.Instance.Phase
            : BossEncounterPhase.Idle;
        if (phase != BossEncounterPhase.Idle &&
            phase != BossEncounterPhase.Combat &&
            phase != BossEncounterPhase.FailedSafe)
        {
            Toast($"F5: 등장 연출 중({phase})에는 워프하지 않는다");
            return;
        }

        // 카운트다운 중에도 막는다 — 패드를 떠났다 돌아오는 것으로 읽혀 카운트다운이 리셋된다.
        if (BossTeleportManager.Instance != null && BossTeleportManager.Instance.IsCountdownActive)
        {
            Toast("F5: 이미 카운트다운 중이다");
            return;
        }

        if (!TryFindEntrance(out Vector3 destination))
        {
            Toast("F5: 이 씬에는 보스방 진입 패드가 없다");
            return;
        }

        destination += ScatterOffset(player.OwnerClientId);

        // 바닥은 절대 Y 로 박지 않는다 — 보스룸 보행면은 Y 0.50 이고 테스트 씬은 0 이다(GroundProbe 규약).
        if (GroundProbe.TryFindGround(destination, 0, out RaycastHit ground, out _))
            destination.y = ground.point.y + GroundClearance;

        ApplyWarp(player, destination);

        Debug.Log($"[Dev] F5 워프 — 보스방 진입 패드 {destination} (clientId={player.OwnerClientId})", this);
        Toast("F5: 보스방 진입 패드로 이동");
    }

    /// <summary>
    /// 진입 패드 위치. <see cref="BossEnterZoneVisual"/> 에서 얻는다 —
    /// 🔴 트리거(BossEnterTrigger)는 <b>서버에만</b> 붙으므로 클라에서는 그걸로 찾을 수 없다.
    ///    범위 표시는 모든 피어에 붙는다(MapContentSpawner.AttachBossEnterZone).
    /// </summary>
    static bool TryFindEntrance(out Vector3 position)
    {
        BossEnterZoneVisual zone = FindAnyObjectByType<BossEnterZoneVisual>();
        if (zone == null)
        {
            position = default;
            return false;
        }

        position = zone.PadCenterWorld;
        return true;
    }

    // 클라별 고정 산개 — 같은 클라는 늘 같은 자리라 재현이 쉽다(랜덤을 안 쓰는 이유).
    static Vector3 ScatterOffset(ulong clientId)
    {
        float angle = clientId * 137f * Mathf.Deg2Rad;   // 137° = 겹치지 않게 흩어지는 각
        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ScatterRadius;
    }

    /// <summary>
    /// 오너 권한 이동 구성에서의 순간이동 3단 — <see cref="BossTeleportManager"/> 의
    /// <c>TeleportOwnerClientRpc</c> 와 <b>같은 순서</b>다. 하나라도 빠지면 되돌려진다:
    /// 리지드바디 속도를 안 지우면 관성이 남고, NetworkTransform 을 안 부르면 보간이 옛 위치로 끌어간다.
    /// </summary>
    static void ApplyWarp(NetworkObject player, Vector3 destination)
    {
        Quaternion rotation = player.transform.rotation;

        if (player.TryGetComponent(out Rigidbody rb))
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = destination;
            rb.rotation = rotation;
        }

        player.transform.SetPositionAndRotation(destination, rotation);

        // 오너만 부를 수 있다 — 오너가 아닌 쪽에서 부르면 NetworkTransform 이 예외를 던진다
        // (BossTeleportManager 가 서버에서 같은 이유로 오너 여부를 먼저 본다).
        if (player.IsOwner && player.TryGetComponent(out NetworkTransform netTransform))
            netTransform.Teleport(destination, rotation, player.transform.localScale);
    }

    void Toast(string message)
    {
        _toast = message;
        _toastUntil = Time.unscaledTime + ToastSeconds;
    }

    void OnGUI()
    {
        if (string.IsNullOrEmpty(_toast) || Time.unscaledTime > _toastUntil) return;

        // 빌드에서는 콘솔이 안 보인다 — 도구가 무엇을 했는지 화면으로 말해야 쓸 수 있다.
        var style = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
        style.normal.textColor = Color.yellow;
        GUI.Label(new Rect(12f, 12f, 640f, 28f), _toast, style);
    }
}
#endif
