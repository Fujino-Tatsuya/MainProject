#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System;
using System.Globalization;
using System.Text;
using BeaverLobby.Player.Dash;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 간파 예측 보정 질문 창 + 상시 패널(할 일 139 단계 4). 랩 씬(InterruptCalibrationLab)에만 둔다 — OnGUI.
///
/// <list type="bullet">
/// <item><b>질문.</b> <see cref="InterruptCalibrationCapture.SampleReady"/> 표본을 <see cref="InterruptCalibrationQuestionFlow"/> 에 넣고,
///       판정 + 0.5초(실시간) 뒤 시간 배율이 평소(<see cref="GlobalTimeScale.IsActive"/> = false — 슬로우·히트스톱·실패 복귀 끝)면 시간을 멈추고 묻는다.
///       평소로 안 돌아와도 판정 + 3초(실시간)면 그냥 묻는다. 답: Y = 성공이어야 했음(true) · N = 실패여야 했음(false) · Esc = 건너뛰기(기록 안 함), 버튼도 같다.</item>
/// <item><b>정지.</b> 멈출 때 <see cref="Time.timeScale"/> 을 기억하고 0 으로, 솔로 호스트면 <see cref="NetworkClock.Pause"/> 로 게임 시계(슬로우 타임라인)도 멈춘다.
///       <see cref="GlobalTimeScale"/> 는 슬로우·디버그 배속이 걸려 있으면 매 프레임 timeScale 을 다시 쓴다 — 이 컴포넌트가 가장 늦은 실행 순서의 LateUpdate 에서
///       0 을 덮어써 다음 프레임 deltaTime 이 0 이 되게 한다. 재개 = 기억한 배율로 돌려놓고 시계 재개(그 뒤엔 GlobalTimeScale 이 평소대로 쓴다).</item>
/// <item><b>입력.</b> 묻는 동안·패널 위에 마우스가 있는 동안 <see cref="UiInputGateManager"/> 토큰으로 로컬 플레이어 입력(이동·대시·간파·스킬)을 막는다.
///       답한 뒤엔 마우스 버튼을 모두 뗄 때까지 막아 둔다 — 버튼 클릭이 공격으로 새지 않게. 길이 칸은 슬라이더라 키보드 입력이 없다.</item>
/// <item><b>기록.</b> 씬 시작(Awake)마다 새 <see cref="InterruptCalibrationRecorder"/> 세션 폴더. 답(true/false)마다 Commit,
///       summary.txt 는 비활성(씬 나감 포함)·앱 종료·패널 버튼에서 쓴다(표본 0 개면 안 씀).</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(32000)] // LateUpdate 의 timeScale 0 덮어쓰기가 GlobalTimeScale(-100)을 포함한 모든 쓰기보다 늦게
public sealed class InterruptCalibrationQuestionPanel : MonoBehaviour
{
    const string Tag = "[InterruptCalib]";
    public const float LengthSliderMax = 3f;

    [Tooltip("표본을 내는 캡처기. 비우면 씬에서 찾는다.")]
    [SerializeField] InterruptCalibrationCapture capture;

    [Tooltip("허수아비 창 모드. 비우면 씬에서 찾는다(랩 허수아비 하나 전제).")]
    [SerializeField] DebugDummyInterruptWindowMode windowMode;

    [Tooltip("기록 루트 폴더. 비우면 C:/UnityProject/QA.")]
    [SerializeField] string recordRoot = InterruptCalibrationRecorder.DefaultRoot;

    [Tooltip("판정 뒤 결과(성공 연출·실패 복귀)가 다 보일 때까지 기다리는 실시간(초).")]
    [SerializeField, Min(0f)] float settleDelay = InterruptCalibrationQuestionFlow.DefaultSettleDelay;

    [Tooltip("판정 뒤 이 실시간(초)이 지나면 슬로우가 안 끝나도 그냥 묻는다.")]
    [SerializeField, Min(0f)] float maxWait = InterruptCalibrationQuestionFlow.DefaultMaxWait;

