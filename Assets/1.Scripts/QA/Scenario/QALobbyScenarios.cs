using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 할 일 48 로비 데모 체크리스트 시나리오(PLAN-48-qa-automation §3) — 1차: S1·S3·S5·M1·M4.
/// 각 시나리오 = 준비 → 행동 → 상태 대기형 단언 → 항목 기록. 실패해도 다음 단계로 갈 수 있으면 계속 간다.
/// MPPM 은 인스턴스마다 같은 시나리오를 돌리고 역할(호스트/클라)에 따라 갈라진다 — 단계 동기화는 관측 가능한 상태로 한다.
/// </summary>
public static class QALobbyScenarios
{
    public static readonly string[] Known = { "S1", "S3", "S5", "M1", "M4" };

    /// <summary>시나리오 하나의 상한(초) — 러너의 감시 타이머. 넘으면 FAIL 로 끊고 다음으로 간다.</summary>
    public static float TimeLimit(string id)
    {
        switch (id)
        {
            case "S1": return 150f;
            case "S3": return 120f;
            case "S5": return 1500f;
            case "M1": return 300f;
            case "M4": return 360f;
            default: return 60f;
        }
    }

    public static IEnumerator Run(string id, QAScenarioContext ctx)
    {
        switch (id)
        {
            case "S1": return S1(ctx);
            case "S3": return S3(ctx);
            case "S5": return S5(ctx);
            case "M1": return M1(ctx);
            case "M4": return M4(ctx);
            default: return Unknown(ctx, id);
        }
    }

    private static IEnumerator Unknown(QAScenarioContext ctx, string id)
    {
        ctx.Skip("알 수 없는 시나리오", $"'{id}' — 1차 범위는 {string.Join(",", Known)}");
        yield break;
    }

    // ── 공통 ─────────────────────────────────────────────────────

    /// <summary>로비에 들어와 내 플레이어가 설 때까지. 실패면 FAIL 항목을 남기고 false.</summary>
    private static IEnumerator EnsureLobbyPlayer(QAScenarioContext ctx, float timeout, QAFlag ok)
    {
        yield return ctx.Wait(() => QALobbyFlow.IsConnected && QALobbyFlow.HasLocalLobbyPlayer, timeout);
        ok.Value = !ctx.TimedOut;
        if (!ok.Value)
            ctx.Fail("로비·내 플레이어 대기", $"{timeout:F0}초 안에 로비 플레이어 없음 scene={QAUtil.ActiveSceneName()} connected={QALobbyFlow.IsConnected}", ctx.Shot("lobby_timeout"));
    }

    /// <summary>준비 → 카운트다운 → 맵에 내 플레이어가 설 때까지(다른 시나리오의 준비 단계).</summary>
    private static IEnumerator GoToMap(QAScenarioContext ctx, QAFlag ok)
    {
        ok.Value = false;
        if (QALobbyFlow.InMapWithPlayer)
        {
            ok.Value = true;
            yield break;
        }

        var lobby = new QAFlag();
        yield return EnsureLobbyPlayer(ctx, 120f, lobby);
        if (!lobby.Value)
            yield break;

        yield return CloseResultPanelIfOpen(ctx, null);
        yield return QALobbyFlow.ReadyUp(ctx.Input);
        yield return ctx.Wait(() => QALobbyFlow.InMapWithPlayer, 120f);
        ok.Value = !ctx.TimedOut;
        if (!ok.Value)
            ctx.Fail("맵 진입(준비 단계)", $"준비 뒤 120초 안에 맵 플레이어 없음 ready={QALobbyFlow.IsLocalReady()} scene={QAUtil.ActiveSceneName()}", ctx.Shot("map_timeout"));
    }

