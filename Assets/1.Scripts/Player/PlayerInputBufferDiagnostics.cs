#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// 선입력 버퍼 진단 로그(할 일 44 후속) — 0.2초 창이 실제 플레이에서 얼마나 자주 조금 늦거나 일러서 놓치는지 숫자로 본다.
// 관측 전용이다. 버퍼 규칙(PlayerInputBuffer·PlayerInputReader)은 이 파일에 의존하지 않고, 훅은 전부
// [Conditional] 이라 심볼(UNITY_EDITOR·DEVELOPMENT_BUILD·DEV_HOTKEYS)이 없으면 호출째 사라진다.

/// <summary>선입력 버퍼 진단 이벤트 종류.</summary>
public enum InputBufferDiagEvent
{
    /// <summary>키를 눌러 버퍼에 기록됐다.</summary>
    Press,
    /// <summary>누른 프레임에 실행되지 못해 버퍼에 남았다(사유 포함).</summary>
    Buffered,
    /// <summary>실행됐다 — 누른 프레임이면 즉시, 아니면 버퍼에서.</summary>
    Consumed,
    /// <summary>유지 시간이 지나 버려졌다.</summary>
    Expired,
    /// <summary>자원 부족·차단 효과·기타 거부·비움으로 버려졌다(사유 포함).</summary>
    Dropped,
    /// <summary>새 입력에 실제로 덮여 버려졌다(같거나 높은 우선순위).</summary>
    Overwritten,
    /// <summary>바빴던 행동이 끝나 Idle/Move 로 돌아왔다 — 직전 만료 입력이 있으면 놓친 간격을 함께 남긴다.</summary>
    Actionable,
    /// <summary>
    /// 기본 공격 중 스킬 입력을 공격 뒤로 예약했다(거너 — 현재 발 마무리 후 시전). 그 입력은 버퍼에서 예약으로 옮겨 가
    /// 만료·실행 이벤트가 더 나지 않는다(같은 스킬 연타도 1건씩 남되 실행은 1번).
    /// </summary>
    SkillReservedAfterAttack,
    /// <summary>버퍼에 살아 있는 입력보다 우선순위가 낮아 기록되지 않았다(할 일 77). 들고 있던 입력은 그대로다.</summary>
    IgnoredLowerPriority
}

/// <summary>진단 이벤트 1건. 시간은 전부 초(Time.time 기준), 없으면 음수.</summary>
public readonly struct InputBufferDiagRecord
{
    public InputBufferDiagRecord(
        InputBufferDiagEvent evt, PlayerBufferedInput input, float time, int frame,
        float elapsed = -1f, float missedGap = -1f, float pressToActionable = -1f,
        bool immediate = false, string detail = null)
    {
        Event = evt;
        Input = input;
        Time = time;
        Frame = frame;
        Elapsed = elapsed;
        MissedGap = missedGap;
        PressToActionable = pressToActionable;
        Immediate = immediate;
        Detail = detail;
    }

    public InputBufferDiagEvent Event { get; }
    public PlayerBufferedInput Input { get; }
    public float Time { get; }
    public int Frame { get; }
    /// <summary>press 로부터 이 이벤트까지(Consumed·Expired·Dropped·Overwritten).</summary>
    public float Elapsed { get; }
    /// <summary>Actionable — 직전 만료 시각(press + 유지 시간)부터 실행 가능해질 때까지. 놓친 입력이 없으면 음수.</summary>
    public float MissedGap { get; }
    /// <summary>Actionable — 그 입력의 press 부터 실행 가능해질 때까지(놓친 입력 또는 아직 대기 중인 입력).</summary>
    public float PressToActionable { get; }
    /// <summary>Consumed — 누른 프레임에 바로 실행됐다(버퍼를 거치지 않음).</summary>
    public bool Immediate { get; }
    public string Detail { get; }
}

/// <summary>
/// 버퍼가 들고 있는 입력 1개를 <see cref="PlayerInputBuffer"/> 와 같은 규칙(1개·유지 시간 초과 = 만료)으로 따라가며
/// 진단 이벤트를 만든다. 우선순위 판정은 버퍼가 하고, 호출자가 결과대로 Press 또는 IgnoredLowerPriority 를 부른다.
/// 시간·프레임은 호출자가 넘긴다(MonoBehaviour 아님 — EditMode 테스트 대상).
/// </summary>
public sealed class PlayerInputBufferDiagTracker
{
    private readonly Action<InputBufferDiagRecord> sink;
    private readonly Func<string> describeWait;

