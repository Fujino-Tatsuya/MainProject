using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// [각 피어] 간파 QA 기록기(할 일 191 D12, PLAN §3.1 ④) — <see cref="InterruptManager"/> 이벤트(예측·판정·판정 전 종료·승인·거부·서버 시도 슬로우)를 구독해
/// 간파 1회 = 한 줄(<see cref="InterruptQaRecord"/>)로 짝짓고(<see cref="InterruptQaTracker"/>), 닫힌 줄을 <see cref="RecordCompleted"/> 로 낸다.
/// <list type="bullet">
/// <item>세션마다 하나 — 매니저가 세션 시작 때 <see cref="Begin"/>, 끝날 때 <see cref="End"/>(열린 줄을 닫고 summary 를 쓴다). 매 프레임 <see cref="Tick"/>.</item>
/// <item>파일(<paramref name="writeFile"/> — 매니저 칸 <c>qaRecordFile</c>, 에디터·개발 빌드만): <c>C:/UnityProject/QA/191interrupt-&lt;yyyyMMdd_HHmmss&gt;-&lt;host|client&gt;&lt;clientId&gt;/</c>
///       <c>interrupts.csv</c>(줄마다 즉시 덧붙임, UTF-8 BOM 헤더 1줄 — 보정 랩 139 기록과 같은 폴더·형식)·<c>summary.txt</c>(세션 끝). 줄 0 개 세션은 폴더를 만들지 않는다.</item>
/// <item>오너 피어가 예측·판정·서버 결과를 모두 받는다(승인 = 전 피어 배포, 거부 = 그 오너). 서버 시도 슬로우 칸은 서버 피어(호스트 오너)에서만 찬다.</item>
/// </list>
/// 보정 랩(<c>InterruptCalibrationCapture</c>)은 <see cref="RecordCompleted"/> 를 받아 사람 답과 실제 창 상태를 덧붙인다.
/// </summary>
public sealed class InterruptQaRecorder
{
    public const string DefaultRoot = "C:/UnityProject/QA";
    public const string FolderPrefix = "191interrupt-";
    public const string RecordsFileName = "interrupts.csv";
    public const string SummaryFileName = "summary.txt";
    const string Tag = "[InterruptQa]";

    static readonly Encoding Utf8Bom = new UTF8Encoding(true);
    static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    /// <summary>[각 피어] QA 기록 한 줄이 닫혔다(파일 기록 여부와 무관).</summary>
    public static event Action<InterruptQaRecord> RecordCompleted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => RecordCompleted = null;

    readonly InterruptQaTracker _tracker = new InterruptQaTracker();
    readonly Func<ulong, (ulong ClientId, string Character)> _describeActor;
    readonly Func<double> _now;

    InterruptQaSummary _summary;
    bool _active;
    bool _writeFile;
    bool _fileFailed;
    string _root;

    public string SessionFolder { get; private set; }
    public string RecordsPath => SessionFolder != null ? Path.Combine(SessionFolder, RecordsFileName) : null;
    public string SummaryPath => SessionFolder != null ? Path.Combine(SessionFolder, SummaryFileName) : null;
    public bool IsActive => _active;
    public InterruptQaTracker Tracker => _tracker;
    public InterruptQaSummary Summary => _summary;

    /// <param name="describeActor">시전자 NetworkObjectId → (오너 clientId, 캐릭터 이름).</param>
    /// <param name="now">지금 서버 시각.</param>
    public InterruptQaRecorder(Func<ulong, (ulong ClientId, string Character)> describeActor, Func<double> now)
    {
        _describeActor = describeActor;
        _now = now;
        _tracker.Completed += HandleCompleted;
    }

    /// <summary>세션 시작 — 이벤트 구독. <paramref name="root"/> 가 비면 <see cref="DefaultRoot"/>.</summary>
    public void Begin(bool isServer, ulong localClientId, bool writeFile, string root, DateTime startedAt)
    {
        if (_active)
            End();

        _active = true;
        _writeFile = writeFile;
        _fileFailed = false;
        _root = string.IsNullOrEmpty(root) ? DefaultRoot : root;
        _summary = new InterruptQaSummary();
        _tracker.Role = isServer ? "host" : "client";
        _tracker.LocalClientId = localClientId;
        SessionFolder = Path.Combine(_root, FolderName(startedAt, isServer, localClientId));

        InterruptManager.OwnerPredicted += HandleOwnerPredicted;
        InterruptManager.OwnerJudged += HandleOwnerJudged;
        InterruptManager.OwnerJudgeAbandoned += HandleOwnerJudgeAbandoned;
        InterruptManager.ApprovalReceived += HandleApproval;
        InterruptManager.RejectionReceived += HandleRejection;
        InterruptManager.ServerAttemptPredicted += HandleServerAttempt;
    }

