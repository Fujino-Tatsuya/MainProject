// ZonePerfRecorder.cs
// 존별 실시간 성능 기록기 — 로컬 플레이어가 서 있는 존마다 프레임 비용을 쌓아 CSV 로 남긴다(PLAN-cleanup-optimization §S6).
//
// 왜 존별인가: 존은 맵 시작 때 전부 생성되고 계속 켜져 있다(MapContentSpawner). 그래서 위치마다 비용이 다른 이유는
//   "그 자리에서 카메라에 보이는 콘텐츠" 다. 이 숫자는 콘텐츠 수정(그림자 캐스터·메시 병합)의 근거로 쓴다.
//   🔴 존 진입·이탈 때 렌더 설정을 바꾸는 용도가 아니다(§S6 결정 — 경계 렉·그림자 튐·에디터 에셋 오염).
//
// 키
//   Home   = CSV 저장 + 콘솔 요약          (F1~F12 는 전부 다른 디버그 키가 쓰고 있다)
//   End    = 프레임 상한 해제 ↔ 원래 상한 (바꾸면 누적 초기화 — 섞이면 비교가 안 된다)
//   Insert = 누적 초기화
//   F8     = 화면 한 줄 표시 토글(ProfilerHUD 와 같은 키)
//
// 숫자의 의미
//   Frame  = Time.unscaledDeltaTime — 실제 프레임 간격(벽시계). 예산 초과 판정은 이 값으로 한다.
//   Main   = FrameTiming 메인 스레드 작업 시간(Present 대기 제외)
//   Render = FrameTiming 렌더 스레드 시간
//   GPU    = FrameTiming GPU 시간
//   FrameTiming 값은 몇 프레임 늦게 채워진다 — 존 경계에서 한두 프레임이 옆 존으로 들어가는 정도는 무시한다.
//   한 번도 0 이 아닌 값이 안 나온 항목은 CSV·화면에 N/A 로 쓴다(0 으로 찍으면 거짓 신호).
//
// 측정 기본값 = 프레임 상한 해제(measureUncapped). 상한(144)이 걸려 있으면 대기 시간이 Frame 에 섞인다.
//
// 릴리스 빌드: 클래스는 남고 본문만 빠진다(씬에 붙어 있어도 Missing Script 경고가 안 나게).

using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Netcode;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;   // ProfilerRecorderHandle·GetAvailable — 이름과 달리 unsafe 코드는 필요 없다
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#endif

[DisallowMultipleComponent]
public sealed class ZonePerfRecorder : MonoBehaviour
{
    // 릴리스 빌드에선 본문이 빠져 아래 필드를 아무도 안 읽는다(CS0414). 직렬화 형태를 빌드마다 같게 두려고 필드는 남긴다.
#pragma warning disable 0414
    [Header("예산")]
    [Tooltip("측정 예산 fps. 이 PC(i9-13900KF · RTX 4060 Ti) 개발 빌드·상한 해제 기준 200(팀장 10-06).")]
    [SerializeField] int budgetFps = 200;

    [Header("측정 조건")]
    [Tooltip("시작할 때 프레임 상한을 푼다. 상한이 걸려 있으면 대기 시간이 Frame 값에 섞인다.")]
    [SerializeField] bool measureUncapped = true;
    [Tooltip("시작·초기화 직후 이 시간(초)은 버린다 — 씬 로드·셰이더 첫 컴파일 렉.")]
    [SerializeField] float warmupSeconds = 3f;
    [Tooltip("화면 아래 왼쪽에 현재 존 한 줄을 띄운다.")]
    [SerializeField] bool showOverlay = true;
#pragma warning restore 0414

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 144 상한(FrameRateCap.DefaultTarget)에서 실제로 프레임이 떨어지는 경계. 예산과 별개로 같이 센다.
    const float kCapMs = 1000f / FrameRateCap.DefaultTarget;
    const float kHitchMs = 33.3f;           // 30fps 미만 = 체감 끊김
    const float kBinMs = 0.25f;             // p95 히스토그램 칸
    const int kBins = 200;                  // 0 ~ 50ms, 넘치면 마지막 칸
    const float kZoneRescan = 2f;           // 존 목록 재탐색 주기(존이 다 잡히면 멈춘다)

