// ProfilerWindow.cs  (v2 — 병목 triage 대시보드)
// 에디터 전용 런타임 프로파일러 창 (Tools > Profiler HUD).
// 게임 uGUI 와 완전 분리(에디터 UI)라 UI 충돌 0. 도킹 가능.
//
// 한눈에:  ① 병목 판정(CPU/GPU/VSync)  ② CPU 카테고리 분해(ms·%·막대)
//          ③ 렌더링 상세 카운터  ④ 렌더 패스별 ms  ⑤ 내장 도구로 드릴다운 버튼
//
// 데이터: FrameTimingManager(CPU/GPU ms) + ProfilerRecorder(카테고리/패스/카운터).
// 카테고리 마커는 ProfilerRecorderHandle.GetAvailable 로 "실제 존재하는 것만" 선택 → 버전/플랫폼 차이에 강함.
// 깊은 분석은 버튼으로 Profiler / Frame Debugger / Rendering Debugger 를 연다.
//
// Editor 폴더에 있으므로 빌드 제외. 실기기 프로파일링은 런타임 IMGUI HUD(ProfilerHUD.cs) 병행.

using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class ProfilerWindow : EditorWindow
{
    [MenuItem("Tools/Profiler HUD")]
    public static void Open()
    {
        var w = GetWindow<ProfilerWindow>();
        w.titleContent = new GUIContent("Profiler HUD");
        w.minSize = new Vector2(340f, 420f);
        w.Show();
    }

    // ── 설정 ──
    int _targetFps = 60;

    // ── FrameTiming ──
    readonly FrameTiming[] _ft = new FrameTiming[1];
    double _cpuMs, _gpuMs;

    // ── 사용 가능 마커 목록(이름→카테고리) ──
    readonly Dictionary<string, ProfilerCategory> _available = new Dictionary<string, ProfilerCategory>();

    // ── CPU 카테고리 ──
    sealed class Cat
    {
        public string name;
        public string[] markers;
        public bool sum;          // true=여러 단계 합산, false=우선순위 첫 유효 1개
        public List<ProfilerRecorder> recs = new List<ProfilerRecorder>();
        public Label val;
        public VisualElement fill;
    }
    readonly List<Cat> _cats = new List<Cat>();
    ProfilerRecorder _vsyncRec;   // present/vsync 대기 (병목 판정용)

    // ── 렌더 카운터 ──
    sealed class Counter { public string name; public string unit; public ProfilerCategory cat; public ProfilerRecorder rec; public Label val; }
    readonly List<Counter> _counters = new List<Counter>();

    // ── 렌더 패스 ──
    sealed class Pass { public string label; public ProfilerRecorder rec; public Label val; }
    readonly List<Pass> _passes = new List<Pass>();
    static readonly string[] kDefaultPasses =
    {
        "FullScreenFog", "FullScreenFog CopyBack",
        "RenderLoop.Draw", "DrawOpaqueObjects", "DrawTransparentObjects",
        "RenderShadows", "MainLightShadow", "UberPostProcess", "PostProcessing",
    };

    // ── 게임플레이 커스텀 마커 (BT 등) ──
    readonly List<Pass> _gameplay = new List<Pass>();
    static readonly string[] kDefaultGameplay = { "BT" };

    // ── 그래프 ──
    const int kHistory = 180;
    readonly float[] _hist = new float[kHistory];
    int _histIdx;

    // ── UI ──
    Label _verdict, _lblFps, _lblThreads, _graphInfo;
    VisualElement _passBox;
    GraphElement _graph;
    double _lastTextUpdate;

    void OnEnable()
    {
        DiscoverAvailable();
        StartRecorders();
        BuildUI();
        EditorApplication.update += Tick;
    }

    void OnDisable()
    {
        EditorApplication.update -= Tick;
        DisposeRecorders();
    }

    void DiscoverAvailable()
    {
        _available.Clear();
        var list = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(list);
        foreach (var h in list)
        {
            var d = ProfilerRecorderHandle.GetDescription(h);
            if (!string.IsNullOrEmpty(d.Name)) _available[d.Name] = d.Category;
        }
    }

    ProfilerRecorder TryStart(string name, int cap = 15)
    {
        if (_available.TryGetValue(name, out var cat))
            return ProfilerRecorder.StartNew(cat, name, cap);
        return default;
    }

    void StartRecorders()
    {
        // CPU 카테고리 (실제 존재하는 마커만 추가됨)
        AddCat("Scripts", true, "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate", "FixedUpdate.ScriptRunBehaviourFixedUpdate");
        AddCat("Rendering", false, "RenderPipelineManager.DoRenderLoop_Internal", "Camera.Render", "RenderPipeline.Render");
        AddCat("Physics", false, "FixedUpdate.PhysicsFixedUpdate", "Physics.Processing", "Physics.Simulate");
        AddCat("Animation", false, "PostLateUpdate.DirectorLateUpdate", "Update.DirectorUpdate", "Animators.Update", "Animation.Update");
        AddCat("GC", false, "GC.Collect");
        AddCat("VSync/Present", false, "WaitForTargetFPS", "Gfx.WaitForPresentOnGfxThread", "WaitForLastPresentationAndGetTimestamp", "Gfx.PresentFrame");

        // 병목 판정용 present 대기
        _vsyncRec = TryStartFirst("WaitForTargetFPS", "Gfx.WaitForPresentOnGfxThread", "WaitForLastPresentationAndGetTimestamp");

        // 렌더 카운터 (안정 카운터)
        AddCounter("Batches", "", ProfilerCategory.Render, "Batches Count");
        AddCounter("Draw Calls", "", ProfilerCategory.Render, "Draw Calls Count");
        AddCounter("SetPass", "", ProfilerCategory.Render, "SetPass Calls Count");
        AddCounter("Triangles", "", ProfilerCategory.Render, "Triangles Count");
        AddCounter("Vertices", "", ProfilerCategory.Render, "Vertices Count");
        AddCounter("Shadow Casters", "", ProfilerCategory.Render, "Shadow Casters Count");
        AddCounter("Render Textures", "", ProfilerCategory.Render, "Render Textures Count");
        AddCounter("Used Textures", "", ProfilerCategory.Render, "Used Textures Count");
        AddCounter("GC Alloc/frame", "KB", ProfilerCategory.Memory, "GC Allocated In Frame");
        AddCounter("System Memory", "MB", ProfilerCategory.Memory, "System Used Memory");

        // 렌더 패스 (유효한 것만)
        foreach (var n in kDefaultPasses)
        {
            var rec = TryStart(n);
            if (rec.Valid) _passes.Add(new Pass { label = n, rec = rec });
        }

        // 게임플레이 커스텀 마커 (Prof.BT 등) — 아직 안 잡혔어도 등록해두면 Play 중 채워짐
        foreach (var n in kDefaultGameplay)
            _gameplay.Add(new Pass { label = n, rec = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, n, 15) });
    }

    void AddCat(string name, bool sum, params string[] markers)
    {
        var c = new Cat { name = name, sum = sum, markers = markers };
        if (sum)
        {
            foreach (var m in markers) { var r = TryStart(m); if (r.Valid) c.recs.Add(r); }
        }
        else
        {
            foreach (var m in markers) { var r = TryStart(m); if (r.Valid) { c.recs.Add(r); break; } }
        }
        if (c.recs.Count > 0) _cats.Add(c);
    }

    void AddCounter(string label, string unit, ProfilerCategory cat, string counterName)
    {
        if (!_available.ContainsKey(counterName)) return;
        _counters.Add(new Counter { name = label, unit = unit, cat = cat, rec = ProfilerRecorder.StartNew(cat, counterName) });
    }

    ProfilerRecorder TryStartFirst(params string[] names)
    {
        foreach (var n in names) { var r = TryStart(n); if (r.Valid) return r; }
        return default;
    }

    void DisposeRecorders()
    {
        foreach (var c in _cats) foreach (var r in c.recs) if (r.Valid) r.Dispose();
        foreach (var c in _counters) if (c.rec.Valid) c.rec.Dispose();
        foreach (var p in _passes) if (p.rec.Valid) p.rec.Dispose();
        foreach (var p in _gameplay) if (p.rec.Valid) p.rec.Dispose();
        if (_vsyncRec.Valid) _vsyncRec.Dispose();
        _cats.Clear(); _counters.Clear(); _passes.Clear(); _gameplay.Clear();
    }

    // ── UI ──
    void BuildUI()
    {
        var root = rootVisualElement;
        root.Clear();
        root.style.paddingTop = 8; root.style.paddingBottom = 8; root.style.paddingLeft = 10; root.style.paddingRight = 10;

        var scroll = new ScrollView();
        root.Add(scroll);
        var body = scroll.contentContainer;

        // 툴바
        var bar = Row(); bar.style.marginBottom = 4;
        var fps = new IntegerField("Target FPS") { value = _targetFps }; fps.style.width = 150;
        fps.RegisterValueChangedCallback(e => _targetFps = Mathf.Max(1, e.newValue));
        bar.Add(fps);
        body.Add(bar);

        // 병목 판정
        _verdict = new Label("…");
        _verdict.style.fontSize = 15; _verdict.style.unityFontStyleAndWeight = FontStyle.Bold;
        _verdict.style.marginTop = 2; _verdict.style.marginBottom = 2;
        _verdict.style.paddingTop = 4; _verdict.style.paddingBottom = 4; _verdict.style.paddingLeft = 8;
        _verdict.style.borderTopLeftRadius = 4; _verdict.style.borderTopRightRadius = 4;
        _verdict.style.borderBottomLeftRadius = 4; _verdict.style.borderBottomRightRadius = 4;
        body.Add(_verdict);

        _lblFps = Mono(13, FontStyle.Bold); body.Add(_lblFps);
        _lblThreads = Mono(12, FontStyle.Normal); body.Add(_lblThreads);

        // CPU 카테고리
        Header(body, "CPU 카테고리 (프레임 대비 %)");
        foreach (var c in _cats)
        {
            var row = Row(); row.style.alignItems = Align.Center; row.style.marginBottom = 1;
            var name = new Label(c.name); name.style.width = 110; name.style.color = new Color(0.85f, 0.88f, 0.95f);
            var track = new VisualElement();
            track.style.flexGrow = 1; track.style.height = 12;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);
            track.style.marginLeft = 4; track.style.marginRight = 6;
            var fill = new VisualElement(); fill.style.height = 12; fill.style.width = Length.Percent(0);
            track.Add(fill);
            var val = new Label("…"); val.style.width = 96; val.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(name); row.Add(track); row.Add(val);
            c.fill = fill; c.val = val;
            body.Add(row);
        }
        if (_cats.Count == 0)
            body.Add(Note("카테고리 마커 미검출 — 플레이모드 진입 후 표시됨"));

        // 렌더링 상세
        Header(body, "렌더링 상세");
        foreach (var c in _counters)
        {
            var row = Row(); row.style.justifyContent = Justify.SpaceBetween;
            var name = new Label(c.name); name.style.color = new Color(0.8f, 0.85f, 1f);
            var val = new Label("…"); val.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(name); row.Add(val); c.val = val;
            body.Add(row);
        }

        // 렌더 패스
        Header(body, "렌더 패스 ms");
        _passBox = new VisualElement(); body.Add(_passBox);
        foreach (var p in _passes)
        {
            var row = Row(); row.style.justifyContent = Justify.SpaceBetween;
            var name = new Label(p.label); name.style.color = new Color(0.8f, 0.85f, 1f);
            var val = new Label("…"); val.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(name); row.Add(val); p.val = val;
            _passBox.Add(row);
        }
        if (_passes.Count == 0)
            _passBox.Add(Note("활성 패스 마커 없음 — 플레이모드/카메라 렌더 시 표시"));

        // 게임플레이 마커 (BT 등)
        Header(body, "게임플레이 마커 (ms)");
        foreach (var p in _gameplay)
        {
            var row = Row(); row.style.justifyContent = Justify.SpaceBetween;
            var name = new Label(p.label); name.style.color = new Color(0.9f, 0.8f, 1f);
            var val = new Label("…"); val.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(name); row.Add(val); p.val = val;
            body.Add(row);
        }
        body.Add(Note("BT 등 게임 코드 비용을 따로 보려면 using (Prof.BT.Auto()) { ... } 로 감싼다. " +
                      "‘대기’는 아직 마커 미히트(Play 진입·해당 코드 실행 필요)."));

        // 그래프
        Header(body, "프레임타임 (ms) — 파랑=실제 · 노랑=목표예산");
        _graph = new GraphElement(); _graph.style.height = 70;
        _graph.style.backgroundColor = new Color(1f, 1f, 1f, 0.04f);
        body.Add(_graph);
        _graphInfo = Note(""); body.Add(_graphInfo);

        // 드릴다운 버튼
        Header(body, "정밀 분석 (내장 도구)");
        var btns = Row(); btns.style.flexWrap = Wrap.Wrap;
        btns.Add(LinkBtn("Profiler", "Window/Analysis/Profiler"));
        btns.Add(LinkBtn("Frame Debugger", "Window/Analysis/Frame Debugger"));
        btns.Add(LinkBtn("Rendering Debugger", "Window/Analysis/Rendering Debugger"));
        body.Add(btns);

        body.Add(Note("GPU ms 0 → Player > Other Settings > Frame Timing Stats 활성화. " +
                      "패스/카테고리는 플레이모드에서 값이 채워짐. 빨간 항목은 위 버튼으로 드릴다운."));
    }

    static Button LinkBtn(string text, string menu)
    {
        var b = new Button(() => { if (!EditorApplication.ExecuteMenuItem(menu)) Debug.LogWarning($"[ProfilerHUD] 메뉴 없음: {menu}"); }) { text = text };
        b.style.marginRight = 4; b.style.marginTop = 2;
        return b;
    }

    static void Header(VisualElement parent, string text)
    {
        var l = new Label("─ " + text + " ─");
        l.style.unityFontStyleAndWeight = FontStyle.Bold; l.style.fontSize = 11;
        l.style.marginTop = 8; l.style.marginBottom = 2; l.style.color = new Color(0.65f, 0.72f, 0.82f);
        parent.Add(l);
    }

    static Label Note(string text)
    {
        var l = new Label(text); l.style.whiteSpace = WhiteSpace.Normal; l.style.fontSize = 10;
        l.style.color = new Color(0.55f, 0.55f, 0.6f); l.style.marginTop = 6;
        return l;
    }

    static VisualElement Row()
    {
        var v = new VisualElement(); v.style.flexDirection = FlexDirection.Row; v.style.alignItems = Align.Center;
        return v;
    }

    static Label Mono(int size, FontStyle fs)
    {
        var l = new Label(""); l.style.fontSize = size;
        l.style.unityFontStyleAndWeight = fs; l.style.marginTop = 1; l.style.marginBottom = 1;
        return l;
    }

    // ── 갱신 ──
    void Tick()
    {
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, _ft) > 0) { _cpuMs = _ft[0].cpuFrameTime; _gpuMs = _ft[0].gpuFrameTime; }

        float total = (_cpuMs > 0.0) ? (float)System.Math.Max(_cpuMs, _gpuMs) : Time.unscaledDeltaTime * 1000f;
        _hist[_histIdx] = total; _histIdx = (_histIdx + 1) % kHistory;

        double now = EditorApplication.timeSinceStartup;
        if (now - _lastTextUpdate < 0.1) { _graph.MarkDirtyRepaint(); return; }
        _lastTextUpdate = now;

        float budget = 1000f / Mathf.Max(1, _targetFps);
        float fps = total > 0.0001f ? 1000f / total : 0f;
        float frameMs = total > 0.0001f ? total : 1f;

        // 판정
        UpdateVerdict(budget);

        _lblFps.text = $"FPS {fps:0.0}    Frame {total:0.00} ms   (예산 {budget:0.00})";
        _lblFps.style.color = BudgetColor(total, budget);
        _lblThreads.text = $"CPU {_cpuMs:0.00} ms    GPU {_gpuMs:0.00} ms";

        // 카테고리
        foreach (var c in _cats)
        {
            double ms = 0; foreach (var r in c.recs) ms += NsToMs(Avg(r));
            float share = Mathf.Clamp01((float)(ms / frameMs));
            c.val.text = $"{ms,6:0.00} ms  {share * 100f,4:0.0}%";
            c.fill.style.width = Length.Percent(share * 100f);
            var col = ShareColor(share); col.a = 0.85f;
            c.fill.style.backgroundColor = col;
            c.val.style.color = ShareColor(share);
        }

        // 카운터
        foreach (var c in _counters)
        {
            long v = c.rec.Valid ? c.rec.LastValue : 0;
            if (c.unit == "KB") c.val.text = $"{v / 1024.0:0.0} KB";
            else if (c.unit == "MB") c.val.text = $"{v / (1024.0 * 1024.0):0} MB";
            else c.val.text = v.ToString("N0");
        }

        // 패스
        foreach (var p in _passes)
        {
            double ms = p.rec.Valid ? NsToMs(Avg(p.rec)) : 0;
            p.val.text = $"{ms:0.00} ms";
            p.val.style.color = BudgetColor((float)ms, budget);
        }

        // 게임플레이 마커
        foreach (var p in _gameplay)
        {
            if (!p.rec.Valid) { p.val.text = "대기"; p.val.style.color = new Color(0.55f, 0.55f, 0.6f); continue; }
            double ms = NsToMs(Avg(p.rec));
            p.val.text = $"{ms:0.00} ms";
            p.val.style.color = BudgetColor((float)ms, budget);
        }

        // 그래프 라벨
        float gmax = 0f; for (int i = 0; i < kHistory; i++) if (_hist[i] > gmax) gmax = _hist[i];
        _graphInfo.text = $"현재 {total:0.00} · 최대 {gmax:0.00} · 예산 {budget:0.00} ms   (세로축 0~{budget * 2f:0.0} ms)";

        _graph.budgetMs = budget; _graph.data = _hist; _graph.head = _histIdx; _graph.MarkDirtyRepaint();
        Repaint();
    }

    void UpdateVerdict(float budget)
    {
        double vsyncMs = _vsyncRec.Valid ? NsToMs(Avg(_vsyncRec)) : 0;
        float frame = (float)System.Math.Max(_cpuMs, _gpuMs);
        string text; Color col;

        if (_cpuMs <= 0.0 && _gpuMs <= 0.0)
        {
            text = "측정 대기 (플레이모드 진입 / Frame Timing Stats 확인)";
            col = new Color(0.4f, 0.4f, 0.45f);
        }
        else if (vsyncMs > 0.5 && vsyncMs > frame * 0.25f)
        {
            text = $"VSYNC / 프레임캡 제한  (present 대기 {vsyncMs:0.0} ms)";
            col = new Color(0.4f, 0.6f, 1f);
        }
        else if (_gpuMs > _cpuMs * 1.1)
        {
            text = $"GPU-BOUND  (GPU {_gpuMs:0.00} > CPU {_cpuMs:0.00} ms)";
            col = new Color(1f, 0.55f, 0.3f);
        }
        else if (_cpuMs > _gpuMs * 1.1)
        {
            text = $"CPU-BOUND  (CPU {_cpuMs:0.00} > GPU {_gpuMs:0.00} ms)";
            col = new Color(1f, 0.8f, 0.3f);
        }
        else
        {
            text = $"BALANCED  (CPU {_cpuMs:0.00} ≈ GPU {_gpuMs:0.00} ms)";
            col = new Color(0.4f, 0.9f, 0.5f);
        }
        _verdict.text = "판정: " + text;
        _verdict.style.color = Color.white;
        col.a = 0.35f; _verdict.style.backgroundColor = col;
    }

    static double Avg(ProfilerRecorder rec)
    {
        if (!rec.Valid) return 0;
        int count = rec.Count; if (count == 0) return 0;
        double sum = 0; for (int i = 0; i < count; i++) sum += rec.GetSample(i).Value;
        return sum / count;
    }

    static double NsToMs(double ns) => ns * 1e-6;

    static Color BudgetColor(float ms, float budget)
    {
        if (budget <= 0f) return Color.white;
        float r = ms / budget;
        if (r < 0.8f) return new Color(0.4f, 1f, 0.5f);
        if (r < 1.0f) return new Color(1f, 0.9f, 0.3f);
        return new Color(1f, 0.45f, 0.4f);
    }

    static Color ShareColor(float share)
    {
        if (share < 0.25f) return new Color(0.4f, 1f, 0.5f);
        if (share < 0.5f) return new Color(1f, 0.9f, 0.3f);
        return new Color(1f, 0.45f, 0.4f);
    }

    // ── 그래프 ──
    sealed class GraphElement : VisualElement
    {
        public float[] data; public int head; public float budgetMs = 16.67f;
        public GraphElement() { generateVisualContent += OnGenerate; }
        void OnGenerate(MeshGenerationContext ctx)
        {
            if (data == null || data.Length < 2) return;
            float w = contentRect.width, h = contentRect.height;
            if (w <= 1f || h <= 1f) return;
            var p = ctx.painter2D;
            float maxMs = Mathf.Max(budgetMs * 2f, 1f);
            float by = h - (budgetMs / maxMs) * h;
            p.strokeColor = new Color(1f, 0.9f, 0.2f, 0.6f); p.lineWidth = 1f;
            p.BeginPath(); p.MoveTo(new Vector2(0f, by)); p.LineTo(new Vector2(w, by)); p.Stroke();
            int n = data.Length;
            p.strokeColor = new Color(0.35f, 0.9f, 1f, 0.95f); p.lineWidth = 1.5f; p.BeginPath();
            for (int i = 0; i < n; i++)
            {
                int idx = (head + i) % n; float ms = data[idx];
                float x = (i / (float)(n - 1)) * w; float y = h - Mathf.Clamp01(ms / maxMs) * h;
                if (i == 0) p.MoveTo(new Vector2(x, y)); else p.LineTo(new Vector2(x, y));
            }
            p.Stroke();
        }
    }
}
