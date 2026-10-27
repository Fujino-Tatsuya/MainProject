using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 48 걷는 로비 조작 도구(QA 전용) — 게임 코드 무수정, public 진입점·Registry 만 읽는다.
/// 준비는 서버가 패드 거리를 다시 보므로(LobbySession.ToggleReadyServerRpc) 반드시 패드까지 걸어간 뒤 F 를 누른다.
/// </summary>
public static class QALobbyFlow
{
    public const string LobbyScene = "3.LobbyScene";
    public const string MapScene = "4.MapScene";
    public const string WaitScene = "6.DemoConnectScene";

    /// <summary>직전 <see cref="PressInteract"/> 이 키 입력으로 반영됐는가(false = 직접 RPC 로 대체).</summary>
    public static bool LastInteractViaKey { get; private set; }

    /// <summary>직전 <see cref="WalkTo"/> 이 도착했는가.</summary>
    public static bool LastWalkArrived { get; private set; }

    public static bool InLobby =>
        QAUtil.ActiveSceneName() == LobbyScene && LobbySession.Active != null && LobbySession.Active.IsSpawned;

    public static bool InMapWithPlayer =>
        QAUtil.ActiveSceneName() == MapScene && Player.LocalPlayer != null && Player.LocalPlayer.IsSpawned;

    public static bool HasLocalLobbyPlayer =>
        InLobby && Player.LocalPlayer != null && Player.LocalPlayer.IsSpawned;

    public static bool IsConnected
    {
        get
        {
            NetworkManager nm = NetworkManager.Singleton;
            return nm != null && nm.IsListening && (nm.IsServer || nm.IsConnectedClient);
        }
    }

    public static bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    public static bool IsLocalReady()
    {
        LobbySession session = LobbySession.Active;
        NetworkManager nm = NetworkManager.Singleton;
        return session != null && nm != null && session.TryGetEntry(nm.LocalClientId, out LobbyPlayerEntry e) && e.Ready;
    }

    public static bool TryGetLocalEntry(out LobbyPlayerEntry entry)
    {
        entry = default;
        LobbySession session = LobbySession.Active;
        NetworkManager nm = NetworkManager.Singleton;
        return session != null && nm != null && session.TryGetEntry(nm.LocalClientId, out entry);
    }

    public static int ReadyCount(out int total)
    {
        LobbySession session = LobbySession.Active;
        total = session != null ? session.EntryCount : 0;
        int ready = 0;
        for (int i = 0; i < total; i++)
            if (session.GetEntry(i).Ready)
                ready++;
        return ready;
    }

    public static List<Player> SpawnedPlayers()
    {
        var list = new List<Player>();
        foreach (Player p in Object.FindObjectsByType<Player>(FindObjectsSortMode.None))
            if (p != null && p.IsSpawned)
                list.Add(p);
        list.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
        return list;
    }

    public static string CleanName(Object o)
    {
        return o == null ? "(없음)" : o.name.Replace("(Clone)", string.Empty).Trim();
    }

    public static LobbyInteractable FindInteractable(LobbyInteractableKind kind, Vector3 from)
    {
        LobbyInteractable best = null;
        float bestDist = float.MaxValue;
        IReadOnlyList<LobbyInteractable> registry = LobbyInteractable.Registry;
        for (int i = 0; i < registry.Count; i++)
        {
            LobbyInteractable it = registry[i];
            if (it == null || it.Kind != kind)
                continue;
            float d = NearestInteractable.PlanarDistance(from, it.Position);
            if (d < bestDist)
            {
                bestDist = d;
                best = it;
            }
        }
        return best;
    }