    [Tooltip("디버그 배속(DebugTimeScale)의 '성공 후 1배속' 이 끝날 때까지도 기다린다(기본 끔 — 그 동안도 1배속이라 화면은 평소와 같다).")]
    [SerializeField] bool waitDebugNormalSpeedHold;

    [Tooltip("질문 중에 다음 표본이 오면 줄 세울 최대 개수. 넘치면 새로 온 표본을 버린다.")]
    [SerializeField, Min(0)] int queueCapacity = InterruptCalibrationQuestionFlow.DefaultQueueCapacity;

    [Tooltip("상시 패널 너비(px). 화면 왼쪽 아래.")]
    [SerializeField, Min(200f)] float panelWidth = 320f;

    [Tooltip("패널을 펼친 채로 시작")]
    [SerializeField] bool expanded = true;

    readonly InterruptCalibrationQuestionFlow _flow = new InterruptCalibrationQuestionFlow();
    readonly CalibrationTimePause _pause = new CalibrationTimePause();
    readonly object _questionGate = new object();
    readonly object _hoverGate = new object();

    InterruptCalibrationRecorder _recorder;
    DebugTimeScale _debugTimeScale;
    NetworkClock _pausedClock;
    bool _questionGateHeld;
    bool _hoverGateHeld;
    Rect _panelRect;
    Rect _questionRect;
    string _lastSummaryPath;

    public InterruptCalibrationRecorder Recorder => _recorder;
    public InterruptCalibrationQuestionFlow Flow => _flow;

    void Awake()
    {
        _recorder = new InterruptCalibrationRecorder(recordRoot, DateTime.Now);
    }

    void OnEnable()
    {
        if (capture == null)
            capture = FindAnyObjectByType<InterruptCalibrationCapture>();
        if (capture != null)
            capture.SampleReady += HandleSampleReady;
        else
            Debug.LogWarning($"{Tag} 캡처기(InterruptCalibrationCapture)가 없다 — 질문 창이 표본을 받지 못한다.", this);
    }

    void OnDisable()
    {
        if (capture != null)
            capture.SampleReady -= HandleSampleReady;

        _flow.Clear();
        EndPause();
        SetQuestionGate(false);
        SetHoverGate(false);
        WriteSummary();
    }

    void OnApplicationQuit() => WriteSummary();

    void HandleSampleReady(InterruptCalibrationCompleted completed) => _flow.Enqueue(completed);

    void Update()
    {
        ApplySettings();

        if (_flow.IsAsking)
            HandleAnswerKeys();

        if (_flow.Tick(Time.realtimeSinceStartup, IsTimeSettled()))
            BeginPause();

        UpdateInputGates();
    }

    void LateUpdate()
    {
        if (_pause.IsPaused)
            Time.timeScale = CalibrationTimePause.HeldScale;
    }

    void ApplySettings()
    {
        _flow.SettleDelay = settleDelay;
        _flow.MaxWait = maxWait;
        _flow.QueueCapacity = queueCapacity;
    }

    // ─── 시간 ───────────────────────────────────────────────────────────

    bool IsTimeSettled()
    {
        GlobalTimeScale global = GlobalTimeScale.Instance;
        if (global != null && global.IsActive)
            return false;

        if (waitDebugNormalSpeedHold)
        {
            if (_debugTimeScale == null)
                _debugTimeScale = FindAnyObjectByType<DebugTimeScale>();
            if (_debugTimeScale != null && _debugTimeScale.IsHoldingNormalSpeed)
                return false;
        }

        return true;
    }

    void BeginPause()
    {
        if (!_pause.Begin(Time.timeScale))
            return;

        Time.timeScale = CalibrationTimePause.HeldScale;

        // 솔로 호스트 일시정지 — 게임 시계(GameNow)·슬로우 타임라인도 멈춘다. 다른 누가 이미 멈춰 뒀으면 건드리지 않는다.
        NetworkClock clock = NetworkClock.Instance;
        NetworkManager network = NetworkManager.Singleton;
        if (clock != null && clock.IsRunning && !clock.IsPaused && network != null && network.IsServer)
        {
            clock.Pause();
            _pausedClock = clock;
        }
    }