    private PlayerBufferedInput pendingKind = PlayerBufferedInput.None;
    private float pendingPressTime;
    private float pendingHold;
    private int pendingPressFrame;
    private bool pendingBuffered;

    // 바쁜 동안(Idle/Move 아님) 만료된 마지막 입력 — 다음 Actionable 에서 놓친 간격으로 쓴다.
    private bool actionable = true;
    private PlayerBufferedInput missedKind = PlayerBufferedInput.None;
    private float missedPressTime;
    private float missedExpiredAt;

    /// <param name="sink">이벤트를 받을 곳.</param>
    /// <param name="describeWait">누른 프레임에 실행 못 한 이유(바쁨·CC 등). Buffered 를 낼 때만 부른다.</param>
    public PlayerInputBufferDiagTracker(Action<InputBufferDiagRecord> sink, Func<string> describeWait = null)
    {
        this.sink = sink;
        this.describeWait = describeWait;
    }

    public PlayerBufferedInput Pending => pendingKind;
    public bool IsActionable => actionable;

    /// <summary>
    /// 버퍼보다 낮은 우선순위라 기록되지 않은 press. 들고 있던 입력(<paramref name="held"/>)의 추적은 그대로 둔다.
    /// elapsed = 들고 있던 입력의 press 부터.
    /// </summary>
    public void IgnoredLowerPriority(PlayerBufferedInput input, PlayerBufferedInput held, float now, int frame)
    {
        CheckExpiry(now, frame);
        if (input == PlayerBufferedInput.None)
            return;

        float elapsed = pendingKind != PlayerBufferedInput.None && pendingKind == held ? now - pendingPressTime : -1f;
        Emit(new InputBufferDiagRecord(InputBufferDiagEvent.IgnoredLowerPriority, input, now, frame,
            elapsed: elapsed, detail: $"보관 중 {held}"));
    }

    /// <summary>버퍼에 기록된 press. 살아 있던 이전 입력은 Overwritten(같거나 낮은 우선순위라 실제로 덮였다).</summary>
    public void Press(PlayerBufferedInput input, float now, int frame, float holdSeconds)
    {
        if (input == PlayerBufferedInput.None || holdSeconds <= 0f)
            return;

        CheckExpiry(now, frame);
        if (pendingKind != PlayerBufferedInput.None)
        {
            Emit(new InputBufferDiagRecord(InputBufferDiagEvent.Overwritten, pendingKind, now, frame,
                elapsed: now - pendingPressTime, detail: $"새 입력 {input}"));
        }

        pendingKind = input;
        pendingPressTime = now;
        pendingHold = holdSeconds;
        pendingPressFrame = frame;
        pendingBuffered = false;
        Emit(new InputBufferDiagRecord(InputBufferDiagEvent.Press, input, now, frame));
    }

    /// <summary>프레임마다 버퍼 캡처 시점에 부른다 — 만료를 확인하고, 누른 프레임에 실행 못 한 입력을 Buffered 로 남긴다.</summary>
    public void BeginFrame(float now, int frame)
    {
        CheckExpiry(now, frame);
        if (pendingKind != PlayerBufferedInput.None && !pendingBuffered && frame > pendingPressFrame)
            MarkBuffered(now, frame, describeWait?.Invoke());
    }

    /// <summary>시작을 시도했지만 막혀(바쁨·CC) 남겨 둔다 — 처음 한 번만 Buffered.</summary>
    public void Blocked(PlayerBufferedInput input, float now, int frame, string reason)
    {
        CheckExpiry(now, frame);
        if (input != PlayerBufferedInput.None && pendingKind == input && !pendingBuffered)
            MarkBuffered(now, frame, reason);
    }

