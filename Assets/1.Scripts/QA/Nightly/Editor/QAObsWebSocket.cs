#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 할 일 108 — OBS Studio 내장 WebSocket 서버(obs-websocket v5, OBS 28+) 프로토콜의 순수 부분(EditMode <c>QANightlyTests</c>).
/// 흐름: 접속 → Hello(op 0, 인증 challenge·salt) → Identify(op 1) → Identified(op 2) → Request(op 6) → RequestResponse(op 7). 이벤트 = Event(op 5).
/// 인증 = base64(sha256(base64(sha256(비밀번호 + salt)) + challenge)). 사양: Docs/tech/qa-nightly-recording-research.md.
/// 할 일 120 — 리플레이 버퍼·화질(SetVideoSettings)·프로필 파라미터 요청과 요청 데이터·응답 데이터·이벤트.
/// </summary>
public static class QAObsProtocol
{
    public const int RpcVersion = 1;
    public const string StartRecord = "StartRecord";
    public const string StopRecord = "StopRecord";
    public const string GetRecordStatus = "GetRecordStatus";
    public const string StartReplayBuffer = "StartReplayBuffer";
    public const string StopReplayBuffer = "StopReplayBuffer";
    public const string SaveReplayBuffer = "SaveReplayBuffer";
    public const string GetReplayBufferStatus = "GetReplayBufferStatus";
    public const string GetLastReplayBufferReplay = "GetLastReplayBufferReplay";
    public const string SetVideoSettings = "SetVideoSettings";
    public const string SetProfileParameter = "SetProfileParameter";
    public const string GetProfileParameter = "GetProfileParameter";
    /// <summary>할 일 120 후속 — 녹화 폴더(리플레이 클립도 여기 저장). obs-websocket 5.3+(OBS 30+). Set 은 출력이 켜져 있으면 다음 시작부터 먹는다 [확인 필요].</summary>
    public const string GetRecordDirectory = "GetRecordDirectory";
    public const string SetRecordDirectory = "SetRecordDirectory";
    /// <summary>이벤트 — 리플레이 저장 끝(<c>savedReplayPath</c>).</summary>
    public const string EventReplayBufferSaved = "ReplayBufferSaved";
    /// <summary>Identify <c>eventSubscriptions</c> — Outputs(1 &lt;&lt; 6: 녹화·리플레이 버퍼 상태·ReplayBufferSaved).</summary>
    public const int SubscriptionOutputs = 1 << 6;
    /// <summary>RequestStatus 코드 — 이미 녹화 중(StartRecord)·녹화 중 아님(StopRecord). 리플레이 버퍼 시작/정지도 같은 코드.</summary>
    public const int CodeOutputRunning = 500;
    public const int CodeOutputNotRunning = 501;

    [Serializable] private sealed class AuthJson { public string challenge = ""; public string salt = ""; }
    [Serializable] private sealed class HelloD { public int rpcVersion; public AuthJson authentication; }
    [Serializable] private sealed class HelloJson { public int op = -1; public HelloD d; }
    [Serializable] private sealed class StatusJson { public bool result; public int code; public string comment = ""; }
    [Serializable]
    private sealed class DataJson
    {
        public string outputPath = "";
        public bool outputActive;
        public double outputDuration;
        public string outputTimecode = "";
        public string savedReplayPath = "";
        public string parameterValue = "";
        public string recordDirectory = "";
    }
    [Serializable] private sealed class ResponseD { public string requestType = ""; public string requestId = ""; public StatusJson requestStatus; public DataJson responseData; }
    [Serializable] private sealed class ResponseJson { public int op = -1; public ResponseD d; }
    [Serializable] private sealed class EventDataJson { public string savedReplayPath = ""; }
    [Serializable] private sealed class EventD { public string eventType = ""; public EventDataJson eventData; }
    [Serializable] private sealed class EventJson { public int op = -1; public EventD d; }
    [Serializable] private sealed class OpOnly { public int op = -1; }

