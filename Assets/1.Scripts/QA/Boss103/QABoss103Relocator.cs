#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>QA 재배치 사유 — 판 집계 열이 갈린다(<c>qa_relocates</c> · <c>qa_wall_escapes</c>).</summary>
public enum QABoss103RelocateReason : byte
{
    /// <summary>C7d — 20초 진척 없음(끼임 단계) → 요청자에게 가장 가까운 링 지점.</summary>
    Stuck = 0,
    /// <summary>할 일 119 — 벽 속·보스방 경계 밖 → 링 지점 중 무작위.</summary>
    Wall = 1,
}

/// <summary>
/// 할 일 103 C7d · 119 — QA 재배치. 봇(오너)이 <see cref="Request"/> →
/// [호스트] 보스와 같은 NavMesh 섬의 보스 둘레 링 지점(<see cref="QABossStart88.TryFindBossRingPoints"/>)으로 <see cref="PlayerFallRecovery.RelocateServer"/>.
/// 사유 <see cref="QABoss103RelocateReason.Stuck"/>(20초 끼임) = 요청자에게 가장 가까운 지점, <see cref="QABoss103RelocateReason.Wall"/>(벽 속·경계 밖) = 유효 지점 중 무작위(판 시드).
/// 판(Combat ~ 판정) 동안만 받는다. 게임 코드는 바꾸지 않는다.
/// <para>클라 → 호스트 = NGO 이름 메시지(<see cref="MessageName"/>, 본문 1바이트 = 사유 — 보낸 사람 = 요청자). 호스트 봇은 메시지 없이 바로 부른다.</para>
/// 로그: 호스트 <c>[QA103] event=qa_relocate reason=stuck|wall</c>(1회 1줄, 할 일 138-2 순간이동 직전 스크린샷 <c>shot=&lt;파일명&gt;|cooldown</c> — <see cref="QABoss103StuckShot"/>,
/// 할 일 140 옮기기 전 계측 <c>state</c>·<c>what</c>·<c>ground</c>·<c>grab</c> — <see cref="QABoss103StuckProbe"/>), 봇 <c>event=qa_relocate_request</c>·<c>event=qa_wall_escape_request</c>.
/// 성공 수 = run_chars <c>qa_relocates</c>(끼임) · <c>qa_wall_escapes</c>(벽).
/// </summary>
public static class QABoss103Relocator
{
    private const string Tag = QABoss103Plan.Tag;
    public const string MessageName = "QA103Relocate";
    private const int RingCount = 3;
    /// <summary>벽 탈출 무작위 후보 수 — 링 후보 중 서로 떨어진 지점을 이만큼 골라 하나를 뽑는다.</summary>
    private const int WallRingCount = 8;

    private static readonly List<Vector3> Points = new List<Vector3>();
    private static IReadOnlyDictionary<ulong, QABoss103Character> _characters;
    private static QABoss103RunStats _stats;
    private static NetworkManager _registeredOn;
    private static System.Random _rng = new System.Random(103);
    private static QABoss103Config _cfg;
    /// <summary>할 일 138-2 — 플레이어별 마지막 재배치 스크린샷 시각(realtime).</summary>
    private static readonly Dictionary<ulong, double> LastShot = new Dictionary<ulong, double>();

    public static int Relocates { get; private set; }
    public static int WallEscapes { get; private set; }
    public static int Failures { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _characters = null;
        _stats = null;
        _registeredOn = null;
        _cfg = null;
        LastShot.Clear();
        Relocates = WallEscapes = Failures = 0;
    }

