using System.Collections.Generic;
using NUnit.Framework;

public sealed class PlayModePortLeakProbeTests
{
    private static PlayModePortLeakProbe.Snapshot MakeSnapshot(bool singleton)
    {
        var snapshot = new PlayModePortLeakProbe.Snapshot
        {
            Phase = "ExitingPlayMode",
            Pid = 1234,
            NetworkManagerCount = 1,
            SingletonPresent = singleton,
            IsListening = true,
            IsServer = true,
            IsClient = true,
            ShutdownInProgress = false,
        };
        snapshot.Transports.Add(new PlayModePortLeakProbe.TransportInfo { Port = 7778, DriverCreated = "예" });
        return snapshot;
    }

    [Test]
    public void FormatSnapshot_WithSingleton_ListsStateAndTransports()
    {
        string text = PlayModePortLeakProbe.FormatSnapshot(MakeSnapshot(true));

        Assert.That(text, Is.EqualTo(
            "[ExitingPlayMode] pid=1234, NetworkManager 1개, Singleton=있음(IsListening=True, IsServer=True, " +
            "IsClient=True, ShutdownInProgress=False), UnityTransport 1개: 포트 7778(드라이버 생성=예)"));
    }

    [Test]
    public void FormatSnapshot_WithoutSingletonOrTransport_SaysNull()
    {
        var snapshot = new PlayModePortLeakProbe.Snapshot { Phase = "EnteredEditMode", Pid = 9 };

        Assert.That(PlayModePortLeakProbe.FormatSnapshot(snapshot),
            Is.EqualTo("[EnteredEditMode] pid=9, NetworkManager 0개, Singleton=null, UnityTransport 0개"));
    }

    [Test]
    public void FormatSnapshot_MissingDriverState_ShowsUnknown()
    {
        var snapshot = new PlayModePortLeakProbe.Snapshot { Phase = "P", Pid = 1 };
        snapshot.Transports.Add(new PlayModePortLeakProbe.TransportInfo { Port = 7777 });

        StringAssert.EndsWith("포트 7777(드라이버 생성=알 수 없음)", PlayModePortLeakProbe.FormatSnapshot(snapshot));
    }

    [Test]
    public void BuildProbePorts_Puts7777First_DedupsAndDropsZero()
    {
        List<ushort> ports = PlayModePortLeakProbe.BuildProbePorts(new ushort[] { 7779, 7777, 0, 7778, 7779 });

        Assert.That(ports, Is.EqualTo(new ushort[] { 7777, 7778, 7779 }));
    }

    [Test]
    public void BuildProbePorts_NullOrEmpty_Only7777()
    {
        Assert.That(PlayModePortLeakProbe.BuildProbePorts(null), Is.EqualTo(new ushort[] { 7777 }));
        Assert.That(PlayModePortLeakProbe.BuildProbePorts(new ushort[0]), Is.EqualTo(new ushort[] { 7777 }));
    }

    [Test]
    public void PortsCsv_RoundTrips_AndSkipsGarbage()
    {
        string csv = PlayModePortLeakProbe.FormatPortsCsv(new ushort[] { 7777, 7780 });

        Assert.That(csv, Is.EqualTo("7777,7780"));
        Assert.That(PlayModePortLeakProbe.ParsePortsCsv(csv), Is.EqualTo(new ushort[] { 7777, 7780 }));
        Assert.That(PlayModePortLeakProbe.ParsePortsCsv("7777,abc,,70000,7781"), Is.EqualTo(new ushort[] { 7777, 7781 }));
        Assert.That(PlayModePortLeakProbe.ParsePortsCsv(null), Is.Empty);
    }

    private const string NetstatSample =
        "\r\n활성 연결\r\n\r\n  프로토콜  로컬 주소              외부 주소              상태            PID\r\n" +
        "  UDP    0.0.0.0:500            *:*                                    4512\r\n" +
        "  UDP    0.0.0.0:7777           *:*                                    1234\r\n" +
        "  UDP    0.0.0.0:17777          *:*                                    999\r\n" +
        "  UDP    [::]:7777              *:*                                    5678\r\n" +
        "  UDP    127.0.0.1:7777         *:*                                    1234\r\n" +
        "  TCP    0.0.0.0:7777           0.0.0.0:0              LISTENING       4242\r\n";

    [Test]
    public void ParseNetstat_FindsUdpOwnersOfExactPort_Distinct()
    {
        Assert.That(PlayModePortLeakProbe.ParseNetstatUdpOwners(NetstatSample, 7777), Is.EqualTo(new[] { 1234, 5678 }));
    }

    [Test]
    public void ParseNetstat_NoMatchOrEmpty_ReturnsEmpty()
    {
        Assert.That(PlayModePortLeakProbe.ParseNetstatUdpOwners(NetstatSample, 7778), Is.Empty);
        Assert.That(PlayModePortLeakProbe.ParseNetstatUdpOwners(string.Empty, 7777), Is.Empty);
    }

    [Test]
    public void DescribeOwner_DistinguishesSelfOthersAndFailure()
    {
        Assert.That(PlayModePortLeakProbe.DescribeOwner(null, 1, null), Is.EqualTo("소유 pid 알 수 없음(netstat 실패)"));
        Assert.That(PlayModePortLeakProbe.DescribeOwner(new int[0], 1, null), Is.EqualTo("netstat 에 해당 포트 없음"));
        Assert.That(PlayModePortLeakProbe.DescribeOwner(new[] { 1 }, 1, _ => "Unity"), Is.EqualTo("이 에디터(pid 1 Unity)"));
        Assert.That(PlayModePortLeakProbe.DescribeOwner(new[] { 2 }, 1, null),
            Is.EqualTo("다른 프로세스(pid 2) — 다른 에디터 호스트면 오탐"));
        Assert.That(PlayModePortLeakProbe.DescribeOwner(new[] { 2, 1 }, 1, null),
            Is.EqualTo("이 에디터(pid 1) + 다른 프로세스(pid 2) — 다른 에디터 호스트면 오탐"));
    }

    [Test]
    public void BuildWarning_StartsWithPortLine_AndIncludesBothSnapshots()
    {
        string warning = PlayModePortLeakProbe.BuildWarning(
            new ushort[] { 7777, 7778 }, new[] { "7777: 이 에디터(pid 1)" }, "EDIT", null);

        Assert.That(warning, Is.EqualTo(
            "[PortLeak] Play 종료 후 포트 7777, 7778 아직 점유" +
            "\n  소유: 7777: 이 에디터(pid 1)" +
            "\n  종료 시 스냅샷: 없음" +
            "\n  편집 모드 스냅샷: EDIT"));
    }
}
