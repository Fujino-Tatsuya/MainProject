using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;

public sealed class DevBootPortRetryTests
{
    private static List<ushort> CollectPorts(ushort startPort, int maxAttempts)
    {
        var ports = new List<ushort>();
        // 시도 횟수보다 넉넉히 돌려 범위 밖에서 false 가 나는지도 확인한다.
        int limit = System.Math.Max(1, maxAttempts) + 5;
        for (int i = 0; i < limit; i++)
        {
            if (DevSceneBooter.TryGetHostPort(startPort, maxAttempts, i, out ushort port))
                ports.Add(port);
            else
                break;
        }

        return ports;
    }

    [Test]
    public void TenAttempts_From7777_Yields7777To7786InOrder()
    {
        List<ushort> ports = CollectPorts(7777, 10);

        Assert.That(ports, Is.EqualTo(new ushort[] { 7777, 7778, 7779, 7780, 7781, 7782, 7783, 7784, 7785, 7786 }));
        Assert.That(DevSceneBooter.TryGetHostPort(7777, 10, 10, out _), Is.False);
    }

    [Test]
    public void OneAttempt_YieldsStartPortOnly()
    {
        Assert.That(CollectPorts(7777, 1), Is.EqualTo(new ushort[] { 7777 }));
    }

    [TestCase(0)]
    [TestCase(-3)]
    public void ZeroOrNegativeAttempts_TreatedAsOne(int maxAttempts)
    {
        Assert.That(CollectPorts(7777, maxAttempts), Is.EqualTo(new ushort[] { 7777 }));
    }

    [Test]
    public void NearUshortMax_StopsAt65535()
    {
        Assert.That(CollectPorts(65530, 10), Is.EqualTo(new ushort[] { 65530, 65531, 65532, 65533, 65534, 65535 }));
        Assert.That(DevSceneBooter.TryGetHostPort(65530, 10, 6, out _), Is.False);
    }

    [Test]
    public void StartAt65535_YieldsOnlyThatPort()
    {
        Assert.That(CollectPorts(65535, 10), Is.EqualTo(new ushort[] { 65535 }));
    }

    [Test]
    public void NegativeAttemptIndex_ReturnsFalse()
    {
        Assert.That(DevSceneBooter.TryGetHostPort(7777, 10, -1, out _), Is.False);
    }

    [Test]
    public void Probe_ReportsInUseWhileBound_AndFreeAfterClose()
    {
        ushort port;
        using (var holder = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            // 0 으로 바인드해 OS 가 비어 있는 포트를 고르게 한다.
            holder.Bind(new IPEndPoint(IPAddress.Any, 0));
            port = (ushort)((IPEndPoint)holder.LocalEndPoint).Port;

            Assert.That(DevSceneBooter.IsUdpPortInUse(port), Is.True);
        }

        Assert.That(DevSceneBooter.IsUdpPortInUse(port), Is.False);
    }
}
