#if UNITY_EDITOR
using System;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 할 일 108 — OBS Studio 내장 WebSocket 서버(obs-websocket v5, OBS 28+) 프로토콜의 순수 부분(EditMode <c>QANightlyTests</c>).
/// 흐름: 접속 → Hello(op 0, 인증 challenge·salt) → Identify(op 1) → Identified(op 2) → Request(op 6) → RequestResponse(op 7).
/// 인증 = base64(sha256(base64(sha256(비밀번호 + salt)) + challenge)). 사양: Docs/tech/qa-nightly-recording-research.md.
/// </summary>
public static class QAObsProtocol
{
    public const int RpcVersion = 1;
    public const string StartRecord = "StartRecord";
    public const string StopRecord = "StopRecord";
    public const string GetRecordStatus = "GetRecordStatus";
    /// <summary>RequestStatus 코드 — 이미 녹화 중(StartRecord)·녹화 중 아님(StopRecord).</summary>
    public const int CodeOutputRunning = 500;
    public const int CodeOutputNotRunning = 501;

    [Serializable] private sealed class AuthJson { public string challenge = ""; public string salt = ""; }
    [Serializable] private sealed class HelloD { public int rpcVersion; public AuthJson authentication; }
    [Serializable] private sealed class HelloJson { public int op = -1; public HelloD d; }
    [Serializable] private sealed class StatusJson { public bool result; public int code; public string comment = ""; }
    [Serializable] private sealed class DataJson { public string outputPath = ""; public bool outputActive; }
    [Serializable] private sealed class ResponseD { public string requestType = ""; public string requestId = ""; public StatusJson requestStatus; public DataJson responseData; }
    [Serializable] private sealed class ResponseJson { public int op = -1; public ResponseD d; }
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

    /// <summary>Identify(op 1). <paramref name="auth"/> 가 비면 authentication 칸을 넣지 않는다. 이벤트 구독 0(요청만 쓴다).</summary>
    public static string IdentifyJson(string auth)
    {
        string a = string.IsNullOrEmpty(auth) ? "" : ",\"authentication\":\"" + Escape(auth) + "\"";
        return "{\"op\":1,\"d\":{\"rpcVersion\":" + RpcVersion + a + ",\"eventSubscriptions\":0}}";
    }

    /// <summary>Request(op 6) — 요청 데이터 없음.</summary>
    public static string RequestJson(string requestType, string requestId) =>
        "{\"op\":6,\"d\":{\"requestType\":\"" + Escape(requestType) + "\",\"requestId\":\"" + Escape(requestId) + "\"}}";

    /// <summary>RequestResponse(op 7) 중 그 requestId. outputPath = StopRecord 응답의 영상 경로(없으면 빈 값).</summary>
    public static bool TryParseResponse(string json, string requestId, out bool ok, out int code, out string comment, out string outputPath, out bool outputActive)
    {
        ok = false;
        code = 0;
        comment = outputPath = "";
        outputActive = false;
        ResponseJson r;
        try { r = JsonUtility.FromJson<ResponseJson>(json); }
        catch { return false; }
        if (r == null || r.op != 7 || r.d == null || r.d.requestId != requestId)
            return false;
        if (r.d.requestStatus != null)
        {
            ok = r.d.requestStatus.result;
            code = r.d.requestStatus.code;
            comment = r.d.requestStatus.comment ?? "";
        }
        if (r.d.responseData != null)
        {
            outputPath = r.d.responseData.outputPath ?? "";
            outputActive = r.d.responseData.outputActive;
        }
        return true;
    }

    public static string Endpoint(string host, int port) => $"ws://{(string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim())}:{port}";

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

/// <summary>OBS 요청 결과.</summary>
public struct QAObsResult
{
    /// <summary>요청이 받아들여졌다(또는 이미 그 상태 — 500/501 은 <see cref="AlreadyInState"/>).</summary>
    public bool Ok;
    public bool AlreadyInState;
    public string OutputPath;
    public string Error;
}

/// <summary>
/// 할 일 108 — OBS WebSocket 요청 1회(접속 → 인증 → 요청 → 응답 → 닫기). 에디터 주 스레드에서 상한(기본 5초)까지만 막는다 —
/// 비동기 이어짐이 Unity 동기화 문맥에 묶이지 않게 스레드 풀(<see cref="Task.Run(Func{Task})"/>)에서 돈다. 실패는 예외 없이 <see cref="QAObsResult.Error"/>.
/// </summary>
public static class QAObsClient
{
    public static QAObsResult Send(string host, int port, string password, string requestType, double timeoutSeconds = 5.0)
    {
        var result = new QAObsResult { OutputPath = "", Error = "" };
        try
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
            {
                Task<QAObsResult> t = Task.Run(() => SendAsync(host, port, password, requestType, cts.Token));
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

    private static async Task<QAObsResult> SendAsync(string host, int port, string password, string requestType, CancellationToken ct)
    {
        var result = new QAObsResult { OutputPath = "", Error = "" };
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
            await SendText(ws, QAObsProtocol.IdentifyJson(authRequired ? QAObsProtocol.Auth(password, salt, challenge) : null), ct).ConfigureAwait(false);
            string identified = await Receive(ws, ct).ConfigureAwait(false);
            if (QAObsProtocol.OpOf(identified) != 2)
            {
                result.Error = ws.CloseStatus.HasValue ? $"인증 실패·연결 닫힘({ws.CloseStatus} {ws.CloseStatusDescription})" : "Identified(op 2) 아님";
                return result;
            }
            string id = Guid.NewGuid().ToString("N");
            await SendText(ws, QAObsProtocol.RequestJson(requestType, id), ct).ConfigureAwait(false);
            while (true)
            {
                string msg = await Receive(ws, ct).ConfigureAwait(false);
                if (msg == null)
                {
                    result.Error = "응답 전에 연결 닫힘";
                    return result;
                }
                if (!QAObsProtocol.TryParseResponse(msg, id, out bool ok, out int code, out string comment, out string path, out _))
                    continue;
                result.AlreadyInState = code == QAObsProtocol.CodeOutputRunning || code == QAObsProtocol.CodeOutputNotRunning;
                result.Ok = ok || result.AlreadyInState;
                result.OutputPath = path;
                if (!ok)
                    result.Error = $"code {code} {comment}".Trim();
                break;
            }
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct).ConfigureAwait(false); }
            catch { /* 닫기 실패는 무시 */ }
            return result;
        }
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
