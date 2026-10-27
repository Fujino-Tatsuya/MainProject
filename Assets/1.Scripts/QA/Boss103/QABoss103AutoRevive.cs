#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 할 일 103 C7b — QA 자동 부활 대기(순수, EditMode <c>QABoss103AutoReviveTests</c>). Soul 진입 시각 + 지연 = 부활 시각.
/// Soul 을 벗어나면(부활·전멸 리스폰·영구 사망) 대기를 지운다. 같은 사람이 대기 중에 다시 Soul 신호를 내도 처음 시각을 유지한다.
/// </summary>
public sealed class QABoss103ReviveQueue
{
    private readonly Dictionary<ulong, double> _due = new Dictionary<ulong, double>();
    private readonly List<ulong> _scratch = new List<ulong>();

    public readonly double DelaySeconds;

    public QABoss103ReviveQueue(double delaySeconds) => DelaySeconds = delaySeconds;

    public int Count => _due.Count;

    public bool IsPending(ulong clientId) => _due.ContainsKey(clientId);

    /// <summary>Soul 이 됐다 — <paramref name="now"/> + 지연에 부활. 이미 대기 중이면 그대로.</summary>
    public void OnSoul(ulong clientId, double now)
    {
        if (!_due.ContainsKey(clientId))
            _due[clientId] = now + DelaySeconds;
    }

    /// <summary>Soul 을 벗어났거나 나갔다 — 대기 취소. 취소했으면 true.</summary>
    public bool Cancel(ulong clientId) => _due.Remove(clientId);

    /// <summary>부활 시각이 된 사람을 꺼낸다(대기에서 빠진다 — 실패해도 다시 시도하지 않는다). 순서 = clientId 오름차순.</summary>
    public void TakeDue(double now, List<ulong> into)
    {
        into.Clear();
        _scratch.Clear();
        foreach (KeyValuePair<ulong, double> kv in _due)
            if (now >= kv.Value)
                _scratch.Add(kv.Key);
        _scratch.Sort();
        foreach (ulong id in _scratch)
        {
            _due.Remove(id);
            into.Add(id);
        }
    }
}

/// <summary>
/// 할 일 103 C7b — [호스트] 보스전 QA 자동 부활. 판(Combat ~ 판정) 동안만 산다.
/// 튜토리얼 사망 규칙은 보스방 도착(G11) 뒤 일반 규칙으로 넘어가고, 일반 규칙엔 자동 부활이 없어(부활 수단 = F10 디버그뿐)
/// 셋 다 Soul + 공용 목숨 남음 = 전멸도 아닌 교착이 된다. 문서(Docs/tech/party-lives-respawn.md)의 'G11 뒤 자동 부활·1개 차감' 의도를 재현한다:
/// Soul 이 되면 <see cref="MapSceneDeathRulesModel.DefaultReviveDelaySeconds"/> 뒤 정식 진입점
/// <see cref="PlayerReviveController.TryCompleteReviveOnServer()"/>(Alive + 공용 목숨 1 차감, 그 자리). 실패(목숨 0 등)면 Soul 그대로 → 게임 전멸 판정.
/// 성공하면 튜토리얼 규칙과 같은 부활 무적(<see cref="PlayerLandingProtection.BeginProtection"/>, Revive 2초). 게임 코드는 바꾸지 않는다.
/// </summary>
public sealed class QABoss103AutoRevive : MonoBehaviour
{
    private const string Tag = QABoss103Plan.Tag;
    /// <summary>튜토리얼 사망 규칙의 부활 무적(<c>MapSceneDeathRules.reviveInvulnerabilitySeconds</c> 기본값 — 비공개라 같은 값을 둔다).</summary>
    public const float InvulnerabilitySeconds = 2f;
    public const string NoInvulnNote = "qa_revive_no_invuln";

    private readonly QABoss103ReviveQueue _queue = new QABoss103ReviveQueue(MapSceneDeathRulesModel.DefaultReviveDelaySeconds);
    private readonly List<ulong> _due = new List<ulong>();
    private IReadOnlyDictionary<ulong, QABoss103Character> _characters;
    private QABoss103RunStats _stats;
    private bool _stopped;

    public int Revives { get; private set; }
    public int Failures { get; private set; }

