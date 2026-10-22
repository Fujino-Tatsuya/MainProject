#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 88 Q3 — 간파 지연 스위트 진행(QA 시나리오 <c>I88</c>, MPPM 전용). 모든 인스턴스가 맵에 들어간 뒤
/// <b>호스트(메인 에디터)</b>는 <see cref="RunHost"/> 로 런을 차례로 돌리고, <b>가상 플레이어</b>는 <see cref="RunClient"/> 로 호스트 명령을 따른다.
/// <para><b>채널 = 파일</b>(Q2 원격 지연과 같은 방식): 호스트 → 클론 = 클론 루트 <see cref="QA88Command.FileName"/>,
/// 클론 → 호스트 = 각 인스턴스 콘솔 tee(<c>&lt;스위트&gt;/logs/&lt;역할&gt;.log</c>)를 호스트가 따라 읽는다(<see cref="QALogTail88"/>).
/// 원격 지연 적용 확인 = 클론 tee 의 최신 <c>[QA88] event=netsim delayMs</c>.</para>
/// <para><b>스위트 동안</b> 보스 제한시간을 멈춘다(<see cref="QABossTimerHold88"/> — 정리 때 원복).</para>
/// <para><b>런 하나</b>: (대상이 바뀌면 강제 개시) → 런 전 위치 검사(아니면 재배치, 그래도 아니면 건너뜀) → 원격 지연 파일 → 클론 netsim 확인 → 호스트 <c>run_begin</c> → 클론 <c>go</c>(누르는 클론 1명만 봇)
/// → 누르는 클론 <c>run_end</c>(N회) 또는 런 상한 → 클론 <c>idle</c> → 2초 유예(늦은 판정) → 호스트 <c>run_end</c> → 집계·리포트 덮어쓰기.
/// 런 동안 0.5초마다 <see cref="QABossStart88.KeepAliveServer"/>(파티·대상 체력 유지), 23호는 1초마다 점프 콤보 예약, 매 프레임 <see cref="QABossWatch88"/>(보스 공격·창 진단).</para>
/// <para>정리(정상·중단·예외): 여기서는 클론 <c>end</c>·원격 지연 0·봇 정지까지. Play 정지·가상 플레이어 비활성·시나리오 모드 해제는
/// 에디터 감시자(<c>QAInterruptSuite88Watcher</c>)가 <see cref="Finished"/>·<see cref="LastBeat"/> 를 보고 한다.</para>
/// </summary>
public static class QAInterruptSuite88
{
    private const string Tag = QAInterruptLag88Log.QA88Tag;
    /// <summary>호스트 런타임이 스위트 폴더를 남기는 EditorPrefs 키(에디터 감시자가 중단 비고를 붙일 때 읽는다).</summary>
    public const string SuiteDirPrefKey = "QA.88.SuiteDir";
    private const float FollowerWaitSeconds = 120f;
    private const float NetsimConfirmSeconds = 20f;
    private const float StopAckSeconds = 8f;
    private const float JudgeGraceSeconds = 2f;
    private const float KeepAliveSeconds = 0.5f;
    private const float ReserveSeconds = 1f;
    private const float ClientNoCommandSeconds = 900f;
    private const float ClientLimitSeconds = 6f * 3600f;
    /// <summary>런 전 위치 검사에서 재배치를 몇 번까지 해 보는가.</summary>
    private const int PlaceAttempts = 2;
    /// <summary>재배치 뒤 근처가 될 때까지 기다리는 시간(초) — 원격은 오너 권한 이동이라 RPC 왕복(+원격 지연) 뒤 복제된다.</summary>
    private const float PlaceSettleSeconds = 5f;

    /// <summary>호스트 스위트가 시작됐다(이번 Play).</summary>
    public static bool Started { get; private set; }
    /// <summary>호스트 스위트가 끝났다(정상·중단 모두) — 에디터 감시자가 Play 를 멈춘다.</summary>
    public static bool Finished { get; private set; }
    /// <summary>호스트 진행 심장박동(<see cref="Time.realtimeSinceStartupAsDouble"/>). 오래 멈추면 감시자가 예외·정지로 보고 정리한다.</summary>
    public static double LastBeat { get; private set; }
    public static string SuiteDir { get; private set; }

    private static void Beat() => LastBeat = Time.realtimeSinceStartupAsDouble;

