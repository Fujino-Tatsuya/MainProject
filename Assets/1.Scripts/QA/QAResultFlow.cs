using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>QA 사이클을 결과 화면까지 보내는 경로(PLAN-result-stats 검증용).</summary>
public enum QAResultPath
{
    Aborted, // ExitButton 과 같은 진입점(GameManager.GoToResultButton)
    Wiped,   // 서버에서 플레이어를 목숨이 다할 때까지 죽인다 → PartyWipeWatcher
    Cleared  // 보스전 강제 개시(BossTeleportManager.ForceStartEncounter) → 보스 격파 → BossEncounterDirector
}

/// <summary>전멸 강제의 귀결 — <see cref="QAResultFlow.WipeOutcome"/>.</summary>
public enum QAWipeOutcome
{
    Timeout,            // 아무 귀결도 없음(FAIL)
    Result,             // 결과 확정(결과 화면/로비 결과 패널)
    Respawned,          // 전멸 리스폰(보스 미격파 — 설계)
    TutorialAutoRevive, // 튜토리얼 G11 전 — 차감 없는 자동 부활이라 전멸 불가(설계)
}

/// <summary>
/// 결과 화면 플레이어별 통계 QA. 게임 코드 무수정 — public 진입점과 정적 이벤트만 쓴다.
///
/// 1) 호스트: 게임 집계기(SessionStatsTracker)와 <b>따로</b> 같은 서버 이벤트를 세어(<see cref="Tally"/>)
///    확정된 <see cref="SessionResult"/> 와 비교한다 — 이벤트 → 집계 → 페이로드 배선 확인.
/// 2) 호스트: 경로별로 판을 끝낸다(<see cref="DriveHost"/>). QA 가 넣은 피해·사망은 따로 기록한다.
/// 3) 모든 피어: 결과 씬에서 <see cref="SessionResult"/> 와 화면 행(PlayerRow_P*)을 덤프하고 서로 맞는지 본다.
///    피어 간 비교(호스트·클라 값 동일)는 리포트 파일 result-stats.md 끼리 비교한다.
/// </summary>
public sealed class QAResultFlow
{
    public const string ResultScene = "5.ResultScene";

    private const float WipeTimeout = 120f;
    private const float EncounterStartTimeout = 60f;
    private const float CombatTimeout = 120f;
    private const float BossKillTimeout = 90f;
    private const float HitInterval = 0.3f;

    private readonly Tally _tally = new Tally();
    private readonly Dictionary<ulong, int> _injectedDamage = new Dictionary<ulong, int>();
    private readonly Dictionary<ulong, int> _injectedDeaths = new Dictionary<ulong, int>();
    private readonly StringBuilder _log = new StringBuilder();

    public string Dump { get; private set; }

    /// <summary>진행 로그(결과 씬에 못 갔을 때도 리포트에 남긴다).</summary>
    public string LogText => _log.ToString();

    public void Begin()
    {
        _tally.Reset();
        _tally.Subscribe();
        _injectedDamage.Clear();
        _injectedDeaths.Clear();
        _log.Clear();
        Dump = null;
    }

    public void End()
    {
        _tally.Unsubscribe();
    }

    public static bool InResultScene => QAUtil.ActiveSceneName() == ResultScene;

    // ── 호스트: 판 끝내기 ─────────────────────────────────────────

    public IEnumerator DriveHost(QAResultPath path, QARecorder recorder)
    {
        yield return InjectPlayerDamage(recorder);

        Note($"경로 강제 시작: {path}");
        switch (path)
        {
            case QAResultPath.Aborted:
                GameManager.GoToResultButton();
                break;
            case QAResultPath.Wiped:
                yield return ForceWipe(recorder);
                break;
            case QAResultPath.Cleared:
                yield return ForceClear(recorder);
                break;
        }
    }

