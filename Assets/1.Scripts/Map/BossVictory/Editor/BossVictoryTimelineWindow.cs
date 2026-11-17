using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 타임라인 창(PLAN-boss-victory-glitch "타임라인 에디터 창") — 영상 편집기처럼 트랙별 클립·순간 마커를 본다/고친다.
/// 데이터 = 캐릭터별 프로필 <see cref="BossVictoryProfile.timeline"/>(267 — 도구 막대 캐릭터 드롭다운으로 고름, Play 중엔 실제 처치자)·공용 모양 값 = <see cref="BossVictoryGlitchSettings"/>.
/// 런타임과 같은 SO. Unity Timeline 패키지는 쓰지 않는다(Q4).
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
        w.minSize = new Vector2(MinPanelWidth + SplitterWidth + MinTimelineWidth + 40f, 360f);
        w.Show();
    }

    void OnEnable()
    {
        if (_settings == null) _settings = AssetDatabase.LoadAssetAtPath<BossVictoryGlitchSettings>(DefaultAssetPath);
        Undo.undoRedoPerformed += OnUndoRedo;
        SceneView.duringSceneGui += OnSceneGUI;   // 순간이동 Q 두 점 핸들(272)
        wantsMouseMove = false;
        _panelWidth = EditorPrefs.GetFloat(PanelWidthPref, DefaultPanelWidth);
    }

    // ── 왼쪽 속성 패널 / 타임라인 분할 막대(296 — 옛 아래 칸 255 후속 9 를 왼쪽 세로 패널로) — 좌우로 끌어 패널 폭(최소·최대 제한, EditorPrefs 기억).
    //    패널 내용이 길면 세로 스크롤. 타임라인은 오른쪽 나머지. ──
    const string PanelWidthPref = "BossVictoryTimelineWindow.PanelWidth";
    const float DefaultPanelWidth = 360f;
    const float MinPanelWidth = 220f;
    const float MinTimelineWidth = 260f;
    const float SplitterWidth = 6f;
    float _panelWidth = DefaultPanelWidth;
    bool _splitDragging;
    Vector2 _timelineScroll;
    Rect _bodyVisible;          // 트랙 스크롤 뷰 안 좌표의 보이는 칸(267 후속 — 밖을 누른 이벤트 무시)
    int _rulerControlId = -1;   // 시간 눈금 재생 헤드 끌기 hotControl
    Vector2 _inspectorScroll;

    float MaxPanelWidth => Mathf.Max(MinPanelWidth, position.width - SplitterWidth - MinTimelineWidth);
    float PanelWidth => Mathf.Clamp(_panelWidth, MinPanelWidth, MaxPanelWidth);

    void DrawSplitter(Rect bar)
    {
        int id = GUIUtility.GetControlID(FocusType.Passive);
        Event e = Event.current;
        EditorGUI.DrawRect(new Rect(bar.x + SplitterWidth * 0.5f - 1f, bar.y, 2f, bar.height), _splitDragging ? Color.white : new Color(0.08f, 0.08f, 0.08f));
        EditorGUIUtility.AddCursorRect(bar, MouseCursor.ResizeHorizontal);
        switch (e.type)
        {
            case EventType.MouseDown when e.button == 0 && bar.Contains(e.mousePosition):
                _splitDragging = true;
                GUIUtility.hotControl = id;
                e.Use();
                break;
            case EventType.MouseDrag when _splitDragging && GUIUtility.hotControl == id:
                _panelWidth = Mathf.Clamp(e.mousePosition.x - SplitterWidth * 0.5f, MinPanelWidth, MaxPanelWidth);
                e.Use();
                Repaint();
                break;
            case EventType.MouseUp when _splitDragging:
                _splitDragging = false;
                if (GUIUtility.hotControl == id) GUIUtility.hotControl = 0;
                EditorPrefs.SetFloat(PanelWidthPref, _panelWidth);
                e.Use();
                break;
        }
    }

    void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    // ── 캐릭터별 프로필(267) — 도구 막대 드롭다운(팔라딘·거너·어쌔신 — 기본 프로필 에셋은 11-12 삭제, 표에 없는 캐릭터 = 코드 기본값). Play 중 연출이 돌면 실제 처치자 캐릭터를 따라간다. ──
    static readonly TutorialCharacter[] Characters = { TutorialCharacter.Paladin, TutorialCharacter.Gunner, TutorialCharacter.Assassin };
    static readonly string[] CharacterLabels = { "팔라딘", "거너", "어쌔신" };
    [SerializeField] TutorialCharacter _character;
    BossVictoryProfile _profile;

    static string CharacterLabel(TutorialCharacter c)
    {
        int i = System.Array.IndexOf(Characters, c);
        return i >= 0 ? CharacterLabels[i] : "코드 기본값";   // None = 처치자를 못 찾은 회차(실제 처치자 따라갈 때만)
    }

    // 실제 처치자(랩 재생 중) — 없으면 false.
    static bool TryLiveKiller(out TutorialCharacter character, out BossVictoryProfile profile)
    {
        BossVictoryGlitchPresentation p = EditorApplication.isPlaying ? BossVictoryGlitchPresentation.Instance : null;
        profile = p != null ? p.Profile : null;
        character = profile != null ? p.KillerCharacter : TutorialCharacter.None;
        return profile != null;
    }

    // 지금 편집할 프로필 — 실제 처치자 → 드롭다운 캐릭터 프로필(표에 없으면 코드 기본값 — 저장 안 됨, 배너 안내).
    void ResolveProfile()
    {
        BossVictoryProfile before = _profile;
        if (TryLiveKiller(out TutorialCharacter live, out BossVictoryProfile liveProfile))
        {
            _character = live;
            _profile = liveProfile;
        }
        else
        {
            if (System.Array.IndexOf(Characters, _character) < 0) _character = TutorialCharacter.Paladin;   // 옛 '기본' 선택·처치자 없던 회차 뒤 → 팔라딘
            _profile = _settings != null ? _settings.ProfileFor(_character) : null;
        }
        if (_profile == before) return;
        _finisherClipKey = _bossHitClipKey = null;
        _finisherEstimate = null;
        _profileSo = null;
        if (before != null) Select(-1, -1, -1);   // 번호가 다른 타임라인 — 선택 풀기
    }

    bool ProfileIsAsset => _profile != null && EditorUtility.IsPersistent(_profile);

    BossVictoryTimeline Timeline => _profile != null ? _profile.timeline : null;

    void OnGUI()
    {
        DrawToolbar();
        if (_settings == null)
        {
            EditorGUILayout.HelpBox($"설정 SO 가 없다 — {DefaultAssetPath}", MessageType.Warning);
            return;
        }
        ResolveProfile();
        if (_profile == null) return;
        _profile.timeline ??= BossVictoryTimeline.CreateDefault();
        DrawProfileBanner();
        if (Event.current.type == EventType.Layout && Timeline.SyncCameraCutShots()) Save();   // 컷 값 개수 = 컷 클립 개수(손으로 고친 에셋 보정)
        if (!_initialFit && Event.current.type == EventType.Layout)
        {
            _initialFit = true;   // 처음 열 때 전체 보기 — 로비 전환 마커(끝)까지 한 화면에
            FitAll();
        }

        // 왼쪽 = 속성 패널(296 — 세로 스크롤, 폭은 분할 막대) · 오른쪽 = 타임라인. 선택이 바뀌는 타임라인 입력보다 패널을 먼저 그린다(같은 이벤트 안 레이아웃 어긋남 방지).
        if (!HasClipSelection) _selIndex = -1;   // Undo 로 목록이 줄었으면
        using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
        {
            float panelWidth = PanelWidth;
            _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll, GUILayout.Width(panelWidth), GUILayout.ExpandHeight(true));
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Clamp(panelWidth * 0.45f, 90f, 170f);
            DrawInspector();
            EditorGUIUtility.labelWidth = labelWidth;
            EditorGUILayout.EndScrollView();
            DrawSplitter(GUILayoutUtility.GetRect(SplitterWidth, SplitterWidth, 10f, 100000f, GUILayout.ExpandHeight(true)));
            Rect timelineView = GUILayoutUtility.GetRect(MinTimelineWidth, 100000f, 100f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawTimeline(timelineView);
        }
        ShowPendingCopyPopup();
    }

    // 타임라인 영역(267 후속 — 스크롤바): 위 시간 눈금 = 세로 고정·가로(_scrollTime) 따라감 · 왼쪽 이름 열 = 세로 따라감·가로 고정 ·
    // 세로 = 스크롤 뷰(트랙이 넘치면 막대, 맨 밑에 트랙 1칸 여유) · 가로 = 시간 스크롤 막대(확대해서 넘칠 때만, 맨 아래 고정).
    void DrawTimeline(Rect view)
    {
        int rows = BossVictoryTimeline.Tracks.Length + MarkerRowCount;
        float contentHeight = rows * RowHeight + RowHeight;
        if (Event.current.type == EventType.Repaint) _timelineWidth = view.width;
        float vBarWidth = GUI.skin.verticalScrollbar.fixedWidth > 0f ? GUI.skin.verticalScrollbar.fixedWidth : 15f;
        float hBarHeight = GUI.skin.horizontalScrollbar.fixedHeight > 0f ? GUI.skin.horizontalScrollbar.fixedHeight : 15f;
        bool vBar = false, hBar = false;
        for (int pass = 0; pass < 2; pass++)   // 막대 둘이 서로 폭·높이를 줄이므로 두 번
        {
            vBar = contentHeight > view.height - RulerHeight - (hBar ? hBarHeight : 0f);
            hBar = NeedsTimeScrollbar(view.width - LabelWidth - (vBar ? vBarWidth : 0f));
        }
        float laneWidth = Mathf.Max(1f, view.width - LabelWidth - (vBar ? vBarWidth : 0f));
        Rect ruler = new Rect(view.x + LabelWidth, view.y, laneWidth, RulerHeight);
        Rect body = new Rect(view.x, view.y + RulerHeight, view.width, Mathf.Max(1f, view.height - RulerHeight - (hBar ? hBarHeight : 0f)));
        Rect scroll = new Rect(ruler.x, body.yMax, laneWidth, hBarHeight);

        // 시간 눈금(스크롤 뷰 밖, 창 좌표) — 휠 줌·가운데 끌기·재생 헤드 누르기.
        HandleView(ruler, ruler);
        HandleRulerScrub(ruler);
        EditorGUI.DrawRect(new Rect(view.x, view.y, LabelWidth, RulerHeight), new Color(0.13f, 0.13f, 0.13f));
        if (vBar) EditorGUI.DrawRect(new Rect(ruler.xMax, view.y, vBarWidth, RulerHeight), new Color(0.13f, 0.13f, 0.13f));
        DrawRuler(ruler);
        DrawPlayhead(ruler);

        Rect content = new Rect(0f, 0f, LabelWidth + laneWidth, contentHeight);
        _timelineScroll.x = 0f;
        _timelineScroll = GUI.BeginScrollView(body, _timelineScroll, content, false, false);
        _bodyVisible = new Rect(0f, _timelineScroll.y, LabelWidth + laneWidth, body.height);   // 스크롤 뷰 안 좌표의 보이는 칸 — 밖(눈금·패널)을 누른 것은 무시
        Rect labels = new Rect(0f, 0f, LabelWidth, rows * RowHeight);
        Rect lanes = new Rect(LabelWidth, 0f, laneWidth, rows * RowHeight);

        HandleView(new Rect(lanes.x, _bodyVisible.y, lanes.width, _bodyVisible.height), lanes);
        HandleLabelClick(labels);
        HandleEdit(lanes, ruler);
        if (!HasClipSelection) _selIndex = -1;   // Undo 로 목록이 줄었으면
        DrawRows(labels, lanes);
        DrawTrackClips(lanes);
        DrawMarkers(lanes);
        DrawPlayhead(lanes);
        DrawSelectRect(lanes);
        AddCursorRects(lanes);
        GUI.EndScrollView(false);   // 휠은 줌(HandleView) — 세로는 스크롤 막대로
        if (hBar) DrawScrollbar(scroll);
    }

    float _timelineWidth = -1f;   // 지난 그리기의 타임라인 폭(전체 보기 — 왼쪽 패널 뺀 폭, 아직 안 그렸으면 -1)

    // 줄 이름 누르기 = 그 노드 선택(트랙 = 모양 값 · 마커 = 시각·옵션) — 255 후속 3.
    void HandleLabelClick(Rect labels)
    {
        Event e = Event.current;
        if (e.type != EventType.MouseDown || e.button != 0 || !labels.Contains(e.mousePosition) || !_bodyVisible.Contains(e.mousePosition)) return;
        int row = Mathf.FloorToInt((e.mousePosition.y - labels.y) / RowHeight);
        int markers = MarkerRowCount;
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
            DrawCharacterPopup();
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
        float timelineWidth = _timelineWidth > 0f ? _timelineWidth : position.width - PanelWidth - SplitterWidth;   // 왼쪽 속성 패널(296) 뺀 타임라인 폭
        float width = Mathf.Max(100f, timelineWidth - LabelWidth - 20f);
        _pixelsPerSecond = Mathf.Clamp(width / Mathf.Max(0.5f, Timeline.Duration + 0.3f), MinPixelsPerSecond, MaxPixelsPerSecond);
        _scrollTime = 0f;
        Repaint();
    }

    // ── 시간 ↔ 픽셀 ──────────────────────────────────────────────────────

    float TimeToX(Rect lanes, float t) => lanes.x + (t - _scrollTime) * _pixelsPerSecond;
    float XToTime(Rect lanes, float x) => _scrollTime + (x - lanes.x) / _pixelsPerSecond;

    Rect RowRect(Rect lanes, int row) => new Rect(lanes.x, lanes.y + row * RowHeight, lanes.width, RowHeight);
    // 위 = 마커 줄(로비 전환·카메라·숨김 — 첫 화면에 바로 보이게), 아래 = 클립 트랙.
    // 거너 Q 마커 5개(차징 시작·1·2·3단계·발사 — 294)는 한 줄 '거너 Q'(차징 시작 줄)에 같이 놓는다.
    static int MarkerRow(BossVictoryMarker marker) => BossVictoryTimeline.IsGunnerQ(marker) ? (int)BossVictoryMarker.ChargeStart : (int)marker;
    static readonly int MarkerRowCount = (int)BossVictoryMarker.ChargeStart + 1;
    static int TrackRow(BossVictoryTrack track) => MarkerRowCount + (int)track;

    Rect ClipRect(Rect lanes, int row, BossVictoryClip clip)
    {
        Rect r = RowRect(lanes, row);
        float x0 = TimeToX(lanes, clip.start), x1 = TimeToX(lanes, clip.End);
        return new Rect(x0, r.y + 2f, Mathf.Max(2f, x1 - x0), r.height - 4f);
    }

    // 휠 = 마우스 위치 기준 줌 · 가운데 버튼 끌기 = 이동. view = 받는 칸(눈금 = 창 좌표, 트랙 = 스크롤 뷰 안의 보이는 칸), lanes = 시간 ↔ x 기준(같은 좌표계).
    void HandleView(Rect view, Rect lanes)
    {
        Event e = Event.current;
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
            if (MarkerRow(marker) != row) continue;   // 거너 Q 1·2·3단계·발사 = 차징 시작 줄(294)
            DrawRowBackground(labels, lanes, row);
            string label = IsClipNode(marker) ? "▬ " + ClipNodeLabel(marker)
                : marker == BossVictoryMarker.ChargeStart ? "◆ 거너 Q(차징·1·2·3단계·발사)"
                : "◆ " + BossVictoryTimeline.MarkerLabel(marker);
            GUI.Label(new Rect(labels.x + 6f, labels.y + row * RowHeight + 4f, LabelWidth - 8f, RowHeight), label, MarkerLabelStyle(marker));
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
        DrawCutBlends(local);
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
        if (BossVictoryTimeline.IsUvScroll(track)) DrawUvScroll(r, clip);
        if (BossVictoryTimeline.IsBob(track))
            GUI.Label(new Rect(r.x + 4f, r.y + 2f, Mathf.Max(0f, r.width - 6f), r.height - 2f),
                $"±{clip.bobAmplitude:0.##}% · {clip.bobPeriod:0.##}s", EditorStyles.miniLabel);
        if (BossVictoryTimeline.IsAssassinDash(track))
            GUI.Label(new Rect(r.x + 4f, r.y + 2f, Mathf.Max(0f, r.width - 6f), r.height - 2f),
                $"#{BossVictoryAssassinDash.Number(Timeline.assassinDash, index)}", EditorStyles.miniBoldLabel);
        if (track == BossVictoryTrack.CameraCut)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, 2f, r.height), Color.white);   // 컷 = 시작 순간
            BossVictoryCameraCuts.Cut shot = Timeline.CameraCutShot(index);
            GUI.Label(new Rect(r.x + 4f, r.y + 4f, Mathf.Max(0f, r.width - 6f), r.height - 4f),
                $"#{index + 1} {AnchorLabel(shot.anchor)}{(shot.Moves ? " → 이동" : " 고정")}{(shot.hideWalls ? "" : " · 벽 숨김 끔")}", EditorStyles.miniLabel);
        }
        if (selected) DrawOutline(r, Color.white);
        if (IsKeyed(track)) DrawKeys(r, track, index, selected);
    }

    // 노이즈 UV 수직 이동(267 후속) — 시작 → 끝 오프셋 곡선(작은 쪽 = 아래, 큰 쪽 = 위)·'0.00 → 1.00 이징' 글자.
    static void DrawUvScroll(Rect r, BossVictoryClip clip)
    {
        const int n = 24;
        float lo = Mathf.Min(clip.uvFrom, clip.uvTo), hi = Mathf.Max(clip.uvFrom, clip.uvTo);
        float span = Mathf.Max(0.0001f, hi - lo);
        var pts = new Vector3[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float u = i / (float)n;
            float v = Mathf.LerpUnclamped(clip.uvFrom, clip.uvTo, BossVictoryKeys.Ease(clip.uvEase, u));
            pts[i] = new Vector3(r.x + u * r.width, r.yMax - 2f - (hi > lo ? (v - lo) / span : 0.5f) * (r.height - 4f));
        }
        Handles.color = EnvelopeColor;
        Handles.DrawAAPolyLine(2f, pts);
        GUI.Label(new Rect(r.x + 4f, r.y + 2f, Mathf.Max(0f, r.width - 6f), r.height - 2f),
            $"{clip.uvFrom:0.##} → {clip.uvTo:0.##} {KeyCurveLabels[Mathf.Clamp((int)clip.uvEase, 0, KeyCurveLabels.Length - 1)]}", EditorStyles.miniLabel);
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

    static string OffText(BossVictoryMarker marker) => marker switch
    {
        BossVictoryMarker.SlowStart => "끔 — 스킬 End",
        BossVictoryMarker.BossSlow => "끔 — 느려짐 시작 따름",
        _ => "끔",
    };

    static string MarkerText(BossVictoryTimeline tl, BossVictoryMarker marker) =>
        BossVictoryTimeline.IsGunnerQ(marker) ? GunnerQShortLabel(marker)
        : BossVictoryTimeline.ZeroMeansOff(marker) && tl.MarkerTime(marker) <= 0f
            ? $"{BossVictoryTimeline.MarkerLabel(marker)} ({OffText(marker)})"
            : $"{BossVictoryTimeline.MarkerLabel(marker)} {tl.MarkerTime(marker):0.00}s";

    // 거너 Q 한 줄(294) — 마커 5개가 붙어 있어 라벨은 이름만 짧게(시각은 선택하면 아래 칸).
    static string GunnerQShortLabel(BossVictoryMarker marker) => marker switch
    {
        BossVictoryMarker.ChargeStart => "차징",
        BossVictoryMarker.GunnerStage1 => "1단계",
        BossVictoryMarker.GunnerStage2 => "2단계",
        BossVictoryMarker.GunnerStage3 => "3단계",
        BossVictoryMarker.GunnerFire => "발사",
        _ => BossVictoryTimeline.MarkerLabel(marker),
    };

    static readonly string[] AnchorLabels = { "가운데", "처치자", "보스", "처치자 무기(매 프레임)", "보스 정면" };

    static string AnchorLabel(BossVictoryCameraCuts.Anchor anchor) =>
        (int)anchor >= 0 && (int)anchor < AnchorLabels.Length ? AnchorLabels[(int)anchor] : anchor.ToString();

    // 마커 = 줄 전체를 지나는 세로선 + 큰 마름모 + "이름 시각s" 라벨(상시). 화면 밖이면 그 줄 끝에 ◀/▶ 라벨을 붙여 둔다(누르면 그쪽으로 이동).
    void DrawMarkers(Rect lanes)
    {
        GUI.BeginClip(lanes);
        Rect local = new Rect(0f, 0f, lanes.width, lanes.height);
        DrawClipNodes(local);
        bool gunnerPinLeft = false, gunnerPinRight = false;   // 거너 Q 줄(294) — 화면 밖 핀은 쪽마다 하나
        foreach (BossVictoryMarker marker in BossVictoryTimeline.Markers)
        {
            if (IsClipNode(marker)) continue;   // 처치 연출 스킬·보스 피격 애니 = 클립(255 후속 7·10)
            bool lobby = marker == BossVictoryMarker.LobbyTransition;
            float x = TimeToX(local, Timeline.MarkerTime(marker));
            Color c = IsSelectedMarker(marker) ? Color.white : MarkerTint(marker);
            Rect row = RowRect(local, MarkerRow(marker));
            string text = MarkerText(Timeline, marker);
            GUIStyle style = MarkerLabelStyle(marker);
            if (BossVictoryTimeline.IsGunnerQ(marker) && (x < 0f || x > local.width))
            {
                if (x < 0f ? gunnerPinLeft : gunnerPinRight) continue;
                if (x < 0f) gunnerPinLeft = true;
                else gunnerPinRight = true;
                text = "거너 Q";
            }
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
    // 거너 Q 줄(294)은 마커 5개가 한 줄 — 보이는 마커를 먼저 잡고(가까운 것), 화면 밖으로 옮기는 것은 그 쪽 끝 핀을 눌렀을 때만.
    bool PickMarker(Rect lanes, Vector2 mouse, out BossVictoryMarker picked)
    {
        picked = default;
        float best = float.MaxValue;
        bool found = false;
        foreach (BossVictoryMarker marker in BossVictoryTimeline.Markers)
        {
            if (IsClipNode(marker)) continue;   // 클립 — PickClipNode
            Rect row = RowRect(lanes, MarkerRow(marker));
            if (!row.Contains(mouse)) continue;
            float x = TimeToX(lanes, Timeline.MarkerTime(marker));
            if (x < lanes.x || x > lanes.xMax) continue;
            float textWidth = MarkerLabelStyle(marker).CalcSize(new GUIContent(MarkerText(Timeline, marker))).x;
            Rect label = MarkerLabelRect(new Rect(lanes.x, row.y, lanes.width, row.height), x, textWidth, lanes.xMax);
            float d = Mathf.Abs(mouse.x - x);
            if ((d <= MarkerSizeLobby + 4f || label.Contains(mouse)) && d < best)
            {
                best = d;
                picked = marker;
                found = true;
            }
        }
        if (found) return true;
        foreach (BossVictoryMarker marker in BossVictoryTimeline.Markers)
        {
            if (IsClipNode(marker)) continue;
            Rect row = RowRect(lanes, MarkerRow(marker));
            if (!row.Contains(mouse)) continue;
            float x = TimeToX(lanes, Timeline.MarkerTime(marker));
            if (x >= lanes.x && x <= lanes.xMax) continue;
            if (BossVictoryTimeline.IsGunnerQ(marker) && (x < lanes.x ? mouse.x > lanes.x + PinPickWidth : mouse.x < lanes.xMax - PinPickWidth)) continue;
            _scrollTime = Mathf.Max(0f, Timeline.MarkerTime(marker) - lanes.width * 0.5f / _pixelsPerSecond);
            Repaint();
            return false;
        }
        return false;
    }

    const float PinPickWidth = 90f;   // 거너 Q 줄 화면 밖 핀 누르는 폭(px)

    // 거너 Q 줄에서 시각 t 에 가장 가까운 마커(우클릭 메뉴).
    BossVictoryMarker NearestGunnerQ(float t)
    {
        BossVictoryMarker best = BossVictoryMarker.ChargeStart;
        float bestD = float.MaxValue;
        foreach (BossVictoryMarker marker in BossVictoryTimeline.Markers)
        {
            if (!BossVictoryTimeline.IsGunnerQ(marker)) continue;
            float d = Mathf.Abs(Timeline.MarkerTime(marker) - t);
            if (d < bestD) { bestD = d; best = marker; }
        }
        return best;
    }

    // 재생 헤드 세로 선 — 눈금(창 좌표)과 트랙(스크롤 뷰 안) 에 따로 그린다. r = 그 칸(x 기준 = r.x).
    void DrawPlayhead(Rect r)
    {
        float x = TimeToX(r, _playhead);
        if (x < r.x || x > r.xMax) return;
        EditorGUI.DrawRect(new Rect(x - 0.5f, r.y, 1.5f, r.height), new Color(1f, 0.3f, 0.3f));
    }

    float TimeScrollTotal(float visible) => Mathf.Max(Timeline.Duration + 1f, _scrollTime + visible);

    // 가로 막대가 필요한가 — 전체 시간(끝 + 1초)이 보이는 폭보다 길거나 이미 옆으로 밀려 있으면.
    bool NeedsTimeScrollbar(float laneWidth) => _scrollTime > 0f || TimeScrollTotal(laneWidth / _pixelsPerSecond) > laneWidth / _pixelsPerSecond + 0.0001f;

    void DrawScrollbar(Rect r)
    {
        float visible = r.width / _pixelsPerSecond;
        _scrollTime = GUI.HorizontalScrollbar(r, _scrollTime, visible, 0f, TimeScrollTotal(visible));
    }

    // 시간 눈금 누르기·끌기 = 재생 헤드(스크롤 뷰 밖이라 따로). 끌기·놓기는 HandleEdit 의 Playhead 끌기가 이어 받는다.
    void HandleRulerScrub(Rect ruler)
    {
        _rulerControlId = GUIUtility.GetControlID(FocusType.Passive);
        Event e = Event.current;
        if (e.type != EventType.MouseDown || e.button != 0 || !ruler.Contains(e.mousePosition)) return;
        _drag = DragMode.Playhead;
        GUIUtility.hotControl = _rulerControlId;
        SetPlayhead(XToTime(ruler, e.mousePosition.x));
        e.Use();
    }

    // ── 선택·편집 ────────────────────────────────────────────────────────
    // 끌기 = 이동 · 양 끝(좌우 화살표 커서 — 왼쪽 = 시작 이동·끝 고정, 오른쪽 = 길이) · 클립 위 마름모 = 키(중간 키 끌기 = 시각, 더블클릭 = 키 추가, 255 후속 6) ·
    // 우클릭 = 추가/복사/붙여넣기/삭제 · Del(키 선택 중이면 그 키) · Ctrl+C/V · 0.01초 스냅 · Undo.

    // Rect = 빈 곳 사각형 선택 · Group = 여러 개 같이 끌기(296).
    enum DragMode { None, Move, ResizeLeft, ResizeRight, Key, Marker, Playhead, NodeTrim, CutBlend, Rect, Group }

    const float MinLength = 0.01f;
    const float HandleSize = 7f;

    [SerializeField] bool _snap = true;
    [SerializeField] bool _helpFold = true;   // 왼쪽 패널 '조작' 접기(296)
    int _selTrack = -1;
    int _selIndex = -1;
    int _selMarker = -1;
    int _selKey = -1;   // 선택 클립 안 키 번호(효과 세기·카메라 컷 — 255 후속 6)
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

    // 여러 개 선택(296)이면 그 목록만 — 하나면 _sel*(키를 고른 클립도 선택으로 보임, 지금까지 그대로).
    bool IsSelected(BossVictoryTrack track, int index) => _multi.Count > 0 ? _multi.Contains(NodeRef.Clip(track, index)) : _selTrack == (int)track && _selIndex == index;
    bool IsSelectedMarker(BossVictoryMarker marker) => _multi.Count > 0 ? _multi.Contains(NodeRef.Marker(marker)) : _selMarker == (int)marker;
    bool IsSelectedKey(BossVictoryTrack track, int clip, int key) => _multi.Count > 0 ? _multi.Contains(NodeRef.KeyOf(track, clip, key)) : _selTrack == (int)track && _selIndex == clip && _selKey == key;
    bool HasClipSelection => _selTrack >= 0 && _selIndex >= 0 && _selIndex < Timeline.Clips((BossVictoryTrack)_selTrack).Count;

    bool _selFinisher;   // '처치 연출 스킬 End' 줄 노드(시간 배율 칸)
    bool _selPalette;    // 팔레트(271 — 기준 색·대비·12색 견본) 칸

    void Select(int track, int index, int marker)
    {
        _selTrack = track;
        _selIndex = index;
        _selMarker = marker;
        _selKey = -1;
        _selFinisher = false;
        _selPalette = false;
        _multi.Clear();
        GUI.FocusControl(null);
    }

    // ── 키(255 후속 6) — 효과 세기 트랙 = 클립 세기 키 · 카메라 컷 = 화면 키. 시각 = 클립 비율(클립을 옮기거나 늘이면 비율대로 따라감) ──

    static readonly Color KeyColor = new Color(1f, 0.85f, 0.35f);
    static readonly string[] KeyCurveLabels = { "선형", "ease in/out", "ease in", "ease out", "계단" };
    const float KeySize = 4f;

    static bool IsKeyed(BossVictoryTrack track) => BossVictoryTimeline.HasFades(track) || track == BossVictoryTrack.CameraCut;

    int KeyCount(BossVictoryTrack track, int clip)
    {
        if (track == BossVictoryTrack.CameraCut)
        {
            BossVictoryCameraCuts.Cut shot = Timeline.CameraCutShot(clip);
            return shot.HasKeys ? shot.keys.Count : 0;
        }
        BossVictoryClip c = Timeline.Clips(track)[clip];
        return c.HasKeys ? c.keys.Count : 0;
    }

    float KeyU(BossVictoryTrack track, int clip, int key) => track == BossVictoryTrack.CameraCut
        ? Timeline.CameraCutShot(clip).keys[key].u
        : Timeline.Clips(track)[clip].keys[key].u;

    // 키 마름모 높이(0 = 아래, 1 = 위) — 세기 키 = 세기 · 컷 키 = 가운데.
    float KeyHeight(BossVictoryTrack track, int clip, int key) => track == BossVictoryTrack.CameraCut ? 0.5f : Timeline.Clips(track)[clip].keys[key].value;

    // 키 시각의 기준 구간(초) — 컷 = 다음 컷 시작까지(런타임 진행과 같은 식).
    float KeySpanStart(BossVictoryTrack track, int clip) => Timeline.Clips(track)[clip].start;

    float KeySpanLength(BossVictoryTrack track, int clip) => track == BossVictoryTrack.CameraCut
        ? Mathf.Max(MinLength, Timeline.CameraCutEnd(clip) - Timeline.cameraCut[clip].start)
        : Mathf.Max(MinLength, Timeline.Clips(track)[clip].length);

    Vector2 KeyPoint(Rect r, BossVictoryTrack track, int clip, int key)
    {
        float x = r.x + KeyU(track, clip, key) * r.width;
        float y = r.yMax - 2f - KeyHeight(track, clip, key) * (r.height - 4f);
        return new Vector2(x, y);
    }

    void DrawKeys(Rect r, BossVictoryTrack track, int clip, bool selected)
    {
        int n = KeyCount(track, clip);
        for (int k = 0; k < n; k++)
        {
            Vector2 p = KeyPoint(r, track, clip, k);
            bool on = IsSelectedKey(track, clip, k);
            float s = on ? KeySize + 2f : KeySize;
            Handles.color = on ? Color.white : (selected ? KeyColor : KeyColor * 0.8f);
            Handles.DrawAAConvexPolygon(new Vector3(p.x, p.y - s), new Vector3(p.x + s, p.y), new Vector3(p.x, p.y + s), new Vector3(p.x - s, p.y));
        }
    }

    bool PickKey(Rect lanes, BossVictoryTrack track, Vector2 mouse, out int clip, out int key)
    {
        clip = key = -1;
        if (!IsKeyed(track)) return false;
        float best = HandleSize;
        List<BossVictoryClip> clips = Timeline.Clips(track);
        for (int i = 0; i < clips.Count; i++)
        {
            Rect r = ClipRect(lanes, TrackRow(track), clips[i]);
            int n = KeyCount(track, i);
            for (int k = 0; k < n; k++)
            {
                float d = Vector2.Distance(mouse, KeyPoint(r, track, i, k));
                if (d > best) continue;
                best = d;
                clip = i;
                key = k;
            }
        }
        return clip >= 0;
    }

    // 더블클릭 = 그 시각에 중간 키(지금 곡선 값으로 — 모양이 안 바뀐다).
    bool TryAddKeyAt(Rect lanes, Vector2 mouse)
    {
        foreach (BossVictoryTrack track in BossVictoryTimeline.Tracks)
        {
            if (!IsKeyed(track) || !RowRect(lanes, TrackRow(track)).Contains(mouse)) continue;
            List<BossVictoryClip> clips = Timeline.Clips(track);
            for (int i = clips.Count - 1; i >= 0; i--)
            {
                Rect r = ClipRect(lanes, TrackRow(track), clips[i]);
                if (!r.Contains(mouse)) continue;
                AddKey(track, i, (mouse.x - r.x) / Mathf.Max(1f, r.width));
                return true;
            }
        }
        return false;
    }

    void AddKey(BossVictoryTrack track, int clip, float u)
    {
        u = Mathf.Clamp01(u);
        Record("처치 연출 타임라인 키 추가");
        int added;
        if (track == BossVictoryTrack.CameraCut)
        {
            Timeline.SyncCameraCutShots();
            BossVictoryCameraCuts.Cut shot = Timeline.cameraCutShots[clip];
            shot.MigrateToKeys();
            if (shot.keys.Count == 1)   // 고정 컷 → 끝 키(같은 화면)부터 — 중간 키는 그 사이
            {
                BossVictoryCameraCuts.CutKey end = shot.keys[0];
                end.u = 1f;
                shot.keys.Add(end);
            }
            BossVictoryCameraCuts.LocalPose(shot.keys, shot.smoothPath, u, out Vector3 p, out Quaternion q, out float fov);
            BossVictoryKeys.Segment(shot.keys, u, out int seg, out _);
            added = BossVictoryKeys.Insert(shot.keys, BossVictoryCameraCuts.CutKey.Make(u, p, q, fov, shot.keys[seg].curve));
            Timeline.cameraCutShots[clip] = shot;
        }
        else
        {
            List<BossVictoryClip> clips = Timeline.Clips(track);
            BossVictoryClip c = clips[clip];
            c.MigrateFadesToKeys();
            BossVictoryKeys.Segment(c.keys, u, out int seg, out _);
            added = BossVictoryKeys.Insert(c.keys, BossVictoryKey.Make(u, BossVictoryKeys.Value(c.keys, u), c.keys[seg].curve));
            clips[clip] = c;
        }
        Select((int)track, clip, -1);
        _selKey = added;
        Save();
        Repaint();
    }

    void DragKey(BossVictoryTrack track, int clip, int key, float mouseTime) => SetKeyTime(track, clip, key, Snap(mouseTime));

    // 중간 키를 타임라인 초 time 으로(앞뒤 키 사이로 막음) — 하나 끌기·여러 개 같이 끌기(296) 공용.
    void SetKeyTime(BossVictoryTrack track, int clip, int key, float time)
    {
        float u = (time - KeySpanStart(track, clip)) / KeySpanLength(track, clip);
        if (track == BossVictoryTrack.CameraCut)
        {
            BossVictoryCameraCuts.Cut shot = Timeline.cameraCutShots[clip];
            BossVictoryCameraCuts.CutKey k = shot.keys[key];
            k.u = BossVictoryKeys.ClampMiddleU(shot.keys, key, u);
            shot.keys[key] = k;
            return;
        }
        BossVictoryClip c = Timeline.Clips(track)[clip];
        BossVictoryKey sk = c.keys[key];
        sk.u = BossVictoryKeys.ClampMiddleU(c.keys, key, u);
        c.keys[key] = sk;
    }

    void DeleteSelectedKey()
    {
        var track = (BossVictoryTrack)_selTrack;
        int n = KeyCount(track, _selIndex);
        if (_selKey <= 0 || _selKey >= n - 1) return;   // 시작·끝 키는 클립 양 끝 — 지우지 않는다
        Record("처치 연출 타임라인 키 삭제");
        if (track == BossVictoryTrack.CameraCut) Timeline.cameraCutShots[_selIndex].keys.RemoveAt(_selKey);
        else Timeline.Clips(track)[_selIndex].keys.RemoveAt(_selKey);
        _selKey = -1;
        Save();
        Repaint();
    }

    void DrawKeyListButtons(BossVictoryTrack track)
    {
        int n = KeyCount(track, _selIndex);
        float u = (_playhead - KeySpanStart(track, _selIndex)) / KeySpanLength(track, _selIndex);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"키 {n}개", GUILayout.Width(60f));
            using (new EditorGUI.DisabledScope(u <= 0f || u >= 1f))
                if (GUILayout.Button($"재생 헤드에 중간 키 ({_playhead:0.00}초)")) AddKey(track, _selIndex, u);
            using (new EditorGUI.DisabledScope(_selKey <= 0 || _selKey >= n - 1))
                if (GUILayout.Button("선택 키 삭제", GUILayout.Width(90f))) DeleteSelectedKey();
        }
        EditorGUILayout.LabelField("클립 위 마름모 = 키 · 더블클릭 = 중간 키 추가 · 중간 키 끌기 = 시각 · Del = 선택 키 삭제(시작·끝 키는 고정). 키 시각은 클립 비율이라 클립을 옮기거나 늘이면 따라간다.", EditorStyles.wordWrappedMiniLabel);
    }

    void DrawIntensityKeyInspector(BossVictoryTrack track)
    {
        BossVictoryClip c = Timeline.Clips(track)[_selIndex];
        if (!c.HasKeys) return;
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("세기 키", EditorStyles.miniBoldLabel);
        DrawKeyListButtons(track);
        if (_selKey < 0 || _selKey >= c.keys.Count) return;
        BossVictoryKey k = c.keys[_selKey];
        bool middle = _selKey > 0 && _selKey < c.keys.Count - 1;
        EditorGUI.BeginChangeCheck();
        using (new EditorGUI.DisabledScope(!middle))
        {
            float t = EditorGUILayout.FloatField($"키 #{_selKey + 1} 시각(초)", c.start + k.u * c.length);
            k.u = BossVictoryKeys.ClampMiddleU(c.keys, _selKey, (t - c.start) / Mathf.Max(MinLength, c.length));
        }
        k.value = EditorGUILayout.Slider("세기", k.value, 0f, 1f);
        using (new EditorGUI.DisabledScope(_selKey >= c.keys.Count - 1))
            k.curve = (BossVictoryKeyCurve)EditorGUILayout.Popup("다음 키까지 곡선", (int)k.curve, KeyCurveLabels);
        if (EditorGUI.EndChangeCheck())
        {
            RecordField("처치 연출 타임라인 세기 키");
            c.keys[_selKey] = k;
            Save();
        }
    }

    void DrawCutKeyInspector()
    {
        BossVictoryCameraCuts.Cut shot = Timeline.cameraCutShots[_selIndex];
        if (!shot.HasKeys) return;
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("화면 키", EditorStyles.miniBoldLabel);
        DrawKeyListButtons(BossVictoryTrack.CameraCut);
        int key = _selKey >= 0 && _selKey < shot.keys.Count ? _selKey : 0;
        BossVictoryCameraCuts.CutKey k = shot.keys[key];
        bool middle = key > 0 && key < shot.keys.Count - 1;
        float start = Timeline.cameraCut[_selIndex].start, span = KeySpanLength(BossVictoryTrack.CameraCut, _selIndex);
        EditorGUILayout.LabelField($"키 #{key + 1}{(key == 0 ? " (시작)" : key == shot.keys.Count - 1 ? " (끝)" : "")}{(shot.keys.Count == 1 ? " — 고정 컷" : "")}", EditorStyles.miniBoldLabel);
        EditorGUI.BeginChangeCheck();
        using (new EditorGUI.DisabledScope(!middle))
        {
            float t = EditorGUILayout.FloatField("시각(초)", start + k.u * span);
            k.u = BossVictoryKeys.ClampMiddleU(shot.keys, key, (t - start) / span);
        }
        k.position = EditorGUILayout.Vector3Field("위치(기준 m)", k.position);
        k.rotation = Quaternion.Euler(EditorGUILayout.Vector3Field("회전(기준 °)", k.rotation.eulerAngles));
        k.fov = EditorGUILayout.Slider("FOV", k.fov, 10f, 90f);
        using (new EditorGUI.DisabledScope(key >= shot.keys.Count - 1))
            k.curve = (BossVictoryKeyCurve)EditorGUILayout.Popup("다음 키까지 곡선", (int)k.curve, KeyCurveLabels);
        if (EditorGUI.EndChangeCheck())
        {
            RecordField("처치 연출 타임라인 화면 키");
            shot.keys[key] = k;
            Save();
        }
    }

    // 편집 한 번 = Undo 한 단계(새 묶음). 끌기는 시작~끝을 한 묶음(BeginDragUndo·RecordDrag·EndDragUndo).
    void Record(string label)
    {
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(label);
        Undo.RecordObject(_profile, label);
    }

    // 수치 칸 — 칸을 고치는 동안(슬라이더 끌기 포함)은 Unity 가 이벤트마다 묶음을 나눈다. 새 묶음을 억지로 만들지 않는다.
    void RecordField(string label)
    {
        Undo.RecordObject(_profile, label);
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
        Undo.RecordObject(_profile, "처치 연출 타임라인 끌기");
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
        if (_settings == null || _profile == null) return;
        PruneMulti();   // 여러 개 선택(296) — 사라진 번호 빼기
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
        _finisherClipKey = _bossHitClipKey = null;   // 처치 연출 스킬 칸·피격 상태 이름이 바뀌었을 수 있다 — 클립 다시 찾기
        Timeline.SyncCameraCutShots();
        Timeline.FitCameraCutLengths();   // 컷 길이 = 다음 컷 시작까지(표시용 — 판정은 시작만)
        EditorUtility.SetDirty(_profile);
        if (ProfileIsAsset) AssetDatabase.SaveAssetIfDirty(_profile);
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
            case EventType.MouseDown when e.button == 0 && _bodyVisible.Contains(e.mousePosition):   // 시간 눈금 누르기 = HandleRulerScrub(스크롤 뷰 밖)
                if (lanes.Contains(e.mousePosition))
                {
                    if (e.clickCount == 2 && TryAddKeyAt(lanes, e.mousePosition))
                    {
                        e.Use();
                        break;
                    }
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
                if (_drag == DragMode.Rect) FinishSelectRect(lanes);   // 사각형 선택(296)
                if (_dragRecorded) Save();
                EndDragUndo();
                _drag = DragMode.None;
                if (GUIUtility.hotControl == controlId || GUIUtility.hotControl == _rulerControlId) GUIUtility.hotControl = 0;
                _dragRecorded = false;
                e.Use();
                break;
            case EventType.ContextClick when lanes.Contains(e.mousePosition) && _bodyVisible.Contains(e.mousePosition):
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
                if ((HasClipSelection || _multi.Count >= 2) && GUIUtility.keyboardControl == 0)
                {
                    DeleteSelected();   // 여러 개 선택이면 전부(296)
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
                if (_multi.Count >= 2)   // 여러 개(296) — 선택 클립을 가장 늦은 끝 뒤에 간격 그대로
                {
                    float end = MultiClipEnd();
                    if (CopyMulti())
                    {
                        if (s_clipboardMulti != null) PasteMulti(end);
                        else PasteAt(s_clipboardTrack, end);
                    }
                }
                else if (HasClipSelection)
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
        Event ev = Event.current;
        _dragMouseT0 = XToTime(lanes, mouse.x);
        if (ev.shift || ev.control || ev.command)   // 여러 개 선택(267·296) — 노드 = 넣기/빼기, 빈 곳 = 사각형으로 더하기
        {
            if (TryPickNode(lanes, mouse, out NodeRef picked))
            {
                ToggleMulti(picked);
                _drag = DragMode.None;
            }
            else BeginSelectRect(mouse, true);
            return;
        }
        // 여러 개 선택 안의 노드를 잡으면 = 전부 같이 끌기(296). 처치 연출 스킬·보스 피격 애니 끝 자르기는 그 노드 하나만.
        if (_multi.Count >= 2 && TryPickNode(lanes, mouse, out NodeRef grabbed, out DragMode grabMode) && _multi.Contains(grabbed) && grabMode != DragMode.NodeTrim)
        {
            BeginGroupDrag(grabbed, grabMode);
            return;
        }
        if (PickClipNode(lanes, mouse, out BossVictoryMarker node, out bool trimEdge))
        {
            Select(-1, -1, (int)node);
            _dragMarkerOrig = Timeline.MarkerTime(node);
            _drag = trimEdge ? DragMode.NodeTrim : DragMode.Marker;
            return;
        }
        if (PickMarker(lanes, mouse, out BossVictoryMarker pickedMarker))
        {
            Select(-1, -1, (int)pickedMarker);
            _dragMarkerOrig = Timeline.MarkerTime(pickedMarker);
            _drag = DragMode.Marker;
            return;
        }
        foreach (BossVictoryTrack track in BossVictoryTimeline.Tracks)
        {
            if (!RowRect(lanes, TrackRow(track)).Contains(mouse)) continue;
            if (track == BossVictoryTrack.CameraCut && PickCutBlend(lanes, mouse, out int blendCut))
            {
                Select((int)track, blendCut, -1);
                _drag = DragMode.CutBlend;
                return;
            }
            if (PickKey(lanes, track, mouse, out int keyClip, out int key))
            {
                Select((int)track, keyClip, -1);
                _selKey = key;
                _drag = key > 0 && key < KeyCount(track, keyClip) - 1 ? DragMode.Key : DragMode.None;   // 시작·끝 키는 클립 끝에 고정
                return;
            }
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
        BeginSelectRect(mouse, false);   // 빈 곳 = 선택 풀기 + 끌면 사각형 선택(296)
    }

    // 클립 손잡이 고르기(키 마름모는 먼저 PickKey) — 순서 = ① 가장 가까운 끝(몸통보다 먼저, 이웃 클립 포함) ② 몸통.
    // 끝 잡는 폭 = 클립 밖 EdgeOuter + 안쪽 클립 폭의 30%(EdgeInnerMin~EdgeInnerMax) — 짧은 클립·줌 아웃에서도 잡힌다.
    bool PickClip(Rect lanes, BossVictoryTrack track, Vector2 mouse, out int index, out DragMode mode)
    {
        List<BossVictoryClip> clips = Timeline.Clips(track);
        int row = TrackRow(track);
        index = -1;
        mode = DragMode.None;

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
    // 마우스 커서 — 끝 = 좌우 화살표 · 몸통 = 이동 · 중간 키 = 밀기.
    void AddCursorRects(Rect lanes)
    {
        AddClipNodeCursors(lanes);
        foreach (BossVictoryMarker marker in BossVictoryTimeline.Markers)
        {
            if (IsClipNode(marker)) continue;
            Rect row = RowRect(lanes, MarkerRow(marker));
            float x = TimeToX(lanes, Timeline.MarkerTime(marker));
            AddCursor(lanes, new Rect(x - MarkerSizeLobby - 4f, row.y, (MarkerSizeLobby + 4f) * 2f, row.height), MouseCursor.ResizeHorizontal);
        }
        for (int i = 0; i < Timeline.cameraCut.Count; i++)
            if (CutBlendRect(lanes, i, out Rect wedge))
                AddCursor(lanes, new Rect(wedge.x - HandleSize, wedge.y, HandleSize * 2f, wedge.height), MouseCursor.ResizeHorizontal);
        foreach (BossVictoryTrack track in BossVictoryTimeline.Tracks)
        {
            List<BossVictoryClip> clips = Timeline.Clips(track);
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
        if (_drag == DragMode.Rect)   // 사각형 선택(296) — 놓을 때 고른다
        {
            _rectB = mouse;
            Repaint();
            return;
        }
        RecordDrag();   // 매 끌기 이벤트 기록 — 손을 떼면 한 묶음으로 합친다
        _dragRecorded = true;
        if (_drag == DragMode.Group)   // 여러 개 같이 끌기(296)
        {
            ApplyGroupDrag(mt);
            Repaint();
            return;
        }
        if (_drag == DragMode.NodeTrim)
        {
            DragNodeTrim((BossVictoryMarker)_selMarker, mt);
            Repaint();
            return;
        }
        if (_drag == DragMode.Marker)
        {
            Timeline.SetMarkerTime((BossVictoryMarker)_selMarker, Snap(_dragMarkerOrig + (mt - _dragMouseT0)));   // 라벨을 잡아도 튀지 않게 이동량으로
            Repaint();
            return;
        }
        if (!HasClipSelection) return;
        if (_drag == DragMode.CutBlend)
        {
            SetCutBlendSeconds(_selIndex, Timeline.cameraCut[_selIndex].start - Snap(mt));
            Repaint();
            return;
        }
        if (_drag == DragMode.Key)
        {
            DragKey((BossVictoryTrack)_selTrack, _selIndex, _selKey, mt);
            Repaint();
            return;
        }
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
            BossVictoryMarker m = BossVictoryTimeline.IsGunnerQ(marker) ? NearestGunnerQ(t) : marker;   // 거너 Q 줄(294) = 가장 가까운 마커
            if (!_multi.Contains(NodeRef.Marker(m))) Select(-1, -1, (int)m);   // 여러 개 선택 안의 노드면 그대로
            menu.AddItem(new GUIContent($"여기({t:0.00}초)로 옮기기"), false, () => MoveMarker(m, t));
            menu.AddItem(new GUIContent($"재생 헤드({_playhead:0.00}초)로 옮기기"), false, () => MoveMarker(m, _playhead));
            if (IsClipNode(m))
            {
                if (NodeTrim(m) > 0f) menu.AddItem(new GUIContent("끝 자르기 풀기(클립 끝까지)"), false, () => SetNodeTrim(m, 0f));
                else menu.AddDisabledItem(new GUIContent("끝 자르기 풀기(안 잘림)"));
            }
            menu.AddSeparator("");
            AddCopyToCharactersItem(menu, mouse);
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
                if (!_multi.Contains(NodeRef.Clip(track, hit))) Select((int)track, hit, -1);   // 여러 개 선택 안의 노드면 그대로
                menu.AddItem(new GUIContent("복사"), false, CopySelected);
                AddCopyToCharactersItem(menu, mouse);
                menu.AddItem(new GUIContent("삭제"), false, DeleteSelected);
                if (track == BossVictoryTrack.CameraCut)
                {
                    int cutIndex = hit;
                    if (Timeline.CameraCutBlendSeconds(cutIndex) > 0f) menu.AddItem(new GUIContent("블렌드 제거"), false, () => SetCutBlend(cutIndex, 0f));
                    else if (Timeline.CameraCutBlendLimit(cutIndex) > 0f) menu.AddItem(new GUIContent("블렌드 추가 (앞 컷에서 0.3초)"), false, () => SetCutBlend(cutIndex, DefaultCutBlend));
                    else menu.AddDisabledItem(new GUIContent("블렌드 추가 (앞에 시간이 없음)"));
                }
                menu.AddSeparator("");
            }
            if (CanAdd(track)) menu.AddItem(new GUIContent($"클립 추가 ({t:0.00}초)"), false, () => AddClip(tr, t));
            else menu.AddDisabledItem(new GUIContent("클립 추가 (이 트랙은 1개만)"));
            if (s_hasClipboard && (s_clipboardMulti != null || CanAdd(track))) menu.AddItem(new GUIContent(s_clipboardMulti != null ? $"붙여넣기 — 클립 {s_clipboardMulti.Count}개, 각자 트랙 ({t:0.00}초)" : $"붙여넣기 ({t:0.00}초)"), false, () => PasteAt(tr, t));
            else menu.AddDisabledItem(new GUIContent("붙여넣기"));
            menu.ShowAsContext();
            return;
        }
    }

    bool CanAdd(BossVictoryTrack track) =>
        BossVictoryTimeline.KindOf(track) != BossVictoryTimeline.TrackKind.Single || Timeline.Clips(track).Count == 0;

    static BossVictoryClip NewClip(BossVictoryTrack track, float start) => BossVictoryTimeline.IsBob(track)
        ? new BossVictoryClip { start = start, length = 2f, fadeIn = 0.2f, fadeOut = 0.3f, weight = 1f,
            bobAmplitude = BossVictoryTimeline.DefaultBobAmplitude, bobPeriod = BossVictoryTimeline.DefaultBobPeriod, bobPhase = 0.25f }   // 레퍼런스 값(꼭대기에서 출발)
        : BossVictoryTimeline.IsUvScroll(track)
        ? new BossVictoryClip { start = start, length = 1f, weight = 1f, uvFrom = 0f, uvTo = 1f, uvEase = BossVictoryKeyCurve.EaseInOut }   // 노이즈 한 장 위로, ease in/out
        : BossVictoryTimeline.StartFixed(track)
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
        BossVictoryClip added = NewClip(track, start);
        if (BossVictoryTimeline.HasFades(track)) added.MigrateFadesToKeys();   // 효과 세기 = 키(255 후속 6)
        if (BossVictoryTimeline.IsAssassinDash(track)) InitAssassinDash(clips, ref added);
        clips.Add(added);
        if (track == BossVictoryTrack.CameraCut) Timeline.cameraCutShots.Add(BossVictoryCameraCuts.Cut.Default);
        Select((int)track, clips.Count - 1, -1);
        Save();
        Repaint();
    }

    // 새 어쌔신 순간이동·Q 이동 클립(272 후속) — 길이 = Custom0 → End 구간, 두 점 = 앞 클립(시작 순 마지막) 값, 없으면 기본.
    static void InitAssassinDash(List<BossVictoryClip> clips, ref BossVictoryClip added)
    {
        added.length = BossVictoryAssassinDash.DefaultLength;
        added.fadeIn = added.fadeOut = 0f;
        added.weight = 1f;
        var order = new List<int>(clips.Count);
        BossVictoryAssassinDash.Order(clips, order);
        if (order.Count > 0)
        {
            BossVictoryClip last = clips[order[order.Count - 1]];
            added.dashFrom = last.dashFrom;
            added.dashTo = last.dashTo;
        }
        else
        {
            added.dashFrom = BossVictoryAssassinDash.DefaultFrom;
            added.dashTo = BossVictoryAssassinDash.DefaultTo;
        }
    }

    static Vector3 FlatOffset(Vector3 v) => new Vector3(v.x, 0f, v.z);   // 높이는 런타임이 처치자 발 높이로 맞춘다

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
        if (_multi.Count >= 2) { CopyMulti(); return; }   // 여러 개(296) — 선택 클립 전부(트랙·간격 유지)
        if (!HasClipSelection) return;
        s_clipboardMulti = null;
        s_clipboardTrack = (BossVictoryTrack)_selTrack;
        s_clipboard = Timeline.Clips(s_clipboardTrack)[_selIndex].WithCopiedKeys();
        s_clipboardShot = s_clipboardTrack == BossVictoryTrack.CameraCut ? Timeline.CameraCutShot(_selIndex).WithCopiedKeys() : BossVictoryCameraCuts.Cut.Default;
        s_hasClipboard = true;
    }

    void PasteAt(BossVictoryTrack track, float start)
    {
        if (s_clipboardMulti != null) { PasteMulti(start); return; }   // 여러 개 클립보드(296) — 각자 트랙에
        if (!s_hasClipboard || !CanAdd(track)) return;
        Record("처치 연출 타임라인 붙여넣기");
        BossVictoryClip c = s_clipboard.WithCopiedKeys();   // 키 목록은 클립마다 따로
        if (BossVictoryTimeline.HasFades(track)) c.MigrateFadesToKeys();
        else c.keys = null;
        c.start = Mathf.Max(0f, Snap(start));
        List<BossVictoryClip> clips = Timeline.Clips(track);
        clips.Add(Clamp(track, c));
        if (track == BossVictoryTrack.CameraCut)
        {
            Timeline.SyncCameraCutShots();   // 모자란 앞 번호 보정 뒤 이 클립 값
            Timeline.cameraCutShots[clips.Count - 1] = s_clipboardTrack == BossVictoryTrack.CameraCut ? s_clipboardShot.WithCopiedKeys() : BossVictoryCameraCuts.Cut.Default;
        }
        Select((int)track, clips.Count - 1, -1);
        Save();
        Repaint();
    }

    void DeleteSelected()
    {
        if (_multi.Count >= 2) { DeleteMulti(); return; }   // 여러 개(296)
        if (!HasClipSelection) return;
        if (_selKey >= 0)   // 키 선택 중 = 그 키(시작·끝 키는 지우지 않는다)
        {
            DeleteSelectedKey();
            return;
        }
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
            "이 시각에 처치자가 백대쉬한다(처치자 이동 = 순간이동이면 이 마커는 안 씀 — 어쌔신 순간이동·Q 이동 트랙, 후퇴 차징이면 가까울 때만 E + 백대쉬 — 272) — 하던 동작이 남아 있으면 끊고, 먼저 끝났으면 그 사이 대기 자세. 스킬 연출 시작보다 늦으면 건너뛴다. " +
            "처치자 순서 노드는 각 피어가 자기 화면 시계로 앞으로만 판정 — 스크럽으로 되돌아가지 않는다(다시 보려면 ⏮).",
        BossVictoryMarker.FinisherStart =>
            "클립 시작 = 처치 연출 스킬을 표시용으로 바로 트는 시각 — 백대쉬가 덜 끝났으면 끊고, 처치 순간 쓰던 스킬이 남아 있어도 기다리지 않는다. " +
            "기본 = 백대쉬 시작 + 백대쉬 시간 + 백대쉬 후 대기. 클립 길이 = 그 캐릭터 처치 연출 스킬 클립 전체(공속 100%). " +
            "오른쪽 끝을 앞으로 끌면 끝 자르기 — 그 프레임에서 처치자 애니가 멈춘다(배율 0, 원래 끝보다 늘릴 수 없음). 앞으로만(⏮ 로 다시 보기).",
        BossVictoryMarker.SlowStart =>
            "이 시각부터 처치자가 End 후 배율(아래 칸)로 느려진다. 0 = 끔 — 처치 연출 스킬 클립의 End 이벤트에서. 보스는 '보스 슬로우' 가 꺼져 있을 때만 이 순간을 따른다. 앞으로만(⏮ 로 다시 보기).",
        BossVictoryMarker.BossHitAnim =>
            "이 시각에 보스가 아래 상태(23호 getowned = Boss_23_ParryHit_InPlace, 제자리)를 처음부터 재생한다(보스 ×1). 0 = 처치 순간. 상태 이름이 비면 마지막 피격 자세 그대로. " +
            "보스 배율은 피격 애니·정지·슬로우 중 시각이 가장 늦은 노드가 이긴다. 한 번만 — 되감기는 ⏮.",
        BossVictoryMarker.BossFreeze =>
            "이 시각에 보스를 그 자세로 멈춘다(배율 0). 0 = 끔. 뒤에 슬로우가 오면 슬로우 배율로 다시 움직인다(시각이 가장 늦은 노드가 이김).",
        BossVictoryMarker.BossSlow =>
            "이 시각부터 보스 ×End 후 배율(아래 칸). 0 = 끔 — '느려짐 시작'(그것도 끔이면 처치 연출 스킬 End) 순간을 따른다. 뒤에 정지가 오면 정지.",
        BossVictoryMarker.ChargeStart =>
            "거너(처치자 이동 = 후퇴 차징)만 — 이 시각에 Q 차징(집중 자세)을 시작한다. 보스와 가까우면 그 전에 '백대쉬 시작'에 E 표시 + 백대쉬, 멀어도 이 시각을 따른다(294). " +
            "차징 구체는 아래 '구체 지연'만큼 늦게 나온다. 기본 = 백대쉬 시작 + 0.2초(끌면 그 시각에 고정). 다른 이동 방식에선 쓰지 않는다.",
        BossVictoryMarker.GunnerStage1 or BossVictoryMarker.GunnerStage2 or BossVictoryMarker.GunnerStage3 =>
            "거너 Q 단계 마커(294) — 이 시각에 차징 구체가 아래 '도달 크기'가 된다(앞 마커에서 '곡선'으로 커짐). 도달 순간 최대 충전 알림(켜기·크기), 한계 돌파 색 1→2→3 단계를 차례로 입힌다(차징 시작~1단계 전 = 0). " +
            "3단계부터 발사까지 과충전 루프. 앞 마커보다 앞으로 끌면 앞 마커 시각으로 친다.",
        BossVictoryMarker.GunnerFire =>
            "거너 Q 발사(294) — 이 시각에 구체를 지우고 방출 섬광 → 큰 레이저(벽 무시·보스 관통·길이 칸·착탄 이펙트 없음) → Gunner_Q_Fire(그 End 이벤트 = 처치 연출 스킬 End). " +
            "레이저는 '유지 시간'(타임라인 초) 뒤 꺼진다. 처치 연출 시간 배율과 무관하게 이 시각에 쏜다. 옛 1.2초 고정 대기 대신.",
        _ => "",
    };

    // 선택 클립·마커 수치 직접 입력
    void DrawInspector()
    {
        _snap = EditorGUILayout.ToggleLeft("0.01초 스냅", _snap);
        _helpFold = EditorGUILayout.Foldout(_helpFold, "조작", true);
        if (_helpFold)
            EditorGUILayout.LabelField("휠 = 줌 · 가운데 버튼 끌기 = 이동 · 클립 양 끝 끌기 = 길이 · 우클릭 = 추가/복사/붙여넣기/삭제·다른 캐릭터에 복사 · " +
                "Shift(또는 Ctrl)+클릭 = 여러 개 선택(다시 누르면 빠짐 — 클립·마커·키 섞어서) · 빈 곳 끌기 = 사각형 선택(Shift = 더하기) · 빈 곳 클릭 = 선택 풀기 · " +
                "선택한 것 하나를 끌면 전부 같은 초만큼 이동 · 선택 클립 끝 끌기 = 전부 같은 초만큼 길이 · Del · Ctrl+C/V/D · Ctrl+Z 되돌리기 · Ctrl+Y·Ctrl+Shift+Z 다시 하기", EditorStyles.wordWrappedMiniLabel);
        if (_multi.Count >= 2)
        {
            DrawMultiInspector();   // 여러 개 선택(296) — 개수 + 같은 종류면 공통 칸
            return;
        }
        DrawFinisherEnd();
        using (new EditorGUILayout.VerticalScope())   // 왼쪽 패널(296) — 좁아도 사유가 보이게 세로로
        {
            string reason = SceneViewBlockReason(out _, out _);
            using (new EditorGUI.DisabledScope(reason != null))
                if (GUILayout.Button($"Scene 뷰로 새 카메라 컷 ({_playhead:0.00}초)")) AddCutFromSceneView();
            if (reason != null) GUILayout.Label(reason, EditorStyles.wordWrappedMiniLabel);
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
            if (BossVictoryTimeline.ZeroMeansOff(marker) && Timeline.MarkerTime(marker) > 0f && GUILayout.Button($"끄기 (0 = {OffText(marker)})", GUILayout.Width(200f)))
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
        if (_selPalette)
        {
            DrawPalette();
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
        if (BossVictoryTimeline.IsUvScroll(track))
        {
            c.uvFrom = EditorGUILayout.FloatField(new GUIContent("시작 오프셋(UV)", "노이즈 텍스처 V 오프셋 — 1 = 한 장. 텍스처가 Repeat 라 넘어가면 감긴다."), c.uvFrom);
            c.uvTo = EditorGUILayout.FloatField(new GUIContent("끝 오프셋(UV)", "클립 끝 이후에도 이 값 유지 — 다음 클립은 여기에 더해진다."), c.uvTo);
            c.uvEase = (BossVictoryKeyCurve)EditorGUILayout.Popup("이징", (int)c.uvEase, KeyCurveLabels);
        }
        if (BossVictoryTimeline.IsAssassinDash(track))
        {
            EditorGUILayout.LabelField("실행 순서", $"#{BossVictoryAssassinDash.Number(Timeline.assassinDash, _selIndex)}(시작 순 — 겹치면 앞 클립은 다음 클립 시작에서 끝)");
            c.dashFrom = FlatOffset(EditorGUILayout.Vector3Field(new GUIContent("시작 지점(보스 기준)", "클립 시작에 이 점으로 순간이동. +Z = 보스 정면, 높이는 무시(처치자 발 높이)."), c.dashFrom));
            c.dashTo = FlatOffset(EditorGUILayout.Vector3Field(new GUIContent("도착 지점(보스 기준)", "클립 길이 동안 여기까지 Q 이동(곡선 = 프로필 teleportMoveEase). 끝에 일섬."), c.dashTo));
        }
        if (BossVictoryTimeline.IsBob(track))
        {
            c.bobAmplitude = EditorGUILayout.FloatField(new GUIContent("진폭(화면 높이 %)", "+ = 화면 내용이 위로. 레퍼런스 0.22."), c.bobAmplitude);
            c.bobPeriod = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("주기(초)", "레퍼런스 1.6."), c.bobPeriod));
            c.bobPhase = EditorGUILayout.Slider(new GUIContent("시작 위상(주기)", "0 = 가운데에서 위로 출발 · 0.25 = 꼭대기에서 출발."), c.bobPhase, 0f, 1f);
            c.bobDamping = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("감쇠(1/초)", "e^(−감쇠·경과). 0 = 그대로 · 2 = 0.35초마다 절반."), c.bobDamping));
        }
        if (EditorGUI.EndChangeCheck())
        {
            RecordField("처치 연출 타임라인 클립 수치");
            Timeline.Clips(track)[_selIndex] = Clamp(track, c);
            Save();
        }
        if (BossVictoryTimeline.HasFades(track)) DrawIntensityKeyInspector(track);
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
        DrawApplyToSceneViewButtons();

        EditorGUI.BeginChangeCheck();
        c.start = Mathf.Max(0f, EditorGUILayout.FloatField("시작(초)", c.start));
        EditorGUILayout.LabelField("구간 끝(초)", $"{Timeline.CameraCutEnd(_selIndex):0.00} — 다음 컷 시작(마지막 컷 = 로비 전환)까지 자동");
        shot.anchor = (BossVictoryCameraCuts.Anchor)EditorGUILayout.Popup(new GUIContent("기준",
                "화면을 이 점 + 처치자→보스 수평 축 좌표계로 저장 — 어디서 죽든 같은 구도. '보스 정면' = 보스 발밑 + 보스 수평 정면 축(처치자 위치와 무관 — 보스 기준으로 순간이동하는 어쌔신용, 297). 바꾸면 저장된 화면은 새 기준으로 다시 읽힌다(Scene 뷰로 다시 찍기 권장)."),
            (int)shot.anchor, AnchorLabels);
        if (BossVictoryCameraCuts.FollowsEveryFrame(shot.anchor))
        {
            shot.weaponFollowSmoothing = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("무기 따라가기 부드러움(초)",
                "기준 '처치자 무기' — 원점 = 무기 위치(매 프레임), 축 = 처치자→보스 수평(+X = 오른쪽). 키 = 이 좌표계라 '총 오른쪽 얼마나'. " +
                "이 값 = 무기 위치를 따라가는 지수 감쇠 시간(초), 0 = 딱 붙음. 무기 = BossVictoryWeaponMarker → WeaponTransformRelay 첫 무기, 없으면 처치자 발밑."),
                shot.weaponFollowSmoothing));
        }
        shot.smoothPath = EditorGUILayout.Toggle(new GUIContent("부드러운 경로",
            "키 3개 이상이면 위치를 Catmull-Rom 곡선으로 잇는다(키를 지나며 꺾이지 않음). 끄면 키 사이 직선."), shot.smoothPath);
        shot.hideWalls = EditorGUILayout.Toggle(new GUIContent("벽 숨김",
            "켜면 이 컷 카메라를 가리는 벽을 끈다(이동 중이면 매 프레임 그 위치로). 끄면 이 컷 동안 벽 숨김 없음 — 앞 컷에서 숨겼던 벽도 다시 보인다."), shot.hideWalls);
        if (EditorGUI.EndChangeCheck())
        {
            RecordField("처치 연출 타임라인 카메라 컷 수치");
            Timeline.cameraCut[_selIndex] = c;
            Timeline.cameraCutShots[_selIndex] = shot;
            Save();
        }
        DrawCutBlendInspector();
        DrawCutKeyInspector();
    }

    // ── 'Scene 뷰로' — 지금 보고 있는 Scene 뷰 화면을 컷 화면으로(255 후속 2) ──
    // 저장 = 기준 좌표계(기준점 + 처치자→보스 축). 그 기준이 되는 처치자·보스 위치:
    // Play 중 연출이 돌면 지금 컷이 잡은 위치(컷 전이면 지금 위치) · 아니면 이 에디터 세션에서 마지막으로 본 회차 위치(SessionState). 없으면 버튼을 막는다.

    // ── 순간이동 Q 두 점(272 후속) — 이 창이 열려 있고 프로필 이동이 '순간이동'이면 Scene 뷰에 클립마다 시작·도착 지점·경로(#n) + 선택 클립 끌기 핸들 ──
    // 기준 보스 = 재생 중 보스 → 이 세션 마지막 회차 보스(SessionState) → 열린 씬의 23호. 저장 = 보스 기준 오프셋(BossVictoryKillerMoves.BossLocalOffset, 높이 0).

    const string BossPoseKey = "BossVictoryTimeline.LastBossPose";
    static readonly Color TeleportColor = new Color(0.75f, 0.45f, 1f);
    static readonly Color ArrivalColor = new Color(1f, 0.55f, 0.85f);

    static bool TryGetBossPose(out Vector3 position, out Quaternion rotation, out string source)
    {
        GameObject boss = EditorApplication.isPlaying && BossVictoryGlitchPresentation.Instance != null ? BossVictoryGlitchPresentation.Instance.BossObject : null;
        if (boss != null)
        {
            position = boss.transform.position;
            rotation = boss.transform.rotation;
            SessionState.SetBool(BossPoseKey, true);
            SessionState.SetVector3(BossPoseKey + ".Pos", position);
            SessionState.SetVector3(BossPoseKey + ".Euler", rotation.eulerAngles);
            source = $"재생 중 보스 {boss.name}";
            return true;
        }
        if (SessionState.GetBool(BossPoseKey, false))
        {
            position = SessionState.GetVector3(BossPoseKey + ".Pos", Vector3.zero);
            rotation = Quaternion.Euler(SessionState.GetVector3(BossPoseKey + ".Euler", Vector3.zero));
            source = "마지막 회차 보스";
            return true;
        }
        TwentyThreeBoss scene = Object.FindFirstObjectByType<TwentyThreeBoss>();
        position = scene != null ? scene.transform.position : default;
        rotation = scene != null ? scene.transform.rotation : Quaternion.identity;
        source = scene != null ? $"씬의 {scene.name}" : "";
        return scene != null;
    }

    // 어쌔신 순간이동·Q 이동 클립(272 후속) — 모든 클립 두 점·경로 + '#n' 표시, 선택한 클립만 밝게 + 핸들.
    void OnSceneGUI(SceneView view)
    {
        if (_profile == null) return;
        bool dash = _profile.killerMove == BossVictoryKillerMove.TeleportDash;
        bool sword = ShowsFallingSword(_profile);
        bool pass = _profile.killerMove == BossVictoryKillerMove.RetreatCharge && _profile.gunnerQ != null && _profile.gunnerQ.useBeamPassPoint;
        if (!dash && !sword && !pass) return;
        if (!TryGetBossPose(out Vector3 bossPos, out Quaternion bossRot, out string source)) return;
        List<BossVictoryClip> clips = Timeline.assassinDash;

        Handles.color = new Color(1f, 1f, 1f, 0.6f);
        Handles.DrawWireDisc(bossPos, Vector3.up, 0.6f);
        Handles.DrawAAPolyLine(2f, bossPos, bossPos + BossVictoryKillerMoves.Yaw(bossRot) * Vector3.forward * 1.5f);   // 보스 정면(+Z)
        Handles.Label(bossPos + Vector3.up * 0.8f, $"기준: {source}");
        if (sword) DrawFallingSwordHandle(bossPos, bossRot);
        if (pass) DrawBeamPassHandle(bossPos, bossRot);
        if (!dash || clips == null || clips.Count == 0) return;

        int selected = _selTrack == (int)BossVictoryTrack.AssassinDash && HasClipSelection ? _selIndex : -1;
        var order = new List<int>(clips.Count);
        BossVictoryAssassinDash.Order(clips, order);
        for (int k = 0; k < order.Count; k++)
        {
            int i = order[k];
            bool on = i == selected;
            float alpha = selected < 0 || on ? 1f : 0.35f;
            Vector3 a = BossVictoryKillerMoves.BossPoint(bossPos, bossRot, clips[i].dashFrom);
            Vector3 b = BossVictoryKillerMoves.BossPoint(bossPos, bossRot, clips[i].dashTo);
            Handles.color = WithAlpha(ArrivalColor, alpha);
            Handles.DrawAAPolyLine(on ? 6f : 3f, a, b);
            Vector3 path = b - a;
            if (path.sqrMagnitude > 0.01f) Handles.ConeHandleCap(0, b - path.normalized * 0.3f, Quaternion.LookRotation(path), 0.35f, EventType.Repaint);
            Handles.color = WithAlpha(TeleportColor, alpha);
            Handles.DrawWireDisc(a, Vector3.up, on ? 0.5f : 0.4f);
            Handles.color = WithAlpha(ArrivalColor, alpha);
            Handles.DrawWireDisc(b, Vector3.up, on ? 0.5f : 0.4f);
            Handles.Label(a + Vector3.up * 0.6f, $"#{k + 1} 시작");
            Handles.Label(b + Vector3.up * 0.6f, $"#{k + 1} 도착");
        }
        if (selected < 0) return;

        BossVictoryClip c = clips[selected];
        EditorGUI.BeginChangeCheck();
        Vector3 na = Handles.PositionHandle(BossVictoryKillerMoves.BossPoint(bossPos, bossRot, c.dashFrom), Quaternion.identity);
        Vector3 nb = Handles.PositionHandle(BossVictoryKillerMoves.BossPoint(bossPos, bossRot, c.dashTo), Quaternion.identity);
        if (!EditorGUI.EndChangeCheck()) return;
        RecordField("처치 연출 순간이동 지점");
        c.dashFrom = FlatOffset(BossVictoryKillerMoves.BossLocalOffset(bossPos, bossRot, na));
        c.dashTo = FlatOffset(BossVictoryKillerMoves.BossLocalOffset(bossPos, bossRot, nb));
        clips[selected] = c;
        Save();
        Repaint();
    }

    static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

    static readonly Color BeamPassColor = new Color(0.45f, 0.9f, 1f);

    // 거너 레이저 통과 지점(301 — 프로필 gunnerQ.beamPassPoint, 보스 기준 x 옆·y 위·z 앞): 점 + 바닥까지 세로선 + 끌기 핸들(높이 포함 3D 저장).
    void DrawBeamPassHandle(Vector3 bossPos, Quaternion bossRot)
    {
        Vector3 point = BossVictoryKillerMoves.BossPoint(bossPos, bossRot, _profile.gunnerQ.beamPassPoint);
        Vector3 floor = new Vector3(point.x, bossPos.y, point.z);
        Handles.color = BeamPassColor;
        Handles.SphereHandleCap(0, point, Quaternion.identity, 0.35f, EventType.Repaint);
        Handles.DrawDottedLine(floor, point, 3f);
        Handles.DrawWireDisc(floor, Vector3.up, 0.4f);
        Handles.Label(point + Vector3.up * 0.4f, $"레이저 통과 지점 · 높이 {_profile.gunnerQ.beamPassPoint.y:0.##}m");
        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.PositionHandle(point, Quaternion.identity);
        if (!EditorGUI.EndChangeCheck()) return;
        Undo.RecordObject(_profile, "처치 연출 거너 레이저 통과 지점");
        _profile.gunnerQ.beamPassPoint = BossVictoryKillerMoves.BossLocalOffset(bossPos, bossRot, moved);
        EditorUtility.SetDirty(_profile);
        if (ProfileIsAsset) AssetDatabase.SaveAssetIfDirty(_profile);
        _profileSo?.Update();
        Repaint();
    }

    // 처치 연출 낙하 검 자리(팔라딘 — 프로필 fallingSwordOffset): 보스 기준 점 + 바닥까지 세로선 + 끌기 핸들(높이 0 으로 저장).
    static bool ShowsFallingSword(BossVictoryProfile p) =>
        p.killerMove != BossVictoryKillerMove.TeleportDash && (BossVictoryGlitchPresentation.UsesKillerSword(p) || p.finisherSlot == PlayerSkillSlot.Ultimate);

    void DrawFallingSwordHandle(Vector3 bossPos, Quaternion bossRot)
    {
        Vector3 point = BossVictoryKillerMoves.BossPoint(bossPos, bossRot, FlatOffset(_profile.fallingSwordOffset));
        Handles.color = FallingSwordColor;
        Handles.DrawWireDisc(point, Vector3.up, 0.7f);
        Handles.DrawAAPolyLine(3f, point, point + Vector3.up * 3f);
        Handles.Label(point + Vector3.up * 3.2f, _profile.fallingSwordLife > 0f ? $"낙하 검 · 수명 {_profile.fallingSwordLife:0.##}초" : "낙하 검 · 수명 = 엔트리");
        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.PositionHandle(point, Quaternion.identity);
        if (!EditorGUI.EndChangeCheck()) return;
        Undo.RecordObject(_profile, "처치 연출 낙하 검 자리");
        _profile.fallingSwordOffset = FlatOffset(BossVictoryKillerMoves.BossLocalOffset(bossPos, bossRot, moved));
        EditorUtility.SetDirty(_profile);
        if (ProfileIsAsset) AssetDatabase.SaveAssetIfDirty(_profile);
        _profileSo?.Update();
        Repaint();
    }

    static readonly Color FallingSwordColor = new Color(1f, 0.85f, 0.3f);

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
        s.BossForward = SessionState.GetVector3(SubjectsKey + ".BossForward", Vector3.forward);   // 기준 '보스 정면'(297)
        s.HasWeapon = SessionState.GetBool(SubjectsKey + ".HasWeapon", false);
        s.Weapon = SessionState.GetVector3(SubjectsKey + ".Weapon", Vector3.zero);
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
        SessionState.SetVector3(SubjectsKey + ".BossForward", s.BossForward);
        SessionState.SetBool(SubjectsKey + ".HasWeapon", s.HasWeapon);
        SessionState.SetVector3(SubjectsKey + ".Weapon", s.Weapon);
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
            int key = _selKey >= 0 ? _selKey : 0;
            if (GUILayout.Button($"지금 Scene 뷰로 이 키 (#{key + 1})")) CaptureSceneView(s, key);
            if (GUILayout.Button("지금 Scene 뷰로 끝 화면")) CaptureSceneView(s, -1);
        }
        string weaponNote = BossVictoryCameraCuts.FollowsEveryFrame(shot.anchor) ? (s.HasWeapon ? " · 무기 위치 있음" : " · 무기 못 찾음 → 처치자 발밑") : "";
        EditorGUILayout.LabelField(reason ?? $"기준 = {source} 처치자·보스 위치 · 컷 기준 '{AnchorLabel(shot.anchor)}'{weaponNote}", EditorStyles.wordWrappedMiniLabel);
    }

    // ── 컷 → Scene 뷰(267 후속) — 'Scene 뷰로' 의 반대. 자세 = 런타임 재생과 같은 BossVictoryCameraCuts.Pose(컷, 기준, 진행) ──
    // 컷 시작(진행 0) 또는 재생 헤드 시각(CameraCutProgress — 이동 컷이면 그 순간 위치). 블렌드는 앞 컷과 섞는 구간이라 넣지 않는다(이 컷 자신의 화면).

    static string ApplyToSceneViewBlockReason(out BossVictoryCameraCuts.Subjects s, out string source)
    {
        if (!TryGetSubjects(out s, out source)) return "기준 위치 없음 — 처치자·보스 기준 좌표계라 디버그 씬(DebugBossVictory)에서 Play 해 연출을 한 번 돌리면 생긴다";
        if (SceneView.lastActiveSceneView == null) return "Scene 뷰 없음 — Scene 뷰 창을 열어 둘 것";
        return null;
    }

    void DrawApplyToSceneViewButtons()
    {
        string reason = ApplyToSceneViewBlockReason(out BossVictoryCameraCuts.Subjects s, out _);
        float start = Timeline.cameraCut[_selIndex].start, end = Timeline.CameraCutEnd(_selIndex);
        bool inCut = _playhead >= start && _playhead <= end;
        using (new EditorGUILayout.HorizontalScope())
        using (new EditorGUI.DisabledScope(reason != null))
        {
            if (GUILayout.Button(new GUIContent("이 카메라 뷰로 Scene 카메라 세팅하기",
                    "Scene 뷰 카메라를 이 컷 시작 화면(위치·회전·FOV)으로 맞춘다 — 런타임 재생과 같은 자세 계산."))) ApplyCutToSceneView(s, 0f);
            using (new EditorGUI.DisabledScope(!inCut))
                if (GUILayout.Button(new GUIContent($"재생 헤드 {_playhead:0.00}초 화면으로",
                        inCut ? "이동 컷이면 재생 헤드 순간의 위치·회전·FOV 로 맞춘다." : "재생 헤드가 이 컷 구간 밖이다.")))
                    ApplyCutToSceneView(s, Timeline.CameraCutProgress(_selIndex, _playhead));
        }
        if (reason != null) EditorGUILayout.LabelField(reason, EditorStyles.wordWrappedMiniLabel);
    }

    void ApplyCutToSceneView(in BossVictoryCameraCuts.Subjects s, float u)
    {
        SceneView sv = SceneView.lastActiveSceneView;
        if (sv == null || !HasClipSelection || _selTrack != (int)BossVictoryTrack.CameraCut) return;
        BossVictoryCameraCuts.Pose(Timeline.CameraCutShot(_selIndex), s, u, out Vector3 pos, out Quaternion rot, out float fov);
        sv.in2DMode = false;
        sv.orthographic = false;
        SceneView.CameraSettings cs = sv.cameraSettings;
        cs.fieldOfView = fov;
        sv.cameraSettings = cs;
        // Scene 뷰 카메라 = 피벗 − 회전 앞쪽 × 거리(크기·FOV 로 정해짐). 크기를 먼저 맞추고 그 거리만큼 앞에 피벗을 둔다.
        sv.LookAtDirect(pos, rot, 1f);
        sv.LookAtDirect(pos + rot * Vector3.forward * sv.cameraDistance, rot, 1f);
        sv.Repaint();
    }

    // key = 그 키 화면을 지금 Scene 뷰로 · -1 = 끝 화면(키 1개면 끝 키를 만들어 이동 컷으로).
    void CaptureSceneView(BossVictoryCameraCuts.Subjects s, int key)
    {
        if (!HasClipSelection || _selTrack != (int)BossVictoryTrack.CameraCut || !TryGetSceneView(out Vector3 pos, out Quaternion rot, out float fov)) return;
        Record(key < 0 ? "처치 연출 타임라인 컷 끝 화면 = Scene 뷰" : "처치 연출 타임라인 컷 키 = Scene 뷰");
        Timeline.SyncCameraCutShots();
        BossVictoryCameraCuts.Cut shot = Timeline.cameraCutShots[_selIndex];
        shot.MigrateToKeys();
        BossVictoryCameraCuts.ToLocal(shot.anchor, s, pos, rot, out Vector3 lp, out Quaternion lr);
        if (key < 0)
        {
            if (shot.keys.Count == 1) shot.keys.Add(BossVictoryCameraCuts.CutKey.Make(1f, lp, lr, fov));
            key = shot.keys.Count - 1;
        }
        key = Mathf.Clamp(key, 0, shot.keys.Count - 1);
        BossVictoryCameraCuts.CutKey k = shot.keys[key];
        k.position = lp;
        k.rotation = lr;
        k.fov = fov;
        shot.keys[key] = k;
        Timeline.cameraCutShots[_selIndex] = shot;
        _selKey = key;
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
        shot.keys = new List<BossVictoryCameraCuts.CutKey> { BossVictoryCameraCuts.CutKey.Make(0f, lp, lr, fov) };
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
        if (so.ApplyModifiedProperties()) SavePalette();   // 공용 색이 바뀌었을 수 있다 — Play 중 팔레트도 다시(271)
    }

    SerializedObject _profileSo;

    // 캐릭터별 칸(267) — 지금 프로필 SO 칸. Undo 는 SerializedObject 가.
    void DrawProfileFields(string title, params string[] names)
    {
        if (_profileSo == null || _profileSo.targetObject != _profile) _profileSo = new SerializedObject(_profile);
        _profileSo.Update();
        if (title != null) EditorGUILayout.Space(4f);
        if (title != null) EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
        foreach (string n in names)
        {
            SerializedProperty p = _profileSo.FindProperty(n);
            if (p != null) EditorGUILayout.PropertyField(p, true);
        }
        if (_profileSo.ApplyModifiedProperties()) SavePalette();   // 기준 색 칸 등 — Play 중 팔레트도 다시(271)
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

    // RECTIFY 안쪽 원 3개(267 후속) — 원마다 접는 칸(① 마젠타 · ② 블루 · ③ 흰색).
    static readonly string[] CircleLabels = { "① 마젠타 원", "② 블루 원", "③ 흰 원(회색 → 흰 → 확장)" };

    void DrawRectifyCircleFields()
    {
        SerializedObject so = SettingsObject();
        SerializedProperty arr = so.FindProperty(nameof(BossVictoryGlitchSettings.rectifyCircles));
        if (arr == null) return;
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("안쪽 원(생성 시각·시작 반지름·페이드·확장·중심 — 색은 팔레트 칸)", EditorStyles.miniBoldLabel);
        if (arr.arraySize != BossVictoryRectifyFill.CircleCount)
        {
            EditorGUILayout.HelpBox($"원은 {BossVictoryRectifyFill.CircleCount}개만 쓴다(지금 {arr.arraySize}개).", MessageType.Warning);
            if (GUILayout.Button("원 3개로 맞추기")) arr.arraySize = BossVictoryRectifyFill.CircleCount;
        }
        for (int i = 0; i < arr.arraySize && i < CircleLabels.Length; i++)
            EditorGUILayout.PropertyField(arr.GetArrayElementAtIndex(i), new GUIContent(CircleLabels[i]), true);
        ApplySettings(so);
    }

    // 마커 노드 — 시각 외 옵션.
    void DrawMarkerOptions(BossVictoryMarker marker)
    {
        switch (marker)
        {
            case BossVictoryMarker.BackDashStart:
                DrawProfileFields("처치자 이동(272 · 이 캐릭터 프로필)", nameof(BossVictoryProfile.killerMove));
                if (_profile.killerMove == BossVictoryKillerMove.TeleportDash)
                {
                    EditorGUILayout.HelpBox("순간이동(272 후속) — 이 마커는 쓰지 않는다. 순간이동·Q 이동은 '어쌔신 순간이동·Q 이동' 트랙 클립이 낸다(트랙 이름을 누르면 애니 상태·곡선·정지 프레임).", MessageType.Info);
                    break;
                }
                if (_profile.killerMove == BossVictoryKillerMove.RetreatCharge)
                {
                    DrawProfileFields("후퇴 차징(가까우면 이 마커에 후퇴 스킬 + 백대쉬 · Q 차징은 가깝든 멀든 '거너 Q' 줄 차징 시작 마커에 — 294)",
                        nameof(BossVictoryProfile.retreatDistance), nameof(BossVictoryProfile.retreatSkillSlot));
                }
                DrawProfileFields("백대쉬(처치 연출 전용 · 이 캐릭터 프로필)",
                    nameof(BossVictoryProfile.backDashDistance), nameof(BossVictoryProfile.backDashDuration),
                    nameof(BossVictoryProfile.backDashEase), nameof(BossVictoryProfile.backDashAngle),
                    nameof(BossVictoryProfile.backDashIdleDelay), nameof(BossVictoryProfile.finisherWithBackDash));
                EditorGUILayout.LabelField("방향 = 보스 반대쪽을 보정 각만큼 돌림(+ = 시계 방향). 후 대기 = 백대쉬 끝 ~ Idle 복귀. 리액션 애니는 보스 등장 밀림과 같다(등장 연출 값은 그대로). 기본값 = 보스 등장 밀림 값.", EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("스킬 연출 시작 = 백대쉬 시작 + 시간 + 후 대기"))
                {
                    Record("처치 연출 타임라인 스킬 연출 시작 맞추기");
                    Timeline.SetMarkerTime(BossVictoryMarker.FinisherStart, Snap(BossVictoryKillerSequence.DefaultFinisherStart(
                        Timeline.backDashStartTime, _profile.backDashDuration, _profile.backDashIdleDelay)));
                    Save();
                }
                break;
            case BossVictoryMarker.FinisherStart:
                DrawClipNodeOptions(BossVictoryMarker.FinisherStart);
                DrawProfileFields("처치 연출 스킬 칸(이 캐릭터 프로필)", nameof(BossVictoryProfile.finisherSlot));
                DrawProfileFields("처치자 검(269 · 팔라딘만, 이 캐릭터 프로필) — 손 검 번개 = 연출 시작~끝 · 낙하 검 배율 = 이 연출의 R 낙하 검",
                    nameof(BossVictoryProfile.handSwordLightning), nameof(BossVictoryProfile.fallingSwordScale),
                    nameof(BossVictoryProfile.fallingSwordOffset), nameof(BossVictoryProfile.fallingSwordLife));
                EditorGUILayout.LabelField("낙하 검 자리 = 보스 기준(+Z = 보스 정면, 바닥에 꽂힘) — 이 창이 열려 있으면 Scene 뷰 노란 원 핸들로 끈다. 생기는 시각 = 처치 연출 스킬 클립 Custom0 이벤트, 수명 0 = 엔트리 그대로.", EditorStyles.wordWrappedMiniLabel);
                break;
            case BossVictoryMarker.SlowStart:
                DrawProfileFields("처치자 End 후 배율", nameof(BossVictoryProfile.killerAfterFinisherScale));
                break;
            case BossVictoryMarker.BossHitAnim:
                DrawClipNodeOptions(BossVictoryMarker.BossHitAnim);
                DrawSettingsFields("보스 피격 애니(비우면 마지막 자세)", nameof(BossVictoryGlitchSettings.bossHitStateName), nameof(BossVictoryGlitchSettings.bossHitFadeSeconds));
                break;
            case BossVictoryMarker.BossSlow:
                DrawProfileFields("보스 End 후 배율", nameof(BossVictoryProfile.bossAfterFinisherScale));
                break;
            case BossVictoryMarker.ChargeStart:
                DrawProfileFields("거너 Q 차징(294 · 이 캐릭터 프로필) — 시작 크기 · 구체 지연(타임라인 초) · 차징 오프셋(총구 기준 x 옆·y 위·z 앞, 구체·알림·과충전)",
                    GunnerQField(nameof(BossVictoryGunnerQ.startScale)), GunnerQField(nameof(BossVictoryGunnerQ.sphereDelay)),
                    GunnerQField(nameof(BossVictoryGunnerQ.chargeOffset)));
                DrawProfileFields(FxTitle("차징 시작"), GunnerQField(nameof(BossVictoryGunnerQ.fxStart)));
                if (Timeline.chargeStartTime >= 0f && GUILayout.Button($"자동으로(백대쉬 시작 + {BossVictoryTimeline.DefaultChargeDelay:0.0}초)", GUILayout.Width(220f)))
                {
                    Record("처치 연출 타임라인 Q 차징 시작 자동");
                    Timeline.chargeStartTime = -1f;
                    Save();
                }
                DrawGunnerQNote();
                break;
            case BossVictoryMarker.GunnerStage1:
            case BossVictoryMarker.GunnerStage2:
            case BossVictoryMarker.GunnerStage3:
            {
                int k = marker - BossVictoryMarker.GunnerStage1 + 1;
                string stage = GunnerQField($"stage{k}");
                DrawProfileFields($"거너 Q {k}단계(294 · 이 캐릭터 프로필) — 도달 크기·곡선(앞 마커 → 이 마커)·최대 충전 알림 켜기·크기 · 한계 돌파 색 {k}",
                    $"{stage}.{nameof(BossVictoryGunnerQ.Stage.sphereScale)}", $"{stage}.{nameof(BossVictoryGunnerQ.Stage.ease)}",
                    $"{stage}.{nameof(BossVictoryGunnerQ.Stage.burst)}", $"{stage}.{nameof(BossVictoryGunnerQ.Stage.burstScale)}");
                DrawProfileFields($"{k}단계 도착 카메라 흔들림(295) — 세기 0 = 없음 · 길이 0 = 0.2초",
                    $"{stage}.{nameof(BossVictoryGunnerQ.Stage.shakeIntensity)}", $"{stage}.{nameof(BossVictoryGunnerQ.Stage.shakeDuration)}");
                DrawProfileFields(FxTitle($"{k}단계"), GunnerQField($"fxStage{k}"));
                DrawProfileFields("최대 충전 알림 양·밝기 배율(295 · 모든 단계 공용, 1 = 그대로)",
                    GunnerQField(nameof(BossVictoryGunnerQ.burstAmount)), GunnerQField(nameof(BossVictoryGunnerQ.burstBrightness)));
                if (k == 3) DrawProfileFields("과충전 루프(3단계 ~ 발사) — 크기 · 양·밝기 배율(295, 1 = 그대로)", GunnerQField(nameof(BossVictoryGunnerQ.overchargeScale)),
                    GunnerQField(nameof(BossVictoryGunnerQ.overchargeAmount)), GunnerQField(nameof(BossVictoryGunnerQ.overchargeBrightness)));
                DrawGunnerQNote();
                break;
            }
            case BossVictoryMarker.GunnerFire:
                DrawProfileFields("처치 연출 전용 레이저 이펙트(302) — 프리팹(루트 BossVictoryBeamRig: Body +Z 1m · Muzzle · End, 폴더 5.VFX/BossVictory/Gunner) · 멈춤 시점에 같이 멈추기(끄면 루프). " +
                                  "넣으면 아래 3D 원통·흐름 속도는 안 먹는다(회색 — 모양·흐름 = 프리팹·머티리얼). 색 덮어쓰기·한계 돌파 색은 색상(hue)만 먹고, 굵기 배율은 프리팹 Body 굵기·플레어 크기에 곱해짐, 페이드 0 = 기본 0.15초. 비우면 기존 레이저",
                    GunnerQField(nameof(BossVictoryGunnerQ.victoryBeamPrefab)), GunnerQField(nameof(BossVictoryGunnerQ.victoryBeamFreeze)));
                DrawProfileFields("302 새 레이저 층별 굵기 — 안쪽 파란 몸통(Glow·흰 심) · 바깥 흰 공기막(Wisp). 1 = 저작 크기, 레이저 굵기 배율과 곱해짐",
                    GunnerQField(nameof(BossVictoryGunnerQ.victoryBeamInnerScale)), GunnerQField(nameof(BossVictoryGunnerQ.victoryBeamAirScale)));
                if (GUILayout.Button("뼈대 이펙트 만들기 (302 — 없는 파일만, 비어 있으면 거너 프로필에 연결)", GUILayout.Width(420f)))
                {
                    BossVictoryGunnerBeamAuthoring.Create();
                    GUIUtility.ExitGUI();
                }
                DrawProfileFields("거너 Q 발사(294 · 이 캐릭터 프로필) — 방출 섬광 크기 · 레이저 굵기 배율(보이는 것만) · 3D 원통(300 — 몸통을 원통 메시로, 낮은 앵글에서도 두께) · 길이(m, 벽 무시·보스 관통) · 유지 시간(타임라인 초) · 레이저 멈춤 시점(발사 뒤 초, 299 — 이때 정지 화면 → 유지 끝에 재개, 유지 ≤ 이 값이면 안 멈춤) · 발사 오프셋(총구 기준 x 옆·y 위·z 앞, 섬광·레이저 시작점)",
                    GunnerQField(nameof(BossVictoryGunnerQ.ventScale)), GunnerQField(nameof(BossVictoryGunnerQ.beamWidthScale)));
                using (new EditorGUI.DisabledScope(RigBeamSet))   // 302 — 새 레이저면 기존 레이저 전용 칸은 회색
                    DrawProfileFields(null, GunnerQField(nameof(BossVictoryGunnerQ.beamMesh3D)));
                DrawProfileFields(null,
                    GunnerQField(nameof(BossVictoryGunnerQ.beamLength)), GunnerQField(nameof(BossVictoryGunnerQ.beamHoldSeconds)),
                    GunnerQField(nameof(BossVictoryGunnerQ.beamFreezeAt)),
                    GunnerQField(nameof(BossVictoryGunnerQ.fireOffset)));
                DrawProfileFields("발사 연출(295) — 카메라 흔들림(세기 0 = 없음) · 총구 번쩍 라이트(세기 0 = 없음, 발사 소켓) · 레이저 밝기·색 덮어쓰기(끄면 한계 돌파 색)·페이드아웃(초, 유지 시간 뒤 · 0 = 바로 끔)",
                    GunnerQField(nameof(BossVictoryGunnerQ.fireShakeIntensity)), GunnerQField(nameof(BossVictoryGunnerQ.fireShakeDuration)),
                    GunnerQField(nameof(BossVictoryGunnerQ.fireFlashIntensity)), GunnerQField(nameof(BossVictoryGunnerQ.fireFlashDuration)),
                    GunnerQField(nameof(BossVictoryGunnerQ.fireFlashRange)), GunnerQField(nameof(BossVictoryGunnerQ.fireFlashColor)),
                    GunnerQField(nameof(BossVictoryGunnerQ.beamBrightness)), GunnerQField(nameof(BossVictoryGunnerQ.beamColorOverride)),
                    GunnerQField(nameof(BossVictoryGunnerQ.beamColor)), GunnerQField(nameof(BossVictoryGunnerQ.beamFadeSeconds)));
                DrawProfileFields("레이저 흐름·진동(301) — 흐름 = 몸통 텍스처·노이즈가 총구 → 끝으로 흐르는 속도(UV/초, 0 = 재질 값, 음수 = 반대로) · 땅울림 카메라 세기·주파수 · 몸·총 떨림(m) · 진동 길이(발사 뒤 초, 레이저 유지와 별개)·올림·내림(초) — 세기 0 = 없음");
                using (new EditorGUI.DisabledScope(RigBeamSet))
                    DrawProfileFields(null, GunnerQField(nameof(BossVictoryGunnerQ.beamFlowSpeed)), GunnerQField(nameof(BossVictoryGunnerQ.beamNoiseFlowSpeed)));
                DrawProfileFields(null,
                    GunnerQField(nameof(BossVictoryGunnerQ.vibrationCameraIntensity)), GunnerQField(nameof(BossVictoryGunnerQ.vibrationFrequency)),
                    GunnerQField(nameof(BossVictoryGunnerQ.vibrationBodyIntensity)), GunnerQField(nameof(BossVictoryGunnerQ.vibrationSeconds)),
                    GunnerQField(nameof(BossVictoryGunnerQ.vibrationRampIn)), GunnerQField(nameof(BossVictoryGunnerQ.vibrationRampOut)));
                DrawProfileFields("레이저 통과 지점(301) — 켜면 총구 → 이 점(보스 기준 x 옆·y 위·z 앞, m)을 지나 길이만큼(위아래로 기울 수 있음). 이 마커를 고르면 Scene 뷰 핸들로 끈다. 끄면 보스 쪽 수평",
                    GunnerQField(nameof(BossVictoryGunnerQ.useBeamPassPoint)), GunnerQField(nameof(BossVictoryGunnerQ.beamPassPoint)));
                DrawProfileFields("레이저 = 광원(301) — 씬 조명 어둡게(0~1, 간파 암순응과 같은 공유 배율) · 화면 대비(0~100) · 레이저 위 포인트 라이트 수·세기·범위(레이저 색) · 차오름(발사부터)·돌아옴(유지 끝부터) 초 — 0 = 없음, 글리치 흑백·호박색 위에 겹친다",
                    GunnerQField(nameof(BossVictoryGunnerQ.lightSceneDim)), GunnerQField(nameof(BossVictoryGunnerQ.lightContrast)),
                    GunnerQField(nameof(BossVictoryGunnerQ.beamLightCount)), GunnerQField(nameof(BossVictoryGunnerQ.beamLightIntensity)),
                    GunnerQField(nameof(BossVictoryGunnerQ.beamLightRange)), GunnerQField(nameof(BossVictoryGunnerQ.lightInSeconds)),
                    GunnerQField(nameof(BossVictoryGunnerQ.lightRecoverSeconds)));
                DrawProfileFields("반동 밀림(301) — 발사부터 레이저 반대쪽(수평)으로 등속(LINEAR) 거리(m)·시간(초). 오너가 실제로 움직이고 벽에서 멈춘다(다른 피어는 위치 복제). 0 = 없음",
                    GunnerQField(nameof(BossVictoryGunnerQ.recoilDistance)), GunnerQField(nameof(BossVictoryGunnerQ.recoilSeconds)));
                DrawGunnerQNote();
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

    // 거너 Q 칸(294) — 프로필 gunnerQ 안 칸 경로.
    // 302 — 처치 연출 전용 레이저 프리팹이 들어 있으면 기존 레이저 전용 칸(3D 원통·흐름 속도)을 회색으로.
    bool RigBeamSet => _profile != null && _profile.gunnerQ != null && _profile.gunnerQ.victoryBeamPrefab != null;

    static string GunnerQField(string name) => $"{nameof(BossVictoryProfile.gunnerQ)}.{name}";

    // 차징 이펙트 마커 값(295) 제목 — 앞 마커 → 이 마커 사이를 구체 크기와 같은 곡선으로 보간.
    static string FxTitle(string at) =>
        $"차징 이펙트 · {at}(295) — 파트 빛줄기(Stretch)·입자(Mesh)·코어별 크기·양·반복 간격(0 = 프리팹)·수명·속도·밝기·길이(빛줄기만) 배율 · 총구 포인트 라이트(세기 0 = 없음)";

    void DrawGunnerQNote()
    {
        EditorGUILayout.LabelField("거너 Q 줄 = 차징 시작 → 1단계 → 2단계 → 3단계 → 발사(마커 5개, 끌기·스냅·Undo 기존대로). 시각 = 타임라인 초 — 처치자·이펙트 시간 배율과 무관하게 마커 시각에. " +
            "전 피어가 같은 타임라인으로 각자 재생(새 RPC·복제 값 없음, 이 피어 표시 전용).", EditorStyles.wordWrappedMiniLabel);
        if (_profile.killerMove != BossVictoryKillerMove.RetreatCharge)
            EditorGUILayout.HelpBox("이 프로필은 처치자 이동이 '후퇴 차징'이 아니라 거너 Q 줄을 쓰지 않는다.", MessageType.Info);
    }

    // 트랙 노드(트랙 이름 또는 그 클립 선택) — 그 트랙 모양 값.
    void DrawTrackShape(BossVictoryTrack track)
    {
        switch (track)
        {
            case BossVictoryTrack.AssassinDash:
                if (_profile.killerMove != BossVictoryKillerMove.TeleportDash)
                    EditorGUILayout.HelpBox("이 캐릭터 처치자 이동이 순간이동이 아니라 이 트랙은 쓰지 않는다(백대쉬 시작 마커의 '처치자 이동').", MessageType.Info);
                EditorGUILayout.LabelField("클립 = 시작에 그 클립 시작 지점으로 순간이동 → 길이 동안 도착 지점까지 Q 이동 → 끝에 일섬. 여러 개 = 시작 순 #1 #2 …, 겹치면 앞 클립은 다음 클립 시작에서 끝. " +
                    "클립마다 애니를 Custom0 프레임부터 다시 틀고 Custom0→End 가 클립 길이에 맞게 속도를 맞춘다. 클립 사이엔 제자리에서 애니가 이어지고, 마지막 뒤엔 정지 프레임까지 가서 로비 전환까지 멈춘다. " +
                    "두 점 = 보스 기준(+Z = 보스 정면) — 클립을 고르면 Scene 뷰 핸들로 끈다.", EditorStyles.wordWrappedMiniLabel);
                DrawProfileFields("순간이동 Q(이 캐릭터 프로필)", nameof(BossVictoryProfile.teleportMoveState), nameof(BossVictoryProfile.teleportMoveEase));
                if (GUILayout.Button($"정지 프레임 = {BossVictoryKillerMoves.DefaultFreezeFrame}f", GUILayout.Width(140f)))
                {
                    Record("처치 연출 타임라인 순간이동 Q 정지 프레임");
                    FinisherFull(out AnimationClip clip, out _);
                    float fps = clip != null ? clip.frameRate : 60f;
                    Timeline.finisherTrimSeconds = BossVictoryAnimTrim.ClampTrim(
                        BossVictoryKillerMoves.FreezeSeconds(BossVictoryKillerMoves.DefaultFreezeFrame, fps), clip != null ? clip.length : 0f, fps);
                    Save();
                }
                EditorGUILayout.LabelField($"정지 = {Timeline.finisherTrimSeconds:0.###}초(클립 시간 · '처치 연출 스킬' 클립 끝 자르기와 같은 칸)", EditorStyles.miniLabel);
                break;
            case BossVictoryTrack.Amber:
                DrawPaletteHint();
                DrawPaletteSlots("노란 틴트 색(이 캐릭터 팔레트)", BossVictoryPaletteSlot.Amber);
                DrawSettingsFields("노란 틴트 모양", nameof(BossVictoryGlitchSettings.amberLumaLift),
                    nameof(BossVictoryGlitchSettings.skyAmberMinLuma));
                DrawSettingsFields("처치 연출 전용 볼륨", nameof(BossVictoryGlitchSettings.victoryVolume), nameof(BossVictoryGlitchSettings.victoryVignetteIntensity),
                    nameof(BossVictoryGlitchSettings.victoryOverrideExposure), nameof(BossVictoryGlitchSettings.victoryPostExposure));
                break;
            case BossVictoryTrack.Scanline:
                EditorGUILayout.LabelField("주사선은 벽·하늘에만 — 바닥(표면 법선이 위를 향함)은 뺀다. 플레이어·보스 위는 늘 주사선, RECTIFY 글자 위는 뺀다.", EditorStyles.wordWrappedMiniLabel);
                DrawSettingsFields("주사선 모양", nameof(BossVictoryGlitchSettings.scanlineCount), nameof(BossVictoryGlitchSettings.scanlineDarkness),
                    nameof(BossVictoryGlitchSettings.scanlineFloorNormalMin), nameof(BossVictoryGlitchSettings.scanlineFloorNormalMax),
                    nameof(BossVictoryGlitchSettings.scanlineFloorKeep));
                break;
            case BossVictoryTrack.BossNoise:
                DrawPaletteHint();
                if (_profile != null)
                {
                    EditorGUILayout.LabelField("보스 표현(캐릭터별) — 노이즈 = 아래 노이즈 모양 · 림라이트 = 안쪽 '보스 림 안쪽' 색 × 세기(블룸 과노출), 프레넬 테두리만 '보스 림 테두리' 색(팔레트 칸). 켜고 끄는 시각 = 이 트랙 클립.", EditorStyles.wordWrappedMiniLabel);
                    DrawProfileFields("보스 표현(캐릭터별)", nameof(BossVictoryProfile.bossLook), nameof(BossVictoryProfile.bossRimWidth),
                        nameof(BossVictoryProfile.bossRimSoftness), nameof(BossVictoryProfile.bossRimInsideIntensity));
                }
                DrawSettingsFields("보스 노이즈 모양",
                    nameof(BossVictoryGlitchSettings.bossNoiseTexture), nameof(BossVictoryGlitchSettings.bossNoiseSpace),
                    nameof(BossVictoryGlitchSettings.bossNoiseTiling), nameof(BossVictoryGlitchSettings.bossNoiseCameraScale),
                    nameof(BossVictoryGlitchSettings.bossNoiseBrightIntensity));
                DrawPaletteSlots("보스 노이즈·림 색(이 캐릭터 팔레트)", BossVictoryPaletteSlot.NoiseBright, BossVictoryPaletteSlot.NoiseDark,
                    BossVictoryPaletteSlot.RimInside, BossVictoryPaletteSlot.RimEdge);
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
                DrawProfileFields("하던 동작 배율", nameof(BossVictoryProfile.killerCurrentActionScale));
                break;
            case BossVictoryTrack.CrtOff:
                DrawPaletteHint();
                DrawPaletteSlots("TV 꺼짐 색(이 캐릭터 팔레트)", BossVictoryPaletteSlot.CrtGlow);
                break;
            case BossVictoryTrack.NoiseScrollV:
                EditorGUILayout.LabelField("보스 노이즈 텍스처 UV 를 수직으로 옮긴다(_NoiseST.w = 카메라 이동 누적 + 이 값). 구간 동안 시작 → 끝 오프셋(이징), 끝난 뒤 끝 값 유지, 클립끼리 더함. + = 위(카메라 위 이동과 같은 방향). 노이즈가 보이려면 '보스 노이즈' 세기 클립이 켜져 있어야 한다.", EditorStyles.wordWrappedMiniLabel);
                DrawSettingsFields("보스 노이즈 모양", nameof(BossVictoryGlitchSettings.bossNoiseTexture), nameof(BossVictoryGlitchSettings.bossNoiseTiling));
                break;
            case BossVictoryTrack.Rectify:
                DrawPaletteHint();
                EditorGUILayout.LabelField("배경 글자 — 외곽선만, 플레이어·보스 뒤·벽/바닥 앞, 찢긴 화면과 같이 찢긴다. 세기 = 알파: 들어오는 동안 가로줄이 그 비율만큼 무작위로 켜져 찢기듯 등장. 모양 값은 공용.", EditorStyles.wordWrappedMiniLabel);
                DrawSettingsFields("배경 글자 모양(공용)", nameof(BossVictoryGlitchSettings.rectifyFont), nameof(BossVictoryGlitchSettings.rectifyText),
                    nameof(BossVictoryGlitchSettings.rectifyWidth), nameof(BossVictoryGlitchSettings.rectifyCenterY),
                    nameof(BossVictoryGlitchSettings.rectifyOutline));
                DrawPaletteSlots("배경 글자 색(이 캐릭터 팔레트)", BossVictoryPaletteSlot.RectifyOutline);
                break;
            case BossVictoryTrack.RectifyFill:
                DrawPaletteHint();
                EditorGUILayout.LabelField("RECTIFY 글자 안쪽 원 3개 — ① 마젠타 → ② 블루 → ③ 흰색. 원마다 생성 시각(클립 시작 기준)에 자기 중심에서 시작 반지름으로 생겨 페이드 → 불투명해진 뒤 easeOutCubic 으로 단어 전체까지 퍼진다. ③은 회색으로 페이드 → 회색에서 흰색으로 바뀐 순간부터 확장. 원 안 색 = 왼쪽 색 → 오른쪽 색. 나중 원이 위, 마지막 원으로 고정. 첫 원 전은 비어 있음(흰 외곽선만). 색이 지나간 자리는 외곽선도 그 색(덮기 칸, 끄면 안쪽만·외곽선 흰색). 세기 = 채움 불투명도. 모양 값은 공용(주황 채움·안쪽 주사선은 캐릭터별 프로필).", EditorStyles.wordWrappedMiniLabel);
                DrawRectifyCircleFields();
                DrawPaletteSlots("안쪽 원·회색·주황 채움·글로우 색(이 캐릭터 팔레트)",
                    BossVictoryPaletteSlot.Circle1Left, BossVictoryPaletteSlot.Circle1Right, BossVictoryPaletteSlot.Circle2Left, BossVictoryPaletteSlot.Circle2Right,
                    BossVictoryPaletteSlot.Circle3Left, BossVictoryPaletteSlot.Circle3Right, BossVictoryPaletteSlot.CircleGray,
                    BossVictoryPaletteSlot.OrangeLeft, BossVictoryPaletteSlot.OrangeRight, BossVictoryPaletteSlot.Glow);
                DrawSettingsFields("안쪽 색 공용", nameof(BossVictoryGlitchSettings.rectifyCircleGrayToWhite),
                    nameof(BossVictoryGlitchSettings.rectifyFillSoftness), nameof(BossVictoryGlitchSettings.rectifyFillCoversOutline));
                DrawSettingsFields("RECTIFY 전용 글로우(맵 블룸과 무관)", nameof(BossVictoryGlitchSettings.rectifyGlowIntensity), nameof(BossVictoryGlitchSettings.rectifyGlowRadius),
                    nameof(BossVictoryGlitchSettings.rectifyGlowTarget));
                if (_profile != null)
                    DrawProfileFields("RECTIFY 주황 채움(캐릭터별 — 이 캐릭터 프로필, 11-12)", nameof(BossVictoryProfile.rectifyOrangeHold), nameof(BossVictoryProfile.rectifyOrangeWipe),
                        nameof(BossVictoryProfile.rectifyOrangeRightToLeft), nameof(BossVictoryProfile.rectifyOrangeJitter),
                        nameof(BossVictoryProfile.rectifyOrangeRowPixels),
                        nameof(BossVictoryProfile.rectifyOrangeGradient),
                        nameof(BossVictoryProfile.rectifyScanlineSpacing), nameof(BossVictoryProfile.rectifyScanlineDark));
                break;
            case BossVictoryTrack.CameraBob:
                EditorGUILayout.LabelField("화면 세로 두둥실 = 세기(키) × 진폭 × e^(−감쇠·경과) × sin(2π(경과/주기 + 위상)). 투영만 옮겨 카메라 위치·컷은 그대로, 시각 t 로 정해져 스크럽·모든 피어 같음. 들어오고 나가는 건 세기 키로.", EditorStyles.wordWrappedMiniLabel);
                break;
            case BossVictoryTrack.GroundCircle:
                EditorGUILayout.LabelField("카메라 바로 아래 바닥(매 프레임 따라감, 높이 = 처치자 발 — '카메라 처치자로' 마커 전엔 처치자 발밑) 둘레 배경을 호박색 대신 흑백으로(246 복원). 반경 = 아래 반경 × 클립 세기 — 세기 키가 0 → 1 로 올라가는 동안 원이 커진다. 흑백·노란 틴트가 들어온 만큼만 보인다. 모양 값은 모든 캐릭터 공용.", EditorStyles.wordWrappedMiniLabel);
                DrawSettingsFields("발밑 회색 원 모양(공용)", nameof(BossVictoryGlitchSettings.groundCircleRadius),
                    nameof(BossVictoryGlitchSettings.groundCircleSoftness), nameof(BossVictoryGlitchSettings.groundCircleBrightness));
                break;
            case BossVictoryTrack.CameraCut:
                DrawSettingsFields("벽 숨김 기준(컷 공통)", nameof(BossVictoryGlitchSettings.wallFacingThreshold), nameof(BossVictoryGlitchSettings.wallSearchRadius));
                break;
            default:
                EditorGUILayout.LabelField("이 트랙은 세기(클립)만 — 따로 모양 값 없음.", EditorStyles.miniLabel);
                break;
        }
    }

    // ── 팔레트(271) — 칸마다 독립 색(11-16 관계 끊음: 기준 색·대비·글자 기준 색 계산 없음) ──────────

    static readonly Dictionary<BossVictoryPaletteSlot, string> PaletteLabels = new Dictionary<BossVictoryPaletteSlot, string>
    {
        { BossVictoryPaletteSlot.Amber, "배경 호박색 틴트" },
        { BossVictoryPaletteSlot.NoiseBright, "보스 노이즈 밝은 색" },
        { BossVictoryPaletteSlot.NoiseDark, "보스 노이즈 어두운 색" },
        { BossVictoryPaletteSlot.RectifyOutline, "RECTIFY 외곽선" },
        { BossVictoryPaletteSlot.Circle1Left, "① 원 왼쪽" },
        { BossVictoryPaletteSlot.Circle1Right, "① 원 오른쪽" },
        { BossVictoryPaletteSlot.Circle2Left, "② 원 왼쪽" },
        { BossVictoryPaletteSlot.Circle2Right, "② 원 오른쪽" },
        { BossVictoryPaletteSlot.Circle3Left, "③ 원 왼쪽" },
        { BossVictoryPaletteSlot.Circle3Right, "③ 원 오른쪽" },
        { BossVictoryPaletteSlot.CircleGray, "③ 회색 단계" },
        { BossVictoryPaletteSlot.OrangeLeft, "주황 채움 왼쪽" },
        { BossVictoryPaletteSlot.OrangeRight, "주황 채움 오른쪽" },
        { BossVictoryPaletteSlot.Glow, "RECTIFY 글로우" },
        { BossVictoryPaletteSlot.CrtGlow, "TV 꺼짐 발광" },
        { BossVictoryPaletteSlot.RimInside, "보스 림 안쪽(밝은 색, 림라이트)" },
        { BossVictoryPaletteSlot.RimEdge, "보스 림 테두리(어두운 색, 림라이트)" },
    };

    void DrawPalette()
    {
        EditorGUILayout.LabelField("팔레트(271 — 이 캐릭터 프로필)", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("칸마다 독립 색 — 한 칸을 바꿔도 다른 칸은 안 바뀐다(11-16 관계 끊음, 그때 화면 색을 구워 넣음). 화면 = 이 색 그대로.", EditorStyles.wordWrappedMiniLabel);
        if (_profile.paletteColors == null || _profile.paletteColors.Length < BossVictoryPalette.Count)
        {
            EditorGUILayout.HelpBox($"칸이 {BossVictoryPalette.Count}개보다 적다 — 모자란 칸은 코드 기본 색으로 그린다.", MessageType.Warning);
            if (GUILayout.Button("모자란 칸 = 코드 기본 색으로 채우기", GUILayout.Width(220f)))
            {
                Undo.RecordObject(_profile, "처치 연출 팔레트 칸 채우기");
                Color[] filled = BossVictoryPalette.CreateDefaultColors();
                for (int i = 0; _profile.paletteColors != null && i < _profile.paletteColors.Length && i < filled.Length; i++) filled[i] = _profile.paletteColors[i];
                _profile.paletteColors = filled;
                SavePalette();
            }
        }

        DrawPaletteSlots(null, BossVictoryPalette.Slots);
    }

    // 팔레트 칸 색 편집 줄(271) — 팔레트 패널 전체 + 트랙 칸마다 그 트랙 색(11-16: 트랙에서 바로 조정). 프로필 없으면 안 그린다.
    void DrawPaletteSlots(string title, params BossVictoryPaletteSlot[] slots)
    {
        if (_profile == null) return;
        if (title != null) EditorGUILayout.Space(4f);
        if (title != null) EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
        BossVictoryPalette palette = BossVictoryPalette.From(_profile);
        foreach (BossVictoryPaletteSlot slot in slots)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string label = PaletteLabels.TryGetValue(slot, out string l) ? l : slot.ToString();
                EditorGUILayout.LabelField(label, GUILayout.Width(200f));
                EditorGUI.BeginChangeCheck();
                Color c = EditorGUILayout.ColorField(GUIContent.none, palette[slot], true, true, true, GUILayout.Width(120f));
                if (EditorGUI.EndChangeCheck()) SetPaletteColor(slot, c);
            }
        }
    }

    void SetPaletteColor(BossVictoryPaletteSlot slot, Color color)
    {
        Undo.RecordObject(_profile, "처치 연출 팔레트 색");
        int i = (int)slot;
        if (_profile.paletteColors == null || _profile.paletteColors.Length <= i)
        {
            Color[] grown = BossVictoryPalette.CreateDefaultColors();
            for (int k = 0; _profile.paletteColors != null && k < _profile.paletteColors.Length && k < grown.Length; k++) grown[k] = _profile.paletteColors[k];
            _profile.paletteColors = grown;
        }
        _profile.paletteColors[i] = color;
        SavePalette();
    }

    void SavePalette()
    {
        Save();
        RefreshLivePalette();
    }

    // Play 중 연출이 돌면 바뀐 팔레트를 바로 화면에(표시만).
    static void RefreshLivePalette()
    {
        if (EditorApplication.isPlaying && BossVictoryGlitchPresentation.Instance != null) BossVictoryGlitchPresentation.Instance.RefreshLook();
    }

    // 이 트랙 색 = 프로필 팔레트 칸이라는 안내 + 팔레트 칸 열기.
    void DrawPaletteHint()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("이 트랙 색 = 이 캐릭터 프로필 팔레트 칸(271 — 칸마다 독립).", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("팔레트", EditorStyles.miniButton, GUILayout.Width(52f)))
            {
                Select(-1, -1, -1);
                _selPalette = true;
            }
        }
    }

    // 처치 연출 스킬 End 줄 노드 — 대상별 시간 배율·기본 스킬 칸.
    void DrawTimeScales()
    {
        EditorGUILayout.LabelField("처치 연출 스킬 · 시간 배율", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("처치자: 하던 동작 ×배율(처치자 평타 클립) → 백대쉬·처치 연출 스킬 ×1 → 느려짐 시작 마커(꺼짐이면 스킬 End)부터 ×End 후 배율 · 팀원: 처치 즉시 · 보스: 처치 순간 0 → 피격 애니 ×1 · 정지 0 · 슬로우 ×End 후 배율 중 시각이 가장 늦은 노드(보스 슬로우 끔 = 처치자 느려짐 순간).", EditorStyles.wordWrappedMiniLabel);
        DrawProfileFields("배율·스킬 칸(이 캐릭터 프로필)",
            nameof(BossVictoryProfile.killerCurrentActionScale), nameof(BossVictoryProfile.killerAfterFinisherScale),
            nameof(BossVictoryProfile.otherPlayersScale), nameof(BossVictoryProfile.bossAfterFinisherScale),
            nameof(BossVictoryProfile.finisherSlot));
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
            if (GUILayout.Toggle(_selPalette, "팔레트", EditorStyles.miniButton, GUILayout.Width(52f)) != _selPalette)
            {
                bool on = !_selPalette;
                Select(-1, -1, -1);
                _selPalette = on;
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

    // 캐릭터 → Variant(클립 미리보기). 코드 기본값(처치자 없음)은 팔라딘 몸으로 보여 준다.
    static string VariantPath(TutorialCharacter c) => c switch
    {
        TutorialCharacter.Gunner => "Assets/2.Prefabs/Player/Gunner/Player_Gunner.prefab",
        TutorialCharacter.Assassin => "Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab",
        _ => "Assets/2.Prefabs/Player/Paladin/Player_Paladin.prefab",
    };

    // 추정 = 스킬 연출 시작 마커 + 처치 연출 스킬 클립의 End 이벤트 시각(255 후속 4 — 이전 스킬을 기다리지 않아 마커 그대로). 가정: 공격 속도 100%.
    string EstimateFinisherEnd()
    {
        float dash = Timeline.finisherStartTime;
        var parts = new List<string>();
        // 지금 프로필의 스킬 칸으로 — 코드 기본값(처치자 없음)이면 세 캐릭터 몸, 캐릭터 프로필이면 그 캐릭터 몸만(267).
        TutorialCharacter[] bodies = _character == TutorialCharacter.None
            ? new[] { TutorialCharacter.Paladin, TutorialCharacter.Gunner, TutorialCharacter.Assassin }
            : new[] { _character };
        foreach (TutorialCharacter body in bodies)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath(body));
            if (prefab == null) continue;
            string name = prefab.name.Replace("Player_", "");
            AnimationClip clip = FinisherClipOf(prefab);
            float end = clip != null ? EndEventTime(clip) : -1f;
            IReadOnlyList<Vector2> timing = NodeTiming(BossVictoryMarker.FinisherStart, clip);
            parts.Add(end >= 0f ? $"{name} {dash + BossVictoryClipTiming.ClipToTimeline(timing, end):0.00}s{(timing != null ? "(실측)" : "(배율 1 가정)")}" : $"{name} ?(End 없음)");
        }
        string slow = Timeline.slowStartTime > 0f ? $" · 느려짐은 마커 {Timeline.slowStartTime:0.00}s" : "";
        return parts.Count == 0 ? "프리팹 없음" : $"{string.Join(" · ", parts)}  (스킬 연출 시작 {dash:0.00}s + 클립 End — 실측 = 디버그 씬 마지막 재생{slow})";
    }

    float FinisherEndEventTime(GameObject prefab)
    {
        AnimationClip clip = FinisherClipOf(prefab);
        return clip != null ? EndEventTime(clip) : -1f;
    }

    static float EndEventTime(AnimationClip clip)
    {
        foreach (AnimationEvent ev in AnimationUtility.GetAnimationEvents(clip))
            if (ev.functionName == nameof(PlayerAnimationEventRelay.HandleSkillEvent) && ev.intParameter == (int)SkillAnimationEventType.End)
                return ev.time;
        return -1f;
    }

    // 캐릭터(프리팹 또는 플레이 중 처치자)의 처치 연출 스킬 상태 클립.
    AnimationClip FinisherClipOf(GameObject character)
    {
        if (character == null) return null;
        Animator animator = character.GetComponentInChildren<Animator>(true);
        // 순간이동 Q(272) — 처치 연출 스킬 대신 프로필 애니 상태(Combo_Attack_03_04). 끝 자르기 = 정지 프레임.
        if (_profile != null && _profile.killerMove == BossVictoryKillerMove.TeleportDash)
            return animator != null && animator.runtimeAnimatorController != null
                ? AnimationEventRequirements.FindStateClip(animator.runtimeAnimatorController, _profile.teleportMoveState) : null;
        PlayerSkillSlot slot = _profile != null ? _profile.finisherSlot : PlayerSkillSlot.Ultimate;   // 지금 프로필 칸(267)
        PlayerSkillController controller = character.GetComponentInChildren<PlayerSkillController>(true);
        PlayerSkillBase skill = controller != null ? controller.GetSkill(slot) : null;
        if (skill == null || skill.Data == null || animator == null || animator.runtimeAnimatorController == null) return null;
        return AnimationEventRequirements.FindStateClip(animator.runtimeAnimatorController, skill.Data.AnimatorStateName);
    }

    // ── 카메라 컷 블렌드(255 후속 8) — 들어오는 컷 시작에서 앞 클립 위로 겹치는 쐐기(Unity Timeline 크로스페이드 모양) ──
    // 블렌드 구간 = [컷 시작 − 블렌드, 컷 시작). 쐐기 = 앞 클립 페이드아웃 대각선 위 삼각형 + 곡선(남은 앞 화면 비율) 얇은 선 + '블렌드 0.30s'. 왼쪽 끝 끌기 = 시간(0 = 사라짐).

    const float DefaultCutBlend = 0.3f;
    static readonly string[] BlendCurveLabels = { "선형", "ease in/out", "ease in", "ease out" };
    static readonly Color BlendColor = new Color(0.55f, 0.95f, 0.75f);

    bool CutBlendRect(Rect lanes, int index, out Rect r)
    {
        r = default;
        float b = Timeline.CameraCutBlendSeconds(index);
        if (b <= 0f) return false;
        Rect row = RowRect(lanes, TrackRow(BossVictoryTrack.CameraCut));
        float start = Timeline.cameraCut[index].start;
        float x0 = TimeToX(lanes, start - b), x1 = TimeToX(lanes, start);
        r = new Rect(x0, row.y + 2f, Mathf.Max(1f, x1 - x0), row.height - 4f);
        return true;
    }

    void DrawCutBlends(Rect lanes)
    {
        for (int i = 0; i < Timeline.cameraCut.Count; i++)
        {
            if (!CutBlendRect(lanes, i, out Rect r)) continue;
            bool selected = IsSelected(BossVictoryTrack.CameraCut, i);
            Color c = selected ? Color.white : BlendColor;
            Handles.color = new Color(c.r, c.g, c.b, 0.35f);
            Handles.DrawAAConvexPolygon(new Vector3(r.x, r.y), new Vector3(r.xMax, r.y), new Vector3(r.xMax, r.yMax));   // 앞 클립 페이드아웃 대각선 위
            Handles.color = new Color(c.r, c.g, c.b, 0.6f);
            Handles.DrawAAPolyLine(1f, new Vector3(r.x, r.y), new Vector3(r.xMax, r.yMax));
            BossVictoryKeyCurve curve = Timeline.CameraCutShot(i).blendCurve;
            const int n = 16;
            var pts = new Vector3[n + 1];
            for (int k = 0; k <= n; k++)
            {
                float u = k / (float)n;
                pts[k] = new Vector3(r.x + u * r.width, r.y + BossVictoryKeys.Ease(curve, u) * r.height, 0f);   // 앞 화면이 빠지는 정도
            }
            Handles.color = c;
            Handles.DrawAAPolyLine(1.5f, pts);
            EditorGUI.DrawRect(new Rect(r.x - 1f, r.y, 2f, r.height), c);   // 왼쪽 끝 = 끌어서 시간
            string label = $"블렌드 {Timeline.CameraCutBlendSeconds(i):0.00}s";
            float w = EditorStyles.miniLabel.CalcSize(new GUIContent(label)).x + 4f;
            Rect lr = new Rect(r.xMax - w, r.y - 1f, w, 12f);
            EditorGUI.DrawRect(lr, new Color(0f, 0f, 0f, 0.55f));
            GUI.Label(lr, label, EditorStyles.miniLabel);
        }
    }

    bool PickCutBlend(Rect lanes, Vector2 mouse, out int index)
    {
        index = -1;
        float best = HandleSize;
        for (int i = 0; i < Timeline.cameraCut.Count; i++)
        {
            if (!CutBlendRect(lanes, i, out Rect r) || mouse.y < r.y || mouse.y > r.yMax) continue;
            float d = Mathf.Abs(mouse.x - r.x);
            if (d > best) continue;
            best = d;
            index = i;
        }
        return index >= 0;
    }

    // 끌기 중(RecordDrag 가 기록) — 최대 = 앞 컷 길이(첫 컷이면 컷 시작). 0 이하 = 블렌드 없음.
    void SetCutBlendSeconds(int index, float seconds)
    {
        Timeline.SyncCameraCutShots();
        BossVictoryCameraCuts.Cut shot = Timeline.cameraCutShots[index];
        shot.blendSeconds = Mathf.Clamp(seconds, 0f, Timeline.CameraCutBlendLimit(index));
        if (shot.blendSeconds < MinLength) shot.blendSeconds = 0f;
        Timeline.cameraCutShots[index] = shot;
    }

    void SetCutBlend(int index, float seconds)
    {
        Record(seconds > 0f ? "처치 연출 타임라인 컷 블렌드 추가" : "처치 연출 타임라인 컷 블렌드 제거");
        SetCutBlendSeconds(index, seconds);
        Select((int)BossVictoryTrack.CameraCut, index, -1);
        Save();
        Repaint();
    }

    void DrawCutBlendInspector()
    {
        Timeline.SyncCameraCutShots();
        BossVictoryCameraCuts.Cut shot = Timeline.cameraCutShots[_selIndex];
        float limit = Timeline.CameraCutBlendLimit(_selIndex);
        int prev = Timeline.PreviousCameraCut(_selIndex);
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField($"앞 컷에서 블렌드 — {(prev >= 0 ? $"컷 #{prev + 1}" : "게임 카메라")} → 이 컷 첫 화면 (최대 {limit:0.00}초)", EditorStyles.miniBoldLabel);
        EditorGUI.BeginChangeCheck();
        float seconds = EditorGUILayout.Slider("시간(초, 0 = 툭 끊김)", Mathf.Min(shot.blendSeconds, limit), 0f, Mathf.Max(0.01f, limit));
        int curve = EditorGUILayout.Popup("곡선", Mathf.Clamp((int)shot.blendCurve, 0, BlendCurveLabels.Length - 1), BlendCurveLabels);
        if (EditorGUI.EndChangeCheck())
        {
            RecordField("처치 연출 타임라인 컷 블렌드");
            SetCutBlendSeconds(_selIndex, seconds);
            shot = Timeline.cameraCutShots[_selIndex];
            shot.blendCurve = (BossVictoryKeyCurve)curve;
            Timeline.cameraCutShots[_selIndex] = shot;
            Save();
        }
        EditorGUILayout.LabelField("구간 = 이 컷 시작 전 그 시간 동안(쐐기). 앞 컷은 계속 진행하고, 그 화면에서 이 컷 첫 화면으로 월드 위치·회전·FOV 를 섞는다(기준점이 달라도 자연스럽게). " +
                                   "블렌드 중 벽 숨김 = 이 컷 설정 · 첫 컷이면 컷 카메라를 켜는 순간의 게임 카메라 화면에서. 우클릭 '블렌드 추가/제거' · 쐐기 왼쪽 끝 끌기.", EditorStyles.wordWrappedMiniLabel);
    }

    // ── 애니 클립 노드(255 후속 7·10) — 마커 줄을 클립으로: 시작 = 마커 값, 길이 = 상태 클립 전체 또는 끝 자르기(BossVictoryAnimTrim) ──
    // 처치 연출 스킬('스킬 연출 시작' 줄) = 미리보기 캐릭터(또는 랩 재생 중 실제 처치자)의 처치 연출 스킬 클립(공속 100%) ·
    // 보스 피격 애니('보스 피격 애니 시작' 줄) = 23호 컨트롤러(또는 실제 보스)의 bossHitStateName 상태 클립(23호 getowned = ParryHit_InPlace 1.42초).
    // 자르기 값은 클립 시간(초) 하나 — 캐릭터마다 클립 길이가 다르면 프레임 표시도 다르다.

    static readonly Color FinisherColor = new Color(0.80f, 0.45f, 0.55f);
    static readonly Color BossHitColor = new Color(0.85f, 0.35f, 0.30f);
    const string BossHitControllerPath = "Assets/4.Animations/Wells&No.23/No.23/Controller/No23Controller.controller";
    Object _finisherClipKey;
    AnimationClip _finisherClipCache;
    string _finisherMoveStateCache;
    string _finisherWhoCache;
    Object _bossHitClipKey;
    string _bossHitStateCache;
    AnimationClip _bossHitClipCache;
    string _bossHitWhoCache;

    static bool IsClipNode(BossVictoryMarker m) => m == BossVictoryMarker.FinisherStart || m == BossVictoryMarker.BossHitAnim;

    static string ClipNodeLabel(BossVictoryMarker m) => m == BossVictoryMarker.FinisherStart ? "처치 연출 스킬" : "보스 피격 애니";

    float NodeTrim(BossVictoryMarker m) => m == BossVictoryMarker.FinisherStart ? Timeline.finisherTrimSeconds : Timeline.bossHitTrimSeconds;

    void SetNodeTrimRaw(BossVictoryMarker m, float trim)
    {
        if (m == BossVictoryMarker.FinisherStart) Timeline.finisherTrimSeconds = trim;
        else Timeline.bossHitTrimSeconds = trim;
    }

    // End 이벤트(노란 선) — 처치 연출 스킬 = Skill End(느려짐 기준) · 보스 피격 애니 = 없음.
    static float NodeEndEvent(BossVictoryMarker m, AnimationClip clip) => m == BossVictoryMarker.FinisherStart && clip != null ? EndEventTime(clip) : -1f;

    float NodeFull(BossVictoryMarker m, out AnimationClip clip, out string who) =>
        m == BossVictoryMarker.FinisherStart ? FinisherFull(out clip, out who) : BossHitFull(out clip, out who);

    // 클립 시간 → 타임라인 초(노드 시작 뒤) — 디버그 씬 마지막 재생 실측 표본(속도 파라미터·공속·연출 배율 포함). 없으면 배율 1 가정(BossVictoryClipTiming).
    static IReadOnlyList<Vector2> NodeTiming(BossVictoryMarker m, AnimationClip clip) =>
        clip != null && BossVictoryClipTiming.TryGet(BossVictoryClipTiming.Key(m == BossVictoryMarker.BossHitAnim, clip.name), out IReadOnlyList<Vector2> s) ? s : null;

    static float NodeSeconds(BossVictoryMarker m, AnimationClip clip, float clipTime) => BossVictoryClipTiming.ClipToTimeline(NodeTiming(m, clip), clipTime);

    // 지금 보여 줄 처치 연출 스킬 클립과 원래 길이(초). 없으면 0.
    float FinisherFull(out AnimationClip clip, out string who)
    {
        GameObject killer = EditorApplication.isPlaying && BossVictoryGlitchPresentation.Instance != null ? BossVictoryGlitchPresentation.Instance.KillerObject : null;
        Object key = killer != null ? killer : AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath(_character));
        string moveState = _profile.killerMove == BossVictoryKillerMove.TeleportDash ? _profile.teleportMoveState : null;   // 순간이동 Q 상태가 바뀌면 다시(272)
        if (key != _finisherClipKey || moveState != _finisherMoveStateCache)
        {
            _finisherClipKey = key;
            _finisherMoveStateCache = moveState;
            _finisherClipCache = FinisherClipOf(key as GameObject);
            _finisherWhoCache = killer != null ? $"실제 처치자 {killer.name}" : _character == TutorialCharacter.None ? "코드 기본값(팔라딘 몸)" : CharacterLabel(_character);
        }
        clip = _finisherClipCache;
        who = _finisherWhoCache;
        return clip != null ? clip.length : 0f;
    }

    // 보스 피격 애니 상태 클립 — 랩 재생 중이면 실제 보스 Animator, 아니면 23호 컨트롤러. 상태 이름이 비면 없음.
    float BossHitFull(out AnimationClip clip, out string who)
    {
        string state = _settings.bossHitStateName;
        GameObject boss = EditorApplication.isPlaying && BossVictoryGlitchPresentation.Instance != null ? BossVictoryGlitchPresentation.Instance.BossObject : null;
        Object key = boss != null ? boss : AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(BossHitControllerPath);
        if (key != _bossHitClipKey || state != _bossHitStateCache)
        {
            _bossHitClipKey = key;
            _bossHitStateCache = state;
            Animator animator = boss != null ? boss.GetComponentInChildren<Animator>(true) : null;
            RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : key as RuntimeAnimatorController;
            _bossHitClipCache = string.IsNullOrEmpty(state) || controller == null ? null : AnimationEventRequirements.FindStateClip(controller, state);
            _bossHitWhoCache = string.IsNullOrEmpty(state) ? "상태 이름 없음(마지막 자세)" : boss != null ? $"실제 보스 {boss.name} '{state}'" : $"23호 '{state}'";
        }
        clip = _bossHitClipCache;
        who = _bossHitWhoCache;
        return clip != null ? clip.length : 0f;
    }

    // 클립 칸 — 원래 길이를 모르면(클립 없음) 시작 표시만 짧게.
    bool ClipNodeRect(Rect lanes, BossVictoryMarker m, out Rect r)
    {
        float full = NodeFull(m, out AnimationClip clip, out _);
        float trim = NodeTrim(m);
        float length = full > 0f ? NodeSeconds(m, clip, BossVictoryAnimTrim.PlayedLength(trim, full)) : Mathf.Max(0.1f, trim);
        float start = Timeline.MarkerTime(m);
        Rect row = RowRect(lanes, MarkerRow(m));
        float x0 = TimeToX(lanes, start), x1 = TimeToX(lanes, start + length);
        r = new Rect(x0, row.y + 2f, Mathf.Max(2f, x1 - x0), row.height - 4f);
        return r.xMax >= lanes.x && r.x <= lanes.xMax;
    }

    void DrawClipNodes(Rect lanes)
    {
        DrawClipNode(lanes, BossVictoryMarker.FinisherStart);
        DrawClipNode(lanes, BossVictoryMarker.BossHitAnim);
    }

    void DrawClipNode(Rect lanes, BossVictoryMarker m)
    {
        if (!ClipNodeRect(lanes, m, out Rect r)) return;
        float full = NodeFull(m, out AnimationClip clip, out string who);
        bool selected = IsSelectedMarker(m);
        float trim = NodeTrim(m);
        float start = Timeline.MarkerTime(m);
        Color color = m == BossVictoryMarker.FinisherStart ? FinisherColor : BossHitColor;
        bool trimmed = full > 0f && BossVictoryAnimTrim.IsTrimmed(trim, full);
        if (trimmed)   // 잘려 나간 뒤쪽 = 흐린 칸(원래 끝까지)
        {
            float xFull = TimeToX(lanes, start + NodeSeconds(m, clip, full));
            EditorGUI.DrawRect(new Rect(r.xMax, r.y, Mathf.Max(0f, xFull - r.xMax), r.height), new Color(color.r, color.g, color.b, 0.18f));
        }
        EditorGUI.DrawRect(r, color * (selected ? 1f : 0.75f));
        if (clip != null)
        {
            float fps = BossVictoryAnimTrim.FrameRate(clip.frameRate);
            float played = BossVictoryAnimTrim.PlayedLength(trim, full);
            int playedFrames = Mathf.RoundToInt(played * fps);
            float frameWidth = r.width / Mathf.Max(1, playedFrames);   // 평균 — 실측이면 구간마다 다르다
            int step = frameWidth >= 4f ? 1 : frameWidth >= 0.8f ? 5 : 0;   // 프레임 눈금(너무 촘촘하면 5프레임마다, 그보다 촘촘하면 생략)
            for (int k = step; step > 0 && k < playedFrames; k += step)
            {
                float x = TimeToX(lanes, start + NodeSeconds(m, clip, k / fps));
                bool major = k % 10 == 0;
                EditorGUI.DrawRect(new Rect(x, r.yMax - (major ? 7f : 4f), 1f, major ? 7f : 4f), new Color(1f, 1f, 1f, major ? 0.6f : 0.35f));
            }
            float end = NodeEndEvent(m, clip);
            if (end >= 0f && end <= played)
            {
                float xe = TimeToX(lanes, start + NodeSeconds(m, clip, end));
                EditorGUI.DrawRect(new Rect(xe - 0.5f, r.y, 1.5f, r.height), new Color(1f, 0.95f, 0.4f, 0.9f));   // End 이벤트(노란 선)
            }
        }
        string label = clip != null
            ? $"{ClipNodeLabel(m)} {start:0.00}s · {who} {BossVictoryAnimTrim.FrameLabel(trim, full, clip.frameRate)}{(trimmed ? " 잘림" : "")} · 끝 {start + NodeSeconds(m, clip, BossVictoryAnimTrim.PlayedLength(trim, full)):0.00}s{(NodeTiming(m, clip) != null ? " 실측" : " 배율 1 가정")}"
            : $"{ClipNodeLabel(m)} {start:0.00}s · {who}{(string.IsNullOrEmpty(who) || who.Contains("없음") ? "" : " 클립 없음")}";
        GUI.Label(new Rect(r.x + 4f, r.y + 2f, Mathf.Max(r.width, 260f), r.height - 2f), label, EditorStyles.miniBoldLabel);
        if (selected) DrawOutline(r, Color.white);
        if (trimmed) EditorGUI.DrawRect(new Rect(r.xMax - 2f, r.y, 2f, r.height), Color.white);   // 자른 끝
    }

    // 몸통 = 시작 이동(마커 끌기와 같음) · 오른쪽 끝 = 끝 자르기(원래 길이를 알 때만).
    bool PickClipNode(Rect lanes, Vector2 mouse, out BossVictoryMarker m, out bool trimEdge)
    {
        foreach (BossVictoryMarker node in new[] { BossVictoryMarker.FinisherStart, BossVictoryMarker.BossHitAnim })
        {
            m = node;
            trimEdge = false;
            if (!ClipNodeRect(lanes, node, out Rect r)) continue;
            float inner = EdgeInner(r);
            if (NodeFull(node, out _, out _) > 0f && mouse.y >= r.y && mouse.y <= r.yMax && mouse.x >= r.xMax - inner && mouse.x <= r.xMax + EdgeOuter)
            {
                trimEdge = true;
                return true;
            }
            if (r.Contains(mouse)) return true;
        }
        m = default;
        trimEdge = false;
        return false;
    }

    void AddClipNodeCursors(Rect lanes)
    {
        foreach (BossVictoryMarker node in new[] { BossVictoryMarker.FinisherStart, BossVictoryMarker.BossHitAnim })
        {
            if (!ClipNodeRect(lanes, node, out Rect r)) continue;
            float inner = EdgeInner(r);
            AddCursor(lanes, new Rect(r.x, r.y, Mathf.Max(0f, r.width - inner), r.height), MouseCursor.MoveArrow);
            if (NodeFull(node, out _, out _) > 0f) AddCursor(lanes, new Rect(r.xMax - inner, r.y, EdgeOuter + inner, r.height), MouseCursor.ResizeHorizontal);
        }
    }

    void DragNodeTrim(BossVictoryMarker m, float mouseTime)
    {
        float full = NodeFull(m, out AnimationClip clip, out _);
        if (clip == null) return;
        float clipTime = BossVictoryClipTiming.TimelineToClip(NodeTiming(m, clip), mouseTime - Timeline.MarkerTime(m));   // 끈 자리(타임라인 초) → 그 순간 클립 시간
        SetNodeTrimRaw(m, BossVictoryAnimTrim.ClampTrim(clipTime, full, clip.frameRate));
    }

    void SetNodeTrim(BossVictoryMarker m, float trim)
    {
        Record($"처치 연출 타임라인 {ClipNodeLabel(m)} 끝 자르기");
        float full = NodeFull(m, out AnimationClip clip, out _);
        SetNodeTrimRaw(m, clip != null ? BossVictoryAnimTrim.ClampTrim(trim, full, clip.frameRate) : Mathf.Max(0f, trim));
        Select(-1, -1, (int)m);
        Save();
        Repaint();
    }

    void DrawClipNodeOptions(BossVictoryMarker m)
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField($"{ClipNodeLabel(m)} 클립 · 끝 자르기", EditorStyles.miniBoldLabel);
        if (m == BossVictoryMarker.FinisherStart)
            EditorGUILayout.LabelField("클립 길이·프레임 = 도구 막대 캐릭터(기본 = 팔라딘 몸) · 디버그 랩 재생 중이면 실제 처치자(267 — 옛 '미리보기 캐릭터' 드롭다운 통합).", EditorStyles.wordWrappedMiniLabel);
        float full = NodeFull(m, out AnimationClip clip, out string who);
        if (clip == null)
        {
            EditorGUILayout.HelpBox($"{who} — 클립을 못 찾았다. 길이·자르기를 보여 줄 수 없다.", MessageType.Info);
            return;
        }
        float fps = BossVictoryAnimTrim.FrameRate(clip.frameRate);
        int fullFrames = BossVictoryAnimTrim.Frames(full, fps);
        float end = NodeEndEvent(m, clip);
        string endText = m == BossVictoryMarker.FinisherStart ? (end >= 0f ? $" · End 이벤트 {BossVictoryAnimTrim.Frames(end, fps)}f" : " · End 이벤트 없음") : "";
        EditorGUILayout.LabelField("클립", $"{who} · {clip.name} · {full:0.00}초 = {fullFrames}f @ {fps:0}fps{(m == BossVictoryMarker.FinisherStart ? "(공속 100%)" : "")}{endText}", EditorStyles.wordWrappedMiniLabel);
        float trim = NodeTrim(m);
        IReadOnlyList<Vector2> timing = NodeTiming(m, clip);
        float nodeStart = Timeline.MarkerTime(m);
        EditorGUILayout.LabelField("타임라인 초",
            $"끝 {nodeStart + NodeSeconds(m, clip, BossVictoryAnimTrim.PlayedLength(trim, full)):0.00}s" +
            (end >= 0f ? $" · End {nodeStart + NodeSeconds(m, clip, end):0.00}s" : "") +
            (timing != null
                ? $" — 실측(디버그 씬 마지막 재생, 끝 구간 배율 ×{BossVictoryClipTiming.LastRate(timing):0.##}). 속도·느려짐 값을 바꿨으면 다시 재생하면 맞춰진다."
                : " — 배율 1 가정(재생 전). 실제는 스킬 속도 파라미터·공속·연출 배율로 달라진다 — 디버그 씬을 한 번 재생하면 실측으로 그린다."),
            EditorStyles.wordWrappedMiniLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            int frame = EditorGUILayout.IntSlider("끝 프레임", BossVictoryAnimTrim.Frames(BossVictoryAnimTrim.PlayedLength(trim, full), fps), 1, Mathf.Max(1, fullFrames));
            if (EditorGUI.EndChangeCheck()) SetNodeTrim(m, frame / fps);
            using (new EditorGUI.DisabledScope(trim <= 0f))
                if (GUILayout.Button("안 자름", GUILayout.Width(60f))) SetNodeTrim(m, 0f);
        }
        if (trim > 0f && full > 0f && trim >= full)
            EditorGUILayout.HelpBox($"자른 길이 {trim:0.00}초 ≥ {who} 클립 {full:0.00}초 — 안 잘린다(클립 끝까지).", MessageType.None);
        if (m == BossVictoryMarker.FinisherStart)
        {
            string slow = Timeline.slowStartTime > 0f
                ? $"느려짐은 '느려짐 시작' 마커 {Timeline.slowStartTime:0.00}s."
                : end >= 0f && BossVictoryAnimTrim.IsTrimmed(trim, full) && trim < end
                    ? "자른 끝이 End 이벤트보다 앞 — 느려짐은 자른 끝(멈추는 순간)에서."
                    : "느려짐은 End 이벤트에서(지금 규칙).";
            EditorGUILayout.LabelField(
                "오른쪽 끝을 앞으로 끌면 그 프레임에서 처치자 애니가 멈춘다(배율 0, 공속과 무관 — 클립 프레임 기준). 원래 끝보다 늘릴 수 없고, 끝까지 끌면 '안 자름'. " +
                "이미 생긴 이펙트는 지금 규칙대로 계속되고, 자른 뒤의 애니 이벤트는 나지 않는다(의도). " + slow,
                EditorStyles.wordWrappedMiniLabel);
        }
        else
        {
            EditorGUILayout.LabelField(
                "오른쪽 끝을 앞으로 끌면 그 프레임에서 보스가 멈춘다(배율 0). 보스 배율 규칙의 노드 하나로 시각 순 판정 — 같은 시각이면 피격 애니 < 자른 끝 < 정지 < 슬로우, " +
                "뒤에 '보스 슬로우'(꺼짐이면 처치자 느려짐 순간)가 오면 그 배율로 자른 프레임 뒤를 다시 재생한다. 원래 끝보다 늘릴 수 없고, 끝까지 끌면 '안 자름'(지금과 같음).",
                EditorStyles.wordWrappedMiniLabel);
        }
    }

    // ── 캐릭터 드롭다운·프로필 안내(267) ─────────────────────────────────

    void DrawCharacterPopup()
    {
        if (_settings == null) return;
        bool live = TryLiveKiller(out _, out _);
        using (new EditorGUI.DisabledScope(live))
        {
            int index = Mathf.Max(0, System.Array.IndexOf(Characters, _character));
            int picked = EditorGUILayout.Popup(index, CharacterLabels, EditorStyles.toolbarPopup, GUILayout.Width(72f));
            if (picked != index)
            {
                _character = Characters[picked];
                ResolveProfile();
                FitAll();
            }
        }
        if (live) GUILayout.Label("실제 처치자", EditorStyles.miniLabel, GUILayout.Width(56f));
    }

    // 지금 편집 중인 프로필 한 줄 — 표에 없는 캐릭터는 코드 기본값(저장 안 됨)을 쓴다 · 만들기 버튼(코드 기본값으로 새 에셋).
    void DrawProfileBanner()
    {
        bool own = _character != TutorialCharacter.None && _settings.HasOwnProfile(_character);
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            string text = own
                ? $"편집 중: {CharacterLabel(_character)} 프로필 — {_profile.name}"
                : _character == TutorialCharacter.None
                    ? "이번 회차는 처치자를 못 찾아 코드 기본값 프로필로 돈다 — 고쳐도 저장되지 않는다."
                    : !ProfileIsAsset
                        ? $"{CharacterLabel(_character)} 은(는) 표에 프로필이 없어 코드 기본값으로 돈다 — 고쳐도 저장되지 않는다. 오른쪽 버튼으로 프로필을 만든다."
                        : $"{CharacterLabel(_character)} 은(는) 표에 프로필이 없어 공용 '기본 프로필' 칸의 {_profile.name} 을 쓴다 — 여기서 고치면 그 에셋이 바뀐다.";
            GUILayout.Label(text, EditorStyles.wordWrappedMiniLabel);
            if (ProfileIsAsset && GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(40f))) EditorGUIUtility.PingObject(_profile);
            if (!own && _character != TutorialCharacter.None && GUILayout.Button("이 캐릭터 프로필 만들기(코드 기본값)", EditorStyles.miniButton, GUILayout.Width(200f)))
            {
                BossVictoryProfileAuthoring.CreateCharacterProfile(_settings, _character);
                ResolveProfile();
            }
        }
    }

    // ── 노드 여러 개 선택(Shift·Ctrl/⌘ 클릭 · 빈 곳 사각형 — 267·296) · 다른 캐릭터에 복사(267) ─────────────
    // 복사: 마커(하나뿐인 노드)는 덮어쓰기, 클립은 같은 시각에 새로 추가(TV 꺼짐·처치자 평타처럼 1개뿐인 트랙은 덮어쓰기). 키는 복사 대상 아님. Undo 한 번에 되돌림.
    // 선택 = 하나면 _selTrack/_selIndex/_selMarker/_selKey(지금까지 그대로), 둘 이상이면 _multi(+ 마지막으로 누른 것이 _sel* — 끌기 기준).

    readonly struct NodeRef : System.IEquatable<NodeRef>
    {
        public readonly int Track;    // 클립·키 노드 = 트랙(마커면 -1)
        public readonly int Index;    // 클립 번호(키면 그 키가 든 클립)
        public readonly int MarkerId; // 마커(클립·키면 -1)
        public readonly int Key;      // 키 번호(296 — 클립·마커면 -1)

        NodeRef(int track, int index, int marker, int key) { Track = track; Index = index; MarkerId = marker; Key = key; }
        public static NodeRef Clip(BossVictoryTrack track, int index) => new NodeRef((int)track, index, -1, -1);
        public static NodeRef Marker(BossVictoryMarker marker) => new NodeRef(-1, -1, (int)marker, -1);
        public static NodeRef KeyOf(BossVictoryTrack track, int clip, int key) => new NodeRef((int)track, clip, -1, key);
        public bool IsMarker => MarkerId >= 0;
        public bool IsKey => Key >= 0;
        public bool IsClip => !IsMarker && !IsKey;
        public NodeRef ClipOfKey => new NodeRef(Track, Index, -1, -1);
        public bool Equals(NodeRef o) => Track == o.Track && Index == o.Index && MarkerId == o.MarkerId && Key == o.Key;
        public override bool Equals(object o) => o is NodeRef n && Equals(n);
        public override int GetHashCode() => ((Track * 397 ^ Index) * 31 ^ MarkerId) * 17 ^ Key;
    }

    readonly List<NodeRef> _multi = new List<NodeRef>();
    List<NodeRef> _pendingCopy;
    Vector2 _pendingCopyAt;

    bool TryPrimaryNode(out NodeRef node)
    {
        if (_selMarker >= 0) { node = NodeRef.Marker((BossVictoryMarker)_selMarker); return true; }
        if (HasClipSelection && _selKey >= 0) { node = NodeRef.KeyOf((BossVictoryTrack)_selTrack, _selIndex, _selKey); return true; }
        if (HasClipSelection) { node = NodeRef.Clip((BossVictoryTrack)_selTrack, _selIndex); return true; }
        node = default;
        return false;
    }

    // 지금 선택 전부(여러 개면 그것, 아니면 하나 또는 없음).
    List<NodeRef> SelectionList()
    {
        if (_multi.Count > 0) return new List<NodeRef>(_multi);
        var one = new List<NodeRef>();
        if (TryPrimaryNode(out NodeRef n)) one.Add(n);
        return one;
    }

    // 다른 캐릭터에 복사할 노드 = 지금 선택(키 제외 — 키는 클립 안 값).
    List<NodeRef> CopySelection()
    {
        List<NodeRef> list = SelectionList();
        list.RemoveAll(n => n.IsKey);
        return list;
    }

    bool TryPickNode(Rect lanes, Vector2 mouse, out NodeRef node) => TryPickNode(lanes, mouse, out node, out _);

    // 누른 노드 + 잡은 손잡이(몸통 = Move/Marker/Key · 클립 끝 = Resize · 처치 연출 스킬 끝 = NodeTrim). 순서 = 끌기(BeginDrag)와 같다(컷 블렌드 쐐기 제외).
    bool TryPickNode(Rect lanes, Vector2 mouse, out NodeRef node, out DragMode mode)
    {
        if (PickClipNode(lanes, mouse, out BossVictoryMarker clipNode, out bool trimEdge))
        {
            node = NodeRef.Marker(clipNode);
            mode = trimEdge ? DragMode.NodeTrim : DragMode.Marker;
            return true;
        }
        if (PickMarker(lanes, mouse, out BossVictoryMarker marker)) { node = NodeRef.Marker(marker); mode = DragMode.Marker; return true; }
        foreach (BossVictoryTrack track in BossVictoryTimeline.Tracks)
        {
            if (!RowRect(lanes, TrackRow(track)).Contains(mouse)) continue;
            if (PickKey(lanes, track, mouse, out int keyClip, out int key)) { node = NodeRef.KeyOf(track, keyClip, key); mode = DragMode.Key; return true; }
            if (PickClip(lanes, track, mouse, out int index, out mode)) { node = NodeRef.Clip(track, index); return true; }
            break;
        }
        node = default;
        mode = DragMode.None;
        return false;
    }

    // Shift·Ctrl/⌘ 클릭 — 노드를 여러 개 선택에 넣거나 뺀다(처음이면 지금 선택도 같이 넣는다). 끌기 없음.
    void ToggleMulti(NodeRef node)
    {
        if (_multi.Count == 0 && TryPrimaryNode(out NodeRef primary) && !primary.Equals(node)) _multi.Add(primary);
        bool added = BossVictoryMultiEdit.Toggle(_multi, node);
        if (added) SetPrimary(node);
        else if (_multi.Count > 0) SetPrimary(_multi[_multi.Count - 1]);
        else SetPrimary(null);
        if (_multi.Count <= 1) _multi.Clear();   // 하나만 남으면 = 보통 선택(_sel*)
    }

    // 선택을 통째로 바꾼다(사각형 선택·붙여넣기 — 296). 0개 = 선택 없음, 1개 = 보통 선택.
    void SetSelection(List<NodeRef> nodes)
    {
        _multi.Clear();
        if (nodes.Count >= 2) _multi.AddRange(nodes);
        SetPrimary(nodes.Count > 0 ? nodes[nodes.Count - 1] : (NodeRef?)null);
    }

    // _sel* = 이 노드(여러 개 선택은 그대로 — 끌기 기준·마지막으로 누른 것).
    void SetPrimary(NodeRef? node)
    {
        NodeRef n = node.GetValueOrDefault();
        bool has = node.HasValue;
        _selTrack = has && !n.IsMarker ? n.Track : -1;
        _selIndex = has && !n.IsMarker ? n.Index : -1;
        _selMarker = has && n.IsMarker ? n.MarkerId : -1;
        _selKey = has && n.IsKey ? n.Key : -1;
        _selFinisher = false;
        _selPalette = false;
        GUI.FocusControl(null);
    }

    bool IsValid(NodeRef n)
    {
        if (n.IsMarker) return n.MarkerId < BossVictoryTimeline.Markers.Length;
        if (n.Track < 0 || n.Track >= BossVictoryTimeline.Tracks.Length) return false;
        var track = (BossVictoryTrack)n.Track;
        if (n.Index < 0 || n.Index >= Timeline.Clips(track).Count) return false;
        return !n.IsKey || n.Key < KeyCount(track, n.Index);
    }

    // Undo 등으로 번호가 사라진 노드를 뺀다.
    void PruneMulti()
    {
        if (_multi.Count == 0) return;
        _multi.RemoveAll(n => !IsValid(n));
        if (_multi.Count <= 1)
        {
            NodeRef? last = _multi.Count == 1 ? _multi[0] : (NodeRef?)null;
            _multi.Clear();
            SetPrimary(last);
        }
    }

    string NodeLabel(NodeRef n) => n.IsMarker
        ? BossVictoryTimeline.MarkerLabel((BossVictoryMarker)n.MarkerId)
        : n.IsKey
            ? $"{BossVictoryTimeline.TrackLabel((BossVictoryTrack)n.Track)} #{n.Index + 1} 키 #{n.Key + 1}"
            : $"{BossVictoryTimeline.TrackLabel((BossVictoryTrack)n.Track)} #{n.Index + 1}";

    // ── 사각형 선택(296) — 빈 곳 끌기. Shift(또는 Ctrl/⌘)면 지금 선택에 더하기. 클립·마커·클립 노드(처치 연출 스킬·보스 피격 애니)를 잡는다(키는 Shift+클릭으로). ──

    Vector2 _rectA, _rectB;
    bool _rectAdditive;
    List<NodeRef> _rectBase;
    static readonly Color SelectRectFill = new Color(0.45f, 0.65f, 1f, 0.15f);
    static readonly Color SelectRectLine = new Color(0.55f, 0.75f, 1f, 0.9f);

    Rect SelectRect => Rect.MinMaxRect(Mathf.Min(_rectA.x, _rectB.x), Mathf.Min(_rectA.y, _rectB.y), Mathf.Max(_rectA.x, _rectB.x), Mathf.Max(_rectA.y, _rectB.y));

    void BeginSelectRect(Vector2 mouse, bool additive)
    {
        _drag = DragMode.Rect;
        _rectA = _rectB = mouse;
        _rectAdditive = additive;
        _rectBase = SelectionList();
    }

    void FinishSelectRect(Rect lanes)
    {
        Rect r = SelectRect;
        if (r.width < 3f && r.height < 3f) return;   // 클릭(끌기 아님) — 빈 곳 클릭 = 이미 선택 풀림(Shift 면 그대로)
        SetSelection(BossVictoryMultiEdit.CombineSelection(_rectBase, NodesInRect(lanes, r), _rectAdditive));
    }

    List<NodeRef> NodesInRect(Rect lanes, Rect r)
    {
        var hits = new List<NodeRef>();
        foreach (BossVictoryMarker m in BossVictoryTimeline.Markers)
        {
            if (IsClipNode(m))
            {
                if (ClipNodeRect(lanes, m, out Rect nr) && nr.Overlaps(r)) hits.Add(NodeRef.Marker(m));
                continue;
            }
            Rect row = RowRect(lanes, MarkerRow(m));
            float x = TimeToX(lanes, Timeline.MarkerTime(m));
            if (x < lanes.x || x > lanes.xMax) continue;
            if (new Rect(x - MarkerSize, row.center.y - MarkerSize, MarkerSize * 2f, MarkerSize * 2f).Overlaps(r)) hits.Add(NodeRef.Marker(m));
        }
        foreach (BossVictoryTrack track in BossVictoryTimeline.Tracks)
        {
            List<BossVictoryClip> clips = Timeline.Clips(track);
            for (int i = 0; i < clips.Count; i++)
                if (ClipRect(lanes, TrackRow(track), clips[i]).Overlaps(r)) hits.Add(NodeRef.Clip(track, i));
        }
        return hits;
    }

    void DrawSelectRect(Rect lanes)
    {
        if (_drag != DragMode.Rect) return;
        Rect r = SelectRect;
        EditorGUI.DrawRect(r, SelectRectFill);
        DrawOutline(r, SelectRectLine);
    }

    // ── 여러 개 같이 끌기(296) — 선택한 것 하나를 잡으면 전부 같은 Δ초. 스냅 = 잡은 노드 기준, 그 Δ 를 나머지에 그대로(0초 앞으로는 Δ 를 한 번에 막아 간격 유지).
    //    클립 끝을 잡으면 선택 클립 전부 같은 Δ 로 길이(클립마다 최소 길이). 못 움직이는 노드(꺼진 마커·시작·끝 키·클립과 같이 가는 키 등)는 건너뜀. Undo 한 번(끌기 묶음). ──

    struct GroupItem
    {
        public NodeRef Node;
        public float T0;    // 클립 = 시작 · 마커 = 시각 · 키 = 타임라인 초
        public float Len0;  // 클립 = 길이 · 키 = 기준 구간 길이
    }

    readonly List<GroupItem> _group = new List<GroupItem>();
    NodeRef _groupGrab;
    DragMode _groupMode;   // Move · ResizeLeft · ResizeRight

    void BeginGroupDrag(NodeRef grabbed, DragMode mode)
    {
        _groupGrab = grabbed;
        _groupMode = grabbed.IsClip && (mode == DragMode.ResizeLeft || mode == DragMode.ResizeRight) ? mode : DragMode.Move;
        _group.Clear();
        foreach (NodeRef n in _multi)
        {
            if (!IsValid(n)) continue;
            var item = new GroupItem { Node = n };
            if (n.IsMarker) item.T0 = Timeline.MarkerTime((BossVictoryMarker)n.MarkerId);
            else if (n.IsKey)
            {
                var track = (BossVictoryTrack)n.Track;
                item.Len0 = KeySpanLength(track, n.Index);
                item.T0 = KeySpanStart(track, n.Index) + KeyU(track, n.Index, n.Key) * item.Len0;
            }
            else
            {
                BossVictoryClip c = Timeline.Clips((BossVictoryTrack)n.Track)[n.Index];
                item.T0 = c.start;
                item.Len0 = c.length;
            }
            _group.Add(item);
        }
        SetPrimary(grabbed);   // 여러 개 선택은 그대로
        _drag = DragMode.Group;
    }

    // 이 노드가 '이동'에서 움직이는가.
    bool GroupMovable(in GroupItem it)
    {
        NodeRef n = it.Node;
        if (n.IsMarker) return !(BossVictoryTimeline.ZeroMeansOff((BossVictoryMarker)n.MarkerId) && it.T0 <= 0f);   // 꺼진 마커(0)는 그대로
        if (n.IsKey)
        {
            if (_multi.Contains(n.ClipOfKey)) return false;   // 클립이 같이 움직이면 키는 비율대로 따라간다
            return n.Key > 0 && n.Key < KeyCount((BossVictoryTrack)n.Track, n.Index) - 1;   // 시작·끝 키 = 클립 양 끝 고정
        }
        return true;
    }

    // 클립 끝 끌기에서 길이가 바뀌는가 — 컷(길이 자동)·마커·키 제외.
    static bool GroupResizable(in GroupItem it) => it.Node.IsClip && BossVictoryTimeline.KindOf((BossVictoryTrack)it.Node.Track) != BossVictoryTimeline.TrackKind.Cut;

    // '이동' Δ 기준 시각 — 처치자 평타(시작 0 고정)는 끝이 움직인다.
    static float MoveOrigin(in GroupItem it) =>
        it.Node.IsClip && BossVictoryTimeline.StartFixed((BossVictoryTrack)it.Node.Track) ? it.T0 + it.Len0 : it.T0;

    void ApplyGroupDrag(float mouseTime)
    {
        int grab = _group.FindIndex(g => g.Node.Equals(_groupGrab));
        if (grab < 0) return;
        GroupItem g0 = _group[grab];
        float origin = _groupMode == DragMode.ResizeRight ? g0.T0 + g0.Len0 : _groupMode == DragMode.ResizeLeft ? g0.T0 : MoveOrigin(g0);
        float d = BossVictoryMultiEdit.SnapDelta(origin, mouseTime - _dragMouseT0, _snap);
        if (_groupMode == DragMode.Move)
        {
            var origins = new List<float>(_group.Count);
            foreach (GroupItem it in _group)
                if (GroupMovable(it) && !it.Node.IsKey && !(it.Node.IsClip && BossVictoryTimeline.StartFixed((BossVictoryTrack)it.Node.Track)))
                    origins.Add(it.T0);
            d = BossVictoryMultiEdit.ClampMoveDelta(d, BossVictoryMultiEdit.Earliest(origins));
        }
        foreach (GroupItem it in _group)
        {
            if (!IsValid(it.Node)) continue;
            if (_groupMode == DragMode.Move)
            {
                if (!GroupMovable(it)) continue;
                if (it.Node.IsMarker) Timeline.SetMarkerTime((BossVictoryMarker)it.Node.MarkerId, Mathf.Max(0f, it.T0 + d));
                else if (it.Node.IsKey) SetKeyTime((BossVictoryTrack)it.Node.Track, it.Node.Index, it.Node.Key, it.T0 + d);
                else
                {
                    var track = (BossVictoryTrack)it.Node.Track;
                    List<BossVictoryClip> clips = Timeline.Clips(track);
                    BossVictoryClip c = clips[it.Node.Index];
                    if (BossVictoryTimeline.StartFixed(track)) c.length = Mathf.Max(MinLength, it.T0 + it.Len0 + d);   // 끝이 움직임(Clamp 가 시작 0)
                    else c.start = Mathf.Max(0f, it.T0 + d);
                    clips[it.Node.Index] = Clamp(track, c);
                }
                continue;
            }
            if (!GroupResizable(it)) continue;
            var rtrack = (BossVictoryTrack)it.Node.Track;
            List<BossVictoryClip> rclips = Timeline.Clips(rtrack);
            BossVictoryClip rc = rclips[it.Node.Index];
            float s, l;
            if (_groupMode == DragMode.ResizeLeft) BossVictoryMultiEdit.ResizeLeft(it.T0, it.Len0, d, MinLength, out s, out l);
            else BossVictoryMultiEdit.ResizeRight(it.T0, it.Len0, d, MinLength, out s, out l);
            rc.start = s;
            rc.length = l;
            rclips[it.Node.Index] = Clamp(rtrack, rc);
        }
    }

    // ── 여러 개 삭제·복사·붙여넣기(296 — 기존 Del·Ctrl+C/V/D 를 여러 개에도) ──
    // 삭제 = 선택 클립 전부 + 클립과 같이 지우지 않는 중간 키(마커는 지울 수 없어 건너뜀). 복사 = 선택 클립(트랙·간격 유지 — 붙여넣기는 각자 트랙, 가장 앞 클립이 그 시각).

    struct ClipboardItem
    {
        public BossVictoryTrack Track;
        public BossVictoryClip Clip;   // start = 가장 앞 클립 기준 오프셋
        public BossVictoryCameraCuts.Cut Shot;
    }

    static List<ClipboardItem> s_clipboardMulti;   // null = 하나짜리 클립보드(s_clipboard)

    void DeleteMulti()
    {
        var keys = new List<NodeRef>();
        var clips = new List<NodeRef>();
        foreach (NodeRef n in _multi)
        {
            if (!IsValid(n) || n.IsMarker) continue;
            if (n.IsClip) clips.Add(n);
            else if (!_multi.Contains(n.ClipOfKey) && n.Key > 0 && n.Key < KeyCount((BossVictoryTrack)n.Track, n.Index) - 1) keys.Add(n);
        }
        if (keys.Count == 0 && clips.Count == 0) return;
        Record("처치 연출 타임라인 여러 개 삭제");
        keys.Sort((a, b) => b.Key.CompareTo(a.Key));   // 같은 클립 안 뒤 키부터
        foreach (NodeRef k in keys)
        {
            if (k.Track == (int)BossVictoryTrack.CameraCut) Timeline.cameraCutShots[k.Index].keys.RemoveAt(k.Key);
            else Timeline.Clips((BossVictoryTrack)k.Track)[k.Index].keys.RemoveAt(k.Key);
        }
        clips.Sort((a, b) => a.Track != b.Track ? a.Track.CompareTo(b.Track) : b.Index.CompareTo(a.Index));   // 트랙마다 뒤 번호부터
        foreach (NodeRef c in clips)
        {
            Timeline.Clips((BossVictoryTrack)c.Track).RemoveAt(c.Index);
            if (c.Track == (int)BossVictoryTrack.CameraCut && c.Index < Timeline.cameraCutShots.Count) Timeline.cameraCutShots.RemoveAt(c.Index);
        }
        Select(-1, -1, -1);
        Save();
        Repaint();
    }

    // 여러 개 선택 복사 — 클립이 둘 이상이면 여러 개 클립보드, 하나면 보통 클립보드. 클립이 없으면 false(그대로).
    bool CopyMulti()
    {
        var items = new List<ClipboardItem>();
        float first = float.PositiveInfinity;
        foreach (NodeRef n in _multi)
            if (n.IsClip && IsValid(n)) first = Mathf.Min(first, Timeline.Clips((BossVictoryTrack)n.Track)[n.Index].start);
        foreach (NodeRef n in _multi)
        {
            if (!n.IsClip || !IsValid(n)) continue;
            var track = (BossVictoryTrack)n.Track;
            BossVictoryClip c = Timeline.Clips(track)[n.Index].WithCopiedKeys();
            c.start -= first;
            items.Add(new ClipboardItem
            {
                Track = track,
                Clip = c,
                Shot = track == BossVictoryTrack.CameraCut ? Timeline.CameraCutShot(n.Index).WithCopiedKeys() : BossVictoryCameraCuts.Cut.Default,
            });
        }
        if (items.Count == 0) return false;
        if (items.Count == 1)
        {
            s_clipboardMulti = null;
            s_clipboardTrack = items[0].Track;
            s_clipboard = items[0].Clip;
            s_clipboardShot = items[0].Shot;
        }
        else s_clipboardMulti = items;
        s_hasClipboard = true;
        return true;
    }

    // 여러 개 클립보드 붙여넣기 — 각자 트랙에, 가장 앞 클립 = start(간격 유지). 1개뿐인 트랙이 이미 차 있으면 건너뜀. 붙인 것을 선택.
    void PasteMulti(float start)
    {
        if (s_clipboardMulti == null || s_clipboardMulti.Count == 0) return;
        Record("처치 연출 타임라인 여러 개 붙여넣기");
        var pasted = new List<NodeRef>();
        foreach (ClipboardItem item in s_clipboardMulti)
        {
            if (!CanAdd(item.Track)) continue;
            BossVictoryClip c = item.Clip.WithCopiedKeys();
            if (BossVictoryTimeline.HasFades(item.Track)) c.MigrateFadesToKeys();
            else c.keys = null;
            c.start = Mathf.Max(0f, Snap(start + item.Clip.start));
            List<BossVictoryClip> clips = Timeline.Clips(item.Track);
            clips.Add(Clamp(item.Track, c));
            if (item.Track == BossVictoryTrack.CameraCut)
            {
                Timeline.SyncCameraCutShots();
                Timeline.cameraCutShots[clips.Count - 1] = item.Shot.WithCopiedKeys();
            }
            pasted.Add(NodeRef.Clip(item.Track, clips.Count - 1));
        }
        SetSelection(pasted);
        Save();
        Repaint();
    }

    // 선택 클립 중 가장 늦은 끝(복제 = 그 뒤에 붙여넣기). 클립이 없으면 재생 헤드.
    float MultiClipEnd()
    {
        float end = _playhead;
        bool any = false;
        foreach (NodeRef n in _multi)
        {
            if (!n.IsClip || !IsValid(n)) continue;
            float e = Timeline.Clips((BossVictoryTrack)n.Track)[n.Index].End;
            end = any ? Mathf.Max(end, e) : e;
            any = true;
        }
        return end;
    }

    // ── 여러 개 선택 칸(296) — "N개 선택" + 같은 종류면 공통 칸(고치면 전부에), 다르면 개수만 ──

    static readonly string[] MultiKindLabels = { "클립", "마커", "키" };

    void DrawMultiInspector()
    {
        PruneMulti();
        if (_multi.Count < 2) return;
        EditorGUILayout.LabelField($"{_multi.Count}개 선택", EditorStyles.boldLabel);
        for (int i = 0; i < _multi.Count && i < 8; i++) EditorGUILayout.LabelField("· " + NodeLabel(_multi[i]), EditorStyles.miniLabel);
        if (_multi.Count > 8) EditorGUILayout.LabelField($"… 외 {_multi.Count - 8}개", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("하나를 끌면 전부 같은 초만큼 이동 · 선택 클립 끝 끌기 = 전부 같은 초만큼 길이 · Del = 클립·중간 키 삭제(마커 제외) · Ctrl+C/V/D = 클립(트랙·간격 유지).", EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space(4f);

        NodeRef a = _multi[0];
        bool sameKind = true, sameTrack = true;
        foreach (NodeRef n in _multi)
        {
            if (n.IsMarker != a.IsMarker || n.IsKey != a.IsKey) sameKind = false;
            if (n.Track != a.Track) sameTrack = false;
        }
        if (!sameKind || (!a.IsMarker && !sameTrack))
        {
            EditorGUILayout.HelpBox(sameKind ? "트랙이 달라 공통 칸이 없다." : "종류(클립·마커·키)가 섞여 공통 칸이 없다.", MessageType.None);
            return;
        }
        int kind = a.IsMarker ? 1 : a.IsKey ? 2 : 0;
        EditorGUILayout.LabelField($"공통 칸 — {MultiKindLabels[kind]} {_multi.Count}개{(a.IsMarker ? "" : $" · {BossVictoryTimeline.TrackLabel((BossVictoryTrack)a.Track)}")} (값이 다르면 '—', 고치면 그 칸만 전부에)", EditorStyles.wordWrappedMiniLabel);
        if (a.IsMarker) DrawMultiMarkerFields();
        else if (a.IsKey) DrawMultiKeyFields((BossVictoryTrack)a.Track);
        else DrawMultiClipFields((BossVictoryTrack)a.Track);
    }

    // 값 목록 → 같으면 그 값, 다르면 '—' 로 그리는 칸. 고쳤으면 true.
    static bool MixedFloat(string label, List<float> values, out float result, float min = float.NegativeInfinity)
    {
        bool mixed = false;
        for (int i = 1; i < values.Count; i++) if (!Mathf.Approximately(values[i], values[0])) mixed = true;
        EditorGUI.showMixedValue = mixed;
        EditorGUI.BeginChangeCheck();
        result = Mathf.Max(min, EditorGUILayout.FloatField(label, values[0]));
        bool changed = EditorGUI.EndChangeCheck();
        EditorGUI.showMixedValue = false;
        return changed;
    }

    static bool MixedPopup(string label, List<int> values, string[] options, out int result)
    {
        bool mixed = false;
        for (int i = 1; i < values.Count; i++) if (values[i] != values[0]) mixed = true;
        EditorGUI.showMixedValue = mixed;
        EditorGUI.BeginChangeCheck();
        result = EditorGUILayout.Popup(label, Mathf.Clamp(values[0], 0, options.Length - 1), options);
        bool changed = EditorGUI.EndChangeCheck();
        EditorGUI.showMixedValue = false;
        return changed;
    }

    static bool MixedVector3(string label, List<Vector3> values, out Vector3 result)
    {
        bool mixed = false;
        for (int i = 1; i < values.Count; i++) if (values[i] != values[0]) mixed = true;
        EditorGUI.showMixedValue = mixed;
        EditorGUI.BeginChangeCheck();
        result = EditorGUILayout.Vector3Field(label, values[0]);
        bool changed = EditorGUI.EndChangeCheck();
        EditorGUI.showMixedValue = false;
        return changed;
    }

    void MultiClipFloat(BossVictoryTrack track, string label, System.Func<BossVictoryClip, float> get, System.Func<BossVictoryClip, float, BossVictoryClip> set, float min = float.NegativeInfinity)
    {
        var values = new List<float>();
        foreach (NodeRef n in _multi) values.Add(get(Timeline.Clips(track)[n.Index]));
        if (!MixedFloat(label, values, out float v, min)) return;
        ApplyMultiClips(track, c => set(c, v));
    }

    void MultiClipPopup(BossVictoryTrack track, string label, string[] options, System.Func<BossVictoryClip, int> get, System.Func<BossVictoryClip, int, BossVictoryClip> set)
    {
        var values = new List<int>();
        foreach (NodeRef n in _multi) values.Add(get(Timeline.Clips(track)[n.Index]));
        if (!MixedPopup(label, values, options, out int v)) return;
        ApplyMultiClips(track, c => set(c, v));
    }

    void MultiClipVector3(BossVictoryTrack track, string label, System.Func<BossVictoryClip, Vector3> get, System.Func<BossVictoryClip, Vector3, BossVictoryClip> set)
    {
        var values = new List<Vector3>();
        foreach (NodeRef n in _multi) values.Add(get(Timeline.Clips(track)[n.Index]));
        if (!MixedVector3(label, values, out Vector3 v)) return;
        ApplyMultiClips(track, c => set(c, v));
    }

    void ApplyMultiClips(BossVictoryTrack track, System.Func<BossVictoryClip, BossVictoryClip> change)
    {
        RecordField("처치 연출 타임라인 여러 클립 수치");
        List<BossVictoryClip> clips = Timeline.Clips(track);
        foreach (NodeRef n in _multi) clips[n.Index] = Clamp(track, change(clips[n.Index]));
        Save();
    }

    // 같은 트랙 클립 — 시작·길이(처치자 평타 = 끝) + 트랙 값(노이즈 UV·두둥실·순간이동 두 점).
    void DrawMultiClipFields(BossVictoryTrack track)
    {
        if (BossVictoryTimeline.StartFixed(track))
            MultiClipFloat(track, "끝(초) = 동작 끊는 시각", c => c.length, (c, v) => { c.length = v; return c; }, MinLength);
        else
        {
            MultiClipFloat(track, "시작(초)", c => c.start, (c, v) => { c.start = v; return c; }, 0f);
            if (BossVictoryTimeline.KindOf(track) != BossVictoryTimeline.TrackKind.Cut)
                MultiClipFloat(track, "길이(초)", c => c.length, (c, v) => { c.length = v; return c; }, MinLength);
        }
        if (BossVictoryTimeline.IsUvScroll(track))
        {
            MultiClipFloat(track, "시작 오프셋(UV)", c => c.uvFrom, (c, v) => { c.uvFrom = v; return c; });
            MultiClipFloat(track, "끝 오프셋(UV)", c => c.uvTo, (c, v) => { c.uvTo = v; return c; });
            MultiClipPopup(track, "이징", KeyCurveLabels, c => (int)c.uvEase, (c, v) => { c.uvEase = (BossVictoryKeyCurve)v; return c; });
        }
        if (BossVictoryTimeline.IsAssassinDash(track))
        {
            MultiClipVector3(track, "시작 지점(보스 기준)", c => c.dashFrom, (c, v) => { c.dashFrom = FlatOffset(v); return c; });
            MultiClipVector3(track, "도착 지점(보스 기준)", c => c.dashTo, (c, v) => { c.dashTo = FlatOffset(v); return c; });
        }
        if (BossVictoryTimeline.IsBob(track))
        {
            MultiClipFloat(track, "진폭(화면 높이 %)", c => c.bobAmplitude, (c, v) => { c.bobAmplitude = v; return c; });
            MultiClipFloat(track, "주기(초)", c => c.bobPeriod, (c, v) => { c.bobPeriod = v; return c; }, 0f);
            MultiClipFloat(track, "시작 위상(주기)", c => c.bobPhase, (c, v) => { c.bobPhase = Mathf.Clamp01(v); return c; });
            MultiClipFloat(track, "감쇠(1/초)", c => c.bobDamping, (c, v) => { c.bobDamping = v; return c; }, 0f);
        }
        if (track == BossVictoryTrack.CameraCut)
            EditorGUILayout.LabelField("컷 화면·기준은 하나씩 고친다(컷마다 다른 화면).", EditorStyles.wordWrappedMiniLabel);
    }

    // 같은 트랙 키 — 세기 키 = 세기·다음 키까지 곡선 · 컷 화면 키 = FOV·곡선.
    void DrawMultiKeyFields(BossVictoryTrack track)
    {
        bool cut = track == BossVictoryTrack.CameraCut;
        var curves = new List<int>();
        var values = new List<float>();
        foreach (NodeRef n in _multi)
        {
            if (cut)
            {
                BossVictoryCameraCuts.CutKey k = Timeline.cameraCutShots[n.Index].keys[n.Key];
                curves.Add((int)k.curve);
                values.Add(k.fov);
            }
            else
            {
                BossVictoryKey k = Timeline.Clips(track)[n.Index].keys[n.Key];
                curves.Add((int)k.curve);
                values.Add(k.value);
            }
        }
        bool valueChanged = MixedFloat(cut ? "FOV" : "세기", values, out float value);
        bool curveChanged = MixedPopup("다음 키까지 곡선", curves, KeyCurveLabels, out int curve);
        if (!valueChanged && !curveChanged) return;
        RecordField("처치 연출 타임라인 여러 키 수치");
        foreach (NodeRef n in _multi)
        {
            if (cut)
            {
                BossVictoryCameraCuts.Cut shot = Timeline.cameraCutShots[n.Index];
                BossVictoryCameraCuts.CutKey k = shot.keys[n.Key];
                if (valueChanged) k.fov = Mathf.Clamp(value, 10f, 90f);
                if (curveChanged) k.curve = (BossVictoryKeyCurve)curve;
                shot.keys[n.Key] = k;
            }
            else
            {
                BossVictoryClip c = Timeline.Clips(track)[n.Index];
                BossVictoryKey k = c.keys[n.Key];
                if (valueChanged) k.value = Mathf.Clamp01(value);
                if (curveChanged) k.curve = (BossVictoryKeyCurve)curve;
                c.keys[n.Key] = k;
            }
        }
        Save();
    }

    // 마커 — 시각(같게 맞추기) + 전부 거너 Q 단계 마커면 단계 칸 공통.
    void DrawMultiMarkerFields()
    {
        var times = new List<float>();
        foreach (NodeRef n in _multi) times.Add(Timeline.MarkerTime((BossVictoryMarker)n.MarkerId));
        if (MixedFloat("시각(초)", times, out float t, 0f))
        {
            RecordField("처치 연출 타임라인 여러 마커 시각");
            foreach (NodeRef n in _multi) Timeline.SetMarkerTime((BossVictoryMarker)n.MarkerId, t);
            Save();
        }
        var stagePaths = new List<string>();
        foreach (NodeRef n in _multi)
        {
            var m = (BossVictoryMarker)n.MarkerId;
            if (m < BossVictoryMarker.GunnerStage1 || m > BossVictoryMarker.GunnerStage3) { stagePaths = null; break; }
            stagePaths.Add(GunnerQField($"stage{m - BossVictoryMarker.GunnerStage1 + 1}"));
        }
        if (stagePaths != null) DrawCommonProfileProperties("거너 Q 단계(294) 공통 칸", stagePaths);
        // 차징 이펙트(295) — 차징 시작·1~3단계 마커만 골랐으면 fx 칸 공통
        var fxPaths = new List<string>();
        foreach (NodeRef n in _multi)
        {
            var m = (BossVictoryMarker)n.MarkerId;
            if (m == BossVictoryMarker.ChargeStart) fxPaths.Add(GunnerQField(nameof(BossVictoryGunnerQ.fxStart)));
            else if (m >= BossVictoryMarker.GunnerStage1 && m <= BossVictoryMarker.GunnerStage3) fxPaths.Add(GunnerQField($"fxStage{m - BossVictoryMarker.GunnerStage1 + 1}"));
            else { fxPaths = null; break; }
        }
        if (fxPaths != null) DrawCommonProfileProperties("차징 이펙트(295) 공통 칸", fxPaths);
    }

    // 프로필 SO 의 같은 모양 칸 여러 개(경로 목록)를 한 번에 — 첫 경로의 하위 칸마다 값이 다르면 '—', 고치면 그 칸만 전부에(boxedValue).
    void DrawCommonProfileProperties(string title, IReadOnlyList<string> paths)
    {
        if (paths == null || paths.Count == 0) return;
        if (_profileSo == null || _profileSo.targetObject != _profile) _profileSo = new SerializedObject(_profile);
        _profileSo.Update();
        SerializedProperty first = _profileSo.FindProperty(paths[0]);
        if (first == null) return;
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
        int indent = EditorGUI.indentLevel;
        SerializedProperty it = first.Copy();
        SerializedProperty end = first.GetEndProperty();
        bool enter = true;
        while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
        {
            bool group = it.propertyType == SerializedPropertyType.Generic && !it.isArray;
            enter = group;   // 안쪽 묶음(클래스·구조체)은 펼쳐 칸마다
            EditorGUI.indentLevel = indent + Mathf.Max(0, it.depth - first.depth - 1);
            if (group)
            {
                EditorGUILayout.LabelField(it.displayName, EditorStyles.miniBoldLabel);
                continue;
            }
            string rel = it.propertyPath.Substring(first.propertyPath.Length);
            var others = new List<SerializedProperty>();
            bool mixed = false;
            for (int i = 1; i < paths.Count; i++)
            {
                SerializedProperty o = _profileSo.FindProperty(paths[i] + rel);
                if (o == null) continue;
                others.Add(o);
                if (!SerializedProperty.DataEquals(it, o)) mixed = true;
            }
            EditorGUI.showMixedValue = mixed;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(it, true);
            if (EditorGUI.EndChangeCheck())
            {
                object v = it.boxedValue;
                foreach (SerializedProperty o in others) o.boxedValue = v;
            }
            EditorGUI.showMixedValue = false;
        }
        EditorGUI.indentLevel = indent;
        if (_profileSo.ApplyModifiedProperties()) SavePalette();
    }

    void AddCopyToCharactersItem(GenericMenu menu, Vector2 mouse)
    {
        List<NodeRef> nodes = CopySelection();
        string label = nodes.Count > 1 ? $"다른 캐릭터에 복사… (노드 {nodes.Count}개)" : "다른 캐릭터에 복사…";
        if (nodes.Count == 0 || !ProfileIsAsset) { menu.AddDisabledItem(new GUIContent(label)); return; }
        menu.AddItem(new GUIContent(label), false, () =>
        {
            _pendingCopy = nodes;   // 팝업은 다음 OnGUI 에서(메뉴 콜백은 GUI 밖)
            _pendingCopyAt = mouse;
            Repaint();
        });
    }

    void ShowPendingCopyPopup()
    {
        if (_pendingCopy == null || Event.current.type != EventType.Repaint) return;
        List<NodeRef> nodes = _pendingCopy;
        _pendingCopy = null;
        PopupWindow.Show(new Rect(_pendingCopyAt, Vector2.one), new CopyToCharactersPopup(this, nodes));
    }

    // 복사 대상 후보 — 세 캐릭터 중 지금 프로필이 아닌 것. 표에 프로필이 없는 캐릭터는 고를 수 없다(null).
    List<(string label, BossVictoryProfile profile)> CopyTargets()
    {
        var list = new List<(string, BossVictoryProfile)>();
        foreach (TutorialCharacter c in Characters)
        {
            BossVictoryProfile p = _settings.HasOwnProfile(c) ? _settings.ProfileFor(c) : null;
            if (p != null && p == _profile) continue;
            list.Add((CharacterLabel(c), p));
        }
        return list;
    }

    void CopyNodesTo(List<NodeRef> nodes, List<BossVictoryProfile> targets)
    {
        if (nodes.Count == 0 || targets.Count == 0) return;
        const string label = "처치 연출 노드 다른 캐릭터에 복사";
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(label);
        int group = Undo.GetCurrentGroup();
        Undo.RecordObjects(targets.ToArray(), label);
        BossVictoryTimeline src = Timeline;
        foreach (BossVictoryProfile target in targets)
        {
            target.timeline ??= BossVictoryTimeline.CreateDefault();
            foreach (NodeRef n in nodes)
            {
                if (n.IsMarker) target.timeline.CopyMarkerFrom(src, (BossVictoryMarker)n.MarkerId);
                else if (n.Index < src.Clips((BossVictoryTrack)n.Track).Count) target.timeline.CopyClipFrom(src, (BossVictoryTrack)n.Track, n.Index);
            }
            target.timeline.SyncCameraCutShots();
            target.timeline.FitCameraCutLengths();
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
        }
        Undo.CollapseUndoOperations(group);
        var names = new List<string>();
        foreach (BossVictoryProfile t in targets) names.Add(t.name);
        var what = new List<string>();
        foreach (NodeRef n in nodes) what.Add(NodeLabel(n));
        Debug.Log($"[BossVictory] {_profile.name} → {string.Join(", ", names)} 노드 복사: {string.Join(" · ", what)} (마커 = 덮어쓰기 · 클립 = 추가, Ctrl+Z 로 되돌림)");
    }

    sealed class CopyToCharactersPopup : PopupWindowContent
    {
        readonly BossVictoryTimelineWindow _w;
        readonly List<NodeRef> _nodes;
        readonly List<(string label, BossVictoryProfile profile)> _targets;
        readonly bool[] _on;

        public CopyToCharactersPopup(BossVictoryTimelineWindow w, List<NodeRef> nodes)
        {
            _w = w;
            _nodes = nodes;
            _targets = w.CopyTargets();
            _on = new bool[_targets.Count];
        }

        public override Vector2 GetWindowSize() => new Vector2(300f, 96f + 20f * (_targets.Count + Mathf.Min(_nodes.Count, 6)));

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.LabelField($"다른 캐릭터에 복사 — 노드 {_nodes.Count}개(같은 시각)", EditorStyles.boldLabel);
            for (int i = 0; i < _nodes.Count && i < 6; i++) EditorGUILayout.LabelField("· " + _w.NodeLabel(_nodes[i]), EditorStyles.miniLabel);
            if (_nodes.Count > 6) EditorGUILayout.LabelField($"… 외 {_nodes.Count - 6}개", EditorStyles.miniLabel);
            EditorGUILayout.Space(2f);
            for (int i = 0; i < _targets.Count; i++)
            {
                bool has = _targets[i].profile != null;
                using (new EditorGUI.DisabledScope(!has))
                    _on[i] = EditorGUILayout.ToggleLeft(has ? $"{_targets[i].label} ({_targets[i].profile.name})" : $"{_targets[i].label} (프로필 없음 — 먼저 만들기)", _on[i] && has);
            }
            EditorGUILayout.LabelField("마커 = 덮어쓰기 · 클립 = 새로 추가(1개뿐인 트랙은 덮어쓰기)", EditorStyles.miniLabel);
            var picked = new List<BossVictoryProfile>();
            for (int i = 0; i < _targets.Count; i++) if (_on[i] && _targets[i].profile != null) picked.Add(_targets[i].profile);
            using (new EditorGUI.DisabledScope(picked.Count == 0))
                if (GUILayout.Button($"복사 ({picked.Count}개 캐릭터)"))
                {
                    _w.CopyNodesTo(_nodes, picked);
                    editorWindow.Close();
                    _w.Repaint();
                }
        }
    }
}