    /// <summary>결과 패널이 열려 있으면 ESC → 안 닫히면 Hide. itemName 이 있으면 ESC 결과를 항목으로 남긴다.</summary>
    private static IEnumerator CloseResultPanelIfOpen(QAScenarioContext ctx, string itemName)
    {
        LobbyResultPanel panel = Object.FindFirstObjectByType<LobbyResultPanel>();
        if (panel == null || !panel.IsOpen)
            yield break;

        yield return ctx.Input.PressKey(UnityEngine.InputSystem.Key.Escape);
        yield return ctx.Wait(() => panel == null || !panel.IsOpen, 1.5f);
        bool viaEsc = !ctx.TimedOut;
        if (!viaEsc && panel != null)
            panel.Hide();
        if (itemName != null)
        {
            if (viaEsc)
                ctx.Pass(itemName, "ESC 키 주입으로 닫힘");
            else
                ctx.Eye(itemName, "ESC 키 주입 미반영(창 포커스/배경 입력) — Hide() 로 닫음. ESC 는 사람이 확인");
        }
    }

    /// <summary>
    /// 이동·대시·평타·스킬 4슬롯 한 번씩. 스킬은 앞 스킬이 끝나고 쿨다운이 돌 때까지(최대 8초) 기다렸다 쓴다 —
    /// 1차(48-20261020_143105)의 Sub·Interrupt=False 는 Main 실행 중(IsBusy)에 1.2초 간격으로 눌러 Cast 가 조기 거부한 것이었다.
    /// unexpectedFalse = 막힐 사유가 없는데 false 가 나온 슬롯 수(FAIL 대상). 사유가 있는 거부는 근거로만 남긴다.
    /// </summary>
    private static IEnumerator CombatSample(QAScenarioContext ctx, StringBuilder log, QACount unexpectedFalse = null)
    {
        Player p = Player.LocalPlayer;
        if (p == null)
            yield break;
        QAInputController input = ctx.Input;
        input.SetTarget(p);

        log.Append($"dash={input.Dash()} ");
        yield return new WaitForSeconds(0.8f);
        for (int i = 0; i < 3; i++)
        {
            log.Append($"atk{i}={input.Attack()} ");
            yield return new WaitForSeconds(0.5f);
        }

        foreach (PlayerSkillSlot slot in System.Enum.GetValues(typeof(PlayerSkillSlot)))
        {
            p = Player.LocalPlayer;
            if (p == null)
                break;
            input.SetTarget(p);
            yield return ctx.Wait(() => input.SkillBlockReason(slot) == null, 8f);
            string reason = input.SkillBlockReason(slot);
            p = Player.LocalPlayer;
            if (p == null)
                break;
            Vector3 aim = p.transform.position + p.transform.forward * 3f;
            bool used = input.UseSkillAt(slot, aim);
            log.Append(used ? $"{slot}=True " : $"{slot}=False({reason ?? "사유 없음"}) ");
            if (!used && reason == null && unexpectedFalse != null)
                unexpectedFalse.Value++;
            yield return new WaitForSeconds(0.5f);
        }
    }

    private static string Who()
    {
        NetworkManager nm = NetworkManager.Singleton;
        return nm == null ? "nm=null" : $"client={nm.LocalClientId} server={nm.IsServer}";
    }

    // ── S1 솔로 부팅 → 로비 직행·스폰·카메라·이동/대시/평타/스킬 ─────────