    /// <summary>
    /// 무작위 플레이만으로는 몬스터를 거의 못 때린다(10-08 1차: 60초 전원 0). 판을 끝내기 전에
    /// 플레이어 i(clientId 오름차순)가 살아 있는 일반 몬스터를 (i+1)회 때리게 해 행마다 다른 값이 나오게 한다.
    /// 실제 피격 경로(ReceiveAttack → Unit.ApplyHealthDamage → CombatStatsEvents)를 탄다.
    /// </summary>
    private IEnumerator InjectPlayerDamage(QARecorder recorder)
    {
        var players = new List<Player>();
        foreach (Player p in UnityEngine.Object.FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (p != null && p.IsSpawned)
                players.Add(p);
        }
        players.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

        for (int i = 0; i < players.Count; i++)
        {
            Player attacker = players[i];
            for (int hitIndex = 0; hitIndex <= i; hitIndex++)
            {
                MonsterBase target = FindLivingMonster();
                if (target == null)
                {
                    recorder.Add(QASeverity.Warning, "ResultStats", "피해 주입 대상(살아 있는 일반 몬스터) 없음 — 데미지 행 검증 약함");
                    yield break;
                }

                int before = target.CurrentHealth;
                var info = new AttackInfo { damage = 40 };
                var context = new AttackHitContext(attacker.transform.position, attacker.transform, null, attacker);
                target.ReceiveAttack(info, context);
                int dealt = Mathf.Max(0, before - target.CurrentHealth);
                Increment(_injectedDamage, attacker.OwnerClientId, dealt);
                yield return new WaitForSeconds(0.2f);
            }
        }

        var sb = new StringBuilder("피해 주입(HP 감소 기준):");
        foreach (Player p in players)
            sb.Append($" c{p.OwnerClientId}={Get(_injectedDamage, p.OwnerClientId)}");
        Note(sb.ToString());
    }