    /// <summary>세션 끝 — 열린 줄을 닫고 summary 를 쓰고 구독을 푼다.</summary>
    public void End()
    {
        if (!_active)
            return;

        InterruptManager.OwnerPredicted -= HandleOwnerPredicted;
        InterruptManager.OwnerJudged -= HandleOwnerJudged;
        InterruptManager.OwnerJudgeAbandoned -= HandleOwnerJudgeAbandoned;
        InterruptManager.ApprovalReceived -= HandleApproval;
        InterruptManager.RejectionReceived -= HandleRejection;
        InterruptManager.ServerAttemptPredicted -= HandleServerAttempt;

        _tracker.Flush();
        WriteSummary();
        _active = false;
    }

    public void Tick(double now)
    {
        if (_active)
            _tracker.Tick(now);
    }

    public static string FolderName(DateTime startedAt, bool isServer, ulong localClientId) =>
        FolderPrefix + startedAt.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) +
        "-" + (isServer ? "host" : "client") + localClientId.ToString(CultureInfo.InvariantCulture);

    /// <summary>summary.txt 를 (다시) 쓴다. 파일 기록이 꺼졌거나 줄이 없으면 null.</summary>
    public string WriteSummary()
    {
        if (!_writeFile || _fileFailed || _summary == null || _summary.Count == 0 || SessionFolder == null)
            return null;

        try
        {
            Directory.CreateDirectory(SessionFolder);
            File.WriteAllText(SummaryPath, _summary.Build(Path.GetFileName(SessionFolder)), Utf8Bom);
            return SummaryPath;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{Tag} summary 기록 실패({SummaryPath}): {e.Message}");
            return null;
        }
    }

    // ---- 이벤트 ----

    void HandleOwnerPredicted(InterruptCastRequest cast, InterruptJudgeResult prediction)
    {
        (ulong clientId, string character) = _describeActor != null ? _describeActor(cast.ActorId) : (0UL, "-");
        _tracker.Predicted(cast.ActorId, clientId, character, cast.ViaDash, cast.PressTime, prediction, Now(),
            DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
    }

    void HandleOwnerJudged(ulong actorId, InterruptJudgeResult result) => _tracker.Judged(actorId, result, Now());

    void HandleOwnerJudgeAbandoned(ulong actorId, InterruptOwnerJudgeAbandon kind, SkillEndReason reason)
    {
        switch (kind)
        {
            case InterruptOwnerJudgeAbandon.SkillCut:
                _tracker.Abandoned(actorId, InterruptQaJudge.Cut, reason.ToString());
                break;
            case InterruptOwnerJudgeAbandon.CastRejected:
                _tracker.Abandoned(actorId, InterruptQaJudge.CastRejected, "서버 시전 거부");
                break;
            default:
                _tracker.Abandoned(actorId, InterruptQaJudge.Dropped, "이력 밖");
                break;
        }
    }

    void HandleApproval(InterruptClaimApproval approval) => _tracker.Approved(approval);

    void HandleRejection(InterruptClaimRejection rejection) => _tracker.Rejected(rejection);

    void HandleServerAttempt(InterruptCastRequest cast, InterruptJudgeResult prediction) =>
        _tracker.ServerAttemptPredicted(cast.ActorId, prediction.Forecast, Now());

    double Now() => _now != null ? _now() : 0.0;

    void HandleCompleted(InterruptQaRecord record)
    {
        _summary?.Add(record);
        Append(record);
        RecordCompleted?.Invoke(record);
    }

    void Append(InterruptQaRecord record)
    {
        if (!_writeFile || _fileFailed || SessionFolder == null)
            return;

        try
        {
            if (!File.Exists(RecordsPath))
            {
                Directory.CreateDirectory(SessionFolder);
                File.WriteAllText(RecordsPath, InterruptQaCsv.Header + InterruptQaCsv.NewLine, Utf8Bom);
                Debug.Log($"{Tag} 기록 시작 — {RecordsPath}");
            }

            File.AppendAllText(RecordsPath, InterruptQaCsv.FormatRow(record) + InterruptQaCsv.NewLine, Utf8NoBom);
        }
        catch (Exception e)
        {
            // 한 번 실패하면 이 세션은 파일 기록을 멈춘다(매 간파 경고 폭주 방지). 이벤트·집계는 계속.
            _fileFailed = true;
            Debug.LogWarning($"{Tag} 기록 실패 — 이 세션 파일 기록 중단({RecordsPath}): {e.Message}");
        }
    }
}