    /// <summary>실행됐다. input = None 이면 종류와 무관하게 들고 있던 입력을 실행된 것으로 본다(행동 시작으로 버퍼 전체 비움).</summary>
    public void Consumed(PlayerBufferedInput input, float now, int frame, string detail)
    {
        CheckExpiry(now, frame);
        if (!Matches(input))
            return;

        bool immediate = !pendingBuffered && frame == pendingPressFrame;
        Emit(new InputBufferDiagRecord(InputBufferDiagEvent.Consumed, pendingKind, now, frame,
            elapsed: now - pendingPressTime, immediate: immediate, detail: detail));
        pendingKind = PlayerBufferedInput.None;
    }

    /// <summary>실행되지 않고 버려졌다. input = None 이면 종류 무관(버퍼 전체 비움).</summary>
    public void Dropped(PlayerBufferedInput input, float now, int frame, string reason)
    {
        CheckExpiry(now, frame);
        if (!Matches(input))
            return;

        Emit(new InputBufferDiagRecord(InputBufferDiagEvent.Dropped, pendingKind, now, frame,
            elapsed: now - pendingPressTime, detail: reason));
        pendingKind = PlayerBufferedInput.None;
    }

    /// <summary>
    /// 행동 상태가 바뀔 때 부른다. 바쁨 → 실행 가능으로 바뀌면 Actionable — 바쁜 동안 만료된 입력이 있으면
    /// 놓친 간격(지금 - 만료 시각)과 press→지금을, 아직 대기 중인 입력이 있으면 press→지금을 싣는다.
    /// </summary>
    public void StateChanged(bool nowActionable, float now, int frame, string detail)
    {
        CheckExpiry(now, frame);
        if (nowActionable == actionable)
            return;

        actionable = nowActionable;
        if (!nowActionable)
        {
            missedKind = PlayerBufferedInput.None;
            return;
        }

        PlayerBufferedInput kind = PlayerBufferedInput.None;
        float missedGap = -1f;
        float pressToActionable = -1f;
        if (missedKind != PlayerBufferedInput.None)
        {
            kind = missedKind;
            missedGap = now - missedExpiredAt;
            pressToActionable = now - missedPressTime;
        }
        else if (pendingKind != PlayerBufferedInput.None)
        {
            kind = pendingKind;
            pressToActionable = now - pendingPressTime;
        }

        missedKind = PlayerBufferedInput.None;
        Emit(new InputBufferDiagRecord(InputBufferDiagEvent.Actionable, kind, now, frame,
            missedGap: missedGap, pressToActionable: pressToActionable, detail: detail));
    }

    /// <summary>기본 공격 중 스킬 입력을 공격 뒤로 예약했다. 들고 있던 그 입력은 예약으로 옮겨 가므로 추적을 끝낸다(만료로 세지 않는다).</summary>
    public void SkillReservedAfterAttack(PlayerBufferedInput input, float now, int frame, string detail)
    {
        CheckExpiry(now, frame);
        if (input == PlayerBufferedInput.None)
            return;

        float elapsed = pendingKind == input ? now - pendingPressTime : -1f;
        Emit(new InputBufferDiagRecord(InputBufferDiagEvent.SkillReservedAfterAttack, input, now, frame,
            elapsed: elapsed, detail: detail));
        if (pendingKind == input)
            pendingKind = PlayerBufferedInput.None;
    }

    private bool Matches(PlayerBufferedInput input) =>
        pendingKind != PlayerBufferedInput.None && (input == PlayerBufferedInput.None || input == pendingKind);

    private void MarkBuffered(float now, int frame, string reason)
    {
        pendingBuffered = true;
        Emit(new InputBufferDiagRecord(InputBufferDiagEvent.Buffered, pendingKind, now, frame,
            elapsed: now - pendingPressTime, detail: reason));
    }

    // PlayerInputBuffer.Peek 와 같은 조건(now - recordedAt > hold).
    private void CheckExpiry(float now, int frame)
    {
        if (pendingKind == PlayerBufferedInput.None || now - pendingPressTime <= pendingHold)
            return;

        if (!actionable)
        {
            missedKind = pendingKind;
            missedPressTime = pendingPressTime;
            missedExpiredAt = pendingPressTime + pendingHold;
        }

        Emit(new InputBufferDiagRecord(InputBufferDiagEvent.Expired, pendingKind, now, frame,
            elapsed: now - pendingPressTime, detail: actionable ? "실행 가능 상태에서 만료" : "행동 중 만료"));
        pendingKind = PlayerBufferedInput.None;
    }

