#if UNITY_EDITOR
using System.Globalization;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 할 일 103 C2 — 인스턴스마다(호스트·가상 플레이어) 자기 로컬 플레이어로 23호와 싸우는 전투 봇(공격 쪽만). 플레이어 이동·스킬은 오너 권한이라 오너 클라에서 돈다.
/// <list type="bullet">
/// <item>대상 = <see cref="TwentyThreeBoss"/>(웰즈·드론은 피격 대상이 아니다). 보스가 Combat 이 아니거나, 내가 연출 잠금·사망·영혼이면 아무것도 안 한다.</item>
/// <item>거리 유지(<see cref="QABoss103Bot.DecideMove"/>, 보스 HurtBox 표면까지 수평 거리): 근접 = 2m 넘으면 1m 까지 붙음, 거너 = 6~9m(벗어나면 7.5m 로).
///   멈춰 있을 때만 평타(누르고 있음 — <see cref="QABoss103InputDriver.HoldBasic"/>)와 스킬을 쓴다.</item>
/// <item>이동: 클라엔 NavMesh 가 없어 직선 이동 + 앞 바닥 레이캐스트(88 Approach 와 같은 낭떠러지 검사). 이동 중 2초 동안 0.3m 미만이면 옆걸음 0.8초(<see cref="QABoss103StuckDetector"/>).
///   대시는 이동에 쓰지 않는다 — 회피 전용(C3, <c>QABoss103CombatBot.Evasion.cs</c>).</item>
/// <item>스킬 = Q(Main)·R(Ultimate). 팔라딘 E(방어)·우클릭 간파·대시 회피는 C3 파일. 쿨이 돌면 프로필 확률로 굴리고 실패면 1초 뒤 다시(<see cref="QABoss103SkillGate"/>).
///   팔라딘 Q = 홀드(스킬이 끝날 때까지, 상한 <see cref="HoldCapSeconds"/>), 거너 Q = 정신 집중 → 최대 충전(<c>MaxChargeTime</c>) 뒤 좌클릭 발사,
///   R(팔라딘·거너 = 대상 지정) = 23호 지정, 어쌔신 R = 변신(변신 중엔 R 을 다시 누르지 않는다 — Q·평타는 게임이 변신 판으로 바꾼다).</item>
/// <item>조준 오차: 보스 위치(또는 스킬 목표점)에 프로필 반경 원판 균일 오프셋(<see cref="QABoss103Bot.AimOffset"/>) — 평타는 <see cref="AimResampleSeconds"/> 마다,
///   스킬은 시전마다 새로 굴린다.</item>
/// </list>
/// 로그: 시작 <c>[QA103] event=bot_profile</c>, 주기 <c>event=bot_diag</c>, 끝 <c>event=bot_combat_done</c>.
/// </summary>
public sealed partial class QABoss103CombatBot : MonoBehaviour
{
    private const string Tag = QABoss103Plan.Tag;
    private const float DiagSeconds = 10f;
    private const float RetargetSeconds = 1f;
    /// <summary>평타 조준 오프셋 다시 굴리는 간격(초).</summary>
    public const float AimResampleSeconds = 0.5f;
    /// <summary>팔라딘 Q 홀드 상한(초) — 스킬 자체 최대 시간이 먼저 끝낸다. 안전망.</summary>
    public const float HoldCapSeconds = 4f;
    /// <summary>거너 Q 최대 충전 뒤 발사까지 여유(초).</summary>
    private const float ChargeFireSlack = 0.05f;

    private static readonly int GroundMask = LayerMask.GetMask("Default", "Ground", "Env");

    private QABoss103Character _character;
    private int _slot;
    private QABoss103BotProfile _profile;
    private QABoss103Bot.RangeBand _band;
    private System.Random _aimRng, _skillRng;
    private QAInputController _input;
    private readonly QABoss103InputDriver _driver = new QABoss103InputDriver();

    private TwentyThreeBoss _boss;
    private Collider _bossBody;
    private Collider _bossHurt;
    private float _bossRadius;
    private float _nextRetarget;

    private QABoss103Bot.Move _move = QABoss103Bot.Move.Hold;
    private readonly QABoss103StuckDetector _stuck = new QABoss103StuckDetector();
    private Vector3 _sidestepDir;
    private bool _edgeBlocked;

    private Vector3 _aimOffset;
    private float _nextAimResample;

