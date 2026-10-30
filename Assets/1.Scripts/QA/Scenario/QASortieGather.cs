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

            bool moved = false;
            Vector3 spot = zone != null ? FreeSpot(zone, slot, count, out moved) : Vector3.zero;
            if (zone != null)
                Debug.Log($"{tag} event=sortie_walk slot={slot} count={count} zone={zone.name} center={QABoss103Plan.Token(Fmt(zone.WorldCenter))} spot={QABoss103Plan.Token(Fmt(spot))} spotInside={(zone.ContainsPoint(spot) ? "true" : "false")} spotMoved={(moved ? "true" : "false")} {Stage()}");

            // 집결은 상대(다른 인스턴스)를 기다리므로 걸음은 재시도하고, 밀려나면 다시 들어간다.
            // 막힌 다리는 memory 에 남아 다음 경로에서 빠진다(할 일 169 2차). 구역 안에서 막히면 거기서 멈춘다 — 틈에 대고 계속 밀면 벽 타기(P9)가 난다.
            var memory = new QAWalkMemory();
            System.Func<bool> settled = () => zone != null && zone.IsSpawned && Player.LocalPlayer != null &&
                                              InsideInset(zone, Player.LocalPlayer.transform.position, SettleInset);
            float nextLog = 0f;
            while (Time.realtimeSinceStartup - started < timeout)
            {
                if (QALobbyFlow.InMapWithPlayer)
                    break;
                QABlackboard.ScriptedControl = true;
                Player me = Player.LocalPlayer;
                if (QALobbyFlow.InLobby && me != null && zone != null && zone.IsSpawned && !zone.ContainsPoint(me.transform.position))
                    yield return WalkRoute(input, slot, spot, "sortie", tag, memory, settled);
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
    /// 할 일 169 2차 — 다리마다 정체 감지(<see cref="QAWalkStall"/>, 2초 0.35m): 막히면 그 다리를 <paramref name="memory"/> 에 넣어
    /// 다음 경로에서 빼고(<see cref="QAWalkMemory"/>), 뒤로 물러났다 옆으로 비켜 걷는다. 이전엔 다리마다 15초를 같은 틈에 대고 밀다가
    /// 같은 경로를 다시 잡아 180초 타임아웃까지 끼어 있었다. <paramref name="settleWhenStalled"/> 가 참이면(예: 이미 구역 안) 막힌 자리에서 멈춘다.
    /// </summary>
    public static IEnumerator WalkRoute(QAInputController input, int slot, Vector3 goal, string label = "sortie", string tag = "[QA]",
        QAWalkMemory memory = null, System.Func<bool> settleWhenStalled = null)
    {
        Player me = Player.LocalPlayer;
        if (me == null)
            yield break;
        memory ??= new QAWalkMemory();
        var nodes = new List<Vector3> { me.transform.position };
        foreach (Vector3 a in WalkAnchors())
            if (Mathf.Abs(a.y - me.transform.position.y) <= WalkAnchorMaxRise)
                nodes.Add(a);
        nodes.Add(goal);
        List<int> route = QABoss103Plan.WalkRoute(nodes.Count,
            (a, b) => !memory.IsBlocked(nodes[a], nodes[b]) && WalkClear(nodes[a], nodes[b]),
            (a, b) => PlanarDistance(nodes[a], nodes[b]));

        var sb = new StringBuilder();
        if (route != null)
            foreach (int i in route)
                sb.Append(sb.Length > 0 ? ">" : "").Append(Fmt(nodes[i]));
        Debug.Log($"{tag} event={label}_route slot={slot} from={QABoss103Plan.Token(Fmt(nodes[0]))} anchors={nodes.Count - 2} blocked={memory.BlockedCount} route={(route != null ? QABoss103Plan.Token(sb.ToString()) : "none")}");
        if (route == null)
        {
            // 길 없음 — 막힌 다리를 다 뺀 탓이면 잊고(다음엔 다시 시도) 이번엔 직선. 직선도 막히면 아래와 같이 비켜 걷는다.
            if (memory.BlockedCount > 0)
                memory.Clear();
            route = new List<int> { nodes.Count - 1 };
        }
        for (int k = 0; k < route.Count; k++)
        {
            bool last = k == route.Count - 1;
            Vector3 p = nodes[route[k]];
            p.y = Player.LocalPlayer != null ? Player.LocalPlayer.transform.position.y : p.y;
            Vector3 from = Player.LocalPlayer != null ? Player.LocalPlayer.transform.position : nodes[0];
            yield return WalkLeg(input, p, last ? 0.3f : 0.5f, LegTimeoutSeconds);
            if (_lastLeg == LegEnd.Arrived)
                continue;
            if (_lastLeg == LegEnd.NoPlayer)
                yield break;

            Player now = Player.LocalPlayer;
            string at = QABoss103Plan.Token(now != null ? Fmt(now.transform.position) : "na");
            if (settleWhenStalled != null && settleWhenStalled())
            {
                Debug.Log($"{tag} event={label}_route_settle slot={slot} leg={k + 1}/{route.Count} at={at} why={_lastLeg}");
                yield break;
            }
            memory.Block(from, p);
            Debug.Log($"{tag} event={label}_route_stuck slot={slot} leg={k + 1}/{route.Count} at={at} why={_lastLeg} target={QABoss103Plan.Token(Fmt(p))} blocked={memory.BlockedCount}");
            yield return Sidestep(input, p - from, memory.Recoveries++, label, tag, slot);
            yield break; // 호출자 루프가 지금 자리에서 막힌 다리를 빼고 다시 경로를 잡는다
        }
    }

    private enum LegEnd { Arrived, Stalled, Timeout, NoPlayer }

    private static LegEnd _lastLeg;

    /// <summary>다리 하나 상한(초) — 정체 감지가 먼저 끊는 게 보통이고, 조금씩 미끄러지며 안 닿는 경우의 안전판.</summary>
    private const float LegTimeoutSeconds = 15f;

    /// <summary>평면으로 목표까지 걷는다(<see cref="QALobbyFlow.WalkTo"/> 와 같은 입력) + 정체 감지. 결과는 <see cref="_lastLeg"/>.</summary>
    private static IEnumerator WalkLeg(QAInputController input, Vector3 target, float stopDistance, float timeout)
    {
        _lastLeg = LegEnd.Timeout;
        QABlackboard.ScriptedControl = true;
        var stall = new QAWalkStall();
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            Player p = Player.LocalPlayer;
            if (p == null)
            {
                _lastLeg = LegEnd.NoPlayer;
                break;
            }
            input.SetTarget(p);
            Vector3 to = target - p.transform.position;
            to.y = 0f;
            if (to.magnitude <= stopDistance)
            {
                _lastLeg = LegEnd.Arrived;
                break;
            }
            if (stall.Tick(p.transform.position, Time.realtimeSinceStartup))
            {
                _lastLeg = LegEnd.Stalled;
                break;
            }
            input.SetMove(to);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        input.SetMove(Vector3.zero);
    }

    private const float SidestepBackSeconds = 0.4f;
    private const float SidestepSideSeconds = 0.8f;
    private const float SidestepProbeMeters = 1f;

    /// <summary>막힌 방향 반대로 잠깐 물러났다가 옆으로 비켜 걷는다. 옆은 시도마다 좌우 번갈아(<see cref="SidestepSide"/>), 그쪽이 막혔고 반대가 열렸으면 반대.</summary>
    private static IEnumerator Sidestep(QAInputController input, Vector3 travel, int attempt, string label, string tag, int slot)
    {
        Player me = Player.LocalPlayer;
        if (me == null)
            yield break;
        Vector3 pos = me.transform.position;
        Vector3 side = SidestepSide(travel, attempt);
        if (!WalkClear(pos, pos + side * SidestepProbeMeters) && WalkClear(pos, pos - side * SidestepProbeMeters))
            side = -side;
        Vector3 back = PlanarDir(-travel);
        Debug.Log($"{tag} event={label}_sidestep slot={slot} attempt={attempt} at={QABoss103Plan.Token(Fmt(pos))} back={QABoss103Plan.Token(Fmt(back))} side={QABoss103Plan.Token(Fmt(side))}");
        yield return Push(input, back, SidestepBackSeconds);
        yield return Push(input, side, SidestepSideSeconds);
    }

    private static IEnumerator Push(QAInputController input, Vector3 dir, float seconds)
    {
        float t = 0f;
        while (t < seconds && Player.LocalPlayer != null)
        {
            input.SetTarget(Player.LocalPlayer);
            input.SetMove(dir);
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        input.SetMove(Vector3.zero);
    }

    /// <summary>진행 방향의 옆(평면). attempt 짝수 = 왼쪽, 홀수 = 오른쪽. 방향이 0 이면 +X 기준.</summary>
    public static Vector3 SidestepSide(Vector3 travel, int attempt)
    {
        Vector3 n = PlanarDir(travel);
        Vector3 left = new Vector3(-n.z, 0f, n.x);
        return attempt % 2 == 0 ? left : -left;
    }

    private static Vector3 PlanarDir(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.right;
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
    /// 두 점 사이 평면 걸음이 막히지 않는가 — 무릎 높이 구 캐스트(플레이어 캡슐 반경 0.38 + 여유 → 0.45).
    /// 출발점이 벽에 붙어 있으면 구가 처음부터 겹쳐 그 벽을 못 보므로(시작 겹침 = 거리 0 은 버린다) 그때만 작은 구(0.3)로 본다.
    /// 할 일 169 2차 — 마스크에 Default·Barrier 를 더했다. 포드 발사대(PF_DropPod_LaunchStation…/Platform_Fixed/collider)와
    /// 계단·데크(PodLaunch_FloorCollider/Stair_1·2·Floor_1·2)가 Default 레이어라 Wall/Env 만 보던 판정은 출격 구역 앞 계단과 발사대 사이 틈을
    /// '열림' 으로 보고 그 틈(-0.6~-2.1, 6.68)으로 걸어 들어가 끼었다. 플레이어 자신·다른 플레이어 콜라이더(Default 시체 상자 포함)는 뺀다.
    /// </summary>
    private static bool WalkClear(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        d.y = 0f;
        float len = d.magnitude;
        if (len < 0.01f)
            return true;
        int mask = ObstacleMask;
        Vector3 origin = new Vector3(a.x, a.y + 0.6f, a.z);
        float radius = AnyObstacle(Physics.OverlapSphere(origin, WalkCastRadius, mask, QueryTriggerInteraction.Ignore)) ? WalkCastRadiusHugging : WalkCastRadius;
        foreach (RaycastHit hit in Physics.SphereCastAll(origin, radius, d / len, len, mask, QueryTriggerInteraction.Ignore))
            if (hit.distance > 0f && !IsPlayerCollider(hit.collider))
                return false;
        return true;
    }

    private const float WalkCastRadius = 0.45f;
    private const float WalkCastRadiusHugging = 0.3f;

    /// <summary>플레이어 걸음을 막는 정적 지형 레이어(바닥 Ground 제외).</summary>
    private static int ObstacleMask => LayerMask.GetMask("Default", "Wall", "Env", "Barrier");

    private static bool IsPlayerCollider(Collider c) => c != null && c.GetComponentInParent<Player>() != null;

    private static bool AnyObstacle(Collider[] hits)
    {
        foreach (Collider c in hits)
            if (!IsPlayerCollider(c))
                return true;
        return false;
    }

    /// <summary>발 위치 p 에 플레이어가 설 수 있는가(캡슐 반경 0.4, 발목~1.5m 겹침 없음). 데크·계단 밑, 계단과 발사대 사이 틈은 거짓.</summary>
    private static bool Standable(Vector3 p)
    {
        const float r = 0.4f;
        Collider[] hits = Physics.OverlapCapsule(p + Vector3.up * (r + 0.15f), p + Vector3.up * 1.5f, r, ObstacleMask, QueryTriggerInteraction.Ignore);
        return !AnyObstacle(hits);
    }

    /// <summary>구역 안에서 막히면 거기서 멈추는 기준 — 박스 가장자리에서 이만큼(m) 안쪽.</summary>
    private const float SettleInset = 0.3f;

    /// <summary>자리 후보 격자: 박스 가장자리 안쪽 여백(m)·간격(m).</summary>
    private const float SpotInset = 0.8f;
    private const float SpotGridStep = 0.5f;

    /// <summary>자리끼리 최소 평면 간격(m) — 캡슐 지름 0.76 + 여유.</summary>
    private const float SpotSeparation = 1.0f;

    /// <summary>월드 점이 구역 박스 안이고 수평 가장자리에서 <paramref name="inset"/> 이상 안쪽인가.</summary>
    public static bool InsideInset(PartyGatherZone zone, Vector3 worldPosition, float inset)
    {
        BoxCollider box = zone.GetComponent<BoxCollider>();
        Vector3 local = box.transform.InverseTransformPoint(worldPosition) - box.center;
        Vector3 half = box.size * 0.5f;
        Vector3 s = box.transform.lossyScale;
        float ix = inset / Mathf.Max(1e-4f, Mathf.Abs(s.x));
        float iz = inset / Mathf.Max(1e-4f, Mathf.Abs(s.z));
        return Mathf.Abs(local.x) <= half.x - ix && Mathf.Abs(local.z) <= half.z - iz && Mathf.Abs(local.y) <= half.y;
    }

    /// <summary>
    /// 설 수 있는 자리(할 일 169 2차). 긴 축 일렬 자리(<see cref="Spot"/>)가 지형 속이면(Gather_Sortie 일렬은 발사대 데크 밑 z 8.09)
    /// 박스 안 격자 후보 중 설 수 있고 앞 자리와 1m 떨어진 가장 가까운 점으로 옮긴다(<see cref="ChooseSpots"/>).
    /// 자리 0..count-1 을 같은 순서로 계산하므로 인스턴스마다 같은 결과다(다른 플레이어 콜라이더는 판정에서 뺀다).
    /// </summary>
    public static Vector3 FreeSpot(PartyGatherZone zone, int slot, int count, out bool moved)
    {
        // 높이 = 구역 바닥(Ground) — 내 발 높이는 인스턴스마다 다를 수 있어(계단 위 등) 자리 계산이 갈린다.
        float y = Spot(zone, 0, 1).y;
        Vector3 top = zone.WorldCenter + Vector3.up * 1.4f;
        if (Physics.Raycast(top, Vector3.down, out RaycastHit floor, 6f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore))
            y = floor.point.y;
        var preferred = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            Vector3 p = Spot(zone, i, count);
            p.y = y;
            preferred.Add(p);
        }
        // 설 수 있어도 막힌 구석(발사대 뒤 오른쪽 x 6.1 등)은 못 간다 — 스폰 지점에서 경유점으로 닿는 점만 쓴다.
        List<Vector3> reach = ReachableAnchors(y);
        System.Func<Vector3, bool> usable = p =>
        {
            if (!Standable(p))
                return false;
            if (reach == null)
                return true;
            foreach (Vector3 a in reach)
                if (WalkClear(a, p))
                    return true;
            return false;
        };
        Vector3[] spots = ChooseSpots(preferred, ZoneCandidates(zone, y, SpotInset, SpotGridStep), usable, SpotSeparation);
        int s = Mathf.Clamp(slot, 0, spots.Length - 1);
        moved = PlanarDistance(spots[s], preferred[s]) > 0.01f;
        return spots[s];
    }

    /// <summary>
    /// 스폰 지점에서 평면 걸음(<see cref="WalkClear"/>)으로 이어지는 경유 기준점(높이 floorY ± <see cref="WalkAnchorMaxRise"/>).
    /// 스폰 지점을 못 찾으면 null(도달 판정 생략).
    /// </summary>
    private static List<Vector3> ReachableAnchors(float floorY)
    {
        Transform[] spawns = QALobbyFlow.LobbySpawnPoints();
        if (spawns == null || spawns.Length == 0)
            return null;
        var nodes = new List<Vector3>();
        foreach (Vector3 a in WalkAnchors())
            if (Mathf.Abs(a.y - floorY) <= WalkAnchorMaxRise)
                nodes.Add(a);
        var reached = new List<Vector3>();
        var queue = new Queue<Vector3>();
        foreach (Transform t in spawns)
            if (t != null)
            {
                reached.Add(t.position);
                queue.Enqueue(t.position);
            }
        while (queue.Count > 0)
        {
            Vector3 u = queue.Dequeue();
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                if (!WalkClear(u, nodes[i]))
                    continue;
                reached.Add(nodes[i]);
                queue.Enqueue(nodes[i]);
                nodes.RemoveAt(i);
            }
        }
        return reached;
    }

    /// <summary>박스 안 격자 후보(가장자리 inset 안쪽, 간격 step, 높이 y).</summary>
    private static List<Vector3> ZoneCandidates(PartyGatherZone zone, float y, float inset, float step)
    {
        var list = new List<Vector3>();
        BoxCollider box = zone.GetComponent<BoxCollider>();
        Transform t = box.transform;
        Vector3 half = Vector3.Scale(box.size * 0.5f, t.lossyScale);
        float hx = Mathf.Abs(half.x) - inset, hz = Mathf.Abs(half.z) - inset;
        if (hx < 0f || hz < 0f)
            return list;
        Vector3 c = zone.WorldCenter;
        for (float x = -hx; x <= hx + 1e-3f; x += step)
            for (float z = -hz; z <= hz + 1e-3f; z += step)
            {
                Vector3 p = c + t.right * x + t.forward * z;
                p.y = y;
                list.Add(p);
            }
        return list;
    }

    /// <summary>
    /// 자리 고르기(순수). 자리 i 순서대로: 선호 점이 설 수 있고 앞에서 고른 자리와 <paramref name="minSeparation"/> 이상 떨어졌으면 그대로,
    /// 아니면 그 조건을 만족하는 후보 중 선호 점에 평면으로 가장 가까운 것. 그런 후보도 없으면 선호 점 그대로.
    /// <paramref name="standable"/> 은 후보마다 한 번만 부른다.
    /// </summary>
    public static Vector3[] ChooseSpots(IReadOnlyList<Vector3> preferred, IReadOnlyList<Vector3> candidates, System.Func<Vector3, bool> standable, float minSeparation)
    {
        var result = new Vector3[preferred.Count];
        var ok = new bool[candidates.Count];
        for (int c = 0; c < candidates.Count; c++)
            ok[c] = standable(candidates[c]);

        bool Separated(Vector3 p, int upTo)
        {
            for (int j = 0; j < upTo; j++)
                if (PlanarDistance(result[j], p) < minSeparation)
                    return false;
            return true;
        }

        for (int i = 0; i < preferred.Count; i++)
        {
            Vector3 want = preferred[i];
            if (standable(want) && Separated(want, i))
            {
                result[i] = want;
                continue;
            }
            int best = -1;
            float bestD = float.MaxValue;
            for (int c = 0; c < candidates.Count; c++)
            {
                if (!ok[c] || !Separated(candidates[c], i))
                    continue;
                float d = PlanarDistance(candidates[c], want);
                if (d < bestD)
                {
                    bestD = d;
                    best = c;
                }
            }
            result[i] = best >= 0 ? candidates[best] : want;
        }
        return result;
    }

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

/// <summary>
/// 걸음 정체 감지(순수, 할 일 169 2차). 기준점에서 평면으로 <see cref="MinProgress"/> 이상 벗어나면 기준을 옮기고,
/// 못 벗어난 채 <see cref="WindowSeconds"/> 가 지나면 정체. 시간은 호출자가 넘긴다.
/// </summary>
public sealed class QAWalkStall
{
    public const float DefaultWindowSeconds = 2f;
    public const float DefaultMinProgress = 0.35f;

    public float WindowSeconds { get; }
    public float MinProgress { get; }

    private Vector3 _anchor;
    private float _since;
    private bool _has;

    public QAWalkStall(float windowSeconds = DefaultWindowSeconds, float minProgress = DefaultMinProgress)
    {
        WindowSeconds = windowSeconds;
        MinProgress = minProgress;
    }

    public void Reset() => _has = false;

    /// <summary>이번 위치·시각. 정체면 true.</summary>
    public bool Tick(Vector3 position, float now)
    {
        position.y = 0f;
        if (!_has || Vector3.Distance(position, _anchor) >= MinProgress)
        {
            _anchor = position;
            _since = now;
            _has = true;
            return false;
        }
        return now - _since >= WindowSeconds;
    }
}

/// <summary>
/// 막힌 걸음 다리 기억(순수, 할 일 169 2차). 다리 (from → to) 가 막혔으면, 출발이 from 에서 <see cref="FromRadius"/> 안이고
/// 도착이 to 에서 <see cref="ToRadius"/> 안인 다리는 막힌 것으로 본다(비켜 걸은 뒤 같은 방향을 다시 고르지 않게).
/// </summary>
public sealed class QAWalkMemory
{
    public const float FromRadius = 1.5f;
    public const float ToRadius = 0.5f;

    private readonly List<KeyValuePair<Vector3, Vector3>> _blocked = new List<KeyValuePair<Vector3, Vector3>>();

    /// <summary>비켜 걷기 횟수(좌우 번갈아 고르는 데 쓴다).</summary>
    public int Recoveries;

    public int BlockedCount => _blocked.Count;

    public void Block(Vector3 from, Vector3 to) => _blocked.Add(new KeyValuePair<Vector3, Vector3>(from, to));

    public void Clear() => _blocked.Clear();

    public bool IsBlocked(Vector3 from, Vector3 to)
    {
        foreach (KeyValuePair<Vector3, Vector3> e in _blocked)
            if (Planar(from, e.Key) <= FromRadius && Planar(to, e.Value) <= ToRadius)
                return true;
        return false;
    }

    private static float Planar(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
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
