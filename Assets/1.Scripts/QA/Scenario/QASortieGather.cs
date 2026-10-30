using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// 할 일 169 — 로비 출격 집결 걷기(QA 전용, 게임 코드 무수정). 103 의 출격(Sortie103)을 <see cref="QAScenarioContext"/> 없이 쓰게 옮긴 것 —
/// 필요한 건 <see cref="QAInputController"/> 뿐이라 10분 시나리오(<see cref="QASessionController"/>)·시나리오 러너가 같은 경로를 탄다.
/// 출격 집결 구역(진행기 sortieZone = Gather_Sortie)의 긴 축 일렬 자리(<see cref="QABoss103Plan.PadLineOffset"/>)로 걸어가 머문다
/// → 전원 박스 안 + 유지 시간(LobbyRules 5초) → 진행기가 메인 진입. 훈련 입구 구역은 지나가지 않는다(Lobby 단계에서 입구 확정 = 훈련 시작 → 출격 꺼짐).
/// 걷기·대기 동안 <see cref="QABlackboard.ScriptedControl"/> 을 켜 두어 AI 가 돌아다니거나 공격(Busy — 카운트다운 시작 거부)하지 않게 하고, 끝에 끈다.
/// </summary>
public static class QASortieGather
{
    /// <summary>출격 집결부터 맵 플레이어까지 상한(초) — 다른 인스턴스 걸음·5초 집결·로딩 포함.</summary>
    public const float DefaultMapTimeoutSeconds = 180f;

    /// <summary>구역 오브젝트가 스폰될 때까지 기다리는 상한(초).</summary>
    public const float ZoneSpawnTimeoutSeconds = 30f;

    /// <summary>스폰된 구역이 활성으로 동기화될 때까지 기다리는 상한(초). 넘으면 비활성(훈련 단계 등)으로 보고 실패.</summary>
    public const float ZoneActiveTimeoutSeconds = 10f;

    /// <summary>
    /// 자리·인원(순수 계산). 자리 = <paramref name="spawnedIds"/>(스폰된 플레이어 OwnerClientId) 중 나보다 작은 id 수,
    /// 인원 = 기대 인원·스폰 인원·자리+1 중 가장 큰 값. 내가 목록에 없으면 false(자리 0·인원은 같은 규칙).
    /// </summary>
    public static bool TrySlot(ulong localId, IReadOnlyList<ulong> spawnedIds, int expected, out int slot, out int count)
    {
        slot = spawnedIds != null ? QABoss103Plan.SlotOf(localId, spawnedIds) : -1;
        bool found = slot >= 0;
        if (!found)
            slot = 0;
        count = Mathf.Max(1, Mathf.Max(expected, Mathf.Max(spawnedIds != null ? spawnedIds.Count : 0, slot + 1)));
        return found;
    }

    /// <summary>지금 스폰된 플레이어(<see cref="QALobbyFlow.SpawnedPlayers"/>) 기준 내 자리·인원.</summary>
    public static bool TryLocalSlot(int expected, out int slot, out int count)
    {
        var ids = new List<ulong>();
        foreach (Player p in QALobbyFlow.SpawnedPlayers())
            ids.Add(p.OwnerClientId);
        ulong me = Unity.Netcode.NetworkManager.Singleton != null ? Unity.Netcode.NetworkManager.Singleton.LocalClientId : 0;
        return TrySlot(me, ids, expected, out slot, out count);
    }

