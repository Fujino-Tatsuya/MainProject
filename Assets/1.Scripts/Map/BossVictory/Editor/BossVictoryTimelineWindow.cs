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
    [SerializeField] bool _initialFit;

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
        Undo.undoRedoPerformed += OnUndoRedo;
        wantsMouseMove = false;
    }

    void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
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
        if (!_initialFit && Event.current.type == EventType.Layout)
        {
            _initialFit = true;   // 처음 열 때 전체 보기 — 로비 전환 마커(끝)까지 한 화면에
            FitAll();
        }

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
        AddCursorRects(lanes);
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
    // 위 = 마커 줄(로비 전환·카메라·숨김 — 첫 화면에 바로 보이게), 아래 = 클립 트랙.
    static int MarkerRow(BossVictoryMarker marker) => (int)marker;
    static int TrackRow(BossVictoryTrack track) => BossVictoryTimeline.Markers.Length + (int)track;

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
        foreach (BossVictoryMarker marker in BossVictoryTimeline.Markers)
        {
            DrawRowBackground(labels, lanes, row);
            GUI.Label(new Rect(labels.x + 6f, labels.y + row * RowHeight + 4f, LabelWidth - 8f, RowHeight),
                "◆ " + BossVictoryTimeline.MarkerLabel(marker), MarkerLabelStyle(marker));
            row++;
        }
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

    static readonly Color LobbyColor = new Color(0.35f, 0.95f, 0.55f);

    static Color MarkerTint(BossVictoryMarker marker) => marker == BossVictoryMarker.LobbyTransition ? LobbyColor : MarkerColor;

    static GUIStyle s_markerLabel, s_lobbyLabel;

    static GUIStyle MarkerLabelStyle(BossVictoryMarker marker)
    {
        s_markerLabel ??= new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = MarkerColor } };
        s_lobbyLabel ??= new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = LobbyColor } };
        return marker == BossVictoryMarker.LobbyTransition ? s_lobbyLabel : s_markerLabel;
    }

    static string MarkerText(BossVictoryTimeline tl, BossVictoryMarker marker) =>
        $"{BossVictoryTimeline.MarkerLabel(marker)} {tl.MarkerTime(marker):0.00}s";

    // 마커 = 줄 전체를 지나는 세로선 + 큰 마름모 + "이름 시각s" 라벨(상시). 화면 밖이면 그 줄 끝에 ◀/▶ 라벨을 붙여 둔다(누르면 그쪽으로 이동).
    void DrawMarkers(Rect lanes)
    {
        GUI.BeginClip(lanes);
        Rect local = new Rect(0f, 0f, lanes.width, lanes.height);
        foreach (BossVictoryMarker marker in BossVictoryTimeline.Markers)
        {
            bool lobby = marker == BossVictoryMarker.LobbyTransition;
            float x = TimeToX(local, Timeline.MarkerTime(marker));
            Color c = IsSelectedMarker(marker) ? Color.white : MarkerTint(marker);
            Rect row = RowRect(local, MarkerRow(marker));
            string text = MarkerText(Timeline, marker);
            GUIStyle style = MarkerLabelStyle(marker);
            float textWidth = style.CalcSize(new GUIContent(text)).x;
            if (x < 0f || x > local.width)
            {
                string arrow = x < 0f ? "◀ " + text : text + " ▶";
                float w = textWidth + 18f;
                Rect pin = x < 0f ? new Rect(2f, row.y + 3f, w, row.height - 6f) : new Rect(local.width - w - 2f, row.y + 3f, w, row.height - 6f);
                EditorGUI.DrawRect(pin, new Color(0f, 0f, 0f, 0.55f));
                GUI.Label(pin, arrow, style);
                continue;
            }
            float lineWidth = lobby ? 2.5f : 1.5f;
            EditorGUI.DrawRect(new Rect(x - lineWidth * 0.5f, 0f, lineWidth, local.height), new Color(c.r, c.g, c.b, lobby ? 0.8f : 0.45f));
            Vector3 mid = new Vector3(x, row.center.y, 0f);
            float s = lobby ? MarkerSizeLobby : MarkerSize;
            Handles.color = c;
            Handles.DrawAAConvexPolygon(mid + Vector3.up * s, mid + Vector3.right * s, mid + Vector3.down * s, mid + Vector3.left * s);
            Rect label = MarkerLabelRect(row, x, textWidth, local.width);
            EditorGUI.DrawRect(label, new Color(0f, 0f, 0f, 0.55f));
            GUI.Label(label, text, style);
        }
        GUI.EndClip();
    }

    const float MarkerSize = 7f;
    const float MarkerSizeLobby = 10f;

    // 라벨은 마름모 오른쪽, 오른쪽 끝에 닿으면 왼쪽.
    static Rect MarkerLabelRect(Rect row, float x, float textWidth, float width)
    {
        float w = textWidth + 8f;
        float lx = x + MarkerSizeLobby + 3f;
        if (lx + w > width) lx = x - MarkerSizeLobby - 3f - w;
        return new Rect(lx, row.y + 3f, w, row.height - 6f);
    }

    // 마커 잡기 — 마름모 ±(크기+4)px 또는 라벨 위. 화면 밖 ◀/▶ 라벨을 누르면 그 마커가 보이게 옮긴다.
    bool PickMarker(Rect lanes, Vector2 mouse, out BossVictoryMarker picked)
    {
        foreach (BossVictoryMarker marker in BossVictoryTimeline.Markers)
        {
            picked = marker;
            Rect row = RowRect(lanes, MarkerRow(marker));
            if (!row.Contains(mouse)) continue;
            float x = TimeToX(lanes, Timeline.MarkerTime(marker));
            if (x < lanes.x || x > lanes.xMax)
            {
                _scrollTime = Mathf.Max(0f, Timeline.MarkerTime(marker) - lanes.width * 0.5f / _pixelsPerSecond);
                Repaint();
                return false;
            }
            float textWidth = MarkerLabelStyle(marker).CalcSize(new GUIContent(MarkerText(Timeline, marker))).x;
            Rect label = MarkerLabelRect(new Rect(lanes.x, row.y, lanes.width, row.height), x, textWidth, lanes.xMax);
            if (Mathf.Abs(mouse.x - x) <= MarkerSizeLobby + 4f || label.Contains(mouse)) return true;
        }
        picked = default;
        return false;
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
    // 끌기 = 이동 · 양 끝(좌우 화살표 커서 — 왼쪽 = 시작 이동·끝 고정, 오른쪽 = 길이) · 선택 클립 위쪽 작은 네모 = 페이드 · 우클릭 = 추가/복사/붙여넣기/삭제 · Del · Ctrl+C/V · 0.01초 스냅 · Undo.

    enum DragMode { None, Move, ResizeLeft, ResizeRight, FadeIn, FadeOut, Marker, Playhead }

    const float MinLength = 0.01f;
    const float HandleSize = 7f;

    [SerializeField] bool _snap = true;
    int _selTrack = -1;
    int _selIndex = -1;
    int _selMarker = -1;
    DragMode _drag;
    BossVictoryClip _dragOrig;
    float _dragMouseT0;
    bool _dragRecorded;
    float _dragMarkerOrig;
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
        float xIn = FadeInX(r, clip);
        float xOut = FadeOutX(r, clip);
        EditorGUI.DrawRect(new Rect(xIn - HandleSize * 0.5f, r.y, HandleSize, HandleSize), Color.white);
        EditorGUI.DrawRect(new Rect(xOut - HandleSize * 0.5f, r.y, HandleSize, HandleSize), Color.white);
    }

    // 편집 한 번 = Undo 한 단계(새 묶음). 끌기는 시작~끝을 한 묶음(BeginDragUndo·RecordDrag·EndDragUndo).
    void Record(string label)
    {
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(label);
        Undo.RecordObject(_settings, label);
    }

    // 수치 칸 — 칸을 고치는 동안(슬라이더 끌기 포함)은 Unity 가 이벤트마다 묶음을 나눈다. 새 묶음을 억지로 만들지 않는다.
    void RecordField(string label)
    {
        Undo.RecordObject(_settings, label);
    }

    int _dragUndoGroup = -1;

    void RecordDrag()
    {
        if (_dragUndoGroup < 0)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("처치 연출 타임라인 끌기");
            _dragUndoGroup = Undo.GetCurrentGroup();
        }
        Undo.RecordObject(_settings, "처치 연출 타임라인 끌기");
    }

    void EndDragUndo()
    {
        if (_dragUndoGroup >= 0) Undo.CollapseUndoOperations(_dragUndoGroup);
        _dragUndoGroup = -1;
    }

    // Ctrl+Z / Ctrl+Y · Ctrl+Shift+Z — 창에 포커스가 있고 수치 칸 입력 중이 아닐 때(입력 중이면 칸이 글자 되돌리기를 쓴다).
    static bool TryUndoRedoKey(Event e)
    {
        if (e.type != EventType.KeyDown || !(e.control || e.command) || GUIUtility.keyboardControl != 0) return false;
        if (e.keyCode == KeyCode.Z && !e.shift) Undo.PerformUndo();
        else if (e.keyCode == KeyCode.Y || (e.keyCode == KeyCode.Z && e.shift)) Undo.PerformRedo();
        else return false;
        return true;
    }

    // 되돌리기/다시 하기 뒤 — 선택 범위 보정 · 저장 · 다시 그림 · Play 중이면 연출이 바로 새 값으로 다시 평가되게 한 프레임 돌림.
    void OnUndoRedo()
    {
        if (_settings == null) return;
        if (_selTrack >= 0 && !HasClipSelection) Select(-1, -1, -1);
        if (_selMarker >= BossVictoryTimeline.Markers.Length) Select(-1, -1, -1);
        _drag = DragMode.None;
        _dragRecorded = false;
        _dragUndoGroup = -1;
        Save();
        if (EditorApplication.isPlaying) EditorApplication.QueuePlayerLoopUpdate();
        Repaint();
    }

    // SO 에 바로 저장(Play 끝나도 유지 — Q5).
    void Save()
    {
        EditorUtility.SetDirty(_settings);
        AssetDatabase.SaveAssetIfDirty(_settings);
    }

    void HandleEdit(Rect lanes, Rect ruler)
    {
        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        Event e = Event.current;
        if (TryUndoRedoKey(e))
        {
            e.Use();
            return;
        }
        switch (e.type)
        {
            case EventType.MouseDown when e.button == 0:
                if (ruler.Contains(e.mousePosition))
                {
                    _drag = DragMode.Playhead;
                    GUIUtility.hotControl = controlId;
                    SetPlayhead(XToTime(lanes, e.mousePosition.x));
                    e.Use();
                }
                else if (lanes.Contains(e.mousePosition))
                {
                    BeginDrag(lanes, e.mousePosition);
                    if (_drag != DragMode.None) GUIUtility.hotControl = controlId;   // 창 밖으로 끌어도 놓칠 때까지 이 창이 받는다
                    e.Use();
                }
                break;
            case EventType.MouseDrag when e.button == 0 && _drag != DragMode.None:
                ApplyDrag(lanes, e.mousePosition);
                e.Use();
                break;
            case EventType.MouseUp when e.button == 0 && _drag != DragMode.None:
                if (_dragRecorded) Save();
                EndDragUndo();
                _drag = DragMode.None;
                if (GUIUtility.hotControl == controlId) GUIUtility.hotControl = 0;
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
        if (PickMarker(lanes, mouse, out BossVictoryMarker picked))
        {
            Select(-1, -1, (int)picked);
            _dragMarkerOrig = Timeline.MarkerTime(picked);
            _drag = DragMode.Marker;
            return;
        }
        foreach (BossVictoryTrack track in BossVictoryTimeline.Tracks)
        {
            if (!RowRect(lanes, TrackRow(track)).Contains(mouse)) continue;
            if (PickClip(lanes, track, mouse, out int index, out DragMode mode))
            {
                Select((int)track, index, -1);
                _dragOrig = Timeline.Clips(track)[index];
                _drag = mode;
                return;
            }
            break;
        }
        Select(-1, -1, -1);
        _drag = DragMode.None;
    }

    // 클립 손잡이 고르기 — 순서 = ① 선택 클립 위쪽 띠의 페이드 네모 ② 가장 가까운 끝(몸통보다 먼저, 이웃 클립 포함) ③ 몸통.
    // 끝 잡는 폭 = 클립 밖 EdgeOuter + 안쪽 클립 폭의 30%(EdgeInnerMin~EdgeInnerMax) — 짧은 클립·줌 아웃에서도 잡힌다.
    // 페이드 네모는 선택한 클립의 위쪽 띠에서만(띠 아래는 끝이 이긴다) — 페이드 0 이라 네모가 끝에 붙어 있어도 서로 안 막는다.
    bool PickClip(Rect lanes, BossVictoryTrack track, Vector2 mouse, out int index, out DragMode mode)
    {
        List<BossVictoryClip> clips = Timeline.Clips(track);
        int row = TrackRow(track);
        index = -1;
        mode = DragMode.None;

        if (IsFadeStrip(track, mouse, lanes, out Rect sr))
        {
            BossVictoryClip sel = clips[_selIndex];
            float dIn = Mathf.Abs(mouse.x - FadeInX(sr, sel));
            float dOut = Mathf.Abs(mouse.x - FadeOutX(sr, sel));
            if (Mathf.Min(dIn, dOut) <= HandleSize)
            {
                index = _selIndex;
                mode = dIn <= dOut ? DragMode.FadeIn : DragMode.FadeOut;
                return true;
            }
        }

        float best = float.MaxValue;
        for (int i = 0; i < clips.Count; i++)
        {
            Rect r = ClipRect(lanes, row, clips[i]);
            float inner = EdgeInner(r);
            float dl = mouse.x - r.x, dr = r.xMax - mouse.x;
            if (dl >= -EdgeOuter && dl <= inner && Mathf.Abs(dl) < best) { best = Mathf.Abs(dl); index = i; mode = DragMode.ResizeLeft; }
            if (dr >= -EdgeOuter && dr <= inner && Mathf.Abs(dr) < best) { best = Mathf.Abs(dr); index = i; mode = DragMode.ResizeRight; }
        }
        if (index >= 0) return true;

        for (int i = clips.Count - 1; i >= 0; i--)
        {
            if (!ClipRect(lanes, row, clips[i]).Contains(mouse)) continue;
            index = i;
            mode = DragMode.Move;
            return true;
        }
        return false;
    }

    const float EdgeOuter = 6f;
    const float EdgeInnerMin = 4f;
    const float EdgeInnerMax = 10f;

    static float EdgeInner(Rect r) => Mathf.Clamp(r.width * 0.3f, EdgeInnerMin, EdgeInnerMax);
    static float FadeInX(Rect r, BossVictoryClip c) => r.x + c.fadeIn / Mathf.Max(MinLength, c.length) * r.width;
    static float FadeOutX(Rect r, BossVictoryClip c) => r.xMax - c.fadeOut / Mathf.Max(MinLength, c.length) * r.width;

    // 선택한 페이드 있는 클립의 위쪽 띠(페이드 네모 줄) 안인가.
    bool IsFadeStrip(BossVictoryTrack track, Vector2 mouse, Rect lanes, out Rect clipRect)
    {
        clipRect = default;
        if (_selTrack != (int)track || !HasClipSelection || !BossVictoryTimeline.HasFades(track)) return false;
        clipRect = ClipRect(lanes, TrackRow(track), Timeline.Clips(track)[_selIndex]);
        return mouse.y <= clipRect.y + HandleSize + 1f && mouse.x >= clipRect.x - HandleSize && mouse.x <= clipRect.xMax + HandleSize;
    }

    // 마우스 커서 — 끝 = 좌우 화살표 · 몸통 = 이동 · 페이드 네모 = 밀기.
    void AddCursorRects(Rect lanes)
    {
        foreach (BossVictoryMarker marker in BossVictoryTimeline.Markers)
        {
            Rect row = RowRect(lanes, MarkerRow(marker));
            float x = TimeToX(lanes, Timeline.MarkerTime(marker));
            AddCursor(lanes, new Rect(x - MarkerSizeLobby - 4f, row.y, (MarkerSizeLobby + 4f) * 2f, row.height), MouseCursor.ResizeHorizontal);
        }
        foreach (BossVictoryTrack track in BossVictoryTimeline.Tracks)
        {
            List<BossVictoryClip> clips = Timeline.Clips(track);
            bool fadeStripRow = _selTrack == (int)track && HasClipSelection && BossVictoryTimeline.HasFades(track);
            for (int i = 0; i < clips.Count; i++)
            {
                Rect r = ClipRect(lanes, TrackRow(track), clips[i]);
                float inner = EdgeInner(r);
                float top = r.y, height = r.height;
                if (fadeStripRow && i == _selIndex)
                {
                    float strip = HandleSize + 1f;
                    AddCursor(lanes, new Rect(FadeInX(r, clips[i]) - HandleSize, r.y, HandleSize * 2f, strip), MouseCursor.SlideArrow);
                    AddCursor(lanes, new Rect(FadeOutX(r, clips[i]) - HandleSize, r.y, HandleSize * 2f, strip), MouseCursor.SlideArrow);
                    top += strip;
                    height -= strip;
                }
                AddCursor(lanes, new Rect(r.x + inner, r.y, Mathf.Max(0f, r.width - inner * 2f), r.height), MouseCursor.MoveArrow);
                AddCursor(lanes, new Rect(r.x - EdgeOuter, top, EdgeOuter + inner, height), MouseCursor.ResizeHorizontal);
                AddCursor(lanes, new Rect(r.xMax - inner, top, EdgeOuter + inner, height), MouseCursor.ResizeHorizontal);
            }
        }
    }

    static void AddCursor(Rect lanes, Rect r, MouseCursor cursor)
    {
        float x0 = Mathf.Max(r.x, lanes.x), x1 = Mathf.Min(r.xMax, lanes.xMax);
        if (x1 <= x0 || r.height <= 0f) return;
        EditorGUIUtility.AddCursorRect(new Rect(x0, r.y, x1 - x0, r.height), cursor);
    }

    void ApplyDrag(Rect lanes, Vector2 mouse)
    {
        float mt = XToTime(lanes, mouse.x);
        if (_drag == DragMode.Playhead)
        {
            SetPlayhead(mt);
            return;
        }
        RecordDrag();   // 매 끌기 이벤트 기록 — 손을 떼면 한 묶음으로 합친다
        _dragRecorded = true;
        if (_drag == DragMode.Marker)
        {
            Timeline.SetMarkerTime((BossVictoryMarker)_selMarker, Snap(_dragMarkerOrig + (mt - _dragMouseT0)));   // 라벨을 잡아도 튀지 않게 이동량으로
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

    static string MarkerHelp(BossVictoryMarker marker) => marker switch
    {
        BossVictoryMarker.LobbyTransition =>
            "실게임: 호스트 화면이 이 시각을 지나면 로비 결과 패널로 넘어간다(TV 꺼짐 클립이 없으면 검정 없이). " +
            "디버그 씬: 이 시각이 한 바퀴 끝 — 로비로 가지 않고 랩 '반복' 대기(loopHoldSeconds) 뒤 처음부터.",
        BossVictoryMarker.FocusKiller => "처치자가 아닌 피어의 카메라가 이 시각에 처치자로 즉시 넘어간다(처치자 화면 그대로).",
        BossVictoryMarker.HideOthers => "이 시각에 처치자 외 플레이어 캐릭터를 모든 화면에서 숨긴다.",
        _ => "",
    };

    // 선택 클립·마커 수치 직접 입력
    void DrawInspector()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            _snap = EditorGUILayout.ToggleLeft("0.01초 스냅", _snap, GUILayout.Width(100f));
            GUILayout.Label("휠 = 줌 · 가운데 버튼 끌기 = 이동 · 클립 양 끝 끌기 = 길이 · 우클릭 = 추가/복사/붙여넣기/삭제 · Del · Ctrl+C/V/D · Ctrl+Z 되돌리기 · Ctrl+Y·Ctrl+Shift+Z 다시 하기", EditorStyles.miniLabel);
        }
        if (_selMarker >= 0)
        {
            var marker = (BossVictoryMarker)_selMarker;
            EditorGUILayout.LabelField("마커", BossVictoryTimeline.MarkerLabel(marker), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(MarkerHelp(marker), EditorStyles.wordWrappedMiniLabel);
            EditorGUI.BeginChangeCheck();
            float time = EditorGUILayout.FloatField("시각(초)", Timeline.MarkerTime(marker));
            if (EditorGUI.EndChangeCheck())
            {
                RecordField("처치 연출 타임라인 마커 수치");
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
            RecordField("처치 연출 타임라인 클립 수치");
            Timeline.Clips(track)[_selIndex] = Clamp(c);
            Save();
        }
    }
}
