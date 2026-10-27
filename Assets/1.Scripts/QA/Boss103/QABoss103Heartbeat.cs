#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 할 일 103 C6 — 판 진행 심장박동(호스트·MPPM 클론 각자). <see cref="QABoss103Batch.BeatIntervalSeconds"/> 마다
/// <see cref="QABoss103Batch.BeatPath"/> 에 <c>key=value</c>(역할·판·단계·박동 시각·Combat 진입 시각·데이터 출처)를 덮어쓴다.
/// 에디터 루프(<c>QABoss103BatchRunner</c>)가 읽어 심장박동 정지·단계 상한을 판정한다. 출력 폴더가 없으면(설정 없음) 아무것도 안 한다.
/// 데이터 출처는 런타임 실제 적용 경로 기준(<see cref="RuntimeDataSource"/>).
/// </summary>
public sealed class QABoss103Heartbeat : MonoBehaviour
{
    private const string Tag = QABoss103Plan.Tag;
    /// <summary><c>DataSourcePlayMode</c> 가 테이블 Play 진입 때 덮어쓴 값의 스냅샷을 두는 SessionState 키(에디터 어셈블리 — 키만 같이 읽는다).</summary>
    private const string PlaySnapshotKey = "MainProject.DataTable.PlaySnapshot";

    private static QABoss103Heartbeat _active;
    private static string _phase = "boot";
    private static DateTime _combatUtc;

    private QABoss103Config _cfg;
    private string _role = "";
    private string _path = "";
    private string _source = "";
    private float _next;
    private bool _failed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _active = null;
        _phase = "boot";
        _combatUtc = default;
    }

    /// <summary>시나리오 시작 — 이 인스턴스의 박동을 켠다(이미 있으면 갈아 끼운다).</summary>
    public static void Begin(QABoss103Config cfg, string role)
    {
        if (cfg == null || !cfg.HasOut)
            return;
        if (_active != null)
            Destroy(_active.gameObject);
        var go = new GameObject("[QA] Boss103Heartbeat");
        DontDestroyOnLoad(go);
        QABoss103Heartbeat hb = go.AddComponent<QABoss103Heartbeat>();
        hb._cfg = cfg;
        hb._role = string.IsNullOrEmpty(role) ? "unknown" : role;
        hb._path = QABoss103Batch.BeatPath(cfg.OutDir, cfg.Run, cfg.Retry, hb._role);
        hb._source = RuntimeDataSource();
        _active = hb;
        _phase = "lobby";
        _combatUtc = default;
        hb.Beat();
        Debug.Log($"{Tag} event=heartbeat state=on role={QABoss103Plan.Token(hb._role)} dataSource={hb._source} path={QABoss103Plan.Token(hb._path)}");
    }

    /// <summary>진행 단계(lobby·map·traits·gather·intro·combat·end) — 바뀌면 바로 박동한다.</summary>
    public static void SetPhase(string phase)
    {
        if (_phase == phase)
            return;
        _phase = phase;
        if (_active != null)
            _active.Beat();
    }

    /// <summary>Combat 진입(호스트 판정 시작·클론 관측) — 단계 combat + 진입 시각.</summary>
    public static void MarkCombat()
    {
        if (_combatUtc == default)
            _combatUtc = DateTime.UtcNow;
        SetPhase("combat");
    }

    /// <summary>
    /// 런타임 실제 적용 경로 기준 데이터 출처 — 테이블 Play 진입 때 값이 메모리에 덮어써졌는지(SessionState 스냅샷)로 본다.
    /// 에디터 툴바 설정값만 보지 않는다(<see cref="QABoss103Batch.SourceFromRuntime"/>).
    /// </summary>
    public static string RuntimeDataSource()
    {
        try
        {
            bool prefInspector = UnityEditor.EditorPrefs.GetInt(DevBootTarget.ScopedKey("DataSource"), 0) == 1;
            return QABoss103Batch.SourceFromRuntime(prefInspector, UnityEditor.SessionState.GetString(PlaySnapshotKey, string.Empty));
        }
        catch
        {
            return "na";
        }
    }

    /// <summary>[호스트] 이 판 클론 심장박동 파일의 출처(역할 → 값). 파일이 없으면 빈 목록.</summary>
    public static List<KeyValuePair<string, string>> CloneSources(QABoss103Config cfg)
    {
        var list = new List<KeyValuePair<string, string>>();
        if (cfg == null || !cfg.HasOut)
            return list;
        try
        {
            string dir = Path.Combine(cfg.OutDir, QABoss103Batch.BeatFolder);
            if (!Directory.Exists(dir))
                return list;
            string prefix = QABoss103Batch.BeatPrefix(cfg.Run, cfg.Retry);
            foreach (string path in Directory.GetFiles(dir, prefix + "*.txt"))
            {
                string role = Path.GetFileNameWithoutExtension(path).Substring(prefix.Length);
                if (role == "host")
                    continue;
                Dictionary<string, string> kv = QABoss103Batch.ParseKeyValues(ReadShared(path));
                list.Add(new KeyValuePair<string, string>(role, kv.TryGetValue("ds", out string ds) ? ds : "na"));
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{Tag} event=heartbeat_read ok=false error={QABoss103Plan.Token(e.Message)}");
        }
        return list;
    }

    /// <summary>[호스트] 이 판 데이터 출처 = 호스트 런타임 + 클론 박동(<see cref="QABoss103Batch.CombineSources"/>).</summary>
    public static string CombinedDataSource(QABoss103Config cfg) => QABoss103Batch.CombineSources(RuntimeDataSource(), CloneSources(cfg));

    public static string ReadShared(string path)
    {
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var sr = new StreamReader(fs))
            return sr.ReadToEnd();
    }

    private void Update()
    {
        if (Time.realtimeSinceStartup >= _next)
            Beat();
    }

    private void Beat()
    {
        _next = Time.realtimeSinceStartup + QABoss103Batch.BeatIntervalSeconds;
        if (_failed || _cfg == null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            string text = QABoss103Batch.FormatKeyValues(new[]
            {
                new KeyValuePair<string, string>("role", _role),
                new KeyValuePair<string, string>("run", _cfg.Run.ToString()),
                new KeyValuePair<string, string>("retry", _cfg.Retry.ToString()),
                new KeyValuePair<string, string>("phase", _phase),
                new KeyValuePair<string, string>("beat", QABoss103Batch.Ticks(DateTime.UtcNow)),
                new KeyValuePair<string, string>("combat", _combatUtc == default ? "0" : QABoss103Batch.Ticks(_combatUtc)),
                new KeyValuePair<string, string>("ds", _source),
            });
            File.WriteAllText(_path, text);
        }
        catch (Exception e)
        {
            // 한 번 실패하면 끈다(로그 폭주 방지) — 루프는 박동 정지로 판정한다.
            _failed = true;
            Debug.LogWarning($"{Tag} event=heartbeat state=fail path={QABoss103Plan.Token(_path)} error={QABoss103Plan.Token(e.Message)}");
        }
    }

    private void OnDestroy()
    {
        if (_active == this)
            _active = null;
    }
}
#endif
