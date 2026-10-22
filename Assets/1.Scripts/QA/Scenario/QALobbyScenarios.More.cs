using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 할 일 48 체크리스트 2차 시나리오(할 일 48.3) — S2·S4·M2·M3·M5·M6·M7·M8.
/// MPPM 역할은 시나리오 시작 때 3인이 모인 순간의 clientId 순서로 정한다: 0 = 호스트, 1 = 낮은 클라(A), 2 = 높은 클라(B).
/// 인스턴스끼리는 관측 가능한 상태(LobbySession 자리·준비·카운트다운·씬·플레이어 수)로만 맞춘다. 끊긴 쪽은 아무것도 못 보므로
/// 그쪽만 넉넉한 고정 대기를 쓴다(근거는 각 단계 주석).
/// 끊김 흉내 = NetworkManager.Shutdown. 재접속을 붙잡아 둘 땐 DemoSessionDirector.enabled=false — 재시도 루프가 Update 에서만 돈다.
/// </summary>
public static partial class QALobbyScenarios
{
    private static float MoreTimeLimit(string id)
    {
        switch (id)
        {
            case "S2": return 90f;
            case "S4": return 300f;
            case "M2": return 420f;
            case "M3": return 300f;
            case "M5": return 420f;
            case "M6": return 360f;
            case "M7": return 480f;
            case "M8": return 420f;
            case "P572": return 720f;
#if UNITY_EDITOR
            case Interrupt88Id: return QAInterruptSuite88.TimeLimitSeconds();
#endif
            default: return 60f;
        }
    }

    /// <summary>끊김·거절을 일부러 내는 시나리오 — 그 동안 네트워크 감지기의 접속 해제는 판정 제외(QABlackboard.ExpectDisconnects).</summary>
    public static bool SimulatesDisconnects(string id) =>
        id == "M2" || id == "M4" || id == "M5" || id == "M6" || id == "M7" || id == "M8";

    private static IEnumerator RunMore(string id, QAScenarioContext ctx)
    {
        switch (id)
        {
            case "S2": return S2(ctx);
            case "S4": return S4(ctx);
            case "M2": return M2(ctx);
            case "M3": return M3(ctx);
            case "M5": return M5(ctx);
            case "M6": return M6(ctx);
            case "M7": return M7(ctx);
            case "M8": return M8(ctx);
            case "P572": return P572(ctx);
#if UNITY_EDITOR
            case Interrupt88Id: return I88(ctx);
#endif
            default: return null;
        }
    }

    // ── 공통 도구 ─────────────────────────────────────────────────

    private static ulong LocalId => NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;

    /// <summary>재접속 루프를 붙잡거나(false) 놓는다(true). 붙잡은 채 시나리오가 끝나면 다음 시나리오가 접속을 못 하니 반드시 놓는다.</summary>
    private static void SetReconnect(bool allowed)
    {
        DemoSessionDirector d = Object.FindFirstObjectByType<DemoSessionDirector>();
        if (d != null)
            d.enabled = allowed;
        Debug.Log($"[QA] 재접속 루프 {(allowed ? "재개" : "보류")} (director={(d != null)})");
    }

    private static void Leave(bool holdReconnect)
    {
        if (holdReconnect)
            SetReconnect(false);
        Debug.Log($"[QA] 클라 Shutdown — 끊김 흉내(재접속 {(holdReconnect ? "보류" : "자동")})");
        NetworkManager.Singleton.Shutdown();
    }

    /// <summary>접속 승인 거절 사유(데모 규칙) — 없으면 None.</summary>
    private static DemoJoinRejectReason LastReject()
    {
        NetworkManager nm = NetworkManager.Singleton;
        return nm != null && DemoJoinRule.TryParseApprovalReason(nm.DisconnectReason, out DemoJoinRejectReason r) ? r : DemoJoinRejectReason.None;
    }

    /// <summary>3인이 모일 때까지 기다려 내 역할 번호(0·1·2)를 정한다. 못 모이면 -1.</summary>
    private static IEnumerator ResolveRole(QAScenarioContext ctx, QACount role, float timeout, bool needLobby)
    {
        role.Value = -1;
        int expected = ctx.ExpectedPlayers;
        yield return ctx.Wait(() => QALobbyFlow.IsConnected && (!needLobby || QALobbyFlow.HasLocalLobbyPlayer) &&
                                    QALobbyFlow.SpawnedPlayers().Count >= expected, timeout);
        if (ctx.TimedOut)
        {
            ctx.Fail($"{expected}인 집결", $"{timeout:F0}초 안에 {QALobbyFlow.SpawnedPlayers().Count}/{expected} scene={QAUtil.ActiveSceneName()} {Who()}", ctx.Shot("gather_fail"));
            yield break;
        }
        List<Player> players = QALobbyFlow.SpawnedPlayers();
        for (int i = 0; i < players.Count; i++)
            if (players[i].OwnerClientId == LocalId)
                role.Value = i;
        Debug.Log($"[QA][{ctx.Current}] 역할 {role.Value} ({Who()})");
    }

    private static Player PlayerOf(ulong clientId)
    {
        foreach (Player p in QALobbyFlow.SpawnedPlayers())
            if (p.OwnerClientId == clientId)
                return p;
        return null;
    }

    private static bool AllEntriesReady(int count)
    {
        int ready = QALobbyFlow.ReadyCount(out int total);
        return total == count && ready == count;
    }

    private static bool NoneReady()
    {
        return QALobbyFlow.ReadyCount(out int total) == 0 && total > 0;
    }

    /// <summary>
    /// 준비를 켜거나 끈다(패드까지 걸어가 F, 키가 안 먹으면 같은 RPC). 셋이 같은 패드 중심으로 가면 서로 막혀 반경 밖에 남으므로
    /// (2차 M2: A 가 준비 못 함) clientId 마다 패드 둘레 다른 자리(반경 절반, 120° 간격)로 간다.
    /// </summary>
    private static IEnumerator SetReady(QAScenarioContext ctx, bool ready)
    {
        if (!QALobbyFlow.HasLocalLobbyPlayer || QALobbyFlow.IsLocalReady() == ready)
            yield break;
        LobbyInteractable pad = QALobbyFlow.FindInteractable(LobbyInteractableKind.Ready, Player.LocalPlayer.transform.position);
        if (pad == null)
            yield break;
        float angle = (LocalId % 3) * 120f * Mathf.Deg2Rad;
        Vector3 spot = pad.Position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (pad.Radius * 0.5f);
        yield return QALobbyFlow.WalkTo(ctx.Input, spot, 0.3f, 20f);
        yield return QALobbyFlow.PressInteract(ctx.Input, () => QALobbyFlow.IsLocalReady() == ready, () => LobbySession.Active?.RequestToggleReady());
        QABlackboard.ScriptedControl = false;
    }

    /// <summary>호스트가 판을 나가기(GoToResultButton)로 끝내고 로비 복귀까지 기다린다. 클라는 로비 복귀만 기다린다.</summary>
    private static IEnumerator EndMatchToLobby(QAScenarioContext ctx, float timeout)
    {
        if (QALobbyFlow.IsServer && QAUtil.ActiveSceneName() == QALobbyFlow.MapScene)
            GameManager.GoToResultButton();
        yield return ctx.Wait(() => QALobbyFlow.HasLocalLobbyPlayer, timeout);
        yield return CloseResultPanelIfOpen(ctx, null);
    }

    private static CharacterRoster Roster()
    {
        if (LobbyUIController.Active != null && LobbyUIController.Active.Roster != null)
            return LobbyUIController.Active.Roster;
        NetworkLoadingFlowController flow = Object.FindFirstObjectByType<NetworkLoadingFlowController>();
        return flow != null ? flow.CharacterRoster : null;
    }

    private static List<int> AvailableCharacters(CharacterRoster roster)
    {
        var ids = new List<int>();
        if (roster == null)
            return ids;
        for (int i = 0; i < roster.Count; i++)
            if (roster.TryGetAvailableCharacter(i, out _))
                ids.Add(i);
        return ids;
    }

