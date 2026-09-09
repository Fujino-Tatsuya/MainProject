// ProfilerHUD.cs
// 런타임 프로파일링 오버레이 HUD — 에디터/개발 빌드 전용 (릴리스 빌드에서는 컴파일 제외).
// 외부 패키지 의존성 없음 (IMGUI + ProfilerRecorder + FrameTimingManager).
// 프로젝트 기준: Unity 6000.3.16f1 / URP 17.3 / 신 Input System.
//
// 사용법:
//   1) 빈 GameObject 에 ProfilerHUD 컴포넌트 추가 (Reset 시 FullScreenFog 마커 자동 채움).
//   2) Custom Markers 에 RenderGraph 패스 이름을 넣으면 패스별 ms 가 표시됨.
//   3) 플레이 → F8 토글.
//
// GPU ms 가 0/N/A 면 Project Settings > Player > Other Settings > "Frame Timing Stats" 활성화.
// 패스 ms 는 AddRasterRenderPass<T>("이름", ...) 의 "이름" 과 Marker Name 이 정확히 같아야 잡힌다.
//
// 주의: 이 컴포넌트는 #if UNITY_EDITOR || DEVELOPMENT_BUILD 로 감싸져 있어 릴리스 빌드엔 클래스가 없다.
//       릴리스로 출하되는 씬/프리팹에는 붙여두지 말 것(미싱 스크립트 경고 방지). 프로파일링 시 수동으로 추가.

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;   // 신 Input System (이 프로젝트: Active Input Handling = New)
#endif

[DisallowMultipleComponent]
public sealed class ProfilerHUD : MonoBehaviour
{
    // ───────── 인스펙터 설정 ─────────
    [Header("표시")]
#if ENABLE_INPUT_SYSTEM
    [Tooltip("HUD on/off 토글 키 (신 Input System)")]
    public Key toggleKey = Key.F8;
#else
    [Tooltip("HUD on/off 토글 키 (구 Input Manager)")]
    public KeyCode toggleKey = KeyCode.F8;
#endif
    [Tooltip("시작 시 켜진 상태로 둘지")]
    public bool startVisible = true;
    [Tooltip("화면 모서리 위치")]
    public Corner anchor = Corner.TopLeft;
    [Range(0.6f, 2.5f)] public float scale = 1f;
    [Tooltip("숫자/그래프 갱신 주기(초). 작을수록 자주 갱신·약간의 비용")]
    [Range(0.05f, 1f)] public float refreshInterval = 0.2f;

    [Header("프레임 예산")]
    [Tooltip("목표 프레임레이트 → 예산 ms 자동 계산(빨강/노랑/초록 기준)")]
    public int targetFps = 60;

    [Header("렌더 패스 마커 (선택)")]
    [Tooltip("패스별 CPU/GPU 시간을 추적할 마커 이름. RenderGraph 패스 이름과 정확히 일치해야 함.")]
    public List<MarkerSpec> customMarkers = new List<MarkerSpec>();

    public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

    [System.Serializable]
    public sealed class MarkerSpec
    {
        [Tooltip("화면에 보일 라벨")] public string label = "Fog";
        [Tooltip("실제 마커 이름 = RenderGraph 패스 이름 (예: FullScreenFog)")] public string markerName = "FullScreenFog";
        [Tooltip("이 패스 개별 예산 ms (0이면 색 임계값 미적용)")] public float budgetMs = 0f;
    }

    // 컴포넌트를 처음 붙일 때 이 프로젝트의 실제 패스 마커로 기본값 채움.
    void Reset()
    {
        customMarkers = new List<MarkerSpec>
        {
            new MarkerSpec { label = "Fog",      markerName = "FullScreenFog",          budgetMs = 0f },
            new MarkerSpec { label = "Fog Copy", markerName = "FullScreenFog CopyBack",  budgetMs = 0f },
        };
    }

    // ───────── 내부 상태 ─────────
    bool _visible;
    float _budgetMs;
    int _appliedTargetFps;   // targetFps 를 런타임에 바꿔도 예산이 따라오게 하는 캐시
    float _accum;
    readonly StringBuilder _sb = new StringBuilder(512);

