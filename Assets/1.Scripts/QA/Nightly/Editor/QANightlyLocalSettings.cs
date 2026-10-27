#if UNITY_EDITOR
using System;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 할 일 108 — PC마다 다른 야간 QA 설정(녹화·공유 폴더). 레포에 커밋하지 않는다:
/// 파일 <c>UserSettings/QANightly.local.json</c>(gitignore 됨) → 환경변수가 덮어쓴다(프로세스 → 사용자 범위 순).
/// <list type="bullet">
/// <item><c>QA_NIGHTLY_OBS</c> = 1/0 — 녹화 켜기(기본 꺼짐). <c>QA_OBS_HOST</c>·<c>QA_OBS_PORT</c>·<c>QA_OBS_PASSWORD</c>.</item>
/// <item><c>QA_NIGHTLY_SHARE_ROOT</c> — 날짜별 공유 폴더 루트(비면 복사 안 함).</item>
/// <item>할 일 120 1단계 — 저화질: <c>QA_OBS_VIDEO_WIDTH</c>·<c>QA_OBS_VIDEO_HEIGHT</c>(출력 해상도)·<c>QA_OBS_VIDEO_FPS</c>·<c>QA_OBS_BITRATE_KBPS</c>
///   (0·비움 = OBS 설정 그대로). 영상 보관: <c>QA_NIGHTLY_VIDEO_KEEP_DAYS</c>(0·비움 = 지우지 않음).</item>
/// <item>할 일 120 2단계 — <c>QA_NIGHTLY_REPLAY</c> = 1 이면 판 전체 녹화 대신 리플레이 버퍼 클립만(기본 꺼짐). <c>QA_OBS_REPLAY_SECONDS</c>(버퍼 길이)·
///   <c>QA_OBS_REPLAY_AFTER</c>(이벤트 뒤 기다릴 초)·<c>QA_OBS_REPLAY_COOLDOWN</c>(클립 사이 최소 간격)·<c>QA_NIGHTLY_CLIP_EVENTS</c>(쉼표 목록, 비움 = 표 전체).</item>
/// </list>
/// 비밀번호는 환경변수를 권한다(로컬 파일도 gitignore 경로지만 평문이다).
/// </summary>
public sealed class QANightlyLocalSettings
{
    public const string RelativePath = "UserSettings/QANightly.local.json";
    public const int DefaultObsPort = 4455;
    public const float DefaultReplayAfterSeconds = 10f;
    public const float DefaultReplayCooldownSeconds = 30f;

    /// <summary>batch.txt <c>record</c> 값.</summary>
    public const string RecordOff = "off";
    public const string RecordFull = "obs";
    public const string RecordReplay = "replay";

    public bool ObsEnabled;
    public string ObsHost = "127.0.0.1";
    public int ObsPort = DefaultObsPort;
    public string ObsPassword = "";
    public string ShareRoot = "";

    /// <summary>2단계 스위치 — 판 전체 녹화 대신 리플레이 버퍼 클립만.</summary>
    public bool ReplayOnly;
    /// <summary>OBS 출력(스케일) 해상도. 둘 다 0 보다 커야 적용.</summary>
    public int VideoWidth;
    public int VideoHeight;
    public int VideoFps;
    /// <summary>녹화 비트레이트(kbps). 0 = 그대로.</summary>
    public int BitrateKbps;
    /// <summary>비트레이트 프로필 파라미터 <c>카테고리/키</c> 직접 지정(비움 = 출력 모드로 자동 — <see cref="QAObsProfileParams.TryBitrate"/>).</summary>
    public string BitrateParam = "";
    /// <summary>공유 폴더 날짜별 영상 보관 일수. 0 = 지우지 않음.</summary>
    public int VideoKeepDays;
    /// <summary>리플레이 버퍼 길이(초). 0 = OBS 설정 그대로.</summary>
    public int ReplaySeconds;
    public double ReplayAfterSeconds = DefaultReplayAfterSeconds;
    public double ReplayCooldownSeconds = DefaultReplayCooldownSeconds;
    /// <summary>클립을 만들 이벤트 이름(쉼표). 비움 = 이벤트 표(<see cref="QANightlyEvents.Table"/>) 전체.</summary>
    public string ClipEvents = "";

    /// <summary>값 출처 설명(비밀번호 값은 넣지 않는다).</summary>
    public string Source = "기본값";

    /// <summary>batch.txt <c>record</c> — 꺼짐·판 전체 녹화·리플레이 버퍼.</summary>
    public string RecordMode => !ObsEnabled ? RecordOff : ReplayOnly ? RecordReplay : RecordFull;