    private static IEnumerator S1(QAScenarioContext ctx)
    {
        QAScenarioContext.ErrorMark mark = ctx.MarkErrors();

        yield return ctx.Wait(() => QALobbyFlow.InLobby && QALobbyFlow.IsConnected, 90f);
        bool sawTitle = ctx.SceneLog.Contains("1.TitleScene");
        string scenes = string.Join("→", ctx.SceneLog);
        if (ctx.TimedOut)
        {
            ctx.Fail("부팅→로비 직행", $"90초 안에 로비 없음. 씬={scenes}", ctx.Shot("boot"));
            yield break;
        }
        ctx.Check(!sawTitle && DemoSessionDirector.IsDemoFlowActive && QALobbyFlow.IsServer,
            "부팅→로비 직행(타이틀 없음·호스트)",
            $"{ctx.LastWaitSeconds:F1}s, 씬={scenes}, demoFlow={DemoSessionDirector.IsDemoFlowActive}, {Who()}");

        yield return ctx.Wait(() => QALobbyFlow.HasLocalLobbyPlayer, 30f);
        if (ctx.TimedOut)
        {
            ctx.Fail("로비 스폰", "30초 안에 Player.LocalPlayer 없음", ctx.Shot("spawn"));
            yield break;
        }
        Player p = Player.LocalPlayer;
        int slot = QALobbyFlow.NearestSpawnIndex(p.transform.position, out float d);
        ctx.Check(slot >= 0 && d < 1.5f, "로비 스폰",
            $"{QALobbyFlow.CleanName(p)} pos={p.transform.position} 가장 가까운 SpawnPoint={slot}({d:F2}m)", ctx.Shot("spawn"));

        var probe = new QACameraProbeResult();
        yield return QALobbyFlow.CameraFollowProbe(ctx.Input, probe);
        ctx.Check(probe.Ok, "카메라 추적", probe.Evidence);

        var log = new StringBuilder();
        var unexpected = new QACount();
        QAScenarioContext.ErrorMark combatMark = ctx.MarkErrors();
        yield return CombatSample(ctx, log, unexpected);
        int combatErrors = ctx.ErrorsSince(combatMark, out string combatSummary);
        ctx.Check(combatErrors == 0, "이동/대시/평타/스킬 예외 0", $"반환값: {log}| 에러 {combatErrors}건: {combatSummary}", ctx.Shot("combat"));
        ctx.Check(unexpected.Value == 0, "스킬 4슬롯 시전(로비 제한 없음)",
            $"사유 없는 거부 {unexpected.Value}건. {log}— 로비 스킬 제한은 lobby-demo-flow.md 에 없다(제한 없음이 사양)");

        int errors = ctx.ErrorsSince(mark, out string summary);
        ctx.Check(errors == 0, "S1 구간 에러 로그 0", $"{errors}건: {summary}");
    }

    // ── S3 준비 패드 F → 1/1 → 5초 → Map 1회 진입 ─────────────────────

