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
/// </list>
/// 비밀번호는 환경변수를 권한다(로컬 파일도 gitignore 경로지만 평문이다).
/// </summary>
public sealed class QANightlyLocalSettings
{
    public const string RelativePath = "UserSettings/QANightly.local.json";
    public const int DefaultObsPort = 4455;

    public bool ObsEnabled;
    public string ObsHost = "127.0.0.1";
    public int ObsPort = DefaultObsPort;
    public string ObsPassword = "";
    public string ShareRoot = "";
    /// <summary>값 출처 설명(비밀번호 값은 넣지 않는다).</summary>
    public string Source = "기본값";

    [Serializable]
    private sealed class Json
    {
        public bool obsEnabled;
        public string obsHost = "127.0.0.1";
        public int obsPort = DefaultObsPort;
        public string obsPassword = "";
        public string shareRoot = "";
    }

    /// <summary>로컬 파일 서식(새로 만들 때) — 비밀번호 칸은 비워 둔다.</summary>
    public const string Template =
        "{\n  \"obsEnabled\": false,\n  \"obsHost\": \"127.0.0.1\",\n  \"obsPort\": 4455,\n  \"obsPassword\": \"\",\n  \"shareRoot\": \"\"\n}\n";

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
            s.ObsEnabled = v.Trim() == "1" || string.Equals(v.Trim(), "true", StringComparison.OrdinalIgnoreCase);
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
        if (used.Length > 0)
            s.Source += " + 환경변수" + used;
        return s;
    }

    /// <summary>비밀번호 없이 한 줄.</summary>
    public string Describe() =>
        $"녹화 {(ObsEnabled ? "켜짐" : "꺼짐")} · OBS {QAObsProtocol.Endpoint(ObsHost, ObsPort)} · 비밀번호 {(ObsPassword.Length > 0 ? "있음" : "없음")} · " +
        $"공유 루트 {(ShareRoot.Length > 0 ? ShareRoot : "(없음 — 복사 안 함)")} · 출처 {Source}";
}
#endif