    private static MonsterBase FindLivingMonster()
    {
        foreach (MonsterBase m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
        {
            if (m != null && m.IsSpawned && m.CurrentHealth > 0 && !(m is TwentyThreeBoss))
                return m;
        }
        return null;
    }

    /// <summary>직전 <see cref="QAResultPath.Wiped"/> 강제의 귀결. 결과 화면 말고도 설계상 귀결이 둘 있다(party-lives-respawn.md).</summary>
    public QAWipeOutcome WipeOutcome { get; private set; }

    /// <summary>
    /// 전멸 강제. 설계상 결과 화면까지 안 가는 경우가 둘이라 그것도 귀결로 인정하고 멈춘다(할 일 48.3 원인 분석):
    ///  · 튜토리얼 G11(보스방 도착) 전 사망 = 차감 없이 3초 뒤 자동 부활(TutorialDeathRules, ServerDeathKeepsSoul) — 전멸이 날 수 없다.
    ///    48-20261020_143105 의 '120초 안에 결과 확정 안 됨' 은 이 자동 부활 ↔ QA 재사망이 약 5초 주기로 돈 것(진단 5초 주기와 겹쳐 DeadPresentation 에 멈춘 것처럼 보였다).
    ///    QA 가 Soul 을 직접 되살리면(TryCompleteReviveOnServer) 차감 없는 구간인데도 공용 목숨이 깎이므로 이 구간에선 직접 부활하지 않는다.
    ///  · 보스 미격파 전멸 = 결과 화면 대신 전멸 리스폰(PartyWipeRespawner.PartyWipeRespawned).
    /// </summary>
    private IEnumerator ForceWipe(QARecorder recorder)
    {
        float deadline = Time.time + WipeTimeout;
        float nextDiag = 0f;
        MultiGameRule rule = UnityEngine.Object.FindFirstObjectByType<MultiGameRule>();
        bool respawned = false;
        System.Action<IReadOnlyList<ulong>> onRespawn = _ => respawned = true;
        PartyWipeRespawner.PartyWipeRespawned += onRespawn;
        var freeDeaths = new HashSet<ulong>();
        var freeRevived = new HashSet<ulong>();
        WipeOutcome = QAWipeOutcome.Timeout;
        try
        {
        while (Time.time < deadline && !SessionResult.HasValue && !InResultScene)
        {
            if (respawned)
            {
                WipeOutcome = QAWipeOutcome.Respawned;
                Note("전멸 → 전멸 리스폰(PartyWipeRespawned) — 설계상 결과 화면 대신 리스폰");
                yield break;
            }

            bool diag = Time.time >= nextDiag;
            var sb = diag ? new StringBuilder("전멸 강제 상태:") : null;
            if (diag)
                nextDiag = Time.time + 5f;

            foreach (Player player in UnityEngine.Object.FindObjectsByType<Player>(FindObjectsSortMode.None))
            {
                if (player == null || !player.IsSpawned)
                    continue;

                PlayerLifeCycleController life = player.GetComponent<PlayerLifeCycleController>();
                if (life == null)
                    continue;

                if (diag)
                {
                    int lives = -1;
                    rule?.TryGetLifeCount(player.OwnerClientId, out lives);
                    PlayerInvulnerability inv = player.GetComponent<PlayerInvulnerability>();
                    sb.Append($" c{player.OwnerClientId}[{life.State} hp={player.CurrentHealth} life={lives} " +
                              $"inv={(inv != null && inv.IsServerInvulnerable)} {DeathResolutionDiag(life)}]");
                }

                bool keepsSoul = PlayerLifeCycleController.ServerDeathKeepsSoul != null &&
                                 PlayerLifeCycleController.ServerDeathKeepsSoul(player.OwnerClientId);
                if (life.State == PlayerLifeState.Alive && player.CurrentHealth > 0)
                {
                    if (freeDeaths.Contains(player.OwnerClientId))
                        freeRevived.Add(player.OwnerClientId); // 차감 없는 자동 부활로 돌아왔다
                    // 직접 피해(무적 중엔 거부 — 다음 틱 재시도). 추락 피해는 추락 사망 연출로 DeadPresentation 이
                    // 30초 가까이 늘어져(10-08 3차) 쓰지 않는다.
                    player.ApplyDirectHealthDamage(player.CurrentHealth + 100000);
                    if (player.CurrentHealth <= 0)
                    {
                        Increment(_injectedDeaths, player.OwnerClientId);
                        if (keepsSoul)
                            freeDeaths.Add(player.OwnerClientId);
                    }
                }
                else if (life.State == PlayerLifeState.Soul && !keepsSoul)
                {
                    // 목숨이 남은 Soul 은 게임의 부활 경로로 되살려 다시 죽인다(목숨 1 소비).
                    // 🔴 PlayerLifeCycleController.TryCompleteRevive 는 목숨을 안 깎아 무한 루프가 된다(10-08 3차).
                    player.GetComponent<PlayerReviveController>()?.TryCompleteReviveOnServer();
                }
            }

            if (diag)
                Note(sb.ToString());

            // 전원이 '차감 없는 사망 → 자동 부활' 을 한 번씩 거쳤으면 이 판은 지금 전멸할 수 없다(튜토리얼 G11 전).
            if (freeRevived.Count > 0 && freeRevived.SetEquals(freeDeaths) && freeDeaths.Count >= CountSpawnedPlayers())
            {
                WipeOutcome = QAWipeOutcome.TutorialAutoRevive;
                Note($"전멸 불가 — 튜토리얼 G11 전 사망은 차감 없이 자동 부활(TutorialDeathRules). 자동 부활 확인 {freeRevived.Count}명, 공용 목숨 {(rule != null && rule.IsSpawned ? rule.PartyLives : -1)}");
                yield break;
            }
            yield return new WaitForSeconds(0.25f);
        }

        if (SessionResult.HasValue || InResultScene)
            WipeOutcome = QAWipeOutcome.Result;
        else if (respawned)
            WipeOutcome = QAWipeOutcome.Respawned;
        else
            recorder.Add(QASeverity.Error, "ResultStats", $"전멸 강제 {WipeTimeout:F0}초 안에 결과·리스폰·자동 부활 어느 것도 안 남",
                _log.ToString());
        }
        finally
        {
            PartyWipeRespawner.PartyWipeRespawned -= onRespawn;
        }
    }

    private static int CountSpawnedPlayers()
    {
        int n = 0;
        foreach (Player p in UnityEngine.Object.FindObjectsByType<Player>(FindObjectsSortMode.None))
            if (p != null && p.IsSpawned)
                n++;
        return n;
    }

    /// <summary>사망 연출 해소 예약(private) 진단 — DeadPresentation 에 멈춘 원인을 가르기 위한 읽기 전용 리플렉션.</summary>
    private static string DeathResolutionDiag(PlayerLifeCycleController life)
    {
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        System.Type t = typeof(PlayerLifeCycleController);
        object pending = t.GetField("deathResolutionPending", F)?.GetValue(life);
        object deadline = t.GetField("deathResolutionDeadlineServerTime", F)?.GetValue(life);
        object deferred = t.GetField("deferredDeathWhileLocked", F)?.GetValue(life);
        double now = NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : -1;
        return $"pending={pending} deadline={deadline:0.0} now={now:0.0} deferred={deferred} keepsSoul={PlayerLifeCycleController.ServerDeathKeepsSoul?.Invoke(life.OwnerClientId)}";
    }

    private IEnumerator ForceClear(QARecorder recorder)
    {
        BossTeleportManager teleport = BossTeleportManager.Instance;
        if (teleport == null)
        {
            recorder.Add(QASeverity.Error, "ResultStats", "BossTeleportManager 없음 — 클리어 경로 불가");
            yield break;
        }

        float deadline = Time.time + EncounterStartTimeout;
        while (!teleport.ForceStartEncounter(0f))
        {
            if (Time.time > deadline || InResultScene)
            {
                recorder.Add(QASeverity.Error, "ResultStats", "보스전 강제 개시 실패(ForceStartEncounter 계속 거절)");
                yield break;
            }
            yield return new WaitForSeconds(1f);
        }
        Note("ForceStartEncounter 수락");

        deadline = Time.time + CombatTimeout;
        while (BossEncounterDirector.Instance == null || BossEncounterDirector.Instance.Phase != BossEncounterPhase.Combat)
        {
            if (Time.time > deadline || InResultScene)
            {
                string phase = BossEncounterDirector.Instance != null ? BossEncounterDirector.Instance.Phase.ToString() : "없음";
                recorder.Add(QASeverity.Error, "ResultStats", $"보스전 Combat 진입 실패(현재 {phase})");
                yield break;
            }
            yield return null;
        }
        Note("보스전 Combat 진입");

        TwentyThreeBoss boss = UnityEngine.Object.FindFirstObjectByType<TwentyThreeBoss>();
        Player attacker = Player.LocalPlayer; // 호스트 = clientId 0
        if (boss == null || attacker == null)
        {
            recorder.Add(QASeverity.Error, "ResultStats", $"보스/호스트 플레이어 없음(boss={boss != null}, host={attacker != null})");
            yield break;
        }

        int hit = Mathf.Max(1, boss.MaxHp / 6);
        deadline = Time.time + BossKillTimeout;
        while (boss != null && boss.CurrentHealth > 0)
        {
            if (Time.time > deadline || InResultScene)
            {
                recorder.Add(QASeverity.Error, "ResultStats",
                    $"보스 격파 실패({BossKillTimeout:F0}초, 남은 HP {(boss != null ? boss.CurrentHealth : -1)})");
                yield break;
            }

            int before = boss.CurrentHealth;
            var info = new AttackInfo { damage = hit };
            var context = new AttackHitContext(attacker.transform.position, attacker.transform, null, attacker);
            bool accepted = boss.ReceiveAttack(info, context);
            int dealt = Mathf.Max(0, before - boss.CurrentHealth);
            if (dealt > 0)
                Increment(_injectedDamage, attacker.OwnerClientId, dealt);
            Note($"보스 타격: accepted={accepted} HP {before}→{boss.CurrentHealth}/{boss.MaxHp} 상태={boss.State} " +
                 $"결과확정={SessionResult.HasValue} 집계 처치(c{attacker.OwnerClientId})={_tally.Get(attacker.OwnerClientId).Kills}");

            yield return new WaitForSeconds(HitInterval);
        }
        Note($"보스 격파 — QA 주입 피해 {Get(_injectedDamage, attacker.OwnerClientId)} (attacker={attacker.OwnerClientId})");
        int from = Mathf.Max(0, _tally.BossEvents.Count - 4);
        for (int i = from; i < _tally.BossEvents.Count; i++)
            Note($"보스 피해 이벤트[{i}]: {_tally.BossEvents[i]}");
    }

    // ── 모든 피어: 결과 화면 검증 ──────────────────────────────────

    /// <summary>결과 씬이 뜬 뒤 호출. SessionResult·화면 행·(호스트) 독립 집계를 대조해 Dump 를 만든다.</summary>
    public void Verify(QARecorder recorder, QAResultPath? expected, bool isHost)
    {
        var sb = new StringBuilder();
        NetworkManager nm = NetworkManager.Singleton;
        ulong local = nm != null && nm.IsListening ? nm.LocalClientId : ulong.MaxValue;

        sb.AppendLine("# 결과 화면 통계 덤프");
        sb.AppendLine();
        sb.AppendLine($"- 역할: {(isHost ? "호스트" : "클라이언트")} · LocalClientId={local}");
        sb.AppendLine($"- 기대 경로: {(expected.HasValue ? expected.Value.ToString() : "(클라 — 호스트 리포트 기준)")}");
        sb.AppendLine($"- SessionResult: HasValue={SessionResult.HasValue} · Outcome={SessionResult.Outcome} · " +
                      $"플레이 시간={SessionResult.SurvivalSeconds:F1}s ({SessionResult.FormatSurvival()}) · 행={SessionResult.Players.Count}");
        sb.AppendLine();

        if (!SessionResult.HasValue)
            recorder.Add(QASeverity.Error, "ResultStats", "결과 씬인데 SessionResult.HasValue=false");

        if (expected.HasValue && SessionResult.HasValue && !OutcomeMatches(expected.Value, SessionResult.Outcome))
            recorder.Add(QASeverity.Error, "ResultStats", $"결과 종류 불일치: 기대 {expected.Value}, 실제 {SessionResult.Outcome}");

        if (nm != null && nm.IsListening && SessionResult.Players.Count < nm.ConnectedClientsIds.Count && isHost)
            recorder.Add(QASeverity.Error, "ResultStats",
                $"행 수 {SessionResult.Players.Count} < 접속 인원 {nm.ConnectedClientsIds.Count}");

        sb.AppendLine("## SessionResult 행");
        sb.AppendLine();
        sb.AppendLine("| P | clientId | 캐릭터id | 데미지 | 처치 | 간파 | 사망 | QA주입 피해 | QA주입 사망 |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        IReadOnlyList<PlayerSessionStats> rows = SessionResult.Players;
        for (int i = 0; i < rows.Count; i++)
        {
            PlayerSessionStats r = rows[i];
            sb.AppendLine($"| P{i + 1} | {r.ClientId} | {r.CharacterId} | {r.Damage} | {r.Kills} | {r.Counters} | {r.Deaths} | " +
                          $"{Get(_injectedDamage, r.ClientId)} | {Get(_injectedDeaths, r.ClientId)} |");
            if (i > 0 && rows[i - 1].ClientId >= r.ClientId)
                recorder.Add(QASeverity.Error, "ResultStats", "행이 clientId 오름차순이 아님");
        }
        sb.AppendLine();

        if (isHost)
            AppendTallyComparison(sb, recorder, rows);

        AppendUiComparison(sb, recorder, rows, local);

        if (_log.Length > 0)
        {
            sb.AppendLine("## 진행 로그");
            sb.AppendLine();
            sb.Append(_log);
            sb.AppendLine();
        }

        Dump = sb.ToString();
        recorder.Add(QASeverity.Info, "ResultStats",
            $"결과 덤프: Outcome={SessionResult.Outcome}, 행={rows.Count}, local={local} (result-stats.md)");
    }

    private void AppendTallyComparison(StringBuilder sb, QARecorder recorder, IReadOnlyList<PlayerSessionStats> rows)
    {
        sb.AppendLine("## 호스트 독립 집계 대조 (QA 가 같은 서버 이벤트를 따로 셈)");
        sb.AppendLine();
        sb.AppendLine("| clientId | 데미지 QA/결과 | 처치 QA/결과 | 간파 QA/결과 | 사망 QA/결과 |");
        sb.AppendLine("|---:|---|---|---|---|");
        for (int i = 0; i < rows.Count; i++)
        {
            PlayerSessionStats r = rows[i];
            Tally.Row t = _tally.Get(r.ClientId);
            bool ok = t.Damage == r.Damage && t.Kills == r.Kills && t.Counters == r.Counters && t.Deaths == r.Deaths;
            sb.AppendLine($"| {r.ClientId} | {t.Damage}/{r.Damage} | {t.Kills}/{r.Kills} | {t.Counters}/{r.Counters} | " +
                          $"{t.Deaths}/{r.Deaths} {(ok ? "✅" : "❌")} |");
            if (!ok)
                recorder.Add(QASeverity.Error, "ResultStats",
                    $"clientId {r.ClientId} 집계 불일치: QA({t.Damage},{t.Kills},{t.Counters},{t.Deaths}) vs " +
                    $"결과({r.Damage},{r.Kills},{r.Counters},{r.Deaths})");
            if (r.Damage < Get(_injectedDamage, r.ClientId))
                recorder.Add(QASeverity.Error, "ResultStats",
                    $"clientId {r.ClientId} 데미지 {r.Damage} < QA 주입 피해 {Get(_injectedDamage, r.ClientId)}");
            if (r.Deaths < Get(_injectedDeaths, r.ClientId))
                recorder.Add(QASeverity.Error, "ResultStats",
                    $"clientId {r.ClientId} 사망 {r.Deaths} < QA 주입 사망 {Get(_injectedDeaths, r.ClientId)}");
        }
        sb.AppendLine();
    }

    private static void AppendUiComparison(StringBuilder sb, QARecorder recorder, IReadOnlyList<PlayerSessionStats> rows, ulong local)
    {
        sb.AppendLine("## 화면 행 대조 (PlayerRow_P*)");
        sb.AppendLine();

        ResultStatsView view = UnityEngine.Object.FindFirstObjectByType<ResultStatsView>();
        if (view == null)
        {
            recorder.Add(QASeverity.Error, "ResultStats", "ResultStatsView 없음");
            return;
        }

        sb.AppendLine("| 행 | 슬롯 | 캐릭터 | 데미지 | 처치 | 간파 | 사망 | 나 | 판정 |");
        sb.AppendLine("|---|---|---|---:|---:|---:|---:|---|---|");
        int uiRows = 0;
        foreach (ResultPlayerRowView rowView in view.GetComponentsInChildren<ResultPlayerRowView>(true))
        {
            if (!rowView.name.StartsWith("PlayerRow_P", StringComparison.Ordinal))
                continue;

            uiRows++;
            int index = uiRows - 1;
            Transform t = rowView.transform;
            string slot = Text(t, "Text_Slot");
            string character = Text(t, "Text_Character");
            string damage = Text(t, "Text_Damage");
            string kills = Text(t, "Text_Kills");
            string counters = Text(t, "Text_Counters");
            string deaths = Text(t, "Text_Deaths");
            Transform marker = t.Find("Marker_Local");
            bool markerOn = marker != null && marker.gameObject.activeSelf;

            bool ok = index < rows.Count;
            if (ok)
            {
                PlayerSessionStats r = rows[index];
                ok = slot == $"P{index + 1}" && damage == r.Damage.ToString() && kills == r.Kills.ToString() &&
                     counters == r.Counters.ToString() && deaths == r.Deaths.ToString() &&
                     markerOn == (r.ClientId == local);
            }

            sb.AppendLine($"| {rowView.name} | {slot} | {character} | {damage} | {kills} | {counters} | {deaths} | " +
                          $"{(markerOn ? "●" : "")} | {(ok ? "✅" : "❌")} |");
            if (!ok)
                recorder.Add(QASeverity.Error, "ResultStats", $"화면 행 {rowView.name} 이 SessionResult 와 다름");
        }
        sb.AppendLine();

        if (uiRows != rows.Count)
            recorder.Add(QASeverity.Error, "ResultStats", $"화면 행 수 {uiRows} ≠ SessionResult 행 수 {rows.Count}");

        AppendLayoutCheck(sb, recorder, view);
    }

    /// <summary>
    /// 결과 화면 레이아웃 — 행·열 제목이 화면/패널 안에 있고 서로·버튼과 겹치지 않는지, 글자가 넘치지 않는지.
    /// 패널 = PlayerRows 의 조상 중 켜진 Image 가 있는 첫 오브젝트(없으면 ResultStatsView). 좌표는 화면 픽셀.
    /// </summary>
    private static void AppendLayoutCheck(StringBuilder sb, QARecorder recorder, ResultStatsView view)
    {
        sb.AppendLine("## 레이아웃 검사 (화면 픽셀)");
        sb.AppendLine();

        Canvas.ForceUpdateCanvases();
        Transform rowsRoot = view.transform.Find("PlayerRows");
        if (rowsRoot == null)
            foreach (Transform t in view.GetComponentsInChildren<Transform>(true))
                if (t.name == "PlayerRows") { rowsRoot = t; break; }
        if (rowsRoot == null)
        {
            recorder.Add(QASeverity.Error, "ResultStats", "레이아웃: PlayerRows 없음");
            return;
        }

        RectTransform panel = view.transform as RectTransform;
        for (Transform t = rowsRoot.parent; t != null; t = t.parent)
        {
            UnityEngine.UI.Image img = t.GetComponent<UnityEngine.UI.Image>();
            if (img != null && img.enabled && t.GetComponent<Canvas>() == null)
            {
                panel = (RectTransform)t;
                break;
            }
        }

        Rect screen = new Rect(0, 0, Screen.width, Screen.height);
        Rect panelRect = ScreenRect(panel);
        sb.AppendLine($"- 화면 {Screen.width}×{Screen.height} · 패널 '{panel.name}' {Fmt(panelRect)}");

        var boxes = new List<KeyValuePair<string, Rect>>();
        Transform header = rowsRoot.parent != null ? rowsRoot.parent.Find("PlayerRowsHeader") : null;
        if (header != null)
            boxes.Add(new KeyValuePair<string, Rect>(header.name, ScreenRect((RectTransform)header)));
        foreach (Transform child in rowsRoot)
            if (child.gameObject.activeInHierarchy && child.name.StartsWith("PlayerRow_P", StringComparison.Ordinal))
                boxes.Add(new KeyValuePair<string, Rect>(child.name, ScreenRect((RectTransform)child)));

        int problems = 0;
        foreach (var box in boxes)
        {
            var issues = new List<string>();
            if (!Contains(screen, box.Value)) issues.Add("화면 밖");
            if (!Contains(panelRect, box.Value)) issues.Add($"패널 '{panel.name}' 밖");
            sb.AppendLine($"- {box.Key} {Fmt(box.Value)} {(issues.Count == 0 ? "✅" : "❌ " + string.Join(", ", issues))}");
            problems += issues.Count;
        }

        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
                if (Overlap(boxes[i].Value, boxes[j].Value) > 1f)
                {
                    sb.AppendLine($"- ❌ 겹침: {boxes[i].Key} ↔ {boxes[j].Key}");
                    problems++;
                }

        foreach (UnityEngine.UI.Button button in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None))
        {
            Rect br = ScreenRect((RectTransform)button.transform);
            foreach (var box in boxes)
                if (Overlap(br, box.Value) > 1f)
                {
                    sb.AppendLine($"- ❌ 버튼 '{button.name}' 과 {box.Key} 겹침");
                    problems++;
                }
        }

        foreach (TMP_Text text in rowsRoot.parent.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!(text.transform.IsChildOf(rowsRoot) || (header != null && text.transform.IsChildOf(header))))
                continue;
            text.ForceMeshUpdate();
            if (text.isTextOverflowing || text.isTextTruncated)
            {
                sb.AppendLine($"- ❌ 글자 넘침/잘림: {text.transform.parent.name}/{text.name} '{text.text}' " +
                              $"(폭 {text.rectTransform.rect.width:F0}, 필요 {text.preferredWidth:F0})");
                problems++;
            }
        }

        sb.AppendLine(problems == 0 ? "- 판정: ✅ 잘림·겹침 없음" : $"- 판정: ❌ 문제 {problems}건");
        sb.AppendLine();
        if (problems > 0)
            recorder.Add(QASeverity.Error, "ResultStats", $"결과 화면 레이아웃 문제 {problems}건 (result-stats.md)");
    }