    const string kOutside = "복도/기타";
    // 생성 존(GeneratedZoneIdentity)이 하나도 없을 때 — 시작 방·튜토리얼 같은 고정 구역에 있거나 존 생성 전.
    const string kNoMap = "고정 구역(생성 존 없음)";

    // 지표 — 앞 4개는 ms(히스토그램 있음), 뒤는 개수/KB.
    enum M { Frame, Main, Render, Gpu, Draws, SetPass, Tris, Shadow, GcKB, Count }
    static readonly string[] kMetricNames = { "frame_ms", "main_ms", "render_ms", "gpu_ms", "draws", "setpass", "tris", "shadow_casters", "gc_kb" };
    const int kTimed = 4;
    const int kMetrics = (int)M.Count;

    sealed class ZoneStats
    {
        public string Key;
        public readonly SortedSet<int> Slots = new SortedSet<int>();
        public long Frames;
        public double Seconds;
        public long OverBudget, OverCap, Hitches;
        public readonly double[] Sum = new double[kMetrics];
        public readonly double[] Max = new double[kMetrics];
        public readonly long[] Samples = new long[kMetrics];      // 값이 들어온 프레임 수(N/A 판정·평균 분모)
        public readonly int[][] Hist = new int[kTimed][];
        // 패스별 GPU ms 누적 — 인덱스 = _passes 순서. 패스는 실행 중에 늘어나므로 EnsurePasses 로 늘린다.
        public double[] PassSum = new double[0];
        public double[] PassMax = new double[0];
        public long[] PassN = new long[0];
        public void EnsurePasses(int n)
        {
            if (PassSum.Length >= n) return;
            Array.Resize(ref PassSum, n); Array.Resize(ref PassMax, n); Array.Resize(ref PassN, n);
        }
        public ZoneStats(string key)
        {
            Key = key;
            for (int i = 0; i < kTimed; i++) Hist[i] = new int[kBins];
        }
    }

    struct ZoneArea
    {
        public GeneratedZoneIdentity Id;
        public string Key;
        public int Slot;
        public float MinX, MaxX, MinZ, MaxZ, Area;
    }

    readonly Dictionary<string, ZoneStats> _stats = new Dictionary<string, ZoneStats>();
    readonly List<ZoneArea> _zones = new List<ZoneArea>();
    float _nextRescan;
    int _lastScanCount = -1;
    bool _zonesSettled;

    ProfilerRecorder _draws, _setPass, _tris, _shadow, _gc;

    // ───────── 패스별 GPU 시간 ─────────
    // GPU 샘플링이 되는 마커(MarkerFlags.SampleGPU — URP·RenderGraph 패스, 우리 렌더러 피처)를 자동으로 찾아
    // GpuRecorder 로 잰다. 마커는 패스가 처음 실행될 때 생기므로 주기적으로 다시 찾는다(찾기는 할당이 있어 드물게).
    // 4K 에서 GPU 병목(§S6 빌드 측정)일 때 "어느 패스가 먹는가" 를 존별로 답하려고 넣었다.
    sealed class PassRec { public string Name; public ProfilerRecorder R; }
    readonly List<PassRec> _passes = new List<PassRec>();
    readonly HashSet<string> _passNames = new HashSet<string>();
    readonly List<ProfilerRecorderHandle> _handles = new List<ProfilerRecorderHandle>();
    float _nextPassScan;
    int _passScans;
    readonly FrameTiming[] _ft = new FrameTiming[1];

    Transform _player;
    float _warmupLeft;
    float _sessionSeconds;
    ZoneStats _current;
    int _currentSlot = -1;

    int _prevTargetFrameRate;
    int _prevVSync;
    bool _uncapped;

    string _seed = "미확인";

    // 🔴 정식 흐름(로비 → 로딩 → 맵)에선 MapScene 이 로딩 씬 위에 Additive 로 올라오고, 로딩 씬은 페이드·최소 표시 시간이
    //    끝난 뒤에야 내려간다. 그 사이 프레임이 "시작 존" 으로 섞여 빌드 1차 측정에서 시작 존이 13ms 로 나왔다(10-06).
    //    Dev Boot 는 로딩 씬을 안 거쳐 재현되지 않는다 → 로딩 씬이 떠 있는 동안은 별도 키로 뺀다.
    const string kLoadingSceneName = "2.LoadingScene";
    const string kLoading = "로딩 화면 중(2.LoadingScene)";
    bool _loadingSceneLoaded;