    private static IEnumerator S3(QAScenarioContext ctx)
    {
        var ok = new QAFlag();
        yield return EnsureLobbyPlayer(ctx, 90f, ok);
        if (!ok.Value)
            yield break;
        yield return CloseResultPanelIfOpen(ctx, null);

        if (QALobbyFlow.IsLocalReady())
        {
            // 이전 단계에서 켜져 있으면 끄고 시작 — 토글이라 F 한 번이 해제다.
            LobbyInteractable pad0 = QALobbyFlow.FindInteractable(LobbyInteractableKind.Ready, Player.LocalPlayer.transform.position);
            if (pad0 != null)
            {
                yield return QALobbyFlow.WalkTo(ctx.Input, pad0.Position, pad0.Radius * 0.5f, 20f);
                yield return QALobbyFlow.PressInteract(ctx.Input, () => !QALobbyFlow.IsLocalReady(), () => LobbySession.Active?.RequestToggleReady());
            }
        }

        LobbyInteractable pad = QALobbyFlow.FindInteractable(LobbyInteractableKind.Ready, Player.LocalPlayer.transform.position);
        if (pad == null)
        {
            ctx.Fail("준비 패드 찾기", "LobbyInteractable(Ready) 없음", ctx.Shot("no_pad"));
            yield break;
        }

        yield return QALobbyFlow.WalkTo(ctx.Input, pad.Position, pad.Radius * 0.5f, 20f);
        float dist = Player.LocalPlayer != null ? NearestInteractable.PlanarDistance(Player.LocalPlayer.transform.position, pad.Position) : -1f;
        ctx.Check(QALobbyFlow.LastWalkArrived, "준비 패드까지 걷기", $"거리 {dist:F2}m / 반경 {pad.Radius:F2}m");

        // 카운트다운을 놓치지 않게 F 와 동시에 시작·씬 진입을 따로 잰다.
        int mapLoads = 0;
        float countdownSeenAt = -1f;
        float mapAt = -1f;
        UnityEngine.Events.UnityAction<Scene, LoadSceneMode> onLoaded = (s, m) =>
        {
            if (s.name == QALobbyFlow.MapScene)
            {
                mapLoads++;
                if (mapAt < 0f) mapAt = Time.realtimeSinceStartup;
            }
        };
        SceneManager.sceneLoaded += onLoaded;
        try
        {
            yield return QALobbyFlow.PressInteract(ctx.Input, QALobbyFlow.IsLocalReady, () => LobbySession.Active?.RequestToggleReady());
            bool ready = QALobbyFlow.IsLocalReady();
            int readyCount = QALobbyFlow.ReadyCount(out int total);
            string readyShot = ctx.Shot("ready");
            if (!ready)
                ctx.Fail("준비 패드 F → 준비", $"준비 안 켜짐(키·RPC 모두). 패드 거리 {dist:F2}m", readyShot);
            else if (QALobbyFlow.LastInteractViaKey)
                ctx.Pass("준비 패드 F → 준비", "F 키 주입으로 준비 켜짐", readyShot);
            else
                ctx.Eye("준비 패드 F → 준비", "F 키 주입 미반영 → 같은 RPC 로 준비 켜짐(서버 거리 검증 통과). F 키 경로는 사람이 확인", readyShot);
            ctx.Check(readyCount == 1 && total == 1, "준비 1/1", $"준비 {readyCount}/{total} (라벨 모양은 스크린샷)", readyShot);

            yield return ctx.Wait(() => LobbySession.Active == null || LobbySession.Active.IsCountdownActive || mapLoads > 0, 5f);
            if (LobbySession.Active != null && LobbySession.Active.IsCountdownActive)
            {
                countdownSeenAt = Time.realtimeSinceStartup;
                string cdShot = ctx.Shot("countdown");
                ctx.Pass("카운트다운 시작", $"남은 {LobbySession.Active.CountdownRemainingSeconds:F1}s", cdShot);
            }
            else
            {
                ctx.Fail("카운트다운 시작", $"준비 뒤 5초 안에 카운트다운 없음 scene={QAUtil.ActiveSceneName()}", ctx.Shot("no_countdown"));
            }

            yield return ctx.Wait(() => QALobbyFlow.InMapWithPlayer, 60f);
            if (ctx.TimedOut)
            {
                ctx.Fail("Map 진입", $"60초 안에 맵 플레이어 없음 scene={QAUtil.ActiveSceneName()}", ctx.Shot("map_timeout"));
                yield break;
            }

            if (countdownSeenAt > 0f && mapAt > 0f)
            {
                float secs = mapAt - countdownSeenAt;
                // 카운트다운 5초 + 로딩 씬. 로딩 시간이 섞이므로 하한(≥4s)만 자동 판정, 상한은 근거로 남긴다.
                ctx.Check(secs >= 4f, "5초 카운트다운 후 진입", $"카운트다운 관측→맵 로드 {secs:F1}s(로딩 포함)");
            }

            // 맵이 한 번 더 로드되는지 잠깐 지켜본다.
            yield return new WaitForSecondsRealtime(5f);
            ctx.Check(mapLoads == 1, "Map 1회 진입", $"4.MapScene sceneLoaded {mapLoads}회, 캐릭터={QALobbyFlow.CleanName(Player.LocalPlayer)}", ctx.Shot("map"));
        }
        finally
        {
            SceneManager.sceneLoaded -= onLoaded;
        }
    }

    // ── S5 판 종료 → 로비 결과 패널·캐릭터 유지·준비 해제, 3판 루프 ────────

