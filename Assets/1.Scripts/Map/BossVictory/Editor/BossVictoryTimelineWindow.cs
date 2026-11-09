using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 타임라인 창(PLAN-boss-victory-glitch "타임라인 에디터 창") — 영상 편집기처럼 트랙별 클립·순간 마커를 본다/고친다.
/// 데이터 = <see cref="BossVictoryGlitchSettings.timeline"/>(런타임과 같은 SO). Unity Timeline 패키지는 쓰지 않는다(Q4).
/// </summary>
public sealed class BossVictoryTimelineWindow : EditorWindow
{
    const string MenuPath = "Tools/Boss Victory/처치 연출 타임라인";
    const string DefaultAssetPath = "Assets/Resources/BossVictory/BossVictoryGlitchSettings.asset";

    const float LabelWidth = 130f;
    const float RulerHeight = 22f;
    const float RowHeight = 24f;
    const float MinPixelsPerSecond = 30f;
    const float MaxPixelsPerSecond = 2000f;

    static readonly Color RowA = new Color(0.20f, 0.20f, 0.20f);
    static readonly Color RowB = new Color(0.17f, 0.17f, 0.17f);
    static readonly Color RangeColor = new Color(0.30f, 0.55f, 0.85f);
    static readonly Color PulseColor = new Color(0.90f, 0.55f, 0.25f);
    static readonly Color SingleColor = new Color(0.55f, 0.45f, 0.80f);
    static readonly Color MarkerColor = new Color(0.95f, 0.85f, 0.30f);
    static readonly Color EnvelopeColor = new Color(1f, 1f, 1f, 0.85f);

    [SerializeField] BossVictoryGlitchSettings _settings;
    [SerializeField] float _pixelsPerSecond = 160f;
    [SerializeField] float _scrollTime;
    [SerializeField] float _playhead;

    [MenuItem(MenuPath)]
    static void Open()
    {
        var w = GetWindow<BossVictoryTimelineWindow>();
        w.titleContent = new GUIContent("처치 연출 타임라인");
        w.minSize = new Vector2(520f, 360f);
        w.Show();
    }

    void OnEnable()
    {
        if (_settings == null) _settings = AssetDatabase.LoadAssetAtPath<BossVictoryGlitchSettings>(DefaultAssetPath);
        Undo.undoRedoPerformed += Repaint;
        wantsMouseMove = false;
    }

    void OnDisable()
    {
        Undo.undoRedoPerformed -= Repaint;
    }

    BossVictoryTimeline Timeline => _settings != null ? _settings.timeline : null;

    void OnGUI()
    {
        DrawToolbar();
        if (_settings == null)
        {
            EditorGUILayout.HelpBox($"설정 SO 가 없다 — {DefaultAssetPath}", MessageType.Warning);
            return;
        }
        _settings.timeline ??= BossVictoryTimeline.CreateDefault();

        int rows = BossVictoryTimeline.Tracks.Length + BossVictoryTimeline.Markers.Length;
        Rect area = GUILayoutUtility.GetRect(position.width, RulerHeight + rows * RowHeight + 16f);
        Rect labels = new Rect(area.x, area.y + RulerHeight, LabelWidth, rows * RowHeight);
        Rect lanes = new Rect(area.x + LabelWidth, area.y + RulerHeight, area.width - LabelWidth, rows * RowHeight);
        Rect ruler = new Rect(lanes.x, area.y, lanes.width, RulerHeight);
        Rect scroll = new Rect(lanes.x, lanes.yMax, lanes.width, 16f);

        HandleView(lanes, ruler);
        HandleEdit(lanes, ruler);
        if (!HasClipSelection) _selIndex = -1;   // Undo 로 목록이 줄었으면
        DrawRows(labels, lanes);
        DrawRuler(ruler);
        DrawTrackClips(lanes);
        DrawMarkers(lanes);
        DrawPlayhead(ruler, lanes);
        DrawScrollbar(scroll);
        DrawInspector();
    }

    // ── 도구 막대 ─────────────────────────────────────────────────────────

