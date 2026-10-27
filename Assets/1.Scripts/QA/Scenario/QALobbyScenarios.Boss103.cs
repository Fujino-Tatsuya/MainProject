#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 할 일 103 3인 보스전 밸런싱(B103, MPPM 호스트 + 가상 2) — 로비에서 자리(clientId 순위)대로 팔라딘·어쌔신·거너를 고르고
/// 정식 흐름으로 맵에 들어간 뒤, 호스트는 판을 진행·판정하고(<see cref="QABoss103Match.RunHost"/>) 가상 플레이어는 자기 몫 입력만 한다
/// (<see cref="QABoss103Match.RunClient"/> — 특성 선택·연출 스킵·재선택 팝업 '아니오'). 설정·실행은 에디터 메뉴 <c>Tools/QA/103</c>.
/// 88 의 체력 채우기·어그로 강제·보스 타이머 정지·QA 슈퍼아머는 쓰지 않는다(밸런스 왜곡).
/// </summary>
public static partial class QALobbyScenarios
{
    public const string Boss103Id = "B103";

    /// <summary>로비·맵 진입·시작 경로 + 30분 전투 + 여유.</summary>
    public const float Boss103TimeLimitSeconds = 40f * 60f;

    private static IEnumerator B103(QAScenarioContext ctx)
    {
        QABoss103Config cfg = QABoss103Match.ReadConfig(out string cfgNote);
        ctx.Eye("103 설정", $"{cfgNote} · 기준 시드 {cfg.Seed} · 판 {cfg.Run} · 판 시드 {cfg.RunSeed}");

        // C6 — 심장박동(에디터 루프가 읽는다) + 런타임 데이터 출처. 연속 루프 판은 호스트가 테이블이 아니면 바로 Error.
        QABoss103Heartbeat.Begin(cfg, ctx.Role);
        string source = QABoss103Heartbeat.RuntimeDataSource();
        ctx.Eye("103 데이터 출처(런타임)", $"{source} · 연속 루프 판={(cfg.Batch != 0 ? "예" : "아니오")}");
        if (cfg.Batch != 0 && ctx.Role == "host" && !QABoss103Batch.IsTable(source))
        {
            QABoss103Match.AbortBeforeMap(ctx, cfg, $"{QABoss103Batch.ReasonDataSource} host={source}", QABoss103Outcome.Error);
            yield break;
        }

        var lobby = new QAFlag();
        yield return EnsureLobbyPlayer(ctx, 120f, lobby);
        if (!lobby.Value)
        {
            QABoss103Match.AbortBeforeMap(ctx, cfg, "로비 플레이어 없음");
            yield break;
        }
        yield return CloseResultPanelIfOpen(ctx, null);

        // 3인이 2초 이상 안정적으로 모인 뒤 자리를 정한다(늦은 접속이 자리를 바꾸지 않게).
        float stable = 0f;
        yield return ctx.Wait(() =>
        {
            bool full = LobbySession.Active != null && LobbySession.Active.EntryCount >= ctx.ExpectedPlayers;
            stable = full ? stable + Time.unscaledDeltaTime : 0f;
            return stable >= 2f;
        }, 90f);
        if (ctx.TimedOut)
        {
            QABoss103Match.AbortBeforeMap(ctx, cfg, $"로비 {ctx.ExpectedPlayers}인 집결 실패 — {(LobbySession.Active != null ? LobbySession.Active.EntryCount : 0)}명");
            yield break;
        }

        var ids = new List<ulong>();
        for (int i = 0; i < LobbySession.Active.EntryCount; i++)
            ids.Add(LobbySession.Active.GetEntry(i).ClientId);
        int slot = QABoss103Plan.SlotOf(LocalId, ids);
        QABoss103Character character = QABoss103Plan.CharacterForSlot(Mathf.Max(0, slot));

        var picked = new QAFlag();
        var pickNote = new StringBuilder();
        yield return PickCharacter103(ctx, character, Mathf.Max(0, slot), ids.Count, picked, pickNote);
        ctx.Check(picked.Value, $"103 캐릭터 고정(자리 {slot})", $"{character} → 로비 캐릭터 id {LocalCharacterId()} ({PrefabName(Roster(), LocalCharacterId())}) · {pickNote}");
        if (!picked.Value)
        {
            QABoss103Match.AbortBeforeMap(ctx, cfg, $"캐릭터 {character} 고정 실패 — {pickNote}");
            yield break;
        }

        // dev 86 로비 — 준비 패드 대신 출격 집결 구역(Gather_Sortie)에 전원 5초 → 진행기가 메인 진입.
        var inMap = new QAFlag();
        var sortieNote = new StringBuilder();
        yield return Sortie103(ctx, Mathf.Max(0, slot), ids.Count, inMap, sortieNote);
        if (!inMap.Value)
        {
            QABoss103Match.AbortBeforeMap(ctx, cfg, $"맵 진입 실패 — {sortieNote}");
            yield break;
        }
        QABoss103Heartbeat.SetPhase("map");

        // C6 — 클론도 테이블 값으로 도는지(클론 심장박동의 출처). 모르는 클론(na)은 막지 않는다.
        if (cfg.Batch != 0 && QALobbyFlow.IsServer)
        {
            string combined = QABoss103Heartbeat.CombinedDataSource(cfg);
            if (!QABoss103Batch.IsTable(combined))
            {
                QABoss103Match.AbortBeforeMap(ctx, cfg, $"{QABoss103Batch.ReasonDataSource} {combined}", QABoss103Outcome.Error);
                yield break;
            }
        }

        if (QALobbyFlow.IsServer)
            yield return QABoss103Match.RunHost(ctx, cfg, character);
        else
            yield return QABoss103Match.RunClient(ctx, cfg, character);
    }