    private void Emit(in InputBufferDiagRecord record) => sink?.Invoke(record);
}

/// <summary>진단 이벤트 집계 — 입력 종류별 개수와 놓친 간격·press→actionable 분포(중앙값·90%).</summary>
public sealed class PlayerInputBufferDiagStats
{
    private static readonly PlayerBufferedInput[] Inputs =
    {
        PlayerBufferedInput.SkillMain, PlayerBufferedInput.SkillSub, PlayerBufferedInput.SkillUltimate,
        PlayerBufferedInput.Attack, PlayerBufferedInput.SkillInterrupt, PlayerBufferedInput.Dash
    };

    private readonly Dictionary<PlayerBufferedInput, int[]> counts = new Dictionary<PlayerBufferedInput, int[]>();
    private readonly Dictionary<PlayerBufferedInput, int> immediateCounts = new Dictionary<PlayerBufferedInput, int>();

    /// <summary>놓친 간격(초) — 만료 시각부터 실행 가능해질 때까지.</summary>
    public List<float> MissedGaps { get; } = new List<float>();
    /// <summary>놓친 입력의 press→actionable(초).</summary>
    public List<float> MissedPressToActionable { get; } = new List<float>();
    /// <summary>버퍼에 살아 있던(놓치지 않은) 입력의 press→actionable(초).</summary>
    public List<float> HeldPressToActionable { get; } = new List<float>();

    public int TotalEvents { get; private set; }

    public void Add(in InputBufferDiagRecord record)
    {
        TotalEvents++;
        if (record.Event == InputBufferDiagEvent.Actionable)
        {
            if (record.MissedGap >= 0f)
            {
                MissedGaps.Add(record.MissedGap);
                MissedPressToActionable.Add(record.PressToActionable);
            }
            else if (record.PressToActionable >= 0f)
            {
                HeldPressToActionable.Add(record.PressToActionable);
            }
        }

        if (record.Input == PlayerBufferedInput.None)
            return;

        if (!counts.TryGetValue(record.Input, out int[] perEvent))
        {
            perEvent = new int[Enum.GetValues(typeof(InputBufferDiagEvent)).Length];
            counts[record.Input] = perEvent;
        }

        perEvent[(int)record.Event]++;
        if (record.Event == InputBufferDiagEvent.Consumed && record.Immediate)
        {
            immediateCounts.TryGetValue(record.Input, out int immediate);
            immediateCounts[record.Input] = immediate + 1;
        }
    }

    public int Count(PlayerBufferedInput input, InputBufferDiagEvent evt) =>
        counts.TryGetValue(input, out int[] perEvent) ? perEvent[(int)evt] : 0;

    public int ImmediateCount(PlayerBufferedInput input) =>
        immediateCounts.TryGetValue(input, out int immediate) ? immediate : 0;

    /// <summary>선형 보간 백분위(p = 0~1). 비어 있으면 NaN. 원본 목록은 건드리지 않는다.</summary>
    public static float Percentile(IReadOnlyList<float> values, float p)
    {
        if (values == null || values.Count == 0)
            return float.NaN;

        var sorted = new List<float>(values);
        sorted.Sort();
        float rank = Mathf.Clamp01(p) * (sorted.Count - 1);
        int lower = Mathf.FloorToInt(rank);
        int upper = Mathf.Min(lower + 1, sorted.Count - 1);
        return Mathf.Lerp(sorted[lower], sorted[upper], rank - lower);
    }