    public static string Auth(string password, string salt, string challenge)
    {
        using (SHA256 sha = SHA256.Create())
        {
            string secret = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes((password ?? "") + (salt ?? ""))));
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(secret + (challenge ?? ""))));
        }
    }

    public static int OpOf(string json)
    {
        try { return JsonUtility.FromJson<OpOnly>(json)?.op ?? -1; }
        catch { return -1; }
    }

    /// <summary>Hello(op 0). <paramref name="authRequired"/> = 서버가 비밀번호를 요구한다(challenge·salt 가 있다).</summary>
    public static bool TryParseHello(string json, out int rpcVersion, out bool authRequired, out string challenge, out string salt)
    {
        rpcVersion = 0;
        authRequired = false;
        challenge = salt = "";
        HelloJson h;
        try { h = JsonUtility.FromJson<HelloJson>(json); }
        catch { return false; }
        if (h == null || h.op != 0 || h.d == null)
            return false;
        rpcVersion = h.d.rpcVersion;
        if (h.d.authentication != null && !string.IsNullOrEmpty(h.d.authentication.challenge))
        {
            authRequired = true;
            challenge = h.d.authentication.challenge;
            salt = h.d.authentication.salt ?? "";
        }
        return true;
    }

    /// <summary>Identify(op 1). <paramref name="auth"/> 가 비면 authentication 칸을 넣지 않는다. 이벤트 구독 기본 0(요청만 쓴다).</summary>
    public static string IdentifyJson(string auth, int eventSubscriptions = 0)
    {
        string a = string.IsNullOrEmpty(auth) ? "" : ",\"authentication\":\"" + Escape(auth) + "\"";
        return "{\"op\":1,\"d\":{\"rpcVersion\":" + RpcVersion + a + ",\"eventSubscriptions\":" + eventSubscriptions.ToString(CultureInfo.InvariantCulture) + "}}";
    }

    /// <summary>Request(op 6). <paramref name="requestData"/> = JSON 객체(비면 requestData 칸 없음).</summary>
    public static string RequestJson(string requestType, string requestId, string requestData = null) =>
        "{\"op\":6,\"d\":{\"requestType\":\"" + Escape(requestType) + "\",\"requestId\":\"" + Escape(requestId) + "\"" +
        (string.IsNullOrEmpty(requestData) ? "" : ",\"requestData\":" + requestData) + "}}";

    /// <summary>SetVideoSettings 요청 데이터 — 출력(스케일) 해상도·fps 만(기본 캔버스는 그대로). 짝이 안 맞거나 0 인 값은 넣지 않는다. 바꿀 것이 없으면 null.</summary>
    public static string VideoSettingsData(int outputWidth, int outputHeight, int fps)
    {
        var parts = new List<string>();
        if (outputWidth > 0 && outputHeight > 0)
            parts.Add("\"outputWidth\":" + I(outputWidth) + ",\"outputHeight\":" + I(outputHeight));
        if (fps > 0)
            parts.Add("\"fpsNumerator\":" + I(fps) + ",\"fpsDenominator\":1");
        return parts.Count == 0 ? null : "{" + string.Join(",", parts) + "}";
    }

    /// <summary>SetProfileParameter 요청 데이터(값은 문자열 — OBS 프로필 ini 값).</summary>
    public static string ProfileParameterData(string category, string name, string value) =>
        "{\"parameterCategory\":\"" + Escape(category) + "\",\"parameterName\":\"" + Escape(name) + "\",\"parameterValue\":\"" + Escape(value) + "\"}";

    /// <summary>SetRecordDirectory 요청 데이터.</summary>
    public static string RecordDirectoryData(string directory) => "{\"recordDirectory\":\"" + Escape(directory) + "\"}";

    /// <summary>GetProfileParameter 요청 데이터.</summary>
    public static string ProfileParameterQuery(string category, string name) =>
        "{\"parameterCategory\":\"" + Escape(category) + "\",\"parameterName\":\"" + Escape(name) + "\"}";

    /// <summary>RequestResponse(op 7) 중 그 requestId. outputPath = StopRecord 응답의 영상 경로(없으면 빈 값).</summary>
    public static bool TryParseResponse(string json, string requestId, out bool ok, out int code, out string comment, out string outputPath, out bool outputActive)
    {
        bool parsed = TryParseResponse(json, requestId, out QAObsResult r);
        ok = r.Ok;
        code = r.Code;
        comment = r.Error ?? "";
        outputPath = r.OutputPath ?? "";
        outputActive = r.OutputActive;
        return parsed;
    }

    /// <summary>
    /// RequestResponse(op 7) 중 그 requestId → 결과 칸 전부. <see cref="QAObsResult.Ok"/> = requestStatus.result 그대로(500/501 해석은 클라이언트),
    /// <see cref="QAObsResult.Error"/> = comment.
    /// </summary>
    public static bool TryParseResponse(string json, string requestId, out QAObsResult result)
    {
        result = new QAObsResult { OutputPath = "", OutputTimecode = "", SavedReplayPath = "", ParameterValue = "", RecordDirectory = "", Error = "" };
        ResponseJson r;
        try { r = JsonUtility.FromJson<ResponseJson>(json); }
        catch { return false; }
        if (r == null || r.op != 7 || r.d == null || r.d.requestId != requestId)
            return false;
        if (r.d.requestStatus != null)
        {
            result.Ok = r.d.requestStatus.result;
            result.Code = r.d.requestStatus.code;
            result.Error = r.d.requestStatus.comment ?? "";
        }
        if (r.d.responseData != null)
        {
            result.OutputPath = r.d.responseData.outputPath ?? "";
            result.OutputActive = r.d.responseData.outputActive;
            result.OutputDurationMs = r.d.responseData.outputDuration;
            result.OutputTimecode = r.d.responseData.outputTimecode ?? "";
            result.SavedReplayPath = r.d.responseData.savedReplayPath ?? "";
            result.ParameterValue = r.d.responseData.parameterValue ?? "";
            result.RecordDirectory = r.d.responseData.recordDirectory ?? "";
        }
        return true;
    }

    /// <summary>Event(op 5) — 이벤트 이름과 (ReplayBufferSaved 면) 저장 경로.</summary>
    public static bool TryParseEvent(string json, out string eventType, out string savedReplayPath)
    {
        eventType = savedReplayPath = "";
        EventJson e;
        try { e = JsonUtility.FromJson<EventJson>(json); }
        catch { return false; }
        if (e == null || e.op != 5 || e.d == null)
            return false;
        eventType = e.d.eventType ?? "";
        savedReplayPath = e.d.eventData?.savedReplayPath ?? "";
        return true;
    }

    /// <summary>GetRecordStatus <c>outputTimecode</c>(<c>HH:MM:SS.mmm</c>) → 밀리초. 형식이 아니면 NaN.</summary>
    public static double ParseTimecodeMs(string timecode)
    {
        string[] f = (timecode ?? "").Trim().Split(':');
        if (f.Length != 3 || !int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) ||
            !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) ||
            !double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double s) || h < 0 || m < 0 || s < 0)
            return double.NaN;
        return ((h * 60.0 + m) * 60.0 + s) * 1000.0;
    }

    public static string Endpoint(string host, int port) => $"ws://{(string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim())}:{port}";

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static string Escape(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s ?? "")
        {
            if (c == '"' || c == '\\')
                sb.Append('\\').Append(c);
            else if (c < 0x20)
                sb.Append("\\u").Append(((int)c).ToString("x4"));
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}