    /// <summary>
    /// 로스터에서 그 캐릭터(<see cref="CharacterRoster.Entry.TutorialCharacter"/>) 칸을 찾아 정식 요청(<see cref="LobbySession.RequestCharacter"/>)으로 바꾼다.
    /// dev 86 로비부터 서버가 캐릭터 선택대(<see cref="LobbyInteractableKind.CharacterSelect"/>) 반경 + 여유 안에서만 받으므로 먼저 걸어간다 —
    /// 자리마다 선택대 둘레 다른 각도(<see cref="QABoss103Plan.ArcAngle"/>, 스폰 쪽을 향한 호). 서버 순간이동은 쓰지 않는다.
    /// </summary>
    private static IEnumerator PickCharacter103(QAScenarioContext ctx, QABoss103Character character, int slot, int count, QAFlag ok, StringBuilder note)
    {
        ok.Value = false;
        CharacterRoster roster = Roster();
        TutorialCharacter want = ToTutorialCharacter(character);
        int target = -1;
        if (roster != null)
            for (int i = 0; i < roster.Count; i++)
                if (roster.TryGetAvailableCharacter(i, out CharacterRoster.Entry e) && e.TutorialCharacter == want)
                {
                    target = i;
                    break;
                }
        if (target < 0)
        {
            ctx.Fail("103 캐릭터 칸 찾기", $"로스터={(roster != null ? roster.name : "없음")} 에 {want} 칸 없음");
            yield break;
        }

        if (LocalCharacterId() == target && Player.LocalPlayer != null)
        {
            ok.Value = true;
            note.Append("이미 그 캐릭터");
            yield break;
        }

        LobbyInteractable station = Player.LocalPlayer != null
            ? QALobbyFlow.FindInteractable(LobbyInteractableKind.CharacterSelect, Player.LocalPlayer.transform.position)
            : null;
        if (station == null)
        {
            note.Append("캐릭터 선택대(LobbyInteractable CharacterSelect) 없음");
            yield break;
        }
        Vector3 spot = SelectSpot103(station, slot, count);
        Debug.Log($"{QABoss103Plan.Tag} event=char_walk slot={slot} station={QABossStart88.Fmt(station.Position)} r={station.Radius:F1} spot={QABoss103Plan.Token(QABossStart88.Fmt(spot))}");

        LobbyCharacterChangeReject lastReject = LobbyCharacterChangeReject.None;
        int rejects = 0;
        void OnReject(LobbyCharacterChangeReject r)
        {
            lastReject = r;
            rejects++;
        }
        LobbySession.CharacterRequestRejected += OnReject;
        try
        {
            // 걷기 → 요청 → 3초 대기. 거부·미반영이면 사유를 남기고 다시 걸어간 뒤 재요청(교체 쿨다운·교체 중 거부 포함).
            for (int attempt = 1; attempt <= CharacterRequestAttempts; attempt++)
            {
                yield return QALobbyFlow.WalkTo(ctx.Input, spot, 0.3f, 25f);
                float dist = Player.LocalPlayer != null ? NearestInteractable.PlanarDistance(Player.LocalPlayer.transform.position, station.Position) : -1f;
                int rejectsBefore = rejects;
                LobbySession.Active?.RequestCharacter(target);
                yield return ctx.Wait(() => (LocalCharacterId() == target && Player.LocalPlayer != null) || rejects > rejectsBefore, 3f);
                bool done = LocalCharacterId() == target && Player.LocalPlayer != null;
                string why = done ? "ok" : rejects > rejectsBefore ? lastReject.ToString() : "no_reply";
                Debug.Log($"{QABoss103Plan.Tag} event=char_request slot={slot} attempt={attempt} arrived={(QALobbyFlow.LastWalkArrived ? "true" : "false")} dist={dist:F2} target={target} result={why}");
                if (done)
                {
                    ok.Value = true;
                    note.Append($"요청 {attempt}회 · 선택대 거리 {dist:F2}m");
                    yield break;
                }
                note.Clear().Append($"요청 {attempt}회 실패 — 마지막 {why} · 선택대 거리 {dist:F2}m · 도착 {QALobbyFlow.LastWalkArrived}");
                yield return ctx.Wait(() => LocalCharacterId() == target && Player.LocalPlayer != null, 1.5f);
            }
            ok.Value = LocalCharacterId() == target && Player.LocalPlayer != null;
        }
        finally
        {
            LobbySession.CharacterRequestRejected -= OnReject;
        }
    }

