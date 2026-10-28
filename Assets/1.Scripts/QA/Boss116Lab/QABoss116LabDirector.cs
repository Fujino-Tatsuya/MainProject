#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 할 일 116-C4 — 대쉬 간파 예측 랩 씬(<c>DebugDashPredictLab</c>)의 진행자(호스트에서만 돈다, 씬 배치형). 게임 코드는 바꾸지 않고 기존 서버 API 만 부른다.
/// <list type="bullet">
/// <item><b>살려 두기</b>: 23호 체력이 <see cref="bossHealBelow"/> 아래면 가득(<see cref="Unit.SetCurrentHealthOnServer"/>) — 페이즈 임계(0.7) 위라 페이즈가 안 넘어간다.
///   내 플레이어도 <see cref="playerHealBelow"/> 아래면 가득. 간파 쿨은 남아 있으면 바로 초기화(창마다 시도 표본 — 대시 충전은 실제 규칙 그대로 둔다: 거부·충전 없음 원인을 보려고).</item>
/// <item><b>카운터 창 기록</b>: 23호 <see cref="TwentyThreeBoss.CounterWindowOpen"/> 열림/닫힘마다 <c>labwin</c> 줄(서버 시각·공격 행·보스 상태·보스 거리).</item>
/// <item><b>대시 궤적·끊긴 이유</b>(116-C6): 내 플레이어 대시 1회마다 <c>labdash</c> 줄 — 시작/끝 위치, 의도 거리 vs 실제 거리, 처음 막힌 지점·충돌체,
///   진입 이전 상태·우클릭→대시 전환 여부, 게임 <c>[Dash]</c> 로그로 본 끝 사유(<see cref="QABoss116DashStop"/>), 프레임별 이동 요약(첫 이동 프레임·이동 프레임 수).
///   대시 진입·종료 줄을 <c>Application.logMessageReceived</c> 로 받으므로 프레임 사이에 끝난 대시(진입 프레임 종료)도 남는다.</item>
/// <item><b>판</b>: 설정 파일(<see cref="QABoss116LabConfig"/>, 배치 메뉴가 씀)이 있으면 전투 봇(<see cref="QABoss103CombatBot"/> 대쉬 간파 규칙)을 붙이고,
///   시간 상한·시도 상한 중 먼저 닿으면 끝 파일(<see cref="QABoss116Lab.DonePath"/>)을 쓴다 — 에디터 배치가 보고 Play 를 멈춘다.
///   설정 파일이 오래됐거나 깨졌으면 봇·기록 없이 살려 두기·카메라만(<see cref="QABoss116LabMode.Plain"/>).</item>
/// <item><b>수동 모드</b>(할 일 116-C5, 설정 파일 없음 — 사람이 Dev Boot 로 직접): 예측 로그를 켜고, 사람 입력 시도(<see cref="QABoss116LabManualInput"/>)·창·대시·예측 줄을
///   메모리에 모았다가 Play 정지(<see cref="OnDestroy"/>) 때 <c>C:/UnityProject/QA/116manual-…/parts/</c> 에 쓴다 — 에디터 훅(<c>QABoss116LabManualFinalizer</c>)이 C4 집계로 CSV·report.html.
///   랩 카메라는 끈다(평소 플레이어 카메라).</item>
/// </list>
/// 116-C6: 벽 구간·구간 이동(F2·30초 교대)은 없앴다 — 씬의 구간 지점 오브젝트는 남아 있지만 코드는 참조하지 않는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class QABoss116LabDirector : MonoBehaviour
{
    private const string Tag = QABoss116Lab.Tag;
    /// <summary>설정 파일이 이보다 오래됐으면(배치가 남긴 찌꺼기) 무시한다.</summary>
    private const double ConfigMaxAgeMinutes = 15.0;
    private const string UpperLayerName = "DashInterruptUpper";
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

    private QABoss116LabConfig _cfg;
    private bool _batch;
    private QABoss116LabMode _mode = QABoss116LabMode.Plain;
    private bool Manual => _mode == QABoss116LabMode.Manual;
    // 수동 모드 판 버퍼(Play 정지 때 한 번에 쓴다 — 시도 0 이면 폴더를 만들지 않는다)
    private List<string> _manualLines;
    private QABoss116LabManualInput _manualInput;
    private bool _predictHooked;
    private string _character = "";
    private DateTime _manualStartedAt;
    private double _lastServerT = -1.0;
    private QABoss103CombatBot _bot;
    private QABoss103PartWriter _part;
    private TwentyThreeBoss _boss;
    private bool _started, _ended;
    private double _startedAt;
    private int _bossHeals, _playerHeals, _cdResets;

    // 카운터 창
    private bool _windowWas;
    private int _windows;
    private double _windowOpenT;

    // 대시 궤적 + 끊긴 이유(116-C6) — 기록은 게임 '[Dash] 상태 진입' 줄에서 열고 '[Dash] 종료:' 줄 다음 프레임에 닫는다.
    private bool _logHooked;
    private QABoss116DashRecord _rec;
    private PlayerDashState _recState, _lastClosedState;
    private bool _recClosing;
    private int _transferFrame = -1, _recOpenFrame;
    private int _dashN;
    private Vector3 _dashStart, _dashPrev, _dashDir, _dashEnd;
    private float _dashSpeed, _dashTotal, _dashElapsedPrev;
    private double _dashStartT, _dashEndT;
    private bool _dashStalled, _dashAttached;
    private Vector3 _stallPos;
    private float _stallAt;
    private string _stallCollider = "", _stallLayer = "";
    // 대시 직전 프레임 값(진입 순간은 이미 Dash 상태라 이동 잠금이 의미 없다)
    private bool _prevCanMove;
    private string _prevFrameState = "";
    private string _dashPrevCanMove = "na", _dashPrevFrameState = "", _dashRootMotion = "na", _dashBossM = "na", _dashBossAlong = "na", _dashBossLateral = "na";
    private Animator _anim;
    private Player _animOf;
    private int _upperLayer = -1;
    private readonly List<string> _pendingConsole = new List<string>();

    private void Start()
    {
        string path = Path.Combine(QANetSim88.OwnRoot, QABoss116LabConfig.FileName);
        bool exists = false;
        try
        {
            exists = File.Exists(path);
            if (exists && (DateTime.Now - File.GetLastWriteTime(path)).TotalMinutes <= ConfigMaxAgeMinutes &&
                QABoss116LabConfig.TryParse(File.ReadAllText(path), out QABoss116LabConfig cfg))
            {
                _cfg = cfg;
                _batch = cfg.Bot.HasOut;
            }
            else if (exists)
                Debug.LogWarning($"{Tag} 설정 파일이 {ConfigMaxAgeMinutes:0}분보다 오래됐거나 깨졌다 — 무시하고 사람 모드(기록 없음 — 수동 기록을 쓰려면 이 파일을 지울 것: {path}).");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{Tag} 설정 읽기 실패({e.Message}) — 사람 모드.");
        }
        _cfg ??= new QABoss116LabConfig();
        _mode = QABoss116LabManual.ResolveMode(exists, _batch);
        Application.runInBackground = true;
        if (Manual)
            BeginManual();
        Debug.Log($"{Tag} event=lab_director mode={_mode.ToString().ToLowerInvariant()} run={_cfg.Bot.Run} retry={_cfg.Bot.Retry} runS={_cfg.RunSeconds:0} " +
                  $"maxAttempts={_cfg.MaxAttempts} dashint={_cfg.Bot.DashInterruptPct} out={QABoss103Plan.Token(_cfg.Bot.OutDir)}");
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
        _lastServerT = ServerNow();

        KeepAlive(me);
        TickWindow(me);
        TickDash(me);
        FlushConsole();

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
        HookGameLog(true);
        if (Manual)
        {
            _character = CharacterOf(me).ToString();
            _manualStartedAt = DateTime.Now;
            Write(QABoss103PartFile.Format("labrun", "event", "start", "mode", "manual", "character", _character, "t", F3(ServerNow()), "boss", _boss.name,
                "clients", NetworkManager.Singleton.ConnectedClientsIds.Count.ToString(CultureInfo.InvariantCulture),
                "data", _boss.MonsterData != null ? _boss.MonsterData.name : "na"));
            Debug.Log($"{Tag} event=lab_start mode=manual player={me.name} character={_character} boss={_boss.name} — 사람 조작 기록 중(우클릭→대시·대시→우클릭). " +
                      $"Play 를 멈추면 {QABoss116LabManual.QARoot}/{QABoss116LabManual.Prefix}-<시각>/ 에 CSV·report.html.");
            return;
        }
        if (!_batch)
        {
            Debug.Log($"{Tag} event=lab_start mode=plain player={me.name} boss={_boss.name} — 봇 없음(사람 조작). 살려 두기·창/대시 기록(콘솔)만.");
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
        if (_rec != null)
            FinishDash(Player.LocalPlayer);
        HookGameLog(false);
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
            "playerHeals", _playerHeals.ToString(CultureInfo.InvariantCulture), "cdResets", _cdResets.ToString(CultureInfo.InvariantCulture));
        Write(summary);
        FlushConsole();
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
        if (Manual)
        {
            EndManual();
            return;
        }
        // Play 정지·씬 내림 — 끝 파일 없이 끝났으면 봇·판 파일만 닫는다(배치가 그 판을 오류로 적는다).
        HookGameLog(false);
        QABoss116LabProbe.Active = false;
        if (_bot != null)
        {
            _bot.ClosePart("lab_destroy");
            Destroy(_bot.gameObject);
        }
        _part?.Dispose();
    }

    // ── 수동 모드(할 일 116-C5) ──────────────────────────────

    /// <summary>수동 모드 사람 입력 관측기가 읽는다.</summary>
    public TwentyThreeBoss Boss => _boss != null && _boss.IsSpawned ? _boss : null;
    public bool Started => _started && !_ended;
    public string Character => _character;
    public int CounterWindows => _windows;

    private void BeginManual()
    {
        _manualLines = new List<string>(1024);
        DashInterruptPredictLog.Enabled = true;
        DashInterruptPredictLog.LineEmitted += OnPredictLine;
        _predictHooked = true;
        foreach (QABoss116LabCamera cam in FindObjectsByType<QABoss116LabCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            cam.enabled = false;   // 평소 플레이어 카메라 그대로
        _manualInput = gameObject.AddComponent<QABoss116LabManualInput>();
        _manualInput.Init(this);
    }

    private void OnPredictLine(string line)
    {
        string part = QABoss116DashPredict.ToPartLine(line);
        if (part != null)
            WriteManual(part);
    }

    /// <summary>수동 모드 판 버퍼에 한 줄(사람 시도 <c>dashint</c>·창·대시·예측 줄).</summary>
    public void WriteManual(string line)
    {
        if (_manualLines != null && !string.IsNullOrEmpty(line))
            _manualLines.Add(line);
    }

    /// <summary>
    /// Play 정지 — 진행 중 시도·대시·창을 닫고, 시도가 1건 이상이면 결과 폴더에 판 파일·batch.txt 를 쓴 뒤 에디터 훅에 넘긴다
    /// (CSV·report.html = <c>QABoss116LabManualFinalizer</c>, 같은 정지 직후). 시도 0 이면 폴더를 만들지 않는다.
    /// </summary>
    private void EndManual()
    {
        if (_predictHooked)
        {
            DashInterruptPredictLog.LineEmitted -= OnPredictLine;
            DashInterruptPredictLog.Enabled = false;
            _predictHooked = false;
        }
        if (_manualLines == null || _ended)
        {
            HookGameLog(false);
            return;
        }
        _ended = true;
        if (_manualInput != null)
            _manualInput.Flush();
        if (!_started)
        {
            Debug.Log($"{Tag} event=manual_end 랩이 시작되지 않았다(플레이어·23호 스폰 전 정지) — 결과 폴더 없음.");
            return;
        }
        Player me = Player.LocalPlayer;
        if (_rec != null)
            FinishDash(me);
        HookGameLog(false);
        if (_windowWas)
            Write(QABoss103PartFile.Format(QABoss116Lab.WindowKind, "event", "close", "n", _windows.ToString(CultureInfo.InvariantCulture), "t", F3(_lastServerT), "note", "run_end"));
        int attempts = _manualInput != null ? _manualInput.Attempts : 0;
        double elapsed = Time.realtimeSinceStartupAsDouble - _startedAt;
        Write(QABoss103PartFile.Format(QABoss116Lab.EndKind, "reason", "manual_stop", "attempts", attempts.ToString(CultureInfo.InvariantCulture),
            "elapsedS", elapsed.ToString("0.0", CultureInfo.InvariantCulture), "windows", _windows.ToString(CultureInfo.InvariantCulture),
            "dashes", _dashN.ToString(CultureInfo.InvariantCulture), "bossHeals", _bossHeals.ToString(CultureInfo.InvariantCulture),
            "playerHeals", _playerHeals.ToString(CultureInfo.InvariantCulture), "cdResets", _cdResets.ToString(CultureInfo.InvariantCulture)));
        _pendingConsole.Clear();   // 정지 중 — 대시 콘솔 줄은 버린다(판 파일에는 이미 있다)
        if (attempts == 0)
        {
            Debug.Log($"{Tag} 수동 모드 결과 — 대쉬 간파 시도 0회(카운터 창 {_windows}·대시 {_dashN}회, {elapsed:0}초) — 빈 요약, 결과 폴더를 만들지 않았다.");
            return;
        }
        try
        {
            DateTime at = _manualStartedAt == default ? DateTime.Now : _manualStartedAt;
            string dir = $"{QABoss116LabManual.QARoot}/{QABoss116LabManual.FolderName(at)}";
            if (Directory.Exists(dir))
                dir += DateTime.Now.ToString("ss", CultureInfo.InvariantCulture);
            string part = QABoss116Lab.LabPartPath(dir, 1, 0);
            Directory.CreateDirectory(Path.GetDirectoryName(part));
            File.WriteAllLines(part, _manualLines, new System.Text.UTF8Encoding(false));
            var batch = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("title", "할 일 116-C5 대쉬 간파 예측 랩 — 수동 모드(사람 입력)"),
                new KeyValuePair<string, string>("mode", "manual"),
                new KeyValuePair<string, string>("started_at", at.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("ended_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("scene", QABoss116Lab.ScenePath),
                new KeyValuePair<string, string>("character", _character),
                new KeyValuePair<string, string>("total", "1"),
                new KeyValuePair<string, string>("attempts", attempts.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("unity_version", Application.unityVersion),
            };
            File.WriteAllText(Path.Combine(dir, QABoss103Batch.BatchFile), QABoss103Batch.FormatKeyValues(batch));
            UnityEditor.SessionState.SetString(QABoss116LabManual.PendingKey, dir);
            Debug.Log($"{Tag} event=manual_end attempts={attempts} windows={_windows} dashes={_dashN} dir={QABoss103Plan.Token(dir)} — 에디터로 돌아가면 CSV·report.html 을 만든다.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{Tag} 수동 모드 결과 쓰기 실패: {e.GetType().Name}: {e.Message}");
        }
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
        string line = QABoss103PartFile.Format(QABoss116Lab.WindowKind,
            "event", open ? "open" : "close",
            "n", _windows.ToString(CultureInfo.InvariantCulture),
            "t", F3(t),
            "attack", QABoss116LabProbe.AttackOf(_boss),
            "bossState", _boss.State.ToString(),
            "vulnerable", QABoss116LabProbe.B(_boss.IsVulnerable),
            "bossM", QABoss116LabProbe.F2(new Vector2(b.x - p.x, b.z - p.z).magnitude),
            "durS", open ? "na" : F3(t - _windowOpenT));
        Write(line);
        Debug.Log($"{Tag} event=counter_window {line.Substring(QABoss116Lab.WindowKind.Length + 1)}");
    }

    // ── 대시 궤적 + 끊긴 이유(116-C6) ────────────────────────

    private void HookGameLog(bool on)
    {
        if (on == _logHooked)
            return;
        _logHooked = on;
        if (on)
            Application.logMessageReceived += OnGameLog;
        else
            Application.logMessageReceived -= OnGameLog;
    }

    /// <summary>
    /// 게임 대시 로그(<c>PlayerDashState</c>·<c>PlayerDashController</c>·<c>PlayerSkillController</c> 의 <c>Edit.Log</c>) — 호스트 1명 랩이라 [Dash] 줄은 내 플레이어 것.
    /// 진입 줄 = 새 기록, 종료 줄 = 끝 값(다음 프레임에 줄을 쓴다 — 바로 뒤 '이동 없음/손실' 줄까지 받으려고), 그 사이 줄 = 도중 신호.
    /// </summary>
    private void OnGameLog(string condition, string stackTrace, LogType type)
    {
        if (!_started || _ended || condition == null || !condition.StartsWith("[Dash", StringComparison.Ordinal))
            return;
        Player me = Player.LocalPlayer;
        if (condition.StartsWith(QABoss116DashStop.TransferMark, StringComparison.Ordinal))
        {
            _transferFrame = Time.frameCount;   // 같은 호출에서 곧 진입 줄이 온다(PlayerDashState.Enter → CompleteDashTransfer 가 먼저)
            return;
        }
        if (QABoss116DashStop.TryParseEnter(condition, out string prev, out float speed, out float duration, out float dirX, out float dirZ))
        {
            if (_rec != null)
                FinishDash(me);   // 대시가 대시를 덮었다 — 앞 기록은 이미 종료 줄을 받았다
            OpenDash(me, prev, speed, duration, dirX, dirZ, ServerNow());
            return;
        }
        if (_rec == null)
            return;
        if (QABoss116DashStop.TryParseExit(condition, _rec))
        {
            _recClosing = true;
            _dashEnd = me != null ? me.transform.position : _dashPrev;
            _dashEndT = ServerNow();
            return;
        }
        _rec.Feed(condition);
    }

    private void OpenDash(Player me, string prevState, float speed, float duration, float dirX, float dirZ, double startT)
    {
        _rec = new QABoss116DashRecord
        {
            PrevState = prevState,
            Transfer = _transferFrame == Time.frameCount,
            Speed = speed,
            Duration = duration,
            DirX = dirX,
            DirZ = dirZ,
        };
        _transferFrame = -1;
        _recState = null;
        _recClosing = false;
        _recOpenFrame = Time.frameCount;
        _dashN++;
        Vector3 pos = me != null ? me.transform.position : Vector3.zero;
        _dashStart = _dashPrev = _dashEnd = pos;
        Vector3 dir = new Vector3(float.IsNaN(dirX) ? 0f : dirX, 0f, float.IsNaN(dirZ) ? 0f : dirZ);
        _dashDir = dir.sqrMagnitude > 1e-4f ? dir.normalized : me != null ? Flat(me.transform.forward) : Vector3.forward;
        _dashSpeed = float.IsNaN(speed) ? 0f : speed;
        _dashTotal = float.IsNaN(duration) ? 0f : duration;
        _dashElapsedPrev = 0f;
        _dashStartT = startT;
        _dashEndT = -1.0;
        _dashStalled = false;
        _dashAttached = false;
        _stallCollider = _stallLayer = "";
        _dashPrevCanMove = QABoss116LabProbe.B(_prevCanMove);
        _dashPrevFrameState = _prevFrameState;
        Animator anim = AnimOf(me);
        _dashRootMotion = anim != null ? QABoss116LabProbe.B(anim.applyRootMotion) : "na";
        _dashBossM = _dashBossAlong = _dashBossLateral = "na";
        if (_boss != null)
        {
            Vector3 tb = _boss.transform.position - pos;
            tb.y = 0f;
            float along = Vector3.Dot(tb, _dashDir);
            _dashBossM = QABoss116LabProbe.F2(tb.magnitude);
            _dashBossAlong = QABoss116LabProbe.F2(along);
            _dashBossLateral = QABoss116LabProbe.F2((tb - _dashDir * along).magnitude);
        }
    }

    private void TickDash(Player me)
    {
        PlayerStateController sc = me.GetComponent<PlayerStateController>();
        PlayerDashState ds = sc != null ? sc.ActiveDash : null;
        Vector3 pos = me.transform.position;
        if (_rec != null && _recClosing)
        {
            FinishDash(me);
            ds = ds == _lastClosedState ? null : ds;
        }
        if (_rec == null)
        {
            if (ds == null || ds == _lastClosedState)
            {
                // 대시 밖 — 다음 대시의 '직전 프레임' 값
                _prevCanMove = me.CanMove;
                PlayerSkillController skills = me.GetComponent<PlayerSkillController>();
                _prevFrameState = me.CurrentState + (skills != null && skills.ActiveSkill != null ? ":" + skills.ActiveSkill.GetType().Name : "");
                return;
            }
            // 진입 줄을 못 받았다(로그 꺼짐 등) — 상태 객체로 연다. 끝 사유는 no_exit_log 로 남는다.
            OpenDash(me, "no_enter_log", ds.Speed, ds.Elapsed + ds.Remaining, ds.Direction.x, ds.Direction.z, ServerNow() - ds.Elapsed);
        }
        if (ds == null)
        {
            if (_recState != null || Time.frameCount - _recOpenFrame > 2)
                FinishDash(me);   // 상태는 끝났는데 종료 줄이 없다
            return;
        }
        if (_recState == null)
        {
            _recState = ds;
            _dashSpeed = ds.Speed;
            _dashTotal = ds.Elapsed + ds.Remaining;
            _dashDir = ds.Direction;
        }
        if (ds != _recState)
            return;
        if (ds.AttachedSkill != null)
            _dashAttached = true;
        float dt = ds.Elapsed - _dashElapsedPrev;
        Vector3 step = pos - _dashPrev;
        step.y = 0f;
        float progress = Vector3.Dot(step, _dashDir);
        float expected = _dashSpeed * dt;
        // 처음 막힌 지점 — 이번 프레임 대시 방향 진척이 기대의 40% 미만(대시 시작 0.03초 뒤부터).
        if (!_dashStalled && ds.Elapsed > 0.03f && expected > 0.02f && progress < expected * 0.4f)
        {
            _dashStalled = true;
            _stallPos = pos;
            _stallAt = ds.Elapsed;
            ProbeBlocker(me, _dashPrev, out _stallCollider, out _stallLayer);
        }
        Animator anim = AnimOf(me);
        _rec.Frame(progress, ds.Elapsed, Time.timeScale, anim != null && _upperLayer >= 0 ? anim.GetLayerWeight(_upperLayer) : -1f);
        _dashPrev = pos;
        _dashElapsedPrev = ds.Elapsed;
    }

    private void FinishDash(Player me)
    {
        QABoss116DashRecord rec = _rec;
        _rec = null;
        _lastClosedState = _recState;
        _recState = null;
        _recClosing = false;
        if (rec == null)
            return;
        Vector3 end = rec.HasExit ? _dashEnd : me != null ? me.transform.position : _dashPrev;
        QABoss116Lab.DashDistances(_dashSpeed, _dashTotal, _dashStart.x, _dashStart.z, end.x, end.z, _dashDir.x, _dashDir.z,
            out float intent, out float actual, out float shortfall, out bool blocked);
        string reason = QABoss116DashStop.Classify(rec, shortfall);
        if (blocked && !_dashStalled && me != null)
        {
            // 프레임 진척으로는 못 잡았는데 덜 갔다 — 끝 지점에서 대시 방향으로 다시 찾는다.
            _stallPos = end;
            _stallAt = _dashTotal;
            ProbeBlocker(me, end, out _stallCollider, out _stallLayer);
        }
        bool hasStall = _dashStalled || blocked;
        var kv = new List<string>
        {
            "n", _dashN.ToString(CultureInfo.InvariantCulture),
            "startT", F3(_dashStartT),
            "endT", F3(_dashEndT >= 0.0 ? _dashEndT : ServerNow()),
            "startPos", Xz(_dashStart),
            "endPos", Xz(end),
            "dir", Xz(_dashDir),
            "speed", QABoss116LabProbe.F2(_dashSpeed),
            "durS", QABoss116LabProbe.F3(_dashTotal),
            "intentM", QABoss116LabProbe.F2(intent),
            "actualM", QABoss116LabProbe.F2(actual),
            "shortM", QABoss116LabProbe.F2(shortfall),
            "blocked", QABoss116LabProbe.B(blocked),
            "blockPos", hasStall ? Xz(_stallPos) : "na",
            "blockCollider", hasStall ? _stallCollider : "na",
            "blockLayer", hasStall ? _stallLayer : "na",
            "blockAtS", hasStall ? QABoss116LabProbe.F3(_stallAt) : "na",
            "endState", me != null ? me.CurrentState.ToString() : "na",
            "attached", QABoss116LabProbe.B(_dashAttached),
            "prevFrame", _dashPrevFrameState,
            "canMovePrev", _dashPrevCanMove,
            "rootMotion", _dashRootMotion,
            "bossM", _dashBossM,
            "bossAlongM", _dashBossAlong,
            "bossLateralM", _dashBossLateral,
        };
        kv.AddRange(rec.ToKv(reason));
        string line = QABoss103PartFile.Format(QABoss116Lab.DashKind, kv.ToArray());
        Write(line);
        // 로그 콜백 안에서 부를 수 있어 콘솔 줄은 다음 Update 에 찍는다.
        if (blocked || reason != QABoss116DashStop.Full)
            _pendingConsole.Add($"{Tag} event=dash_cut reason={reason} prev={rec.PrevState} transfer={QABoss116LabProbe.B(rec.Transfer)} " +
                                $"intentM={QABoss116LabProbe.F2(intent)} actualM={QABoss116LabProbe.F2(actual)} next={rec.NextState} detail={QABoss103Plan.Token(rec.Detail())}");
    }

    private void FlushConsole()
    {
        if (_pendingConsole.Count == 0)
            return;
        string[] lines = _pendingConsole.ToArray();
        _pendingConsole.Clear();
        foreach (string l in lines)
            Debug.Log(l);
    }

    /// <summary>내 플레이어 Animator 와 상체 레이어(<c>DashInterruptUpper</c>) 번호 — 플레이어가 바뀌면 다시 찾는다.</summary>
    private Animator AnimOf(Player me)
    {
        if (me == null)
            return null;
        if (me != _animOf || _anim == null)
        {
            _animOf = me;
            _anim = me.GetComponentInChildren<Animator>();
            _upperLayer = -1;
        }
        if (_upperLayer < 0 && _anim != null && _anim.isInitialized)
            _upperLayer = _anim.GetLayerIndex(UpperLayerName);
        return _anim;
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
        if (Manual)
        {
            WriteManual(line);
            return;
        }
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