    /// <summary>도메인 리로드 없는 Play 진입에서도 이전 Play 의 끝 표시가 남지 않게.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Started = false;
        Finished = false;
        LastBeat = 0.0;
        SuiteDir = null;
    }

    /// <summary>스위트에 들어가기 전에 끝났을 때(맵 진입 실패 등) — 감시자가 Play 를 멈추고 정리하게 한다.</summary>
    public static void MarkFinished()
    {
        Started = true;
        Finished = true;
        Beat();
    }

    // ── 설정·명령 파일 ─────────────────────────────────────────

    /// <summary>스위트 설정(메인 프로젝트 Library — git 밖). 에디터 메뉴가 쓰고 호스트가 읽는다. 클론 루트엔 없다.</summary>
    public static string ConfigPath => Path.Combine(QANetSim88.OwnRoot, "Library", "QA88", "suite.txt");

    public static bool TryReadConfig(out QA88SuiteConfig cfg)
    {
        cfg = null;
        try
        {
            return File.Exists(ConfigPath) && QA88SuiteConfig.TryParse(File.ReadAllText(ConfigPath), out cfg);
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static void WriteConfig(QA88SuiteConfig cfg)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
        File.WriteAllText(ConfigPath, cfg.Format());
    }

    /// <summary>러너 상한(초). 호스트 = 설정 기준, 클론 = 넉넉한 고정값(호스트의 <c>end</c> 로 끝난다).</summary>
    public static float TimeLimitSeconds() =>
        File.Exists(ConfigPath) && TryReadConfig(out QA88SuiteConfig cfg) ? cfg.SuiteTimeLimitSeconds() : ClientLimitSeconds;

    public static string CommandPath(string root) => Path.Combine(root, QA88Command.FileName);

    /// <summary>클론 명령 파일을 모두 지운다(이전 스위트의 <c>go</c> 가 남아 있으면 클론이 바로 봇을 켠다).</summary>
    public static int DeleteCommandFiles()
    {
        int n = 0;
        foreach (string root in QANetSim88.CloneRoots())
        {
            string path = CommandPath(root);
            if (!File.Exists(path))
                continue;
            File.Delete(path);
            n++;
        }
        return n;
    }

    private static void WriteCommand(string root, QA88Command cmd)
    {
        try
        {
            File.WriteAllText(CommandPath(root), cmd.Format());
        }
        catch (IOException e)
        {
            Debug.LogWarning($"{Tag} 명령 파일 쓰기 실패 {root}: {e.Message}");
        }
    }

    private static string CloneRole(string root) => "client-" + Path.GetFileName(root);

    /// <summary>
    /// <paramref name="sinceT"/>(기록소 경과초) 이후 새로 잡히거나 재발한 Warning 이상 발견 수(노이즈·오탐 제외)와 첫 요약.
    /// 기존 탐지기 5종(예외·소프트락·성능·네트워크·불변식) 결과를 런 비고로 옮길 때 쓴다.
    /// </summary>
    public static int FindingsSince(QARecorder rec, float sinceT, out string top)
    {
        top = "";
        int n = 0;
        if (rec == null)
            return 0;
        foreach (QAFinding f in rec.Findings)
        {
            if (f.Severity < QASeverity.Warning || QAKnownNoise.IsExcluded(f) || f.LastTimeSeconds < sinceT)
                continue;
            n++;
            if (top.Length == 0)
            {
                string s = $"{f.Severity}/{f.Category}:{f.Summary}";
                int nl = s.IndexOf('\n');
                if (nl >= 0) s = s.Substring(0, nl);
                top = s.Length > 80 ? s.Substring(0, 80) + "…" : s;
            }
        }
        return n;
    }

    private static float RecorderNow(QARecorder rec) => rec != null ? Mathf.Max(0f, Time.time - rec.SessionStartTime) : 0f;

    // ── 호스트 ────────────────────────────────────────────────

    /// <summary>[호스트] 스위트 진행. 예외가 나도 정리(클론 end·지연 0·봇 정지·리포트)하고 <see cref="Finished"/> 를 세운다.</summary>
    public static IEnumerator RunHost(QAScenarioContext ctx)
    {
        Started = true;
        Finished = false;
        Beat();
        if (!TryReadConfig(out QA88SuiteConfig cfg))
        {
            ctx.Fail("88 스위트 설정", $"{ConfigPath} 없음·런 0개 — Tools/QA/88 메뉴로 실행할 것");
            Finished = true;
            yield break;
        }
        string dir = QAReportWriter.ScenarioRunDir("88", null);
        if (dir == null)
        {
            ctx.Fail("88 리포트 폴더", "만들지 못함(콘솔 확인)");
            Finished = true;
            yield break;
        }
        SuiteDir = dir;
        EditorPrefs.SetString(SuiteDirPrefKey, dir);

        var host = new HostRun(ctx, cfg, dir);
        IEnumerator body = host.Execute();
        while (true)
        {
            object current;
            try
            {
                if (!body.MoveNext())
                    break;
                current = body.Current;
            }
            catch (Exception e)
            {
                host.Abort($"예외: {e.GetType().Name}: {e.Message}");
                Debug.LogException(e);
                break;
            }
            yield return current;
        }
        host.Close();
        Finished = true;
    }

    /// <summary>호스트 스위트 한 번의 상태.</summary>
    private sealed class HostRun
    {
        private readonly QAScenarioContext _ctx;
        private readonly QA88SuiteConfig _cfg;
        private readonly string _dir;
        private readonly QA88ReportHeader _header = new QA88ReportHeader();
        private readonly List<QA88RunResult> _results = new List<QA88RunResult>();
        private readonly List<CloneState> _clones = new List<CloneState>();
        private QALogTee88 _tee;
        private QAInterruptBot88 _hostBot;
        private QA88TwentyThreeAssist _assist;   // 23호 런 동안만(대상 고정 + 넉백 면역)
        private MonsterBase _target;
        private QAInterruptTargetKind _targetKind;
        private readonly QABossWatch88 _watch = new QABossWatch88();
        private int _seq;
        private string _status = "준비";
        private bool _closed;
        private float _nextKeepAlive, _nextReserve;
        private QABossTimerHold88 _timerHold;    // 스위트 동안 보스 제한시간 정지(정리 때 원복)

        public HostRun(QAScenarioContext ctx, QA88SuiteConfig cfg, string dir)
        {
            _ctx = ctx;
            _cfg = cfg;
            _dir = dir;
        }

        private string HostLogPath => Path.Combine(_dir, "logs", "host.log");

        public IEnumerator Execute()
        {
            _tee = QALogTee88.Open(HostLogPath);
            _header.UnityVersion = Application.unityVersion;
            _header.Attempts = _cfg.Attempts;
            _header.RunTimeoutSec = _cfg.RunTimeoutSec;
            foreach (QA88RunSpec s in _cfg.Runs)
            {
                if (s.Kind != QAInterruptTargetKind.TwentyThree)
                    continue;
                _header.Notes.Add("**측정 조건(23호): 대상 고정(AdoptAggro QA88) + 넉백 면역(SuperArmor) — 이상 조건, 실제 플레이와 다름.** " +
                                   "23호 런 동안 서버가 1초마다 누르는 클라1 로 어그로를 승계시키고 콤보 중이면 콤보 고정 대상도 옮기며(`event=aggro_force`), 같은 클라에 QA 출처 무기한 슈퍼아머를 건다(런 끝·정리에서 해제, `event=assist_end`). 중간보스 런엔 적용하지 않는다.");
                break;
            }
            _header.Notes.Add("판 유지: 런 동안 0.5초마다 `QABossStart88.KeepAliveServer` 가 파티 전원과 **대상 보스(중간보스·23호) 체력**을 최대 체력 70% 아래면 최대로 채운다 — 측정 유지 목적(처치·전멸로 런이 끊기지 않게). 통계·피격 연출·사망 신호 없음.");
            _header.Notes.Add("23호: 1초마다 점프×3 콤보 예약(F3 Dev API, 3번째 착지 간파 창). A 스윕은 23호 창 길이가 복제되지 않아 런의 첫 창을 길이 학습에만 쓴다. " +
                               "봇은 착지 창(체공 직후 열린 창)만 배우고 누른다(개전 돌진 창 등은 건너뜀).");
            _header.Notes.Add("조준: 봇은 대상(중간보스·23호) 모두 누를 때 대상 쪽을 조준한다(`UseSkillToward`, 누름 줄 `aim=target`) — 마우스 위치로 간파 방향이 달라져 측정이 오염되지 않게. 누름 줄 `surf`·`reach`·`inReach`·`faceDeg` 로 사거리·방향을 본다. " +
                               "`isTarget` = 누를 때 보스 복제 어그로 대상이 봇인가(23호 점프×3 은 콤보 시작 때 어그로 대상에 고정), `knockedBack` = 창이 열린 뒤 또는 누르기 1.5초 안에 봇이 넉백됐는가(`knockAgoS`).");
            _header.Notes.Add("보스 제한시간: 스위트 동안 멈춘다(`QABossTimerHold88` — 만료 시각을 뒤로 미룸, 새로 시작되는 타이머도 매 프레임 다시 미룸, 정리 때 남은 시간 그대로 원복). " +
                               "풀타임 1차(88-20261022_180739) 런2 에서 존4 스킵 때 시작된 420초가 만료돼 파티가 보스방으로 끌려간 것을 막는다. 로그 `event=boss_timer_hold`·`boss_timer_restore`.");
            _header.Notes.Add($"런 전 위치 검사: 매 런 시작 전 파티 전원(누르는 봇 `*` 포함)이 대상 근처(중간보스 {QAPartyPlacement88.MidbossNearMeters:0}m · 23호 {QAPartyPlacement88.TwentyThreeNearMeters:0}m, 높이 차 {QAPartyPlacement88.NearMaxDy:0}m)인지 본다(`event=party_check`). " +
                               $"아니면 강제 개시와 같은 경로로 재배치(`event=party_relocate`, 최대 {PlaceAttempts}회·회당 {PlaceSettleSeconds:0}초 대기)하고, 그래도 아니면 런을 건너뛴다(비고에 사유).");
            _header.Notes.Add("원격 지연 = Multiplayer Tools NetworkSimulator 송신·수신 각각 X ms(RTT 대략 +2X). 호스트는 지연 0. 런 시작 전 클론 콘솔의 `[QA88] event=netsim` 으로 적용을 확인한다.");
            _header.Notes.Add("진단 줄(원본 로그): 호스트 `event=boss_attack`·`boss_window`(엣지)·`boss_diag`(5초 — 상태·휴면·NavMesh·어그로·창·플레이어별 거리·깨어 있는 일반몹), 파티 배치 `party_points`·`party_point`, 누르는 클론 `bot_diag`(5초 — 관측 경로·창 값·마지막 관측·보스 거리·보스 상태·낭떠러지 멈춤).");
            _header.Notes.Add("탐지기: 기존 5종(예외·소프트락·성능·네트워크·불변식)이 모든 인스턴스에서 상시 감시한다. 런 구간에 새로 잡히거나 재발한 Warning 이상(노이즈 제외)은 비고 '탐지기' 로 남긴다. 인스턴스별 전체 집계는 각 `48-*` 리포트.");
            Debug.Log($"{Tag} event=suite_begin runs={_cfg.Runs.Count} attempts={_cfg.Attempts} runTimeoutSec={_cfg.RunTimeoutSec} hostBot={(_cfg.HostBot ? "true" : "false")} dir={QAInterruptLag88Plan.LogToken(_dir)}");
            WriteReport();
            _timerHold = QABossTimerHold88.Begin();

            // 1) 클론 팔로워 준비 — 명령 파일로 스위트 폴더를 알리고 tee 의 follower_ready 를 기다린다.
            QANetSim88.ResetRemoteDelayFiles();
            foreach (string root in QANetSim88.CloneRoots())
                _clones.Add(new CloneState(root, Path.Combine(_dir, "logs", CloneRole(root) + ".log")));
            SendAll("idle", null);
            float until = Time.realtimeSinceStartup + FollowerWaitSeconds;
            while (Time.realtimeSinceStartup < until && !AllReady())
            {
                _timerHold?.Tick("follower_wait");
                PollClones();
                Beat();
                yield return null;
            }
            PollClones();
            var ready = new List<CloneState>();
            foreach (CloneState c in _clones)
                if (c.Ready) ready.Add(c);
            if (ready.Count > 0)
                ready[0].Presser = true;
            foreach (CloneState c in _clones)
                if (!c.Ready) _header.Notes.Add($"가상 플레이어 {c.Role}: {FollowerWaitSeconds:0}초 안에 팔로워 준비 없음 — 제외(비활성 클론 폴더일 수 있음).");
            _header.Pressers = (ready.Count > 0 ? $"원격 {ready[0].Role} 1명" : "원격 없음") + (_cfg.HostBot ? " + 호스트(기준 그룹)" : "") +
                               (ready.Count > 1 ? $" · 나머지 원격 {ready.Count - 1}명은 대기(같은 창을 여럿이 누르면 먼저 성공한 쪽이 창을 닫아 서로 오염)" : "");
            foreach (CloneState c in ready)
                _header.LogFiles.Add("logs/" + Path.GetFileName(c.LogPath));
            _header.LogFiles.Insert(0, "logs/host.log");
            _header.Notes.Add("원본 로그 = 스위트 시작 뒤 각 인스턴스 콘솔 전부(tee). `[InterruptLag]` 판정 줄은 호스트 로그에만 있다.");
            if (ready.Count == 0 && !_cfg.HostBot)
            {
                Abort("준비된 가상 플레이어 없음 — Tools/QA/Activate Virtual Players (2,3) 후 실행할 것");
                _ctx.Fail("88 가상 플레이어 준비", $"{FollowerWaitSeconds:0}초 안에 팔로워 없음(클론 폴더 {_clones.Count}개)");
                yield break;
            }

            // 2) 런.
            QAInterruptTargetKind? startedKind = null;
            bool startOk = false;
            string startDetail = "";
            for (int i = 0; i < _cfg.Runs.Count; i++)
            {
                QA88RunSpec spec = _cfg.Runs[i];
                _status = $"진행 중 {i}/{_cfg.Runs.Count}";
                var result = new QA88RunResult { Spec = spec };
                Beat();

                if (startedKind != spec.Kind)
                {
                    StopHostBot("kind_change");
                    EndAssist("kind_change");
                    var start = new QABossStart88Result();
                    yield return WithTimerHold(QABossStart88.Start(spec.Kind, start), "boss_start");   // 존4 스킵(G8)이 제한시간을 새로 시작한다
                    Beat();
                    startedKind = spec.Kind;
                    startOk = start.Ok;
                    startDetail = start.Detail;
                    _target = start.Target;
                    _targetKind = spec.Kind;
                    _watch.Bind(_target);
                    Debug.Log($"{Tag} event=boss_start kind={QAInterruptLag88Plan.KindToken(spec.Kind)} ok={(start.Ok ? "true" : "false")} detail={QAInterruptLag88Plan.LogToken(start.Detail)}");
                }
                if (!startOk)
                {
                    result.Skipped = true;
                    result.Notes.Add($"강제 개시 실패: {startDetail}");
                    Finish(result);
                    continue;
                }

                // 런 전 위치 검사 — 대상 근처가 아니면 재배치, 그래도 아니면 건너뛴다(공회전 금지).
                var place = new PlaceCheck();
                yield return EnsurePartyNear(spec, ready.Count > 0 ? ready[0].ClientId : -1, place);
                Beat();
                if (!place.Ok)
                {
                    result.Skipped = true;
                    result.Notes.Add($"런 전 위치 검사 실패 — {place.Detail}");
                    Finish(result);
                    continue;
                }
                if (place.Relocations > 0)
                    result.Notes.Add($"런 전 재배치 {place.Relocations}회 뒤 시작");

                // 원격 지연 → 클론 netsim 확인.
                QANetSim88.SetRemoteDelayFiles(spec.DelayMs);
                float confirmUntil = Time.realtimeSinceStartup + NetsimConfirmSeconds;
                while (Time.realtimeSinceStartup < confirmUntil && !NetsimConfirmed(ready, spec.DelayMs))
                {
                    PollClones();
                    KeepAlive();
                    Beat();
                    yield return null;
                }
                PollClones();
                if (!NetsimConfirmed(ready, spec.DelayMs))
                {
                    result.Skipped = true;
                    result.Notes.Add($"원격 지연 {spec.DelayMs}ms 적용 미확인({NetsimSummary(ready)}) — 런 건너뜀");
                    Finish(result);
                    continue;
                }
                foreach (CloneState c in ready)
                    if (!c.NetsimAvailable)
                        result.Notes.Add($"{c.Role} 시뮬레이터 available=false(지연 미적용 가능)");

                // 런 시작.
                float runStartT = RecorderNow(_ctx.Recorder);
                Debug.Log(QAInterruptLag88Log.FormatRunBegin(spec, "host", _cfg.HostBot));
                int seed = 88 + spec.Index;
                foreach (CloneState c in ready)
                    Send(c, "go", spec, c.Presser, seed);
                if (_cfg.HostBot && _ctx.Input != null)
                {
                    QAInterruptBot88Config bc = QAInterruptBot88Config.Default(spec.Kind, spec.Mode);
                    bc.Attempts = _cfg.Attempts;
                    bc.Seed = seed;
                    _hostBot = QAInterruptBot88.Begin(_ctx.Input.gameObject, bc);
                }
                if (spec.Kind == QAInterruptTargetKind.TwentyThree && _target is TwentyThreeBoss ttBoss)
                {
                    CloneState p = ready.Count > 0 ? ready[0] : null;
                    _assist = QA88TwentyThreeAssist.Begin(ttBoss, p != null && p.ClientId >= 0 ? (ulong)p.ClientId : 0UL, p != null && p.ClientId >= 0, spec.Index);
                }

                CloneState presser = ready.Count > 0 ? ready[0] : null;
                float runUntil = Time.realtimeSinceStartup + _cfg.RunTimeoutSec;
                bool timedOut = false;
                while (true)
                {
                    PollClones();
                    KeepAlive();
                    Beat();
                    bool presserDone = presser == null || presser.RunEnds.ContainsKey(spec.Index);
                    bool hostDone = _hostBot == null || _hostBot.IsDone;
                    if (presserDone && hostDone)
                        break;
                    if (Time.realtimeSinceStartup >= runUntil)
                    {
                        timedOut = true;
                        break;
                    }
                    yield return null;
                }
                StopHostBot("run_end");
                EndAssist("run_end");

                // 클론 런 정지(대기 클론 포함) → run_end 수신.
                foreach (CloneState c in ready)
                    Send(c, "idle", spec, false, seed);
                float ackUntil = Time.realtimeSinceStartup + StopAckSeconds;
                while (Time.realtimeSinceStartup < ackUntil && !AllRunEnded(ready, spec.Index))
                {
                    PollClones();
                    KeepAlive();
                    Beat();
                    yield return null;
                }
                float graceUntil = Time.realtimeSinceStartup + JudgeGraceSeconds;
                while (Time.realtimeSinceStartup < graceUntil)
                {
                    PollClones();
                    KeepAlive();
                    Beat();
                    yield return null;
                }

                int hostFindings = FindingsSince(_ctx.Recorder, runStartT, out string hostTop);
                Debug.Log(QAInterruptLag88Log.FormatRunEnd(spec.Index, "host", timedOut ? "timeout" : "done",
                    _hostBot != null ? _hostBot.Attempts : 0, _hostBot != null ? _hostBot.Windows : 0, _hostBot != null ? _hostBot.Skipped : 0, hostFindings, hostTop));

                if (timedOut)
                    result.Notes.Add($"런 상한 {_cfg.RunTimeoutSec}s 초과로 끊음");
                if (hostFindings > 0)
                    result.Notes.Add($"탐지기(host) {hostFindings}건: {hostTop}");
                foreach (CloneState c in ready)
                {
                    if (!c.RunEnds.TryGetValue(spec.Index, out QA88LogLine end))
                    {
                        result.Notes.Add($"{c.Role} run_end 없음(끊김·정지 의심)");
                        continue;
                    }
                    int f = end.GetInt("findings", 0);
                    if (f > 0)
                        result.Notes.Add($"탐지기({c.Role}) {f}건: {end.Get("findingTop")}");
                    if (c.Presser && end.GetInt("attempts", 0) < _cfg.Attempts)
                        result.Notes.Add($"{c.Role} 누름 {end.GetInt("attempts", 0)}/{_cfg.Attempts}(창 {end.GetInt("windows", 0)}, 쿨다운·거부 건너뜀 {end.GetInt("skipped", 0)})");
                }

                result.Groups = QAInterruptLag88Aggregate.Aggregate(CollectRunLines(ready, spec.Index));
                Finish(result);
            }

            _status = "완료";
            Debug.Log($"{Tag} event=suite_end runs={_results.Count} dir={QAInterruptLag88Plan.LogToken(_dir)}");
            _ctx.Eye("88 간파 지연 리포트", $"{_dir}/report.md (런 {_results.Count}/{_cfg.Runs.Count}) — 판정 없음, 표만");
        }

        /// <summary>중단 사유를 상태로 남긴다(리포트는 <see cref="Close"/> 에서).</summary>
        public void Abort(string reason)
        {
            _status = $"중단: {reason}";
            Debug.LogWarning($"{Tag} event=suite_abort reason={QAInterruptLag88Plan.LogToken(reason)}");
        }

        /// <summary>정상·중단·예외 공통 정리 — 클론 end·원격 지연 0·봇 정지·리포트·tee 닫기.</summary>
        public void Close()
        {
            if (_closed)
                return;
            _closed = true;
            StopHostBot("suite_end");
            try
            {
                _timerHold?.End("suite_end");
                _timerHold = null;
                QABossTimerHold88.ReleaseActive("suite_end");   // 다른 경로로 남은 것까지
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} 보스 제한시간 원복 중 오류: {e.Message}");
            }
            try
            {
                EndAssist("suite_end");
                QA88TwentyThreeAssist.ReleaseActive("suite_end");   // 다른 경로로 남은 것까지
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} 23호 이상 조건 해제 중 오류: {e.Message}");
            }
            try
            {
                SendAll("end", null);
                QANetSim88.ResetRemoteDelayFiles();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} 정리 중 파일 오류: {e.Message}");
            }
            if (_status.StartsWith("진행 중", StringComparison.Ordinal) || _status == "준비")
                _status = $"중단: {_status}에서 끝남";
            WriteReport();
            _tee?.Dispose();
            _tee = null;
        }

        private void Finish(QA88RunResult result)
        {
            _results.Add(result);
            _status = $"진행 중 {_results.Count}/{_cfg.Runs.Count}";
            WriteReport();
        }

        private void WriteReport()
        {
            try
            {
                _header.Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                _header.Status = _status;
                File.WriteAllText(Path.Combine(_dir, "report.md"),
                    QAInterruptLag88ReportBuilder.Build(_header, _cfg.Runs, _results), new UTF8Encoding(true));
            }
            catch (Exception e)
            {
                Debug.LogError($"{Tag} 리포트 저장 실패: {e.Message}");
            }
        }

        private void KeepAlive()
        {
            _timerHold?.Tick("run");
            _watch.Tick(_targetKind);
            float now = Time.realtimeSinceStartup;
            if (now >= _nextKeepAlive)
            {
                _nextKeepAlive = now + KeepAliveSeconds;
                QABossStart88.KeepAliveServer(_target);
                _assist?.RefreshSuperArmor();
            }
            if (now >= _nextReserve && _target is TwentyThreeBoss boss)
            {
                _nextReserve = now + ReserveSeconds;
                _assist?.ForceAggroBeforeReserve();   // 콤보 예약 직전 — 콤보 중이면 고정 대상까지
                QABossStart88.ReserveJumpComboIfIdle(boss);
            }
        }

        private void EndAssist(string reason)
        {
            _assist?.End(reason);
            _assist = null;
        }

        /// <summary>안쪽 코루틴을 돌리는 동안 매 프레임 보스 제한시간을 다시 막는다(강제 개시가 새 타이머를 시작해도 바로 미룬다).</summary>
        private IEnumerator WithTimerHold(IEnumerator inner, string where)
        {
            while (true)
            {
                _timerHold?.Tick(where);
                Beat();
                if (!inner.MoveNext())
                    break;
                yield return inner.Current;
            }
            _timerHold?.Tick(where);
        }

        /// <summary>
        /// 런 전 위치 검사 — 파티 전원(누르는 봇 포함)이 대상 근처인가(<see cref="QABossStart88.IsPartyNear"/>). 아니면 강제 개시와 같은 경로
        /// (<see cref="QABossStart88.RelocatePartyNear"/>)로 재배치하고 근처가 될 때까지 <see cref="PlaceSettleSeconds"/> 기다린다. 최대 <see cref="PlaceAttempts"/> 회.
        /// </summary>
        private IEnumerator EnsurePartyNear(QA88RunSpec spec, int presserClientId, PlaceCheck outcome)
        {
            float radius = QAPartyPlacement88.NearRadius(spec.Kind);
            string kind = QAInterruptLag88Plan.KindToken(spec.Kind);
            for (int attempt = 0; ; attempt++)
            {
                bool near = QABossStart88.IsPartyNear(_target, radius, presserClientId, out string detail);
                Debug.Log($"{Tag} event=party_check run={spec.Index} kind={kind} attempt={attempt} ok={(near ? "true" : "false")} radius={radius:0} maxDy={QAPartyPlacement88.NearMaxDy:0} " +
                          $"relocations={outcome.Relocations} target={QAInterruptLag88Plan.LogToken(_target != null ? _target.name : "none")} {detail}");
                if (near)
                {
                    outcome.Ok = true;
                    yield break;
                }
                if (_target == null || attempt >= PlaceAttempts)
                {
                    outcome.Detail = _target == null
                        ? "대상 보스 없음(처치·디스폰 의심)"
                        : $"재배치 {outcome.Relocations}회 뒤에도 대상 근처 아님(반경 {radius:0}m · 높이 차 {QAPartyPlacement88.NearMaxDy:0}m, {detail})";
                    Debug.LogWarning($"{Tag} event=party_check_skip run={spec.Index} kind={kind} relocations={outcome.Relocations} {detail}");
                    yield break;
                }

                int moved = QABossStart88.RelocatePartyNear(spec.Kind, _target, out string method);
                outcome.Relocations++;
                Debug.Log($"{Tag} event=party_relocate run={spec.Index} kind={kind} n={outcome.Relocations} moved={moved} method={method}");
                float until = Time.realtimeSinceStartup + PlaceSettleSeconds;
                while (Time.realtimeSinceStartup < until && !QABossStart88.IsPartyNear(_target, radius, presserClientId, out _))
                {
                    PollClones();
                    KeepAlive();
                    Beat();
                    yield return null;
                }
            }
        }

        private void StopHostBot(string reason)
        {
            if (_hostBot != null)
                _hostBot.Stop(reason);
            _hostBot = null;
        }

        // ── 클론 ──

        private void SendAll(string state, QA88RunSpec? spec)
        {
            foreach (CloneState c in _clones)
                Send(c, state, spec ?? default, false, 0);
        }

        private void Send(CloneState c, string state, QA88RunSpec spec, bool press, int seed)
        {
            WriteCommand(c.Root, new QA88Command
            {
                Seq = ++_seq,
                State = state,
                Run = state == "go" ? spec.Index : -1,
                Kind = spec.Kind,
                Mode = spec.Mode,
                Attempts = _cfg.Attempts,
                Seed = seed,
                Press = press,
                SuiteDir = _dir,
            });
        }

        private void PollClones()
        {
            foreach (CloneState c in _clones)
                c.Poll();
        }

        private bool AllReady()
        {
            foreach (CloneState c in _clones)
                if (!c.Ready) return false;
            return true;
        }

        private static bool NetsimConfirmed(List<CloneState> ready, int delayMs)
        {
            foreach (CloneState c in ready)
                if (c.NetsimMs != delayMs) return false;
            return true;
        }

        private static string NetsimSummary(List<CloneState> ready)
        {
            var parts = new List<string>();
            foreach (CloneState c in ready)
                parts.Add($"{c.Role}={(c.NetsimMs < 0 ? "없음" : c.NetsimMs + "ms")}");
            return string.Join(", ", parts);
        }

        private static bool AllRunEnded(List<CloneState> ready, int run)
        {
            foreach (CloneState c in ready)
                if (!c.RunEnds.ContainsKey(run)) return false;
            return true;
        }

        private IEnumerable<QA88LogLine> CollectRunLines(List<CloneState> ready, int run)
        {
            var lines = new List<QA88LogLine>();
            var files = new List<string> { HostLogPath };
            foreach (CloneState c in ready)
                files.Add(c.LogPath);
            foreach (string path in files)
            {
                Dictionary<int, List<QA88LogLine>> runs = QAInterruptLag88Log.SplitByRun(QALogTail88.ReadAllLines(path));
                if (runs.TryGetValue(run, out List<QA88LogLine> l))
                    lines.AddRange(l);
            }
            return lines;
        }
    }

    /// <summary>런 전 위치 검사 결과(코루틴 밖으로 넘긴다).</summary>
    private sealed class PlaceCheck
    {
        public bool Ok;
        public int Relocations;
        public string Detail = "";
    }

    /// <summary>호스트가 본 가상 플레이어 하나(명령 파일 위치·tee 따라 읽기 상태).</summary>
    private sealed class CloneState
    {
        public readonly string Root;
        public readonly string Role;
        public readonly string LogPath;
        public bool Ready;
        public bool Presser;
        public int NetsimMs = -1;
        /// <summary>클론의 NetworkManager.LocalClientId(<c>follower_ready</c> 줄). 없으면 -1 — 23호 이상 조건을 걸 대상.</summary>
        public int ClientId = -1;
        public bool NetsimAvailable = true;
        public readonly Dictionary<int, QA88LogLine> RunEnds = new Dictionary<int, QA88LogLine>();
        private readonly QALogTail88 _tail;

        public CloneState(string root, string logPath)
        {
            Root = root;
            Role = CloneRole(root);
            LogPath = logPath;
            _tail = new QALogTail88(logPath);
        }

        public void Poll()
        {
            foreach (QA88LogLine l in _tail.ReadNew())
            {
                if (!l.IsQA88)
                    continue;
                switch (l.Event)
                {
                    case "follower_ready":
                        Ready = true;
                        ClientId = l.GetInt("clientId", -1);
                        break;
                    case "netsim":
                        NetsimMs = l.GetInt("delayMs");
                        NetsimAvailable = l.Get("available") != "False" && l.Get("available") != "false";
                        break;
                    case "follower_netsim":
                        // 팔로워가 준비 순간의 적용값을 알린다(그 전에 netsim 줄이 tee 밖에서 찍혔을 수 있어서).
                        NetsimMs = l.GetInt("delayMs");
                        break;
                    case "run_end":
                        RunEnds[l.GetInt("run")] = l;
                        break;
                }
            }
        }
    }

    // ── 가상 플레이어(클론) ─────────────────────────────────────

    /// <summary>[클론] 호스트 명령을 따른다: 스위트 폴더로 tee 시작 → go 면 런 시작(누르는 클론만 봇) → idle/다음 go 면 런 끝 → end 면 종료.</summary>
    public static IEnumerator RunClient(QAScenarioContext ctx)
    {
        string root = QANetSim88.OwnRoot;
        string role = CloneRole(root);
        string cmdPath = CommandPath(root);
        QALogTee88 tee = null;
        QAInterruptBot88 bot = null;
        int lastSeq = -1, curRun = -1, lastBegun = -1;
        float runStartT = 0f;
        float giveUp = Time.realtimeSinceStartup + ClientNoCommandSeconds;
        float nextPoll = 0f;
        int runs = 0;

        void EndRun(string reason)
        {
            if (curRun < 0)
                return;
            if (bot != null && bot.IsRunning)
                bot.Stop(reason);
            int f = FindingsSince(ctx.Recorder, runStartT, out string top);
            Debug.Log(QAInterruptLag88Log.FormatRunEnd(curRun, role, reason,
                bot != null ? bot.Attempts : 0, bot != null ? bot.Windows : 0, bot != null ? bot.Skipped : 0, f, top));
            bot = null;
            curRun = -1;
        }

        try
        {
            while (true)
            {
                if (curRun >= 0 && bot != null && bot.IsDone)
                    EndRun("done");

                if (Time.realtimeSinceStartup >= nextPoll)
                {
                    nextPoll = Time.realtimeSinceStartup + 0.25f;
                    QA88Command cmd = default;
                    bool have = false;
                    try
                    {
                        have = File.Exists(cmdPath) && QA88Command.TryParse(File.ReadAllText(cmdPath), out cmd);
                    }
                    catch (IOException)
                    {
                        // 호스트가 쓰는 중 — 다음 폴링
                    }

                    if (have && tee == null && !string.IsNullOrEmpty(cmd.SuiteDir))
                    {
                        tee = QALogTee88.Open(Path.Combine(cmd.SuiteDir, "logs", role + ".log"));
                        Unity.Netcode.NetworkManager nm = Unity.Netcode.NetworkManager.Singleton;
                        string clientId = nm != null && nm.IsListening ? nm.LocalClientId.ToString() : "na";
                        Debug.Log($"{Tag} event=follower_ready role={QAInterruptLag88Plan.LogToken(role)} clientId={clientId}");
                        Debug.Log($"{Tag} event=follower_netsim delayMs={QANetSim88.AppliedDelayMs}");
                    }
                    if (have && cmd.Seq != lastSeq)
                    {
                        lastSeq = cmd.Seq;
                        if (curRun >= 0 && !(cmd.State == "go" && cmd.Run == curRun))
                            EndRun("host_stop");
                        if (cmd.State == "go" && cmd.Run > lastBegun)
                        {
                            lastBegun = curRun = cmd.Run;
                            runStartT = RecorderNow(ctx.Recorder);
                            var spec = new QA88RunSpec { Index = cmd.Run, Kind = cmd.Kind, Mode = cmd.Mode, DelayMs = QANetSim88.AppliedDelayMs };
                            Debug.Log(QAInterruptLag88Log.FormatRunBegin(spec, role, cmd.Press));
                            runs++;
                            if (cmd.Press && ctx.Input != null)
                            {
                                QAInterruptBot88Config bc = QAInterruptBot88Config.Default(cmd.Kind, cmd.Mode);
                                bc.Attempts = cmd.Attempts;
                                bc.Seed = cmd.Seed;
                                bot = QAInterruptBot88.Begin(ctx.Input.gameObject, bc);
                            }
                        }
                        if (cmd.State == "end")
                        {
                            Debug.Log($"{Tag} event=follower_end role={QAInterruptLag88Plan.LogToken(role)} runs={runs}");
                            break;
                        }
                    }
                    if (tee == null && Time.realtimeSinceStartup >= giveUp)
                    {
                        ctx.Fail("88 호스트 명령 대기", $"{ClientNoCommandSeconds:0}초 동안 {cmdPath} 없음");
                        yield break;
                    }
                }
                yield return null;
            }
            ctx.Eye("88 팔로워", $"{role} 런 {runs}개 참여 — 집계는 호스트 88 리포트");
        }
        finally
        {
            EndRun("follower_exit");
            tee?.Dispose();
        }
    }
}

