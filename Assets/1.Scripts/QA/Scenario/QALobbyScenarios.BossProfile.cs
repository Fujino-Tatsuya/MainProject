using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// 할 일 57.2 — 보스전 실측 프로파일링(P572, 솔로 호스트). 정식 흐름(로비 → 로딩 → 4.MapScene) 뒤 G10 열기 → ForceStartEncounter → Combat 을
/// 최소 90초(첫 23호 돌진·드론 고정·점프 예고를 다 볼 때까지, 최대 240초) 유지하며 Profiler 바이너리 로그(.raw, 1000프레임씩 나눔)와
/// 프레임 CSV 를 <c>_lane-artifacts/&lt;repo 폴더&gt;/57.2-profile/run-*</c> 에 남긴다. 57.2 실행·분석 메뉴는 삭제됨(155).
/// 보스 코드는 건드리지 않는다 — 예고는 리플렉션으로 관찰만 하고(<see cref="QABossProfileProbe"/>), 관찰한 프레임에 빈 마커 <c>QA57.*</c> 를 찍는다.
/// 패턴은 기존 Dev 예약(<c>TwentyThreeBoss.DevReserveNextAttack</c> = F11 돌진·F6 점프)으로 앞당기고, 판이 끝나지 않게 체력만 채운다(무적 아님 — 피격 흐름 그대로).
/// </summary>
public static partial class QALobbyScenarios
{
    private const float P572MinCombat = 90f;
    private const float P572MaxCombat = 240f;

    private static IEnumerator P572(QAScenarioContext ctx)
    {
        string dir = QABossProfileProbe.NewRunDir();
        var probe = new GameObject("[QA] BossProfileProbe").AddComponent<QABossProfileProbe>();
        UnityEngine.Object.DontDestroyOnLoad(probe.gameObject);
        probe.Begin(dir);
        try
        {
            var ok = new QAFlag();
            yield return EnsureLobbyPlayer(ctx, 120f, ok);
            if (!ok.Value)
                yield break;
            if (!QALobbyFlow.IsServer)
            {
                ctx.Skip("57.2 보스 프로파일", "솔로 호스트 전용(Dev 예약·체력 유지가 서버 경로)");
                yield break;
            }
            yield return CloseResultPanelIfOpen(ctx, null);

            // 로딩부터 기록 — 예열(로딩 중 1회)과 로딩 히치도 같은 파일에 남긴다.
            probe.StartProfiler();
            probe.SetPhase(QABossProfileProbe.Phase.Loading);
            yield return QALobbyFlow.ReadyUp(ctx.Input);
            yield return ctx.Wait(() => QALobbyFlow.InMapWithPlayer, 120f);
            if (ctx.TimedOut)
            {
                ctx.Fail("맵 진입", $"120초 안에 맵 플레이어 없음 scene={QAUtil.ActiveSceneName()}", ctx.Shot("p572_map"));
                yield break;
            }
            probe.SetPhase(QABossProfileProbe.Phase.Map);
            yield return new WaitForSecondsRealtime(3f);
            ctx.Check(probe.PrewarmLogs.Count == 1, "57.1 예열 로그 정확히 1회(로딩 중)",
                probe.PrewarmLogs.Count == 0 ? "0회" : $"{probe.PrewarmLogs.Count}회: {string.Join(" / ", probe.PrewarmLogs)}");

            // M7 과 같은 진입: G10 까지 열기 → 보스전 강제 개시 → Combat.
            MapSceneDirector tutorial = MapSceneDirector.Instance;
            if (tutorial != null && tutorial.IsTutorialActive && !tutorial.IsGateOpen(MapSceneGate.G10_BossEnterAndPad))
                MapSceneDebugActions.OpenGatesThrough(tutorial, MapSceneGate.G10_BossEnterAndPad);
            probe.SetPhase(QABossProfileProbe.Phase.BossIntro);
            BossTeleportManager tp = BossTeleportManager.Instance;
            float deadline = Time.realtimeSinceStartup + 30f;
            while (tp != null && Phase != BossEncounterPhase.Combat && !tp.ForceStartEncounter(0f) && Time.realtimeSinceStartup < deadline)
                yield return new WaitForSecondsRealtime(1f);
            yield return ctx.Wait(() => Phase == BossEncounterPhase.Combat, 150f);
            if (ctx.TimedOut)
            {
                ctx.Fail("보스전 Combat", $"단계={Phase}", ctx.Shot("p572_no_combat"));
                yield break;
            }
            probe.SetPhase(QABossProfileProbe.Phase.Combat);
            ctx.Pass("보스전 Combat 진입", $"강제 개시 → Combat ({ctx.LastWaitSeconds:F1}s)", ctx.Shot("p572_combat"));

            // 23호 돌진·점프를 기존 Dev 예약으로 앞당긴다(첫 공격 예고 = 돌진이 되게 돌진 먼저). 드론은 자기 타이머(첫 5초)로 돈다.
            yield return new WaitForSecondsRealtime(1f);
            float combatStart = Time.realtimeSinceStartup;
            float lastReserve = -999f;
            while (true)
            {
                float t = Time.realtimeSinceStartup - combatStart;
                bool all = probe.FirstDash >= 0 && probe.FirstDroneLock >= 0 && probe.FirstJump >= 0;
                if ((t >= P572MinCombat && all) || t >= P572MaxCombat || Phase != BossEncounterPhase.Combat)
                    break;
                if (t - lastReserve >= 30f)
                {
                    lastReserve = t;
                    probe.ReserveMissing();
                }
                yield return null;
            }
            float combatSeconds = Time.realtimeSinceStartup - combatStart;
            probe.StopProfiler();

            ctx.Eye("57.2 첫 예고 프레임(관찰)", probe.FirstsSummary() + $" · 전투 {combatSeconds:F0}s, 단계={Phase} — 판정은 프레임 CSV 를 직접 본다(57.2 분석 메뉴 삭제됨(155))", ctx.Shot("p572_end"));
            ctx.Check(probe.Deaths == 0, "판 유지(사망 0)", $"사망 {probe.Deaths} · 체력 채움 {probe.Heals}회");
            ctx.Eye("57.2 산출물", $"{dir} (세그먼트 {probe.Segments}개, 프레임 {probe.Rows}행)");
        }
        finally
        {
            probe.End();
        }
    }
}

