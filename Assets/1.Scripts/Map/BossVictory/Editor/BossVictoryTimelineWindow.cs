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

    const float LabelWidth = 160f;
    const float RulerHeight = 22f;
    const float RowHeight = 24f;
    const float MinPixelsPerSecond = 30f;
    const float MaxPixelsPerSecond = 2000f;

    static readonly Color RowA = new Color(0.20f, 0.20f, 0.20f);
    static readonly Color RowB = new Color(0.17f, 0.17f, 0.17f);
    static readonly Color RangeColor = new Color(0.30f, 0.55f, 0.85f);
    static readonly Color PulseColor = new Color(0.90f, 0.55f, 0.25f);
    static readonly Color SingleColor = new Color(0.55f, 0.45f, 0.80f);
    static readonly Color CutColor = new Color(0.30f, 0.65f, 0.50f);
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
        if (Event.current.type == EventType.Layout && Timeline.SyncCameraCutShots()) Save();   // 컷 값 개수 = 컷 클립 개수(손으로 고친 에셋 보정)
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
        HandleLabelClick(labels);
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

    // 줄 이름 누르기 = 그 노드 선택(트랙 = 모양 값 · 마커 = 시각·옵션) — 255 후속 3.
    void HandleLabelClick(Rect labels)
    {
        Event e = Event.current;
        if (e.type != EventType.MouseDown || e.button != 0 || !labels.Contains(e.mousePosition)) return;
        int row = Mathf.FloorToInt((e.mousePosition.y - labels.y) / RowHeight);
        int markers = BossVictoryTimeline.Markers.Length;
        if (row < markers) Select(-1, -1, row);
        else if (row - markers < BossVictoryTimeline.Tracks.Length) Select(row - markers, -1, -1);
        else return;
        e.Use();
        Repaint();
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
                BossVictoryTimeline.TrackKind.Cut => " ·다음 컷까지",
                _ => "",
            };
            if (_selTrack == (int)track && _selIndex < 0)
                EditorGUI.DrawRect(new Rect(labels.x, labels.y + row * RowHeight, 3f, RowHeight), Color.white);
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
            BossVictoryTimeline.TrackKind.Cut => CutColor,
            _ => RangeColor,
        };
        bool selected = IsSelected(track, index);
        EditorGUI.DrawRect(r, baseColor * (selected ? 1f : 0.75f));
        if (BossVictoryTimeline.HasFades(track)) DrawEnvelope(r, clip);
        if (track == BossVictoryTrack.CameraCut)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, 2f, r.height), Color.white);   // 컷 = 시작 순간
            BossVictoryCameraCuts.Cut shot = Timeline.CameraCutShot(index);
            GUI.Label(new Rect(r.x + 4f, r.y + 4f, Mathf.Max(0f, r.width - 6f), r.height - 4f),
                $"#{index + 1} {AnchorLabel(shot.anchor)}{(shot.hasEnd ? " → 이동" : " 고정")}{(shot.hideWalls ? "" : " · 벽 숨김 끔")}", EditorStyles.miniLabel);
        }
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
        marker == BossVictoryMarker.SlowStart && tl.MarkerTime(marker) <= 0f
            ? $"{BossVictoryTimeline.MarkerLabel(marker)} (끔 — 스킬 End)"
            : $"{BossVictoryTimeline.MarkerLabel(marker)} {tl.MarkerTime(marker):0.00}s";

    static readonly string[] AnchorLabels = { "가운데", "처치자", "보스" };
    static readonly string[] EaseLabels = { "선형", "ease in/out", "ease in", "ease out" };

    static string AnchorLabel(BossVictoryCameraCuts.Anchor anchor) =>
        (int)anchor >= 0 && (int)anchor < AnchorLabels.Length ? AnchorLabels[(int)anchor] : anchor.ToString();

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
    static BossVictoryCameraCuts.Cut s_clipboardShot;

    float Snap(float t) => _snap ? Mathf.Round(t * 100f) / 100f : t;

    bool IsSelected(BossVictoryTrack track, int index) => _selTrack == (int)track && _selIndex == index;
    bool IsSelectedMarker(BossVictoryMarker marker) => _selMarker == (int)marker;
    bool HasClipSelection => _selTrack >= 0 && _selIndex >= 0 && _selIndex < Timeline.Clips((BossVictoryTrack)_selTrack).Count;

    bool _selFinisher;   // '처치 연출 스킬 End' 줄 노드(시간 배율 칸)

    void Select(int track, int index, int marker)
    {
        _selTrack = track;
        _selIndex = index;
        _selMarker = marker;
        _selFinisher = false;
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
        if (_selIndex >= 0 && !HasClipSelection) Select(-1, -1, -1);
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
        Timeline.SyncCameraCutShots();
        Timeline.FitCameraCutLengths();   // 컷 길이 = 다음 컷 시작까지(표시용 — 판정은 시작만)
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
        bool resizable = BossVictoryTimeline.KindOf(track) != BossVictoryTimeline.TrackKind.Cut;   // 컷 길이 = 다음 컷 시작까지(자동)
        for (int i = 0; resizable && i < clips.Count; i++)
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
            bool resizable = BossVictoryTimeline.KindOf(track) != BossVictoryTimeline.TrackKind.Cut;
            for (int i = 0; i < clips.Count; i++)
            {
                Rect r = ClipRect(lanes, TrackRow(track), clips[i]);
                if (!resizable)
                {
                    AddCursor(lanes, r, MouseCursor.MoveArrow);
                    continue;
                }
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
        Timeline.Clips((BossVictoryTrack)_selTrack)[_selIndex] = Clamp((BossVictoryTrack)_selTrack, c);
        Repaint();
    }

    // 처치자 평타 = 시작 0 고정(끝만 의미) — 옮기기·왼쪽 끝 끌기·입력 모두 끝을 남기고 시작을 0 으로.
    static BossVictoryClip Clamp(BossVictoryTrack track, BossVictoryClip c)
    {
        c = Clamp(c);
        if (!BossVictoryTimeline.StartFixed(track)) return c;
        float end = c.End;
        c.start = 0f;
        c.length = Mathf.Max(MinLength, end);
        c.fadeIn = c.fadeOut = 0f;
        c.weight = 1f;
        return c;
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
            else menu.AddDisabledItem(new GUIContent("클립 추가 (이 트랙은 1개만)"));
            if (s_hasClipboard && CanAdd(track)) menu.AddItem(new GUIContent($"붙여넣기 ({t:0.00}초)"), false, () => PasteAt(tr, t));
            else menu.AddDisabledItem(new GUIContent("붙여넣기"));
            menu.ShowAsContext();
            return;
        }
    }

    bool CanAdd(BossVictoryTrack track) =>
        BossVictoryTimeline.KindOf(track) != BossVictoryTimeline.TrackKind.Single || Timeline.Clips(track).Count == 0;

    static BossVictoryClip NewClip(BossVictoryTrack track, float start) => BossVictoryTimeline.StartFixed(track)
        ? BossVictoryClip.Make(0f, Mathf.Max(MinLength, start))   // 처치자 평타 — 누른 시각 = 끝
        : BossVictoryTimeline.KindOf(track) switch
    {
        BossVictoryTimeline.TrackKind.Pulse => BossVictoryClip.Make(start, 0.2f, 0.1f, 0.1f),
        BossVictoryTimeline.TrackKind.Single or BossVictoryTimeline.TrackKind.Cut => BossVictoryClip.Make(start, 1f),
        _ => BossVictoryClip.Make(start, 1f, 0.1f, 0.1f),
    };

    void AddClip(BossVictoryTrack track, float start)
    {
        if (!CanAdd(track)) return;
        Record("처치 연출 타임라인 클립 추가");
        List<BossVictoryClip> clips = Timeline.Clips(track);
        clips.Add(NewClip(track, start));
        if (track == BossVictoryTrack.CameraCut) Timeline.cameraCutShots.Add(BossVictoryCameraCuts.Cut.Default);
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
        s_clipboardShot = s_clipboardTrack == BossVictoryTrack.CameraCut ? Timeline.CameraCutShot(_selIndex) : BossVictoryCameraCuts.Cut.Default;
        s_hasClipboard = true;
    }

    void PasteAt(BossVictoryTrack track, float start)
    {
        if (!s_hasClipboard || !CanAdd(track)) return;
        Record("처치 연출 타임라인 붙여넣기");
        BossVictoryClip c = s_clipboard;
        c.start = Mathf.Max(0f, Snap(start));
        List<BossVictoryClip> clips = Timeline.Clips(track);
        clips.Add(Clamp(track, c));
        if (track == BossVictoryTrack.CameraCut)
        {
            Timeline.SyncCameraCutShots();   // 모자란 앞 번호 보정 뒤 이 클립 값
            Timeline.cameraCutShots[clips.Count - 1] = s_clipboardTrack == BossVictoryTrack.CameraCut ? s_clipboardShot : BossVictoryCameraCuts.Cut.Default;
        }
        Select((int)track, clips.Count - 1, -1);
        Save();
        Repaint();
    }

    void DeleteSelected()
    {
        if (!HasClipSelection) return;
        Record("처치 연출 타임라인 클립 삭제");
        Timeline.Clips((BossVictoryTrack)_selTrack).RemoveAt(_selIndex);
        if (_selTrack == (int)BossVictoryTrack.CameraCut && _selIndex < Timeline.cameraCutShots.Count) Timeline.cameraCutShots.RemoveAt(_selIndex);
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
        RememberSubjects();
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
        BossVictoryMarker.BackDashStart =>
            "이 시각에 처치자가 백대쉬한다 — 하던 동작이 남아 있으면 끊고, 먼저 끝났으면 그 사이 대기 자세. 스킬 연출 시작보다 늦으면 건너뛴다. " +
            "처치자 순서 노드는 각 피어가 자기 화면 시계로 앞으로만 판정 — 스크럽으로 되돌아가지 않는다(다시 보려면 ⏮).",
        BossVictoryMarker.FinisherStart =>
            "이 시각에 처치 연출 스킬을 표시용으로 바로 튼다 — 백대쉬가 덜 끝났으면 끊고, 처치 순간 쓰던 스킬이 남아 있어도 기다리지 않는다. " +
            "기본 = 백대쉬 시작 + 백대쉬 시간 + 백대쉬 후 대기. 앞으로만(⏮ 로 다시 보기).",
        BossVictoryMarker.SlowStart =>
            "이 시각부터 처치자·보스가 End 후 배율(아래 칸)로 느려진다. 0 = 끔 — 처치 연출 스킬 클립의 End 이벤트에서(255 후속 3 까지와 같음). 앞으로만(⏮ 로 다시 보기).",
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
        DrawFinisherEnd();
        using (new EditorGUILayout.HorizontalScope())
        {
            string reason = SceneViewBlockReason(out _, out _);
            using (new EditorGUI.DisabledScope(reason != null))
                if (GUILayout.Button($"Scene 뷰로 새 카메라 컷 ({_playhead:0.00}초)", GUILayout.Width(220f))) AddCutFromSceneView();
            if (reason != null) GUILayout.Label(reason, EditorStyles.miniLabel);
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
            if (marker == BossVictoryMarker.SlowStart && Timeline.MarkerTime(marker) > 0f && GUILayout.Button("끄기 (0 = 스킬 End)", GUILayout.Width(160f)))
            {
                Record("처치 연출 타임라인 마커 끄기");
                Timeline.SetMarkerTime(marker, 0f);
                Save();
            }
            DrawMarkerOptions(marker);
            return;
        }
        if (_selFinisher)
        {
            DrawTimeScales();
            return;
        }
        if (_selTrack >= 0 && !HasClipSelection)
        {
            var only = (BossVictoryTrack)_selTrack;
            EditorGUILayout.LabelField("트랙", BossVictoryTimeline.TrackLabel(only), EditorStyles.boldLabel);
            DrawTrackShape(only);
            return;
        }
        if (!HasClipSelection)
        {
            EditorGUILayout.LabelField("선택 없음 — 클립·마커·트랙 이름·'시간 배율' 을 누르면 수치를 고칠 수 있다.", EditorStyles.miniLabel);
            return;
        }
        var track = (BossVictoryTrack)_selTrack;
        if (track == BossVictoryTrack.CameraCut)
        {
            DrawCutInspector();
            DrawTrackShape(track);
            return;
        }
        BossVictoryClip c = Timeline.Clips(track)[_selIndex];
        EditorGUILayout.LabelField("클립", $"{BossVictoryTimeline.TrackLabel(track)} #{_selIndex + 1}", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        if (BossVictoryTimeline.StartFixed(track))
        {
            EditorGUILayout.LabelField("시작(초)", "0 고정(처치 순간)");
            c.length = EditorGUILayout.FloatField("끝(초) = 동작 끊는 시각", c.length);
        }
        else
        {
            c.start = EditorGUILayout.FloatField("시작(초)", c.start);
            c.length = EditorGUILayout.FloatField("길이(초)", c.length);
            EditorGUILayout.LabelField("끝(초)", c.End.ToString("0.00"));
        }
        if (BossVictoryTimeline.HasFades(track))
        {
            c.fadeIn = EditorGUILayout.FloatField("페이드 인(초)", c.fadeIn);
            c.fadeOut = EditorGUILayout.FloatField("페이드 아웃(초)", c.fadeOut);
            c.weight = EditorGUILayout.Slider("세기", c.weight, 0f, 1f);
        }
        if (EditorGUI.EndChangeCheck())
        {
            RecordField("처치 연출 타임라인 클립 수치");
            Timeline.Clips(track)[_selIndex] = Clamp(track, c);
            Save();
        }
        DrawTrackShape(track);
    }

    // 카메라 컷 클립 — 시작(= 컷 시작)·기준·시작/끝 화면·곡선·벽 숨김. 길이는 다음 컷 시작까지 자동(255 후속 2).
    void DrawCutInspector()
    {
        BossVictoryClip c = Timeline.cameraCut[_selIndex];
        Timeline.SyncCameraCutShots();
        BossVictoryCameraCuts.Cut shot = Timeline.cameraCutShots[_selIndex];
        EditorGUILayout.LabelField("클립", $"카메라 컷 #{_selIndex + 1}", EditorStyles.boldLabel);
        DrawSceneViewButtons(shot);

        EditorGUI.BeginChangeCheck();
        c.start = Mathf.Max(0f, EditorGUILayout.FloatField("시작(초)", c.start));
        EditorGUILayout.LabelField("구간 끝(초)", $"{Timeline.CameraCutEnd(_selIndex):0.00} — 다음 컷 시작(마지막 컷 = 로비 전환)까지 자동");
        shot.anchor = (BossVictoryCameraCuts.Anchor)EditorGUILayout.Popup(new GUIContent("기준",
                "화면을 이 점 + 처치자→보스 수평 축 좌표계로 저장 — 어디서 죽든 같은 구도. 바꾸면 저장된 화면은 새 기준으로 다시 읽힌다(Scene 뷰로 다시 찍기 권장)."),
            (int)shot.anchor, AnchorLabels);
        EditorGUILayout.LabelField("시작 화면", EditorStyles.miniBoldLabel);
        shot.startPosition = EditorGUILayout.Vector3Field("위치(기준 m)", shot.startPosition);
        shot.startRotation = Quaternion.Euler(EditorGUILayout.Vector3Field("회전(기준 °)", shot.startRotation.eulerAngles));
        shot.startFov = EditorGUILayout.Slider("FOV", shot.startFov, 10f, 90f);
        shot.hasEnd = EditorGUILayout.Toggle(new GUIContent("끝 화면 (이동)", "켜면 컷 구간 동안 시작 → 끝 화면으로 이동. 끄면 고정 컷."), shot.hasEnd);
        using (new EditorGUI.DisabledScope(!shot.hasEnd))
        {
            shot.endPosition = EditorGUILayout.Vector3Field("위치(기준 m)", shot.endPosition);
            shot.endRotation = Quaternion.Euler(EditorGUILayout.Vector3Field("회전(기준 °)", shot.endRotation.eulerAngles));
            shot.endFov = EditorGUILayout.Slider("FOV", shot.endFov, 10f, 90f);
            shot.ease = (BossVictoryCameraCuts.Ease)EditorGUILayout.Popup("이동 곡선", (int)shot.ease, EaseLabels);
        }
        shot.hideWalls = EditorGUILayout.Toggle(new GUIContent("벽 숨김",
            "켜면 이 컷 카메라를 가리는 벽을 끈다(이동 중이면 매 프레임 그 위치로). 끄면 이 컷 동안 벽 숨김 없음 — 앞 컷에서 숨겼던 벽도 다시 보인다."), shot.hideWalls);
        if (EditorGUI.EndChangeCheck())
        {
            RecordField("처치 연출 타임라인 카메라 컷 수치");
            Timeline.cameraCut[_selIndex] = c;
            Timeline.cameraCutShots[_selIndex] = shot;
            Save();
        }
    }

    // ── 'Scene 뷰로' — 지금 보고 있는 Scene 뷰 화면을 컷 화면으로(255 후속 2) ──
    // 저장 = 기준 좌표계(기준점 + 처치자→보스 축). 그 기준이 되는 처치자·보스 위치:
    // Play 중 연출이 돌면 지금 컷이 잡은 위치(컷 전이면 지금 위치) · 아니면 이 에디터 세션에서 마지막으로 본 회차 위치(SessionState). 없으면 버튼을 막는다.

    const string SubjectsKey = "BossVictoryTimeline.LastSubjects";

    static bool TryGetSubjects(out BossVictoryCameraCuts.Subjects s, out string source)
    {
        BossVictoryGlitchPresentation p = EditorApplication.isPlaying ? BossVictoryGlitchPresentation.Instance : null;
        if (p != null && p.TryGetCutSubjects(out s))
        {
            source = "지금 회차";
            return true;
        }
        s = default;
        source = "";
        if (!SessionState.GetBool(SubjectsKey, false)) return false;
        s.Killer = SessionState.GetVector3(SubjectsKey + ".Killer", Vector3.zero);
        s.BossBase = SessionState.GetVector3(SubjectsKey + ".Boss", Vector3.forward);
        source = "마지막 회차";
        return true;
    }

    // Play 중 연출이 돌면 기준 위치를 기억해 둔다(Play 를 끈 뒤에도 같은 기준으로 찍게).
    static void RememberSubjects()
    {
        BossVictoryGlitchPresentation p = BossVictoryGlitchPresentation.Instance;
        if (p == null || !p.TryGetCutSubjects(out BossVictoryCameraCuts.Subjects s)) return;
        SessionState.SetBool(SubjectsKey, true);
        SessionState.SetVector3(SubjectsKey + ".Killer", s.Killer);
        SessionState.SetVector3(SubjectsKey + ".Boss", s.BossBase);
    }

    static bool TryGetSceneView(out Vector3 position, out Quaternion rotation, out float fov)
    {
        SceneView sv = SceneView.lastActiveSceneView;
        Camera cam = sv != null ? sv.camera : null;
        position = cam != null ? cam.transform.position : default;
        rotation = cam != null ? cam.transform.rotation : Quaternion.identity;
        fov = cam != null ? Mathf.Clamp(cam.fieldOfView, 10f, 90f) : 40f;
        return cam != null && !sv.orthographic;
    }

    // 막힌 이유(없으면 null).
    static string SceneViewBlockReason(out BossVictoryCameraCuts.Subjects s, out string source)
    {
        if (!TryGetSubjects(out s, out source)) return "기준 위치 없음 — 디버그 씬(DebugBossVictory)에서 Play 해 연출을 한 번 돌리면 생긴다";
        if (!TryGetSceneView(out _, out _, out _)) return "Scene 뷰 없음(또는 직교 보기) — Scene 뷰를 원근으로 열어 둘 것";
        return null;
    }

    void DrawSceneViewButtons(BossVictoryCameraCuts.Cut shot)
    {
        string reason = SceneViewBlockReason(out BossVictoryCameraCuts.Subjects s, out string source);
        using (new EditorGUILayout.HorizontalScope())
        using (new EditorGUI.DisabledScope(reason != null))
        {
            if (GUILayout.Button("지금 Scene 뷰로 시작 화면")) CaptureSceneView(s, end: false);
            if (GUILayout.Button("지금 Scene 뷰로 끝 화면")) CaptureSceneView(s, end: true);
        }
        EditorGUILayout.LabelField(reason ?? $"기준 = {source} 처치자·보스 위치 · 컷 기준 '{AnchorLabel(shot.anchor)}'", EditorStyles.wordWrappedMiniLabel);
    }

    void CaptureSceneView(BossVictoryCameraCuts.Subjects s, bool end)
    {
        if (!HasClipSelection || _selTrack != (int)BossVictoryTrack.CameraCut || !TryGetSceneView(out Vector3 pos, out Quaternion rot, out float fov)) return;
        Record(end ? "처치 연출 타임라인 컷 끝 화면 = Scene 뷰" : "처치 연출 타임라인 컷 시작 화면 = Scene 뷰");
        Timeline.SyncCameraCutShots();
        BossVictoryCameraCuts.Cut shot = Timeline.cameraCutShots[_selIndex];
        BossVictoryCameraCuts.ToLocal(shot.anchor, s, pos, rot, out Vector3 lp, out Quaternion lr);
        if (end)
        {
            shot.hasEnd = true;
            shot.endPosition = lp;
            shot.endRotation = lr;
            shot.endFov = fov;
        }
        else
        {
            shot.startPosition = lp;
            shot.startRotation = lr;
            shot.startFov = fov;
        }
        Timeline.cameraCutShots[_selIndex] = shot;
        Save();
        Repaint();
    }

    // 재생 헤드 시각에 컷 하나를 만들고 시작 화면 = 지금 Scene 뷰(기준 = 가운데, 고정).
    void AddCutFromSceneView()
    {
        if (SceneViewBlockReason(out BossVictoryCameraCuts.Subjects s, out _) != null || !TryGetSceneView(out Vector3 pos, out Quaternion rot, out float fov)) return;
        Record("처치 연출 타임라인 Scene 뷰로 새 컷");
        Timeline.SyncCameraCutShots();
        BossVictoryCameraCuts.Cut shot = BossVictoryCameraCuts.Cut.Default;
        BossVictoryCameraCuts.ToLocal(shot.anchor, s, pos, rot, out Vector3 lp, out Quaternion lr);
        shot.startPosition = shot.endPosition = lp;
        shot.startRotation = shot.endRotation = lr;
        shot.startFov = shot.endFov = fov;
        Timeline.cameraCut.Add(BossVictoryClip.Make(Mathf.Max(0f, Snap(_playhead)), 1f));
        Timeline.cameraCutShots.Add(shot);
        Select((int)BossVictoryTrack.CameraCut, Timeline.cameraCut.Count - 1, -1);
        Save();
        Repaint();
    }

    // ── 노드별 설정 칸(255 후속 3) — 데이터는 설정 SO 칸 그대로(SO 인스펙터에도 보임). SerializedObject 로 고쳐 Undo·저장 ──

    SerializedObject _so;

    SerializedObject SettingsObject()
    {
        if (_so == null || _so.targetObject != _settings) _so = new SerializedObject(_settings);
        _so.Update();
        return _so;
    }

    void ApplySettings(SerializedObject so)
    {
        if (so.ApplyModifiedProperties()) Save();
    }

    void DrawSettingsFields(string title, params string[] names)
    {
        SerializedObject so = SettingsObject();
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
        foreach (string n in names)
        {
            SerializedProperty p = so.FindProperty(n);
            if (p != null) EditorGUILayout.PropertyField(p, true);
        }
        ApplySettings(so);
    }

    // 마커 노드 — 시각 외 옵션.
    void DrawMarkerOptions(BossVictoryMarker marker)
    {
        switch (marker)
        {
            case BossVictoryMarker.BackDashStart:
                DrawSettingsFields("백대쉬(처치 연출 전용)",
                    nameof(BossVictoryGlitchSettings.backDashDistance), nameof(BossVictoryGlitchSettings.backDashDuration),
                    nameof(BossVictoryGlitchSettings.backDashEase), nameof(BossVictoryGlitchSettings.backDashAngle),
                    nameof(BossVictoryGlitchSettings.backDashIdleDelay));
                EditorGUILayout.LabelField("방향 = 보스 반대쪽을 보정 각만큼 돌림(+ = 시계 방향). 후 대기 = 백대쉬 끝 ~ Idle 복귀. 리액션 애니는 보스 등장 밀림과 같다(등장 연출 값은 그대로). 기본값 = 보스 등장 밀림 값.", EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("스킬 연출 시작 = 백대쉬 시작 + 시간 + 후 대기", GUILayout.Width(300f)))
                {
                    Record("처치 연출 타임라인 스킬 연출 시작 맞추기");
                    Timeline.SetMarkerTime(BossVictoryMarker.FinisherStart, Snap(BossVictoryKillerSequence.DefaultFinisherStart(
                        Timeline.backDashStartTime, _settings.backDashDuration, _settings.backDashIdleDelay)));
                    Save();
                }
                break;
            case BossVictoryMarker.FinisherStart:
                DrawSettingsFields("처치 연출 스킬 칸", nameof(BossVictoryGlitchSettings.defaultFinisherSlot));
                break;
            case BossVictoryMarker.SlowStart:
                DrawSettingsFields("End 후 배율", nameof(BossVictoryGlitchSettings.killerAfterFinisherScale), nameof(BossVictoryGlitchSettings.bossAfterFinisherScale));
                break;
            case BossVictoryMarker.FocusKiller:
            {
                SerializedObject so = SettingsObject();
                SerializedProperty p = so.FindProperty(nameof(BossVictoryGlitchSettings.focusKillerBlendSeconds));
                EditorGUILayout.Space(4f);
                bool cut = p.floatValue <= 0f;
                bool newCut = EditorGUILayout.Toggle(new GUIContent("즉시 컷", "끄면 원래 카메라 대상에서 처치자로 짧게 부드럽게 옮긴다(ease in/out)."), cut);
                if (newCut != cut) p.floatValue = newCut ? 0f : 0.3f;
                if (!newCut) p.floatValue = Mathf.Max(0.01f, EditorGUILayout.FloatField("블렌드(초)", p.floatValue));
                ApplySettings(so);
                break;
            }
            case BossVictoryMarker.HideOthers:
                DrawSettingsFields("숨김 대상", nameof(BossVictoryGlitchSettings.hideOthersTarget));
                EditorGUILayout.LabelField("팀원 = 렌더러·월드 캔버스·머리 위 체력바(몸 아래 소켓 이펙트 포함) · + 붙은 이펙트 = 팀원을 따라다니는 풀 이펙트도. 소환물은 지금 플레이어 캐릭터에 없다.", EditorStyles.wordWrappedMiniLabel);
                break;
        }
    }

    // 트랙 노드(트랙 이름 또는 그 클립 선택) — 그 트랙 모양 값.
    void DrawTrackShape(BossVictoryTrack track)
    {
        switch (track)
        {
            case BossVictoryTrack.Amber:
                DrawSettingsFields("노란 틴트 모양", nameof(BossVictoryGlitchSettings.amberColor));
                break;
            case BossVictoryTrack.Scanline:
                DrawSettingsFields("주사선 모양", nameof(BossVictoryGlitchSettings.scanlineCount));
                break;
            case BossVictoryTrack.BossNoise:
                DrawSettingsFields("보스 노이즈 모양",
                    nameof(BossVictoryGlitchSettings.bossNoiseTexture), nameof(BossVictoryGlitchSettings.bossNoiseSpace),
                    nameof(BossVictoryGlitchSettings.bossNoiseTiling), nameof(BossVictoryGlitchSettings.bossNoiseCameraScale),
                    nameof(BossVictoryGlitchSettings.bossNoiseBrightColor), nameof(BossVictoryGlitchSettings.bossNoiseBrightIntensity),
                    nameof(BossVictoryGlitchSettings.bossNoiseDarkColor));
                break;
            case BossVictoryTrack.Tear:
                DrawSettingsFields("가로 찢김 모양(끌림과 같은 줄)",
                    nameof(BossVictoryGlitchSettings.tearLinePixels), nameof(BossVictoryGlitchSettings.tearLineMaxMultiplier),
                    nameof(BossVictoryGlitchSettings.tearMaxShift), nameof(BossVictoryGlitchSettings.tearDensity),
                    nameof(BossVictoryGlitchSettings.tearRefreshRate));
                break;
            case BossVictoryTrack.Smear:
                DrawSettingsFields("밝은 부분 끌림 모양(줄 = 가로 찢김 값)", nameof(BossVictoryGlitchSettings.tearStreakLength));
                break;
            case BossVictoryTrack.KillerAction:
                EditorGUILayout.LabelField("처치 순간 하던 동작(평타·스킬 등 루프 아닌 클립)을 이 배율로 이어 가다 클립 끝에서 끊는다(먼저 끝나면 대기 · 대기·이동 중이었으면 빈 구간). 클립이 없으면 자기 클립 끝까지.", EditorStyles.wordWrappedMiniLabel);
                DrawSettingsFields("하던 동작 배율", nameof(BossVictoryGlitchSettings.killerCurrentActionScale));
                break;
            case BossVictoryTrack.CrtOff:
                DrawSettingsFields("TV 꺼짐 모양", nameof(BossVictoryGlitchSettings.crtOffGlowColor));
                break;
            case BossVictoryTrack.CameraCut:
                DrawSettingsFields("벽 숨김 기준(컷 공통)", nameof(BossVictoryGlitchSettings.wallFacingThreshold), nameof(BossVictoryGlitchSettings.wallSearchRadius));
                break;
            default:
                EditorGUILayout.LabelField("이 트랙은 세기(클립)만 — 따로 모양 값 없음.", EditorStyles.miniLabel);
                break;
        }
    }

    // 처치 연출 스킬 End 줄 노드 — 대상별 시간 배율·기본 스킬 칸.
    void DrawTimeScales()
    {
        EditorGUILayout.LabelField("처치 연출 스킬 · 시간 배율", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("처치자: 하던 동작 ×배율(처치자 평타 클립) → 백대쉬·처치 연출 스킬 ×1 → 느려짐 시작 마커(꺼짐이면 스킬 End)부터 ×End 후 배율 · 팀원: 처치 즉시 · 보스: 느려짐 전 0, 뒤 End 후 배율.", EditorStyles.wordWrappedMiniLabel);
        DrawSettingsFields("배율·스킬 칸",
            nameof(BossVictoryGlitchSettings.killerCurrentActionScale), nameof(BossVictoryGlitchSettings.killerAfterFinisherScale),
            nameof(BossVictoryGlitchSettings.otherPlayersScale), nameof(BossVictoryGlitchSettings.bossAfterFinisherScale),
            nameof(BossVictoryGlitchSettings.defaultFinisherSlot));
    }

    // ── 처치 연출 스킬 End 시각(읽기 전용, 255) ──────────────────────────
    // 처치자 순서·시간 배율은 애니 이벤트가 정한다(타임라인 값 아님). 랩 재생 중이면 이번 회차 실제 시각, 아니면 캐릭터별 클립으로 추정.

    string _finisherEstimate;

    void DrawFinisherEnd()
    {
        BossVictoryGlitchPresentation p = EditorApplication.isPlaying ? BossVictoryGlitchPresentation.Instance : null;
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Toggle(_selFinisher, "시간 배율", EditorStyles.miniButton, GUILayout.Width(64f)) != _selFinisher)
            {
                bool on = !_selFinisher;
                Select(-1, -1, -1);
                _selFinisher = on;
            }
            if (p != null && p.GlitchTime >= 0f)
            {
                float at = p.FinisherEndedAt;
                EditorGUILayout.LabelField("처치 연출 스킬 End", at >= 0f ? $"실제 {at:0.00}s (이번 회차 — 이 시각부터 처치자·보스 End 후 배율)" : "실제 — 아직 안 옴(백대쉬·스킬 재생 중)", EditorStyles.miniBoldLabel);
                return;
            }
            _finisherEstimate ??= EstimateFinisherEnd();
            EditorGUILayout.LabelField("처치 연출 스킬 End 예상", _finisherEstimate, EditorStyles.miniLabel);
            if (GUILayout.Button("다시 계산", EditorStyles.miniButton, GUILayout.Width(64f))) _finisherEstimate = null;
        }
    }

    static readonly string[] FinisherVariantPaths =
    {
        "Assets/2.Prefabs/Player/Paladin/Player_Paladin.prefab",
        "Assets/2.Prefabs/Player/Gunner/Player_Gunner.prefab",
        "Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab",
    };

    // 추정 = 스킬 연출 시작 마커 + 처치 연출 스킬 클립의 End 이벤트 시각(255 후속 4 — 이전 스킬을 기다리지 않아 마커 그대로). 가정: 공격 속도 100%.
    string EstimateFinisherEnd()
    {
        float dash = Timeline.finisherStartTime;
        var parts = new List<string>();
        foreach (string path in FinisherVariantPaths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            string name = prefab.name.Replace("Player_", "");
            float end = FinisherEndEventTime(prefab);
            parts.Add(end >= 0f ? $"{name} {dash + end:0.00}s" : $"{name} ?(End 없음)");
        }
        string slow = Timeline.slowStartTime > 0f ? $" · 느려짐은 마커 {Timeline.slowStartTime:0.00}s" : "";
        return parts.Count == 0 ? "프리팹 없음" : $"{string.Join(" · ", parts)}  (스킬 연출 시작 {dash:0.00}s + 클립 End · 공속 100% 가정{slow})";
    }

    float FinisherEndEventTime(GameObject prefab)
    {
        PlayerSkillSlot slot = PlayerVictoryFinisher.ResolveSlot(prefab, _settings);
        PlayerSkillController controller = prefab.GetComponentInChildren<PlayerSkillController>(true);
        PlayerSkillBase skill = controller != null ? controller.GetSkill(slot) : null;
        Animator animator = prefab.GetComponentInChildren<Animator>(true);
        if (skill == null || skill.Data == null || animator == null || animator.runtimeAnimatorController == null) return -1f;
        AnimationClip clip = AnimationEventRequirements.FindStateClip(animator.runtimeAnimatorController, skill.Data.AnimatorStateName);
        if (clip == null) return -1f;
        foreach (AnimationEvent ev in AnimationUtility.GetAnimationEvents(clip))
            if (ev.functionName == nameof(PlayerAnimationEventRelay.HandleSkillEvent) && ev.intParameter == (int)SkillAnimationEventType.End)
                return ev.time;
        return -1f;
    }
}