    private readonly QABoss103SkillGate _gateQ = new QABoss103SkillGate();
    private readonly QABoss103SkillGate _gateR = new QABoss103SkillGate();
    private PlayerSkillSlot _holdingSlot = (PlayerSkillSlot)(-1);
    private float _holdStartedAt;
    private bool _sawHoldSkillActive;
    private float _nextCastAttempt;
    /// <summary>장전된 스킬이 시전 거부(사거리·조건)되면 다시 시도할 간격(초) — 굴림은 다시 하지 않는다.</summary>
    private const float CastRetrySeconds = 0.25f;
    private float _chargeSeenAt = -1f;
    private bool _chargeFired;

    private string _state = "init";
    private float _nextDiag;
    private bool _combatLogged;

    // 집계
    private int _attackStarts, _castsQ, _castsR, _castRejects, _gunnerFires, _edgeBlocks, _transforms;

    public QABoss103BotProfile Profile => _profile;

    public static QABoss103CombatBot Create(int runSeed, QABoss103Character character, QABoss103BotTier tier)
    {
        var go = new GameObject("[QA] Boss103CombatBot");
        DontDestroyOnLoad(go);
        var input = go.AddComponent<QAInputController>();
        QABoss103CombatBot bot = go.AddComponent<QABoss103CombatBot>();
        bot._input = input;
        bot._character = character;
        bot._slot = (int)character;
        bot._profile = QABoss103BotProfile.For(tier);
        bot._band = QABoss103Bot.BandFor(character);
        bot._aimRng = new System.Random(QABoss103Bot.Seed(runSeed, bot._slot, QABoss103Bot.Stream.Aim));
        bot._skillRng = new System.Random(QABoss103Bot.Seed(runSeed, bot._slot, QABoss103Bot.Stream.Skill));
        Debug.Log($"{Tag} event=bot_profile character={character} slot={bot._slot} runSeed={runSeed} {bot._profile.Describe()} " +
                  $"band={F(bot._band.Min)}/{F(bot._band.Settle)}/{F(bot._band.Max)} reflectMissing={bot._driver.Missing}");
        bot.InitEvasion(runSeed);
        return bot;
    }

    private void Update()
    {
        Player me = Player.LocalPlayer;
        if (me != _driver.Player)
        {
            _driver.Bind(me);
            _input.SetTarget(me);
        }

        string idle = IdleReason(me);
        if (idle != null)
        {
            Idle(idle);
            return;
        }
        if (!_combatLogged)
        {
            _combatLogged = true;
            Debug.Log($"{Tag} event=bot_combat_start character={_character} tier={_profile.Tier} boss={QABoss103Plan.Token(_boss.name)}");
        }

        TickCombat(me);

        if (Time.realtimeSinceStartup >= _nextDiag)
        {
            _nextDiag = Time.realtimeSinceStartup + DiagSeconds;
            LogDiag(me);
        }
    }