/// <summary>
/// 할 일 57.2 관찰자(QA 전용) — 매 프레임 LateUpdate(가장 늦게)에서 프레임 시간을 모으고, 보스 예고가 켜진 프레임을 찾아 빈 마커를 찍는다.
/// 보스 코드는 바꾸지 않는다 — 비공개 필드를 리플렉션으로 읽기만 한다(예고 켜짐 <c>BossAttackConeTelegraph._active</c>,
/// 드론 단계 <c>WellsDroneAttack._view</c>, 점프 예고 <c>TwentyThreeBoss._c3JumpTelegraph</c>, 현재 공격 <c>_currentEntry.attackId</c>).
/// Profiler 메타데이터(프레임마다 frameCount·단계)를 같이 남겨 분석기가 CSV 와 .raw 프레임을 맞춘다.
/// </summary>
[DefaultExecutionOrder(32000)]
public sealed class QABossProfileProbe : MonoBehaviour
{
    public enum Phase { Lobby = 0, Loading = 1, Map = 2, BossIntro = 3, Combat = 4, Done = 5 }

    /// <summary>프레임 메타데이터 키 — 분석기(QABossProfileAnalyzer — 삭제됨(155))와 같았다. data = { frameCount, phase }.</summary>
    public static readonly Guid MetaGuid = new Guid("5b7a2c57-0a72-4f1e-9a57-2c0de57a2b01");
    public const int MetaTag = 572;
    public const int SegmentFrames = 1000;

    private static readonly ProfilerMarker ConeShowMarker = new ProfilerMarker("QA57.ConeShow");
    private static readonly ProfilerMarker DroneMarkMarker = new ProfilerMarker("QA57.DroneMark");
    private static readonly ProfilerMarker DroneLockMarker = new ProfilerMarker("QA57.DroneLock");
    private static readonly ProfilerMarker JumpShowMarker = new ProfilerMarker("QA57.JumpShow");
    private static readonly ProfilerMarker SegmentMarker = new ProfilerMarker("QA57.Segment");