    // 내장 카운터 레코더
    ProfilerRecorder _mainThread;     // Internal, ns
    ProfilerRecorder _drawCalls;      // Render, count
    ProfilerRecorder _setPass;        // Render, count
    ProfilerRecorder _tris;           // Render, count
    ProfilerRecorder _verts;          // Render, count
    ProfilerRecorder _gcAlloc;        // Memory, bytes/frame
    ProfilerRecorder _sysMem;         // Memory, bytes

    readonly List<ProfilerRecorder> _markerRecorders = new List<ProfilerRecorder>();

    // FrameTiming
    FrameTiming[] _frameTimings = new FrameTiming[1];
    double _cpuMs, _gpuMs;

    // 그래프(총 프레임 ms 히스토리)
    const int kHistory = 120;
    readonly float[] _history = new float[kHistory];
    int _historyIndex;

    // 캐시된 표시 문자열
    string _line1 = "", _line2 = "", _line3 = "";
    readonly List<string> _markerLines = new List<string>();
    readonly List<double> _markerMs = new List<double>();   // 색 판정용. OnGUI 에서 재계산하지 않는다.

    // ⚠️ IMGUI 드로우 수 절감(2026-09-04)
    // 이전: 라벨 3+N개 + 그래프 막대 120개를 각각 그려 SetPass 가 프레임당 130 가까이 늘었다
    //       (측정 결과: HUD ON 230 / OFF 101 — 도구가 렌더 통계를 절반 이상 부풀렸다).
    // 이후: richText 색 태그로 텍스트를 한 문자열에 합쳐 라벨 1개, 그래프는 픽셀을 직접
    //       구워 텍스처 1장으로 그린다 → 배경 1 + 라벨 1 + 그래프 1 = 3 드로우.
    readonly StringBuilder _sbAll = new StringBuilder(768);
    string _combined = "";
    readonly GUIContent _content = new GUIContent();
    const int kGraphTexH = 48;
    Texture2D _graphTex;
    Color32[] _graphPixels;

    // GUI 리소스
    Texture2D _whiteTex;
    GUIStyle _labelStyle;
    bool _stylesReady;

    void OnEnable()
    {
        _visible = startVisible;
        _appliedTargetFps = Mathf.Max(1, targetFps);
        _budgetMs = 1000f / _appliedTargetFps;

        _mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
        _drawCalls  = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        _setPass    = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        _tris       = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        _verts      = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
        _gcAlloc    = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        _sysMem     = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");

        for (int i = 0; i < customMarkers.Count; i++)
        {
            var markerName = customMarkers[i] != null ? customMarkers[i].markerName : null;
            // 패스 마커는 보통 Render 카테고리에 등록됨. 15프레임 평균 버퍼.
            var rec = string.IsNullOrEmpty(markerName)
                ? default
                : ProfilerRecorder.StartNew(ProfilerCategory.Render, markerName, 15);
            _markerRecorders.Add(rec);
        }

        _whiteTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        _whiteTex.SetPixel(0, 0, Color.white);
        _whiteTex.Apply();

        _graphTex = new Texture2D(kHistory, kGraphTexH, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,   // 막대 경계가 뭉개지지 않게
            wrapMode = TextureWrapMode.Clamp
        };
        _graphPixels = new Color32[kHistory * kGraphTexH];
    }

    void OnDisable()
    {
        _mainThread.Dispose();
        _drawCalls.Dispose();
        _setPass.Dispose();
        _tris.Dispose();
        _verts.Dispose();
        _gcAlloc.Dispose();
        _sysMem.Dispose();
        for (int i = 0; i < _markerRecorders.Count; i++)
        {
            var r = _markerRecorders[i];
            if (r.Valid) r.Dispose();
        }
        _markerRecorders.Clear();
        if (_whiteTex != null) Destroy(_whiteTex);
        if (_graphTex != null) Destroy(_graphTex);
    }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null && kb[toggleKey].wasPressedThisFrame) _visible = !_visible;
#else
        if (Input.GetKeyDown(toggleKey)) _visible = !_visible;