/// <summary>이 인스턴스 콘솔 전부를 파일로 복사한다(원본 로그 사본 + 호스트가 클론 상태를 읽는 통로). 다른 프로세스가 읽는 중에도 쓴다.</summary>
public sealed class QALogTee88 : IDisposable
{
    private StreamWriter _writer;

    public static QALogTee88 Open(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        var tee = new QALogTee88 { _writer = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true } };
        Application.logMessageReceived += tee.Handle;
        return tee;
    }

    private void Handle(string message, string stack, LogType type)
    {
        if (_writer == null)
            return;
        _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{type}] {message}");
        if ((type == LogType.Exception || type == LogType.Error || type == LogType.Assert) && !string.IsNullOrEmpty(stack))
            _writer.WriteLine(stack.TrimEnd());
    }

    public void Dispose()
    {
        Application.logMessageReceived -= Handle;
        _writer?.Dispose();
        _writer = null;
    }
}

/// <summary>다른 프로세스가 쓰는 로그 파일을 끝에서부터 이어 읽는다(완성된 줄만, <c>[QA88]</c>·<c>[InterruptLag]</c> 줄만 돌려준다).</summary>
public sealed class QALogTail88
{
    private readonly string _path;
    private long _pos;

    public QALogTail88(string path) => _path = path;

