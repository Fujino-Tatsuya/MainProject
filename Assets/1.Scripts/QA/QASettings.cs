#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// QA 자동화 설정(EditorPrefs 저장, 재컴파일 없이 조정). 설정 창(QASettingsWindow)과
/// 부트스트랩(QAAutoBootstrap)이 공유한다.
///
/// 배치 주의: QAAutoBootstrap이 Assembly-CSharp(에디터 패스)에 있으므로 이 파일도
/// Editor 폴더가 아닌 QA 루트에 둬 같은 어셈블리에 포함시켜야 참조된다.
/// (Assembly-CSharp은 Assembly-CSharp-Editor를 참조할 수 없음.) EditorPrefs는 에디터 전용이라 #if UNITY_EDITOR.
/// </summary>
public static class QASettings
{
    public const string AutoRunKey = "QA.AutoRunFromBootstrap";
    public const string DurationKey = "QA.SessionDuration";
    public const string RepeatKey = "QA.RepeatCount";
    public const string PlayerCountKey = "QA.PlayerCount";
    public const string ReportRootKey = "QA.ReportRoot";
    public const string ResultPathsKey = "QA.ResultPaths";
    public const string ResultPathOrderKey = "QA.ResultPathOrder";
    public const string ScenariosKey = "QA.Scenarios48";

    /// <summary>
    /// 할 일 48 시나리오 모드(쉼표 구분 id, 예: "S1,S3,S5"). 비어 있지 않으면 부트스트랩이 QASessionController·QAAgent 대신
    /// QAScenarioRunner 를 붙인다. MPPM 가상 플레이어도 같은 EditorPrefs 를 읽는다. Tools/QA/48 메뉴가 설정·해제한다.
    /// </summary>
    public static string Scenarios
    {
        get => TryReadRunFile(out string[] f) ? f[0] : EditorPrefs.GetString(ScenariosKey, string.Empty);
        set
        {
            EditorPrefs.SetString(ScenariosKey, value ?? string.Empty);
            WriteRunFile();
        }
    }

    /// <summary>결과 화면 경로 순서(쉼표 구분: Aborted,Wiped,Cleared). 비우면 기본 순서.</summary>
    public static string ResultPathOrder
    {
        get => TryReadRunFile(out string[] f) && f.Length > 2 ? f[2] : EditorPrefs.GetString(ResultPathOrderKey, string.Empty);
        set
        {
            EditorPrefs.SetString(ResultPathOrderKey, value ?? string.Empty);
            WriteRunFile();
        }
    }

    // ── 시나리오 실행 파일(할 일 48.3) ───────────────────────────
    // MPPM 클론 에디터는 EditorPrefs 를 프로세스 안에 캐시해 메인이 바꾼 시나리오 목록을 못 본다(2차에서 클론이 M2 대신 이전 값 M1,M4 를 돌림).
    // 그래서 시나리오·인원·경로 순서를 리포트 루트(모든 인스턴스가 같은 절대 경로)의 파일로도 넘긴다. 줄 = ids / 인원 / 경로 순서.

    private static string RunFilePath
    {
        get
        {
            string root = ReportRoot;
            return string.IsNullOrWhiteSpace(root) || !System.IO.Path.IsPathRooted(root.Trim())
                ? null
                : System.IO.Path.Combine(root.Trim(), ".scenario48.txt");
        }
    }

    private static bool TryReadRunFile(out string[] fields)
    {
        fields = null;
        string path = RunFilePath;
        if (path == null || !System.IO.File.Exists(path))
            return false;
        fields = System.IO.File.ReadAllText(path).Replace("\r", string.Empty).Split('\n');
        return fields.Length > 0;
    }

    private static void WriteRunFile()
    {
        string path = RunFilePath;
        if (path == null)
            return;
        string ids = EditorPrefs.GetString(ScenariosKey, string.Empty);
        if (string.IsNullOrWhiteSpace(ids))
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
            return;
        }
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        System.IO.File.WriteAllText(path, $"{ids}\n{EditorPrefs.GetInt(PlayerCountKey, 3)}\n{EditorPrefs.GetString(ResultPathOrderKey, string.Empty)}");
    }

    /// <summary>
    /// 결과 화면 경로 모드. 켜면 각 사이클을 Play 시간 뒤 결과 화면까지 보낸다
    /// (사이클 1=중도 종료, 2=전멸, 3=클리어, 이후 반복) — QAResultFlow.
    /// </summary>
    public static bool ResultPaths
    {
        get => EditorPrefs.GetBool(ResultPathsKey, false);
        set => EditorPrefs.SetBool(ResultPathsKey, value);
    }

    /// <summary>0.BootStrapScene에서 Play 시 QA 하네스를 자동 생성할지.</summary>
    public static bool AutoRun
    {
        get => EditorPrefs.GetBool(AutoRunKey, true);
        set => EditorPrefs.SetBool(AutoRunKey, value);
    }

    /// <summary>맵 도달 후 각 사이클을 돌릴 시간(초).</summary>
    public static float Duration
    {
        get => EditorPrefs.GetFloat(DurationKey, 180f);
        set => EditorPrefs.SetFloat(DurationKey, value);
    }

    /// <summary>QA 전체 사이클 반복 횟수. 0 이하이면 무한.</summary>
    public static int RepeatCount
    {
        get => EditorPrefs.GetInt(RepeatKey, 1);
        set => EditorPrefs.SetInt(RepeatKey, value);
    }

    /// <summary>멀티플레이 인원 수(호스트 포함). 3=호스트1+MPPM 가상 플레이어2. 1=단독.</summary>
    public static int PlayerCount
    {
        get => TryReadRunFile(out string[] f) && f.Length > 1 && int.TryParse(f[1], out int n) ? n : EditorPrefs.GetInt(PlayerCountKey, 3);
        set
        {
            EditorPrefs.SetInt(PlayerCountKey, value);
            WriteRunFile();
        }
    }

    /// <summary>
    /// 리포트를 저장할 시스템 절대 경로(예: D:/QALogs). 프로젝트 밖도 가능.
    /// 비어 있거나 절대 경로가 아니면 기본 경로(&lt;repo&gt;/Docs/temp/qa-runs)로 폴백한다.
    /// 실제 해석은 QAReportWriter.RunsRoot()가 담당.
    /// </summary>
    public static string ReportRoot
    {
        get => EditorPrefs.GetString(ReportRootKey, string.Empty);
        set => EditorPrefs.SetString(ReportRootKey, value ?? string.Empty);
    }
}
#endif
