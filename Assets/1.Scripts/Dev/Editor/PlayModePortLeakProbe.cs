using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using Object = UnityEngine.Object;

/// <summary>
/// 진단 전용 (할 일 56.1). Play 종료 뒤에도 에디터가 UDP 포트를 쥐고 있는지 탐침하고,
/// 잡혀 있을 때만 종료 직전·편집 모드 진입 후 네트워크 상태 스냅샷을 경고로 남긴다. 동작은 바꾸지 않는다.
/// </summary>
[InitializeOnLoad]
public static class PlayModePortLeakProbe
{
    public const ushort DefaultPort = 7777;
    private const string ExitSnapshotKey = "PlayModePortLeakProbe.ExitSnapshot";
    private const string ExitPortsKey = "PlayModePortLeakProbe.ExitPorts";
    private const int NetstatTimeoutMs = 2000;
    private const string Unknown = "알 수 없음";

    public sealed class TransportInfo
    {
        public ushort Port;
        /// <summary>"예" / "아니오" / "알 수 없음".</summary>
        public string DriverCreated;
    }

    public sealed class Snapshot
    {
        public string Phase;
        public int Pid;
        public int NetworkManagerCount;
        public bool SingletonPresent;
        public bool IsListening;
        public bool IsServer;
        public bool IsClient;
        public bool ShutdownInProgress;
        public List<TransportInfo> Transports = new List<TransportInfo>();
    }