    public string BuildSummary(float holdSeconds, string csvPath)
    {
        var sb = new StringBuilder();
        sb.Append(PlayerInputBufferDiagnostics.Prefix)
            .Append(" 요약 — 유지 ").Append(Ms(holdSeconds)).Append("ms, 이벤트 ").Append(TotalEvents)
            .Append("건, CSV: ").Append(csvPath ?? "(없음)").AppendLine();
        sb.AppendLine("입력 | press | 즉시 실행 | 버퍼 실행 | 만료 | 버림 | 덮임 | 평타 뒤 예약 | 낮은 우선순위 무시");
        foreach (PlayerBufferedInput input in Inputs)
        {
            int consumed = Count(input, InputBufferDiagEvent.Consumed);
            int immediate = ImmediateCount(input);
            sb.Append(PlayerInputBufferDiagnostics.KeyLabel(input))
                .Append(" | ").Append(Count(input, InputBufferDiagEvent.Press))
                .Append(" | ").Append(immediate)
                .Append(" | ").Append(consumed - immediate)
                .Append(" | ").Append(Count(input, InputBufferDiagEvent.Expired))
                .Append(" | ").Append(Count(input, InputBufferDiagEvent.Dropped))
                .Append(" | ").Append(Count(input, InputBufferDiagEvent.Overwritten))
                .Append(" | ").Append(Count(input, InputBufferDiagEvent.SkillReservedAfterAttack))
                .Append(" | ").Append(Count(input, InputBufferDiagEvent.IgnoredLowerPriority))
                .AppendLine();
        }

        AppendDistribution(sb, "놓친 간격(실행 가능 - 만료)", MissedGaps);
        AppendDistribution(sb, "press→actionable(놓침)", MissedPressToActionable);
        AppendDistribution(sb, "press→actionable(버퍼 성공)", HeldPressToActionable);
        return sb.ToString();
    }

    private static void AppendDistribution(StringBuilder sb, string label, List<float> values)
    {
        sb.Append(label).Append(": n=").Append(values.Count);
        if (values.Count > 0)
        {
            sb.Append(", 중앙값 ").Append(Ms(Percentile(values, 0.5f))).Append("ms")
                .Append(", 90% ").Append(Ms(Percentile(values, 0.9f))).Append("ms");
        }

        sb.AppendLine();
    }

    private static string Ms(float seconds) =>
        Mathf.RoundToInt(seconds * 1000f).ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// 진단 세션 — 오너 클라 프로세스 하나에 하나. 이벤트를 콘솔(<see cref="Prefix"/>)과 CSV 로 남기고 Play 종료 때 요약을 낸다.
/// CSV: 에디터 = 프로젝트 Logs/InputBuffer-&lt;시각&gt;.csv, 빌드 = Application.persistentDataPath.
/// </summary>
public static class PlayerInputBufferDiagnostics
{
    public const string Prefix = "[InputBuf]";
    private const string CsvHeader =
        "time_ms,frame,event,input,state,action,elapsed_ms,missed_gap_ms,press_to_actionable_ms,immediate,detail";

    /// <summary>켜고 끄는 토글(기본 켜짐). 에디터는 메뉴 Tools/Debug/선입력 버퍼 진단 로그 로도 바꾼다.</summary>
    public static bool Enabled = true;

    private static StreamWriter writer;
    private static string csvPath;
    private static PlayerInputBufferDiagStats stats;
    private static float lastHoldSeconds;
    private static bool quitHooked;

    public static string CsvPath => csvPath;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        CloseWriter();
        csvPath = null;
        stats = null;
        lastHoldSeconds = 0f;
        if (quitHooked)
        {
            Application.quitting -= OnQuitting;
            quitHooked = false;
        }
#if UNITY_EDITOR
        Enabled = UnityEditor.EditorPrefs.GetBool(EnabledPrefKey, true);
#endif
    }

#if UNITY_EDITOR
    public const string EnabledPrefKey = "MainProject.InputBufferDiagnostics.Enabled";
#endif

    public static string KeyLabel(PlayerBufferedInput input) => input switch
    {
        PlayerBufferedInput.Attack => "LMB",
        PlayerBufferedInput.Dash => "Space",
        PlayerBufferedInput.SkillMain => "Q",
        PlayerBufferedInput.SkillSub => "E",
        PlayerBufferedInput.SkillInterrupt => "RMB",
        PlayerBufferedInput.SkillUltimate => "R",
        _ => "-"
    };

