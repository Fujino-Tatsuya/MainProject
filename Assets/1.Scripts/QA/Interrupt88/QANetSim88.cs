using System.Collections.Generic;
using System.IO;
using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
using UnityEngine;

/// <summary>
/// 할 일 88 — 이 인스턴스의 Unity Transport 네트워크 시뮬레이터 지연을 QA 파일로 제어한다(QA 전용, 게임 코드 무수정).
/// 파일 = 이 인스턴스 프로젝트 루트(MPPM 클론은 <c>Library/VP/mppm*/</c>)의 <see cref="FileName"/>, 내용 = 정수 ms 한 줄.
/// 메인 에디터(호스트)는 QA 창(Tools/QA) ▸ 88 ▸ NetSim 버튼(<c>QAScenario88Menu.SetRemoteDelay</c>)가 <b>클론 루트에만</b> 쓰므로
/// 원격 클라만 지연된다. 파일이 없으면 0. Play 중에도 <see cref="PollSeconds"/> 마다 다시 읽어 바로 바꾼다.
/// 적용 = Multiplayer Tools <see cref="NetworkSimulator"/> 프리셋(<c>UnityTransport.SetDebugSimulatorParameters</c> 는 NGO 2.x 에서 효과 없음).
/// 시뮬레이터 단계는 송신·수신 모두에 걸린다(ApplyMode.AllPackets) — 지연 X ms 면 이 클라의 RTT 가 대략 2X 늘어난다.
/// 에디터·Development Build 에서만 실제로 지연된다(<c>UNITY_MP_TOOLS_NETSIM_IMPLEMENTATION_ENABLED</c>).
/// 부착 = <c>QAAutoBootstrap</c> 하네스. 바뀔 때마다 <c>[QA88] event=netsim</c> 한 줄을 남긴다.
/// </summary>
public sealed class QANetSim88 : MonoBehaviour
{
    public const string FileName = "qa-netsim88.txt";
    public const float PollSeconds = 0.5f;

    private NetworkSimulator _sim;
    private float _nextPoll;

    /// <summary>지금 이 인스턴스에 적용된 지연(ms, 송수신 각각). 0 = 없음.</summary>
    public static int AppliedDelayMs { get; private set; }

    public static string FilePathFor(string projectRoot) => Path.Combine(projectRoot, FileName);

    private static string OwnFilePath => FilePathFor(Path.GetDirectoryName(Application.dataPath));

    /// <summary>이 인스턴스 프로젝트 루트(MPPM 클론이면 <c>Library/VP/mppm*</c>).</summary>
    public static string OwnRoot => Path.GetDirectoryName(Application.dataPath);

    /// <summary>
    /// 메인 에디터 기준 MPPM 클론 루트 목록(이름순). 클론에서 부르면 비어 있다.
    /// 에디터 메뉴(<c>QAScenario88Menu</c>)와 Play 중 호스트 스위트(<c>QAInterruptSuite88</c>)가 같이 쓴다.
    /// </summary>
    public static List<string> CloneRoots()
    {
        var roots = new List<string>();
        string vp = Path.Combine(OwnRoot, "Library", "VP");
        if (Directory.Exists(vp))
            roots.AddRange(Directory.GetDirectories(vp, "mppm*"));
        roots.Sort(System.StringComparer.Ordinal);
        return roots;
    }

    /// <summary>MPPM 클론 전부에 원격 지연(ms, 송수신 각각)을 건다. 0 이하면 원복. 쓴 클론 수.</summary>
    public static int SetRemoteDelayFiles(int delayMs)
    {
        if (delayMs <= 0)
            return ResetRemoteDelayFiles();
        delayMs = Mathf.Min(delayMs, QAInterruptLag88Plan.MaxDelayMs);
        int n = 0;
        foreach (string root in CloneRoots())
        {
            File.WriteAllText(FilePathFor(root), delayMs.ToString());
            n++;
        }
        return n;
    }

    /// <summary>원격 지연 원복(0) — 클론의 지연 파일을 지운다. 지운 수.</summary>
    public static int ResetRemoteDelayFiles()
    {
        int n = 0;
        foreach (string root in CloneRoots())
        {
            string path = FilePathFor(root);
            if (!File.Exists(path))
                continue;
            File.Delete(path);
            n++;
        }
        return n;
    }

    /// <summary>파일에서 읽을 지연. 파일이 없거나 형식이 틀리면 0.</summary>
    public static int ReadRequestedDelayMs()
    {
        try
        {
            string path = OwnFilePath;
            return File.Exists(path) && QAInterruptLag88Plan.TryParseDelayMs(File.ReadAllText(path), out int ms) ? ms : 0;
        }
        catch (IOException)
        {
            return AppliedDelayMs; // 메인이 쓰는 중 — 다음 폴링에 다시 읽는다
        }
    }

    private void OnEnable()
    {
        AppliedDelayMs = 0;
        _nextPoll = 0f;
    }

    private void Update()
    {
        if (Time.realtimeSinceStartup < _nextPoll)
            return;
        _nextPoll = Time.realtimeSinceStartup + PollSeconds;

        int requested = ReadRequestedDelayMs();
        if (requested != AppliedDelayMs)
            Apply(requested);
    }

    /// <summary>이 인스턴스에 지연을 건다(0 = 원복). 파일 없이 직접 부를 때도 쓴다 — 다음 폴링에서 파일 값으로 되돌아간다.</summary>
    public void Apply(int delayMs)
    {
        delayMs = Mathf.Clamp(delayMs, 0, QAInterruptLag88Plan.MaxDelayMs);
        if (_sim == null && delayMs == 0)
        {
            AppliedDelayMs = 0;
            return;
        }
        if (_sim == null)
            _sim = gameObject.AddComponent<NetworkSimulator>();
        _sim.ConnectionPreset = NetworkSimulatorPreset.Create($"QA88 {delayMs}ms", "할 일 88 간파 지연 QA", delayMs);
        AppliedDelayMs = delayMs;
        Debug.Log($"[QA88] event=netsim delayMs={delayMs} available={_sim.IsAvailable} file={QAInterruptLag88Plan.LogToken(OwnFilePath)}");
    }

    private void OnDisable()
    {
        // NetworkSimulator.OnDisable 이 트랜스포트 파라미터를 None 으로 되돌린다 — 여기서는 상태만 맞춘다.
        AppliedDelayMs = 0;
    }
}