    // 1초 단위 타임라인 — 존 평균이 숨기는 "언제" 를 본다(시작 직후 몇 초만 무거운지, 계속 무거운지).
    struct TimelineRow { public float T; public string Zone; public int Frames, FtN; public double Frame, FrameMax, Main, Render, Gpu, Draws; }
    readonly List<TimelineRow> _timeline = new List<TimelineRow>(1024);
    TimelineRow _tl;
    float _tlStart;
    readonly StringBuilder _sb = new StringBuilder(256);
    readonly GUIContent _overlay = new GUIContent();
    float _overlayTimer;
    GUIStyle _style;
    Texture2D _bg;

    float BudgetMs => 1000f / Mathf.Max(1, budgetFps);

    void OnEnable()
    {
        _draws   = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        _tris    = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        _shadow  = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
        _gc      = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");

        _prevTargetFrameRate = Application.targetFrameRate;
        _prevVSync = QualitySettings.vSyncCount;
        if (measureUncapped) SetUncapped(true);

        // 시드는 MapGenerator 가 로그로만 남긴다 — 남의 코드를 고치지 않으려고 로그를 읽는다.
        Application.logMessageReceived += OnLog;
        _loadingSceneLoaded = SceneManager.GetSceneByName(kLoadingSceneName).isLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        ResetAll();
    }

    // 🔴 저장은 여기서 한다. OnApplicationQuit 만 두면 **맵 → 결과 화면 전환으로 MapScene 이 내려갈 때**
    //    측정기도 같이 꺼지면서 데이터가 사라진다(10-06 빌드 첫 측정을 이렇게 잃었다). OnDisable 은 씬 언로드·게임 종료 둘 다 불린다.
    //    상한을 복구하기 전에 저장해야 CSV 머리의 "상한" 칸이 측정 당시 상태로 남는다.
    void OnDisable()
    {
        if (TotalFrames() > 0) SaveCsv("자동 저장(맵 종료·게임 종료)");
        Application.logMessageReceived -= OnLog;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        if (_uncapped) SetUncapped(false);
        _draws.Dispose(); _setPass.Dispose(); _tris.Dispose(); _shadow.Dispose(); _gc.Dispose();
        for (int i = 0; i < _passes.Count; i++) _passes[i].R.Dispose();
        _passes.Clear(); _passNames.Clear();
        if (_bg != null) Destroy(_bg);
    }

    void OnSceneLoaded(Scene s, LoadSceneMode mode)
    {
        if (s.name == kLoadingSceneName) _loadingSceneLoaded = true;
    }

    void OnSceneUnloaded(Scene s)
    {
        if (s.name == kLoadingSceneName) _loadingSceneLoaded = false;
    }

    void OnLog(string msg, string stack, LogType type)
    {
        const string head = "[MapGenerator] 생성 완료. Seed:";
        if (!msg.StartsWith(head, StringComparison.Ordinal)) return;
        int end = msg.IndexOf(' ', head.Length);
        _seed = end > head.Length ? msg.Substring(head.Length, end - head.Length) : msg.Substring(head.Length);
    }