/// <summary>
/// 할 일 120 — OBS 프로필 파라미터(프로필 <c>basic.ini</c> 의 [카테고리] 키) 이름. 출력 모드(<c>[Output] Mode</c> = Simple/Advanced)에 따라 다르다.
/// 🔶 [확인 필요] 키 이름은 obs-websocket 문서에 없고 OBS 프로필 ini 기준(작성자 지식)이다. 바꾼 값이 다음 녹화·리플레이 시작에 바로 먹는지도 확인 필요 —
/// 안 먹으면 런북 §5.2 대로 OBS 설정 창에서 손으로 프로필을 만든다.
/// </summary>
public static class QAObsProfileParams
{
    public const string OutputCategory = "Output";
    public const string OutputModeName = "Mode";
    public const string Simple = "Simple";
    public const string Advanced = "Advanced";
    public const string SimpleCategory = "SimpleOutput";
    public const string AdvancedCategory = "AdvOut";

    /// <summary>
    /// 녹화 비트레이트 키. <paramref name="overrideParam"/>(<c>카테고리/키</c>)가 있으면 그것. 아니면 단순 모드 = <c>SimpleOutput/VBitrate</c>
    /// (단순 모드 녹화 품질이 "스트림과 같음"일 때 녹화도 이 값을 쓴다 [확인 필요]). 고급 모드는 녹화 인코더 비트레이트가 프로필 ini 가 아니라
    /// 인코더 설정 파일(recordEncoder.json)에 있어 바꿀 수 없다 → false.
    /// </summary>
    public static bool TryBitrate(string outputMode, string overrideParam, out string category, out string name)
    {
        if (TrySplit(overrideParam, out category, out name))
            return true;
        if (IsSimple(outputMode))
        {
            category = SimpleCategory;
            name = "VBitrate";
            return true;
        }
        category = name = "";
        return false;
    }