    private static IEnumerator S5(QAScenarioContext ctx)
    {
        QAScenarioContext.ErrorMark loopMark = ctx.MarkErrors();
        QAResultPath[] paths = ctx.EndPaths;
        var flow = new QAResultFlow();

        for (int round = 1; round <= 3; round++)
        {
            QAResultPath path = paths[(round - 1) % paths.Length];
            string tag = $"{round}판({path})";

            var inMap = new QAFlag();
            yield return GoToMap(ctx, inMap);
            if (!inMap.Value)
            {
                ctx.Fail($"{tag} 시작", "맵에 못 들어감 — 루프 중단");
                break;
            }

            string character = QALobbyFlow.CleanName(Player.LocalPlayer);
            yield return new WaitForSeconds(3f);

            if (!QALobbyFlow.IsServer)
            {
                ctx.Skip($"{tag} 판 종료", "호스트 전용 — 클라는 호스트 종료를 기다린다");
                yield return ctx.Wait(() => QALobbyFlow.HasLocalLobbyPlayer, 400f);
            }
            else
            {
                flow.Begin();
                yield return flow.DriveHost(path, ctx.Recorder);
                if (path == QAResultPath.Wiped && flow.WipeOutcome != QAWipeOutcome.Result)
                {
                    // 전멸이 설계상 결과로 안 간다(party-lives-respawn.md §1 — 보스 미격파 전멸 = 리스폰, 튜토리얼 G11 전 = 차감 없는 자동 부활).
                    // 귀결을 판정하고 판은 나가기로 끝낸다 — 로비 복귀·결과 패널 단언은 그대로 이어서 본다.
                    bool designed = flow.WipeOutcome == QAWipeOutcome.Respawned || flow.WipeOutcome == QAWipeOutcome.TutorialAutoRevive;
                    ctx.Check(designed, $"{tag} 전멸 처리(설계 귀결)",
                        $"귀결={flow.WipeOutcome} — 결과 화면 대신 {(flow.WipeOutcome == QAWipeOutcome.Respawned ? "전멸 리스폰" : flow.WipeOutcome == QAWipeOutcome.TutorialAutoRevive ? "튜토리얼 자동 부활(전멸 불가)" : "귀결 없음")}. 판 종료는 나가기로 대체",
                        ctx.Shot($"r{round}_wipe"));
                    GameManager.GoToResultButton();
                }
                yield return ctx.Wait(() => QALobbyFlow.HasLocalLobbyPlayer, 300f);
                flow.End();
            }

            if (ctx.TimedOut)
            {
                ctx.Fail($"{tag} 로비 복귀", $"판 종료 뒤 로비 플레이어 없음 scene={QAUtil.ActiveSceneName()} result={SessionResult.HasValue}", ctx.Shot($"r{round}_no_lobby"));
                break;
            }
            ctx.Pass($"{tag} 로비 복귀", $"{ctx.LastWaitSeconds:F1}s, outcome={(SessionResult.HasValue ? SessionResult.Outcome.ToString() : "없음")}");

            LobbyResultPanel panel = null;
            yield return ctx.Wait(() => (panel = Object.FindFirstObjectByType<LobbyResultPanel>()) != null && panel.IsOpen, 10f);
            yield return new WaitForSecondsRealtime(1f); // 행 복제
            string panelShot = ctx.Shot($"r{round}_result");
            ctx.Check(!ctx.TimedOut, $"{tag} 결과 패널 열림", panel == null ? "LobbyResultPanel 없음" : $"IsOpen={panel.IsOpen} HasResult={panel.HasResult}(모양·180초 자동 닫힘은 눈확인)", panelShot);

            string after = QALobbyFlow.CleanName(Player.LocalPlayer);
            ctx.Check(after == character, $"{tag} 캐릭터 유지", $"판 전 {character} → 로비 {after}");
            ctx.Check(!QALobbyFlow.IsLocalReady(), $"{tag} 준비 해제", $"ready={QALobbyFlow.IsLocalReady()}");

            yield return CloseResultPanelIfOpen(ctx, round == 1 ? $"{tag} 결과 패널 ESC 닫기" : null);
        }

        int errors = ctx.ErrorsSince(loopMark, out string summary);
        ctx.Check(errors == 0, "3판 루프 누적 에러 로그 0", $"{errors}건: {summary}");
    }

