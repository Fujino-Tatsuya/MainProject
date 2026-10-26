// ----------------------------------------------------------------------------
//  DevBossPointWarp.cs — F6 로 보스전 중 보스방의 지정 지점까지 순간이동 (개발용, 할 일 102)
//
//  목적: 보스 패턴·기믹을 특정 자리에서 반복 확인할 때 걸어가는 시간을 없앤다.
//        **누른 사람(로컬 플레이어)만** 옮긴다 — 호스트·클라 모두 쓴다.
//
//  🔴 지점은 컴포넌트가 아니라 **이름**으로 찾는다 — 보스방(bossroom.prefab)의 빈 GameObject
//     "[Dev] BossWarpPoint". 이 클래스는 DEV_HOTKEYS 없는 릴리스에서 빠지므로, 지점에 스크립트를 붙이면
//     그 빌드에서 missing script 가 남는다. 위치를 바꾸려면 그 오브젝트를 옮기면 된다(회전은 y축만 쓴다).
//
//  🔴 오너 권한 이동이라 ServerRpc 가 없다. 순서는 BossTeleportManager.TeleportOwnerClientRpc 와 같다 —
//     EndDash → ClearKnockbackVelocity → TeleportAuthoritative → RotateImmediately → NetworkTransform.Teleport.
//     (F5 의 Rigidbody 직접 쓰기는 kinematic 바디에서 속도가 안 지워지는 옛 방식이라 쓰지 않는다.)
//
//  🔴 보스전(Phase == Combat)에서만 옮긴다. 연출 구간은 서버가 도착 ACK 를 기다리고 참가자를 잠그는 중이다.
//     낙하 복구 중에도 옮기지 않는다 — 서버의 지연 복귀가 나중에 안전지점으로 되돌린다(취소는 서버 전용 API 뿐).
//
//  ⚠️ 씬에 배치하지 않는다 — RuntimeInitializeOnLoadMethod 로 스스로 생긴다(DevBossEntranceWarp 와 같은 부류).
//  🔴 로그는 `Debug.Log` 다. `Edit.*` 는 [Conditional("UNITY_EDITOR")] 라 빌드에서 사라진다.
// ----------------------------------------------------------------------------
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;   // 신 Input System (이 프로젝트: Active Input Handling = New)
#endif

/// <summary>F6 워프를 해도 되는가 — 판정 결과.</summary>
public enum DevBossPointWarpVerdict
{
    Warp,
    NetworkNotRunning,
    NoLocalPlayer,
    NotInBossCombat,
    FallRecovering,
    NoWarpPoint,
}

/// <summary>F6 워프 조건 판정(순수 함수 — EditMode 테스트 대상).</summary>
public static class DevBossPointWarpRules
{
    /// <param name="hasEncounter">씬에 BossEncounterDirector 가 있는가. 없으면 보스전이 아니다.</param>
    /// <param name="fallRecovering">로컬 플레이어가 낙하 중이거나 낙하 복구 연출 중인가.</param>
    public static DevBossPointWarpVerdict Evaluate(
        bool networkListening,
        bool hasLocalPlayer,
        bool hasEncounter,
        BossEncounterPhase phase,
        bool fallRecovering,
        bool hasWarpPoint)
    {
        if (!networkListening) return DevBossPointWarpVerdict.NetworkNotRunning;
        if (!hasLocalPlayer) return DevBossPointWarpVerdict.NoLocalPlayer;
        if (!hasEncounter || phase != BossEncounterPhase.Combat) return DevBossPointWarpVerdict.NotInBossCombat;
        if (fallRecovering) return DevBossPointWarpVerdict.FallRecovering;
        if (!hasWarpPoint) return DevBossPointWarpVerdict.NoWarpPoint;
        return DevBossPointWarpVerdict.Warp;
    }

    /// <summary>하지 않은 이유(토스트·로그 문구).</summary>
    public static string Describe(DevBossPointWarpVerdict verdict, BossEncounterPhase phase)
    {
        switch (verdict)
        {
            case DevBossPointWarpVerdict.Warp: return "보스방 지정 지점으로 이동";
            case DevBossPointWarpVerdict.NetworkNotRunning: return "네트워크가 아직 안 떴다 — 이동 없음";
            case DevBossPointWarpVerdict.NoLocalPlayer: return "로컬 플레이어가 아직 없다 — 이동 없음";
            case DevBossPointWarpVerdict.NotInBossCombat: return $"보스 전투 중이 아니다({phase}) — 이동 없음";
            case DevBossPointWarpVerdict.FallRecovering: return "낙하 복구 중이다 — 끝난 뒤 다시 눌러라";
            case DevBossPointWarpVerdict.NoWarpPoint: return $"지점 \"{DevBossPointWarp.PointName}\" 이 씬에 없다 — 이동 없음";
            default: return verdict.ToString();
        }
    }
}

[DisallowMultipleComponent]
public sealed class DevBossPointWarp : MonoBehaviour
{
    /// <summary>보스방에 놓은 빈 GameObject 이름. 컴포넌트를 붙이지 않는다(파일 머리 주석).</summary>
    public const string PointName = "[Dev] BossWarpPoint";

    // F6 은 2026-10-20 23호 단타 예약을 걷어 내며 비었다(Docs/tech/debug-hotkeys.md).
#if ENABLE_INPUT_SYSTEM
    const Key WarpKey = Key.F6;
#else
    const KeyCode WarpKey = KeyCode.F6;
#endif