    /// <summary>싸우지 않을 이유(null = 싸운다).</summary>
    private string IdleReason(Player me)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || (!nm.IsConnectedClient && !nm.IsServer))
            return "offline";
        if (me == null)
            return "no_player";
        PlayerLifeCycleController life = me.GetComponent<PlayerLifeCycleController>();
        if (life != null && life.State != PlayerLifeState.Alive)
            return "life_" + life.State;
        PlayerEncounterLock encounterLock = me.GetComponent<PlayerEncounterLock>();
        if (encounterLock != null && (encounterLock.IsCinematicLocked || encounterLock.IsCinematicInputLocked))
            return "cinematic_lock";
        BossEncounterDirector d = BossEncounterDirector.Instance;
        if (d == null || !d.IsSpawned || d.Phase != BossEncounterPhase.Combat)
            return "phase_" + (d != null ? d.Phase.ToString() : "none");
        if (!ResolveBoss())
            return "no_boss";
        return null;
    }

    private void Idle(string reason)
    {
        if (_state != reason)
        {
            Debug.Log($"{Tag} event=bot_state character={_character} state={reason} prev={_state}");
            _state = reason;
        }
        _input.SetMove(Vector3.zero);
        _driver.ReleaseBasic();
        StopHold();
        FlushEvasion(reason);
        _move = QABoss103Bot.Move.Hold;
    }

    private bool ResolveBoss()
    {
        if (_boss != null && _boss.CurrentHealth > 0)
            return true;
        if (Time.realtimeSinceStartup < _nextRetarget)
            return false;
        _nextRetarget = Time.realtimeSinceStartup + RetargetSeconds;
        _boss = FindAnyObjectByType<TwentyThreeBoss>();
        if (_boss == null || _boss.CurrentHealth <= 0)
        {
            _boss = null;
            return false;
        }
        _bossBody = null;
        foreach (Collider c in _boss.GetComponents<Collider>())
            if (c != null && !c.isTrigger) { _bossBody = c; break; }
        Hurtbox hurt = _boss.GetComponentInChildren<Hurtbox>();
        _bossHurt = hurt != null ? hurt.GetComponent<Collider>() : null;
        return true;
    }

    // ── 전투 ───────────────────────────────────────────────────

    private void TickCombat(Player me)
    {
        if (_state != "combat")
        {
            Debug.Log($"{Tag} event=bot_state character={_character} state=combat prev={_state}");
            _state = "combat";
        }

        Vector3 myPos = me.transform.position;
        Vector3 bossPos = _boss.transform.position;
        float surface = SurfaceDistance(myPos, bossPos);
        // 체공(몸 콜라이더 꺼짐) 중엔 맞지 않는다 — 치지 않고 자리만 잡는다.
        bool hittable = _bossBody == null || _bossBody.enabled;

        // C3 — 위험 감지·회피·간파가 먼저. 대시 중·방어 유지 중엔 공격 쪽 입력을 넣지 않는다.
        double now = Time.realtimeSinceStartupAsDouble;
        TickEvasion(me, myPos, surface, now);
        TickCounter(me, myPos, surface, now);
        if (EvasionHoldsBot(now))
        {
            _input.SetMove(Vector3.zero);
            _driver.ReleaseBasic();
            return;
        }

        if (Time.time >= _nextAimResample)
        {
            _nextAimResample = Time.time + AimResampleSeconds;
            _aimOffset = SampleOffset();
        }

        PlayerSkillController skills = me.GetComponent<PlayerSkillController>();
        bool inSkill = TickActiveSkill(skills, bossPos);

        // 이동(스킬이 이동을 잠그는 동안은 넣지 않는다 — 거너 Q 집중은 이동 가능).
        _move = QABoss103Bot.DecideMove(surface, _band, _move);
        bool canMoveInSkill = !inSkill || (skills != null && skills.ActiveSkill != null && skills.ActiveSkill.CanMoveWhileActive);
        bool attackBusy = _driver.IsBasicActive;
        Vector3 moveDir = Vector3.zero;
        // 회피한 위험이 끝날 때까지는 다시 들어가지 않는다(물러나기는 그대로).
        bool avoid = _move == QABoss103Bot.Move.Approach && AvoidingApproach(now);
        if (_move != QABoss103Bot.Move.Hold && canMoveInSkill && !attackBusy && !avoid)
        {
            Vector3 toBoss = bossPos - myPos;
            toBoss.y = 0f;
            moveDir = _move == QABoss103Bot.Move.Approach ? toBoss.normalized : -toBoss.normalized;
        }
        Steer(myPos, moveDir);

        if (inSkill || !hittable)
        {
            _driver.ReleaseBasic();
            return;
        }

        // 공격은 거리 띠 안에서 멈춰 있을 때(또는 이미 공격 중).
        bool engaged = _move == QABoss103Bot.Move.Hold || attackBusy;
        if (!engaged)
        {
            _driver.ReleaseBasic();
            return;
        }

        _driver.SetAim(bossPos + _aimOffset);

        // 스킬 장전이 있으면 평타를 놓아 이번 타로 마무리하고, 끝나면 시전한다.
        bool wantQ = _gateQ.Tick(ReadyQ(skills), Time.timeAsDouble, _skillRng, _profile.SkillUseChance);
        bool wantR = _gateR.Tick(ReadyR(skills), Time.timeAsDouble, _skillRng, _profile.SkillUseChance);
        if (wantQ || wantR)
        {
            _driver.ReleaseBasic();
            if (!attackBusy && !skills.IsBusy && Time.time >= _nextCastAttempt)
            {
                _nextCastAttempt = Time.time + CastRetrySeconds;
                if (wantR && TryCastR(skills, bossPos))
                    return;
                if (wantQ && TryCastQ(skills, bossPos))
                    return;
            }
            if (attackBusy)
                return;
        }

        if (!skills.IsBusy && _driver.HoldBasic())
            _attackStarts++;
    }

    private bool ReadyQ(PlayerSkillController skills) =>
        skills != null && skills.GetSkill(PlayerSkillSlot.Main) != null && skills.IsCooldownReady(PlayerSkillSlot.Main);

    private bool ReadyR(PlayerSkillController skills)
    {
        if (skills == null || skills.GetSkill(PlayerSkillSlot.Ultimate) == null || !skills.IsCooldownReady(PlayerSkillSlot.Ultimate))
            return false;
        // 어쌔신 변신 중 R 재입력 = 수동 해제 — 쓰지 않는다.
        if (_character == QABoss103Character.Assassin)
        {
            AssassinState st = skills.GetComponent<AssassinState>();
            if (st != null && st.IsTransformed)
                return false;
        }
        return true;
    }

    private bool TryCastQ(PlayerSkillController skills, Vector3 bossPos)
    {
        Vector3 point = bossPos + SampleOffset();
        _driver.SetAim(point);
        bool hold = skills.GetSkill(PlayerSkillSlot.Main).Data.InputType == PlayerSkillInputType.Hold;
        if (hold)
            _driver.SetSkillHeld(PlayerSkillSlot.Main, true);
        bool ok = skills.ExecuteTargetedSkill(PlayerSkillSlot.Main, null, point, true);
        if (!ok)
        {
            if (hold)
                _driver.SetSkillHeld(PlayerSkillSlot.Main, false);
            _castRejects++;
            return false;
        }
        _gateQ.MarkUsed();
        _castsQ++;
        if (hold)
        {
            _holdingSlot = PlayerSkillSlot.Main;
            _holdStartedAt = Time.time;
            _sawHoldSkillActive = false;
        }
        _chargeSeenAt = -1f;
        _chargeFired = false;
        Debug.Log($"{Tag} event=bot_cast character={_character} slot=Q skill={skills.GetSkill(PlayerSkillSlot.Main).GetType().Name} hold={(hold ? "true" : "false")} " +
                  $"aimErrM={F(Flat(point - bossPos))} rolls={_gateQ.Rolls} fails={_gateQ.Fails}");
        return true;
    }

    private bool TryCastR(PlayerSkillController skills, Vector3 bossPos)
    {
        Vector3 point = bossPos + SampleOffset();
        _driver.SetAim(point);
        PlayerSkillBase r = skills.GetSkill(PlayerSkillSlot.Ultimate);
        bool targeted = r.Data.TargetingMode == SkillTargetingMode.SingleTarget;
        bool ok = skills.ExecuteTargetedSkill(PlayerSkillSlot.Ultimate, targeted ? _boss : null, point, true);
        if (!ok)
        {
            _castRejects++;
            return false;
        }
        _gateR.MarkUsed();
        _castsR++;
        if (r is AssassinTransformSkill)
            _transforms++;
        Debug.Log($"{Tag} event=bot_cast character={_character} slot=R skill={r.GetType().Name} target={(targeted ? "boss" : "point")} " +
                  $"aimErrM={F(Flat(point - bossPos))} rolls={_gateR.Rolls} fails={_gateR.Fails}");
        return true;
    }

    /// <summary>진행 중 스킬 돌보기(팔라딘 Q 홀드 놓기·거너 Q 발사). 스킬 실행·요청 중이면 true.</summary>
    private bool TickActiveSkill(PlayerSkillController skills, Vector3 bossPos)
    {
        if (skills == null)
            return false;
        PlayerSkillBase active = skills.ActiveSkill;

        if ((int)_holdingSlot >= 0)
        {
            if (active != null && active.Slot == _holdingSlot)
                _sawHoldSkillActive = true;
            bool ended = _sawHoldSkillActive && (active == null || active.Slot != _holdingSlot);
            bool timeout = Time.time - _holdStartedAt >= HoldCapSeconds;
            if (ended || timeout || (!skills.IsBusy && Time.time - _holdStartedAt > 1f))
                StopHold();
            else
                _driver.SetAim(bossPos + _aimOffset); // 홀드 동안 보스 쪽으로 조향
        }

        if (active is GunnerChargeLaserSkill charge && !_chargeFired)
        {
            if (_chargeSeenAt < 0f)
                _chargeSeenAt = Time.time;
            _driver.SetAim(bossPos + _aimOffset);
            float maxCharge = charge.Data is GunnerChargeLaserData data ? data.MaxChargeTime : 1f;
            if (Time.time - _chargeSeenAt >= maxCharge + ChargeFireSlack && _driver.SkillPrimary())
            {
                _chargeFired = true;
                _gunnerFires++;
                Debug.Log($"{Tag} event=bot_charge_fire character={_character} chargedS={F(Time.time - _chargeSeenAt)} maxChargeS={F(maxCharge)}");
            }
        }
        else if (active is GunnerChargeLaserSkill)
        {
            _driver.SetAim(bossPos + _aimOffset);
        }

        return skills.IsBusy;
    }

    private void StopHold()
    {
        if ((int)_holdingSlot < 0)
            return;
        _driver.SetSkillHeld(_holdingSlot, false);
        _holdingSlot = (PlayerSkillSlot)(-1);
    }

    // ── 이동 ───────────────────────────────────────────────────

    private void Steer(Vector3 myPos, Vector3 dir)
    {
        double now = Time.timeAsDouble;
        if (_stuck.Tick(now, myPos.x, myPos.z, dir != Vector3.zero))
        {
            QABoss103StuckDetector.SideDirection(dir.x, dir.z, _stuck.Side, out float sx, out float sz);
            _sidestepDir = new Vector3(sx, 0f, sz);
            Debug.Log($"{Tag} event=bot_stuck character={_character} n={_stuck.Escapes} side={(_stuck.Side > 0 ? "left" : "right")} pos={Fmt(myPos)}");
        }
        if (_stuck.IsSidestepping(now))
            dir = _sidestepDir;

        if (dir == Vector3.zero)
        {
            _input.SetMove(Vector3.zero);
            return;
        }
        bool safe = IsSafeStep(myPos, dir.normalized);
        if (!safe && !_edgeBlocked)
        {
            _edgeBlocks++;
            Debug.Log($"{Tag} event=bot_edge_block character={_character} n={_edgeBlocks} pos={Fmt(myPos)}");
        }
        _edgeBlocked = !safe;
        _input.SetMove(safe ? dir : Vector3.zero);
    }

    private static bool IsSafeStep(Vector3 foot, Vector3 dir)
    {
        Vector3 probe = foot + dir * QAPartyPlacement88.EdgeProbeAhead + Vector3.up;
        bool found = Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 1f + QAPartyPlacement88.EdgeMaxDrop + 0.5f, GroundMask, QueryTriggerInteraction.Ignore);
        return QAPartyPlacement88.IsSafeStep(found, foot.y, found ? hit.point.y : 0f);
    }

    /// <summary>보스 HurtBox 표면까지 수평 거리. HurtBox 가 꺼졌으면(체공) 켜져 있을 때 본 반경으로 중심 거리에서 뺀다.</summary>
    private float SurfaceDistance(Vector3 myPos, Vector3 bossPos)
    {
        if (_bossHurt != null && _bossHurt.enabled)
        {
            Bounds b = _bossHurt.bounds;
            _bossRadius = Mathf.Max(_bossRadius, Mathf.Max(b.extents.x, b.extents.z));
            return (float)InterruptOmniRangeModel.HorizontalDistanceToBounds(myPos, b);
        }
        return Mathf.Max(0f, QAPartyPlacement88.HorizontalDistance(myPos, bossPos) - _bossRadius);
    }

    private Vector3 SampleOffset()
    {
        QABoss103Bot.AimOffset(_aimRng, _profile.AimErrorRadius, out float x, out float z);
        return new Vector3(x, 0f, z);
    }

    // ── 로그 ───────────────────────────────────────────────────

    private void LogDiag(Player me)
    {
        Vector3 myPos = me.transform.position;
        Vector3 bossPos = _boss.transform.position;
        Debug.Log($"{Tag} event=bot_diag character={_character} tier={_profile.Tier} state={_state} move={_move} " +
                  $"surfM={F(SurfaceDistance(myPos, bossPos))} hittable={(_bossBody == null || _bossBody.enabled ? "true" : "false")} " +
                  $"attackStarts={_attackStarts} castsQ={_castsQ} castsR={_castsR} rejects={_castRejects} gunnerFires={_gunnerFires} " +
                  $"rollsQ={_gateQ.Rolls}/{_gateQ.Fails} rollsR={_gateR.Rolls}/{_gateR.Fails} stuck={_stuck.Escapes} edgeBlocks={_edgeBlocks} " +
                  $"bossHp={_boss.CurrentHealth} myHp={me.CurrentHealth} {EvasionDiag()}");
    }

    private void OnDestroy()
    {
        StopHold();
        ReleaseGuard();
        _sensor.Unbind();
        RestoreDashMode();
        _driver.Release();
        if (_input != null)
        {
            _input.SetMove(Vector3.zero);
            _input.SetTarget(null);
        }
        Debug.Log($"{Tag} event=bot_combat_done character={_character} tier={_profile.Tier} attackStarts={_attackStarts} castsQ={_castsQ} castsR={_castsR} " +
                  $"transforms={_transforms} rejects={_castRejects} gunnerFires={_gunnerFires} rollsQ={_gateQ.Rolls}/{_gateQ.Fails} rollsR={_gateR.Rolls}/{_gateR.Fails} " +
                  $"stuck={_stuck.Escapes} edgeBlocks={_edgeBlocks} {EvasionDiag()}");
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
    private static string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
    private static string Fmt(Vector3 v) => $"{F(v.x)}/{F(v.y)}/{F(v.z)}";
}
#endif