    // ── M1 MPPM 자동 접속·늦은 클라 SpawnPoint_1·2·슬롯·각자 카메라 ──────

    private static IEnumerator M1(QAScenarioContext ctx)
    {
        QAScenarioContext.ErrorMark mark = ctx.MarkErrors();
        int expected = ctx.ExpectedPlayers;

        yield return ctx.Wait(() => QALobbyFlow.IsConnected && QALobbyFlow.HasLocalLobbyPlayer, 180f);
        if (ctx.TimedOut)
        {
            ctx.Fail("자동 접속·로비 입장", $"180초 안에 로비 플레이어 없음 scene={QAUtil.ActiveSceneName()} {Who()} demoFlow={DemoSessionDirector.IsDemoFlowActive}", ctx.Shot("connect"));
            yield break;
        }
        ctx.Check(DemoSessionDirector.IsDemoFlowActive, "자동 접속·로비 입장(버튼 없음)",
            $"{ctx.LastWaitSeconds:F1}s, {Who()}, demoFlow={DemoSessionDirector.IsDemoFlowActive}, 씬={string.Join("→", ctx.SceneLog)}");

        // 전원이 로비에 설 때까지 — 스폰 위치는 아무도 움직이기 전에 본다.
        yield return ctx.Wait(() => QALobbyFlow.SpawnedPlayers().Count >= expected, 180f);
        List<Player> players = QALobbyFlow.SpawnedPlayers();
        if (ctx.TimedOut)
        {
            ctx.Fail($"{expected}인 로비 집결", $"플레이어 {players.Count}/{expected}", ctx.Shot("gather"));
            yield break;
        }

        var sb = new StringBuilder();
        bool slotsOk = true;
        var used = new HashSet<int>();
        foreach (Player pl in players)
        {
            int idx = QALobbyFlow.NearestSpawnIndex(pl.transform.position, out float d);
            sb.Append($"c{pl.OwnerClientId}:{QALobbyFlow.CleanName(pl)}→SpawnPoint_{idx}({d:F2}m) ");
            if (idx < 0 || d > 1.5f || !used.Add(idx))
                slotsOk = false;
        }
        // 접속 순서 = clientId 오름차순. 호스트(0) = 0번, 늦은 클라 = 1·2번.
        for (int i = 0; i < players.Count && slotsOk; i++)
        {
            int idx = QALobbyFlow.NearestSpawnIndex(players[i].transform.position, out _);
            if (idx != i)
                slotsOk = false;
        }
        string gatherShot = ctx.Shot("gather");
        if (QALobbyFlow.IsServer)
            ctx.Check(slotsOk, "늦은 클라 SpawnPoint_1·2", sb.ToString(), gatherShot);
        else
            ctx.Eye("늦은 클라 SpawnPoint_1·2(클라 관측)", sb.ToString() + "— 판정은 호스트 리포트", gatherShot);

        LobbySession session = LobbySession.Active;
        int entries = session != null ? session.EntryCount : 0;
        ctx.Check(entries == expected, "로비 슬롯 행 수", $"LobbySession 자리 {entries}/{expected} (슬롯 초상화·이름 모양은 스크린샷 눈확인)", gatherShot);

        // 호스트가 위치를 다 본 뒤 각자 움직인다.
        yield return new WaitForSecondsRealtime(4f);
        var probe = new QACameraProbeResult();
        yield return QALobbyFlow.CameraFollowProbe(ctx.Input, probe);
        ctx.Check(probe.Ok, "각자 카메라 추적", $"{Who()} {probe.Evidence}", ctx.Shot("camera"));

        int errors = ctx.ErrorsSince(mark, out string summary);
        ctx.Check(errors == 0, "M1 구간 에러 로그 0", $"{errors}건: {summary}");
    }