    /// <summary>리플레이 버퍼 길이(초) 키 — 단순 <c>SimpleOutput/RecRBTime</c>, 고급 <c>AdvOut/RecRBTime</c>. 모드를 모르면 false.</summary>
    public static bool TryReplaySeconds(string outputMode, out string category, out string name) => TryByMode(outputMode, "RecRBTime", out category, out name);

    /// <summary>리플레이 버퍼 사용 키 — <c>…/RecRB</c> = <c>true</c>. 모드를 모르면 false.</summary>
    public static bool TryReplayEnable(string outputMode, out string category, out string name) => TryByMode(outputMode, "RecRB", out category, out name);

    private static bool TryByMode(string outputMode, string key, out string category, out string name)
    {
        category = IsSimple(outputMode) ? SimpleCategory : IsAdvanced(outputMode) ? AdvancedCategory : "";
        name = category.Length > 0 ? key : "";
        return category.Length > 0;
    }

    private static bool IsSimple(string mode) => string.Equals((mode ?? "").Trim(), Simple, StringComparison.OrdinalIgnoreCase);
    private static bool IsAdvanced(string mode) => string.Equals((mode ?? "").Trim(), Advanced, StringComparison.OrdinalIgnoreCase);

    /// <summary><c>카테고리/키</c> → 둘 다 비지 않으면 true.</summary>
    public static bool TrySplit(string param, out string category, out string name)
    {
        category = name = "";
        string p = (param ?? "").Trim();
        int slash = p.IndexOf('/');
        if (slash <= 0 || slash >= p.Length - 1)
            return false;
        category = p.Substring(0, slash).Trim();
        name = p.Substring(slash + 1).Trim();
        return category.Length > 0 && name.Length > 0;
    }
}

/// <summary>OBS 요청 결과.</summary>
public struct QAObsResult
{
    /// <summary>요청이 받아들여졌다(또는 이미 그 상태 — 500/501 은 <see cref="AlreadyInState"/>).</summary>
    public bool Ok;
    public bool AlreadyInState;
    public int Code;
    public string OutputPath;
    public bool OutputActive;
    /// <summary>GetRecordStatus — 녹화 경과(ms).</summary>
    public double OutputDurationMs;
    public string OutputTimecode;
    /// <summary>GetLastReplayBufferReplay 응답 또는 ReplayBufferSaved 이벤트의 클립 경로.</summary>
    public string SavedReplayPath;
    public string ParameterValue;
    /// <summary>GetRecordDirectory 응답의 녹화 폴더.</summary>
    public string RecordDirectory;
    public string Error;
}

/// <summary>
/// 할 일 108 — OBS WebSocket 요청 1회(접속 → 인증 → 요청 → 응답 → 닫기). 에디터 주 스레드에서 상한(기본 5초)까지만 막는다 —
/// 비동기 이어짐이 Unity 동기화 문맥에 묶이지 않게 스레드 풀(<see cref="Task.Run(Func{Task})"/>)에서 돈다. 실패는 예외 없이 <see cref="QAObsResult.Error"/>.
/// 할 일 120 — 요청 데이터·이벤트 기다리기(<c>waitEventType</c> — SaveReplayBuffer 뒤 ReplayBufferSaved).
/// </summary>
public static class QAObsClient
{
    public static QAObsResult Send(string host, int port, string password, string requestType, double timeoutSeconds = 5.0) =>
        Request(host, port, password, requestType, null, timeoutSeconds, null);

