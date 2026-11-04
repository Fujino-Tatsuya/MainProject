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
/// <item>할 일 140-4 — 북진 중 간파 판정(<see cref="PlayerInterruptSkillBase.ServerInterruptResolved"/>)마다 <c>event=pn_server_cast</c>: 서버가 본 위치·실제/AABB 표면 거리·정면각·앵커 겹침.</item>
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

    /// <summary>할 일 119 C3 — 송전탑 구간(참여·파괴·탑 피해). 판 끝 <see cref="Stop"/> 에서 기믹 기록으로 이름을 붙인다.</summary>
    public readonly QABoss103PylonWaveTracker Pylons = new QABoss103PylonWaveTracker();
    /// <summary>할 일 119 C3 — 실패 사유 근거(판정 순간 상태). 전멸이면 마지막으로 생존 0 이 된 순간(전멸 리스폰 전) 상태.</summary>
    public QABoss103FailSnapshot FailSnapshot { get; private set; }

    private readonly Dictionary<ulong, PlayerLifeState> _lifeOf = new Dictionary<ulong, PlayerLifeState>();
    private QABoss103FailSnapshot _zeroAliveSnapshot;
    private bool _wiped;

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
        QABoss103StuckProbe.HostReset();
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
        PlayerInterruptSkillBase.ServerInterruptSucceeded += OnInterruptSucceeded;
        PlayerInterruptSkillBase.ServerInterruptResolved += OnInterruptResolved;
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
        PlayerInterruptSkillBase.ServerInterruptSucceeded -= OnInterruptSucceeded;
        PlayerInterruptSkillBase.ServerInterruptResolved -= OnInterruptResolved;
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
            PlayerLifeCycleController life = p.GetComponent<PlayerLifeCycleController>();
            if (life != null && !_lifeOf.ContainsKey(p.OwnerClientId))
                _lifeOf[p.OwnerClientId] = life.State;
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
        if (target is BossChargingPylon)
            Pylons.Damage(amount);
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

    // ── 할 일 140-4 — 북진 중 서버 간파 판정 관측(기록 전용 event=pn_server_cast) ──

    private static readonly FieldInfo RaidGimmickField = typeof(TwentyThreeBoss).GetField("_raidGimmick", AnyInst);
    private static readonly FieldInfo GimmickStepField = typeof(TwentyThreeBoss).GetField("_gStep", AnyInst);
    private static readonly FieldInfo BossDataField = typeof(TwentyThreeBoss).GetField("_boss", AnyInst);
    private readonly Collider[] _anchorHits = new Collider[16];
    private Player _lastInterruptSucceeded;

    /// <summary>같은 판정 안에서 <see cref="OnInterruptResolved"/> 보다 먼저 난다 — 성공 여부를 넘긴다.</summary>
    private void OnInterruptSucceeded(Player owner, PlayerInterruptSkillBase skill) => _lastInterruptSucceeded = owner;

    /// <summary>
    /// [서버] 간파 판정 1회 — 23호가 북진 중일 때만, 서버가 본 시전자 위치·보스 HurtBox 실제/AABB 표면 거리·전방위 사거리·정면각·
    /// 판정 앵커가 지금 HurtBox 에 겹치나를 한 줄로. 수신 0(<c>resolved=0</c>) + <c>overlap=false</c> = 판정 상자가 보스에 안 닿았다.
    /// </summary>
    private void OnInterruptResolved(Player owner, int resolvedCount)
    {
        bool success = _lastInterruptSucceeded == owner;
        _lastInterruptSucceeded = null;
        if (_stopped || owner == null || _boss == null || RaidGimmickField == null)
            return;
        object g = RaidGimmickField.GetValue(_boss);
        if (g == null || g.ToString() != "PushNorth")
            return;

        Hurtbox hb = _boss.GetComponentInChildren<Hurtbox>();
        Collider hurt = hb != null ? hb.GetComponent<Collider>() : null;
        Vector3 pos = owner.transform.position, bossPos = _boss.transform.position;
        double trueSurf = double.NaN, aabbSurf = double.NaN;
        if (hurt != null && hurt.enabled)
        {
            Vector3 p = new Vector3(pos.x, hurt.bounds.center.y, pos.z);
            trueSurf = QAPartyPlacement88.HorizontalDistance(p, hurt.ClosestPoint(p));
            Bounds b = hurt.bounds;
            aabbSurf = QABoss103PushNorthCounter.AabbSurfaceDistance(pos.x, pos.z, b.min.x, b.min.z, b.max.x, b.max.z);
        }
        PlayerSkillController skills = owner.GetComponent<PlayerSkillController>();
        double reach = QABoss103CombatBot.CounterReach(owner, skills);
        ColliderInfo anchor = skills != null ? skills.GetSkill(PlayerSkillSlot.Interrupt)?.HitboxAnchor : null;
        string overlap = anchor == null || hurt == null ? "na" : AnchorTouches(anchor, hurt) ? "true" : "false";

        Vector3 to = pos - bossPos;
        Vector3 fwd = _boss.transform.forward;
        double frontDeg = QAInterruptLag88Plan.FlatAngleDeg(fwd.x, fwd.z, to.x, to.z);
        BossDataSO data = BossDataField != null ? BossDataField.GetValue(_boss) as BossDataSO : null;
        double limit = data == null ? double.NaN : data.counterRequiresFront ? data.counterFrontAngle : 180.0;

        int slot = SlotOf(owner.OwnerClientId);
        string bot = slot >= 0 ? ((QABoss103Character)slot).ToString() : "client" + owner.OwnerClientId;
        object step = GimmickStepField != null ? GimmickStepField.GetValue(_boss) : null;
        Debug.Log(QABoss103PushNorthCounter.FormatServerCastLine(Tag, bot, slot, step?.ToString(), _boss.CounterWindowOpen, resolvedCount, success,
            pos.x, pos.z, bossPos.x, bossPos.z, trueSurf, aabbSurf, reach, frontDeg, limit, overlap, Now));
    }

    /// <summary>판정 앵커 형상(지금 월드 값)이 <paramref name="hurt"/> 에 겹치나 — 서버 판정(OverlapHitboxAnchor)과 같은 질의를 그 콜라이더 층으로만.</summary>
    private bool AnchorTouches(ColliderInfo anchor, Collider hurt)
    {
        int mask = 1 << hurt.gameObject.layer;
        int n;
        switch (anchor.OverlapCollider)
        {
            case OverlapCollider.Box:
                BoxColliderInfo box = default;
                anchor.GetBoxColliderInfo(ref box);
                n = Physics.OverlapBoxNonAlloc(box.center, box.halfExtents, _anchorHits, box.orientation, mask, QueryTriggerInteraction.Collide);
                break;
            case OverlapCollider.Sphere:
                SphereColliderInfo sphere = default;
                anchor.GetSphereColliderInfo(ref sphere);
                n = Physics.OverlapSphereNonAlloc(sphere.center, sphere.radius, _anchorHits, mask, QueryTriggerInteraction.Collide);
                break;
            case OverlapCollider.Capsule:
                CapsuleColliderInfo capsule = default;
                anchor.GetCapsuleColliderInfo(ref capsule);
                n = Physics.OverlapCapsuleNonAlloc(capsule.point0, capsule.point1, capsule.radius, _anchorHits, mask, QueryTriggerInteraction.Collide);
                break;
            default:
                return false;
        }
        for (int i = 0; i < n; i++)
            if (_anchorHits[i] == hurt)
                return true;
        return false;
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
        {
            Stats.Death(SlotOf(clientId), Now);
            // 할 일 120 — 야간 QA 이벤트 시각표·리플레이 클립의 '사망'(QANightlyEvents.Death).
            Debug.Log($"{Tag} event=death slot={SlotOf(clientId)} client={clientId} state={current}");
        }
        // 부활(TryCompleteReviveOnServer)은 Alive 전환 → 목숨 차감 → ServerReviveCompleted 순서다. 차감 전 값을 여기서 잡는다.
        if (previous == PlayerLifeState.Soul && current == PlayerLifeState.Alive)
            _livesAtRevive[clientId] = _rule != null ? _rule.PartyLives : -1;
        if (previous != current)
            Activity.Note("life_" + current);
        _lifeOf[clientId] = current;
        // 할 일 119 C3 — 생존 0 순간(전멸 리스폰이 살리기 전) 상태를 남긴다. 목숨이 남아 부활하면 다음 0 순간이 덮어쓴다.
        if (previous == PlayerLifeState.Alive && AliveCount() == 0)
            _zeroAliveSnapshot = Capture();
    }

    private int AliveCount()
    {
        if (_lifeOf.Count == 0)
            return -1;
        int n = 0;
        foreach (KeyValuePair<ulong, PlayerLifeState> kv in _lifeOf)
            if (SlotOf(kv.Key) >= 0 && kv.Value == PlayerLifeState.Alive)
                n++;
        return n;
    }

    /// <summary>할 일 119 C3 — 지금 보스 공격·상태·페이즈, 생존 인원, 공용 목숨, 보스 HP.</summary>
    private QABoss103FailSnapshot Capture()
    {
        var s = new QABoss103FailSnapshot
        {
            Alive = AliveCount(),
            Lives = _rule != null ? _rule.PartyLives : Stats.LivesNow,
            BossHpPct = BossHpPct(),
            ServerT = Now,
            LastAttack = Stats.AttackStarts.Count > 0 ? Stats.AttackStarts[Stats.AttackStarts.Count - 1].Value : "",
        };
        if (_boss != null)
        {
            s.BossState = _boss.State.ToString();
            s.BossPhase = _boss.CurrentPhase;
            if (_boss.State == MonsterState.Attack && CurrentEntryField != null && CurrentEntryField.GetValue(_boss) is BossAttackEntry e)
            {
                s.BossAttack = e.attackId.ToString();
                s.BossAttackPhase = _boss.AttackPhase.ToString();
            }
        }
        return s;
    }

    /// <summary>[서버] 송전탑 구간 — 참여 탑 수·그중 부서진 수. 송전기 시퀀스 중이면 이름 '송전기'(나머지는 판 끝에 기믹 기록으로).</summary>
    private void TickPylons(double now)
    {
        int engaged = 0, destroyed = 0;
        foreach (BossChargingPylon p in BossChargingPylon.Active)
        {
            if (p == null || !p.IsEngaged)
                continue;
            engaged++;
            if (p.WasDestroyed)
                destroyed++;
        }
        string label = _boss != null && _boss.IsSpawned && _boss.IsChargeSequenceActive ? QABoss103FailReason.PylonCharge : null;
        Pylons.Tick(now, engaged, destroyed, label);
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
            _wiped = true;
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
        TickPylons(now);

        // 할 일 140 — 끼임 계측: 플레이어마다 구속(23호 잡기·밀기) 전환을 본다. 재배치 줄의 grab= 이 이 기록을 읽는다(같은 realtime 기준).
        double real = Time.realtimeSinceStartupAsDouble;
        foreach (Player p in _landed.Keys)
            if (p != null)
                QABoss103StuckProbe.HostObserve(p.OwnerClientId, p, real);
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
        // 할 일 119 C3 — 전멸 리스폰이 이미 살렸으면 생존 0 순간 상태, 아니면 지금 상태.
        FailSnapshot = _wiped && _zeroAliveSnapshot != null ? _zeroAliveSnapshot : Capture();
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
        QABoss103FailReason.LabelWaves(Pylons.Waves, merged);
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