    // ── M4 판 도중 재접속 → 합류 문구 → 첫 스폰·같은 캐릭터·BGM/HUD/카메라 ─────

    private static IEnumerator M4(QAScenarioContext ctx)
    {
        int expected = ctx.ExpectedPlayers;
        var inMap = new QAFlag();
        yield return GoToMap(ctx, inMap);
        if (!inMap.Value)
            yield break;

        yield return ctx.Wait(() => QALobbyFlow.SpawnedPlayers().Count >= expected, 90f);
        List<Player> players = QALobbyFlow.SpawnedPlayers();
        if (ctx.TimedOut || expected < 2)
        {
            ctx.Fail("맵 집결", $"플레이어 {players.Count}/{expected} — 재접속 대상 없음");
            yield break;
        }

        // 재접속 역할 = 호스트(0) 아닌 가장 작은 clientId.
        Player rejoiner = null;
        foreach (Player pl in players)
            if (pl.OwnerClientId != NetworkManager.ServerClientId && (rejoiner == null || pl.OwnerClientId < rejoiner.OwnerClientId))
                rejoiner = pl;
        ulong rejoinId = rejoiner.OwnerClientId;
        string rejoinCharacter = QALobbyFlow.CleanName(rejoiner);
        bool iAmRejoiner = !QALobbyFlow.IsServer && NetworkManager.Singleton.LocalClientId == rejoinId;
        QAScenarioContext.ErrorMark mark = ctx.MarkErrors();

        yield return new WaitForSecondsRealtime(5f);

        var originalIds = new HashSet<ulong>();
        foreach (Player pl in players)
            originalIds.Add(pl.OwnerClientId);

        if (iAmRejoiner)
            yield return M4Rejoiner(ctx, rejoinCharacter);
        else
            yield return M4Observer(ctx, rejoinId, rejoinCharacter, expected, originalIds);

        int errors = ctx.ErrorsSince(mark, out string summary);
        ctx.Check(errors == 0, iAmRejoiner ? "재접속 구간 예외 0(재접속 클라)" : "재접속 구간 예외 0(관측 피어)", $"{errors}건: {summary}");
    }

