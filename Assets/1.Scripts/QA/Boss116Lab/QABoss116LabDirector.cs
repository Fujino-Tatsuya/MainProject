#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 할 일 116-C4 — 대쉬 간파 예측 랩 씬(<c>DebugDashPredictLab</c>)의 진행자(호스트에서만 돈다, 씬 배치형). 게임 코드는 바꾸지 않고 기존 서버 API 만 부른다.
/// <list type="bullet">
/// <item><b>살려 두기</b>: 23호 체력이 <see cref="bossHealBelow"/> 아래면 가득(<see cref="Unit.SetCurrentHealthOnServer"/>) — 페이즈 임계(0.7) 위라 페이즈가 안 넘어간다.
///   내 플레이어도 <see cref="playerHealBelow"/> 아래면 가득. 간파 쿨은 남아 있으면 바로 초기화(창마다 시도 표본 — 대시 충전은 실제 규칙 그대로 둔다: 거부·충전 없음 원인을 보려고).</item>
/// <item><b>카운터 창 기록</b>: 23호 <see cref="TwentyThreeBoss.CounterWindowOpen"/> 열림/닫힘마다 <c>labwin</c> 줄(서버 시각·공격 행·보스 상태·구간·내 벽 거리).</item>
/// <item><b>대시 궤적</b>: 내 플레이어 대시 1회마다 <c>labdash</c> 줄 — 시작/끝 위치, 의도 거리 vs 실제 거리, 처음 막힌 지점·충돌체 이름/레이어(<see cref="QABoss116Lab.DashDistances"/>).</item>
/// <item><b>구간 교대</b>: <see cref="QABoss116LabConfig.ZoneSeconds"/> 마다 벽 구간(복도) ↔ 열린 구간으로 보스·플레이어를 옮긴다(보스 공격·창·내 대시/스킬 중이 아닐 때, 5초 넘게 못 옮기면 그대로 옮김).</item>
/// <item><b>판</b>: 설정 파일(<see cref="QABoss116LabConfig"/>, 배치 메뉴가 씀)이 있으면 전투 봇(<see cref="QABoss103CombatBot"/> 대쉬 간파 규칙)을 붙이고,
///   시간 상한·시도 상한 중 먼저 닿으면 끝 파일(<see cref="QABoss116Lab.DonePath"/>)을 쓴다 — 에디터 배치가 보고 Play 를 멈춘다.
///   설정 파일이 없으면(사람이 Dev Boot 로 직접) 봇 없이 살려 두기·기록(콘솔)·카메라만.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public sealed class QABoss116LabDirector : MonoBehaviour
{
    private const string Tag = QABoss116Lab.Tag;
    /// <summary>설정 파일이 이보다 오래됐으면(배치가 남긴 찌꺼기) 무시한다.</summary>
    private const double ConfigMaxAgeMinutes = 15.0;
    private const float ZoneMoveForceSeconds = 5f;
    // 정적 초기화에서 LayerMask 를 부르면 MonoBehaviour 생성(에디터 AddComponent)에서 예외 — 처음 쓸 때 구한다.
    private static int _obstacleMask = -1;
    private static int ObstacleMask => _obstacleMask >= 0 ? _obstacleMask
        : _obstacleMask = LayerMask.GetMask("Default", "Wall", "Env", "Barrier", "Enemy", "Player");

    [Header("살려 두기")]
    [Tooltip("23호 체력이 최대의 이 비율 아래면 가득 채운다(페이즈 임계 0.7 보다 위).")]
    [SerializeField, Range(0.75f, 1f)] private float bossHealBelow = 0.85f;
    [Tooltip("내 플레이어 체력이 최대의 이 비율 아래면 가득 채운다.")]
    [SerializeField, Range(0f, 1f)] private float playerHealBelow = 0.5f;
    [Tooltip("간파 쿨이 남아 있으면 바로 초기화(창마다 시도). 대시 충전은 건드리지 않는다.")]
    [SerializeField] private bool resetInterruptCooldown = true;

    [Header("구간(벽 = 복도 / 열린 쪽)")]
    [SerializeField] private Transform wallBossSpot;
    [SerializeField] private Transform wallPlayerSpot;
    [SerializeField] private Transform openBossSpot;
    [SerializeField] private Transform openPlayerSpot;
    [Tooltip("설정 파일 없이(사람이 직접) 돌 때도 구간을 교대할까.")]
    [SerializeField] private bool cycleZonesWithoutConfig;

    private QABoss116LabConfig _cfg;
    private bool _batch;
    private QABoss103CombatBot _bot;
    private QABoss103PartWriter _part;
    private TwentyThreeBoss _boss;
    private bool _started, _ended;
    private double _startedAt;
    private string _zone = "";
    private int _zoneIndex = -1;
    private float _zonePendingSince = -1f;
    private int _bossHeals, _playerHeals, _cdResets, _zoneMoves;

    // 카운터 창
    private bool _windowWas;
    private int _windows;
    private double _windowOpenT;

    // 대시 궤적
    private bool _tracking;
    private int _dashN;
    private Vector3 _dashStart, _dashPrev, _dashDir;
    private float _dashSpeed, _dashTotal, _dashElapsedPrev, _dashWall;
    private double _dashStartT;
    private bool _dashStalled, _dashAttached;
    private Vector3 _stallPos;
    private float _stallAt;
    private string _stallCollider = "", _stallLayer = "";

    private void Start()
    {
        string path = Path.Combine(QANetSim88.OwnRoot, QABoss116LabConfig.FileName);
        try
        {
            if (File.Exists(path) && (DateTime.Now - File.GetLastWriteTime(path)).TotalMinutes <= ConfigMaxAgeMinutes &&
                QABoss116LabConfig.TryParse(File.ReadAllText(path), out QABoss116LabConfig cfg))
            {
                _cfg = cfg;
                _batch = cfg.Bot.HasOut;
            }
            else if (File.Exists(path))
                Debug.LogWarning($"{Tag} 설정 파일이 {ConfigMaxAgeMinutes:0}분보다 오래됐거나 깨졌다 — 무시하고 사람 모드({path}).");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{Tag} 설정 읽기 실패({e.Message}) — 사람 모드.");
        }
        _cfg ??= new QABoss116LabConfig();
        Application.runInBackground = true;
        Debug.Log($"{Tag} event=lab_director mode={(_batch ? "batch" : "manual")} run={_cfg.Bot.Run} retry={_cfg.Bot.Retry} runS={_cfg.RunSeconds:0} " +
                  $"maxAttempts={_cfg.MaxAttempts} zoneS={_cfg.ZoneSeconds:0} dashint={_cfg.Bot.DashInterruptPct} out={QABoss103Plan.Token(_cfg.Bot.OutDir)}");
    }

    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsServer || _ended)
            return;
        Player me = Player.LocalPlayer;
        if (_boss == null || !_boss.IsSpawned)
            _boss = FindAnyObjectByType<TwentyThreeBoss>();
        if (me == null || _boss == null || !_boss.IsSpawned)
            return;

        if (!_started)
            Begin(me);

        KeepAlive(me);
        TickWindow(me);
        TickDash(me);
        TickZone(me);

        if (_batch)
        {
            string end = QABoss116Lab.EndReason(Time.realtimeSinceStartupAsDouble - _startedAt, _bot != null ? _bot.DashInterruptAttempts : 0,
                _cfg.RunSeconds, _cfg.MaxAttempts);
            if (end != null)
                End(end);
        }
    }

    private void Begin(Player me)
    {
        _started = true;
        _startedAt = Time.realtimeSinceStartupAsDouble;
        QABoss116LabProbe.PlannedZone = "";
        if (!_batch)
        {
            Debug.Log($"{Tag} event=lab_start mode=manual player={me.name} boss={_boss.name} — 봇 없음(사람 조작). 살려 두기·창/대시 기록(콘솔)만.");
            return;
        }

        QABoss103Config bot = _cfg.Bot;
        QABoss103Character character = CharacterOf(me);
        try
        {
            _part = new QABoss103PartWriter(QABoss116Lab.LabPartPath(bot.OutDir, bot.Run, bot.Retry));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{Tag} 랩 판 파일 열기 실패: {e.Message}");
        }
        QABoss116LabProbe.Active = true;
        _bot = QABoss103CombatBot.Create(bot.RunSeed, character, bot.BotTier);
        _bot.AttachPart(bot);
        Write(QABoss103PartFile.Format("labrun", "event", "start", "character", character.ToString(), "tier", bot.BotTier.ToString(),
            "runSeed", bot.RunSeed.ToString(CultureInfo.InvariantCulture), "t", F3(ServerNow()), "boss", _boss.name,
            "clients", NetworkManager.Singleton.ConnectedClientsIds.Count.ToString(CultureInfo.InvariantCulture),
            "data", _boss.MonsterData != null ? _boss.MonsterData.name : "na"));
        Debug.Log($"{Tag} event=lab_start mode=batch run={bot.Run} retry={bot.Retry} character={character} tier={bot.BotTier} runSeed={bot.RunSeed} " +
                  $"boss={_boss.name} dashint={bot.DashInterruptPct}");
    }

    private void End(string reason)
    {
        _ended = true;
        QABoss103Config bot = _cfg.Bot;
        int attempts = _bot != null ? _bot.DashInterruptAttempts : 0;
        double elapsed = Time.realtimeSinceStartupAsDouble - _startedAt;
        QABoss116LabProbe.Active = false;
        if (_tracking)
            FinishDash(Player.LocalPlayer);
        if (_windowWas)
            Write(QABoss103PartFile.Format(QABoss116Lab.WindowKind, "event", "close", "n", _windows.ToString(CultureInfo.InvariantCulture), "t", F3(ServerNow()), "note", "run_end"));
        if (_bot != null)
        {
            _bot.ClosePart("lab_end");
            Destroy(_bot.gameObject);
            _bot = null;
        }
        string summary = QABoss103PartFile.Format(QABoss116Lab.EndKind, "reason", reason, "attempts", attempts.ToString(CultureInfo.InvariantCulture),
            "elapsedS", elapsed.ToString("0.0", CultureInfo.InvariantCulture), "windows", _windows.ToString(CultureInfo.InvariantCulture),
            "dashes", _dashN.ToString(CultureInfo.InvariantCulture), "bossHeals", _bossHeals.ToString(CultureInfo.InvariantCulture),
            "playerHeals", _playerHeals.ToString(CultureInfo.InvariantCulture), "cdResets", _cdResets.ToString(CultureInfo.InvariantCulture),
            "zoneMoves", _zoneMoves.ToString(CultureInfo.InvariantCulture));
        Write(summary);
        _part?.Dispose();
        _part = null;
        try
        {
            string done = QABoss116Lab.DonePath(bot.OutDir, bot.Run, bot.Retry);
            Directory.CreateDirectory(Path.GetDirectoryName(done));
            File.WriteAllText(done, summary + "\n");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{Tag} 끝 파일 쓰기 실패: {e.Message}");
        }
        Debug.Log($"{Tag} event=lab_end {summary.Substring(QABoss116Lab.EndKind.Length + 1)}");
    }

    private void OnDestroy()
    {
        // Play 정지·씬 내림 — 끝 파일 없이 끝났으면 봇·판 파일만 닫는다(배치가 그 판을 오류로 적는다).
        QABoss116LabProbe.Active = false;
        if (_bot != null)
        {
            _bot.ClosePart("lab_destroy");
            Destroy(_bot.gameObject);
        }
        _part?.Dispose();
    }

    // ── 살려 두기 ─────────────────────────────────────────────

    private void KeepAlive(Player me)
    {
        int bossMax = _boss.FinalMaxHp;
        if (bossMax > 0 && _boss.CurrentHealth > 0 && _boss.CurrentHealth < bossMax * bossHealBelow)
        {
            _boss.SetCurrentHealthOnServer(bossMax);
            _bossHeals++;
        }
        int myMax = me.FinalMaxHp;
        if (myMax > 0 && me.CurrentHealth > 0 && me.CurrentHealth < myMax * playerHealBelow)
        {
            me.SetCurrentHealthOnServer(myMax);
            _playerHeals++;
        }
        if (resetInterruptCooldown && me.TryGetComponent(out PlayerSkillController skills) && skills.GetCooldownRemaining(PlayerSkillSlot.Interrupt) > 0f &&
            !(skills.ActiveSkill is PlayerInterruptSkillBase))
        {
            skills.ResetCooldownServer(PlayerSkillSlot.Interrupt);
            _cdResets++;
        }
    }

    // ── 카운터 창 ─────────────────────────────────────────────

    private void TickWindow(Player me)
    {
        bool open = _boss.CounterWindowOpen;
        if (open == _windowWas)
            return;
        _windowWas = open;
        double t = ServerNow();
        if (open)
        {
            _windows++;
            _windowOpenT = t;
        }
        Vector3 p = me.transform.position, b = _boss.transform.position;
        float wall = QABoss116LabProbe.WallDistance(p, out string wallName);
        string line = QABoss103PartFile.Format(QABoss116Lab.WindowKind,
            "event", open ? "open" : "close",
            "n", _windows.ToString(CultureInfo.InvariantCulture),
            "t", F3(t),
            "attack", QABoss116LabProbe.AttackOf(_boss),
            "bossState", _boss.State.ToString(),
            "vulnerable", QABoss116LabProbe.B(_boss.IsVulnerable),
            "zonePlanned", _zone,
            "wallM", QABoss116LabProbe.F2(wall),
            "wallName", wallName,
            "bossM", QABoss116LabProbe.F2(new Vector2(b.x - p.x, b.z - p.z).magnitude),
            "durS", open ? "na" : F3(t - _windowOpenT));
        Write(line);
        Debug.Log($"{Tag} event=counter_window {line.Substring(QABoss116Lab.WindowKind.Length + 1)}");
    }

    // ── 대시 궤적 ─────────────────────────────────────────────

    private void TickDash(Player me)
    {
        PlayerStateController sc = me.GetComponent<PlayerStateController>();
        PlayerDashState ds = sc != null ? sc.ActiveDash : null;
        Vector3 pos = me.transform.position;
        if (ds != null && !_tracking)
        {
            _tracking = true;
            _dashN++;
            _dashStart = pos;
            _dashPrev = pos;
            _dashDir = ds.Direction;
            _dashSpeed = ds.Speed;
            _dashTotal = ds.Elapsed + ds.Remaining;
            _dashElapsedPrev = ds.Elapsed;
            _dashStartT = ServerNow() - ds.Elapsed;
            _dashWall = QABoss116LabProbe.WallDistance(pos, out _);
            _dashStalled = false;
            _dashAttached = false;
            _stallCollider = _stallLayer = "";
            return;
        }
        if (!_tracking)
            return;
        if (ds == null)
        {
            FinishDash(me);
            return;
        }
        if (ds.AttachedSkill != null)
            _dashAttached = true;
        float dt = ds.Elapsed - _dashElapsedPrev;
        Vector3 step = pos - _dashPrev;
        step.y = 0f;
        float expected = _dashSpeed * dt;
        // 처음 막힌 지점 — 이번 프레임 대시 방향 진척이 기대의 40% 미만(대시 시작 0.03초 뒤부터).
        if (!_dashStalled && ds.Elapsed > 0.03f && expected > 0.02f && Vector3.Dot(step, _dashDir) < expected * 0.4f)
        {
            _dashStalled = true;
            _stallPos = pos;
            _stallAt = ds.Elapsed;
            ProbeBlocker(me, _dashPrev, out _stallCollider, out _stallLayer);
        }
        _dashPrev = pos;
        _dashElapsedPrev = ds.Elapsed;
    }

    private void FinishDash(Player me)
    {
        _tracking = false;
        Vector3 end = me != null ? me.transform.position : _dashPrev;
        QABoss116Lab.DashDistances(_dashSpeed, _dashTotal, _dashStart.x, _dashStart.z, end.x, end.z, _dashDir.x, _dashDir.z,
            out float intent, out float actual, out float shortfall, out bool blocked);
        if (blocked && !_dashStalled && me != null)
        {
            // 프레임 진척으로는 못 잡았는데 덜 갔다 — 끝 지점에서 대시 방향으로 다시 찾는다.
            _stallPos = end;
            _stallAt = _dashTotal;
            ProbeBlocker(me, end, out _stallCollider, out _stallLayer);
        }
        bool hasStall = _dashStalled || blocked;
        string line = QABoss103PartFile.Format(QABoss116Lab.DashKind,
            "n", _dashN.ToString(CultureInfo.InvariantCulture),
            "startT", F3(_dashStartT),
            "endT", F3(ServerNow()),
            "startPos", Xz(_dashStart),
            "endPos", Xz(end),
            "dir", Xz(_dashDir),
            "speed", QABoss116LabProbe.F2(_dashSpeed),
            "intentM", QABoss116LabProbe.F2(intent),
            "actualM", QABoss116LabProbe.F2(actual),
            "shortM", QABoss116LabProbe.F2(shortfall),
            "blocked", QABoss116LabProbe.B(blocked),
            "blockPos", hasStall ? Xz(_stallPos) : "na",
            "blockCollider", hasStall ? _stallCollider : "na",
            "blockLayer", hasStall ? _stallLayer : "na",
            "blockAtS", hasStall ? QABoss116LabProbe.F3(_stallAt) : "na",
            "wallM", QABoss116LabProbe.F2(_dashWall),
            "zonePlanned", _zone,
            "endState", me != null ? me.CurrentState.ToString() : "na",
            "attached", QABoss116LabProbe.B(_dashAttached));
        Write(line);
        if (blocked)
            Debug.Log($"{Tag} event=dash_blocked {line.Substring(QABoss116Lab.DashKind.Length + 1)}");
    }

    /// <summary>내 캡슐을 <paramref name="from"/> 에서 대시 방향으로 0.6m 쓸어 처음 닿는 벽·몸(바닥 면 제외).</summary>
    private void ProbeBlocker(Player me, Vector3 from, out string collider, out string layer)
    {
        collider = "none";
        layer = "";
        CapsuleCollider capsule = me.GetComponent<CapsuleCollider>();
        Transform t = me.transform;
        float radius = 0.3f, half = 0.5f;
        Vector3 center = from + Vector3.up;
        if (capsule != null)
        {
            Vector3 lossy = capsule.transform.lossyScale;
            radius = capsule.radius * Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.z));
            half = Mathf.Max(0f, capsule.height * Mathf.Abs(lossy.y) * 0.5f - radius);
            center = from + (capsule.transform.TransformPoint(capsule.center) - t.position);
        }
        radius = Mathf.Max(0.05f, radius - 0.05f);
        Vector3 up = Vector3.up * (half + 0.05f);
        RaycastHit[] hits = Physics.CapsuleCastAll(center + up, center - up + Vector3.up * 0.1f, radius, _dashDir, 0.6f, ObstacleMask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        foreach (RaycastHit h in hits)
        {
            if (h.collider == null || h.collider.transform.IsChildOf(t) || h.normal.y > 0.6f)
                continue;
            if (h.distance < best)
            {
                best = h.distance;
                collider = h.collider.name;
                layer = LayerMask.LayerToName(h.collider.gameObject.layer);
            }
        }
    }

    // ── 구간 ──────────────────────────────────────────────────

    private void TickZone(Player me)
    {
        if (!_batch && !cycleZonesWithoutConfig)
            return;
        if (wallBossSpot == null || wallPlayerSpot == null || openBossSpot == null || openPlayerSpot == null)
            return;
        double elapsed = Time.realtimeSinceStartupAsDouble - _startedAt;
        int index = _cfg.ZoneSeconds > 0.0 ? (int)Math.Floor(elapsed / _cfg.ZoneSeconds) : 0;
        if (index == _zoneIndex)
            return;
        string want = QABoss116Lab.PlannedZone(elapsed, _cfg.ZoneSeconds);
        if (_zonePendingSince < 0f)
            _zonePendingSince = Time.realtimeSinceStartup;
        bool busy = _boss.State == MonsterState.Attack || _boss.CounterWindowOpen || me.CurrentState == PlayerActionState.Dash ||
                    me.CurrentState == PlayerActionState.Skill;
        if (busy && _zoneIndex >= 0 && Time.realtimeSinceStartup - _zonePendingSince < ZoneMoveForceSeconds)
            return;
        _zoneIndex = index;
        _zonePendingSince = -1f;
        _zone = want;
        QABoss116LabProbe.PlannedZone = want;
        Transform bossSpot = want == QABoss116Lab.ZoneWall ? wallBossSpot : openBossSpot;
        Transform playerSpot = want == QABoss116Lab.ZoneWall ? wallPlayerSpot : openPlayerSpot;

        bool bossOk = false;
        NavMeshAgent agent = _boss.GetComponent<NavMeshAgent>();
        if (agent != null && agent.enabled && agent.isOnNavMesh)
            bossOk = agent.Warp(bossSpot.position);
        else
        {
            _boss.transform.position = bossSpot.position;
            bossOk = true;
        }
        _boss.transform.rotation = Quaternion.LookRotation(Flat(playerSpot.position - bossSpot.position));
        _boss.SetSpawnAnchor(bossSpot.position);
        bool playerOk = me.TryGetComponent(out PlayerFallRecovery mover) && mover.RelocateServer(playerSpot.position);
        _zoneMoves++;
        string line = QABoss103PartFile.Format(QABoss116Lab.ZoneKind, "zone", want, "index", index.ToString(CultureInfo.InvariantCulture),
            "t", F3(ServerNow()), "bossOk", QABoss116LabProbe.B(bossOk), "playerOk", QABoss116LabProbe.B(playerOk), "forced", QABoss116LabProbe.B(busy));
        Write(line);
        Debug.Log($"{Tag} event=zone {line.Substring(QABoss116Lab.ZoneKind.Length + 1)}");
    }

    // ── 도구 ──────────────────────────────────────────────────

    /// <summary>내 간파 스킬 클래스 이름으로 캐릭터(봇 자리)를 정한다 — Dev Boot 툴바 캐릭터 그대로.</summary>
    private static QABoss103Character CharacterOf(Player me)
    {
        PlayerSkillController skills = me.GetComponent<PlayerSkillController>();
        PlayerSkillBase interrupt = skills != null ? skills.GetSkill(PlayerSkillSlot.Interrupt) : null;
        string name = QABoss116DashPredict.CharacterFromSkill(interrupt != null ? interrupt.GetType().Name : me.name);
        return Enum.TryParse(name, out QABoss103Character c) ? c : QABoss103Character.Paladin;
    }

    private void Write(string line)
    {
        if (_part == null)
            return;
        try { _part.Write(line); }
        catch (Exception e)
        {
            Debug.LogWarning($"{Tag} 랩 판 파일 쓰기 실패: {e.Message}");
            _part.Dispose();
            _part = null;
        }
    }

    private static double ServerNow()
    {
        NetworkManager nm = NetworkManager.Singleton;
        return nm != null && nm.IsListening ? nm.ServerTime.Time : -1.0;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward;
    }

    private static string Xz(Vector3 v) =>
        v.x.ToString("0.00", CultureInfo.InvariantCulture) + "/" + v.z.ToString("0.00", CultureInfo.InvariantCulture);

    private static string F3(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);
}
#endif
