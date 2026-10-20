#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// 할 일 57.2 — P572 시나리오가 남긴 Profiler 바이너리 로그(seg-*.raw)·frames.csv·events.csv 를 읽어 보고용 표를 만든다.
/// 세그먼트 하나를 에디터 업데이트 한 번에 처리한다(진행 = run 폴더의 analysis-status.txt). 끝나면 불러온 프레임을 비우고
/// 바꾼 Profiler 버퍼 길이(ProfilerUserSettings.frameCount)를 되돌린다.
/// 산출: profile-frames.csv · events-analysis.md · event-dumps/*.txt · top-frames.md · summary.md · 첫 예고가 든 세그먼트의 seg-*.data.
/// 프레임 맞추기 = 런타임이 프레임마다 넣은 메타데이터(<see cref="QABossProfileProbe.MetaGuid"/> = { frameCount, phase }).
/// </summary>
[InitializeOnLoad]
public static class QABossProfileAnalyzer
{
    private const string MenuRoot = "Tools/QA/57.2/";
    private const string KeyDeep = "QA572.OrigDeep";
    private const string KeyEditor = "QA572.OrigProfileEditor";
    private const string KeyArmed = "QA572.Armed";

    static QABossProfileAnalyzer()
    {
        EditorApplication.playModeStateChanged += change =>
        {
            if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(KeyArmed, false))
                RestoreProfilerAfterRun();
        };
    }

    // ── 실행 전·후 Profiler 설정 ─────────────────────────────────────

    /// <summary>Deep Profile·에디터 프로파일 끔(원래 값은 SessionState 에 두고 Play 가 끝나면 되돌린다). Deep 은 바꾸면 리로드라 다를 때만.</summary>
    public static void PrepareProfilerForRun()
    {
        SessionState.SetBool(KeyDeep, ProfilerDriver.deepProfiling);
        SessionState.SetBool(KeyEditor, ProfilerDriver.profileEditor);
        SessionState.SetBool(KeyArmed, true);
        if (ProfilerDriver.deepProfiling)
            ProfilerDriver.deepProfiling = false;
        if (ProfilerDriver.profileEditor)
            ProfilerDriver.profileEditor = false;
        Debug.Log($"[QA][57.2] Profiler 준비 — deep={ProfilerDriver.deepProfiling} profileEditor={ProfilerDriver.profileEditor}(원래 deep={SessionState.GetBool(KeyDeep, false)} editor={SessionState.GetBool(KeyEditor, false)})");
    }

    private static void RestoreProfilerAfterRun()
    {
        SessionState.SetBool(KeyArmed, false);
        UnityEngine.Profiling.Profiler.enabled = false;
        UnityEngine.Profiling.Profiler.enableBinaryLog = false;
        UnityEngine.Profiling.Profiler.logFile = "";
        bool deep = SessionState.GetBool(KeyDeep, false), editor = SessionState.GetBool(KeyEditor, false);
        if (ProfilerDriver.profileEditor != editor)
            ProfilerDriver.profileEditor = editor;
        if (ProfilerDriver.deepProfiling != deep)
            ProfilerDriver.deepProfiling = deep;
        Debug.Log($"[QA][57.2] Profiler 설정 원복 — enabled=false binaryLog=false deep={deep} profileEditor={editor}");
    }

    // ── 분석 ─────────────────────────────────────────────────────

    private sealed class FrameRow
    {
        public int Seg, Index, Frame = -1, Phase = -1;
        public float FrameMs, PlayerLoopMs, EditorLoopMs, GcCollectMs, GcAllocKb;
        public string Markers = "";
        public bool Edge;   // 세그먼트 첫·끝 프레임 — 프로파일러를 껐다 켠 측정 도구 비용(분포에서 따로 뺀다)
        public readonly Dictionary<string, float> Diag = new Dictionary<string, float>();   // Diag57.* 진단 마커 ms(+ ".JIT")

        public float DiagMs(string key) => Diag.TryGetValue(key, out float ms) ? ms : 0f;
    }

    private static readonly string[] DiagKeys = { "Show", "ClientLock", "CreateJump" };

    private sealed class Ev
    {
        public int Frame;
        public string Phase, Kind, Detail;
    }

    private sealed class TopFrame
    {
        public FrameRow Row;
        public string Text;
    }

    private const int TopK = 10;
    private static string _dir;
    private static List<string> _segs;
    private static int _next;
    private static readonly List<FrameRow> _rows = new List<FrameRow>();
    private static readonly List<Ev> _events = new List<Ev>();
    private static readonly Dictionary<int, string> _watch = new Dictionary<int, string>();   // frame → 사유(첫 예고 F-1·F·F+1)
    private static readonly Dictionary<int, string> _watchText = new Dictionary<int, string>();
    private static readonly List<TopFrame> _topCombat = new List<TopFrame>();
    private static readonly List<TopFrame> _topCombatTotal = new List<TopFrame>();
    private static readonly List<TopFrame> _topLoading = new List<TopFrame>();
    private static readonly HashSet<int> _saveSegs = new HashSet<int>();
    private static int _origFrameCount = -1;
    private static PropertyInfo _frameCountProp;

    [MenuItem(MenuRoot + "Analyze Latest Boss Profile")]
    public static void AnalyzeLatest()
    {
        string root = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Application.dataPath)), "_lane-artifacts",
                                   Path.GetFileName(Path.GetDirectoryName(Application.dataPath)), "57.2-profile");
        string dir = Directory.Exists(root) ? Directory.GetDirectories(root, "run-*").OrderBy(d => d).LastOrDefault() : null;
        if (dir == null)
        {
            Debug.LogError($"[QA][57.2] {root} 에 run-* 없음");
            return;
        }
        Analyze(dir);
    }

    public static void Analyze(string dir)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[QA][57.2] Play 중에는 분석하지 않는다(불러오기가 기록 버퍼를 덮는다).");
            return;
        }
        _dir = dir;
        _segs = Directory.GetFiles(dir, "seg-*.raw").OrderBy(s => s).ToList();
        _next = 0;
        _rows.Clear();
        _events.Clear();
        _watch.Clear();
        _watchText.Clear();
        _topCombat.Clear();
        _topCombatTotal.Clear();
        _topLoading.Clear();
        _saveSegs.Clear();
        ReadEvents();

        // 버퍼(기본 300)를 세그먼트(1000)보다 크게 — 불러오기가 앞 프레임을 버리지 않게.
        Type t = typeof(EditorWindow).Assembly.GetType("UnityEditor.Profiling.ProfilerUserSettings");
        _frameCountProp = t?.GetProperty("frameCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        _origFrameCount = _frameCountProp != null ? (int)_frameCountProp.GetValue(null) : -1;
        if (_frameCountProp != null && _origFrameCount < 2000)
            _frameCountProp.SetValue(null, 2000);

        Status($"시작 — 세그먼트 {_segs.Count}개, 이벤트 {_events.Count}줄, 버퍼 {_origFrameCount}→{(_frameCountProp != null ? _frameCountProp.GetValue(null) : "?")}");
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void ReadEvents()
    {
        string path = Path.Combine(_dir, "events.csv");
        if (!File.Exists(path))
            return;
        foreach (string line in File.ReadAllLines(path).Skip(1))
        {
            string[] c = line.Split(',');
            if (c.Length < 5 || !int.TryParse(c[0], out int f))
                continue;
            _events.Add(new Ev { Frame = f, Phase = c[2], Kind = c[3], Detail = c[4] });
        }
        var firsts = new Dictionary<string, int>();
        foreach (Ev e in _events)
        {
            string key = e.Kind == "ConeShow" && (e.Detail == "Dash" || e.Detail == "RageDash") ? "DashShow" : e.Kind;
            foreach (string k in key == "DashShow" ? new[] { "ConeShow", "DashShow" } : new[] { key })
                if (IsWatchKind(k) && !firsts.ContainsKey(k))
                    firsts[k] = e.Frame;
        }
        foreach (KeyValuePair<string, int> kv in firsts)
            for (int d = -1; d <= 1; d++)
            {
                string why = $"{kv.Key} 첫 관찰 f{kv.Value} {(d == 0 ? "F" : d < 0 ? "F-1" : "F+1")}";
                _watch[kv.Value + d] = _watch.TryGetValue(kv.Value + d, out string w) ? w + " · " + why : why;
            }
    }

    private static bool IsWatchKind(string k) => k == "ConeShow" || k == "DashShow" || k == "DroneMark" || k == "DroneLock" || k == "JumpShow";

    private static void Status(string s)
    {
        File.AppendAllText(Path.Combine(_dir, "analysis-status.txt"), $"{DateTime.Now:HH:mm:ss} {s}\n");
        Debug.Log($"[QA][57.2] {s}");
    }

    private static void Tick()
    {
        try
        {
            if (_next < _segs.Count)
            {
                ProcessSegment(_next, _segs[_next]);
                _next++;
                return;
            }
            // 첫 예고가 든 세그먼트는 .data 로도 남긴다(Profiler 창에서 바로 연다).
            foreach (int s in _saveSegs.OrderBy(x => x))
            {
                ProfilerDriver.ClearAllFrames();
                if (ProfilerDriver.LoadProfile(_segs[s], false))
                    ProfilerDriver.SaveProfile(Path.ChangeExtension(_segs[s], ".data"));
            }
            WriteOutputs();
            Finish("완료");
        }
        catch (Exception e)
        {
            Finish("예외: " + e);
        }
    }

    private static void Finish(string status)
    {
        EditorApplication.update -= Tick;
        ProfilerDriver.ClearAllFrames();
        if (_frameCountProp != null && _origFrameCount > 0)
            _frameCountProp.SetValue(null, _origFrameCount);
        Status($"{status} (버퍼 {(_frameCountProp != null ? _frameCountProp.GetValue(null) : "?")})");
    }

    private static void ProcessSegment(int seg, string path)
    {
        ProfilerDriver.ClearAllFrames();
        if (!ProfilerDriver.LoadProfile(path, false))
        {
            Status($"seg {seg}: 불러오기 실패 {path}");
            return;
        }
        int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
        int n = 0;
        int start = _rows.Count;
        for (int i = first; i <= last; i++)
        {
            FrameRow row = ReadFrame(seg, i);
            if (row == null)
                continue;
            _rows.Add(row);
            n++;
        }
        if (n > 0)
        {
            _rows[start].Edge = true;
            _rows[_rows.Count - 1].Edge = true;
        }
        Status($"seg {seg}: 프레임 {first}..{last} ({n}개) {Path.GetFileName(path)}");
    }

    private static FrameRow ReadFrame(int seg, int index)
    {
        var row = new FrameRow { Seg = seg, Index = index };
        using (RawFrameDataView raw = ProfilerDriver.GetRawFrameDataView(index, 0))
        {
            if (raw == null || !raw.valid)
                return null;
            NativeArray<int> meta = raw.GetFrameMetaData<int>(QABossProfileProbe.MetaGuid, QABossProfileProbe.MetaTag);
            if (meta.IsCreated && meta.Length >= 2)
            {
                row.Frame = meta[0];
                row.Phase = meta[1];
            }
        }

        using (HierarchyFrameDataView v = ProfilerDriver.GetHierarchyFrameDataView(index, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                                                                                     HierarchyFrameDataView.columnTotalTime, false))
        {
            if (v == null || !v.valid)
                return null;
            row.FrameMs = v.frameTimeMs;
            var kids = new List<int>();
            var stack = new Stack<int>();
            var markers = new List<string>();
            int root = v.GetRootItemID();
            v.GetItemChildren(root, kids);
            foreach (int k in kids) stack.Push(k);
            // 에디터 Play 는 PlayerLoop 가 루트가 아니라 Application.Tick > … > UpdateScene 아래에 있다 — 트리 어디서든 찾는다(바깥 것만).
            while (stack.Count > 0)
            {
                int id = stack.Pop();
                string name = v.GetItemName(id);
                float ms = v.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnTotalTime);
                kids.Clear();
                v.GetItemChildren(id, kids);
                if (name == "PlayerLoop")
                {
                    row.PlayerLoopMs += ms;
                    row.GcAllocKb += v.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnGcMemory) / 1024f;
                }
                else if (name == "GC.Collect") row.GcCollectMs += ms;
                else if (name == "QA57.Segment") markers.Add("Segment");
                else if (name.StartsWith("QA57.")) markers.Add(name.Substring(5));
                else if (name.StartsWith("Diag57."))
                {
                    // 진단 마커(할 일 57.2 진단 커밋) — 호출 자체 ms 와 그 안의 Mono.JIT.
                    string key = name.Substring(7);
                    row.Diag.TryGetValue(key, out float prev);
                    row.Diag[key] = prev + ms;
                    foreach (int k in kids)
                        if (v.GetItemName(k) == "Mono.JIT")
                        {
                            row.Diag.TryGetValue(key + ".JIT", out float j);
                            row.Diag[key + ".JIT"] = j + v.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnTotalTime);
                        }
                }
                foreach (int k in kids) stack.Push(k);
            }
            row.EditorLoopMs = Math.Max(0f, row.FrameMs - row.PlayerLoopMs);
            row.Markers = string.Join("|", markers);

            if (row.Frame >= 0 && _watch.TryGetValue(row.Frame, out string why))
            {
                _watchText[row.Frame] = $"## f{row.Frame} — {why}\n{Header(row)}\n\n{Describe(v, 0.01f, true)}";
                if (why.EndsWith(" F") || why.Contains(" F · "))
                    _saveSegs.Add(seg);
            }
            if (row.Phase == (int)QABossProfileProbe.Phase.Combat)
            {
                OfferTop(_topCombat, row, v);
                OfferTop(_topCombatTotal, row, v, r => r.FrameMs);
            }
            else if (row.Phase == (int)QABossProfileProbe.Phase.Loading)
                OfferTop(_topLoading, row, v);
        }
        return row;
    }

    private static void OfferTop(List<TopFrame> list, FrameRow row, HierarchyFrameDataView v, Func<FrameRow, float> key = null)
    {
        key = key ?? (r => r.PlayerLoopMs);
        if (list.Count >= TopK && key(list[list.Count - 1].Row) >= key(row))
            return;
        list.Add(new TopFrame { Row = row, Text = Describe(v, 0.5f, false) });
        list.Sort((a, b) => key(b.Row).CompareTo(key(a.Row)));
        if (list.Count > TopK)
            list.RemoveAt(list.Count - 1);
    }

    private static string Header(FrameRow r) =>
        $"seg {r.Seg} idx {r.Index} · frame {r.Frame} · phase {(QABossProfileProbe.Phase)Math.Max(0, r.Phase)} · 전체 {r.FrameMs:F2} ms · PlayerLoop {r.PlayerLoopMs:F2} ms · 에디터(전체−PlayerLoop) {r.EditorLoopMs:F2} ms · GC.Collect {r.GcCollectMs:F2} ms · GC Alloc {r.GcAllocKb:F1} KB · 마커 [{r.Markers}]{DiagText(r)}{(r.Edge ? " · 🔸세그먼트 경계(측정 도구)" : "")}";

    private static string DiagText(FrameRow r) =>
        r.Diag.Count == 0 ? "" : " · 진단 " + string.Join(" ", DiagKeys.Where(k => r.Diag.ContainsKey(k)).Select(k => $"{k} {r.DiagMs(k):F3} ms(JIT {r.DiagMs(k + ".JIT"):F3})"));

    /// <summary>자기 시간 상위 5개(경로) + 트리(총 ms ≥ threshold). full=false 면 트리는 깊이 4 까지만.</summary>
    private static string Describe(HierarchyFrameDataView v, float threshold, bool full)
    {
        var self = new List<(float ms, int id)>();
        var tree = new StringBuilder();
        var kids = new List<int>();
        var stack = new Stack<(int id, int depth)>();
        v.GetItemChildren(v.GetRootItemID(), kids);
        for (int i = kids.Count - 1; i >= 0; i--) stack.Push((kids[i], 0));
        while (stack.Count > 0)
        {
            (int id, int depth) = stack.Pop();
            float total = v.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnTotalTime);
            float selfMs = v.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
            self.Add((selfMs, id));
            if (total >= threshold && (full || depth <= 4))
                tree.AppendLine($"{new string(' ', depth * 2)}{v.GetItemName(id)}  {total:F3} ms (self {selfMs:F3}, calls {v.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnCalls):F0}, gc {v.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnGcMemory) / 1024f:F1} KB)");
            kids.Clear();
            v.GetItemChildren(id, kids);
            for (int i = kids.Count - 1; i >= 0; i--) stack.Push((kids[i], depth + 1));
        }
        var sb = new StringBuilder("자기 시간 상위 5:\n");
        var anc = new List<int>();
        foreach ((float ms, int id) in self.OrderByDescending(s => s.ms).Take(5))
        {
            anc.Clear();
            v.GetItemAncestors(id, anc);
            var names = anc.Where(a => a != v.GetRootItemID()).Select(a => v.GetItemName(a)).Reverse().ToList();
            names.Add(v.GetItemName(id));
            sb.AppendLine($"  {ms:F3} ms  {string.Join(" > ", names.Skip(Math.Max(0, names.Count - 4)))}");
        }
        sb.AppendLine("트리:").Append(tree);
        return sb.ToString();
    }

    // ── 출력 ─────────────────────────────────────────────────────

    private static void WriteOutputs()
    {
        var inv = CultureInfo.InvariantCulture;
        var csv = new StringBuilder("seg,idx,frame,phase,frame_ms,player_loop_ms,editor_ms,gc_collect_ms,gc_alloc_kb,qa_markers,seg_edge,diag_show_ms,diag_show_jit_ms,diag_lock_ms,diag_lock_jit_ms,diag_jump_ms,diag_jump_jit_ms\n");
        foreach (FrameRow r in _rows)
            csv.AppendLine(string.Format(inv, "{0},{1},{2},{3},{4:F3},{5:F3},{6:F3},{7:F3},{8:F1},{9},{10},{11:F3},{12:F3},{13:F3},{14:F3},{15:F3},{16:F3}", r.Seg, r.Index, r.Frame, r.Phase, r.FrameMs, r.PlayerLoopMs, r.EditorLoopMs, r.GcCollectMs, r.GcAllocKb, r.Markers, r.Edge ? 1 : 0,
                r.DiagMs("Show"), r.DiagMs("Show.JIT"), r.DiagMs("ClientLock"), r.DiagMs("ClientLock.JIT"), r.DiagMs("CreateJump"), r.DiagMs("CreateJump.JIT")));
        File.WriteAllText(Path.Combine(_dir, "profile-frames.csv"), csv.ToString(), new UTF8Encoding(false));

        // 첫 예고 프레임 덤프
        string dumps = Path.Combine(_dir, "event-dumps");
        Directory.CreateDirectory(dumps);
        var ev = new StringBuilder("# 57.2 첫 예고 프레임 (F = QA 가 LateUpdate 에서 켜짐을 본 프레임 — 호출은 F 또는 F-1)\n\n");
        foreach (KeyValuePair<int, string> kv in _watchText.OrderBy(k => k.Key))
        {
            File.WriteAllText(Path.Combine(dumps, $"f{kv.Key}.txt"), kv.Value, new UTF8Encoding(false));
            FrameRow r = _rows.FirstOrDefault(x => x.Frame == kv.Key);
            if (r != null)
                ev.AppendLine($"- {_watch[kv.Key]}: {Header(r)} → `event-dumps/f{kv.Key}.txt`");
        }
        ev.AppendLine().AppendLine("## 모든 발생(관찰 프레임 F 의 전체·PlayerLoop ms, F-1 도)");
        foreach (Ev e in _events.Where(x => IsWatchKind(x.Kind)))
        {
            FrameRow f0 = _rows.FirstOrDefault(x => x.Frame == e.Frame), fm = _rows.FirstOrDefault(x => x.Frame == e.Frame - 1);
            ev.AppendLine($"- f{e.Frame} {e.Kind} {e.Detail}: F {(f0 != null ? $"{f0.FrameMs:F2}/{f0.PlayerLoopMs:F2}{DiagText(f0)}" : "-")} ms · F-1 {(fm != null ? $"{fm.FrameMs:F2}/{fm.PlayerLoopMs:F2}{DiagText(fm)}" : "-")} ms");
        }

        // 진단 마커 호출 목록(Play 전체 — 로딩 예열 포함, 프레임 순)
        ev.AppendLine().AppendLine("## 진단 마커가 든 프레임(Diag57.*, 호출 ms(JIT) — 전체/PlayerLoop)");
        foreach (FrameRow r in _rows.Where(x => x.Diag.Count > 0))
            ev.AppendLine($"- f{r.Frame} {(QABossProfileProbe.Phase)Math.Max(0, r.Phase)}: {r.FrameMs:F2}/{r.PlayerLoopMs:F2} ms{DiagText(r)}");
        File.WriteAllText(Path.Combine(_dir, "events-analysis.md"), ev.ToString(), new UTF8Encoding(false));

        var top = new StringBuilder("# 57.2 상위 프레임\n\n## 보스전(Combat) — 전체 프레임 ms 기준(에디터 포함)\n\n");
        foreach (TopFrame t in _topCombatTotal) top.AppendLine($"### {Header(t.Row)}\n```\n{t.Text}```\n");
        top.AppendLine("## 보스전(Combat) — PlayerLoop ms 기준(게임 코드)\n");
        foreach (TopFrame t in _topCombat) top.AppendLine($"### {Header(t.Row)}\n```\n{t.Text}```\n");
        top.AppendLine("## 로딩(Loading)\n");
        foreach (TopFrame t in _topLoading) top.AppendLine($"### {Header(t.Row)}\n```\n{t.Text}```\n");
        File.WriteAllText(Path.Combine(_dir, "top-frames.md"), top.ToString(), new UTF8Encoding(false));

        var sum = new StringBuilder("# 57.2 분포 요약\n\n");
        foreach (int phase in new[] { (int)QABossProfileProbe.Phase.Combat, (int)QABossProfileProbe.Phase.Loading, (int)QABossProfileProbe.Phase.BossIntro, (int)QABossProfileProbe.Phase.Map })
        {
            var rs = _rows.Where(r => r.Phase == phase).ToList();
            sum.AppendLine($"## {(QABossProfileProbe.Phase)phase} — 프로파일 프레임 {rs.Count}개");
            sum.AppendLine(Stats("전체(frameTimeMs, EditorLoop 포함)", rs.Select(r => r.FrameMs).ToList()));
            sum.AppendLine(Stats("PlayerLoop", rs.Select(r => r.PlayerLoopMs).ToList()));
            sum.AppendLine(Stats("전체 — 세그먼트 첫·끝 제외", rs.Where(r => !r.Edge).Select(r => r.FrameMs).ToList()));
            sum.AppendLine(Stats("PlayerLoop — 세그먼트 첫·끝 제외", rs.Where(r => !r.Edge).Select(r => r.PlayerLoopMs).ToList()));
            sum.AppendLine($"- GC.Collect 든 프레임 {rs.Count(r => r.GcCollectMs > 0)}개 · 세그먼트 전환 프레임 {rs.Count(r => r.Markers.Contains("Segment"))}개");
            sum.AppendLine();
        }
        sum.AppendLine("## 런타임 frames.csv(레코더, 직전 프레임 값) — Combat");
        string framesPath = Path.Combine(_dir, "frames.csv");
        if (File.Exists(framesPath))
        {
            var dt = new List<float>(); var main = new List<float>(); var pl = new List<float>();
            foreach (string line in File.ReadAllLines(framesPath).Skip(1))
            {
                string[] c = line.Split(',');
                if (c.Length < 8 || c[2] != ((int)QABossProfileProbe.Phase.Combat).ToString()) continue;
                if (float.TryParse(c[3], NumberStyles.Float, inv, out float a)) dt.Add(a);
                if (float.TryParse(c[4], NumberStyles.Float, inv, out float b)) main.Add(b);
                if (float.TryParse(c[5], NumberStyles.Float, inv, out float p)) pl.Add(p);
            }
            sum.AppendLine(Stats("unscaledDeltaTime", dt));
            sum.AppendLine(Stats("Main Thread 레코더", main));
            sum.AppendLine(Stats("PlayerLoop 레코더", pl));
        }
        File.WriteAllText(Path.Combine(_dir, "summary.md"), sum.ToString(), new UTF8Encoding(false));
    }

    private static string Stats(string label, List<float> xs)
    {
        if (xs.Count == 0)
            return $"- {label}: 없음";
        var s = xs.OrderBy(x => x).ToList();
        float P(double q) => s[Math.Min(s.Count - 1, (int)Math.Ceiling(q * s.Count) - 1)];
        return string.Format(CultureInfo.InvariantCulture,
            "- {0}: n={1} 평균 {2:F2} · p50 {3:F2} · p95 {4:F2} · p99 {5:F2} · 최대 {6:F2} ms · ≥16.7ms {7}개 · ≥33.3ms {8}개",
            label, s.Count, s.Average(), P(0.5), P(0.95), P(0.99), s[s.Count - 1], s.Count(x => x >= 16.7f), s.Count(x => x >= 33.3f));
    }
}
#endif
