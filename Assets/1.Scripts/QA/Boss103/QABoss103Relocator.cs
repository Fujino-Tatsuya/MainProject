#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 할 일 103 C7d — QA 끼임 재배치. 봇(오너)이 <see cref="QABoss103StuckEscalation"/> 20초 단계에서 <see cref="Request"/> →
/// [호스트] 보스와 같은 NavMesh 섬의 보스 둘레 링 지점(<see cref="QABossStart88.TryFindBossRingPoints"/>) 중 요청자에게 가장 가까운 곳으로
/// <see cref="PlayerFallRecovery.RelocateServer"/>. 판(Combat ~ 판정) 동안만 받는다. 게임 코드는 바꾸지 않는다.
/// <para>클라 → 호스트 = NGO 이름 메시지(<see cref="MessageName"/>, 빈 본문 — 보낸 사람 = 요청자). 호스트 봇은 메시지 없이 바로 부른다.</para>
/// 로그: 호스트 <c>[QA103] event=qa_relocate</c>(1회 1줄), 봇 <c>event=qa_relocate_request</c>. 성공 수 = run_chars <c>qa_relocates</c>.
/// </summary>
public static class QABoss103Relocator
{
    private const string Tag = QABoss103Plan.Tag;
    public const string MessageName = "QA103Relocate";
    private const int RingCount = 3;

    private static readonly List<Vector3> Points = new List<Vector3>();
    private static IReadOnlyDictionary<ulong, QABoss103Character> _characters;
    private static QABoss103RunStats _stats;
    private static NetworkManager _registeredOn;

    public static int Relocates { get; private set; }
    public static int Failures { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _characters = null;
        _stats = null;
        _registeredOn = null;
        Relocates = Failures = 0;
    }

    /// <summary>[호스트] Combat 진입 — 요청을 받기 시작한다.</summary>
    public static void Begin(IReadOnlyDictionary<ulong, QABoss103Character> characters, QABoss103RunStats stats)
    {
        Stop();
        _characters = characters;
        _stats = stats;
        Relocates = Failures = 0;
        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && nm.IsServer && nm.CustomMessagingManager != null)
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, OnMessage);
            _registeredOn = nm;
        }
        Debug.Log($"{Tag} event=qa_relocate_on registered={(_registeredOn != null ? "true" : "false")}");
    }

    /// <summary>[호스트] 판정 순간 — 더 받지 않는다.</summary>
    public static void Stop()
    {
        if (_registeredOn != null)
        {
            if (_registeredOn.CustomMessagingManager != null)
                _registeredOn.CustomMessagingManager.UnregisterNamedMessageHandler(MessageName);
            _registeredOn = null;
            Debug.Log($"{Tag} event=qa_relocate_off relocates={Relocates} failures={Failures}");
        }
        _characters = null;
        _stats = null;
    }

    /// <summary>[오너 봇] 재배치 요청. 보냈으면(또는 호스트에서 바로 처리했으면) true.</summary>
    public static bool Request()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
            return false;
        if (nm.IsServer)
        {
            Relocate(nm.LocalClientId);
            return true;
        }
        if (!nm.IsConnectedClient || nm.CustomMessagingManager == null)
            return false;
        using (var w = new FastBufferWriter(4, Unity.Collections.Allocator.Temp))
            nm.CustomMessagingManager.SendNamedMessage(MessageName, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        return true;
    }

    private static void OnMessage(ulong sender, FastBufferReader _) => Relocate(sender);

    private static void Relocate(ulong clientId)
    {
        if (_characters == null)
        {
            Debug.Log($"{Tag} event=qa_relocate client={clientId} ok=false reason=not_running");
            return;
        }
        bool known = _characters.TryGetValue(clientId, out QABoss103Character ch);
        string character = known ? ch.ToString() : "na";
        NetworkManager nm = NetworkManager.Singleton;
        NetworkObject po = nm != null && nm.ConnectedClients.TryGetValue(clientId, out NetworkClient c) ? c.PlayerObject : null;
        PlayerFallRecovery mover = po != null ? po.GetComponent<PlayerFallRecovery>() : null;
        PlayerLifeCycleController life = po != null ? po.GetComponent<PlayerLifeCycleController>() : null;
        TwentyThreeBoss boss = Object.FindAnyObjectByType<TwentyThreeBoss>();
        string reason = mover == null ? "no_player"
            : life != null && life.State != PlayerLifeState.Alive ? "life_" + life.State
            : boss == null || boss.CurrentHealth <= 0 ? "no_boss"
            : null;
        string detail = "";
        Vector3 from = po != null ? po.transform.position : Vector3.zero;
        Vector3 to = from;
        if (reason == null)
        {
            QABossStart88.TryFindBossRingPoints(boss, RingCount, Points, out detail);
            if (Points.Count == 0)
                reason = "no_ring_point";
            else
            {
                to = Points[0];
                float best = QAPartyPlacement88.HorizontalDistance(from, to);
                for (int i = 1; i < Points.Count; i++)
                {
                    float d = QAPartyPlacement88.HorizontalDistance(from, Points[i]);
                    if (d < best)
                    {
                        best = d;
                        to = Points[i];
                    }
                }
                if (!mover.RelocateServer(to))
                    reason = "relocate_rejected";
            }
        }

        bool ok = reason == null;
        if (ok)
        {
            Relocates++;
            if (known)
                _stats?.QaRelocate((int)ch);
        }
        else
            Failures++;
        Vector3 bossPos = boss != null ? boss.transform.position : Vector3.zero;
        Debug.Log($"{Tag} event=qa_relocate character={character} client={clientId} ok={(ok ? "true" : "false")}" +
                  (ok ? "" : $" reason={reason}") +
                  $" from={QABossStart88.Fmt(from)} to={QABossStart88.Fmt(to)} movedM={QAPartyPlacement88.HorizontalDistance(from, to):F1}" +
                  $" bossDistM={(boss != null ? QAPartyPlacement88.HorizontalDistance(to, bossPos).ToString("F1") : "na")}" +
                  (detail.Length > 0 ? $" detail={QABoss103Plan.Token(detail)}" : ""));
    }
}
#endif