    /// <summary>로비 스폰 지점(LobbyPlayerSpawner.spawnPoints, 직렬화 private) — QA 판정용 리플렉션.</summary>
    public static Transform[] LobbySpawnPoints()
    {
        LobbyPlayerSpawner spawner = Object.FindFirstObjectByType<LobbyPlayerSpawner>();
        if (spawner == null)
            return null;
        FieldInfo field = typeof(LobbyPlayerSpawner).GetField("spawnPoints", BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(spawner) as Transform[];
    }

    /// <summary>가장 가까운 스폰 지점 번호(-1 = 없음)와 평면 거리.</summary>
    public static int NearestSpawnIndex(Vector3 pos, out float distance)
    {
        distance = float.MaxValue;
        Transform[] points = LobbySpawnPoints();
        if (points == null)
            return -1;
        int best = -1;
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] == null)
                continue;
            float d = NearestInteractable.PlanarDistance(pos, points[i].position);
            if (d < distance)
            {
                distance = d;
                best = i;
            }
        }
        return best;
    }

    /// <summary>평면으로 목표까지 걷는다(모터 변위 경로). 도착 = 평면 거리 ≤ stopDistance.</summary>
    public static IEnumerator WalkTo(QAInputController input, Vector3 target, float stopDistance, float timeout)
    {
        LastWalkArrived = false;
        QABlackboard.ScriptedControl = true;
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            Player p = Player.LocalPlayer;
            if (p == null)
                break;
            input.SetTarget(p); // 교체·재스폰 뒤에도 새 오브젝트를 잡는다
            Vector3 to = target - p.transform.position;
            to.y = 0f;
            if (to.magnitude <= stopDistance)
            {
                LastWalkArrived = true;
                break;
            }
            input.SetMove(to);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        input.SetMove(Vector3.zero);
    }

    /// <summary>F 를 눌러 changed 가 참이 되는지 본다. 키가 반영되지 않으면 fallback(같은 서버 RPC)을 부른다.</summary>
    public static IEnumerator PressInteract(QAInputController input, System.Func<bool> changed, System.Action fallback)
    {
        LastInteractViaKey = false;
        yield return input.PressKey(Key.F);
        float t = 0f;
        while (t < 1.5f)
        {
            if (changed())
            {
                LastInteractViaKey = true;
                yield break;
            }
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        Debug.LogWarning("[QA] F 키 주입이 반영되지 않았다(창 포커스·배경 입력 설정) — 같은 서버 RPC 를 직접 부른다.");
        fallback?.Invoke();
        t = 0f;
        while (t < 2f && !changed())
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    /// <summary>준비 패드까지 걸어가 F → 내 준비가 켜질 때까지. 결과는 <see cref="IsLocalReady"/>·LastInteractViaKey.</summary>
    public static IEnumerator ReadyUp(QAInputController input)
    {
        if (!HasLocalLobbyPlayer || IsLocalReady())
            yield break;

        LobbyInteractable pad = FindInteractable(LobbyInteractableKind.Ready, Player.LocalPlayer.transform.position);
        if (pad == null)
        {
            Debug.LogWarning("[QA] 준비 패드(LobbyInteractable Ready)를 찾지 못했다.");
            yield break;
        }

        // MPPM 셋이 같은 중심으로 가면 서로 막힌다 — clientId 마다 패드 둘레 다른 자리(반경 절반, 120° 간격).
        ulong id = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
        float angle = (id % 3) * 120f * Mathf.Deg2Rad;
        Vector3 spot = pad.Position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (pad.Radius * 0.5f);
        yield return WalkTo(input, spot, 0.3f, 20f);
        yield return PressInteract(input, IsLocalReady, () => LobbySession.Active?.RequestToggleReady());
        QABlackboard.ScriptedControl = false;
    }

    /// <summary>
    /// 카메라 추적 확인: 열린 방향으로 1.2초 걷고 카메라 이동량이 플레이어 이동량의 절반 이상인지 본다.
    /// 막힌 방향이면 다음 축으로 바꾼다. 결과 문자열은 근거로 쓴다.
    /// </summary>
    public static IEnumerator CameraFollowProbe(QAInputController input, QACameraProbeResult result)
    {
        result.Done = false;
        result.Ok = false;
        result.Evidence = "플레이어 없음";
        Vector3[] dirs = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
        QABlackboard.ScriptedControl = true;

        foreach (Vector3 dir in dirs)
        {
            Player p = Player.LocalPlayer;
            Camera cam = Camera.main;
            if (p == null)
                break;
            if (cam == null)
            {
                result.Evidence = "Camera.main 없음";
                break;
            }

            input.SetTarget(p);
            Vector3 p0 = p.transform.position;
            Vector3 c0 = cam.transform.position;
            float t = 0f;
            while (t < 1.2f && p != null)
            {
                input.SetMove(dir);
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            input.SetMove(Vector3.zero);
            // 카메라 감쇠가 따라올 시간.
            yield return new WaitForSecondsRealtime(0.6f);
            if (p == null || cam == null)
                break;

            Vector3 dp = p.transform.position - p0;
            Vector3 dc = cam.transform.position - c0;
            dp.y = 0f;
            dc.y = 0f;
            result.Evidence = $"dir={dir} 플레이어 {dp.magnitude:F2}m · 카메라 {dc.magnitude:F2}m · cam={cam.name}";
            if (dp.magnitude < 0.8f)
                continue; // 막힘 — 다른 방향

            result.Ok = dc.magnitude >= dp.magnitude * 0.5f;
            break;
        }

        QABlackboard.ScriptedControl = false;
        result.Done = true;
    }

    /// <summary>활성 UI 글자 중 needle 을 포함하는 것(TMP·uGUI).</summary>
    public static void CollectTexts(string needle, HashSet<string> into)
    {
        foreach (TMP_Text t in Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
            if (t != null && t.isActiveAndEnabled && !string.IsNullOrEmpty(t.text) && t.text.Contains(needle))
                into.Add(t.text.Trim());
        foreach (UnityEngine.UI.Text t in Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None))
            if (t != null && t.isActiveAndEnabled && !string.IsNullOrEmpty(t.text) && t.text.Contains(needle))
                into.Add(t.text.Trim());
    }
}

public sealed class QACameraProbeResult
{
    public bool Done;
    public bool Ok;
    public string Evidence;
}