    static PlayModePortLeakProbe()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode)
        {
            Snapshot exit = Capture("ExitingPlayMode");
            // 도메인 리로드가 끼어도 남도록 SessionState 에 문자열로 보관한다.
            SessionState.SetString(ExitSnapshotKey, FormatSnapshot(exit));
            SessionState.SetString(ExitPortsKey, FormatPortsCsv(exit.Transports.Select(t => t.Port)));
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            // 한두 프레임 뒤에 탐침한다.
            EditorApplication.delayCall += () => EditorApplication.delayCall += ProbeAfterExit;
        }
    }

    private static void ProbeAfterExit()
    {
        string exitText = SessionState.GetString(ExitSnapshotKey, string.Empty);
        string exitPortsCsv = SessionState.GetString(ExitPortsKey, string.Empty);
        SessionState.EraseString(ExitSnapshotKey);
        SessionState.EraseString(ExitPortsKey);

        try
        {
            Snapshot edit = Capture("EnteredEditMode");
            List<ushort> targets = BuildProbePorts(
                ParsePortsCsv(exitPortsCsv).Concat(edit.Transports.Select(t => t.Port)));

            List<ushort> occupied = targets.Where(DevSceneBooter.IsUdpPortInUse).ToList();
            if (occupied.Count == 0)
                return;

            string netstat = RunNetstat();
            var owners = new List<string>();
            foreach (ushort port in occupied)
            {
                List<int> pids = netstat == null ? null : ParseNetstatUdpOwners(netstat, port);
                owners.Add($"{port}: {DescribeOwner(pids, edit.Pid, LookupProcessName)}");
            }

            UnityEngine.Debug.LogWarning(BuildWarning(occupied, owners, FormatSnapshot(edit), exitText));
        }
        catch (Exception ex)
        {
            // 진단 실패가 편집 흐름을 방해하지 않게 한다.
            UnityEngine.Debug.LogWarning($"[PortLeak] 진단 실패: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static Snapshot Capture(string phase)
    {
        var snapshot = new Snapshot
        {
            Phase = phase,
            Pid = Process.GetCurrentProcess().Id,
        };

        NetworkManager[] managers = Object.FindObjectsByType<NetworkManager>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None);
        snapshot.NetworkManagerCount = managers.Length;

        NetworkManager singleton = NetworkManager.Singleton;
        snapshot.SingletonPresent = singleton != null;
        if (singleton != null)
        {
            snapshot.IsListening = singleton.IsListening;
            snapshot.IsServer = singleton.IsServer;
            snapshot.IsClient = singleton.IsClient;
            snapshot.ShutdownInProgress = singleton.ShutdownInProgress;
        }

        UnityTransport[] transports = Object.FindObjectsByType<UnityTransport>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None);
        foreach (UnityTransport transport in transports)
        {
            snapshot.Transports.Add(new TransportInfo
            {
                Port = transport.ConnectionData.Port,
                DriverCreated = ReadDriverCreated(transport),
            });
        }

        return snapshot;
    }

    /// <summary>비공개 m_Driver.IsCreated 를 리플렉션으로 읽는다. 실패하면 "알 수 없음".</summary>
    private static string ReadDriverCreated(UnityTransport transport)
    {
        try
        {
            FieldInfo field = typeof(UnityTransport).GetField("m_Driver", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            object driver = field?.GetValue(transport);
            PropertyInfo isCreated = driver?.GetType().GetProperty("IsCreated", BindingFlags.Instance | BindingFlags.Public);
            if (isCreated?.GetValue(driver) is bool created)
                return created ? "예" : "아니오";
        }
        catch (Exception)
        {
            // best-effort
        }

        return Unknown;
    }

    /// <summary>netstat -ano -p UDP 를 짧은 타임아웃으로 한 번 돌린다. 실패·시간 초과면 null.</summary>
    private static string RunNetstat()
    {
        try
        {
            var info = new ProcessStartInfo("netstat", "-ano -p UDP")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using (Process process = Process.Start(info))
            {
                if (process == null)
                    return null;

                var output = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(NetstatTimeoutMs))
                {
                    try { process.Kill(); } catch (Exception) { }
                    return null;
                }

                return output.Wait(NetstatTimeoutMs) ? output.Result : null;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string LookupProcessName(int pid)
    {
        try
        {
            using (Process process = Process.GetProcessById(pid))
                return process.ProcessName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ── 순수 함수 (EditMode 테스트 대상) ──

    public static string FormatSnapshot(Snapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.Append($"[{snapshot.Phase}] pid={snapshot.Pid}, NetworkManager {snapshot.NetworkManagerCount}개, Singleton=");
        if (snapshot.SingletonPresent)
        {
            sb.Append($"있음(IsListening={snapshot.IsListening}, IsServer={snapshot.IsServer}, " +
                      $"IsClient={snapshot.IsClient}, ShutdownInProgress={snapshot.ShutdownInProgress})");
        }
        else
        {
            sb.Append("null");
        }

        sb.Append($", UnityTransport {snapshot.Transports.Count}개");
        if (snapshot.Transports.Count > 0)
        {
            sb.Append(": ");
            sb.Append(string.Join(", ", snapshot.Transports.Select(t => $"포트 {t.Port}(드라이버 생성={t.DriverCreated ?? Unknown})")));
        }

        return sb.ToString();
    }

    /// <summary>7777 을 맨 앞에 두고, 나머지는 중복·0 을 빼고 오름차순으로 붙인다.</summary>
    public static List<ushort> BuildProbePorts(IEnumerable<ushort> extraPorts)
    {
        var ports = new List<ushort> { DefaultPort };
        if (extraPorts != null)
            ports.AddRange(extraPorts.Where(p => p != 0 && p != DefaultPort).Distinct().OrderBy(p => p));
        return ports;
    }

    public static string FormatPortsCsv(IEnumerable<ushort> ports)
    {
        return ports == null ? string.Empty : string.Join(",", ports.Select(p => p.ToString(CultureInfo.InvariantCulture)));
    }

    public static List<ushort> ParsePortsCsv(string csv)
    {
        var ports = new List<ushort>();
        if (string.IsNullOrEmpty(csv))
            return ports;

        foreach (string part in csv.Split(','))
        {
            if (ushort.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort port))
                ports.Add(port);
        }

        return ports;
    }

    /// <summary>netstat -ano -p UDP 출력에서 로컬 포트가 <paramref name="port"/> 인 줄의 pid 를 모은다(중복 제거).</summary>
    public static List<int> ParseNetstatUdpOwners(string netstatOutput, ushort port)
    {
        var pids = new List<int>();
        if (string.IsNullOrEmpty(netstatOutput))
            return pids;

        foreach (string line in netstatOutput.Split('\n'))
        {
            string[] tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 3 || !string.Equals(tokens[0], "UDP", StringComparison.OrdinalIgnoreCase))
                continue;

            string local = tokens[1];
            int colon = local.LastIndexOf(':');
            if (colon < 0 || !ushort.TryParse(local.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort localPort))
                continue;
            if (localPort != port)
                continue;

            if (int.TryParse(tokens[tokens.Length - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid) && !pids.Contains(pid))
                pids.Add(pid);
        }

        return pids;
    }

    /// <param name="pids">null 이면 netstat 실패.</param>
    public static string DescribeOwner(IReadOnlyList<int> pids, int selfPid, Func<int, string> nameLookup)
    {
        if (pids == null)
            return "소유 pid 알 수 없음(netstat 실패)";
        if (pids.Count == 0)
            return "netstat 에 해당 포트 없음";

        string Label(int pid)
        {
            string name = nameLookup?.Invoke(pid);
            return string.IsNullOrEmpty(name) ? $"pid {pid}" : $"pid {pid} {name}";
        }

        var parts = new List<string>();
        if (pids.Contains(selfPid))
            parts.Add($"이 에디터({Label(selfPid)})");

        List<int> others = pids.Where(p => p != selfPid).ToList();
        if (others.Count > 0)
            parts.Add($"다른 프로세스({string.Join(", ", others.Select(Label))}) — 다른 에디터 호스트면 오탐");

        return string.Join(" + ", parts);
    }

    public static string BuildWarning(IReadOnlyList<ushort> occupiedPorts, IReadOnlyList<string> ownerLines, string editSnapshot, string exitSnapshot)
    {
        var sb = new StringBuilder();
        sb.Append($"[PortLeak] Play 종료 후 포트 {string.Join(", ", occupiedPorts)} 아직 점유");
        if (ownerLines != null)
        {
            foreach (string owner in ownerLines)
                sb.Append("\n  소유: ").Append(owner);
        }

        sb.Append("\n  종료 시 스냅샷: ").Append(string.IsNullOrEmpty(exitSnapshot) ? "없음" : exitSnapshot);
        sb.Append("\n  편집 모드 스냅샷: ").Append(string.IsNullOrEmpty(editSnapshot) ? "없음" : editSnapshot);
        return sb.ToString();
    }
}