    [Serializable]
    private sealed class Json
    {
        public bool obsEnabled;
        public string obsHost = "127.0.0.1";
        public int obsPort = DefaultObsPort;
        public string obsPassword = "";
        public string shareRoot = "";
        public bool replayOnly;
        public int videoWidth;
        public int videoHeight;
        public int videoFps;
        public int videoBitrateKbps;
        public string obsBitrateParam = "";
        public int videoKeepDays;
        public int replaySeconds;
        public float replayAfterSeconds = DefaultReplayAfterSeconds;
        public float replayCooldownSeconds = DefaultReplayCooldownSeconds;
        public string clipEvents = "";
    }

    /// <summary>로컬 파일 서식(새로 만들 때) — 비밀번호 칸은 비워 둔다. 0·빈 값 = 바꾸지 않음.</summary>
    public const string Template =
        "{\n  \"obsEnabled\": false,\n  \"obsHost\": \"127.0.0.1\",\n  \"obsPort\": 4455,\n  \"obsPassword\": \"\",\n  \"shareRoot\": \"\",\n" +
        "  \"videoWidth\": 0,\n  \"videoHeight\": 0,\n  \"videoFps\": 0,\n  \"videoBitrateKbps\": 0,\n  \"obsBitrateParam\": \"\",\n  \"videoKeepDays\": 0,\n" +
        "  \"replayOnly\": false,\n  \"replaySeconds\": 0,\n  \"replayAfterSeconds\": 10,\n  \"replayCooldownSeconds\": 30,\n  \"clipEvents\": \"\"\n}\n";