    private const int CharacterRequestAttempts = 6;

    /// <summary>선택대 반경 안 자리 — 스폰 지점 쪽을 향한 호 위에 자리마다 다른 각도, 반경의 70%(서버 여유 0.5m 와 별개로 안쪽).</summary>
    private static Vector3 SelectSpot103(LobbyInteractable station, int slot, int count)
    {
        Vector3 toward = Vector3.zero;
        Transform[] points = QALobbyFlow.LobbySpawnPoints();
        int n = 0;
        if (points != null)
            foreach (Transform p in points)
                if (p != null)
                {
                    toward += p.position;
                    n++;
                }
        Vector3 from = n > 0 ? toward / n : (Player.LocalPlayer != null ? Player.LocalPlayer.transform.position : station.Position + Vector3.back);
        Vector3 dir = from - station.Position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f)
            dir = Vector3.back;
        dir.Normalize();
        dir = Quaternion.Euler(0f, QABoss103Plan.ArcAngle(slot, count), 0f) * dir;
        return station.Position + dir * (station.Radius * 0.7f);
    }

    /// <summary>
    /// 출격(dev 86 T2b) — 출격 집결 구역(진행기 sortieZone = Gather_Sortie, SortieStation 위)의 긴 축 일렬 자리(<see cref="QABoss103Plan.PadLineOffset"/>)로 걸어가
    /// 그대로 머문다 → 서버 집결 유지 시간(LobbyRules 5초) → 진행기가 메인 진입. 훈련 입구 구역은 지나가지 않는다(Lobby 단계에서 입구 확정 = 훈련 시작 → 출격 꺼짐).
    /// 결과는 내 맵 플레이어가 설 때까지(상한 <see cref="SortieMapTimeoutSeconds"/>). 실패 detail = 집결 n/m·단계.
    /// </summary>
    private static IEnumerator Sortie103(QAScenarioContext ctx, int slot, int count, QAFlag ok, StringBuilder note)
    {
        ok.Value = false;
        if (QALobbyFlow.InMapWithPlayer)
        {
            ok.Value = true;
            yield break;
        }

        float started = Time.realtimeSinceStartup;
        PartyGatherZone zone = null;
        yield return ctx.Wait(() => (zone = SortieZone103()) != null && zone.IsSpawned, 30f);
        if (zone == null || !zone.IsSpawned)
        {
            note.Append($"출격 집결 구역 없음 — {LobbyStage103()}");
            ctx.Fail("103 출격 집결 구역", note.ToString());
            yield break;
        }

        Vector3 spot = SortieSpot103(zone, slot, count);
        bool inside = zone.ContainsPoint(spot);
        Debug.Log($"{QABoss103Plan.Tag} event=sortie_walk slot={slot} zone={zone.name} center={QABoss103Plan.Token(QABossStart88.Fmt(zone.WorldCenter))} spot={QABoss103Plan.Token(QABossStart88.Fmt(spot))} spotInside={(inside ? "true" : "false")} {LobbyStage103()}");

        // 집결은 상대(다른 인스턴스)를 기다리므로 걸음은 재시도하고, 밀려나면 다시 들어간다.
        float nextLog = 0f;
        while (Time.realtimeSinceStartup - started < SortieMapTimeoutSeconds)
        {
            if (QALobbyFlow.InMapWithPlayer)
                break;
            Player me = Player.LocalPlayer;
            if (QALobbyFlow.InLobby && me != null && zone != null && zone.IsSpawned && !zone.ContainsPoint(me.transform.position))
                yield return QALobbyFlow.WalkTo(ctx.Input, spot, 0.3f, 15f);
            else
                yield return null;

            if (Time.realtimeSinceStartup >= nextLog)
            {
                nextLog = Time.realtimeSinceStartup + 5f;
                Debug.Log($"{QABoss103Plan.Tag} event=sortie_wait slot={slot} t={Time.realtimeSinceStartup - started:F0} {SortieState103(zone)}");
            }
        }

        QABlackboard.ScriptedControl = false;
        ok.Value = QALobbyFlow.InMapWithPlayer;
        note.Append($"{Time.realtimeSinceStartup - started:F0}초 · {SortieState103(zone)} · scene={QAUtil.ActiveSceneName()}");
        Debug.Log($"{QABoss103Plan.Tag} event=sortie_end slot={slot} ok={(ok.Value ? "true" : "false")} {QABoss103Plan.Token(note.ToString())}");
        if (!ok.Value)
            ctx.Fail("맵 진입(출격 집결)", $"{SortieMapTimeoutSeconds:F0}초 안에 맵 플레이어 없음 — {note}", ctx.Shot("map_timeout"));
    }

    /// <summary>출격 집결부터 맵 플레이어까지 상한(초) — 다른 인스턴스 걸음·5초 집결·로딩 포함.</summary>
    private const float SortieMapTimeoutSeconds = 180f;

    private static readonly System.Reflection.FieldInfo SortieZoneField =
        typeof(LobbyTrainingDirector).GetField("sortieZone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    /// <summary>진행기의 출격 구역(직렬화 private — 읽기만). 진행기·필드가 없으면 이름 Gather_Sortie 로 찾는다.</summary>
    private static PartyGatherZone SortieZone103()
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
    private static Vector3 SortieSpot103(PartyGatherZone zone, int slot, int count)
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

    private static string LobbyStage103()
    {
        LobbyTrainingDirector d = LobbyTrainingDirector.Active;
        return d != null ? $"stage={d.Stage}/{d.Phase}" : "stage=na";
    }

    private static string SortieState103(PartyGatherZone zone)
    {
        Player me = Player.LocalPlayer;
        bool meIn = zone != null && me != null && zone.IsSpawned && zone.ContainsPoint(me.transform.position);
        return zone != null && zone.IsSpawned
            ? $"gather={zone.GatheredCount}/{zone.ParticipantCount} active={zone.IsActive} countdown={zone.IsCountdownActive} confirmed={zone.IsConfirmed} meIn={meIn} {LobbyStage103()}"
            : $"gather=na {LobbyStage103()}";
    }

    private static TutorialCharacter ToTutorialCharacter(QABoss103Character c)
    {
        switch (c)
        {
            case QABoss103Character.Paladin: return TutorialCharacter.Paladin;
            case QABoss103Character.Assassin: return TutorialCharacter.Assassin;
            default: return TutorialCharacter.Gunner;
        }
    }
}
#endif
