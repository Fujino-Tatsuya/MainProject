#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 할 일 103 3인 보스전 밸런싱(B103, MPPM 호스트 + 가상 2) — 로비에서 자리(clientId 순위)대로 팔라딘·어쌔신·거너를 고르고
/// 정식 흐름으로 맵에 들어간 뒤, 호스트는 판을 진행·판정하고(<see cref="QABoss103Match.RunHost"/>) 가상 플레이어는 자기 몫 입력만 한다
/// (<see cref="QABoss103Match.RunClient"/> — 특성 선택·연출 스킵·재선택 팝업 '아니오'). 103 실행 메뉴는 삭제됨(155) — 야간 시나리오가 이 판 흐름을 그대로 쓴다.
/// 88 의 체력 채우기·어그로 강제·보스 타이머 정지·QA 슈퍼아머는 쓰지 않는다(밸런스 왜곡).
/// </summary>
public static partial class QALobbyScenarios
{
    public const string Boss103Id = "B103";

    /// <summary>로비·맵 진입·시작 경로 + 30분 전투 + 여유.</summary>
    public const float Boss103TimeLimitSeconds = 40f * 60f;

    private static IEnumerator B103(QAScenarioContext ctx) => Boss103Flow(ctx, false);

    /// <summary>
    /// 103 한 판 흐름. <paramref name="lobbySmoke"/> = 할 일 108 야간 QA(<see cref="Nightly108Id"/>) — 할 일 130 부터 3인 집결 뒤
    /// 로비 점검·선택대·출격 집결을 건너뛰고 맵 직행(<see cref="DirectLaunch130"/>, 단계 판정 <see cref="QABoss103Plan.LobbyStepsFor"/>).
    /// 수동(B103)은 선택대 걷기 + 출격 집결 그대로.
    /// </summary>
    private static IEnumerator Boss103Flow(QAScenarioContext ctx, bool lobbySmoke)
    {
        QABoss103Config cfg = QABoss103Match.ReadConfig(out string cfgNote);
        ctx.Eye("103 설정", $"{cfgNote} · 기준 시드 {cfg.Seed} · 판 {cfg.Run} · 판 시드 {cfg.RunSeed}" +
                          (cfg.Nightly.Length > 0 ? $" · 야간 시드 {cfg.Nightly} · 특성 판 시드 {cfg.TraitRunSeed} · 자리 순서 {(cfg.Order != null ? QABoss103Plan.FormatOrder(cfg.Order) : "기본")}" : ""));

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

        // 할 일 130 — 야간은 로비 점검·선택대·출격 집결을 건너뛰고 맵 직행(호스트가 자리별 캐릭터를 서버 저장소에 넣는다).
        QABoss103LobbySteps steps = QABoss103Plan.LobbyStepsFor(lobbySmoke);
        if ((steps & QABoss103LobbySteps.LobbySmoke) != 0)
            yield return NightlyLobbySmoke(ctx);

        var ids = new List<ulong>();
        for (int i = 0; i < LobbySession.Active.EntryCount; i++)
            ids.Add(LobbySession.Active.GetEntry(i).ClientId);
        int slot = QABoss103Plan.SlotOf(LocalId, ids);
        QABoss103Character character = cfg.CharacterForSlot(Mathf.Max(0, slot));

        if ((steps & QABoss103LobbySteps.PickCharacter) != 0)
        {
            var picked = new QAFlag();
            var pickNote = new StringBuilder();
            yield return PickCharacter103(ctx, character, Mathf.Max(0, slot), ids.Count, picked, pickNote);
            ctx.Check(picked.Value, $"103 캐릭터 고정(자리 {slot})", $"{character} → 로비 캐릭터 id {LocalCharacterId()} ({PrefabName(Roster(), LocalCharacterId())}) · {pickNote}");
            if (!picked.Value)
            {
                QABoss103Match.AbortBeforeMap(ctx, cfg, $"캐릭터 {character} 고정 실패 — {pickNote}");
                yield break;
            }
        }

        var inMap = new QAFlag();
        var mapNote = new StringBuilder();
        if ((steps & QABoss103LobbySteps.DirectLaunch) != 0)
            yield return DirectLaunch130(ctx, cfg, ids, Mathf.Max(0, slot), inMap, mapNote);
        else if ((steps & QABoss103LobbySteps.Sortie) != 0)
            // dev 86 로비 — 준비 패드 대신 출격 집결 구역(Gather_Sortie)에 전원 5초 → 진행기가 메인 진입.
            yield return Sortie103(ctx, Mathf.Max(0, slot), ids.Count, inMap, mapNote);
        if (!inMap.Value)
        {
            QABoss103Match.AbortBeforeMap(ctx, cfg, $"맵 진입 실패 — {mapNote}");
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
    /// 129-2 — 걸음은 출격과 같은 경유점 경로(<see cref="QASortieGather.WalkRoute"/>, 로그 char_route). 직선은 벽에 막혀 출격 구역 안에 멈출 수 있었다.
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
                yield return QASortieGather.WalkRoute(ctx.Input, slot, spot, "char", QABoss103Plan.Tag);
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
    /// 할 일 130 야간 맵 직행 — 호스트는 자리별 캐릭터(<see cref="QABoss103Plan.TryMapSlotCharacterIds"/>, 선택대 요청과 같은 로스터 id)를
    /// 서버 저장소(<see cref="ServerCharacterSelectionStore.Set"/> — 맵 스폰 <see cref="PlayerSpawnService"/> 가 읽는다)에 넣고
    /// 진행기 <see cref="LobbyTrainingDirector.ServerLaunchMainForQA"/> 로 메인 진입. 클라는 걷지 않는다.
    /// 결과는 내 맵 플레이어가 설 때까지(상한 <see cref="SortieMapTimeoutSeconds"/>). 로비 외형은 기본 캐릭터인 채 넘어간다(맵 스폰만 자리 캐릭터).
    /// </summary>
    private static IEnumerator DirectLaunch130(QAScenarioContext ctx, QABoss103Config cfg, List<ulong> ids, int slot, QAFlag ok, StringBuilder note)
    {
        ok.Value = false;
        float started = Time.realtimeSinceStartup;
        if (QALobbyFlow.IsServer)
        {
            CharacterRoster roster = Roster();
            var rosterCharacters = new List<QABoss103Character?>();
            if (roster != null)
                for (int i = 0; i < roster.Count; i++)
                {
                    QABoss103Character? c = null;
                    if (roster.TryGetAvailableCharacter(i, out CharacterRoster.Entry e))
                        foreach (QABoss103Character q in QABoss103Plan.PartyOrder)
                            if (e.TutorialCharacter == ToTutorialCharacter(q))
                                c = q;
                    rosterCharacters.Add(c);
                }
            if (!QABoss103Plan.TryMapSlotCharacterIds(ids, cfg.CharacterForSlot, rosterCharacters, out List<QABoss103SlotPick> picks, out string failure))
            {
                note.Append($"자리 캐릭터 id 못 찾음 — {failure} · 로스터={(roster != null ? roster.name : "없음")}");
                ctx.Fail("130 맵 직행(자리 캐릭터 저장)", note.ToString());
                yield break;
            }

            var stored = new StringBuilder();
            bool allStored = true;
            foreach (QABoss103SlotPick p in picks)
            {
                ServerCharacterSelectionStore.Set(p.ClientId, p.CharacterId);
                bool back = ServerCharacterSelectionStore.TryGet(p.ClientId, out int got) && got == p.CharacterId;
                allStored &= back;
                stored.Append(stored.Length > 0 ? ", " : "").Append($"자리{p.Slot} client{p.ClientId} {p.Character}=id{p.CharacterId}({PrefabName(roster, p.CharacterId)}){(back ? "" : " 읽기 불일치")}");
                Debug.Log($"{QABoss103Plan.Tag} event=direct_store slot={p.Slot} client={p.ClientId} character={p.Character} id={p.CharacterId} readBack={(back ? "true" : "false")}");
            }
            ctx.Eye("130 자리별 저장 id", stored.ToString());

            LobbyTrainingDirector d = LobbyTrainingDirector.Active;
            bool launched = allStored && d != null && d.ServerLaunchMainForQA();
            ctx.Check(launched, "130 맵 직행",
                $"로비 점검·선택대·출격 집결 건너뜀 · {QABoss103Plan.FormatSlotPicks(picks)} · 진행기={(d != null ? "있음" : "없음")} · 저장 확인={allStored} · {LobbyStage103()}");
            Debug.Log($"{QABoss103Plan.Tag} event=direct_launch ok={(launched ? "true" : "false")} {LobbyStage103()}");
            if (!launched)
            {
                note.Append($"진행기 메인 진입 거절 — 진행기={(d != null ? "있음" : "없음")} 저장 확인={allStored} {LobbyStage103()}");
                yield break;
            }
        }
        else
        {
            ctx.Eye("130 맵 직행", $"자리 {slot} — 로비에서 걷지 않고 호스트 맵 진입 대기");
        }

        yield return ctx.Wait(() => QALobbyFlow.InMapWithPlayer, SortieMapTimeoutSeconds);
        ok.Value = QALobbyFlow.InMapWithPlayer;
        note.Append($"{Time.realtimeSinceStartup - started:F0}초 · scene={QAUtil.ActiveSceneName()} · {LobbyStage103()}");
        Debug.Log($"{QABoss103Plan.Tag} event=direct_end slot={slot} ok={(ok.Value ? "true" : "false")} {QABoss103Plan.Token(note.ToString())}");
        if (!ok.Value)
            ctx.Fail("맵 진입(직행)", $"{SortieMapTimeoutSeconds:F0}초 안에 맵 플레이어 없음 — {note}", ctx.Shot("map_timeout"));
    }

    /// <summary>
    /// 출격(dev 86 T2b) — 공용 출격 집결(<see cref="QASortieGather.Gather"/>, 할 일 169 에서 옮김 — 10분 시나리오와 같은 메커니즘·로그 이벤트 sortie_*).
    /// 출격 집결 구역(진행기 sortieZone = Gather_Sortie, SortieStation 위)의 긴 축 일렬 자리(<see cref="QABoss103Plan.PadLineOffset"/>)로 걸어가
    /// 그대로 머문다 → 서버 집결 유지 시간(LobbyRules 5초) → 진행기가 메인 진입. 훈련 입구 구역은 지나가지 않는다(Lobby 단계에서 입구 확정 = 훈련 시작 → 출격 꺼짐).
    /// 결과는 내 맵 플레이어가 설 때까지(상한 <see cref="SortieMapTimeoutSeconds"/>). 실패 detail = 집결 n/m·단계.
    /// </summary>
    private static IEnumerator Sortie103(QAScenarioContext ctx, int slot, int count, QAFlag ok, StringBuilder note)
    {
        var result = new QASortieGatherResult();
        yield return QASortieGather.Gather(ctx.Input, slot, count, SortieMapTimeoutSeconds, result, QABoss103Plan.Tag);
        ok.Value = result.InMap;
        note.Append(result.Note);
        if (ok.Value)
            yield break;
        if (result.Failure == QASortieGatherFailure.ZoneMissing || result.Failure == QASortieGatherFailure.ZoneInactive)
            ctx.Fail("103 출격 집결 구역", note.ToString());
        else
            ctx.Fail("맵 진입(출격 집결)", $"{SortieMapTimeoutSeconds:F0}초 안에 맵 플레이어 없음 — {note}", ctx.Shot("map_timeout"));
    }

    /// <summary>출격 집결부터 맵 플레이어까지 상한(초) — 다른 인스턴스 걸음·5초 집결·로딩 포함.</summary>
    private const float SortieMapTimeoutSeconds = QASortieGather.DefaultMapTimeoutSeconds;

    private static string LobbyStage103() => QASortieGather.Stage();

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