    /// <summary>
    /// 출격 집결 — 구역을 찾고(없음·비활성 = 바로 실패) 내 자리로 걸어가 맵 플레이어가 설 때까지 머문다. 밀려나면 다시 들어간다.
    /// 결과는 <paramref name="result"/>. <paramref name="tag"/> = 로그 접두(103 은 <see cref="QABoss103Plan.Tag"/>), 이벤트명 sortie_* 는 공통.
    /// </summary>
    public static IEnumerator Gather(QAInputController input, int slot, int count, float timeout, QASortieGatherResult result, string tag = "[QA]")
    {
        result.Reset();
        if (QALobbyFlow.InMapWithPlayer)
        {
            result.InMap = true;
            yield break;
        }

        float started = Time.realtimeSinceStartup;
        QABlackboard.ScriptedControl = true;
        input.SetMove(Vector3.zero);
        try
        {
            PartyGatherZone zone = null;
            while ((zone = FindZone()) == null || !zone.IsSpawned)
            {
                if (QALobbyFlow.InMapWithPlayer || Time.realtimeSinceStartup - started >= ZoneSpawnTimeoutSeconds)
                    break;
                yield return null;
            }
            if (!QALobbyFlow.InMapWithPlayer && (zone == null || !zone.IsSpawned))
            {
                result.Failure = QASortieGatherFailure.ZoneMissing;
                Finish(result, zone, started, tag, slot);
                yield break;
            }

            float activeSince = Time.realtimeSinceStartup;
            while (zone != null && zone.IsSpawned && !zone.IsActive && !QALobbyFlow.InMapWithPlayer
                   && Time.realtimeSinceStartup - activeSince < ZoneActiveTimeoutSeconds)
                yield return null;
            if (!QALobbyFlow.InMapWithPlayer && (zone == null || !zone.IsActive))
            {
                result.Failure = QASortieGatherFailure.ZoneInactive;
                Finish(result, zone, started, tag, slot);
                yield break;
            }

            Vector3 spot = zone != null ? Spot(zone, slot, count) : Vector3.zero;
            if (zone != null)
                Debug.Log($"{tag} event=sortie_walk slot={slot} count={count} zone={zone.name} center={QABoss103Plan.Token(Fmt(zone.WorldCenter))} spot={QABoss103Plan.Token(Fmt(spot))} spotInside={(zone.ContainsPoint(spot) ? "true" : "false")} {Stage()}");

            // 집결은 상대(다른 인스턴스)를 기다리므로 걸음은 재시도하고, 밀려나면 다시 들어간다.
            float nextLog = 0f;
            while (Time.realtimeSinceStartup - started < timeout)
            {
                if (QALobbyFlow.InMapWithPlayer)
                    break;
                QABlackboard.ScriptedControl = true;
                Player me = Player.LocalPlayer;
                if (QALobbyFlow.InLobby && me != null && zone != null && zone.IsSpawned && !zone.ContainsPoint(me.transform.position))
                    yield return WalkRoute(input, slot, spot, "sortie", tag);
                else
                    yield return null;

                if (Time.realtimeSinceStartup >= nextLog)
                {
                    nextLog = Time.realtimeSinceStartup + 5f;
                    Debug.Log($"{tag} event=sortie_wait slot={slot} t={Time.realtimeSinceStartup - started:F0} {State(zone)}");
                }
            }

            result.InMap = QALobbyFlow.InMapWithPlayer;
            if (!result.InMap)
                result.Failure = QASortieGatherFailure.Timeout;
            Finish(result, zone, started, tag, slot);
        }
        finally
        {
            input.SetMove(Vector3.zero);
            QABlackboard.ScriptedControl = false;
        }
    }

    private static void Finish(QASortieGatherResult result, PartyGatherZone zone, float started, string tag, int slot)
    {
        result.Seconds = Time.realtimeSinceStartup - started;
        result.State = State(zone);
        string why = result.Failure switch
        {
            QASortieGatherFailure.ZoneMissing => "출격 집결 구역 없음 — ",
            QASortieGatherFailure.ZoneInactive => "출격 집결 구역 비활성 — ",
            _ => "",
        };
        result.Note = $"{why}{result.Seconds:F0}초 · {result.State} · scene={QAUtil.ActiveSceneName()}";
        Debug.Log($"{tag} event=sortie_end slot={slot} ok={(result.InMap ? "true" : "false")} fail={result.Failure} {QABoss103Plan.Token(result.Note)}");
    }