    // ───────── 매 프레임 ─────────
    void Update()
    {
        HandleKeys();

        float dt = Time.unscaledDeltaTime;
        if (_warmupLeft > 0f) { _warmupLeft -= dt; return; }

        RefreshZonesIfNeeded();
        ResolvePlayer();
        ResolveZone();

        FrameTimingManager.CaptureFrameTimings();
        bool hasFt = FrameTimingManager.GetLatestTimings(1, _ft) > 0;

        var s = _current;
        float frameMs = dt * 1000f;
        s.Frames++;
        s.Seconds += dt;
        _sessionSeconds += dt;
        if (frameMs > BudgetMs) s.OverBudget++;
        if (frameMs > kCapMs) s.OverCap++;
        if (frameMs > kHitchMs) s.Hitches++;
        if (_currentSlot >= 0) s.Slots.Add(_currentSlot);

        Add(s, M.Frame, frameMs);
        if (hasFt)
        {
            var f = _ft[0];
            Add(s, M.Main, f.cpuMainThreadFrameTime - f.cpuMainThreadPresentWaitTime);
            Add(s, M.Render, f.cpuRenderThreadFrameTime);
            Add(s, M.Gpu, f.gpuFrameTime);
        }
        AddRec(s, M.Draws, _draws, 1.0);
        AddRec(s, M.SetPass, _setPass, 1.0);
        AddRec(s, M.Tris, _tris, 1.0);
        AddRec(s, M.Shadow, _shadow, 1.0);
        AddRec(s, M.GcKB, _gc, 1.0 / 1024.0);

        // 타임라인 1초 칸 — 칸 중간에 존이 바뀌면 칸을 끊는다(한 칸 = 한 존).
        if (_tl.Frames > 0 && (_sessionSeconds - _tlStart >= 1f || !ReferenceEquals(_tl.Zone, s.Key)))
        {
            _timeline.Add(_tl);
            _tl = default;
        }
        if (_tl.Frames == 0) { _tl.T = _sessionSeconds; _tl.Zone = s.Key; _tlStart = _sessionSeconds; }
        _tl.Frames++;
        _tl.Frame += frameMs;
        if (frameMs > _tl.FrameMax) _tl.FrameMax = frameMs;
        if (hasFt)
        {
            var f = _ft[0];
            _tl.FtN++;
            double main = f.cpuMainThreadFrameTime - f.cpuMainThreadPresentWaitTime;
            if (main > 0 && main < 10000) _tl.Main += main;
            if (f.cpuRenderThreadFrameTime > 0 && f.cpuRenderThreadFrameTime < 10000) _tl.Render += f.cpuRenderThreadFrameTime;
            if (f.gpuFrameTime > 0 && f.gpuFrameTime < 10000) _tl.Gpu += f.gpuFrameTime;
        }
        if (_draws.Valid) _tl.Draws += _draws.LastValue;

        ScanPassesIfNeeded();
        s.EnsurePasses(_passes.Count);
        for (int i = 0; i < _passes.Count; i++)
        {
            var r = _passes[i].R;
            if (!r.Valid) continue;
            double ms = r.LastValue * 1e-6;   // ns
            if (!(ms > 0.0) || ms > 1000.0) continue;
            s.PassSum[i] += ms;
            s.PassN[i]++;
            if (ms > s.PassMax[i]) s.PassMax[i] = ms;
        }

        _overlayTimer -= dt;
        if (_overlayTimer <= 0f) { _overlayTimer = 0.25f; RebuildOverlay(); }
    }

    // 0 은 "값 없음" 으로 본다(FrameTiming 미지원·카운터 미지원이 0 으로 온다). 드로우 0 인 프레임은 실제로 없다.
    // 🔴 FrameTiming 은 NaN/무한대도 준다(에디터 첫 Play 에서 gpuFrameTime 확인). NaN 은 `v <= 0` 을 통과하고
    //    (int)NaN = int.MinValue 라 히스토그램 인덱스가 음수가 된다 → 유한하지 않거나 10초를 넘는 시간 값은 버린다.
    static void Add(ZoneStats s, M m, double v)
    {
        if (!(v > 0.0) || double.IsInfinity(v)) return;
        if ((int)m < kTimed && v > 10000.0) return;
        int i = (int)m;
        s.Sum[i] += v;
        s.Samples[i]++;
        if (v > s.Max[i]) s.Max[i] = v;
        if (i < kTimed)
        {
            int b = (int)(v / kBinMs);
            s.Hist[i][b < kBins ? b : kBins - 1]++;
        }
    }

    static void AddRec(ZoneStats s, M m, ProfilerRecorder r, double scale)
    {
        if (!r.Valid) return;
        // GC 0 은 정상값이라 따로 센다(평균 분모에 넣어야 한다).
        if (m == M.GcKB)
        {
            int i = (int)m;
            double v = r.LastValue * scale;
            s.Sum[i] += v; s.Samples[i]++;
            if (v > s.Max[i]) s.Max[i] = v;
            return;
        }
        Add(s, m, r.LastValue * scale);
    }