    // 바닥을 찾은 뒤 살짝 띄운다 — 바닥면에 정확히 놓으면 콜라이더가 파고든 상태로 시작한다.
    const float GroundClearance = 0.1f;

    const float ToastSeconds = 2.5f;

    string _toast;
    float _toastUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        // 도메인 리로드를 꺼 두면 두 번째 Play 에서 또 불린다. 이미 있으면 그대로 둔다.
        if (FindAnyObjectByType<DevBossPointWarp>() != null) return;

        var go = new GameObject("[Dev] BossPointWarp");
        go.AddComponent<DevBossPointWarp>();
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
        Keyboard kb = Keyboard.current;
        return kb != null && kb[WarpKey].wasPressedThisFrame;
#else
        return Input.GetKeyDown(WarpKey);
#endif
    }

    void Warp()
    {
        NetworkManager nm = NetworkManager.Singleton;
        bool listening = nm != null && nm.IsListening;
        NetworkObject player = listening ? nm.LocalClient?.PlayerObject : null;

        BossEncounterDirector director = BossEncounterDirector.Instance;
        BossEncounterPhase phase = director != null ? director.Phase : BossEncounterPhase.Idle;

        // 지점 탐색·낙하 판정은 앞 조건이 다 맞을 때만 한다 — 판정 순서는 Rules 가 쥔다.
        bool fallRecovering = player != null && IsFallRecovering(player.transform);
        Transform point = player != null && director != null && phase == BossEncounterPhase.Combat
            ? FindPoint()
            : null;

        DevBossPointWarpVerdict verdict = DevBossPointWarpRules.Evaluate(
            listening, player != null, director != null, phase, fallRecovering, point != null);
        string message = DevBossPointWarpRules.Describe(verdict, phase);

        if (verdict != DevBossPointWarpVerdict.Warp)
        {
            Debug.LogWarning($"[Dev] F6 — {message}", this);
            Toast($"F6: {message}");
            return;
        }

        Vector3 destination = point.position;
        // 바닥은 절대 Y 로 박지 않는다 — 지점 Y 를 그대로 쓰면 바닥에 파고들 수 있다(GroundProbe 규약).
        if (GroundProbe.TryFindGround(destination, 0, out RaycastHit ground, out _))
            destination.y = ground.point.y + GroundClearance;

        Quaternion rotation = Quaternion.Euler(0f, point.eulerAngles.y, 0f);
        ApplyWarp(player, destination, rotation);

        Debug.Log($"[Dev] F6 — {message} {destination} (clientId={player.OwnerClientId})", this);
        Toast($"F6: {message}");
    }

    /// <summary>
    /// 로컬 플레이어가 낙하 중이거나 낙하 복구 연출 중인가.
    /// 🔴 PlayerFallRecovery 의 오너 코루틴은 비공개라, 그 코루틴이 켜고 끄는 낙하 카메라(<see cref="CameraTargetSwitcher.IsInFallView"/>)로
    ///    본다 — 서버 지연 복귀(fallReturnDelay 뒤 오너에게 TeleportOwnerRpc)는 이 카메라가 켜진 동안 도착한다.
    ///    복구 RPC 가 도착하기 전(경계 아래로 막 떨어진 순간)은 경계 높이로 막는다.
    /// </summary>
    static bool IsFallRecovering(Transform player)
    {
        if (CameraTargetSwitcher.Active != null && CameraTargetSwitcher.Active.IsInFallView)
            return true;

        FallBoundarySettings boundary = FallBoundarySettings.Instance;
        return boundary != null && player.position.y < boundary.FallThresholdY;
    }

    /// <summary>이름으로 찾는다(비활성 제외). 보스방 프리팹이 씬에 하나라 첫 번째를 쓴다.</summary>
    static Transform FindPoint()
    {
        GameObject go = GameObject.Find(PointName);
        return go != null ? go.transform : null;
    }

    /// <summary>
    /// 오너 권한 순간이동 — <see cref="BossTeleportManager"/> 의 <c>TeleportOwnerClientRpc</c> 와 <b>같은 API·같은 순서</b>다.
    /// 대시는 상태라 상태머신으로 끊고, Motor 기준점까지 옮기고, NetworkTransform 보간을 끊는다.
    /// </summary>
    static void ApplyWarp(NetworkObject player, Vector3 destination, Quaternion rotation)
    {
        if (player.TryGetComponent(out PlayerStateController stateController))
            stateController.EndDash();

        var motor = player.GetComponent<PlayerMotor>();
        if (motor != null)
        {
            motor.ClearKnockbackVelocity();
            motor.TeleportAuthoritative(destination);
            if (player.TryGetComponent(out PlayerMovement movement))
                movement.RotateImmediately(rotation * Vector3.forward);
        }
        else
        {
            if (player.TryGetComponent(out Rigidbody rb))
            {
                rb.position = destination;
                rb.rotation = rotation;
            }
            player.transform.SetPositionAndRotation(destination, rotation);
        }

        // 로컬 플레이어 = 오너다 — 오너만 NetworkTransform.Teleport 를 부를 수 있다.
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

        // 빌드에서는 콘솔이 안 보인다. F5 토스트(12px)와 겹치지 않게 한 줄 아래에 그린다.
        var style = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
        style.normal.textColor = Color.yellow;
        GUI.Label(new Rect(12f, 44f, 640f, 28f), _toast, style);
    }
}
#endif