    private static string PrefabName(CharacterRoster roster, int id)
    {
        return roster != null && roster.TryGetAvailableCharacter(id, out CharacterRoster.Entry e) && e.PlayerPrefab != null ? e.PlayerPrefab.name : $"id{id}";
    }

    private static int LocalCharacterId()
    {
        return QALobbyFlow.TryGetLocalEntry(out LobbyPlayerEntry e) ? e.CharacterId : -1;
    }

    private static int CharacterIdOf(ulong clientId)
    {
        LobbySession s = LobbySession.Active;
        return s != null && s.TryGetEntry(clientId, out LobbyPlayerEntry e) ? e.CharacterId : -1;
    }

    // ── S2 추락 → 스폰 지점 복귀(피해 0) ─────────────────────────────

    private static IEnumerator S2(QAScenarioContext ctx)
    {
        var ok = new QAFlag();
        yield return EnsureLobbyPlayer(ctx, 90f, ok);
        if (!ok.Value)
            yield break;
        yield return CloseResultPanelIfOpen(ctx, null);
        if (!QALobbyFlow.IsServer)
        {
            ctx.Skip("추락 복귀", "호스트(솔로) 전용 — 서버에서 위치를 강제로 옮긴다");
            yield break;
        }

        QAScenarioContext.ErrorMark mark = ctx.MarkErrors();
        Player p = Player.LocalPlayer;
        Transform[] points = QALobbyFlow.LobbySpawnPoints();
        int slot = QALobbyFlow.NearestSpawnIndex(p.transform.position, out _);
        if (points == null || slot < 0)
        {
            ctx.Fail("스폰 지점", "LobbyPlayerSpawner.spawnPoints 없음");
            yield break;
        }
        Vector3 spawn = points[slot].position;

        // 스폰에서 조금 걸어 나간 뒤 떨어뜨린다 — 그래야 '스폰으로 복귀' 가 위치로 구분된다.
        yield return QALobbyFlow.WalkTo(ctx.Input, spawn + Vector3.right * 3f, 0.5f, 3f);
        QABlackboard.ScriptedControl = false;
        int hp0 = p.CurrentHealth;
        PlayerLifeState life0 = p.GetComponent<PlayerLifeCycleController>().State;

        // 로비 바닥 아래(fallRespawnY -5 아래)로 서버 텔레포트 = 떨어진 것과 같은 입력. 실제 낭떠러지 걷기는 맵 지오메트리 의존이라 쓰지 않는다.
        Vector3 drop = new Vector3(spawn.x + 3f, -12f, spawn.z);
        bool moved = p.GetComponent<PlayerFallRecovery>() != null && p.GetComponent<PlayerFallRecovery>().RelocateServer(drop);
        float minY = float.MaxValue;
        float t = 0f;
        bool returned = false;
        while (t < 8f)
        {
            p = Player.LocalPlayer;
            if (p != null)
            {
                minY = Mathf.Min(minY, p.transform.position.y);
                if (minY < -4f && NearestInteractable.PlanarDistance(p.transform.position, spawn) < 1.0f && p.transform.position.y > -1f)
                {
                    returned = true;
                    break;
                }
            }
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        p = Player.LocalPlayer;
        string shot = ctx.Shot("fall_return");
        ctx.Check(moved && returned, "추락 → 스폰 지점 복귀",
            $"relocate={moved} 최저 y={minY:F1} → {(p != null ? p.transform.position.ToString() : "없음")} 스폰 SpawnPoint_{slot} {spawn} ({t:F1}s)", shot);
        int hp1 = p != null ? p.CurrentHealth : -1;
        PlayerLifeState life1 = p != null ? p.GetComponent<PlayerLifeCycleController>().State : PlayerLifeState.PermanentDead;
        ctx.Check(hp1 == hp0 && life1 == PlayerLifeState.Alive, "추락 피해 0", $"HP {hp0} → {hp1}, 생명 {life0} → {life1}");

        int errors = ctx.ErrorsSince(mark, out string summary);
        ctx.Check(errors == 0, "S2 구간 에러 로그 0", $"{errors}건: {summary}");
    }

    // ── S4 선택 패드 F → 패널·입력 차단, 3종×3회·연속 10회 교체, 준비 중 잠금, 쿨다운 연타 ─────

    private static IEnumerator S4(QAScenarioContext ctx)
    {
        var ok = new QAFlag();
        yield return EnsureLobbyPlayer(ctx, 90f, ok);
        if (!ok.Value)
            yield break;
        yield return CloseResultPanelIfOpen(ctx, null);
        yield return SetReady(ctx, false);
        QAScenarioContext.ErrorMark mark = ctx.MarkErrors();

        CharacterRoster roster = Roster();
        List<int> avail = AvailableCharacters(roster);
        CharacterSelectPanel panel = Object.FindFirstObjectByType<CharacterSelectPanel>(FindObjectsInactive.Include);
        LobbyInteractable station = QALobbyFlow.FindInteractable(LobbyInteractableKind.CharacterSelect, Player.LocalPlayer.transform.position);
        if (panel == null || station == null || avail.Count < 2)
        {
            ctx.Fail("선택 패드·패널·로스터", $"panel={(panel != null)} station={(station != null)} 고를 수 있는 캐릭터 {avail.Count}종");
            yield break;
        }

        var rejects = new List<LobbyCharacterChangeReject>();
        System.Action<LobbyCharacterChangeReject> onReject = r => rejects.Add(r);
        LobbySession.CharacterRequestRejected += onReject;
        try
        {
            // 1) F → 패널·입력 차단
            yield return QALobbyFlow.WalkTo(ctx.Input, station.Position, station.Radius * 0.5f, 20f);
            QABlackboard.ScriptedControl = false;
            yield return QALobbyFlow.PressInteract(ctx.Input, () => panel.IsOpen, () => panel.Show(LocalCharacterId(), false));
            string panelShot = ctx.Shot("panel");
            if (!panel.IsOpen)
                ctx.Fail("선택 패드 F → 패널", "패널 안 열림(키·Show 모두)", panelShot);
            else if (QALobbyFlow.LastInteractViaKey)
                ctx.Pass("선택 패드 F → 패널", $"F 키 주입으로 열림, 포커스={panel.FocusedCharacterId}, 잠금={panel.IsLocked}", panelShot);
            else
                ctx.Eye("선택 패드 F → 패널", "F 키 미반영 → panel.Show 로 열었다. F 경로는 사람이 확인", panelShot);
            ctx.Check(UiInputGateManager.IsInputBlocked, "패널 열림 중 입력 차단", $"UiInputGateManager.IsInputBlocked={UiInputGateManager.IsInputBlocked}");
            panel.Hide();
            yield return null;
            ctx.Check(!UiInputGateManager.IsInputBlocked, "패널 닫힘 뒤 입력 차단 해제", $"IsInputBlocked={UiInputGateManager.IsInputBlocked}");

            // 2) 패널 경로로 3종×3회(9회) — 매번 F → 포커스 이동 → 확정.
            var perCharacter = new Dictionary<int, int>();
            int panelOk = 0;
            var sb = new StringBuilder();
            int kinds = Mathf.Min(3, avail.Count);
            for (int k = 0; k < 9; k++)
            {
                // 앞 kinds 종을 순환 — 지금 캐릭터의 다음 칸(3종이면 9회에 종류별 3회).
                int cur = avail.IndexOf(LocalCharacterId());
                int target = avail[(cur < 0 || cur >= kinds ? 0 : cur + 1) % kinds];
                var swapped = new QAFlag();
                yield return SwapViaPanel(ctx, panel, station, roster, target, swapped);
                sb.Append($"{k + 1}:{PrefabName(roster, target)}={(swapped.Value ? "O" : "X")} ");
                if (swapped.Value)
                {
                    panelOk++;
                    perCharacter.TryGetValue(target, out int c);
                    perCharacter[target] = c + 1;
                }
                if (k == 2)
                    ctx.Shot("swap3");
            }
            ctx.Check(panelOk == 9, $"패널로 {kinds}종×3회 교체", $"{panelOk}/9 성공, 종류별 {Join(perCharacter, roster)} | {sb}", ctx.Shot("swap9"));

            // 3) 연속 10회 — 같은 서버 RPC(패널 확정이 부르는 RequestCharacter)를 쿨다운(0.5초) 지나 바로바로.
            int burstOk = 0;
            int a = avail[0], b = avail[1];
            for (int i = 0; i < 10; i++)
            {
                int target = LocalCharacterId() == a ? b : a;
                LobbySession.Active?.RequestCharacter(target);
                yield return ctx.Wait(() => LocalCharacterId() == target && Player.LocalPlayer != null &&
                                            QALobbyFlow.CleanName(Player.LocalPlayer) == PrefabName(roster, target), 5f);
                if (!ctx.TimedOut)
                    burstOk++;
                yield return new WaitForSecondsRealtime(0.6f);
            }
            int errorsBurst = ctx.ErrorsSince(mark, out string burstSummary);
            ctx.Check(burstOk == 10, "연속 10회 교체", $"{burstOk}/10 성공, 지금까지 에러 {errorsBurst}건: {burstSummary}");

            // 4) 쿨다운 연타 — 교체 직후 다른 캐릭터를 곧바로 요청 → 서버가 거부(Cooldown·SwapInProgress)하고 첫 교체만 남아야 한다.
            rejects.Clear();
            int current = LocalCharacterId();
            int first = current == a ? b : a;
            int second = avail.Find(x => x != first && x != current);
            if (second == 0 && (first == 0 || current == 0))
                second = current; // 2종뿐이면 원래 캐릭터로 되돌리는 요청 — 그래도 쿨다운에 걸려야 한다
            LobbySession.Active?.RequestCharacter(first);
            yield return null;
            LobbySession.Active?.RequestCharacter(second);
            yield return null;
            LobbySession.Active?.RequestCharacter(second);
            yield return new WaitForSecondsRealtime(2f);
            bool cooldownRejected = rejects.Contains(LobbyCharacterChangeReject.Cooldown) || rejects.Contains(LobbyCharacterChangeReject.SwapInProgress);
            ctx.Check(cooldownRejected && LocalCharacterId() == first, "쿨다운 연타 거부",
                $"요청 {PrefabName(roster, first)}→{PrefabName(roster, second)}×2, 거부={string.Join(",", rejects)}, 최종={PrefabName(roster, LocalCharacterId())} (화면 피드백 없음·로그만 = 문서 §7)", ctx.Shot("cooldown"));
            yield return new WaitForSecondsRealtime(0.6f);

            // 5) 준비 중 잠금 — 솔로는 준비하면 바로 5초 카운트다운이라 서버 텔레포트로 패드↔선택대를 오간다.
            yield return ReadyLockCheck(ctx, panel, station, roster, avail, rejects);
        }
        finally
        {
            LobbySession.CharacterRequestRejected -= onReject;
            if (panel != null && panel.IsOpen)
                panel.Hide();
        }

        int errors = ctx.ErrorsSince(mark, out string summary);
        ctx.Check(errors == 0, "S4 구간 에러 로그 0", $"{errors}건: {summary}");
    }

    private static string Join(Dictionary<int, int> counts, CharacterRoster roster)
    {
        var parts = new List<string>();
        foreach (KeyValuePair<int, int> kv in counts)
            parts.Add($"{PrefabName(roster, kv.Key)}×{kv.Value}");
        return string.Join(" ", parts);
    }

    private static IEnumerator SwapViaPanel(QAScenarioContext ctx, CharacterSelectPanel panel, LobbyInteractable station, CharacterRoster roster, int target, QAFlag swapped)
    {
        swapped.Value = false;
        if (Player.LocalPlayer == null)
            yield break;
        if (NearestInteractable.PlanarDistance(Player.LocalPlayer.transform.position, station.Position) > station.Radius * 0.8f)
        {
            yield return QALobbyFlow.WalkTo(ctx.Input, station.Position, station.Radius * 0.5f, 10f);
            QABlackboard.ScriptedControl = false;
        }
        if (!panel.IsOpen)
            yield return QALobbyFlow.PressInteract(ctx.Input, () => panel.IsOpen, () => panel.Show(LocalCharacterId(), false));
        if (!panel.IsOpen)
            yield break;

        for (int i = 0; i < 8 && panel.FocusedCharacterId != target; i++)
            panel.MoveFocus(1);
        CharacterSelectConfirmResult r = panel.Confirm();
        if (r != CharacterSelectConfirmResult.Chosen)
        {
            Debug.LogWarning($"[QA][S4] 확정 결과 {r} (포커스 {panel.FocusedCharacterId}, 목표 {target})");
            if (panel.IsOpen)
                panel.Hide();
            yield break;
        }
        yield return ctx.Wait(() => LocalCharacterId() == target && Player.LocalPlayer != null &&
                                    QALobbyFlow.CleanName(Player.LocalPlayer) == PrefabName(roster, target), 5f);
        swapped.Value = !ctx.TimedOut;
        yield return new WaitForSecondsRealtime(0.6f); // 교체 쿨다운 0.5초(LobbyRules)
    }

    private static IEnumerator ReadyLockCheck(QAScenarioContext ctx, CharacterSelectPanel panel, LobbyInteractable station, CharacterRoster roster,
                                             List<int> avail, List<LobbyCharacterChangeReject> rejects)
    {
        if (!QALobbyFlow.IsServer)
        {
            ctx.Skip("준비 중 잠금", "솔로 호스트 전용(서버 텔레포트)");
            yield break;
        }
        LobbyInteractable pad = QALobbyFlow.FindInteractable(LobbyInteractableKind.Ready, Player.LocalPlayer.transform.position);
        PlayerFallRecovery rec = Player.LocalPlayer.GetComponent<PlayerFallRecovery>();
        if (pad == null || rec == null)
        {
            ctx.Fail("준비 중 잠금", "준비 패드·PlayerFallRecovery 없음");
            yield break;
        }

        rec.RelocateServer(pad.Position);
        yield return new WaitForSecondsRealtime(0.3f);
        yield return QALobbyFlow.PressInteract(ctx.Input, QALobbyFlow.IsLocalReady, () => LobbySession.Active?.RequestToggleReady());
        if (!QALobbyFlow.IsLocalReady())
        {
            ctx.Fail("준비 중 잠금", "준비가 안 켜짐");
            yield break;
        }

        rec = Player.LocalPlayer.GetComponent<PlayerFallRecovery>();
        rec.RelocateServer(station.Position);
        yield return new WaitForSecondsRealtime(0.3f);
        rejects.Clear();
        int before = LocalCharacterId();
        yield return QALobbyFlow.PressInteract(ctx.Input, () => panel.IsOpen, () => panel.Show(before, true));
        bool lockedPanel = panel.IsOpen && panel.IsLocked;
        for (int i = 0; i < 8 && panel.FocusedCharacterId == before; i++)
            panel.MoveFocus(1);
        CharacterSelectConfirmResult confirm = panel.IsOpen ? panel.Confirm() : CharacterSelectConfirmResult.NotOpen;
        string lockShot = ctx.Shot("ready_locked");
        int other = avail[0] == before ? avail[1] : avail[0];
        LobbySession.Active?.RequestCharacter(other);
        yield return new WaitForSecondsRealtime(0.6f);
        bool serverRejected = rejects.Contains(LobbyCharacterChangeReject.Ready) || rejects.Contains(LobbyCharacterChangeReject.CountdownActive);
        ctx.Check(lockedPanel && confirm == CharacterSelectConfirmResult.Locked && serverRejected && LocalCharacterId() == before,
            "준비 중 잠금(패널·서버)", $"패널 잠금={lockedPanel} 확정={confirm} 서버 거부={string.Join(",", rejects)} 캐릭터 {PrefabName(roster, before)}→{PrefabName(roster, LocalCharacterId())} (잠금 문구 모양은 스크린샷)", lockShot);
        if (panel.IsOpen)
            panel.Hide();

        // 패드로 돌아가 준비 해제 → 카운트다운 취소. 늦어서 맵으로 갔으면 나가기로 로비 복귀.
        rec = Player.LocalPlayer != null ? Player.LocalPlayer.GetComponent<PlayerFallRecovery>() : null;
        if (rec != null && QALobbyFlow.InLobby)
        {
            rec.RelocateServer(pad.Position);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return QALobbyFlow.PressInteract(ctx.Input, () => !QALobbyFlow.IsLocalReady(), () => LobbySession.Active?.RequestToggleReady());
        }
        yield return new WaitForSecondsRealtime(1f);
        if (!QALobbyFlow.InLobby || (LobbySession.Active != null && LobbySession.Active.IsCountdownActive))
        {
            yield return ctx.Wait(() => QALobbyFlow.InMapWithPlayer, 60f);
            yield return EndMatchToLobby(ctx, 120f);
        }
    }

    // ── M2 새 접속 Ready 초기화·해제/퇴장 카운트다운 취소·카운트다운 중 접속 거절 ─────

    private static IEnumerator M2(QAScenarioContext ctx)
    {
        var role = new QACount();
        yield return ResolveRole(ctx, role, 180f, true);
        if (role.Value < 0)
            yield break;
        yield return CloseResultPanelIfOpen(ctx, null);
        yield return SetReady(ctx, false);
        yield return ctx.Wait(() => NoneReady(), 20f);

        if (role.Value == 0) yield return M2Host(ctx);
        else if (role.Value == 1) yield return M2ClientA(ctx);
        else yield return M2ClientB(ctx);

        // 마무리 — 전원 로비(B 가 늦게 들어올 수 있다).
        yield return ctx.Wait(() => QALobbyFlow.HasLocalLobbyPlayer && QALobbyFlow.SpawnedPlayers().Count >= ctx.ExpectedPlayers, 150f);
        if (ctx.TimedOut)
            ctx.Fail("M2 끝 — 3인 로비 복구", $"{QALobbyFlow.SpawnedPlayers().Count}명 scene={QAUtil.ActiveSceneName()}", ctx.Shot("end"));
    }

    // 호스트: 판정 대부분. 단계 = A: B 퇴장 중 호스트 준비 → B 재접속 시 준비 초기화 / B: 3/3 → A 해제 → 취소 / C: 3/3 → B 퇴장 → 취소·초기화 / D: 2/2 카운트다운 중 B 거절.
    private static IEnumerator M2Host(QAScenarioContext ctx)
    {
        // A) B 가 나가면(자리 2) 호스트만 준비(A 는 안 함 → 카운트다운 없음, 안정 상태) → B 가 다시 들어오면 내 준비가 풀려야 한다.
        yield return ctx.Wait(() => LobbySession.Active != null && LobbySession.Active.EntryCount == 2, 60f);
        if (ctx.TimedOut) { ctx.Fail("A) B 퇴장 관측", "60초 안에 자리 2 안 됨"); yield break; }
        yield return SetReady(ctx, true);
        bool hostReady = QALobbyFlow.IsLocalReady();
        yield return ctx.Wait(() => LobbySession.Active != null && LobbySession.Active.EntryCount == 3, 60f);
        bool joined = !ctx.TimedOut;
        yield return new WaitForSecondsRealtime(0.5f);
        ctx.Check(hostReady && joined && !QALobbyFlow.IsLocalReady(), "새 접속 → 전원 준비 초기화",
            $"B 재접속 전 호스트 준비={hostReady}, 재접속={joined}, 뒤 준비={QALobbyFlow.IsLocalReady()} 준비 수={QALobbyFlow.ReadyCount(out _)}", ctx.Shot("join_reset"));

        // B) 3/3 준비 → 카운트다운 → A 해제 → 취소
        yield return SetReady(ctx, true);
        yield return ctx.Wait(() => LobbySession.Active != null && LobbySession.Active.IsCountdownActive, 60f);
        bool cd1 = !ctx.TimedOut;
        ctx.Check(cd1, "3/3 준비 → 카운트다운", $"준비 {QALobbyFlow.ReadyCount(out int t1)}/{t1}");
        yield return ctx.Wait(() => LobbySession.Active != null && !LobbySession.Active.IsCountdownActive, 8f);
        bool cancelledByUnready = !ctx.TimedOut && QAUtil.ActiveSceneName() == QALobbyFlow.LobbyScene;
        ctx.Check(cd1 && cancelledByUnready, "준비 해제 → 카운트다운 취소", $"취소 {ctx.LastWaitSeconds:F1}s, 준비 {QALobbyFlow.ReadyCount(out int t2)}/{t2}", ctx.Shot("unready_cancel"));
        if (!cancelledByUnready && !QALobbyFlow.InLobby) { yield return EndMatchToLobby(ctx, 120f); yield break; }

        // C) 다시 3/3 → B 퇴장 → 취소 + 준비 초기화
        yield return ctx.Wait(() => LobbySession.Active != null && LobbySession.Active.IsCountdownActive, 60f);
        bool cd2 = !ctx.TimedOut;
        yield return ctx.Wait(() => LobbySession.Active != null && LobbySession.Active.EntryCount == 2, 8f);
        bool left = !ctx.TimedOut;
        yield return null;
        bool cancelledByLeave = LobbySession.Active != null && !LobbySession.Active.IsCountdownActive && QALobbyFlow.InLobby;
        int readyAfterLeave = QALobbyFlow.ReadyCount(out int t3);
        ctx.Check(cd2 && left && cancelledByLeave, "퇴장 → 카운트다운 취소", $"카운트다운={cd2} 퇴장={left} 취소={cancelledByLeave}", ctx.Shot("leave_cancel"));
        ctx.Check(left && readyAfterLeave == 0, "퇴장 → 준비 초기화", $"남은 준비 {readyAfterLeave}/{t3}");

        // D) 호스트·A 다시 준비(2/2) → 카운트다운 → 그 사이 B 재접속은 거절돼야 한다(판정은 B 리포트). 호스트는 자리 2 유지만 본다.
        yield return SetReady(ctx, true);
        yield return ctx.Wait(() => LobbySession.Active != null && LobbySession.Active.IsCountdownActive, 20f);
        bool cd3 = !ctx.TimedOut;
        int maxEntries = 0;
        while (cd3 && QALobbyFlow.InLobby && LobbySession.Active != null && LobbySession.Active.IsCountdownActive)
        {
            maxEntries = Mathf.Max(maxEntries, LobbySession.Active.EntryCount);
            yield return null;
        }
        ctx.Check(cd3 && maxEntries == 2, "카운트다운 중 새 접속 불가(호스트 관측)", $"카운트다운={cd3}, 카운트다운 중 최대 자리 {maxEntries}(B 거절 사유는 B 리포트)");

        // 맵에 갔으면 나가기로 로비 복귀 → B 자동 입장.
        yield return ctx.Wait(() => QALobbyFlow.InMapWithPlayer, 60f);
        yield return new WaitForSecondsRealtime(3f);
        yield return EndMatchToLobby(ctx, 120f);
    }

    private static IEnumerator M2ClientA(QAScenarioContext ctx)
    {
        // A) 아무것도 안 함(호스트 준비 + A 미준비 = 카운트다운 없는 안정 상태). B 재접속(자리 3)까지.
        yield return ctx.Wait(() => LobbySession.Active != null && LobbySession.Active.EntryCount == 2, 60f);
        yield return ctx.Wait(() => LobbySession.Active != null && LobbySession.Active.EntryCount == 3, 90f);
        yield return new WaitForSecondsRealtime(1f);

        // B) 준비 → 카운트다운 뜨면 해제
        yield return SetReady(ctx, true);
        bool readyOn = QALobbyFlow.IsLocalReady();
        yield return ctx.Wait(() => LobbySession.Active != null && LobbySession.Active.IsCountdownActive, 60f);
        bool sawCountdown = !ctx.TimedOut;
        yield return SetReady(ctx, false);
        ctx.Check(readyOn && sawCountdown && !QALobbyFlow.IsLocalReady(), "A: 카운트다운 중 준비 해제",
            $"준비 켜짐={readyOn} 카운트다운 관측={sawCountdown} 해제 뒤 ready={QALobbyFlow.IsLocalReady()}");
        yield return ctx.Wait(() => LobbySession.Active != null && !LobbySession.Active.IsCountdownActive, 8f);

        // C) 다시 준비(B 가 카운트다운을 보고 나간다)
        yield return new WaitForSecondsRealtime(1f);
        yield return SetReady(ctx, true);
        yield return ctx.Wait(() => LobbySession.Active != null && LobbySession.Active.EntryCount == 2, 60f);

        // D) 준비 초기화됐으니 다시 준비(2/2 카운트다운)
        yield return SetReady(ctx, true);
        yield return ctx.Wait(() => QALobbyFlow.InMapWithPlayer, 60f);
        yield return ctx.Wait(() => QALobbyFlow.HasLocalLobbyPlayer, 150f);
    }

    private static IEnumerator M2ClientB(QAScenarioContext ctx)
    {
        // A) 나갔다(보류) 8초 뒤 재접속 — 호스트는 그 사이 준비를 켠다(A 미준비라 카운트다운 없음 = 고정 대기가 안전).
        Leave(true);
        yield return new WaitForSecondsRealtime(8f);
        SetReconnect(true);
        yield return ctx.Wait(() => QALobbyFlow.HasLocalLobbyPlayer, 60f);
        ctx.Check(!ctx.TimedOut, "B: 로비 재접속(카운트다운 없음 → 승인)", $"{ctx.LastWaitSeconds:F1}s {Who()}");

        // B) 준비 → (A 해제로 취소)
        yield return new WaitForSecondsRealtime(1f);
        yield return SetReady(ctx, true);
        yield return ctx.Wait(() => LobbySession.Active != null && LobbySession.Active.IsCountdownActive, 60f);
        yield return ctx.Wait(() => LobbySession.Active != null && !LobbySession.Active.IsCountdownActive, 8f);

        // C) 다시 준비 → 3/3 카운트다운 보이면 퇴장(보류)
        yield return SetReady(ctx, true);
        yield return ctx.Wait(() => LobbySession.Active != null && LobbySession.Active.IsCountdownActive && AllEntriesReady(3), 60f);
        bool sawCountdown = !ctx.TimedOut;
        Leave(true);

        // D) 호스트·A 가 다시 준비해 2/2 카운트다운(약 1~2초 뒤 시작, 5초)을 돌리는 동안 재접속 → 거절돼야 한다.
        //    퇴장 → 재준비 ≈ 1~2초, 카운트다운 5초 → 2.5초 뒤 재개면 재시도(3초 간격)가 카운트다운 창 안에 든다.
        yield return new WaitForSecondsRealtime(2.5f);
        SetReconnect(true);
        var seen = new HashSet<DemoJoinRejectReason>();
        float t = 0f;
        while (t < 25f && !QALobbyFlow.IsConnected)
        {
            DemoJoinRejectReason r = LastReject();
            if (r != DemoJoinRejectReason.None)
                seen.Add(r);
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        while (t < 40f && seen.Count == 0)
        {
            DemoJoinRejectReason r = LastReject();
            if (r != DemoJoinRejectReason.None)
                seen.Add(r);
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        var texts = new HashSet<string>();
        QALobbyFlow.CollectTexts("진행", texts);
        ctx.Check(sawCountdown && seen.Contains(DemoJoinRejectReason.GameInProgress), "카운트다운 중 접속 거절",
            $"3/3 카운트다운 관측={sawCountdown}, 거절 사유={string.Join(",", seen)}, 화면={string.Join(" / ", texts)}", ctx.Shot("countdown_reject"));

        // 판이 끝나 호스트가 로비로 오면 자동 입장.
        yield return ctx.Wait(() => QALobbyFlow.HasLocalLobbyPlayer, 180f);
        ctx.Check(!ctx.TimedOut, "B: 판 끝 → 로비 자동 입장", $"{ctx.LastWaitSeconds:F1}s {Who()}");
    }

    // ── M3 교체 3화면 동기·슬롯 초상화·교체 캐릭터로 Map 스폰 ──────────────

    private static IEnumerator M3(QAScenarioContext ctx)
    {
        var role = new QACount();
        yield return ResolveRole(ctx, role, 180f, true);
        if (role.Value < 0)
            yield break;
        yield return CloseResultPanelIfOpen(ctx, null);
        yield return SetReady(ctx, false);

        CharacterRoster roster = Roster();
        List<int> avail = AvailableCharacters(roster);
        List<Player> players = QALobbyFlow.SpawnedPlayers();
        ulong swapperId = players[1].OwnerClientId; // A 가 바꾼다
        int before = CharacterIdOf(swapperId);
        int target = avail.Count > 1 ? avail[(Mathf.Max(0, avail.IndexOf(before)) + 1) % avail.Count] : before;
        string targetName = PrefabName(roster, target);

        if (role.Value == 1)
        {
            LobbyInteractable station = QALobbyFlow.FindInteractable(LobbyInteractableKind.CharacterSelect, Player.LocalPlayer.transform.position);
            CharacterSelectPanel panel = Object.FindFirstObjectByType<CharacterSelectPanel>(FindObjectsInactive.Include);
            yield return new WaitForSecondsRealtime(2f); // 모두 같은 '교체 전' 상태를 본 뒤
            var swapped = new QAFlag();
            if (station != null && panel != null)
                yield return SwapViaPanel(ctx, panel, station, roster, target, swapped);
            ctx.Check(swapped.Value, "A: 패널로 교체", $"{PrefabName(roster, before)} → {targetName}");
        }

        // 모든 화면: A 자리의 캐릭터 id 와 A 플레이어 오브젝트가 새 캐릭터로 바뀌는지.
        yield return ctx.Wait(() => CharacterIdOf(swapperId) == target && PlayerOf(swapperId) != null &&
                                    QALobbyFlow.CleanName(PlayerOf(swapperId)) == targetName, 30f);
        ctx.Check(!ctx.TimedOut, $"교체 동기(이 화면 역할 {role.Value})",
            $"c{swapperId} 자리 id={CharacterIdOf(swapperId)} 오브젝트={QALobbyFlow.CleanName(PlayerOf(swapperId))} 기대 {targetName} ({ctx.LastWaitSeconds:F1}s)");

        // 슬롯 초상화: 슬롯 뷰 아래 Image 중 로스터 초상화와 같은 스프라이트가 있는가.
        yield return new WaitForSecondsRealtime(0.5f);
        Sprite portrait = roster != null && roster.TryGetAvailableCharacter(target, out CharacterRoster.Entry entry) ? entry.Portrait : null;
        bool portraitShown = false;
        foreach (LobbyPlayerSlotView view in Object.FindObjectsByType<LobbyPlayerSlotView>(FindObjectsSortMode.None))
            foreach (UnityEngine.UI.Image img in view.GetComponentsInChildren<UnityEngine.UI.Image>(false))
                if (portrait != null && img.sprite == portrait)
                    portraitShown = true;
        string slotShot = ctx.Shot("slots");
        if (portraitShown)
            ctx.Pass($"슬롯 초상화(역할 {role.Value})", $"{targetName} 초상화 스프라이트가 슬롯에 표시", slotShot);
        else
            ctx.Eye($"슬롯 초상화(역할 {role.Value})", $"슬롯 Image 에서 {targetName} 초상화를 못 찾음(portrait={(portrait != null)}) — 스크린샷 확인", slotShot);

        // 전원 준비 → 맵 → A 가 새 캐릭터로 스폰됐는가.
        yield return new WaitForSecondsRealtime(1f);
        yield return SetReady(ctx, true);
        yield return ctx.Wait(() => QALobbyFlow.InMapWithPlayer && PlayerOf(swapperId) != null, 120f);
        string mapName = QALobbyFlow.CleanName(PlayerOf(swapperId));
        ctx.Check(!ctx.TimedOut && mapName == targetName, $"교체 캐릭터로 Map 스폰(역할 {role.Value})", $"c{swapperId} 맵 오브젝트={mapName} 기대 {targetName}", ctx.Shot("map"));

        yield return new WaitForSecondsRealtime(3f);
        yield return EndMatchToLobby(ctx, 150f);
    }

    // ── M5 보스전 중 재합류 → 가장 먼 모서리, 연출 중엔 보류 ─────────────

    private static readonly BossEncounterPhase[] CinematicPhases =
        { BossEncounterPhase.Preparing, BossEncounterPhase.Descending, BossEncounterPhase.Impact, BossEncounterPhase.Dialogue };

    private static bool IsCinematic(BossEncounterPhase p) => System.Array.IndexOf(CinematicPhases, p) >= 0;

    private static BossEncounterPhase Phase => BossEncounterDirector.Instance != null ? BossEncounterDirector.Instance.Phase : BossEncounterPhase.Idle;

    private static IEnumerator M5(QAScenarioContext ctx)
    {
        var inMap = new QAFlag();
        yield return GoToMap(ctx, inMap);
        if (!inMap.Value)
            yield break;
        var role = new QACount();
        yield return ResolveRole(ctx, role, 90f, false);
        if (role.Value < 0)
            yield break;
        List<Player> players = QALobbyFlow.SpawnedPlayers();
        ulong rejoinId = players[1].OwnerClientId;
        string rejoinCharacter = QALobbyFlow.CleanName(players[1]);
        QAScenarioContext.ErrorMark mark = ctx.MarkErrors();

        if (role.Value == 0)
        {
            yield return new WaitForSecondsRealtime(3f);
            BossTeleportManager tp = BossTeleportManager.Instance;
            float deadline = Time.realtimeSinceStartup + 30f;
            while (tp != null && !tp.ForceStartEncounter(0f) && Time.realtimeSinceStartup < deadline)
                yield return new WaitForSecondsRealtime(1f);
            ctx.Check(tp != null && Time.realtimeSinceStartup < deadline, "보스전 강제 개시", $"ForceStartEncounter 수락={(tp != null && Time.realtimeSinceStartup < deadline)}");

            // A 가 연출 중 끊는다 → 새 clientId 로 돌아오는 순간의 보스 단계를 본다.
            yield return ctx.Wait(() => PlayerOf(rejoinId) == null, 150f);
            bool dropped = !ctx.TimedOut;
            BossEncounterPhase dropPhase = Phase;
            var originals = new HashSet<ulong>();
            foreach (Player pl in players) originals.Add(pl.OwnerClientId);
            Player back = null;
            BossEncounterPhase spawnPhase = BossEncounterPhase.Idle;
            bool heldDuringCinematic = false;
            float t = 0f;
            while (t < 180f)
            {
                foreach (Player pl in QALobbyFlow.SpawnedPlayers())
                    if (!originals.Contains(pl.OwnerClientId))
                        back = pl;
                if (back != null)
                {
                    spawnPhase = Phase;
                    break;
                }
                if (IsCinematic(Phase) || (tp != null && (tp.IsElevatorRunning || tp.IsEncounterBusy)))
                    heldDuringCinematic = true;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            ctx.Check(dropped && back != null && !IsCinematic(spawnPhase), "연출 중 재합류 보류 → 연출 뒤 스폰",
                $"끊김 시 단계={dropPhase}, 대기 중 연출/엘리베이터 관측={heldDuringCinematic}, 스폰 시 단계={spawnPhase}, {t:F1}s", ctx.Shot("rejoin_after_cinematic"));
            if (back != null)
            {
                ctx.Check(QALobbyFlow.CleanName(back) == rejoinCharacter, "보스전 재합류 같은 캐릭터", $"{rejoinCharacter} → {QALobbyFlow.CleanName(back)}");
                yield return new WaitForSecondsRealtime(1f);
                yield return CheckFarthestCorner(ctx, back);
            }
            ctx.Eye("엘리베이터·슬로·전멸 유예 중 보류", "자동 시나리오는 연출(Preparing~Dialogue) 중 끊김만 재현 — 엘리베이터·슬로모션·전멸 유예 보류는 같은 게이트(RejoinSpawnGate)지만 미재현");
            yield return new WaitForSecondsRealtime(3f);
            yield return EndMatchToLobby(ctx, 150f);
        }
        else if (role.Value == 1)
        {
            // 연출 단계(또는 엘리베이터·도착 대기)가 보이면 끊는다 — 자동 재접속.
            yield return ctx.Wait(() => IsCinematic(Phase) || Phase == BossEncounterPhase.AwaitingArrival, 120f);
            BossEncounterPhase at = Phase;
            Leave(false);
            yield return ctx.Wait(() => !QALobbyFlow.IsConnected, 20f);
            yield return ctx.Wait(() => QALobbyFlow.IsConnected && QALobbyFlow.InMapWithPlayer, 180f);
            ctx.Check(!ctx.TimedOut, "A: 보스전 중 재합류", $"끊은 단계={at}, 재합류 {ctx.LastWaitSeconds:F1}s, 지금 단계={Phase}", ctx.Shot("rejoined"));
            yield return ctx.Wait(() => QALobbyFlow.HasLocalLobbyPlayer, 200f);
        }
        else
        {
            yield return ctx.Wait(() => QALobbyFlow.HasLocalLobbyPlayer, 400f);
        }

        int errors = ctx.ErrorsSince(mark, out string summary);
        ctx.Check(errors == 0, $"M5 구간 에러 로그 0(역할 {role.Value})", $"{errors}건: {summary}");
    }

    /// <summary>재합류 위치가 보스룸 모서리 중 다른 플레이어·보스에서 가장 먼 곳인가 — 게임과 같은 공개 규칙(RejoinSpawnPicker.FarthestCorner)으로 다시 계산.</summary>
    private static IEnumerator CheckFarthestCorner(QAScenarioContext ctx, Player back)
    {
        var corners = new List<Pose>();
        bool haveCorners = BossRoomRejoin.TryCollectCorners(corners, out bool fromPrefab);
        if (!haveCorners || back == null)
        {
            ctx.Eye("가장 먼 모서리", $"모서리 수집 실패(haveCorners={haveCorners}) — 스크린샷 확인", ctx.Shot("corner"));
            yield break;
        }
        int nearest = -1;
        float nearestDist = float.MaxValue;
        for (int i = 0; i < corners.Count; i++)
        {
            float d = NearestInteractable.PlanarDistance(back.transform.position, corners[i].position);
            if (d < nearestDist) { nearestDist = d; nearest = i; }
        }
        var others = new List<Vector3>();
        foreach (Player pl in QALobbyFlow.SpawnedPlayers())
            if (pl != back) others.Add(pl.transform.position);
        var bosses = new List<Vector3>();
        foreach (BossHudTarget b in BossHudTarget.Active)
            if (b != null) bosses.Add(b.transform.position);
        // 판정 시점의 다른 사람 위치는 스폰 순간과 조금 다를 수 있다 — 모서리 일치를 보되, 2m 안에 서 있지 않으면 FAIL.
        int expected = RejoinSpawnPicker.FarthestCorner(corners, others, bosses);
        ctx.Check(nearestDist < 2f && nearest == expected, "가장 먼 모서리 스폰",
            $"재합류 {back.transform.position} → 모서리 {nearest}({nearestDist:F1}m), 지금 기준 가장 먼 모서리={expected}, 모서리 출처 프리팹={fromPrefab}", ctx.Shot("corner"));
    }

    // ── M6 재합류 생명·특성·튜토리얼 상태 복원 ──────────────────────────

    private static IEnumerator M6(QAScenarioContext ctx)
    {
        var inMap = new QAFlag();
        yield return GoToMap(ctx, inMap);
        if (!inMap.Value)
            yield break;
        var role = new QACount();
        yield return ResolveRole(ctx, role, 90f, false);
        if (role.Value < 0)
            yield break;
        List<Player> players = QALobbyFlow.SpawnedPlayers();
        ulong rejoinId = players[1].OwnerClientId;
        QAScenarioContext.ErrorMark mark = ctx.MarkErrors();

        if (role.Value == 0)
        {
            yield return new WaitForSecondsRealtime(3f);
            // 1회차: 체력 절반 → 끊김 → 체력 비율·특성·튜토리얼 체크·뒤 배리어 복원.
            Player a = PlayerOf(rejoinId);
            if (a == null) { ctx.Fail("M6 대상", "A 플레이어 없음"); yield break; }
            a.ApplyDirectHealthDamage(a.MaxHp / 2);
            yield return new WaitForSecondsRealtime(0.5f);
            var snap = M6Snapshot.Take(rejoinId, a);
            var back = new M6Back();
            yield return M6WaitRejoin(ctx, rejoinId, back);
            if (back.Player == null) { ctx.Fail("M6 1회차 재합류", "180초 안에 A 재합류 없음", ctx.Shot("m6_no_rejoin")); yield break; }
            yield return new WaitForSecondsRealtime(1.5f);
            var after = M6Snapshot.Take(back.Player.OwnerClientId, back.Player);
            ctx.Check(Mathf.Abs(after.HpRatio - snap.HpRatio) < 0.05f && after.Life == PlayerLifeState.Alive, "재합류 체력 비율 유지",
                $"끊기 전 {snap.Hp}/{snap.MaxHp}({snap.HpRatio:P0}) → {after.Hp}/{after.MaxHp}({after.HpRatio:P0}) 생명 {after.Life}");
            ctx.Check(snap.TraitsKey == after.TraitsKey, "재합류 특성 유지",
                $"[{snap.TraitsKey}] → [{after.TraitsKey}]{(snap.TraitCount == 0 ? " (끊기 전 특성 0개 — 약한 검증)" : "")}");
            ctx.Check(snap.ResetPrompt == after.ResetPrompt, "재합류 리셋 팝업 유지", $"{snap.ResetPrompt} → {after.ResetPrompt}");
            ctx.Check(snap.CheckMask == after.CheckMask, "재합류 튜토리얼 체크 유지", $"0x{snap.CheckMask:X} → 0x{after.CheckMask:X}");
            ctx.Eye("재합류 뒤 배리어", $"마스크 0x{snap.BarrierMask:X} → 0x{after.BarrierMask:X} (TutorialDirector.RebindPlayer 가 0 으로 다시 쓴다 — 배리어 위치는 스크린샷)", ctx.Shot("m6_barrier"));

            // 2회차: 영혼 상태로 끊김 → 영혼으로 복귀(튜토리얼 G11 전이면 그 뒤 자동 부활).
            a = back.Player;
            ulong secondId = a.OwnerClientId;
            a.ApplyDirectHealthDamage(a.CurrentHealth + 100000);
            yield return ctx.Wait(() => a != null && a.GetComponent<PlayerLifeCycleController>().State == PlayerLifeState.Soul, 10f);
            bool wasSoul = !ctx.TimedOut;
            var back2 = new M6Back();
            yield return M6WaitRejoin(ctx, secondId, back2);
            PlayerLifeState first = back2.FirstLife;
            ctx.Check(wasSoul && back2.Player != null && first == PlayerLifeState.Soul, "재합류 영혼 복원",
                $"끊기 전 Soul={wasSoul}, 복귀 첫 생명 상태={first} (튜토리얼 G11 전이면 3초 뒤 자동 부활 — 지금 {(back2.Player != null ? back2.Player.GetComponent<PlayerLifeCycleController>().State.ToString() : "-")})");
            ctx.Skip("재합류 영구 사망 복원", "튜토리얼 맵에서 공용 목숨 0 영구 사망을 안전하게 만들 경로가 없음(G11 전 무차감) — EditMode PlayerRejoinLifeRules 범위");
            yield return new WaitForSecondsRealtime(3f);
            yield return EndMatchToLobby(ctx, 150f);
        }
        else if (role.Value == 1)
        {
            // 1회차: 체력이 절반이 되면 끊는다.
            yield return ctx.Wait(() => Player.LocalPlayer != null && Player.LocalPlayer.CurrentHealth < Player.LocalPlayer.MaxHp, 60f);
            yield return new WaitForSecondsRealtime(1f);
            Leave(false);
            yield return ctx.Wait(() => !QALobbyFlow.IsConnected, 20f);
            yield return ctx.Wait(() => QALobbyFlow.IsConnected && QALobbyFlow.InMapWithPlayer, 180f);
            ctx.Check(!ctx.TimedOut, "A: 1회차 재합류", $"{ctx.LastWaitSeconds:F1}s");
            // 2회차: 영혼이 되면 끊는다.
            yield return ctx.Wait(() => Player.LocalPlayer != null && Player.LocalPlayer.GetComponent<PlayerLifeCycleController>().State == PlayerLifeState.Soul, 60f);
            Leave(false);
            yield return ctx.Wait(() => !QALobbyFlow.IsConnected, 20f);
            yield return ctx.Wait(() => QALobbyFlow.IsConnected && QALobbyFlow.InMapWithPlayer, 180f);
            ctx.Check(!ctx.TimedOut, "A: 2회차(영혼) 재합류", $"{ctx.LastWaitSeconds:F1}s");
            yield return ctx.Wait(() => QALobbyFlow.HasLocalLobbyPlayer, 200f);
        }
        else
        {
            yield return ctx.Wait(() => QALobbyFlow.HasLocalLobbyPlayer, 400f);
        }

        int errors = ctx.ErrorsSince(mark, out string summary);
        ctx.Check(errors == 0, $"M6 구간 에러 로그 0(역할 {role.Value})", $"{errors}건: {summary}");
    }

    private sealed class M6Back
    {
        public Player Player;
        public PlayerLifeState FirstLife = PlayerLifeState.Alive;
    }

    /// <summary>[호스트] oldId 가 사라졌다가 새 clientId 로 돌아올 때까지. 돌아온 첫 프레임의 생명 상태를 같이 잡는다.</summary>
    private static IEnumerator M6WaitRejoin(QAScenarioContext ctx, ulong oldId, M6Back back)
    {
        yield return ctx.Wait(() => PlayerOf(oldId) == null, 60f);
        var present = new HashSet<ulong>();
        foreach (Player pl in QALobbyFlow.SpawnedPlayers()) present.Add(pl.OwnerClientId);
        float t = 0f;
        while (t < 180f)
        {
            foreach (Player pl in QALobbyFlow.SpawnedPlayers())
                if (!present.Contains(pl.OwnerClientId))
                {
                    back.Player = pl;
                    back.FirstLife = pl.GetComponent<PlayerLifeCycleController>().State;
                    yield break;
                }
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private struct M6Snapshot
    {
        public int Hp, MaxHp, CheckMask, BarrierMask, TraitCount;
        public float HpRatio;
        public string TraitsKey;
        public bool ResetPrompt;
        public PlayerLifeState Life;

        public static M6Snapshot Take(ulong clientId, Player p)
        {
            var s = new M6Snapshot { Hp = p.CurrentHealth, MaxHp = p.MaxHp, Life = p.GetComponent<PlayerLifeCycleController>().State };
            s.HpRatio = s.MaxHp > 0 ? (float)s.Hp / s.MaxHp : 0f;
            var traits = new List<int>();
            TutorialTraitDirector td = TutorialTraitDirector.Instance;
            if (td != null)
            {
                td.GetAcquired(clientId, traits);
                s.ResetPrompt = td.TryGetResetPrompt(clientId, out _);
            }
            traits.Sort();
            s.TraitCount = traits.Count;
            s.TraitsKey = string.Join(",", traits);
            TutorialDirector dir = TutorialDirector.Instance;
            if (dir != null)
            {
                if (dir.TryGetPlayerProgress(clientId, out TutorialPlayerProgress prog))
                    s.CheckMask = prog.CheckMask;
                s.BarrierMask = dir.GetBackBarrierMask(clientId);
            }
            return s;
        }
    }

    // ── M7 부재 중 전멸 리스폰 → 복귀 Alive 만피, 결과 행 1개 ──────────────

    private static IEnumerator M7(QAScenarioContext ctx)
    {
        var inMap = new QAFlag();
        yield return GoToMap(ctx, inMap);
        if (!inMap.Value)
            yield break;
        var role = new QACount();
        yield return ResolveRole(ctx, role, 90f, false);
        if (role.Value < 0)
            yield break;
        List<Player> players = QALobbyFlow.SpawnedPlayers();
        ulong absentId = players[1].OwnerClientId;

        if (role.Value == 0)
        {
            // 전멸이 결과 대신 리스폰이 되려면 튜토리얼은 G10(보스 진입·패드)이 열려 있어야 한다(PartyWipeRespawnRules.CanRespawn,
            // party-lives-respawn.md §3.2). 강제 개시는 패드를 건너뛰어 G10 이 닫힌 채라 2차 1회차에서 결과 화면으로 갔다 — 디버그 경로로 G10 까지 연다.
            TutorialDirector tutorial = TutorialDirector.Instance;
            if (tutorial != null && tutorial.IsTutorialActive && !tutorial.IsGateOpen(TutorialGate.G10_BossEnterAndPad))
                TutorialDebugActions.OpenGatesThrough(tutorial, TutorialGate.G10_BossEnterAndPad);
            // 그다음 보스전 강제 개시 → Combat(보스방 도착 = G11).
            BossTeleportManager tp = BossTeleportManager.Instance;
            float deadline = Time.realtimeSinceStartup + 30f;
            while (tp != null && Phase != BossEncounterPhase.Combat && !tp.ForceStartEncounter(0f) && Time.realtimeSinceStartup < deadline)
                yield return new WaitForSecondsRealtime(1f);
            yield return ctx.Wait(() => Phase == BossEncounterPhase.Combat, 150f);
            if (ctx.TimedOut) { ctx.Fail("M7 보스전 Combat", $"단계={Phase}", ctx.Shot("m7_no_combat")); yield break; }

            // A 가 Combat 을 보고 나간다(보류).
            yield return ctx.Wait(() => PlayerOf(absentId) == null, 60f);
            bool absent = !ctx.TimedOut;
            var flow = new QAResultFlow();
            flow.Begin();
            yield return flow.DriveHost(QAResultPath.Wiped, ctx.Recorder);
            flow.End();
            ctx.Check(absent && flow.WipeOutcome == QAWipeOutcome.Respawned, "A 부재 중 전멸 → 리스폰",
                $"A 부재={absent}, 귀결={flow.WipeOutcome}", ctx.Shot("m7_respawn"));

            // A 복귀(보류 해제는 A 가 고정 대기 뒤) → Alive 만피.
            var originals = new HashSet<ulong>();
            foreach (Player pl in QALobbyFlow.SpawnedPlayers()) originals.Add(pl.OwnerClientId);
            Player back = null;
            yield return ctx.Wait(() =>
            {
                foreach (Player pl in QALobbyFlow.SpawnedPlayers())
                    if (!originals.Contains(pl.OwnerClientId)) { back = pl; return true; }
                return false;
            }, 180f);
            yield return new WaitForSecondsRealtime(1f);
            PlayerLifeState life = back != null ? back.GetComponent<PlayerLifeCycleController>().State : PlayerLifeState.PermanentDead;
            ctx.Check(back != null && life == PlayerLifeState.Alive && back.CurrentHealth == back.MaxHp, "복귀 Alive 만피",
                back != null ? $"생명 {life} HP {back.CurrentHealth}/{back.MaxHp}" : "180초 안에 A 복귀 없음", ctx.Shot("m7_back"));

            yield return new WaitForSecondsRealtime(2f);
            yield return EndMatchToLobby(ctx, 150f);
            int rows = SessionResult.HasValue ? SessionResult.Players.Count : -1;
            var ids = new HashSet<ulong>();
            if (SessionResult.HasValue) foreach (PlayerSessionStats r in SessionResult.Players) ids.Add(r.ClientId);
            ctx.Check(rows == ctx.ExpectedPlayers, "결과 행 = 사람 수(재합류자 1행)", $"행 {rows} / 사람 {ctx.ExpectedPlayers}, clientId={string.Join(",", ids)}", ctx.Shot("m7_result"));
        }
        else if (role.Value == 1)
        {
            yield return ctx.Wait(() => Phase == BossEncounterPhase.Combat, 180f);
            Leave(true);
            // 호스트가 전멸 강제(최대 120초) → 리스폰. 전멸 처리 ≈ 사망 연출 1.5초 + 영혼 부활 루프 + 유예 2초 — 넉넉히 45초 뒤 복귀.
            yield return new WaitForSecondsRealtime(45f);
            SetReconnect(true);
            yield return ctx.Wait(() => QALobbyFlow.IsConnected && QALobbyFlow.InMapWithPlayer, 180f);
            ctx.Check(!ctx.TimedOut, "A: 전멸 리스폰 뒤 재합류", $"{ctx.LastWaitSeconds:F1}s, 생명 {(Player.LocalPlayer != null ? Player.LocalPlayer.GetComponent<PlayerLifeCycleController>().State.ToString() : "-")}");
            yield return ctx.Wait(() => QALobbyFlow.HasLocalLobbyPlayer, 200f);
        }
        else
        {
            yield return ctx.Wait(() => QALobbyFlow.HasLocalLobbyPlayer, 450f);
        }
    }

    // ── M8 명단 밖 새 사람 판 도중 거절 → 로비 복귀 시 자동 입장 ───────────

    private static IEnumerator M8(QAScenarioContext ctx)
    {
        var role = new QACount();
        yield return ResolveRole(ctx, role, 180f, true);
        if (role.Value < 0)
            yield break;
        yield return CloseResultPanelIfOpen(ctx, null);
        yield return SetReady(ctx, false);

        if (role.Value == 2)
        {
            // '늦게 켜진 3번째 사람' = 판 시작 전에 나가 명단에 안 든 사람. 호스트·A 가 맵에 들어갈 시간(준비 2초 + 카운트다운 5초 + 로딩 ~10초)을 넉넉히 둔다.
            Leave(true);
            yield return new WaitForSecondsRealtime(30f);
            SetReconnect(true);
            var seen = new HashSet<DemoJoinRejectReason>();
            var texts = new HashSet<string>();
            float t = 0f;
            while (t < 30f && !QALobbyFlow.HasLocalLobbyPlayer)
            {
                DemoJoinRejectReason r = LastReject();
                if (r != DemoJoinRejectReason.None) seen.Add(r);
                QALobbyFlow.CollectTexts("진행", texts);
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            ctx.Check(seen.Contains(DemoJoinRejectReason.GameInProgress), "명단 밖 새 사람 판 도중 거절",
                $"거절 사유={string.Join(",", seen)}, 화면={string.Join(" / ", texts)}", ctx.Shot("m8_rejected"));
            yield return ctx.Wait(() => QALobbyFlow.HasLocalLobbyPlayer, 240f);
            ctx.Check(!ctx.TimedOut, "로비 복귀 시 자동 입장(새 사람)", $"{ctx.LastWaitSeconds:F1}s {Who()}", ctx.Shot("m8_joined"));
            yield break;
        }

        yield return ctx.Wait(() => LobbySession.Active != null && LobbySession.Active.EntryCount == 2, 60f);
        yield return SetReady(ctx, true);
        yield return ctx.Wait(() => QALobbyFlow.InMapWithPlayer, 120f);
        if (ctx.TimedOut) { ctx.Fail("M8 2인 맵 진입", $"scene={QAUtil.ActiveSceneName()}"); yield break; }

        if (role.Value == 0)
        {
            // B 가 거절당하는 동안(접속 30초 뒤부터) 판에 2명만 있는지 본다.
            int maxPlayers = 0;
            float t = 0f;
            while (t < 45f)
            {
                maxPlayers = Mathf.Max(maxPlayers, QALobbyFlow.SpawnedPlayers().Count);
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            ctx.Check(maxPlayers == 2, "판 도중 새 사람 없음(호스트 관측)", $"45초 동안 최대 플레이어 {maxPlayers}명, 명단 {(MatchRoster.Server != null ? MatchRoster.Server.Count : -1)}명");
            yield return EndMatchToLobby(ctx, 150f);
            yield return ctx.Wait(() => LobbySession.Active != null && LobbySession.Active.EntryCount == 3, 120f);
            ctx.Check(!ctx.TimedOut, "로비 복귀 → 새 사람 자동 입장(호스트 관측)", $"{ctx.LastWaitSeconds:F1}s 자리 {(LobbySession.Active != null ? LobbySession.Active.EntryCount : 0)}", ctx.Shot("m8_lobby3"));
        }
        else
        {
            yield return ctx.Wait(() => QALobbyFlow.HasLocalLobbyPlayer, 240f);
        }
    }
}