    void ScanPassesIfNeeded()
    {
        if (Time.unscaledTime < _nextPassScan) return;
        // 처음 10번은 3초마다(패스가 차례로 처음 실행된다), 그 뒤 20초마다(드물게 켜지는 연출 패스용).
        _nextPassScan = Time.unscaledTime + (_passScans < 10 ? 3f : 20f);
        _passScans++;

        _handles.Clear();
        ProfilerRecorderHandle.GetAvailable(_handles);
        for (int i = 0; i < _handles.Count; i++)
        {
            var d = ProfilerRecorderHandle.GetDescription(_handles[i]);
            if ((d.Flags & MarkerFlags.SampleGPU) == 0) continue;
            if (!_passNames.Add(d.Name)) continue;
            var r = new ProfilerRecorder(_handles[i], 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.GpuRecorder);
            _passes.Add(new PassRec { Name = d.Name, R = r });
        }
    }

    // ───────── 존 판정 ─────────
    void RefreshZonesIfNeeded()
    {
        // 존이 하나라도 파괴됐으면(씬 재로드) 다시 찾는다.
        if (_zonesSettled)
        {
            for (int i = 0; i < _zones.Count; i++)
                if (_zones[i].Id == null) { _zonesSettled = false; _lastScanCount = -1; break; }
            if (_zonesSettled) return;
        }
        if (Time.unscaledTime < _nextRescan) return;
        _nextRescan = Time.unscaledTime + kZoneRescan;

        // FindObjectsByType 는 할당한다 — 존이 다 잡히면(두 번 연속 같은 개수) 멈춰서 GC 지표를 오염시키지 않는다.
        var ids = FindObjectsByType<GeneratedZoneIdentity>(FindObjectsSortMode.None);
        _zones.Clear();
        for (int i = 0; i < ids.Length; i++)
        {
            if (TryBuildArea(ids[i], out var a)) _zones.Add(a);
        }
        if (ids.Length > 0 && ids.Length == _lastScanCount) _zonesSettled = true;
        _lastScanCount = ids.Length;
    }

    static bool TryBuildArea(GeneratedZoneIdentity id, out ZoneArea a)
    {
        a = default;
        var rs = id.GetComponentsInChildren<Renderer>(false);
        bool any = false;
        Bounds b = default;
        for (int i = 0; i < rs.Length; i++)
        {
            if (rs[i] is ParticleSystemRenderer || rs[i] is TrailRenderer || rs[i] is LineRenderer) continue;
            if (!any) { b = rs[i].bounds; any = true; }
            else b.Encapsulate(rs[i].bounds);
        }
        if (!any) return false;

        string key = id.SourcePrefab != null ? id.SourcePrefab.name : id.gameObject.name.Replace("(Clone)", "").Trim();
        if (id.GetComponentInChildren<BossEnterTrigger>(true) != null) key += " [BossRoom]";

        a.Id = id;
        a.Key = key;
        a.Slot = id.SlotID;
        a.MinX = b.min.x; a.MaxX = b.max.x;
        a.MinZ = b.min.z; a.MaxZ = b.max.z;
        a.Area = (a.MaxX - a.MinX) * (a.MaxZ - a.MinZ);
        return true;
    }

    void ResolvePlayer()
    {
        if (_player != null) return;
        var nm = NetworkManager.Singleton;
        var po = nm != null && nm.IsListening ? nm.LocalClient?.PlayerObject : null;
        if (po != null) _player = po.transform;
    }

    void ResolveZone()
    {
        string key;
        int slot = -1;
        if (_loadingSceneLoaded)
        {
            key = kLoading;
        }
        else if (_zones.Count == 0)
        {
            key = kNoMap;
        }
        else
        {
            Vector3 p;
            if (_player != null) p = _player.position;
            else { var cam = Camera.main; p = cam != null ? cam.transform.position : Vector3.zero; }

            // 범위가 겹치면(복도 연결부) 가장 작은 존을 고른다.
            int best = -1;
            float bestArea = float.MaxValue;
            for (int i = 0; i < _zones.Count; i++)
            {
                var z = _zones[i];
                if (p.x < z.MinX || p.x > z.MaxX || p.z < z.MinZ || p.z > z.MaxZ) continue;
                if (z.Area < bestArea) { bestArea = z.Area; best = i; }
            }
            if (best >= 0) { key = _zones[best].Key; slot = _zones[best].Slot; }
            else key = kOutside;
        }

        _currentSlot = slot;
        if (_current != null && ReferenceEquals(_current.Key, key)) return;
        if (!_stats.TryGetValue(key, out _current))
        {
            _current = new ZoneStats(key);
            _stats.Add(key, _current);
        }
    }