    private static readonly FieldInfo ConeActive = typeof(BossAttackConeTelegraph).GetField("_active", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo DroneView = typeof(WellsDroneAttack).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo JumpTelegraph = typeof(TwentyThreeBoss).GetField("_c3JumpTelegraph", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo CurrentEntry = typeof(TwentyThreeBoss).GetField("_currentEntry", BindingFlags.Instance | BindingFlags.NonPublic);

    public readonly List<string> PrewarmLogs = new List<string>();
    public int FirstCone = -1, FirstDash = -1, FirstDroneMark = -1, FirstDroneLock = -1, FirstJump = -1;
    public string FirstConeAttack = "";
    public int Heals, Deaths, Segments, Rows;

    private string _dir;
    private Phase _phase = Phase.Lobby;
    private bool _profiling;
    private int _segFrames;
    private readonly int[] _meta = new int[2];
    private StreamWriter _frames, _events;
    private ProfilerRecorder _mainThread, _playerLoop, _gcAlloc;
    private float _t0;

    private TwentyThreeBoss _boss;
    private BossAttackConeTelegraph _cone;
    private WellsDroneAttack _drone;
    private bool _coneWas, _jumpWas;
    private int _droneWas;
    private bool _wasDead;

    /// <summary>산출물 루트 = 레포 폴더 옆 _lane-artifacts/&lt;레포 폴더 이름&gt;/57.2-profile/run-시각.</summary>
    public static string NewRunDir()
    {
        string repo = Path.GetDirectoryName(Application.dataPath);
        string root = Path.Combine(Path.GetDirectoryName(repo), "_lane-artifacts", Path.GetFileName(repo), "57.2-profile");
        string dir = Path.Combine(root, "run-" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void Begin(string dir)
    {
        _dir = dir;
        _t0 = Time.realtimeSinceStartup;
        _frames = new StreamWriter(Path.Combine(dir, "frames.csv"), false, new UTF8Encoding(false));
        // 레코더 값은 직전 프레임 것이다 — 행의 frame = Time.frameCount - 1.
        _frames.WriteLine("frame,t_s,phase,dt_ms,main_thread_ms,player_loop_ms,gc_alloc_bytes,profiling");
        _events = new StreamWriter(Path.Combine(dir, "events.csv"), false, new UTF8Encoding(false));
        _events.WriteLine("frame,t_s,phase,kind,detail");
        _mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
        _playerLoop = StartByName("PlayerLoop");
        _gcAlloc = StartByName("GC Allocated In Frame");
        Application.logMessageReceived += HandleLog;
        Event("begin", $"recorders main={_mainThread.Valid} playerLoop={_playerLoop.Valid} gc={_gcAlloc.Valid} reflection cone={ConeActive != null} drone={DroneView != null} jump={JumpTelegraph != null} entry={CurrentEntry != null}");
    }

    private static ProfilerRecorder StartByName(string name)
    {
        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);
        foreach (ProfilerRecorderHandle h in handles)
        {
            ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(h);
            if (d.Name == name)
                return ProfilerRecorder.StartNew(d.Category, d.Name, 1);
        }
        return default;
    }

    public void SetPhase(Phase p)
    {
        _phase = p;
        Event("phase", p.ToString());
    }

    public void StartProfiler() => OpenSegment();

    public void StopProfiler()
    {
        if (!_profiling)
            return;
        _profiling = false;
        Profiler.enabled = false;
        Profiler.enableBinaryLog = false;
        Profiler.logFile = "";
        Event("profiler", "stop");
    }

    private void OpenSegment()
    {
        // 파일을 바꾸려면 껐다 켠다. 🔴 logFile 을 쓰면 enableBinaryLog 가 꺼진다(57.2 1회차 시험 실행에서 .raw 0개) — 매번 그 뒤에 다시 켠다.
        Profiler.enabled = false;
        Profiler.logFile = Path.Combine(_dir, $"seg-{Segments:000}");
        Profiler.enableBinaryLog = true;
        Profiler.enabled = true;
        _profiling = true;
        _segFrames = 0;
        Event("segment", $"seg-{Segments:000}.raw");
        Segments++;
        using (SegmentMarker.Auto()) { }
    }

    public void End()
    {
        StopProfiler();
        _phase = Phase.Done;
        Application.logMessageReceived -= HandleLog;
        Event("end", FirstsSummary());
        _frames?.Dispose();
        _events?.Dispose();
        _frames = _events = null;
        _mainThread.Dispose();
        _playerLoop.Dispose();
        _gcAlloc.Dispose();
        if (this != null)
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (_frames != null)
            End();
    }

    private void HandleLog(string message, string stack, LogType type)
    {
        if (message != null && message.StartsWith("[Enemy C3] Prewarm"))
        {
            PrewarmLogs.Add(message);
            Event("prewarm", message.Replace(',', ';'));
        }
    }

    private void Event(string kind, string detail)
    {
        _events?.WriteLine($"{Time.frameCount},{Time.realtimeSinceStartup - _t0:F3},{_phase},{kind},{(detail ?? "").Replace(',', ';')}");
        _events?.Flush();
    }

    /// <summary>아직 못 본 23호 패턴을 기존 Dev 예약으로 앞당긴다(돌진 먼저).</summary>
    public void ReserveMissing()
    {
        TwentyThreeBoss boss = Boss();
        if (boss == null)
            return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
        // 23호 단일 공격 Dev 예약(DevReserveNextAttack)은 development f0b014b7 에서 빠졌다 — 점프 콤보 예약만 남음. 돌진은 자연 발생을 기다린다.
        if (FirstJump < 0)
            Event("reserve", $"JumpCombo={boss.DevReserveJumpCombo()}");
#endif
    }

    public string FirstsSummary() =>
        $"첫 공격 예고 f{FirstCone}({FirstConeAttack}) · 첫 돌진 f{FirstDash} · 첫 드론 표식 f{FirstDroneMark} · 첫 드론 고정 f{FirstDroneLock} · 첫 점프 f{FirstJump} (-1 = 미관측)";

    private TwentyThreeBoss Boss()
    {
        if (_boss == null)
            _boss = FindAnyObjectByType<TwentyThreeBoss>();
        return _boss;
    }

    private void LateUpdate()
    {
        if (_frames == null)
            return;

        int frame = Time.frameCount;
        _meta[0] = frame;
        _meta[1] = (int)_phase;
        if (_profiling)
            Profiler.EmitFrameMetaData(MetaGuid, MetaTag, _meta);

        _frames.WriteLine($"{frame - 1},{Time.realtimeSinceStartup - _t0:F3},{(int)_phase},{Time.unscaledDeltaTime * 1000f:F3}," +
                          $"{Ms(_mainThread)},{Ms(_playerLoop)},{(_gcAlloc.Valid ? _gcAlloc.LastValue : -1)},{(_profiling ? 1 : 0)}");
        Rows++;

        if (_phase >= Phase.BossIntro && _phase < Phase.Done)
            Observe(frame);

        if (_profiling && ++_segFrames >= SegmentFrames)
            OpenSegment();
    }

    private static string Ms(ProfilerRecorder r) => r.Valid ? (r.LastValue / 1e6).ToString("F3") : "";

    private void Observe(int frame)
    {
        TwentyThreeBoss boss = Boss();
        if (boss == null)
            return;

        if (_cone == null)
            _cone = boss.GetComponent<BossAttackConeTelegraph>();
        if (_cone != null && ConeActive != null)
        {
            bool on = (bool)ConeActive.GetValue(_cone);
            if (on && !_coneWas)
            {
                string attack = AttackId(boss);
                using (ConeShowMarker.Auto()) { }
                Event("ConeShow", attack);
                if (FirstCone < 0) { FirstCone = frame; FirstConeAttack = attack; }
                if (FirstDash < 0 && (attack == nameof(BossAttackId.Dash) || attack == "RageDash")) FirstDash = frame;
            }
            _coneWas = on;
        }

        if (_drone == null)
            _drone = boss.GetComponent<WellsDroneAttack>();
        if (_drone != null && DroneView != null)
        {
            int view = Convert.ToInt32(DroneView.GetValue(_drone)); // None 0 · Mark 1 · Lock 2
            if (view != _droneWas)
            {
                if (view == 1) { using (DroneMarkMarker.Auto()) { } Event("DroneMark", ""); if (FirstDroneMark < 0) FirstDroneMark = frame; }
                if (view == 2) { using (DroneLockMarker.Auto()) { } Event("DroneLock", ""); if (FirstDroneLock < 0) FirstDroneLock = frame; }
            }
            _droneWas = view;
        }

        if (JumpTelegraph != null)
        {
            bool jump = (UnityEngine.Object)JumpTelegraph.GetValue(boss) != null;
            if (jump && !_jumpWas)
            {
                using (JumpShowMarker.Auto()) { }
                Event("JumpShow", AttackId(boss));
                if (FirstJump < 0) FirstJump = frame;
            }
            _jumpWas = jump;
        }

        KeepAlive();
    }

    private static string AttackId(TwentyThreeBoss boss)
    {
        var e = CurrentEntry != null ? CurrentEntry.GetValue(boss) as BossAttackEntry : null;
        return e != null ? e.attackId.ToString() : "(없음)";
    }

    // 판이 끝나지 않게 — 70% 아래로 떨어지면 최대 체력으로(재합류 복원용 SetCurrentHealthOnServer: 통계·피격 연출·사망 신호 없음).
    private void KeepAlive()
    {
        Player p = Player.LocalPlayer;
        if (p == null || !p.IsServer)
            return;
        PlayerLifeCycleController life = p.GetComponent<PlayerLifeCycleController>();
        bool dead = life != null && life.State != PlayerLifeState.Alive;
        if (dead && !_wasDead)
        {
            Deaths++;
            Event("death", life.State.ToString());
        }
        _wasDead = dead;
        if (!dead && p.CurrentHealth < p.MaxHp * 0.7f)
        {
            p.SetCurrentHealthOnServer(p.MaxHp);
            Heals++;
        }
    }
}