    /// <summary>
    /// 요청 1회. <paramref name="requestData"/> = JSON 객체(없으면 null). <paramref name="waitEventType"/> 가 있으면 Outputs 이벤트를 구독하고
    /// 응답 뒤 그 이벤트를 상한까지 기다린다(못 받아도 요청 결과는 그대로 — <see cref="QAObsResult.SavedReplayPath"/> 만 빈다).
    /// </summary>
    public static QAObsResult Request(string host, int port, string password, string requestType, string requestData, double timeoutSeconds, string waitEventType)
    {
        var result = Empty();
        try
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
            {
                Task<QAObsResult> t = Task.Run(() => SendAsync(host, port, password, requestType, requestData, waitEventType, cts.Token));
                if (!t.Wait(TimeSpan.FromSeconds(timeoutSeconds + 1.0)))
                {
                    cts.Cancel();
                    result.Error = $"시간 초과 {timeoutSeconds:0}초";
                    return result;
                }
                return t.Result;
            }
        }
        catch (Exception e)
        {
            Exception inner = e is AggregateException ae && ae.InnerException != null ? ae.InnerException : e;
            result.Error = $"{inner.GetType().Name}: {inner.Message}";
            return result;
        }
    }

    private static QAObsResult Empty() =>
        new QAObsResult { OutputPath = "", OutputTimecode = "", SavedReplayPath = "", ParameterValue = "", RecordDirectory = "", Error = "" };

    private static async Task<QAObsResult> SendAsync(string host, int port, string password, string requestType, string requestData, string waitEventType,
                                                     CancellationToken ct)
    {
        var result = Empty();
        using (var ws = new ClientWebSocket())
        {
            await ws.ConnectAsync(new Uri(QAObsProtocol.Endpoint(host, port)), ct).ConfigureAwait(false);
            string hello = await Receive(ws, ct).ConfigureAwait(false);
            if (!QAObsProtocol.TryParseHello(hello, out _, out bool authRequired, out string challenge, out string salt))
            {
                result.Error = "Hello(op 0) 아님 — obs-websocket v5 서버인지 확인";
                return result;
            }
            if (authRequired && string.IsNullOrEmpty(password))
            {
                result.Error = "OBS 가 비밀번호를 요구한다 — 환경변수 QA_OBS_PASSWORD 또는 로컬 설정 obsPassword";
                return result;
            }
            int subscriptions = string.IsNullOrEmpty(waitEventType) ? 0 : QAObsProtocol.SubscriptionOutputs;
            await SendText(ws, QAObsProtocol.IdentifyJson(authRequired ? QAObsProtocol.Auth(password, salt, challenge) : null, subscriptions), ct).ConfigureAwait(false);
            string identified = await Receive(ws, ct).ConfigureAwait(false);
            if (QAObsProtocol.OpOf(identified) != 2)
            {
                result.Error = ws.CloseStatus.HasValue ? $"인증 실패·연결 닫힘({ws.CloseStatus} {ws.CloseStatusDescription})" : "Identified(op 2) 아님";
                return result;
            }
            string id = Guid.NewGuid().ToString("N");
            await SendText(ws, QAObsProtocol.RequestJson(requestType, id, requestData), ct).ConfigureAwait(false);
            string eventPath = null;
            while (true)
            {
                string msg = await Receive(ws, ct).ConfigureAwait(false);
                if (msg == null)
                {
                    result.Error = "응답 전에 연결 닫힘";
                    return result;
                }
                if (eventPath == null && IsWantedEvent(msg, waitEventType, out string p))
                {
                    eventPath = p;   // 응답보다 이벤트가 먼저 와도 받는다
                    continue;
                }
                if (!QAObsProtocol.TryParseResponse(msg, id, out QAObsResult r))
                    continue;
                bool ok = r.Ok;
                result = r;
                result.AlreadyInState = r.Code == QAObsProtocol.CodeOutputRunning || r.Code == QAObsProtocol.CodeOutputNotRunning;
                result.Ok = ok || result.AlreadyInState;
                result.Error = ok ? "" : $"code {r.Code} {r.Error}".Trim();
                break;
            }
            if (!string.IsNullOrEmpty(waitEventType) && result.Ok)
            {
                try
                {
                    while (eventPath == null)
                    {
                        string msg = await Receive(ws, ct).ConfigureAwait(false);
                        if (msg == null)
                            break;
                        if (IsWantedEvent(msg, waitEventType, out string p))
                            eventPath = p;
                    }
                }
                catch (Exception)
                {
                    // 상한까지 이벤트 없음 — 요청 결과는 그대로, 경로는 호출자가 GetLastReplayBufferReplay 로 다시 묻는다.
                }
            }
            if (!string.IsNullOrEmpty(eventPath))
                result.SavedReplayPath = eventPath;
            if (ws.State == WebSocketState.Open)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct).ConfigureAwait(false); }
                catch { /* 닫기 실패는 무시 */ }
            }
            return result;
        }
    }

    private static bool IsWantedEvent(string msg, string waitEventType, out string savedReplayPath)
    {
        savedReplayPath = "";
        return !string.IsNullOrEmpty(waitEventType) && QAObsProtocol.TryParseEvent(msg, out string type, out savedReplayPath) && type == waitEventType;
    }

    private static Task SendText(ClientWebSocket ws, string text, CancellationToken ct) =>
        ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, ct);

    /// <summary>메시지 하나(조각 합침). 닫히면 null.</summary>
    private static async Task<string> Receive(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var sb = new StringBuilder();
        while (true)
        {
            WebSocketReceiveResult r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
            if (r.MessageType == WebSocketMessageType.Close)
                return null;
            sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
            if (r.EndOfMessage)
                return sb.ToString();
        }
    }
}
#endif