    public static void Write(in InputBufferDiagRecord record, PlayerActionState state, string action, float holdSeconds)
    {
        EnsureSession();
        lastHoldSeconds = holdSeconds;
        stats.Add(record);

        Debug.Log($"{Prefix} t={Ms(record.Time)}ms f={record.Frame} {EventName(record.Event)} " +
                  $"{KeyLabel(record.Input)} state={state} action={action}{DescribeNumbers(record)}" +
                  (string.IsNullOrEmpty(record.Detail) ? "" : $" — {record.Detail}"));

        if (writer == null)
            return;

        try
        {
            writer.WriteLine(string.Join(",",
                Ms(record.Time),
                record.Frame.ToString(CultureInfo.InvariantCulture),
                EventName(record.Event),
                KeyLabel(record.Input),
                state.ToString(),
                Csv(action),
                OptionalMs(record.Elapsed),
                OptionalMs(record.MissedGap),
                OptionalMs(record.PressToActionable),
                record.Event == InputBufferDiagEvent.Consumed ? (record.Immediate ? "1" : "0") : "",
                Csv(record.Detail)));
            writer.Flush();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{Prefix} CSV 쓰기 실패 — 이후 콘솔만 남긴다: {e.Message}");
            CloseWriter();
        }
    }

    private static void EnsureSession()
    {
        if (stats != null)
            return;

        stats = new PlayerInputBufferDiagStats();
        if (!quitHooked)
        {
            Application.quitting += OnQuitting;
            quitHooked = true;
        }

        try
        {
#if UNITY_EDITOR
            string directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs");
#else
            string directory = Application.persistentDataPath;
#endif
            Directory.CreateDirectory(directory);
            csvPath = Path.Combine(directory, $"InputBuffer-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            writer = new StreamWriter(csvPath, false, new UTF8Encoding(true));
            writer.WriteLine(CsvHeader);
            Debug.Log($"{Prefix} 진단 로그 시작 — CSV: {csvPath}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{Prefix} CSV 를 열지 못해 콘솔만 남긴다: {e.Message}");
            csvPath = null;
            CloseWriter();
        }
    }

    // 에디터는 Play 종료 때도 불린다.
    private static void OnQuitting()
    {
        if (stats != null)
        {
            Debug.Log(stats.BuildSummary(lastHoldSeconds, csvPath));
            Debug.Log($"{Prefix} CSV: {csvPath ?? "(없음)"}");
        }

        CloseWriter();
        stats = null;
        csvPath = null;
    }

    private static void CloseWriter()
    {
        if (writer == null)
            return;

        try { writer.Dispose(); }
        catch (Exception) { /* 닫기 실패는 무시 — 진단 전용 */ }
        writer = null;
    }

    public static string EventName(InputBufferDiagEvent evt) => evt switch
    {
        InputBufferDiagEvent.Press => "press",
        InputBufferDiagEvent.Buffered => "buffered",
        InputBufferDiagEvent.Consumed => "consumed",
        InputBufferDiagEvent.Expired => "expired",
        InputBufferDiagEvent.Dropped => "dropped",
        InputBufferDiagEvent.Overwritten => "overwritten",
        InputBufferDiagEvent.Actionable => "actionable",
        InputBufferDiagEvent.SkillReservedAfterAttack => "skill-reserved-after-attack",
        InputBufferDiagEvent.IgnoredLowerPriority => "ignored-lower-priority",
        _ => evt.ToString()
    };

    private static string DescribeNumbers(in InputBufferDiagRecord record)
    {
        switch (record.Event)
        {
            case InputBufferDiagEvent.Consumed:
                return record.Immediate ? " 즉시" : $" press→실행 {Ms(record.Elapsed)}ms";
            case InputBufferDiagEvent.Buffered:
            case InputBufferDiagEvent.Expired:
            case InputBufferDiagEvent.Dropped:
            case InputBufferDiagEvent.Overwritten:
                return $" press→{Ms(record.Elapsed)}ms";
            case InputBufferDiagEvent.IgnoredLowerPriority:
                return record.Elapsed >= 0f ? $" 보관 입력 press→{Ms(record.Elapsed)}ms" : "";
            case InputBufferDiagEvent.Actionable:
                if (record.MissedGap >= 0f)
                    return $" 놓친 간격 {Ms(record.MissedGap)}ms, press→actionable {Ms(record.PressToActionable)}ms";
                return record.PressToActionable >= 0f ? $" 대기 입력 press→actionable {Ms(record.PressToActionable)}ms" : "";
            default:
                return "";
        }
    }

    private static string Ms(float seconds) =>
        Mathf.RoundToInt(seconds * 1000f).ToString(CultureInfo.InvariantCulture);

    private static string OptionalMs(float seconds) => seconds >= 0f ? Ms(seconds) : "";

    private static string Csv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0
            ? value
            : "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
#endif