    /// <summary>
    /// 119 C5 — 지금 자리에서 <paramref name="goal"/> 까지 씬 기준점(<see cref="WalkAnchors"/>)을 경유해 걷는다(<see cref="QABoss103Plan.WalkRoute"/>).
    /// 직선이 열려 있으면 직선, 길이 없으면 직선(이전 동작). 경유 걸음이 닿지 못하면 멈추고 호출자 루프가 지금 자리에서 다시 경로를 잡는다.
    /// <paramref name="label"/> = 로그 이벤트 접두(출격 sortie_route · 129-2 캐릭터 선택대 char_route).
    /// </summary>
    public static IEnumerator WalkRoute(QAInputController input, int slot, Vector3 goal, string label = "sortie", string tag = "[QA]")
    {
        Player me = Player.LocalPlayer;
        if (me == null)
            yield break;
        var nodes = new List<Vector3> { me.transform.position };
        foreach (Vector3 a in WalkAnchors())
            if (Mathf.Abs(a.y - me.transform.position.y) <= WalkAnchorMaxRise)
                nodes.Add(a);
        nodes.Add(goal);
        List<int> route = QABoss103Plan.WalkRoute(nodes.Count, (a, b) => WalkClear(nodes[a], nodes[b]), (a, b) => PlanarDistance(nodes[a], nodes[b]));

        var sb = new StringBuilder();
        if (route != null)
            foreach (int i in route)
                sb.Append(sb.Length > 0 ? ">" : "").Append(Fmt(nodes[i]));
        Debug.Log($"{tag} event={label}_route slot={slot} from={QABoss103Plan.Token(Fmt(nodes[0]))} anchors={nodes.Count - 2} route={(route != null ? QABoss103Plan.Token(sb.ToString()) : "none")}");
        if (route == null)
        {
            yield return QALobbyFlow.WalkTo(input, goal, 0.3f, 15f);
            yield break;
        }
        for (int k = 0; k < route.Count; k++)
        {
            bool last = k == route.Count - 1;
            Vector3 p = nodes[route[k]];
            p.y = me != null ? me.transform.position.y : p.y;
            yield return QALobbyFlow.WalkTo(input, p, last ? 0.3f : 0.5f, 15f);
            if (!QALobbyFlow.LastWalkArrived)
            {
                Debug.Log($"{tag} event={label}_route_stuck slot={slot} leg={k + 1}/{route.Count} at={QABoss103Plan.Token(Player.LocalPlayer != null ? Fmt(Player.LocalPlayer.transform.position) : "na")}");
                yield break;
            }
        }
    }

    /// <summary>경유점으로 쓰는 기준점의 내 발 높이 차 상한(m) — 계단 위(포드 데크) 점은 평면 걸음에서 뺀다.</summary>
    private const float WalkAnchorMaxRise = 0.5f;

    /// <summary>
    /// 로비 걸음 경유 기준점 — 스폰 지점·출격 패드(SortieStation)·포드 발사 걷기 시작 지점(walkSlots start·entryPath)·공용 경로점(walkPath).
    /// 좌표를 하드코딩하지 않고 씬에서 읽는다(레이아웃이 바뀌어도 열린 길을 다시 찾는다). 포드 점은 에디터 접근자라 에디터에서만.
    /// </summary>
    private static List<Vector3> WalkAnchors()
    {
        var list = new List<Vector3>();
        void Add(Transform t)
        {
            if (t != null)
                list.Add(t.position);
        }
        Transform[] spawns = QALobbyFlow.LobbySpawnPoints();
        if (spawns != null)
            foreach (Transform t in spawns)
                Add(t);
        GameObject station = GameObject.Find("SortieStation");
        if (station != null)
            Add(station.transform);
#if UNITY_EDITOR
        LobbyPodLaunch pod = Object.FindFirstObjectByType<LobbyPodLaunch>();
        if (pod != null)
        {
            foreach (LobbyPodLaunch.WalkSlot s in pod.EditorWalkSlots)
                if (s != null)
                {
                    Add(s.start);
                    if (s.entryPath != null)
                        foreach (Transform t in s.entryPath)
                            Add(t);
                }
            foreach (Transform t in pod.EditorWalkPath)
                Add(t);
        }
#endif
        return list;
    }