    public List<QA88LogLine> ReadNew()
    {
        var result = new List<QA88LogLine>();
        try
        {
            if (!File.Exists(_path))
                return result;
            using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (fs.Length < _pos)
                    _pos = 0;
                if (fs.Length == _pos)
                    return result;
                fs.Seek(_pos, SeekOrigin.Begin);
                var bytes = new byte[fs.Length - _pos];
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = fs.Read(bytes, read, bytes.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                int last = Array.LastIndexOf(bytes, (byte)'\n', read - 1);
                if (last < 0)
                    return result;
                _pos += last + 1;
                string text = Encoding.UTF8.GetString(bytes, 0, last + 1);
                foreach (string line in text.Split('\n'))
                    if (QAInterruptLag88Log.TryParse(line.TrimEnd('\r'), out QA88LogLine p))
                        result.Add(p);
            }
        }
        catch (IOException)
        {
            // 쓰는 중 잠김 — 다음 폴링
        }
        return result;
    }

    /// <summary>파일 전체 줄(쓰는 중이어도 읽는다). 없으면 빈 목록.</summary>
    public static List<string> ReadAllLines(string path)
    {
        var lines = new List<string>();
        try
        {
            if (!File.Exists(path))
                return lines;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                    lines.Add(line);
            }
        }
        catch (IOException e)
        {
            Debug.LogWarning($"[QA88] 로그 읽기 실패 {path}: {e.Message}");
        }
        return lines;
    }
}
#endif