    private static Rect ScreenRect(RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        Canvas canvas = rt.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    private static bool Contains(Rect outer, Rect inner) =>
        inner.xMin >= outer.xMin - 0.5f && inner.yMin >= outer.yMin - 0.5f &&
        inner.xMax <= outer.xMax + 0.5f && inner.yMax <= outer.yMax + 0.5f;

    private static float Overlap(Rect a, Rect b)
    {
        float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
        float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
        return w > 0 && h > 0 ? Mathf.Min(w, h) : 0f;
    }

    private static string Fmt(Rect r) => $"[{r.xMin:F0},{r.yMin:F0} ~ {r.xMax:F0},{r.yMax:F0}]";

    private static bool OutcomeMatches(QAResultPath path, SessionOutcome outcome)
    {
        switch (path)
        {
            case QAResultPath.Aborted: return outcome == SessionOutcome.Aborted;
            case QAResultPath.Wiped: return outcome == SessionOutcome.Wiped;
            default: return outcome == SessionOutcome.Cleared;
        }
    }

    private static string Text(Transform row, string child)
    {
        Transform t = row.Find(child);
        TMP_Text text = t != null ? t.GetComponent<TMP_Text>() : null;
        return text != null ? text.text : "(없음)";
    }

    public void Note(string message)
    {
        _log.AppendLine($"- [{Time.time:F1}s · {QAUtil.ActiveSceneName()}] {message}");
        Debug.Log($"[QA][ResultStats] {message}");
    }

    private static void Increment(Dictionary<ulong, int> map, ulong key, int amount = 1)
    {
        map.TryGetValue(key, out int value);
        map[key] = value + amount;
    }

    private static int Get(Dictionary<ulong, int> map, ulong key)
    {
        return map.TryGetValue(key, out int value) ? value : 0;
    }

    /// <summary>
    /// 게임 집계기와 독립된 서버 이벤트 카운터. 집계 규칙(몬스터만·공격자 없음 제외·Alive→DeadPresentation)은
    /// PLAN-result-stats 의 정의를 그대로 옮겼다. 결과가 확정되면(SessionResult.HasValue) 더 세지 않는다.
    /// </summary>
    private sealed class Tally
    {
        public struct Row
        {
            public int Damage, Kills, Counters, Deaths;
        }

        private readonly Dictionary<ulong, Row> _rows = new Dictionary<ulong, Row>();

        public void Reset() { _rows.Clear(); BossEvents.Clear(); }

        public Row Get(ulong clientId) => _rows.TryGetValue(clientId, out Row row) ? row : default;

        public void Subscribe()
        {
            CombatStatsEvents.ServerDamageApplied += OnDamage;
            CombatStatsEvents.ServerCounterSucceeded += OnCounter;
            PlayerLifeCycleController.ServerLifeStateChanged += OnLife;
        }

        public void Unsubscribe()
        {
            CombatStatsEvents.ServerDamageApplied -= OnDamage;
            CombatStatsEvents.ServerCounterSucceeded -= OnCounter;
            PlayerLifeCycleController.ServerLifeStateChanged -= OnLife;
        }

        // 게임 집계기와 같은 창: 맵의 SessionStatsTracker 가 살아 있고 결과가 아직 확정되지 않은 동안.
        private static bool Counting =>
            SessionStatsTracker.Active != null && !SessionResult.HasValue &&
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        /// <summary>보스 대상 피해 이벤트 원문(게이트 무관) — 막타 처치 누락 진단용.</summary>
        public readonly List<string> BossEvents = new List<string>();

        private void OnDamage(Unit target, ulong attacker, int amount, bool lethal)
        {
            if (target is TwentyThreeBoss)
                BossEvents.Add($"attacker={attacker} amount={amount} lethal={lethal} hp={target.CurrentHealth} " +
                               $"counting={Counting} 확정={SessionResult.HasValue} tracker={(SessionStatsTracker.Active != null)}");
            if (!Counting || !(target is MonsterBase) || attacker == CombatStatsEvents.NoAttacker || amount <= 0)
                return;
            Row row = Get(attacker);
            row.Damage += amount;
            if (lethal)
                row.Kills++;
            _rows[attacker] = row;
        }

        private void OnCounter(Unit target, ulong attacker)
        {
            if (!Counting || attacker == CombatStatsEvents.NoAttacker)
                return;
            Row row = Get(attacker);
            row.Counters++;
            _rows[attacker] = row;
        }

        private void OnLife(ulong clientId, PlayerLifeState previous, PlayerLifeState current)
        {
            if (!Counting || previous != PlayerLifeState.Alive || current != PlayerLifeState.DeadPresentation)
                return;
            Row row = Get(clientId);
            row.Deaths++;
            _rows[clientId] = row;
        }
    }
}