#endif
        // 인스펙터에서 targetFps 를 런타임에 바꾸면 예산도 따라간다.
        // (OnEnable 에서만 계산하면 실행 중 목표 변경이 색·그래프에 반영되지 않는다.)
        int wantFps = Mathf.Max(1, targetFps);
        if (wantFps != _appliedTargetFps)
        {
            _appliedTargetFps = wantFps;
            _budgetMs = 1000f / _appliedTargetFps;
        }

        if (!_visible) return;

        // 프레임 타이밍 캡처 (몇 프레임 지연되어 채워짐)
        FrameTimingManager.CaptureFrameTimings();
        uint n = FrameTimingManager.GetLatestTimings(1, _frameTimings);
        if (n > 0)
        {
            _cpuMs = _frameTimings[0].cpuFrameTime;
            _gpuMs = _frameTimings[0].gpuFrameTime;
        }

        // 총 프레임 ms (FrameTiming 우선, 없으면 deltaTime)
        float totalMs = (_cpuMs > 0.0) ? (float)System.Math.Max(_cpuMs, _gpuMs)
                                       : Time.unscaledDeltaTime * 1000f;
        _history[_historyIndex] = totalMs;
        _historyIndex = (_historyIndex + 1) % kHistory;

        // 갱신 주기마다 문자열 재구성(매 프레임 GC 회피)
        _accum += Time.unscaledDeltaTime;
        if (_accum < refreshInterval) return;
        _accum = 0f;
        RebuildStrings(totalMs);
    }

    void RebuildStrings(float totalMs)
    {
        float fps = totalMs > 0.0001f ? 1000f / totalMs : 0f;
        double mainMs = NsToMs(_mainThread.Valid ? Average(_mainThread) : 0);

        _line1 = Fmt("FPS {0,5:0.0}  |  Frame {1,5:0.00} ms  (예산 {2:0.00})", fps, totalMs, _budgetMs);
        _line2 = Fmt("CPU {0,5:0.00}  GPU {1,5:0.00}  Main {2,5:0.00} ms", _cpuMs, _gpuMs, mainMs);
        _line3 = Fmt("Draw {0}  SetPass {1}  Tris {2}  GC {3:0.0} KB  Mem {4:0} MB",
            _drawCalls.LastValue, _setPass.LastValue, _tris.LastValue,
            _gcAlloc.LastValue / 1024.0, _sysMem.LastValue / (1024.0 * 1024.0));

        _markerLines.Clear();
        _markerMs.Clear();
        for (int i = 0; i < _markerRecorders.Count; i++)
        {
            var r = _markerRecorders[i];
            var spec = customMarkers[i];
            string label = spec != null ? spec.label : "?";
            if (!r.Valid)
            {
                _markerLines.Add(Fmt("{0,-12} (마커 없음)", label));
                _markerMs.Add(0.0);
                continue;
            }
            double ms = NsToMs(Average(r));
            _markerLines.Add(Fmt("{0,-12} {1,6:0.00} ms", label, ms));
            _markerMs.Add(ms);
        }

        RebuildCombined(totalMs);
        RebuildGraphTexture();
    }

    // 라벨 3+N개를 richText 색 태그가 박힌 한 문자열로 합친다(드로우 1회).
    void RebuildCombined(float totalMs)
    {
        _sbAll.Clear();
        AppendColored(_sbAll, _line1, BudgetColor(totalMs, _budgetMs));
        _sbAll.Append('\n').Append(_line2);
        _sbAll.Append('\n').Append(_line3);

        for (int i = 0; i < _markerLines.Count; i++)
        {
            float bMs = (i < customMarkers.Count && customMarkers[i] != null) ? customMarkers[i].budgetMs : 0f;
            double ms = i < _markerMs.Count ? _markerMs[i] : 0.0;
            Color c = (bMs > 0f) ? BudgetColor((float)ms, bMs) : new Color(0.8f, 0.85f, 1f);
            _sbAll.Append('\n');
            AppendColored(_sbAll, _markerLines[i], c);
        }

        _combined = _sbAll.ToString();
        _content.text = _combined;
    }

    static void AppendColored(StringBuilder sb, string text, Color c)
    {
        sb.Append("<color=#");
        AppendHex2(sb, c.r); AppendHex2(sb, c.g); AppendHex2(sb, c.b);
        sb.Append('>').Append(text).Append("</color>");
    }

    static void AppendHex2(StringBuilder sb, float v01)
    {
        int v = Mathf.Clamp(Mathf.RoundToInt(v01 * 255f), 0, 255);
        const string hex = "0123456789ABCDEF";
        sb.Append(hex[v >> 4]).Append(hex[v & 0xF]);
    }

    // 막대 120개를 픽셀로 구워 텍스처 1장으로 만든다(드로우 1회).
    // 텍스처 (0,0) 은 좌하단이므로 y 를 그대로 위로 쌓으면 아래에서 자라는 막대가 된다.
    void RebuildGraphTexture()
    {
        if (_graphTex == null || _graphPixels == null) return;

        var bg = new Color32(255, 255, 255, 16);
        for (int i = 0; i < _graphPixels.Length; i++) _graphPixels[i] = bg;

        float maxMs = Mathf.Max(_budgetMs * 2f, 1f);

        // 예산 라인
        int by = Mathf.Clamp(Mathf.RoundToInt((_budgetMs / maxMs) * (kGraphTexH - 1)), 0, kGraphTexH - 1);
        var budgetC = new Color32(255, 230, 51, 160);
        int bRow = by * kHistory;
        for (int x = 0; x < kHistory; x++) _graphPixels[bRow + x] = budgetC;

        // 막대
        for (int i = 0; i < kHistory; i++)
        {
            int idx = (_historyIndex + i) % kHistory;
            float ms = _history[idx];
            if (ms <= 0f) continue;

            int bh = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(ms / maxMs) * kGraphTexH), 0, kGraphTexH);
            Color32 c = BudgetColor(ms, _budgetMs);
            c.a = 217;
            for (int y = 0; y < bh; y++) _graphPixels[y * kHistory + i] = c;
        }

        _graphTex.SetPixels32(_graphPixels);
        _graphTex.Apply(false);
    }

    static double Average(ProfilerRecorder rec)
    {
        int count = rec.Count;
        if (count == 0) return 0;
        double sum = 0;
        for (int i = 0; i < count; i++) sum += rec.GetSample(i).Value;
        return sum / count;
    }

    static double NsToMs(double ns) => ns * 1e-6;

    string Fmt(string f, params object[] a)
    {
        _sb.Clear();
        _sb.AppendFormat(f, a);
        return _sb.ToString();
    }

    // ───────── 그리기 ─────────
    void EnsureStyles()
    {
        if (_stylesReady) return;
        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(12 * scale),
            richText = true,
            alignment = TextAnchor.UpperLeft,
            wordWrap = false
        };
        _stylesReady = true;
    }

    void OnGUI()
    {
        if (!_visible) return;
        EnsureStyles();

        float pad = 8f * scale;
        float w = 320f * scale;
        float lineH = 18f * scale;
        int extraLines = _markerLines.Count;
        float graphH = 40f * scale;
        float h = pad * 2 + lineH * (3 + extraLines) + graphH + 6f * scale;

        float x = (anchor == Corner.TopLeft || anchor == Corner.BottomLeft)
            ? pad : Screen.width - w - pad;
        float y = (anchor == Corner.TopLeft || anchor == Corner.TopRight)
            ? pad : Screen.height - h - pad;

        // 배경
        DrawRect(new Rect(x, y, w, h), new Color(0f, 0f, 0f, 0.72f));

        float cx = x + pad;
        float cy = y + pad;

        // 텍스트 전체를 라벨 1개로 그린다. 줄별 색은 richText 태그로 문자열에 박혀 있다.
        GUI.color = Color.white;
        GUI.Label(new Rect(cx, cy, w - pad * 2, lineH * (3 + extraLines)), _content, _labelStyle);
        cy += lineH * (3 + extraLines);

        // 그래프는 미리 구운 텍스처 1장.
        if (_graphTex != null)
        {
            GUI.DrawTexture(new Rect(cx, cy + 4f * scale, w - pad * 2, graphH),
                            _graphTex, ScaleMode.StretchToFill, true);
        }
    }

    float CurrentTotalMs()
    {
        int idx = (_historyIndex - 1 + kHistory) % kHistory;
        return _history[idx];
    }

    static Color BudgetColor(float ms, float budget)
    {
        if (budget <= 0f) return Color.white;
        float ratio = ms / budget;
        if (ratio < 0.8f) return new Color(0.3f, 1f, 0.4f);   // 초록
        if (ratio < 1.0f) return new Color(1f, 0.9f, 0.2f);   // 노랑
        return new Color(1f, 0.35f, 0.3f);                    // 빨강
    }

    void DrawRect(Rect r, Color c)
    {
        var prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, _whiteTex);
        GUI.color = prev;
    }
}
#endif // UNITY_EDITOR || DEVELOPMENT_BUILD