    private static IEnumerator M4Rejoiner(QAScenarioContext ctx, string character)
    {
        string joining = DemoConnectUiStrings.JoiningMatch.Text;
        var seenTexts = new HashSet<string>();
        var scenesBefore = ctx.SceneLog.Count;

        Debug.Log("[QA][M4] 클라 Shutdown — 끊김 흉내(DemoSessionDirector 재접속 루프에 맡긴다)");
        NetworkManager.Singleton.Shutdown();
        float t0 = Time.realtimeSinceStartup;

        // 끊긴 뒤 다시 맵에 내 플레이어가 설 때까지 화면 문구를 모은다.
        yield return ctx.Wait(() => !QALobbyFlow.IsConnected, 30f);
        float reconnectDeadline = Time.realtimeSinceStartup + 150f;
        while (Time.realtimeSinceStartup < reconnectDeadline && !(QALobbyFlow.IsConnected && QALobbyFlow.InMapWithPlayer))
        {
            QALobbyFlow.CollectTexts("합류", seenTexts);
            yield return null;
        }
        float elapsed = Time.realtimeSinceStartup - t0;
        string path = string.Join("→", ctx.SceneLog.GetRange(scenesBefore, ctx.SceneLog.Count - scenesBefore));

        if (!(QALobbyFlow.IsConnected && QALobbyFlow.InMapWithPlayer))
        {
            ctx.Fail("판 도중 재접속", $"150초 안에 맵 재합류 못 함 scene={QAUtil.ActiveSceneName()} 거친 씬={path} 문구={string.Join(" / ", seenTexts)}", ctx.Shot("rejoin_timeout"));
            yield break;
        }
        ctx.Pass("판 도중 재접속", $"끊김→맵 스폰 {elapsed:F1}s, 거친 씬={path}, {Who()}");

        bool sawJoining = seenTexts.Contains(joining) || seenTexts.Count > 0;
        if (sawJoining)
            ctx.Pass("합류 문구", string.Join(" / ", seenTexts));
        else
            ctx.Eye("합류 문구", $"'{joining}' 를 프레임 스캔으로 못 봄(스폰이 바로 돼 한 프레임도 안 떴을 수 있음) — 사람이 확인");

        Player p = Player.LocalPlayer;
        ctx.Eye("첫 스폰 지점", $"재합류 위치 {p.transform.position} — 맵 첫 스폰 지점인지 스크린샷으로 확인", ctx.Shot("rejoin_spawn"));
        ctx.Check(QALobbyFlow.CleanName(p) == character, "같은 캐릭터", $"끊기 전 {character} → 재합류 {QALobbyFlow.CleanName(p)}");

        yield return new WaitForSecondsRealtime(2f);
        var probe = new QACameraProbeResult();
        yield return QALobbyFlow.CameraFollowProbe(ctx.Input, probe);
        ctx.Check(probe.Ok, "재합류 카메라 추적", probe.Evidence);
        ctx.Eye("BGM·HUD", "BGM 재생·HUD 표시는 자동 판정 없음 — 스크린샷·소리 확인", ctx.Shot("rejoin_hud"));
    }

    private static IEnumerator M4Observer(QAScenarioContext ctx, ulong rejoinId, string character, int expected, HashSet<ulong> originalIds)
    {
        yield return ctx.Wait(() => !HasPlayer(rejoinId), 40f);
        bool sawDrop = !ctx.TimedOut;
        ctx.Check(sawDrop, "끊김 관측", sawDrop ? $"c{rejoinId} 플레이어 사라짐 {ctx.LastWaitSeconds:F1}s" : $"40초 안에 c{rejoinId} 가 안 사라짐");

        // 재접속하면 clientId 가 새로 붙는다 — 인원 수로 본다.
        yield return ctx.Wait(() => QALobbyFlow.SpawnedPlayers().Count >= expected, 150f);
        List<Player> players = QALobbyFlow.SpawnedPlayers();
        var sb = new StringBuilder();
        Player back = null;
        foreach (Player pl in players)
        {
            sb.Append($"c{pl.OwnerClientId}:{QALobbyFlow.CleanName(pl)}@{pl.transform.position} ");
            if (!originalIds.Contains(pl.OwnerClientId))
                back = pl;
        }
        if (ctx.TimedOut)
        {
            ctx.Fail("재합류 수용", $"150초 안에 {expected}인 복구 안 됨: {sb}", ctx.Shot("observer_timeout"));
            yield break;
        }
        ctx.Pass("재합류 수용", $"{ctx.LastWaitSeconds:F1}s 뒤 {players.Count}인: {sb}", ctx.Shot("observer"));
        if (back != null && QALobbyFlow.IsServer)
            ctx.Check(QALobbyFlow.CleanName(back) == character, "같은 캐릭터(호스트 관측)", $"끊기 전 {character} → 재합류 c{back.OwnerClientId} {QALobbyFlow.CleanName(back)}");
    }

    private static bool HasPlayer(ulong clientId)
    {
        foreach (Player p in QALobbyFlow.SpawnedPlayers())
            if (p.OwnerClientId == clientId)
                return true;
        return false;
    }
}

/// <summary>코루틴 결과 전달용 상자(코루틴은 out 을 못 쓴다).</summary>
public sealed class QAFlag
{
    public bool Value;
}

public sealed class QACount
{
    public int Value;
}