    void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            _settings = (BossVictoryGlitchSettings)EditorGUILayout.ObjectField(_settings, typeof(BossVictoryGlitchSettings), false,
                GUILayout.Width(240f));
            GUILayout.Space(8f);
            GUILayout.Label("줌", GUILayout.Width(20f));
            _pixelsPerSecond = GUILayout.HorizontalSlider(_pixelsPerSecond, MinPixelsPerSecond, MaxPixelsPerSecond, GUILayout.Width(140f));
            if (GUILayout.Button("전체 보기", EditorStyles.toolbarButton, GUILayout.Width(64f))) FitAll();
            GUILayout.FlexibleSpace();
            DrawPreviewControls();
            GUILayout.Label($"재생 헤드 {_playhead:0.00}초", EditorStyles.miniLabel, GUILayout.Width(90f));
        }
    }

    void FitAll()
    {
        if (Timeline == null) return;
        float width = Mathf.Max(100f, position.width - LabelWidth - 20f);
        _pixelsPerSecond = Mathf.Clamp(width / Mathf.Max(0.5f, Timeline.Duration + 0.3f), MinPixelsPerSecond, MaxPixelsPerSecond);
        _scrollTime = 0f;
        Repaint();
    }

    // ── 시간 ↔ 픽셀 ──────────────────────────────────────────────────────

    float TimeToX(Rect lanes, float t) => lanes.x + (t - _scrollTime) * _pixelsPerSecond;
    float XToTime(Rect lanes, float x) => _scrollTime + (x - lanes.x) / _pixelsPerSecond;

    Rect RowRect(Rect lanes, int row) => new Rect(lanes.x, lanes.y + row * RowHeight, lanes.width, RowHeight);
    static int TrackRow(BossVictoryTrack track) => (int)track;
    static int MarkerRow(BossVictoryMarker marker) => BossVictoryTimeline.Tracks.Length + (int)marker;

    Rect ClipRect(Rect lanes, int row, BossVictoryClip clip)
    {
        Rect r = RowRect(lanes, row);
        float x0 = TimeToX(lanes, clip.start), x1 = TimeToX(lanes, clip.End);
        return new Rect(x0, r.y + 2f, Mathf.Max(2f, x1 - x0), r.height - 4f);
    }

    // 휠 = 마우스 위치 기준 줌 · 가운데 버튼 끌기 = 이동
    void HandleView(Rect lanes, Rect ruler)
    {
        Event e = Event.current;
        Rect view = new Rect(ruler.x, ruler.y, lanes.width, ruler.height + lanes.height);
        if (e.type == EventType.ScrollWheel && view.Contains(e.mousePosition))
        {
            float anchor = XToTime(lanes, e.mousePosition.x);
            float scale = e.delta.y > 0f ? 1f / 1.12f : 1.12f;
            _pixelsPerSecond = Mathf.Clamp(_pixelsPerSecond * scale, MinPixelsPerSecond, MaxPixelsPerSecond);
            _scrollTime = Mathf.Max(0f, anchor - (e.mousePosition.x - lanes.x) / _pixelsPerSecond);
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && e.button == 2 && view.Contains(e.mousePosition))
        {
            _scrollTime = Mathf.Max(0f, _scrollTime - e.delta.x / _pixelsPerSecond);
            e.Use();
        }
    }

    // ── 그리기 ───────────────────────────────────────────────────────────

    void DrawRows(Rect labels, Rect lanes)
    {
        int row = 0;
        foreach (BossVictoryTrack track in BossVictoryTimeline.Tracks)
        {
            DrawRowBackground(labels, lanes, row);
            string kind = BossVictoryTimeline.KindOf(track) switch
            {
                BossVictoryTimeline.TrackKind.Pulse => " ·펄스",
                BossVictoryTimeline.TrackKind.Single => " ·1개",
                _ => "",
            };
            GUI.Label(new Rect(labels.x + 6f, labels.y + row * RowHeight + 4f, LabelWidth - 8f, RowHeight),
                BossVictoryTimeline.TrackLabel(track) + kind, EditorStyles.miniLabel);
            row++;
        }
        foreach (BossVictoryMarker marker in BossVictoryTimeline.Markers)
        {
            DrawRowBackground(labels, lanes, row);
            GUI.Label(new Rect(labels.x + 6f, labels.y + row * RowHeight + 4f, LabelWidth - 8f, RowHeight),
                "◆ " + BossVictoryTimeline.MarkerLabel(marker), EditorStyles.miniLabel);
            row++;
        }
    }

    void DrawRowBackground(Rect labels, Rect lanes, int row)
    {
        Color c = row % 2 == 0 ? RowA : RowB;
        EditorGUI.DrawRect(new Rect(labels.x, labels.y + row * RowHeight, labels.width, RowHeight), c * 0.9f);
        EditorGUI.DrawRect(RowRect(lanes, row), c);
    }

    void DrawRuler(Rect ruler)
    {
        EditorGUI.DrawRect(ruler, new Color(0.13f, 0.13f, 0.13f));
        float step = RulerStep();
        float t0 = Mathf.Floor(_scrollTime / step) * step;
        float t1 = _scrollTime + ruler.width / _pixelsPerSecond;
        for (float t = t0; t <= t1; t += step)
        {
            float x = ruler.x + (t - _scrollTime) * _pixelsPerSecond;
            if (x < ruler.x) continue;
            EditorGUI.DrawRect(new Rect(x, ruler.yMax - 8f, 1f, 8f), Color.gray);
            GUI.Label(new Rect(x + 2f, ruler.y, 60f, 14f), t.ToString(step < 0.1f ? "0.00" : "0.0"), EditorStyles.miniLabel);
        }
    }

    // 눈금 간격 = 대략 70px 이상 되는 0.01·0.05·0.1·0.5·1 초
    float RulerStep()
    {
        float[] steps = { 0.01f, 0.05f, 0.1f, 0.25f, 0.5f, 1f, 2f };
        foreach (float s in steps)
            if (s * _pixelsPerSecond >= 70f) return s;
        return 5f;
    }

    void DrawTrackClips(Rect lanes)
    {
        GUI.BeginClip(lanes);
        Rect local = new Rect(0f, 0f, lanes.width, lanes.height);
        foreach (BossVictoryTrack track in BossVictoryTimeline.Tracks)
        {
            List<BossVictoryClip> clips = Timeline.Clips(track);
            for (int i = 0; i < clips.Count; i++) DrawClip(local, track, i, clips[i]);
        }
        GUI.EndClip();
    }

    void DrawClip(Rect lanes, BossVictoryTrack track, int index, BossVictoryClip clip)
    {
        Rect r = ClipRect(lanes, TrackRow(track), clip);
        Color baseColor = BossVictoryTimeline.KindOf(track) switch
        {
            BossVictoryTimeline.TrackKind.Pulse => PulseColor,
            BossVictoryTimeline.TrackKind.Single => SingleColor,
            _ => track == BossVictoryTrack.Hold ? SingleColor : RangeColor,
        };
        bool selected = IsSelected(track, index);
        EditorGUI.DrawRect(r, baseColor * (selected ? 1f : 0.75f));
        if (BossVictoryTimeline.HasFades(track)) DrawEnvelope(r, clip);
        if (selected) DrawOutline(r, Color.white);
        DrawClipDecorations(r, track, clip, selected);
    }

    // 클립 값 모양(페이드·세기) — 아래 = 0, 위 = 1
    static void DrawEnvelope(Rect r, BossVictoryClip clip)
    {
        const int n = 32;
        var pts = new Vector3[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float t = clip.start + clip.length * i / n;
            float v = clip.Evaluate(t);
            pts[i] = new Vector3(r.x + r.width * i / n, r.yMax - 1f - v * (r.height - 2f), 0f);
        }
        Handles.color = EnvelopeColor;
        Handles.DrawAAPolyLine(2f, pts);
    }

    static void DrawOutline(Rect r, Color c)
    {
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), c);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), c);
        EditorGUI.DrawRect(new Rect(r.x, r.y, 1f, r.height), c);
        EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), c);
    }

    void DrawMarkers(Rect lanes)
    {
        GUI.BeginClip(lanes);
        Rect local = new Rect(0f, 0f, lanes.width, lanes.height);
        foreach (BossVictoryMarker marker in BossVictoryTimeline.Markers)
        {
            float x = TimeToX(local, Timeline.MarkerTime(marker));
            Color c = IsSelectedMarker(marker) ? Color.white : MarkerColor;
            EditorGUI.DrawRect(new Rect(x - 0.5f, 0f, 1f, local.height), new Color(c.r, c.g, c.b, 0.35f));
            Rect row = RowRect(local, MarkerRow(marker));
            Vector3 mid = new Vector3(x, row.center.y, 0f);
            Handles.color = c;
            Handles.DrawAAConvexPolygon(mid + Vector3.up * 7f, mid + Vector3.right * 6f, mid + Vector3.down * 7f, mid + Vector3.left * 6f);
            GUI.Label(new Rect(x + 8f, row.y + 4f, 80f, 16f), Timeline.MarkerTime(marker).ToString("0.00"), EditorStyles.miniLabel);
        }
        GUI.EndClip();
    }

    void DrawPlayhead(Rect ruler, Rect lanes)
    {
        float x = TimeToX(lanes, _playhead);
        if (x < lanes.x || x > lanes.xMax) return;
        EditorGUI.DrawRect(new Rect(x - 0.5f, ruler.y, 1.5f, ruler.height + lanes.height), new Color(1f, 0.3f, 0.3f));
    }

    void DrawScrollbar(Rect r)
    {
        float visible = r.width / _pixelsPerSecond;
        float total = Mathf.Max(Timeline.Duration + 1f, _scrollTime + visible);
        _scrollTime = GUI.HorizontalScrollbar(r, _scrollTime, visible, 0f, total);
    }

    // ── 선택·편집 ────────────────────────────────────────────────────────
    // 끌기 = 이동 · 양 끝 = 길이 · 위쪽 작은 네모 = 페이드 · 우클릭 = 추가/복사/붙여넣기/삭제 · Del · Ctrl+C/V · 0.01초 스냅 · Undo.

    enum DragMode { None, Move, ResizeLeft, ResizeRight, FadeIn, FadeOut, Marker, Playhead }

    const float MinLength = 0.01f;
    const float EdgeGrab = 5f;
    const float HandleSize = 7f;

    [SerializeField] bool _snap = true;
    int _selTrack = -1;
    int _selIndex = -1;
    int _selMarker = -1;
    DragMode _drag;
    BossVictoryClip _dragOrig;
    float _dragMouseT0;
    bool _dragRecorded;
    static bool s_hasClipboard;
    static BossVictoryClip s_clipboard;
    static BossVictoryTrack s_clipboardTrack;

    float Snap(float t) => _snap ? Mathf.Round(t * 100f) / 100f : t;

    bool IsSelected(BossVictoryTrack track, int index) => _selTrack == (int)track && _selIndex == index;
    bool IsSelectedMarker(BossVictoryMarker marker) => _selMarker == (int)marker;
    bool HasClipSelection => _selTrack >= 0 && _selIndex >= 0 && _selIndex < Timeline.Clips((BossVictoryTrack)_selTrack).Count;

    void Select(int track, int index, int marker)
    {
        _selTrack = track;
        _selIndex = index;
        _selMarker = marker;
        GUI.FocusControl(null);
    }

    void DrawClipDecorations(Rect r, BossVictoryTrack track, BossVictoryClip clip, bool selected)
    {
        if (!selected || !BossVictoryTimeline.HasFades(track)) return;
        float xIn = r.x + clip.fadeIn / Mathf.Max(MinLength, clip.length) * r.width;
        float xOut = r.xMax - clip.fadeOut / Mathf.Max(MinLength, clip.length) * r.width;
        EditorGUI.DrawRect(new Rect(xIn - HandleSize * 0.5f, r.y, HandleSize, HandleSize), Color.white);
        EditorGUI.DrawRect(new Rect(xOut - HandleSize * 0.5f, r.y, HandleSize, HandleSize), Color.white);
    }

    void Record(string label)
    {
        Undo.RecordObject(_settings, label);
    }

    // SO 에 바로 저장(Play 끝나도 유지 — Q5).
    void Save()
    {
        EditorUtility.SetDirty(_settings);
        AssetDatabase.SaveAssetIfDirty(_settings);
    }

    void HandleEdit(Rect lanes, Rect ruler)
    {
        Event e = Event.current;
        switch (e.type)
        {
            case EventType.MouseDown when e.button == 0:
                if (ruler.Contains(e.mousePosition))
                {
                    _drag = DragMode.Playhead;
                    SetPlayhead(XToTime(lanes, e.mousePosition.x));
                    e.Use();
                }
                else if (lanes.Contains(e.mousePosition))
                {
                    BeginDrag(lanes, e.mousePosition);
                    e.Use();
                }
                break;
            case EventType.MouseDrag when e.button == 0 && _drag != DragMode.None:
                ApplyDrag(lanes, e.mousePosition);
                e.Use();
                break;
            case EventType.MouseUp when e.button == 0 && _drag != DragMode.None:
                if (_dragRecorded) Save();
                _drag = DragMode.None;
                _dragRecorded = false;
                e.Use();
                break;
            case EventType.ContextClick when lanes.Contains(e.mousePosition):
                ShowContextMenu(lanes, e.mousePosition);
                e.Use();
                break;
            case EventType.ValidateCommand when IsEditCommand(e.commandName) && GUIUtility.keyboardControl == 0:
                e.Use();
                break;
            case EventType.ExecuteCommand when IsEditCommand(e.commandName) && GUIUtility.keyboardControl == 0:
                ExecuteCommand(e.commandName);
                e.Use();
                break;
            case EventType.KeyDown when e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace:
                if (HasClipSelection && GUIUtility.keyboardControl == 0)
                {
                    DeleteSelected();
                    e.Use();
                }
                break;
        }
    }

    static bool IsEditCommand(string name) => name == "Copy" || name == "Paste" || name == "Delete" || name == "SoftDelete" || name == "Duplicate";

    void ExecuteCommand(string name)
    {
        switch (name)
        {
            case "Copy": CopySelected(); break;
            case "Paste":
                PasteAt(_selTrack >= 0 ? (BossVictoryTrack)_selTrack : s_clipboardTrack, _playhead);
                break;
            case "Duplicate":
                if (HasClipSelection)
                {
                    CopySelected();
                    BossVictoryClip c = Timeline.Clips((BossVictoryTrack)_selTrack)[_selIndex];
                    PasteAt((BossVictoryTrack)_selTrack, c.End);
                }
                break;
            default: DeleteSelected(); break;
        }
    }

    void BeginDrag(Rect lanes, Vector2 mouse)
    {
        _dragRecorded = false;
        _dragMouseT0 = XToTime(lanes, mouse.x);
        foreach (BossVictoryMarker marker in BossVictoryTimeline.Markers)
        {
            Rect row = RowRect(lanes, MarkerRow(marker));
            if (!row.Contains(mouse) || Mathf.Abs(mouse.x - TimeToX(lanes, Timeline.MarkerTime(marker))) > HandleSize + 1f) continue;
            Select(-1, -1, (int)marker);
            _drag = DragMode.Marker;
            return;
        }
        foreach (BossVictoryTrack track in BossVictoryTimeline.Tracks)
        {
            if (!RowRect(lanes, TrackRow(track)).Contains(mouse)) continue;
            List<BossVictoryClip> clips = Timeline.Clips(track);
            for (int i = clips.Count - 1; i >= 0; i--)
            {
                Rect r = ClipRect(lanes, TrackRow(track), clips[i]);
                if (mouse.x < r.x - EdgeGrab || mouse.x > r.xMax + EdgeGrab) continue;
                Select((int)track, i, -1);
                _dragOrig = clips[i];
                _drag = PickClipMode(track, r, clips[i], mouse);
                return;
            }
            break;
        }
        Select(-1, -1, -1);
        _drag = DragMode.None;
    }

    DragMode PickClipMode(BossVictoryTrack track, Rect r, BossVictoryClip clip, Vector2 mouse)
    {
        if (BossVictoryTimeline.HasFades(track) && mouse.y <= r.y + HandleSize + 1f)
        {
            float len = Mathf.Max(MinLength, clip.length);
            float xIn = r.x + clip.fadeIn / len * r.width;
            float xOut = r.xMax - clip.fadeOut / len * r.width;
            if (Mathf.Abs(mouse.x - xIn) <= HandleSize) return DragMode.FadeIn;
            if (Mathf.Abs(mouse.x - xOut) <= HandleSize) return DragMode.FadeOut;
        }
        if (Mathf.Abs(mouse.x - r.x) <= EdgeGrab) return DragMode.ResizeLeft;
        if (Mathf.Abs(mouse.x - r.xMax) <= EdgeGrab) return DragMode.ResizeRight;
        return DragMode.Move;
    }

    void ApplyDrag(Rect lanes, Vector2 mouse)
    {
        float mt = XToTime(lanes, mouse.x);
        if (_drag == DragMode.Playhead)
        {
            SetPlayhead(mt);
            return;
        }
        if (!_dragRecorded)
        {
            Record("처치 연출 타임라인 끌기");
            _dragRecorded = true;
        }
        if (_drag == DragMode.Marker)
        {
            Timeline.SetMarkerTime((BossVictoryMarker)_selMarker, Snap(mt));
            Repaint();
            return;
        }
        if (!HasClipSelection) return;
        BossVictoryClip o = _dragOrig, c = o;
        float d = mt - _dragMouseT0;
        switch (_drag)
        {
            case DragMode.Move:
                c.start = Mathf.Max(0f, Snap(o.start + d));
                break;
            case DragMode.ResizeLeft:
                c.start = Mathf.Clamp(Snap(o.start + d), 0f, o.End - MinLength);
                c.length = o.End - c.start;
                break;
            case DragMode.ResizeRight:
                c.length = Mathf.Max(MinLength, Snap(o.End + d) - o.start);
                break;
            case DragMode.FadeIn:
                c.fadeIn = Mathf.Clamp(Snap(mt - o.start), 0f, o.length);
                break;
            case DragMode.FadeOut:
                c.fadeOut = Mathf.Clamp(Snap(o.End - mt), 0f, o.length);
                break;
        }
        Timeline.Clips((BossVictoryTrack)_selTrack)[_selIndex] = Clamp(c);
        Repaint();
    }

    static BossVictoryClip Clamp(BossVictoryClip c)
    {
        c.start = Mathf.Max(0f, c.start);
        c.length = Mathf.Max(MinLength, c.length);
        c.fadeIn = Mathf.Clamp(c.fadeIn, 0f, c.length);
        c.fadeOut = Mathf.Clamp(c.fadeOut, 0f, c.length);
        c.weight = Mathf.Clamp01(c.weight);
        return c;
    }

    void ShowContextMenu(Rect lanes, Vector2 mouse)
    {
        float t = Mathf.Max(0f, Snap(XToTime(lanes, mouse.x)));
        var menu = new GenericMenu();
        foreach (BossVictoryMarker marker in BossVictoryTimeline.Markers)
        {
            if (!RowRect(lanes, MarkerRow(marker)).Contains(mouse)) continue;
            BossVictoryMarker m = marker;
            menu.AddItem(new GUIContent($"여기({t:0.00}초)로 옮기기"), false, () => MoveMarker(m, t));
            menu.AddItem(new GUIContent($"재생 헤드({_playhead:0.00}초)로 옮기기"), false, () => MoveMarker(m, _playhead));
            menu.ShowAsContext();
            return;
        }
        foreach (BossVictoryTrack track in BossVictoryTimeline.Tracks)
        {
            if (!RowRect(lanes, TrackRow(track)).Contains(mouse)) continue;
            BossVictoryTrack tr = track;
            List<BossVictoryClip> clips = Timeline.Clips(track);
            int hit = -1;
            for (int i = clips.Count - 1; i >= 0 && hit < 0; i--)
                if (ClipRect(lanes, TrackRow(track), clips[i]).Contains(mouse)) hit = i;
            if (hit >= 0)
            {
                Select((int)track, hit, -1);
                menu.AddItem(new GUIContent("복사"), false, CopySelected);
                menu.AddItem(new GUIContent("삭제"), false, DeleteSelected);
                menu.AddSeparator("");
            }
            if (CanAdd(track)) menu.AddItem(new GUIContent($"클립 추가 ({t:0.00}초)"), false, () => AddClip(tr, t));
            else menu.AddDisabledItem(new GUIContent("클립 추가 (TV 꺼짐은 1개만)"));
            if (s_hasClipboard && CanAdd(track)) menu.AddItem(new GUIContent($"붙여넣기 ({t:0.00}초)"), false, () => PasteAt(tr, t));
            else menu.AddDisabledItem(new GUIContent("붙여넣기"));
            menu.ShowAsContext();
            return;
        }
    }

    bool CanAdd(BossVictoryTrack track) =>
        BossVictoryTimeline.KindOf(track) != BossVictoryTimeline.TrackKind.Single || Timeline.Clips(track).Count == 0;

    static BossVictoryClip NewClip(BossVictoryTrack track, float start) => BossVictoryTimeline.KindOf(track) switch
    {
        BossVictoryTimeline.TrackKind.Pulse => BossVictoryClip.Make(start, 0.2f, 0.1f, 0.1f),
        BossVictoryTimeline.TrackKind.Single => BossVictoryClip.Make(start, 1f),
        _ => track == BossVictoryTrack.Hold ? BossVictoryClip.Make(start, 1f) : BossVictoryClip.Make(start, 1f, 0.1f, 0.1f),
    };

    void AddClip(BossVictoryTrack track, float start)
    {
        if (!CanAdd(track)) return;
        Record("처치 연출 타임라인 클립 추가");
        List<BossVictoryClip> clips = Timeline.Clips(track);
        clips.Add(NewClip(track, start));
        Select((int)track, clips.Count - 1, -1);
        Save();
        Repaint();
    }

    void MoveMarker(BossVictoryMarker marker, float t)
    {
        Record("처치 연출 타임라인 마커 옮기기");
        Timeline.SetMarkerTime(marker, Snap(t));
        Select(-1, -1, (int)marker);
        Save();
        Repaint();
    }

    void CopySelected()
    {
        if (!HasClipSelection) return;
        s_clipboardTrack = (BossVictoryTrack)_selTrack;
        s_clipboard = Timeline.Clips(s_clipboardTrack)[_selIndex];
        s_hasClipboard = true;
    }

    void PasteAt(BossVictoryTrack track, float start)
    {
        if (!s_hasClipboard || !CanAdd(track)) return;
        Record("처치 연출 타임라인 붙여넣기");
        BossVictoryClip c = s_clipboard;
        c.start = Mathf.Max(0f, Snap(start));
        List<BossVictoryClip> clips = Timeline.Clips(track);
        clips.Add(Clamp(c));
        Select((int)track, clips.Count - 1, -1);
        Save();
        Repaint();
    }

    void DeleteSelected()
    {
        if (!HasClipSelection) return;
        Record("처치 연출 타임라인 클립 삭제");
        Timeline.Clips((BossVictoryTrack)_selTrack).RemoveAt(_selIndex);
        Select(-1, -1, -1);
        Save();
        Repaint();
    }

    void SetPlayhead(float t)
    {
        _playhead = Mathf.Max(0f, Snap(t));
        PreviewScrub();
        Repaint();
    }

    // ── 스크럽 미리보기(Q5 — Play 중 디버그 씬) ───────────────────────────
    // 재생 헤드를 끌면 랩 일시정지(Time.timeScale 0) + 연출 시계를 그 시각으로 → 그 시각 화면으로 고정. ▶ = 이어서 재생 · ⏮ = 랩 처음부터(F7).
    // 움직이는 것 = 타임라인이 정하는 것(화면 효과·HUD·마커·TV 꺼짐). 처치자 순서·애니 배율은 애니 이벤트라 대상 아님.

    static bool PreviewAvailable => EditorApplication.isPlaying && DebugBossVictoryLab.Instance != null;

    void Update()
    {
        if (!EditorApplication.isPlaying) return;
        BossVictoryGlitchPresentation p = BossVictoryGlitchPresentation.Instance;
        if (p != null && _drag != DragMode.Playhead && p.GlitchTime >= 0f) _playhead = p.GlitchTime;
        Repaint();
    }

    void DrawPreviewControls()
    {
        if (!EditorApplication.isPlaying)
        {
            GUILayout.Label("미리보기 = Play 중 디버그 씬(DebugBossVictory)", EditorStyles.miniLabel);
            return;
        }
        DebugBossVictoryLab lab = DebugBossVictoryLab.Instance;
        using (new EditorGUI.DisabledScope(lab == null))
        {
            if (GUILayout.Button("⏮ 처음부터", EditorStyles.toolbarButton, GUILayout.Width(72f))) lab.Replay();
            bool paused = lab != null && lab.Paused;
            if (GUILayout.Button(paused ? "▶ 재생" : "❚❚ 일시정지", EditorStyles.toolbarButton, GUILayout.Width(72f))) lab.Paused = !paused;
        }
        if (lab == null) GUILayout.Label("랩 없음 — 디버그 씬에서 Play", EditorStyles.miniLabel);
    }

    void PreviewScrub()
    {
        if (!PreviewAvailable) return;
        BossVictoryGlitchPresentation p = BossVictoryGlitchPresentation.Instance;
        if (p == null || !p.PreviewSeek(_playhead)) return;
        DebugBossVictoryLab.Instance.Paused = true;
    }

    // 선택 클립·마커 수치 직접 입력
    void DrawInspector()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            _snap = EditorGUILayout.ToggleLeft("0.01초 스냅", _snap, GUILayout.Width(100f));
            GUILayout.Label("휠 = 줌 · 가운데 버튼 끌기 = 이동 · 우클릭 = 추가/복사/붙여넣기/삭제 · Del · Ctrl+C/V/D", EditorStyles.miniLabel);
        }
        if (_selMarker >= 0)
        {
            var marker = (BossVictoryMarker)_selMarker;
            EditorGUILayout.LabelField("마커", BossVictoryTimeline.MarkerLabel(marker), EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            float time = EditorGUILayout.FloatField("시각(초)", Timeline.MarkerTime(marker));
            if (EditorGUI.EndChangeCheck())
            {
                Record("처치 연출 타임라인 마커 수치");
                Timeline.SetMarkerTime(marker, time);
                Save();
            }
            return;
        }
        if (!HasClipSelection)
        {
            EditorGUILayout.LabelField("선택 없음 — 클립이나 마커를 누르면 수치를 고칠 수 있다.", EditorStyles.miniLabel);
            return;
        }
        var track = (BossVictoryTrack)_selTrack;
        BossVictoryClip c = Timeline.Clips(track)[_selIndex];
        EditorGUILayout.LabelField("클립", $"{BossVictoryTimeline.TrackLabel(track)} #{_selIndex + 1}", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        c.start = EditorGUILayout.FloatField("시작(초)", c.start);
        c.length = EditorGUILayout.FloatField("길이(초)", c.length);
        EditorGUILayout.LabelField("끝(초)", c.End.ToString("0.00"));
        if (BossVictoryTimeline.HasFades(track))
        {
            c.fadeIn = EditorGUILayout.FloatField("페이드 인(초)", c.fadeIn);
            c.fadeOut = EditorGUILayout.FloatField("페이드 아웃(초)", c.fadeOut);
            c.weight = EditorGUILayout.Slider("세기", c.weight, 0f, 1f);
        }
        if (EditorGUI.EndChangeCheck())
        {
            Record("처치 연출 타임라인 클립 수치");
            Timeline.Clips(track)[_selIndex] = Clamp(c);
            Save();
        }
    }
}