    /// <summary>파일 내용(없으면 null) + 환경변수 읽기(null 반환 = 없음) → 설정. 순수.</summary>
    public static QANightlyLocalSettings Merge(string fileJson, Func<string, string> env)
    {
        var s = new QANightlyLocalSettings();
        if (!string.IsNullOrWhiteSpace(fileJson))
        {
            try
            {
                var j = new Json();
                JsonUtility.FromJsonOverwrite(fileJson, j);
                s.ObsEnabled = j.obsEnabled;
                s.ObsHost = string.IsNullOrWhiteSpace(j.obsHost) ? s.ObsHost : j.obsHost.Trim();
                s.ObsPort = j.obsPort > 0 && j.obsPort < 65536 ? j.obsPort : DefaultObsPort;
                s.ObsPassword = j.obsPassword ?? "";
                s.ShareRoot = (j.shareRoot ?? "").Trim();
                s.ReplayOnly = j.replayOnly;
                s.VideoWidth = Math.Max(0, j.videoWidth);
                s.VideoHeight = Math.Max(0, j.videoHeight);
                s.VideoFps = Math.Max(0, j.videoFps);
                s.BitrateKbps = Math.Max(0, j.videoBitrateKbps);
                s.BitrateParam = (j.obsBitrateParam ?? "").Trim();
                s.VideoKeepDays = Math.Max(0, j.videoKeepDays);
                s.ReplaySeconds = Math.Max(0, j.replaySeconds);
                s.ReplayAfterSeconds = Math.Max(0.0, j.replayAfterSeconds);
                s.ReplayCooldownSeconds = Math.Max(0.0, j.replayCooldownSeconds);
                s.ClipEvents = (j.clipEvents ?? "").Trim();
                s.Source = RelativePath;
            }
            catch (Exception e)
            {
                s.Source = $"{RelativePath} 읽기 실패({e.Message}) — 기본값";
            }
        }
        if (env == null)
            return s;
        string used = "";
        string v;
        if (!string.IsNullOrEmpty(v = env("QA_NIGHTLY_OBS")))
        {
            s.ObsEnabled = IsOn(v);
            used += " QA_NIGHTLY_OBS";
        }
        if (!string.IsNullOrEmpty(v = env("QA_OBS_HOST")))
        {
            s.ObsHost = v.Trim();
            used += " QA_OBS_HOST";
        }
        if (!string.IsNullOrEmpty(v = env("QA_OBS_PORT")) && int.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) && port > 0 && port < 65536)
        {
            s.ObsPort = port;
            used += " QA_OBS_PORT";
        }
        if (!string.IsNullOrEmpty(v = env("QA_OBS_PASSWORD")))
        {
            s.ObsPassword = v;
            used += " QA_OBS_PASSWORD";
        }
        if (!string.IsNullOrEmpty(v = env("QA_NIGHTLY_SHARE_ROOT")))
        {
            s.ShareRoot = v.Trim();
            used += " QA_NIGHTLY_SHARE_ROOT";
        }
        if (!string.IsNullOrEmpty(v = env("QA_NIGHTLY_REPLAY")))
        {
            s.ReplayOnly = IsOn(v);
            used += " QA_NIGHTLY_REPLAY";
        }
        EnvInt(env, "QA_OBS_VIDEO_WIDTH", ref s.VideoWidth, ref used);
        EnvInt(env, "QA_OBS_VIDEO_HEIGHT", ref s.VideoHeight, ref used);
        EnvInt(env, "QA_OBS_VIDEO_FPS", ref s.VideoFps, ref used);
        EnvInt(env, "QA_OBS_BITRATE_KBPS", ref s.BitrateKbps, ref used);
        EnvInt(env, "QA_NIGHTLY_VIDEO_KEEP_DAYS", ref s.VideoKeepDays, ref used);
        EnvInt(env, "QA_OBS_REPLAY_SECONDS", ref s.ReplaySeconds, ref used);
        EnvSeconds(env, "QA_OBS_REPLAY_AFTER", ref s.ReplayAfterSeconds, ref used);
        EnvSeconds(env, "QA_OBS_REPLAY_COOLDOWN", ref s.ReplayCooldownSeconds, ref used);
        if (!string.IsNullOrEmpty(v = env("QA_OBS_BITRATE_PARAM")))
        {
            s.BitrateParam = v.Trim();
            used += " QA_OBS_BITRATE_PARAM";
        }
        if (!string.IsNullOrEmpty(v = env("QA_NIGHTLY_CLIP_EVENTS")))
        {
            s.ClipEvents = v.Trim();
            used += " QA_NIGHTLY_CLIP_EVENTS";
        }
        if (used.Length > 0)
            s.Source += " + 환경변수" + used;
        return s;
    }

    /// <summary>바꿀 화질 값이 하나라도 있나(없으면 OBS 화질 설정을 건드리지 않는다).</summary>
    public bool HasQualitySettings => (VideoWidth > 0 && VideoHeight > 0) || VideoFps > 0 || BitrateKbps > 0;

    /// <summary>비밀번호 없이 한 줄.</summary>
    public string Describe() =>
        $"녹화 {(RecordMode == RecordOff ? "꺼짐" : RecordMode == RecordReplay ? "리플레이 버퍼(클립만)" : "켜짐(판 전체)")} · OBS {QAObsProtocol.Endpoint(ObsHost, ObsPort)} · " +
        $"비밀번호 {(ObsPassword.Length > 0 ? "있음" : "없음")} · 화질 {QualityText()} · 영상 보관 {(VideoKeepDays > 0 ? VideoKeepDays + "일" : "안 지움")} · " +
        (ReplayOnly ? $"리플레이 버퍼 {(ReplaySeconds > 0 ? ReplaySeconds + "초" : "OBS 설정")} · 이벤트 뒤 {ReplayAfterSeconds:0.#}초 · 쿨다운 {ReplayCooldownSeconds:0.#}초 · " +
                      $"클립 이벤트 {(ClipEvents.Length > 0 ? ClipEvents : "표 전체")} · " : "") +
        $"공유 루트 {(ShareRoot.Length > 0 ? ShareRoot : "(없음 — 복사 안 함)")} · 출처 {Source}";

    private string QualityText()
    {
        if (!HasQualitySettings)
            return "OBS 설정 그대로";
        string res = VideoWidth > 0 && VideoHeight > 0 ? $"{VideoWidth}x{VideoHeight}" : "해상도 그대로";
        string fps = VideoFps > 0 ? $"{VideoFps}fps" : "fps 그대로";
        string kbps = BitrateKbps > 0 ? $"{BitrateKbps}kbps" : "비트레이트 그대로";
        return $"{res}/{fps}/{kbps}";
    }

    private static bool IsOn(string v) => v.Trim() == "1" || string.Equals(v.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    private static void EnvInt(Func<string, string> env, string name, ref int field, ref string used)
    {
        string v = env(name);
        if (!string.IsNullOrEmpty(v) && int.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 0)
        {
            field = n;
            used += " " + name;
        }
    }

    private static void EnvSeconds(Func<string, string> env, string name, ref double field, ref string used)
    {
        string v = env(name);
        if (!string.IsNullOrEmpty(v) && double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && n >= 0.0 && !double.IsInfinity(n))
        {
            field = n;
            used += " " + name;
        }
    }
}
#endif
