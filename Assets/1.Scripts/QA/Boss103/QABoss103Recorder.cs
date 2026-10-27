#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 할 일 103 C4 — [호스트] 판 계측기. Combat 진입에 만들고 판정 순간 <see cref="Stop"/>. 게임 코드는 바꾸지 않는다 — 공개 이벤트 구독 + 폴링 + 리플렉션만.
/// <list type="bullet">
/// <item>보스 피해(<see cref="CombatStatsEvents.ServerDamageApplied"/>, 대상 23호) · 제압 중/어쌔신 변신 중 분리 · 행동별 피해(23호 <see cref="Unit.ClientDamagedAttributed"/> — 호스트도 받는 RPC)</item>
/// <item>행동 사용(<see cref="Player.ServerBasicAttackStarted"/>·<see cref="PlayerSkillController.ServerSkillUsed"/>) · 적중(<see cref="Player.ServerAttackLanded"/>, 판정 1회 = 1)</item>
/// <item>받은 피해(<see cref="CombatStatsEvents.ServerHealthLost"/>) · 피격·패턴(<see cref="CombatStatsEvents.ServerDamageApplied"/> 대상 플레이어 — 호출 스택 타입으로
///   장판·드론·폭탄·차징 장판·잡기, 23호 본체면 그 순간 <c>_currentEntry.attackId</c>) · 사망(<see cref="PlayerLifeCycleController.ServerLifeStateChanged"/>)</item>
/// <item>간파 성공(<see cref="CombatStatsEvents.ServerCounterSucceeded"/>) · 제압(<see cref="TwentyThreeBoss.IsSuppressed"/> 상승 엣지) · 변신(<see cref="AssassinState.IsTransformed"/> 폴링)
///   · 공용 목숨(<see cref="MultiGameRule.PartyLivesChanged"/>) · 전멸(<see cref="PartyWipeRespawner.PartyWipeRespawned"/>)</item>
/// <item>공격 시간표(보스 상태 Attack 진입·공격 행 교체) — 클라 봇 굴림을 공격 id 로 잇는다. 전투 기록(<c>[23호/전투기록]</c> 로그, 없으면 보스 <c>_fightLog</c>)은 기믹 파싱용.</item>
/// <item>C5 qa 브랜치 전용 게임 알림: 팔라딘 방어(<see cref="FirstMeleeSubSkill.QA103GuardAbsorbed"/>) · 23호 기믹 결과(<see cref="TwentyThreeBoss.QA103GimmickResult"/>, source=event —
///   전투 기록 파싱 행과 중복 제거, <see cref="QABoss103GimmickNotify.Merge"/>). 상세 Docs/tech/handoff-qa103-notify.md.</item>
/// </list>
/// </summary>
public sealed class QABoss103Recorder : MonoBehaviour
{
    private const string Tag = QABoss103Plan.Tag;
    private const string FightLogPrefix = "[23호/전투기록]";
    private const string BossLogPrefix = "[23호/";
    private const float SnapshotSeconds = 5f;
    private const float RescanSeconds = 1f;
    private const BindingFlags AnyInst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static readonly FieldInfo CurrentEntryField = typeof(TwentyThreeBoss).GetField("_currentEntry", AnyInst);
    private static readonly FieldInfo FightLogField = typeof(TwentyThreeBoss).GetField("_fightLog", AnyInst);

    public readonly QABoss103RunStats Stats = new QABoss103RunStats();
    /// <summary>C7f — 정체 감시용 활동 누계(<see cref="QABoss103StallWatch"/>).</summary>
    public readonly QABoss103Activity Activity = new QABoss103Activity();

    private readonly Dictionary<ulong, int> _slotOf = new Dictionary<ulong, int>();
    private readonly Dictionary<Player, Action<PlayerAttackLanded>> _landed = new Dictionary<Player, Action<PlayerAttackLanded>>();
    private readonly Dictionary<PlayerReviveController, Action<int>> _revived = new Dictionary<PlayerReviveController, Action<int>>();
    /// <summary>할 일 119 — Soul → Alive 순간(목숨 차감 직전)의 공용 목숨. 부활 완료 이벤트의 남은 목숨과 빼서 이번 부활이 쓴 목숨을 잰다.</summary>
    private readonly Dictionary<ulong, int> _livesAtRevive = new Dictionary<ulong, int>();
    private readonly AssassinState[] _assassin = new AssassinState[3];
    private readonly List<string> _frames = new List<string>(32);
    private readonly List<QABoss103GimmickEvent> _eventGimmicks = new List<QABoss103GimmickEvent>();