    // ───────── 키 ─────────
    void HandleKeys()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.homeKey.wasPressedThisFrame) SaveCsv("Home 키");
        if (kb.endKey.wasPressedThisFrame) { SetUncapped(!_uncapped); ResetAll(); }
        if (kb.insertKey.wasPressedThisFrame) ResetAll();
        if (kb.f8Key.wasPressedThisFrame) showOverlay = !showOverlay;
#else
        if (Input.GetKeyDown(KeyCode.Home)) SaveCsv("Home 키");
        if (Input.GetKeyDown(KeyCode.End)) { SetUncapped(!_uncapped); ResetAll(); }
        if (Input.GetKeyDown(KeyCode.Insert)) ResetAll();
        if (Input.GetKeyDown(KeyCode.F8)) showOverlay = !showOverlay;
#endif
    }

    void SetUncapped(bool on)
    {
        _uncapped = on;
        if (on)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }
        else
        {
            QualitySettings.vSyncCount = _prevVSync;
            Application.targetFrameRate = _prevTargetFrameRate;
        }
        Debug.Log($"[ZonePerf] 프레임 상한 {(on ? "해제" : $"복구({_prevTargetFrameRate})")} — 누적 초기화");
    }

    void ResetAll()
    {
        _stats.Clear();
        _timeline.Clear();
        _tl = default;
        _current = null;
        _sessionSeconds = 0f;
        _warmupLeft = warmupSeconds;
        _overlay.text = "ZonePerf 워밍업…";
    }

    long TotalFrames()
    {
        long n = 0;
        foreach (var s in _stats.Values) n += s.Frames;
        return n;
    }

    // ───────── 출력 ─────────
    static double Avg(ZoneStats s, M m) => s.Samples[(int)m] > 0 ? s.Sum[(int)m] / s.Samples[(int)m] : double.NaN;

    static double P95(ZoneStats s, M m)
    {
        int i = (int)m;
        long n = s.Samples[i];
        if (n == 0) return double.NaN;
        long target = (long)Math.Ceiling(n * 0.95);
        long acc = 0;
        var h = s.Hist[i];
        for (int b = 0; b < kBins; b++)
        {
            acc += h[b];
            if (acc >= target) return (b + 1) * kBinMs;   // 칸 상한 — 보수적으로
        }
        return kBins * kBinMs;
    }

    static string F(double v, string fmt = "0.00") =>
        double.IsNaN(v) ? "N/A" : v.ToString(fmt, CultureInfo.InvariantCulture);

    static double Pct(long part, long whole) => whole > 0 ? 100.0 * part / whole : double.NaN;

    void RebuildOverlay()
    {
        var s = _current;
        if (s == null || s.Frames == 0) return;
        _sb.Clear();
        _sb.Append("존 ").Append(s.Key).Append("  ").Append(F(s.Seconds, "0")).Append("초")
           .Append(_uncapped ? "  [상한 해제]" : "  [상한 켜짐]")
           .Append('\n')
           .Append("Frame ").Append(F(Avg(s, M.Frame))).Append(" (p95 ").Append(F(P95(s, M.Frame))).Append(')')
           .Append("  Render ").Append(F(Avg(s, M.Render)))
           .Append("  GPU ").Append(F(Avg(s, M.Gpu)))
           .Append("  그림자 ").Append(F(Avg(s, M.Shadow), "0"))
           .Append('\n')
           .Append(budgetFps).Append("fps 초과 ").Append(F(Pct(s.OverBudget, s.Frames), "0.0")).Append('%')
           .Append("  144 초과 ").Append(F(Pct(s.OverCap, s.Frames), "0.0")).Append('%')
           .Append("   Home=저장 End=상한 Ins=초기화");
        _overlay.text = _sb.ToString();
    }

    void SaveCsv(string reason)
    {
        if (TotalFrames() == 0) { Debug.Log("[ZonePerf] 저장할 프레임이 없다(워밍업 중이거나 방금 초기화)."); return; }

#if UNITY_EDITOR
        string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "ZonePerf");
#else
        string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "ZonePerf");