    /// <summary>
    /// 두 점 사이 평면 걸음이 Wall/Env 에 막히지 않는가 — 무릎 높이 구 캐스트(플레이어 캡슐 반경 0.38 + 여유 → 0.45).
    /// 출발점이 벽에 붙어 있으면 구가 처음부터 겹쳐 그 벽을 못 보므로(SphereCast 는 시작 겹침을 무시) 그때만 작은 구(0.3)로 본다.
    /// </summary>
    private static bool WalkClear(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        d.y = 0f;
        float len = d.magnitude;
        if (len < 0.01f)
            return true;
        int mask = LayerMask.GetMask("Wall", "Env");
        Vector3 origin = new Vector3(a.x, a.y + 0.6f, a.z);
        float radius = Physics.CheckSphere(origin, WalkCastRadius, mask, QueryTriggerInteraction.Ignore) ? WalkCastRadiusHugging : WalkCastRadius;
        return !Physics.SphereCast(origin, radius, d / len, out _, len, mask, QueryTriggerInteraction.Ignore);
    }

    private const float WalkCastRadius = 0.45f;
    private const float WalkCastRadiusHugging = 0.3f;

    private static float PlanarDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private static readonly FieldInfo SortieZoneField =
        typeof(LobbyTrainingDirector).GetField("sortieZone", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>진행기의 출격 구역(직렬화 private — 읽기만). 진행기·필드가 없으면 이름 Gather_Sortie 로 찾는다.</summary>
    public static PartyGatherZone FindZone()
    {
        LobbyTrainingDirector d = LobbyTrainingDirector.Active;
        if (d != null && SortieZoneField?.GetValue(d) is PartyGatherZone z && z != null)
            return z;
        foreach (PartyGatherZone s in PartyGatherZone.Spawned)
            if (s != null && s.name == "Gather_Sortie")
                return s;
        return null;
    }

    /// <summary>구역 박스 긴 축 일렬 자리(바닥 높이 = 내 플레이어 높이 — 평면 걸음).</summary>
    public static Vector3 Spot(PartyGatherZone zone, int slot, int count)
    {
        BoxCollider box = zone.GetComponent<BoxCollider>();
        Transform t = box.transform;
        Vector3 half = Vector3.Scale(box.size * 0.5f, t.lossyScale);
        bool alongX = Mathf.Abs(half.x) >= Mathf.Abs(half.z);
        float longHalf = alongX ? Mathf.Abs(half.x) : Mathf.Abs(half.z);
        float off = QABoss103Plan.PadLineOffset(slot, count, longHalf, 1.1f);
        Vector3 axis = alongX ? t.right : t.forward;
        Vector3 p = zone.WorldCenter + axis * off;
        p.y = Player.LocalPlayer != null ? Player.LocalPlayer.transform.position.y : t.position.y;
        return p;
    }

    /// <summary>진행기 단계(stage=단계/세부, 진행기 없으면 na).</summary>
    public static string Stage()
    {
        LobbyTrainingDirector d = LobbyTrainingDirector.Active;
        return d != null ? $"stage={d.Stage}/{d.Phase}" : "stage=na";
    }

    /// <summary>집결 n/m·active·countdown·confirmed·meIn·stage — 실패 detail·주기 로그 공용.</summary>
    public static string State(PartyGatherZone zone)
    {
        Player me = Player.LocalPlayer;
        bool meIn = zone != null && me != null && zone.IsSpawned && zone.ContainsPoint(me.transform.position);
        return zone != null && zone.IsSpawned
            ? $"gather={zone.GatheredCount}/{zone.ParticipantCount} active={zone.IsActive} countdown={zone.IsCountdownActive} confirmed={zone.IsConfirmed} meIn={meIn} {Stage()}"
            : $"gather=na {Stage()}";
    }

    private static string Fmt(Vector3 v) => $"{v.x:F2}/{v.y:F2}/{v.z:F2}";
}

public enum QASortieGatherFailure
{
    None,
    ZoneMissing,
    ZoneInactive,
    Timeout,
}

/// <summary><see cref="QASortieGather.Gather"/> 결과 — 맵 도달 여부·실패 사유·마지막 집결 상태·근거 문장.</summary>
public sealed class QASortieGatherResult
{
    public bool InMap;
    public QASortieGatherFailure Failure;
    public string State = "";
    public string Note = "";
    public float Seconds;

    public void Reset()
    {
        InMap = false;
        Failure = QASortieGatherFailure.None;
        State = "";
        Note = "";
        Seconds = 0f;
    }
}