    private TwentyThreeBoss _boss;
    private MultiGameRule _rule;
    private bool _stopped;
    private float _nextRescan, _nextSnapshot;
    private BossAttackEntry _lastEntry;
    private bool _wasAttack;
    private string _fightLogCaptured, _fightLogSnapshot;
    private double _combatServerT;
    private int _lastBossHp = int.MinValue, _lastFightLogLength = -1;

    public bool CurrentEntryReadable => CurrentEntryField != null;

    /// <summary>C7g — 마지막으로 본 보스 HP(모르면 <see cref="int.MinValue"/>).</summary>
    public int BossHp => _lastBossHp;

    /// <summary>호스트 — Combat 진입 순간 만든다. <paramref name="characters"/> = clientId → 캐릭터(자리).</summary>
    public static QABoss103Recorder Begin(IReadOnlyDictionary<ulong, QABoss103Character> characters)
    {
        var go = new GameObject("[QA] Boss103Recorder");
        DontDestroyOnLoad(go);
        QABoss103Recorder r = go.AddComponent<QABoss103Recorder>();
        foreach (KeyValuePair<ulong, QABoss103Character> kv in characters)
            r._slotOf[kv.Key] = (int)kv.Value;
        r._combatServerT = Now;
        r.Stats.Begin(r._combatServerT);
        r.Stats.GuardNotifyOn();
        r.Subscribe();
        r.Rescan();
        Debug.Log($"{Tag} event=recorder_start players={r._slotOf.Count} currentEntry={(CurrentEntryField != null ? "ok" : "missing")} fightLog={(FightLogField != null ? "ok" : "missing")}");
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

    private int SlotOf(ulong clientId) => _slotOf.TryGetValue(clientId, out int s) ? s : -1;

    // ── 구독 ─────────────────────────────────────────────────

    private void Subscribe()
    {
        CombatStatsEvents.ServerDamageApplied += OnDamageApplied;
        CombatStatsEvents.ServerHealthLost += OnHealthLost;
        CombatStatsEvents.ServerCounterSucceeded += OnCounterSucceeded;
        PlayerSkillController.ServerSkillUsed += OnSkillUsed;
        Player.ServerBasicAttackStarted += OnBasicStarted;
        PlayerLifeCycleController.ServerLifeStateChanged += OnLifeChanged;
        PartyWipeRespawner.PartyWipeRespawned += OnWipe;
        Application.logMessageReceived += OnLog;
        FirstMeleeSubSkill.QA103GuardAbsorbed += OnGuardAbsorbed;
        TwentyThreeBoss.QA103GimmickResult += OnGimmickResult;
    }

    private void Unsubscribe()
    {
        CombatStatsEvents.ServerDamageApplied -= OnDamageApplied;
        CombatStatsEvents.ServerHealthLost -= OnHealthLost;
        CombatStatsEvents.ServerCounterSucceeded -= OnCounterSucceeded;
        PlayerSkillController.ServerSkillUsed -= OnSkillUsed;
        Player.ServerBasicAttackStarted -= OnBasicStarted;
        PlayerLifeCycleController.ServerLifeStateChanged -= OnLifeChanged;
        PartyWipeRespawner.PartyWipeRespawned -= OnWipe;
        Application.logMessageReceived -= OnLog;
        FirstMeleeSubSkill.QA103GuardAbsorbed -= OnGuardAbsorbed;
        TwentyThreeBoss.QA103GimmickResult -= OnGimmickResult;
        foreach (KeyValuePair<Player, Action<PlayerAttackLanded>> kv in _landed)
            if (kv.Key != null)
                kv.Key.ServerAttackLanded -= kv.Value;
        _landed.Clear();
        foreach (KeyValuePair<PlayerReviveController, Action<int>> kv in _revived)
            if (kv.Key != null)
                kv.Key.ServerReviveCompleted -= kv.Value;
        _revived.Clear();
        if (_boss != null)
            _boss.ClientDamagedAttributed -= OnBossAttributed;
        if (_rule != null)
            _rule.PartyLivesChanged -= OnLives;
    }

    /// <summary>플레이어·보스·목숨 규칙이 새로 생겼으면 붙는다.</summary>
    private void Rescan()
    {
        foreach (Player p in QALobbyFlow.SpawnedPlayers())
        {
            if (p == null || _landed.ContainsKey(p))
                continue;
            int slot = SlotOf(p.OwnerClientId);
            Action<PlayerAttackLanded> h = landed => Stats.Landed(slot, ActionOf(landed.AttackType));
            p.ServerAttackLanded += h;
            _landed[p] = h;
            if (slot >= 0 && slot < _assassin.Length)
                _assassin[slot] = p.GetComponent<AssassinState>();
            PlayerReviveController revive = p.GetComponent<PlayerReviveController>();
            if (revive != null && !_revived.ContainsKey(revive))
            {
                ulong id = p.OwnerClientId;
                Action<int> r = left => OnRevived(id, left);
                revive.ServerReviveCompleted += r;
                _revived[revive] = r;
            }
        }

        if (_boss == null)
        {
            _boss = FindAnyObjectByType<TwentyThreeBoss>();
            if (_boss != null)
                _boss.ClientDamagedAttributed += OnBossAttributed;
        }

        MultiGameRule rule = MultiGameRule.Current;
        if (rule != _rule)
        {
            if (_rule != null)
                _rule.PartyLivesChanged -= OnLives;
            _rule = rule;
            if (_rule != null)
            {
                _rule.PartyLivesChanged += OnLives;
                Stats.Lives(_rule.PartyLives);
            }
        }
    }

    // ── 핸들러 ───────────────────────────────────────────────

    private void OnDamageApplied(Unit target, ulong attacker, int amount, bool lethal)
    {
        if (_stopped || amount <= 0 || target == null)
            return;
        if (target is TwentyThreeBoss boss)
        {
            Stats.DamageDealt(SlotOf(attacker), amount, boss.IsSuppressed);
            Activity.Note("boss_damage");
            return;
        }
        if (target is Player p)
        {
            Stats.PlayerHit(SlotOf(p.OwnerClientId), Now, ClassifyHit());
            Activity.Note("player_hit");
            return;
        }
        Activity.Note("other_damage");   // 송전탑 등 보스 외 유닛
    }

    private void OnHealthLost(Unit target, int amount)
    {
        if (!_stopped && target is Player p)
        {
            Stats.HealthLost(SlotOf(p.OwnerClientId), amount);
            Activity.Note("health_lost");
        }
    }

    private void OnCounterSucceeded(Unit target, ulong attacker)
    {
        if (!_stopped)
        {
            Stats.CounterSuccess(SlotOf(attacker), Now);
            Activity.Note("counter");
        }
    }

    private void OnSkillUsed(Player p, PlayerSkillSlot slot)
    {
        if (_stopped || p == null)
            return;
        QABoss103Action a = slot == PlayerSkillSlot.Main ? QABoss103Action.Q
            : slot == PlayerSkillSlot.Sub ? QABoss103Action.E
            : slot == PlayerSkillSlot.Ultimate ? QABoss103Action.R
            : QABoss103Action.RClick;
        Stats.Use(SlotOf(p.OwnerClientId), a);
    }

    private void OnBasicStarted(Player p)
    {
        if (!_stopped && p != null)
            Stats.Use(SlotOf(p.OwnerClientId), QABoss103Action.Basic);
    }

    private void OnBossAttributed(DamageDealtInfo info)
    {
        if (!_stopped)
            Stats.TypedDamage(SlotOf(info.attackerClientId), ActionOf(info.attackType), info.amount);
    }

    private void OnLifeChanged(ulong clientId, PlayerLifeState previous, PlayerLifeState current)
    {
        if (_stopped)
            return;
        if (previous == PlayerLifeState.Alive && current != PlayerLifeState.Alive)
            Stats.Death(SlotOf(clientId), Now);
        // 부활(TryCompleteReviveOnServer)은 Alive 전환 → 목숨 차감 → ServerReviveCompleted 순서다. 차감 전 값을 여기서 잡는다.
        if (previous == PlayerLifeState.Soul && current == PlayerLifeState.Alive)
            _livesAtRevive[clientId] = _rule != null ? _rule.PartyLives : -1;
        if (previous != current)
            Activity.Note("life_" + current);
    }

    /// <summary>
    /// 할 일 119 — [서버] 부활 완료(봇의 F10 요청 → <see cref="PlayerReviveController.TryCompleteReviveOnServer()"/>). 캐릭터별 부활 수 + 이번 부활이 쓴 공용 목숨.
    /// F10 경로는 목숨 1 을 쓰고 0 이면 거부한다 — 1 이 아니면 runs.csv note 로 드러낸다(QA 가 목숨을 고치지는 않는다).
    /// </summary>
    private void OnRevived(ulong clientId, int livesLeft)
    {
        if (_stopped)
            return;
        int before = _livesAtRevive.TryGetValue(clientId, out int b) ? b : -1;
        _livesAtRevive.Remove(clientId);
        int used = before >= 0 ? before - livesLeft : -1;
        int slot = SlotOf(clientId);
        Stats.QaRevive(slot, used);
        Debug.Log($"{Tag} event=qa_revive path=f10 character={(slot >= 0 ? QABoss103Plan.CharacterForSlot(slot).ToString() : "na")} client={clientId} " +
                  $"livesBefore={(before >= 0 ? before.ToString() : "na")} livesLeft={livesLeft} livesUsed={(used >= 0 ? used.ToString() : "na")}" +
                  (used >= 0 && used != 1 ? " note=" + QABoss103RunStats.ReviveLifeMismatchNote : ""));
    }

    private void OnLives(int lives)
    {
        if (!_stopped)
        {
            Stats.Lives(lives);
            Activity.Note("lives");
        }
    }

    private void OnWipe(IReadOnlyList<ulong> _)
    {
        if (!_stopped)
        {
            Stats.Wipe();
            Activity.Note("wipe");
        }
    }

    private void OnGuardAbsorbed(Player p, GuardVerdict verdict)
    {
        if (!_stopped && p != null)
            Stats.GuardAbsorbed(SlotOf(p.OwnerClientId), verdict == GuardVerdict.BlockAndCounter);
    }

    private void OnGimmickResult(TwentyThreeBoss boss, string gimmick, string result, int value)
    {
        if (!_stopped)
        {
            _eventGimmicks.Add(QABoss103GimmickNotify.FromEvent(gimmick, result, value, Now, _combatServerT));
            Activity.Note("gimmick");
        }
    }

    private void OnLog(string condition, string stackTrace, LogType type)
    {
        if (condition != null && condition.StartsWith(FightLogPrefix, StringComparison.Ordinal))
            _fightLogCaptured = condition;
        else if (!_stopped && condition != null && condition.StartsWith(BossLogPrefix, StringComparison.Ordinal))
            Activity.Note("boss_log");   // C7f — [23호/북진] 회차 차징·실패 등 기믹 진행
    }

    /// <summary>평타 = Default, 우클릭 = 슬롯 밖 Skill(<c>PlayerSkillBase.DamageAttackType</c>), 미지정은 평타로 본다.</summary>
    private static QABoss103Action ActionOf(AttackType t)
    {
        switch (t)
        {
            case AttackType.SkillQ: return QABoss103Action.Q;
            case AttackType.SkillE: return QABoss103Action.E;
            case AttackType.SkillR: return QABoss103Action.R;
            case AttackType.Skill: return QABoss103Action.RClick;
            default: return QABoss103Action.Basic;
        }
    }

    /// <summary>피해 적용 호출 스택의 선언 타입(안쪽부터) → 패턴 키(<see cref="QABoss103Metrics.ClassifySource"/>). QA 전용 — 피격 때만.</summary>
    private string ClassifyHit()
    {
        _frames.Clear();
        StackFrame[] frames = new StackTrace(2, false).GetFrames();
        if (frames != null)
            foreach (StackFrame f in frames)
            {
                Type t = f.GetMethod()?.DeclaringType;
                if (t != null)
                    _frames.Add(t.FullName);
            }
        return QABoss103Metrics.ClassifySource(_frames, CurrentAttackId());
    }

    private string CurrentAttackId()
    {
        if (_boss == null || CurrentEntryField == null)
            return null;
        return CurrentEntryField.GetValue(_boss) is BossAttackEntry e ? e.attackId.ToString() : null;
    }

    // ── 폴링 ─────────────────────────────────────────────────

    private void Update()
    {
        if (_stopped)
            return;
        if (Time.realtimeSinceStartup >= _nextRescan)
        {
            _nextRescan = Time.realtimeSinceStartup + RescanSeconds;
            Rescan();
        }
        double now = Now;

        if (_boss != null && _boss.IsSpawned)
        {
            Stats.Suppressed(_boss.IsSuppressed);
            bool attack = _boss.State == MonsterState.Attack;
            BossAttackEntry entry = CurrentEntryField != null ? CurrentEntryField.GetValue(_boss) as BossAttackEntry : null;
            if (attack && entry != null && (!_wasAttack || !ReferenceEquals(entry, _lastEntry)))
                Stats.AttackStart(now, entry.attackId.ToString());
            _wasAttack = attack;
            _lastEntry = attack ? entry : null;

            // C7f — 보스 HP 변화 · 전투 기록 줄 추가(북진 회차·릴레이 진행)도 정체 감시 활동.
            int hp = _boss.CurrentHealth;
            if (_lastBossHp != int.MinValue && hp != _lastBossHp)
                Activity.Note("boss_hp");
            _lastBossHp = hp;
            int logLength = FightLogField != null && FightLogField.GetValue(_boss) is StringBuilder log ? log.Length : -1;
            if (_lastFightLogLength >= 0 && logLength >= 0 && logLength != _lastFightLogLength)
                Activity.Note("fight_log");
            _lastFightLogLength = logLength;

            if (Time.realtimeSinceStartup >= _nextSnapshot)
            {
                _nextSnapshot = Time.realtimeSinceStartup + SnapshotSeconds;
                string snap = ReadFightLog();
                if (!string.IsNullOrEmpty(snap))
                    _fightLogSnapshot = snap;
            }
        }

        for (int s = 0; s < _assassin.Length; s++)
            if (_assassin[s] != null)
                Stats.Transform(s, _assassin[s].IsTransformed, now);
    }

    private string ReadFightLog()
    {
        if (_boss == null || FightLogField == null)
            return null;
        return FightLogField.GetValue(_boss) is StringBuilder sb ? sb.ToString() : null;
    }

    /// <summary>판정 순간 — 집계를 닫고 전투 기록을 기믹으로 푼다. 보스 체력 %(모르면 NaN).</summary>
    public double Stop()
    {
        if (_stopped)
            return BossHpPct();
        Update();   // 마지막 프레임 상태(변신·제압)
        _stopped = true;
        Stats.End(Now);
        Unsubscribe();

        string source = "captured";
        string text = _fightLogCaptured;
        if (string.IsNullOrEmpty(text))
        {
            text = ReadFightLog();
            source = "reflection";
        }
        if (string.IsNullOrEmpty(text))
        {
            text = _fightLogSnapshot;
            source = "snapshot";
        }
        List<QABoss103GimmickEvent> gimmicks = QABoss103FightLog.Parse(text, out int logSuppress);
        foreach (QABoss103GimmickEvent g in gimmicks)
            if (!double.IsNaN(g.FightT))
                g.ServerT = _combatServerT + g.FightT;   // 근사 — 전투 기록 개전 ≈ Combat 진입
        List<QABoss103GimmickEvent> merged = QABoss103GimmickNotify.Merge(_eventGimmicks, gimmicks);
        Stats.Gimmicks.AddRange(merged);
        Debug.Log($"{Tag} event=recorder_stop fightLog={(string.IsNullOrEmpty(text) ? "none" : source)} gimmicks={merged.Count} gimmickEvents={_eventGimmicks.Count} gimmickLogs={gimmicks.Count} " +
                  $"guard={Stats.Chars[(int)QABoss103Character.Paladin].GuardSuccess} suppress={Stats.SuppressCount} logSuppress={logSuppress} " +
                  $"hits={Stats.Hits.Count} deaths={Stats.DeathsTotal()} attackStarts={Stats.AttackStarts.Count} dealt={Stats.DamageDealtTotal()}");
        return BossHpPct();
    }

    private double BossHpPct()
    {
        TwentyThreeBoss boss = _boss != null ? _boss : FindAnyObjectByType<TwentyThreeBoss>();
        if (boss == null || boss.FinalMaxHp <= 0)
            return double.NaN;
        return 100.0 * Mathf.Max(0, boss.CurrentHealth) / boss.FinalMaxHp;
    }

    private void OnDestroy()
    {
        if (!_stopped)
        {
            _stopped = true;
            Unsubscribe();
        }
    }
}
#endif
