// ProfilerWindow.cs
// 에디터 전용 런타임 프로파일러 창 (Tools > Profiler HUD).
// 게임 uGUI 와 완전 분리된 에디터 UI 라 UI 충돌 0. 도킹 가능(Profiler 창 옆에).
// 데이터: ProfilerRecorder + FrameTimingManager. 에디터/MPPM 플레이모드에서 사용.
// 실기기/스탠드얼론 빌드 프로파일링은 런타임 IMGUI HUD(ProfilerHUD.cs)를 병행.
//
// Editor 폴더에 있으므로 빌드에 포함되지 않음(별도 가드 불필요).

using System.Collections.Generic;
using Unity.Profiling;
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
        w.minSize = new Vector2(300f, 240f);
        w.Show();
    }

    // ── 설정 ──
    int _targetFps = 60;
    static readonly string[] kDefaultMarkers = { "FullScreenFog", "FullScreenFog CopyBack" };

    // ── 레코더 ──
    ProfilerRecorder _mainThread, _drawCalls, _setPass, _tris, _gcAlloc, _sysMem;
    readonly List<MarkerEntry> _markers = new List<MarkerEntry>();

    sealed class MarkerEntry
    {
        public string label;
        public ProfilerRecorder rec;
        public Label ui;
        public VisualElement row;
    }

    // ── FrameTiming ──
    readonly FrameTiming[] _ft = new FrameTiming[1];
    double _cpuMs, _gpuMs;

    // ── 그래프 히스토리 ──
    const int kHistory = 180;
    readonly float[] _hist = new float[kHistory];
    int _histIdx;

    // ── UI ──
    Label _lblFps, _lblCpu, _lblExtra;
    VisualElement _passContainer;
    GraphElement _graph;
    double _lastTextUpdate;

    void OnEnable()
    {
        StartRecorders();
        BuildUI();
        EditorApplication.update += Tick;
    }

    void OnDisable()
    {
        EditorApplication.update -= Tick;
        DisposeRecorders();
    }

    void StartRecorders()
    {
        _mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
        _drawCalls  = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        _setPass    = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        _tris       = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        _gcAlloc    = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        _sysMem     = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
        foreach (var m in kDefaultMarkers)
            AddMarkerRecorder(m);
    }

    void DisposeRecorders()
    {
        _mainThread.Dispose();
        _drawCalls.Dispose();
        _setPass.Dispose();
        _tris.Dispose();
        _gcAlloc.Dispose();
        _sysMem.Dispose();
        foreach (var m in _markers)
            if (m.rec.Valid) m.rec.Dispose();
        _markers.Clear();
    }

    void AddMarkerRecorder(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var entry = new MarkerEntry
        {
            label = name.Trim(),
            rec = ProfilerRecorder.StartNew(ProfilerCategory.Render, name.Trim(), 15)
        };
        _markers.Add(entry);
    }

    // ── UI 구성 ──
    void BuildUI()
    {
        var root = rootVisualElement;
        root.Clear();
        root.style.paddingTop = 8; root.style.paddingBottom = 8;
        root.style.paddingLeft = 10; root.style.paddingRight = 10;

        // 툴바: Target FPS + 마커 추가
        var bar = Row();
        bar.style.marginBottom = 6;
        var fpsField = new IntegerField("Target FPS") { value = _targetFps };
        fpsField.style.width = 150;
        fpsField.RegisterValueChangedCallback(e => _targetFps = Mathf.Max(1, e.newValue));
        bar.Add(fpsField);

        var spacer = new VisualElement(); spacer.style.flexGrow = 1; bar.Add(spacer);

        var addField = new TextField { value = "" };
        addField.style.width = 130;
        addField.style.marginRight = 4;
        bar.Add(addField);
        var addBtn = new Button(() =>
        {
            var n = addField.value;
            if (string.IsNullOrWhiteSpace(n)) return;
            AddMarkerRecorder(n);
            BuildPassRow(_markers[_markers.Count - 1]);
            addField.value = "";
        }) { text = "마커 추가" };
        bar.Add(addBtn);
        root.Add(bar);

        // 상단 통계
        _lblFps = StatLabel(16, FontStyle.Bold); root.Add(_lblFps);
        _lblCpu = StatLabel(12, FontStyle.Normal); root.Add(_lblCpu);
        _lblExtra = StatLabel(12, FontStyle.Normal);
        _lblExtra.style.whiteSpace = WhiteSpace.Normal;
        root.Add(_lblExtra);

        // 패스 목록
        var passHeader = StatLabel(11, FontStyle.Bold);
        passHeader.text = "─ Render Passes ─";
        passHeader.style.marginTop = 6;
        passHeader.style.color = new Color(0.7f, 0.75f, 0.85f);
        root.Add(passHeader);

        _passContainer = new VisualElement();
        root.Add(_passContainer);
        foreach (var m in _markers) BuildPassRow(m);

        // 그래프
        _graph = new GraphElement();
        _graph.style.marginTop = 8;
        _graph.style.height = 70;
        _graph.style.backgroundColor = new Color(1f, 1f, 1f, 0.04f);
        root.Add(_graph);

        var hint = StatLabel(10, FontStyle.Normal);
        hint.text = "GPU ms 가 0 이면 Player > Frame Timing Stats 활성화. 패스 ms 는 RenderGraph 패스 이름과 일치 시 표시.";
        hint.style.whiteSpace = WhiteSpace.Normal;
        hint.style.color = new Color(0.6f, 0.6f, 0.65f);
        hint.style.marginTop = 6;
        root.Add(hint);
    }

    void BuildPassRow(MarkerEntry m)
    {
        var row = Row();
        row.style.justifyContent = Justify.SpaceBetween;
        var name = new Label(m.label);
        name.style.color = new Color(0.8f, 0.85f, 1f);
        var val = new Label("…");
        val.style.unityTextAlign = TextAnchor.MiddleRight;
        row.Add(name); row.Add(val);
        m.ui = val; m.row = row;
        _passContainer.Add(row);
    }

    static VisualElement Row()
    {
        var v = new VisualElement();
        v.style.flexDirection = FlexDirection.Row;
        v.style.alignItems = Align.Center;
        return v;
    }

    static Label StatLabel(int size, FontStyle fs)
    {
        var l = new Label("");
        l.style.fontSize = size;
        l.style.unityFontStyleAndWeight = fs == FontStyle.Bold ? FontStyle.Bold : FontStyle.Normal;
        l.style.marginTop = 1; l.style.marginBottom = 1;
        return l;
    }

    // ── 갱신 ──
    void Tick()
    {
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, _ft) > 0)
        {
            _cpuMs = _ft[0].cpuFrameTime;
            _gpuMs = _ft[0].gpuFrameTime;
        }

        float total = (_cpuMs > 0.0) ? (float)System.Math.Max(_cpuMs, _gpuMs)
                                     : Time.unscaledDeltaTime * 1000f;
        _hist[_histIdx] = total;
        _histIdx = (_histIdx + 1) % kHistory;

        double now = EditorApplication.timeSinceStartup;
        if (now - _lastTextUpdate < 0.1) { _graph.MarkDirtyRepaint(); return; }
        _lastTextUpdate = now;

        float budget = 1000f / Mathf.Max(1, _targetFps);
        float fps = total > 0.0001f ? 1000f / total : 0f;

        _lblFps.text = $"FPS {fps:0.0}    Frame {total:0.00} ms   (예산 {budget:0.00})";
        _lblFps.style.color = BudgetColor(total, budget);

        double mainMs = NsToMs(Avg(_mainThread));
        _lblCpu.text = $"CPU {_cpuMs:0.00}   GPU {_gpuMs:0.00}   Main {mainMs:0.00} ms";
        _lblExtra.text = $"Draw {_drawCalls.LastValue}   SetPass {_setPass.LastValue}   Tris {_tris.LastValue}    " +
                         $"GC {_gcAlloc.LastValue / 1024.0:0.0} KB   Mem {_sysMem.LastValue / (1024.0 * 1024.0):0} MB";

        foreach (var m in _markers)
        {
            if (m.ui == null) continue;
            if (!m.rec.Valid) { m.ui.text = "(마커 없음)"; m.ui.style.color = new Color(0.6f, 0.6f, 0.6f); continue; }
            double ms = NsToMs(Avg(m.rec));
            m.ui.text = $"{ms:0.00} ms";
            m.ui.style.color = BudgetColor((float)ms, budget);
        }

        _graph.budgetMs = budget;
        _graph.data = _hist;
        _graph.head = _histIdx;
        _graph.MarkDirtyRepaint();
        Repaint();
    }

    static double Avg(ProfilerRecorder rec)
    {
        if (!rec.Valid) return 0;
        int count = rec.Count;
        if (count == 0) return 0;
        double sum = 0;
        for (int i = 0; i < count; i++) sum += rec.GetSample(i).Value;
        return sum / count;
    }

    static double NsToMs(double ns) => ns * 1e-6;

    static Color BudgetColor(float ms, float budget)
    {
        if (budget <= 0f) return Color.white;
        float ratio = ms / budget;
        if (ratio < 0.8f) return new Color(0.4f, 1f, 0.5f);
        if (ratio < 1.0f) return new Color(1f, 0.9f, 0.3f);
        return new Color(1f, 0.45f, 0.4f);
    }

    // ── 그래프 엘리먼트 (UI Toolkit Vector API) ──
    sealed class GraphElement : VisualElement
    {
        public float[] data;
        public int head;
        public float budgetMs = 16.67f;

        public GraphElement()
        {
            generateVisualContent += OnGenerate;
        }

        void OnGenerate(MeshGenerationContext ctx)
        {
            if (data == null || data.Length < 2) return;
            float w = contentRect.width, h = contentRect.height;
            if (w <= 1f || h <= 1f) return;

            var p = ctx.painter2D;
            float maxMs = Mathf.Max(budgetMs * 2f, 1f);

            // 예산 라인
            float by = h - (budgetMs / maxMs) * h;
            p.strokeColor = new Color(1f, 0.9f, 0.2f, 0.6f);
            p.lineWidth = 1f;
            p.BeginPath();
            p.MoveTo(new Vector2(0f, by));
            p.LineTo(new Vector2(w, by));
            p.Stroke();

            // 프레임 타임 라인
            int n = data.Length;
            p.strokeColor = new Color(0.35f, 0.9f, 1f, 0.95f);
            p.lineWidth = 1.5f;
            p.BeginPath();
            for (int i = 0; i < n; i++)
            {
                int idx = (head + i) % n;
                float ms = data[idx];
                float x = (i / (float)(n - 1)) * w;
                float y = h - Mathf.Clamp01(ms / maxMs) * h;
                if (i == 0) p.MoveTo(new Vector2(x, y));
                else p.LineTo(new Vector2(x, y));
            }
            p.Stroke();
        }
    }
}
