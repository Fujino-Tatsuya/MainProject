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

    private IEnumerator ForceWipe(QARecorder recorder)
    {
        float deadline = Time.time + WipeTimeout;
        while (Time.time < deadline && !SessionResult.HasValue && !InResultScene)
        {
            foreach (Player player in UnityEngine.Object.FindObjectsByType<Player>(FindObjectsSortMode.None))
            {
                if (player == null || !player.IsSpawned)
                    continue;

                PlayerLifeCycleController life = player.GetComponent<PlayerLifeCycleController>();
                if (life == null)
                    continue;

                if (life.State == PlayerLifeState.Alive && player.CurrentHealth > 0)
                {
                    player.ApplyDirectHealthDamage(player.CurrentHealth + 100000);
                    Increment(_injectedDeaths, player.OwnerClientId);
                }
                else if (life.State == PlayerLifeState.Soul)
                {
                    // 목숨이 남은 Soul 은 되살려 다시 죽인다 — 목숨이 다하면 PermanentDead 로 간다.
                    life.TryCompleteRevive();
                }
            }

            yield return new WaitForSeconds(0.25f);
        }

        if (!SessionResult.HasValue && !InResultScene)
            recorder.Add(QASeverity.Error, "ResultStats", $"전멸 강제 {WipeTimeout:F0}초 안에 결과가 확정되지 않음");
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
            boss.ReceiveAttack(info, context);
            int dealt = Mathf.Max(0, before - boss.CurrentHealth);
            if (dealt > 0)
                Increment(_injectedDamage, attacker.OwnerClientId, dealt);

            yield return new WaitForSeconds(HitInterval);
        }
        Note($"보스 격파 — QA 주입 피해 {Get(_injectedDamage, attacker.OwnerClientId)} (attacker={attacker.OwnerClientId})");
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
    }

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

        public void Reset() => _rows.Clear();

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

        private void OnDamage(Unit target, ulong attacker, int amount, bool lethal)
        {
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