    void EndPause()
    {
        float? restore = _pause.End();
        if (!restore.HasValue)
            return;

        Time.timeScale = restore.Value;
        if (_pausedClock != null)
            _pausedClock.Resume();
        _pausedClock = null;
    }

    // ─── 답 ─────────────────────────────────────────────────────────────

    void HandleAnswerKeys()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null)
            return;

        if (kb.yKey.wasPressedThisFrame)
            Answer(CalibrationAnswer.ShouldSucceed);
        else if (kb.nKey.wasPressedThisFrame)
            Answer(CalibrationAnswer.ShouldFail);
        else if (kb.escapeKey.wasPressedThisFrame)
            Answer(CalibrationAnswer.Skip);
    }

    /// <summary>묻는 중이면 답을 처리한다. 다음 표본을 바로 물을 수 있으면 멈춘 채로 두고, 아니면 재개한다.</summary>
    public void Answer(CalibrationAnswer answer)
    {
        if (!_flow.Answer(answer, out CalibrationAnswerOutcome outcome))
            return;

        if (outcome.Commit)
        {
            try
            {
                _recorder.Commit(outcome.Answered.Sample, outcome.Value);
            }
            catch (Exception e)
            {
                Debug.LogError($"{Tag} 표본 기록 실패({_recorder.SamplesPath}): {e.Message}", this);
            }
        }

        // 줄 서 있던 표본은 판정이 이미 지났다 — 바로 물을 수 있으면 재개하지 않고 이어서 묻는다(한 프레임 풀렸다 다시 멈추는 깜빡임 방지).
        _flow.Tick(Time.realtimeSinceStartup, IsTimeSettled());
        if (!_flow.IsAsking)
            EndPause();
    }

    // ─── 입력 차단 ──────────────────────────────────────────────────────

    void UpdateInputGates()
    {
        Mouse mouse = Mouse.current;
        bool anyMouseHeld = mouse != null &&
            (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed);

        // 답한 뒤엔 마우스 버튼을 다 뗄 때까지 막는다 — 버튼을 누른 손이 공격·간파로 이어지지 않게.
        SetQuestionGate(_flow.IsAsking || (_questionGateHeld && anyMouseHeld));

        bool hover = false;
        if (mouse != null)
        {
            Vector2 p = mouse.position.ReadValue();
            var gui = new Vector2(p.x, Screen.height - p.y);
            hover = _panelRect.Contains(gui) || (_flow.IsAsking && _questionRect.Contains(gui));
        }
        // 패널 위에서 누른 채 밖으로 나가도 뗄 때까지 막는다.
        SetHoverGate(hover || (_hoverGateHeld && anyMouseHeld));
    }

    void SetQuestionGate(bool on)
    {
        if (on == _questionGateHeld)
            return;
        _questionGateHeld = on;
        if (on)
            UiInputGateManager.Acquire(_questionGate);
        else
            UiInputGateManager.Release(_questionGate);
    }

    void SetHoverGate(bool on)
    {
        if (on == _hoverGateHeld)
            return;
        _hoverGateHeld = on;
        if (on)
            UiInputGateManager.Acquire(_hoverGate);
        else
            UiInputGateManager.Release(_hoverGate);
    }

    // ─── 기록 ───────────────────────────────────────────────────────────

    /// <summary>summary.txt 를 (다시) 쓴다. 표본 0 개면 안 쓴다.</summary>
    public void WriteSummary()
    {
        if (_recorder == null)
            return;

        try
        {
            string path = _recorder.WriteSummary();
            if (path != null)
            {
                _lastSummaryPath = path;
                Debug.Log($"{Tag} summary 기록 — {path}", this);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"{Tag} summary 기록 실패({_recorder.SummaryPath}): {e.Message}", this);
        }
    }

    // ─── 화면 ───────────────────────────────────────────────────────────

    void OnGUI()
    {
        DrawPanel();
        if (_flow.IsAsking)
            DrawQuestion();
        else
            _questionRect = Rect.zero;
    }

    void DrawPanel()
    {
        float height = expanded ? 420f : 30f;
        _panelRect = new Rect(10f, Screen.height - height - 10f, panelWidth, height);
        GUILayout.BeginArea(_panelRect);
        GUILayout.BeginVertical(GUI.skin.box);

        if (GUILayout.Button(expanded ? "▼ 간파 예측 보정(139)" : "▶ 간파 예측 보정(139)"))
            expanded = !expanded;

        if (expanded)
        {
            DrawStats();
            DrawRecord();
            DrawWindowMode();
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    void DrawStats()
    {
        int dropped = capture != null ? capture.DroppedCount : 0;
        GUILayout.Label($"표본 {_recorder.Count} · 건너뜀 {_flow.SkippedCount} · 짝 실패 버림 {dropped}");
        GUILayout.Label($"대기열 {_flow.QueuedCount} · 넘쳐 버림 {_flow.OverflowDroppedCount} · {DescribePhase(_flow.Phase)}");
        foreach (CalibrationAgreement a in _recorder.Stats.All)
            GUILayout.Label($"{a.Name}: {a.FormatRate()}");
    }

    void DrawRecord()
    {
        GUILayout.Space(4f);
        GUILayout.Label("기록: " + _recorder.SessionFolder.Replace('\\', '/'));
        if (GUILayout.Button("지금 summary 쓰기"))
            WriteSummary();
        if (_recorder.Count == 0)
            GUILayout.Label("(표본 0 개 — summary 안 씀)");
        else if (_lastSummaryPath != null)
            GUILayout.Label("마지막 summary: " + System.IO.Path.GetFileName(_lastSummaryPath));
    }

    void DrawWindowMode()
    {
        GUILayout.Space(4f);
        if (windowMode == null)
            windowMode = FindAnyObjectByType<DebugDummyInterruptWindowMode>();
        if (windowMode == null)
        {
            GUILayout.Label("허수아비 창 모드 없음");
        }
        else
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("허수아비 창:");
            bool unbounded = windowMode.Mode == DummyInterruptWindowMode.Unbounded;
            if (GUILayout.Toggle(unbounded, "무기한", GUI.skin.button) && !unbounded)
                windowMode.SetMode(DummyInterruptWindowMode.Unbounded);
            if (GUILayout.Toggle(!unbounded, "주기", GUI.skin.button) && unbounded)
                windowMode.SetMode(DummyInterruptWindowMode.Periodic);
            GUILayout.EndHorizontal();

            float open = LengthRow("열림", windowMode.OpenSeconds);
            float closed = LengthRow("닫힘", windowMode.ClosedSeconds);
            if (!Mathf.Approximately(open, windowMode.OpenSeconds) || !Mathf.Approximately(closed, windowMode.ClosedSeconds))
                windowMode.SetDurations(open, closed);
        }

        if (GUILayout.Button("예측기 관측 기록 초기화"))
        {
            InterruptWindowReplicaTracker.ResetObservations();
            Debug.Log($"{Tag} 예측기 관측 기록 초기화", this);
        }
    }

    // 길이 한 줄 — 슬라이더(0~3s, 0.05 단위) + ±0.05 버튼. 키보드 입력 칸을 쓰지 않는다(숫자 키가 스킬로 새지 않게).
    static float LengthRow(string label, float value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{label} {value:0.00}s", GUILayout.Width(80f));
        if (GUILayout.Button("-", GUILayout.Width(22f)))
            value -= 0.05f;
        float slid = GUILayout.HorizontalSlider(value, 0f, Mathf.Max(LengthSliderMax, value));
        if (!Mathf.Approximately(slid, value))
            value = Mathf.Round(slid * 20f) / 20f;
        if (GUILayout.Button("+", GUILayout.Width(22f)))
            value += 0.05f;
        GUILayout.EndHorizontal();
        return Mathf.Max(0f, value);
    }

    void DrawQuestion()
    {
        const float width = 460f;
        const float height = 280f;
        _questionRect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
        GUILayout.BeginArea(_questionRect);
        GUILayout.BeginVertical(GUI.skin.box);

        GUILayout.Label($"간파 표본 #{_recorder.Count + 1} — 이번 간파는?" +
                        (_flow.QueuedCount > 0 ? $"  (뒤에 {_flow.QueuedCount}개 대기)" : ""));
        GUILayout.Label(DescribeSample(_flow.Current.Sample));
        GUILayout.FlexibleSpace();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Y  성공이어야 했음 (true)", GUILayout.Height(36f)))
            Answer(CalibrationAnswer.ShouldSucceed);
        if (GUILayout.Button("N  실패여야 했음 (false)", GUILayout.Height(36f)))
            Answer(CalibrationAnswer.ShouldFail);
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Esc  건너뛰기 (기록 안 함)"))
            Answer(CalibrationAnswer.Skip);

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    // ─── 문구(순수) ─────────────────────────────────────────────────────

    public static string DescribePhase(CalibrationQuestionPhase phase) => phase switch
    {
        CalibrationQuestionPhase.Waiting => "결과 보는 중",
        CalibrationQuestionPhase.Asking => "질문 중(정지)",
        _ => "대기",
    };

    public static string DescribeForecast(InterruptAttemptForecast? forecast) => forecast switch
    {
        InterruptAttemptForecast.Strong => "강",
        InterruptAttemptForecast.Weak => "약",
        InterruptAttemptForecast.NoTarget => "대상 없음",
        _ => "-",
    };

    /// <summary>질문 창 표본 요약 — 오너 예측+Reason, 서버 예측, 서버 결과, 판정 시점 실제 창 상태.</summary>
    public static string DescribeSample(InterruptCalibrationSample s)
    {
        if (s == null)
            return "-";

        var sb = new StringBuilder();
        sb.Append("오너 예측: ").Append(DescribeForecast(s.OwnerForecast))
          .Append(" (").Append(s.OwnerReason?.ToString() ?? "-").Append(")")
          .Append(s.OwnerRepredicted == true ? " → 재예측 " + DescribeForecast(s.OwnerFinalForecast) : "").Append('\n');
        sb.Append("서버 예측: ").Append(DescribeForecast(s.ServerForecast))
          .Append(s.ServerRepredicted == true ? " → 재예측 " + DescribeForecast(s.ServerFinalForecast) : "").Append('\n');
        sb.Append("서버 결과: ").Append(s.ServerSucceeded == true ? "성공" : s.ServerSucceeded == false ? "실패" : "-").Append('\n');
        sb.Append("실제 창: ").Append(s.ActualWindowOpen == true ? "열림" : s.ActualWindowOpen == false ? "닫힘" : "-")
          .Append(" · 경과 ").Append(Seconds(s.ActualOpenElapsed))
          .Append(" · 남음 ").Append(Seconds(s.ActualRemaining)).Append('\n');
        sb.Append("예측 창 남음 ").Append(Seconds(s.PredictedWindowRemaining))
          .Append(" · 대시→우클릭 ").Append(Seconds(s.PressAge))
          .Append(" · 대상 ").Append(s.TargetDistance.HasValue ? s.TargetDistance.Value.ToString("0.00", CultureInfo.InvariantCulture) + "m" : "-");
        return sb.ToString();
    }

    static string Seconds(float? value) =>
        !value.HasValue ? "-"
        : float.IsPositiveInfinity(value.Value) ? "무기한"
        : value.Value.ToString("0.000", CultureInfo.InvariantCulture) + "s";
}
#endif