#endif
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"zone-perf-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        var csv = new StringBuilder(4096);
        csv.Append("# 존별 성능 ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(" · ").Append(reason).Append('\n');
        csv.Append("# 환경 ").Append(Application.isEditor ? "에디터" : "개발 빌드")
           .Append(" · ").Append(SystemInfo.processorType).Append(" · ").Append(SystemInfo.graphicsDeviceName)
           .Append(" · ").Append(SystemInfo.graphicsDeviceType)
           .Append(" · 해상도 ").Append(Screen.width).Append('x').Append(Screen.height)
           .Append(" · GPU 레코더 지원 ").Append(SystemInfo.supportsGpuRecorder).Append('\n');
        csv.Append("# 조건 예산 ").Append(budgetFps).Append("fps(").Append(F(BudgetMs)).Append("ms)")
           .Append(" · 상한 ").Append(_uncapped ? "해제" : Application.targetFrameRate.ToString())
           .Append(" · vSync ").Append(QualitySettings.vSyncCount)
           .Append(" · 시드 ").Append(_seed)
           .Append(" · 측정 ").Append(F(_sessionSeconds, "0")).Append("초");
        if (urp != null)
        {
            csv.Append(" · GRD ").Append(urp.gpuResidentDrawerMode)
               .Append(" · 그림자 ").Append(F(urp.shadowDistance, "0")).Append("m/").Append(urp.shadowCascadeCount).Append("캐스케이드")
               .Append(" · 렌더스케일 ").Append(F(urp.renderScale));
        }
        csv.Append('\n');

        csv.Append("zone,slots,seconds,frames,over_").Append(budgetFps).Append("fps_pct,over_144_pct,hitches");
        for (int m = 0; m < kMetrics; m++)
        {
            csv.Append(',').Append(kMetricNames[m]).Append("_avg");
            if (m < kTimed) csv.Append(',').Append(kMetricNames[m]).Append("_p95");
            csv.Append(',').Append(kMetricNames[m]).Append("_max");
        }
        csv.Append('\n');

        // 무거운 존부터(Frame 평균 내림차순) — 표를 열면 바로 고칠 대상이 위에 온다.
        var rows = new List<ZoneStats>(_stats.Values);
        rows.Sort((a, b) => Nz(Avg(b, M.Frame)).CompareTo(Nz(Avg(a, M.Frame))));

        var log = new StringBuilder(1024);
        log.Append("[ZonePerf] 저장 ").Append(path).Append('\n')
           .Append("존 | 초 | Frame avg/p95 | Render | GPU | 드로우 | 그림자 | ").Append(budgetFps).Append("fps 초과%\n");

        foreach (var s in rows)
        {
            csv.Append('"').Append(s.Key).Append('"').Append(',')
               .Append('"').Append(string.Join(" ", s.Slots)).Append('"').Append(',')
               .Append(F(s.Seconds, "0.0")).Append(',').Append(s.Frames).Append(',')
               .Append(F(Pct(s.OverBudget, s.Frames), "0.0")).Append(',')
               .Append(F(Pct(s.OverCap, s.Frames), "0.0")).Append(',')
               .Append(s.Hitches);
            for (int m = 0; m < kMetrics; m++)
            {
                string fmt = m < kTimed ? "0.00" : (m == (int)M.GcKB ? "0.0" : "0");
                csv.Append(',').Append(F(Avg(s, (M)m), fmt));
                if (m < kTimed) csv.Append(',').Append(F(P95(s, (M)m), fmt));
                csv.Append(',').Append(s.Samples[m] > 0 ? F(s.Max[m], fmt) : "N/A");
            }
            csv.Append('\n');

            log.Append(s.Key).Append(" | ").Append(F(s.Seconds, "0"))
               .Append(" | ").Append(F(Avg(s, M.Frame))).Append('/').Append(F(P95(s, M.Frame)))
               .Append(" | ").Append(F(Avg(s, M.Render)))
               .Append(" | ").Append(F(Avg(s, M.Gpu)))
               .Append(" | ").Append(F(Avg(s, M.Draws), "0"))
               .Append(" | ").Append(F(Avg(s, M.Shadow), "0"))
               .Append(" | ").Append(F(Pct(s.OverBudget, s.Frames), "0.0")).Append('\n');
        }

        // 엑셀이 한글을 깨뜨리지 않게 BOM 을 붙인다(Unity 에셋이 아니라 Logs 밑 CSV 라 무관).
        File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));

        // 패스별 GPU — 존마다 평균 큰 순. ⚠️ 마커는 중첩된다(부모 패스가 자식 시간을 포함) → 합계를 내지 말고 순위로 읽는다.
        string passPath = Path.ChangeExtension(path, null) + "-gpu-passes.csv";
        var pc = new StringBuilder(8192);
        pc.Append("# 패스별 GPU ms(존별). 마커가 중첩되므로 행을 더하지 말 것. zone_gpu_avg = 그 존의 GPU 프레임 평균\n");
        pc.Append("zone,pass,avg_ms,max_ms,frames,zone_gpu_avg\n");
        log.Append("── 패스별 GPU 상위(존별) ──\n");
        var order = new List<int>();
        foreach (var s in rows)
        {
            order.Clear();
            for (int i = 0; i < s.PassN.Length && i < _passes.Count; i++)
                if (s.PassN[i] > 0 && s.PassSum[i] / s.PassN[i] >= 0.02) order.Add(i);
            order.Sort((a, b) => (s.PassSum[b] / s.PassN[b]).CompareTo(s.PassSum[a] / s.PassN[a]));
            log.Append(s.Key).Append(':');
            for (int k = 0; k < order.Count; k++)
            {
                int i = order[k];
                double avg = s.PassSum[i] / s.PassN[i];
                pc.Append('"').Append(s.Key).Append("\",\"").Append(_passes[i].Name).Append("\",")
                  .Append(F(avg, "0.000")).Append(',').Append(F(s.PassMax[i], "0.000")).Append(',')
                  .Append(s.PassN[i]).Append(',').Append(F(Avg(s, M.Gpu))).Append('\n');
                if (k < 6) log.Append("  ").Append(_passes[i].Name).Append(' ').Append(F(avg, "0.00"));
            }
            log.Append('\n');
        }
        File.WriteAllText(passPath, pc.ToString(), new UTF8Encoding(true));
        log.Append("패스 ").Append(_passes.Count).Append("개 추적 · ").Append(passPath);

        // 타임라인 — 마지막 미완성 칸까지 포함. 값 = 그 1초 칸의 평균(ms), frame_max = 칸 안 최악 프레임.
        string tlPath = Path.ChangeExtension(path, null) + "-timeline.csv";
        var tc = new StringBuilder(64 * (_timeline.Count + 2));
        tc.Append("t_sec,zone,frames,frame_ms,frame_max_ms,main_ms,render_ms,gpu_ms,draws\n");
        int tlCount = _timeline.Count + (_tl.Frames > 0 ? 1 : 0);
        for (int i = 0; i < tlCount; i++)
        {
            var r = i < _timeline.Count ? _timeline[i] : _tl;
            int n = Math.Max(1, r.Frames), fn = Math.Max(1, r.FtN);
            tc.Append(F(r.T, "0.0")).Append(",\"").Append(r.Zone).Append("\",").Append(r.Frames).Append(',')
              .Append(F(r.Frame / n)).Append(',').Append(F(r.FrameMax)).Append(',')
              .Append(r.FtN > 0 ? F(r.Main / fn) : "N/A").Append(',')
              .Append(r.FtN > 0 ? F(r.Render / fn) : "N/A").Append(',')
              .Append(r.FtN > 0 ? F(r.Gpu / fn) : "N/A").Append(',')
              .Append(F(r.Draws / n, "0")).Append('\n');
        }
        File.WriteAllText(tlPath, tc.ToString(), new UTF8Encoding(true));
        log.Append("\n타임라인 ").Append(tlCount).Append("칸 · ").Append(tlPath);
        Debug.Log(log.ToString());
    }

    static double Nz(double v) => double.IsNaN(v) ? -1 : v;

    // ───────── 화면 ─────────
    void OnGUI()
    {
        if (!showOverlay || string.IsNullOrEmpty(_overlay.text)) return;
        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = false, richText = false };
            _style.normal.textColor = Color.white;
            _bg = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _bg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
            _bg.Apply();
        }
        const float w = 560f, h = 62f, pad = 8f;
        var r = new Rect(pad, Screen.height - h - pad, w, h);
        GUI.DrawTexture(r, _bg);
        GUI.Label(new Rect(r.x + 6f, r.y + 4f, w - 12f, h - 8f), _overlay, _style);
    }
#endif
}
