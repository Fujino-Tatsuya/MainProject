#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 108 팀원별 야간 QA 메뉴(<c>Tools/QA/Nightly</c>). 한 판 = 시나리오 <see cref="QALobbyScenarios.Nightly108Id"/>(로비 점검 → 캐릭터 선택·출격 → 맵 → 보스 결과),
/// 판 반복·오류 판 재실행·리포트는 103 연속 루프(<see cref="QABoss103BatchRunner"/>)를 그대로 쓴다. 팀원별 차이 = 시드 파일(<see cref="QANightlySeed.RelativePath"/>) 하나.
/// 녹화·공유 폴더는 PC 설정(<see cref="QANightlyLocalSettings"/>). 실행 절차: Docs/tech/qa-nightly-runbook.md.
/// </summary>
public static class QANightlyMenu
{
    private const string Tag = "[QA108/Nightly]";
    private const string Root = "Tools/QA/Nightly/";

    public static string SeedPath => Path.Combine(QANetSim88.OwnRoot, QANightlySeed.RelativePath);

    [MenuItem(Root + "Start Night", priority = 0)]
    public static void StartNight()
    {
        if (QABoss103BatchRunner.IsRunning)
        {
            Debug.LogWarning($"{Tag} 연속 루프가 이미 돈다({QABoss103BatchRunner.CurrentDir}) — Status 로 확인, 멈추려면 Stop After Current Run.");
            return;
        }
        if (!TryLoadSeed(out QANightlySeed seed))
            return;
        QANightlyLocalSettings local = QANightlyHooks.LoadLocal();

        System.DateTime at = System.DateTime.Now;
        string dir = $"{QAScenario103Menu.QARoot}/{QANightlyPaths.BatchFolderName(seed.Name, at)}";
        if (Directory.Exists(dir))
            dir += at.ToString("ss", CultureInfo.InvariantCulture);
        string toolbarBefore = DataSourcePlayMode.Current.ToString();
        string shareDir = QANightlyPaths.ShareDir(local.ShareRoot, at, seed.Name) ?? "";
        List<KeyValuePair<string, string>> batch = QANightlyHeader.BatchKeys(seed, at, QABoss103Batch.SourceTable, QABoss103RunCsv.XlsxRevision(),
            QABoss103RunCsv.Shell("git", "rev-parse --abbrev-ref HEAD"), QABoss103RunCsv.GitCommit(), Application.unityVersion, local.ObsEnabled, shareDir);
        batch.Add(new KeyValuePair<string, string>("data_source_toolbar_before", toolbarBefore));
        batch.Add(new KeyValuePair<string, string>("nightly_seed_file", QANightlySeed.RelativePath));

        var state = new List<KeyValuePair<string, string>>
        {
            KV("scenario", QALobbyScenarios.Nightly108Id),
            KV("nightly", seed.Name),
            KV("tier", seed.TierText),
            KV("order", QABoss103Plan.FormatOrder(seed.Order)),
            KV("traitSeed", seed.TraitSeed.ToString(CultureInfo.InvariantCulture)),
        };
        if (!QABoss103BatchRunner.StartBatch(dir, batch, seed.BaseSeed, seed.Runs, state))
            return;
        Debug.Log($"{Tag} 야간 QA 시작 — {seed.Describe()} · {local.Describe()} · 툴바 출처 {toolbarBefore} → Table · 폴더 {dir}");
        if (local.ObsEnabled)
        {
            QAObsResult r = QAObsClient.Send(local.ObsHost, local.ObsPort, local.ObsPassword, QAObsProtocol.GetRecordStatus);
            if (!r.Ok)
                Debug.LogWarning($"{Tag} OBS 연결 확인 실패(QA 는 녹화 없이 계속 — 판마다 다시 시도) — {r.Error}");
        }
    }

    [MenuItem(Root + "Resume Last", priority = 1)]
    public static void ResumeLast() => QABoss103BatchRunner.ResumeLatest(QANightlyPaths.BatchPattern);

    [MenuItem(Root + "Stop After Current Run", priority = 2)]
    public static void StopAfterCurrent() => QABoss103BatchRunner.StopAfterCurrent();

    [MenuItem(Root + "Status", priority = 3)]
    public static void Status()
    {
        if (TryLoadSeed(out QANightlySeed seed))
            Debug.Log($"{Tag} 시드 파일 {SeedPath} — {seed.Describe()}");
        Debug.Log($"{Tag} PC 설정 — {QANightlyHooks.LoadLocal().Describe()}");
        QABoss103BatchRunner.Status();
    }

    [MenuItem(Root + "Open Report", priority = 4)]
    public static void OpenReport() => QABoss103BatchRunner.OpenReport();

    [MenuItem(Root + "Start Night", true)]
    [MenuItem(Root + "Resume Last", true)]
    private static bool CanStartCheck() => !QABoss103BatchRunner.IsRunning;

    // ── 설정 ─────────────────────────────────────────────────

    [MenuItem(Root + "Settings/Validate Seed File", priority = 40)]
    public static void ValidateSeed()
    {
        if (TryLoadSeed(out QANightlySeed seed))
            Debug.Log($"{Tag} 시드 파일 OK — {seed.Describe()}");
    }

    [MenuItem(Root + "Settings/Create Local Settings File", priority = 41)]
    public static void CreateLocal()
    {
        string path = Path.Combine(QANetSim88.OwnRoot, QANightlyLocalSettings.RelativePath);
        if (File.Exists(path))
        {
            Debug.Log($"{Tag} 이미 있다 — {path} · {QANightlyHooks.LoadLocal().Describe()}");
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, QANightlyLocalSettings.Template);
        Debug.Log($"{Tag} 로컬 설정 파일을 만들었다(gitignore 경로) — {path}");
    }

    [MenuItem(Root + "Settings/Test OBS Connection", priority = 42)]
    public static void TestObs()
    {
        QANightlyLocalSettings s = QANightlyHooks.LoadLocal();
        QAObsResult r = QAObsClient.Send(s.ObsHost, s.ObsPort, s.ObsPassword, QAObsProtocol.GetRecordStatus);
        if (r.Ok)
            Debug.Log($"{Tag} OBS 연결 OK — {QAObsProtocol.Endpoint(s.ObsHost, s.ObsPort)} · 녹화 {(s.ObsEnabled ? "켜짐(야간 판마다 녹화)" : "꺼짐(설정 obsEnabled/QA_NIGHTLY_OBS 로 켠다)")}");
        else
            Debug.LogWarning($"{Tag} OBS 연결 실패 — {QAObsProtocol.Endpoint(s.ObsHost, s.ObsPort)} {r.Error}");
    }

    // ── 도구 ─────────────────────────────────────────────────

    private static bool TryLoadSeed(out QANightlySeed seed)
    {
        seed = null;
        string path = SeedPath;
        if (!File.Exists(path))
        {
            Debug.LogError($"{Tag} 시드 파일이 없다 — {path}");
            return false;
        }
        if (!QANightlySeed.TryParse(File.ReadAllText(path), out seed, out List<string> errors))
        {
            Debug.LogError($"{Tag} 시드 파일 오류 {path} — {string.Join(" / ", errors)}");
            return false;
        }
        return true;
    }

    private static KeyValuePair<string, string> KV(string k, string v) => new KeyValuePair<string, string>(k, v);
}
#endif