    /// <summary>호스트 — Combat 진입 순간. 이미 Soul 인 사람도 지금부터 지연을 센다.</summary>
    public static QABoss103AutoRevive Begin(IReadOnlyDictionary<ulong, QABoss103Character> characters, QABoss103RunStats stats)
    {
        var go = new GameObject("[QA] Boss103AutoRevive");
        DontDestroyOnLoad(go);
        QABoss103AutoRevive r = go.AddComponent<QABoss103AutoRevive>();
        r._characters = characters;
        r._stats = stats;
        PlayerLifeCycleController.ServerLifeStateChanged += r.OnLifeChanged;
        foreach (Player p in QALobbyFlow.SpawnedPlayers())
        {
            PlayerLifeCycleController life = p != null ? p.GetComponent<PlayerLifeCycleController>() : null;
            if (life != null && life.State == PlayerLifeState.Soul)
                r._queue.OnSoul(p.OwnerClientId, Now);
        }
        Debug.Log($"{Tag} event=qa_revive_on delayS={r._queue.DelaySeconds:0.#} invulnS={InvulnerabilitySeconds:0.#} pending={r._queue.Count}");
        return r;
    }

    private static double Now
    {
        get
        {
            NetworkManager nm = NetworkManager.Singleton;
            return nm != null && nm.IsListening ? nm.ServerTime.Time : Time.realtimeSinceStartupAsDouble;
        }
    }

    private void OnLifeChanged(ulong clientId, PlayerLifeState previous, PlayerLifeState current)
    {
        if (_stopped)
            return;
        if (current == PlayerLifeState.Soul)
            _queue.OnSoul(clientId, Now);
        else if (current != PlayerLifeState.DeadPresentation)
            _queue.Cancel(clientId);
    }

    private void Update()
    {
        if (_stopped || _queue.Count == 0)
            return;
        _queue.TakeDue(Now, _due);
        foreach (ulong id in _due)
            Revive(id);
    }

    private void Revive(ulong clientId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        NetworkObject po = nm != null && nm.ConnectedClients.TryGetValue(clientId, out NetworkClient c) ? c.PlayerObject : null;
        string character = _characters != null && _characters.TryGetValue(clientId, out QABoss103Character ch) ? ch.ToString() : "na";
        PlayerReviveController revive = po != null ? po.GetComponent<PlayerReviveController>() : null;
        if (revive == null)
        {
            Failures++;
            Debug.LogWarning($"{Tag} event=qa_revive character={character} client={clientId} delayS={_queue.DelaySeconds:0.#} ok=false livesLeft={LivesLeft()} reason=no_player");
            return;
        }

        bool ok = revive.TryCompleteReviveOnServer();
        string invuln = "na";
        if (ok)
        {
            Revives++;
            PlayerLandingProtection protection = po.GetComponent<PlayerLandingProtection>();
            if (protection != null)
            {
                protection.BeginProtection(InvulnerabilityCause.Revive, InvulnerabilitySeconds, applyStun: false);
                invuln = InvulnerabilitySeconds.ToString("0.#");
            }
            else
            {
                invuln = "none";
            }
            if (_characters != null && _characters.TryGetValue(clientId, out QABoss103Character slot))
                _stats?.QaRevive((int)slot, protection != null);
        }
        else
        {
            Failures++;
        }
        PlayerLifeCycleController life = po.GetComponent<PlayerLifeCycleController>();
        Debug.Log($"{Tag} event=qa_revive character={character} client={clientId} delayS={_queue.DelaySeconds:0.#} ok={(ok ? "true" : "false")} " +
                  $"livesLeft={LivesLeft()} invulnS={invuln} state={(life != null ? life.State.ToString() : "na")}" +
                  (ok && invuln == "none" ? " note=" + NoInvulnNote : ""));
    }

    private static string LivesLeft()
    {
        MultiGameRule rule = MultiGameRule.Current;
        return rule != null ? rule.PartyLives.ToString() : "na";
    }

    /// <summary>판정 순간 — 더 부활시키지 않는다.</summary>
    public void Stop()
    {
        if (_stopped)
            return;
        _stopped = true;
        PlayerLifeCycleController.ServerLifeStateChanged -= OnLifeChanged;
        Debug.Log($"{Tag} event=qa_revive_off revives={Revives} failures={Failures} pending={_queue.Count}");
    }

    private void OnDestroy() => Stop();
}
#endif