    /// <summary>[호스트] Combat 진입 — 요청을 받기 시작한다. 벽 탈출 무작위 지점 시드 = 판 시드, 재배치 스크린샷 = <paramref name="cfg"/> 출력 폴더.</summary>
    public static void Begin(IReadOnlyDictionary<ulong, QABoss103Character> characters, QABoss103RunStats stats, QABoss103Config cfg)
    {
        Stop();
        _characters = characters;
        _stats = stats;
        _cfg = cfg;
        LastShot.Clear();
        _rng = new System.Random(QABoss103Plan.Mix(cfg.RunSeed, 0x3A11, 0, 0));
        Relocates = WallEscapes = Failures = 0;
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
            Debug.Log($"{Tag} event=qa_relocate_off relocates={Relocates} wallEscapes={WallEscapes} failures={Failures}");
        }
        _characters = null;
        _stats = null;
    }

    /// <summary>[오너 봇] 재배치 요청. 보냈으면(또는 호스트에서 바로 처리했으면) true.</summary>
    public static bool Request(QABoss103RelocateReason reason = QABoss103RelocateReason.Stuck)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
            return false;
        if (nm.IsServer)
        {
            Relocate(nm.LocalClientId, reason);
            return true;
        }
        if (!nm.IsConnectedClient || nm.CustomMessagingManager == null)
            return false;
        using (var w = new FastBufferWriter(4, Unity.Collections.Allocator.Temp))
        {
            w.WriteValueSafe((byte)reason);
            nm.CustomMessagingManager.SendNamedMessage(MessageName, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        }
        return true;
    }

    private static void OnMessage(ulong sender, FastBufferReader reader)
    {
        byte raw = (byte)QABoss103RelocateReason.Stuck;
        if (reader.TryBeginRead(1))
            reader.ReadValue(out raw);   // 빈 본문(옛 클론) = 끼임
        Relocate(sender, raw == (byte)QABoss103RelocateReason.Wall ? QABoss103RelocateReason.Wall : QABoss103RelocateReason.Stuck);
    }

    private static string Token(QABoss103RelocateReason reason) => reason == QABoss103RelocateReason.Wall ? "wall" : "stuck";

    private static void Relocate(ulong clientId, QABoss103RelocateReason why)
    {
        if (_characters == null)
        {
            Debug.Log($"{Tag} event=qa_relocate reason={Token(why)} client={clientId} ok=false fail=not_running");
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
        string shot = null;
        Vector3 from = po != null ? po.transform.position : Vector3.zero;
        // 할 일 140 — 옮기기 전 상태(구속·겹친 콜라이더·발밑·직전 2초 잡기 전환). 읽기만.
        Player probed = po != null ? po.GetComponent<Player>() : null;
        string probe = QABoss103StuckProbe.Fields(probed, QABoss103StuckProbe.HostGrab(clientId, Time.realtimeSinceStartupAsDouble));
        Vector3 to = from;
        if (reason == null)
        {
            bool wall = why == QABoss103RelocateReason.Wall;
            QABossStart88.TryFindBossRingPoints(boss, wall ? WallRingCount : RingCount, Points, out detail);
            if (Points.Count == 0)
                reason = "no_ring_point";
            else
            {
                to = wall ? Points[_rng.Next(Points.Count)] : Nearest(from);
                shot = Shot(clientId, why, character);
                if (!mover.RelocateServer(to))
                    reason = "relocate_rejected";
            }
        }

        bool ok = reason == null;
        if (ok)
        {
            if (why == QABoss103RelocateReason.Wall)
            {
                WallEscapes++;
                if (known)
                    _stats?.QaWallEscape((int)ch);
            }
            else
            {
                Relocates++;
                if (known)
                    _stats?.QaRelocate((int)ch);
            }
        }
        else
            Failures++;
        Vector3 bossPos = boss != null ? boss.transform.position : Vector3.zero;
        Debug.Log($"{Tag} event=qa_relocate reason={Token(why)} character={character} client={clientId} ok={(ok ? "true" : "false")}" +
                  (ok ? "" : $" fail={reason}") + probe +
                  $" from={QABossStart88.Fmt(from)} to={QABossStart88.Fmt(to)} movedM={QAPartyPlacement88.HorizontalDistance(from, to):F1}" +
                  $" bossDistM={(boss != null ? QAPartyPlacement88.HorizontalDistance(to, bossPos).ToString("F1") : "na")} points={Points.Count}" +
                  (detail.Length > 0 ? $" detail={QABoss103Plan.Token(detail)}" : "") +
                  (shot != null ? $" shot={shot}" : ""));
    }

    /// <summary>
    /// 할 일 138-2 — 순간이동 직전 호스트 화면 1장(<see cref="QABoss103StuckShot"/>). 반환 = 파일 이름 · <c>cooldown</c> · null(출력 폴더 없음·실패).
    /// 캡처는 프레임 끝에 쓰인다 — 호스트 플레이어는 같은 틱에 옮겨져 이동 뒤 화면, 원격 플레이어는 오너 RPC 왕복 전이라 이동 전 화면이다.
    /// </summary>
    private static string Shot(ulong clientId, QABoss103RelocateReason why, string character)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (QABoss103StuckShot.InCooldown(now, LastShot.TryGetValue(clientId, out double last) ? last : (double?)null))
            return QABoss103StuckShot.CooldownToken;
        string name = QABoss103StuckShot.FileName(_cfg != null ? _cfg.Run : 0, _cfg != null ? _cfg.Retry : 0, System.DateTime.Now, Token(why),
            character != "na" ? character : $"client{clientId}");
        string path = QABoss103StuckShot.PathFor(_cfg != null ? _cfg.OutDir : null, name);
        if (path == null)
            return null;
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            ScreenCapture.CaptureScreenshot(path);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"{Tag} event=qa_relocate_shot ok=false error={QABoss103Plan.Token(e.GetType().Name + ": " + e.Message)}");
            return null;
        }
        LastShot[clientId] = now;
        return name;
    }

    private static Vector3 Nearest(Vector3 from)
    {
        Vector3 to = Points[0];
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
        return to;
    }
}
#endif
