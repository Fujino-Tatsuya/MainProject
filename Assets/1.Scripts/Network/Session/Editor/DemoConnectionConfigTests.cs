using NUnit.Framework;

/// <summary>
/// 부스 데모 연결 설정 파일 규칙 — 정상 host/client, role 자동 판정(할 일 72), 파일 없음(Q5), 틀린 값은 기본값 + 사유, MPPM Client 태그 덮어쓰기.
/// </summary>
public sealed class DemoConnectionConfigTests
{
    const string Pc = "BOOTH-PC1";

    /// <summary>이 PC 의 IPv4 — 자동 판정 테스트용. loopback·사설·Tailscale 대역이 섞인 실제 PC 모양.</summary>
    static readonly string[] LocalAddresses = { "127.0.0.1", "192.168.0.21", "100.79.198.77" };

    static DemoConnectionLoadResult Parse(string json, string fallback = Pc, string[] localAddresses = null)
    {
        Assert.IsTrue(DemoConnectionConfig.TryParse(json, fallback, localAddresses ?? LocalAddresses, out var result), "파싱 자체는 성공해야 한다");
        return result;
    }

    // ─── 정상 ──────────────────────────────────────────────────────────

    [Test]
    public void 정상_클라_설정은_그대로_읽힌다()
    {
        var r = Parse("{ \"role\": \"client\", \"hostAddress\": \"192.168.0.10\", \"port\": 7778, \"playerId\": \"p2\"," +
                      " \"retryIntervalSeconds\": 2.5, \"connectTimeoutSeconds\": 4, \"disconnectTimeoutSeconds\": 12 }");
        var c = r.Config;

        Assert.IsFalse(r.HasIssues, string.Join(" / ", r.Issues));
        Assert.IsFalse(r.IsFileMissing);
        Assert.AreEqual(DemoConnectionRole.Client, c.Role);
        Assert.IsFalse(c.IsHost);
        Assert.AreEqual("192.168.0.10", c.HostAddress);
        Assert.AreEqual(7778, c.Port);
        Assert.AreEqual("p2", c.PlayerId);
        Assert.AreEqual(2.5f, c.RetryIntervalSeconds);
        Assert.AreEqual(4f, c.ConnectTimeoutSeconds);
        Assert.AreEqual(12f, c.DisconnectTimeoutSeconds);
        Assert.AreEqual(string.Empty, c.ListenAddress);
    }

    [Test]
    public void 호스트는_0000_에서_듣는다()
    {
        var r = Parse("{ \"role\": \"HOST\", \"port\": 7777 }");

        Assert.IsFalse(r.HasIssues, string.Join(" / ", r.Issues));
        Assert.IsTrue(r.Config.IsHost);
        Assert.AreEqual("0.0.0.0", r.Config.ListenAddress);
    }

    [Test]
    public void 빠진_필드는_사유_없이_기본값()
    {
        var r = Parse("{ \"role\": \"client\", \"hostAddress\": \"10.0.0.2\" }");
        var c = r.Config;

        Assert.IsFalse(r.HasIssues, string.Join(" / ", r.Issues));
        Assert.AreEqual(DemoConnectionConfig.DefaultPort, c.Port);
        Assert.AreEqual(Pc, c.PlayerId, "playerId 가 없으면 PC 이름");
        Assert.AreEqual(DemoConnectionConfig.DefaultRetryIntervalSeconds, c.RetryIntervalSeconds);
        Assert.AreEqual(DemoConnectionConfig.DefaultConnectTimeoutSeconds, c.ConnectTimeoutSeconds);
        Assert.AreEqual(DemoConnectionConfig.DefaultDisconnectTimeoutSeconds, c.DisconnectTimeoutSeconds);
    }

    // ─── 파일 없음 (Q5) ───────────────────────────────────────────────

    [Test]
    public void 파일이_없으면_호스트_127001_7777_과_안내_플래그()
    {
        var r = DemoConnectionConfig.ForMissingFile(Pc);

        Assert.IsTrue(r.IsFileMissing);
        Assert.IsFalse(r.HasIssues);
        Assert.IsTrue(r.Config.IsHost);
        Assert.AreEqual("127.0.0.1", r.Config.HostAddress);
        Assert.AreEqual(7777, r.Config.Port);
        Assert.AreEqual(Pc, r.Config.PlayerId);
    }

    [Test]
    public void 경로는_dataPath_의_부모_폴더()
    {
        var path = DemoConnectionConfig.ResolvePath("C:/Booth/Game_Data").Replace('\\', '/');
        Assert.AreEqual("C:/Booth/demo-connection.json", path);
    }

    // ─── 틀린 값 → 기본값 + 사유 ─────────────────────────────────────

    [TestCase("192.168.0")]
    [TestCase("10")]
    [TestCase("256.1.1.1")]
    [TestCase("::1")]
    [TestCase("localhost")]
    [TestCase("")]
    public void 잘못된_IP_는_127001_로_대체(string address)
    {
        var r = Parse($"{{ \"role\": \"client\", \"hostAddress\": \"{address}\" }}");

        Assert.AreEqual("127.0.0.1", r.Config.HostAddress);
        Assert.AreEqual(1, r.Issues.Count);
        StringAssert.Contains("hostAddress", r.Issues[0]);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(65536)]
    public void 범위_밖_포트는_7777_로_대체(int port)
    {
        var r = Parse($"{{ \"port\": {port} }}");

        Assert.AreEqual(7777, r.Config.Port);
        Assert.AreEqual(1, r.Issues.Count);
        StringAssert.Contains("port", r.Issues[0]);
    }

    [Test]
    public void 음수_0_과대_초_값은_각각_기본값과_사유()
    {
        var r = Parse("{ \"retryIntervalSeconds\": -1, \"connectTimeoutSeconds\": 0, \"disconnectTimeoutSeconds\": 99999 }");
        var c = r.Config;

        Assert.AreEqual(3, r.Issues.Count);
        Assert.AreEqual(DemoConnectionConfig.DefaultRetryIntervalSeconds, c.RetryIntervalSeconds);
        Assert.AreEqual(DemoConnectionConfig.DefaultConnectTimeoutSeconds, c.ConnectTimeoutSeconds);
        Assert.AreEqual(DemoConnectionConfig.DefaultDisconnectTimeoutSeconds, c.DisconnectTimeoutSeconds);
    }

    [Test]
    public void 빈_playerId_는_PC_이름으로_대체하고_사유()
    {
        var r = Parse("{ \"playerId\": \"   \" }");

        Assert.AreEqual(Pc, r.Config.PlayerId);
        Assert.AreEqual(1, r.Issues.Count);
        StringAssert.Contains("playerId", r.Issues[0]);
    }

    [Test]
    public void PC_이름도_비면_마지막_기본_playerId()
    {
        var r = Parse("{ }", fallback: "");
        Assert.AreEqual(DemoConnectionConfig.LastResortPlayerId, r.Config.PlayerId);
    }

    [Test]
    public void 알_수_없는_role_은_사유를_남기고_자동_판정()
    {
        var r = Parse("{ \"role\": \"server\", \"hostAddress\": \"10.0.0.2\" }");

        Assert.AreEqual(DemoConnectionRole.Client, r.Config.Role);
        Assert.IsTrue(r.Config.AutoRole.HasValue);
        Assert.AreEqual(1, r.Issues.Count);
        StringAssert.Contains("role", r.Issues[0]);
    }

    // ─── role 자동 판정 (할 일 72) ────────────────────────────────────

    [TestCase("auto")]
    [TestCase("AUTO")]
    [TestCase("")]
    [TestCase("  ")]
    public void auto_나_빈_role_은_이_PC_IP_가_hostAddress_면_호스트(string role)
    {
        var r = Parse($"{{ \"role\": \"{role}\", \"hostAddress\": \"100.79.198.77\" }}");

        Assert.IsTrue(r.Config.IsHost);
        Assert.IsFalse(r.HasIssues);
        var auto = r.Config.AutoRole.Value;
        Assert.AreEqual(DemoAutoRoleBasis.LocalAddressMatch, auto.Basis);
        Assert.AreEqual("100.79.198.77", auto.Address);
    }

    [Test]
    public void role_필드가_없어도_자동_판정()
    {
        var r = Parse("{ \"hostAddress\": \"192.168.0.21\" }");

        Assert.IsTrue(r.Config.IsHost);
        Assert.AreEqual(DemoAutoRoleBasis.LocalAddressMatch, r.Config.AutoRole.Value.Basis);
    }

    [Test]
    public void 이_PC_에_hostAddress_가_없으면_그_주소로_접속하는_클라()
    {
        var r = Parse("{ \"role\": \"auto\", \"hostAddress\": \"100.79.198.78\" }");

        Assert.AreEqual(DemoConnectionRole.Client, r.Config.Role);
        Assert.AreEqual("100.79.198.78", r.Config.HostAddress);
        Assert.AreEqual(DemoAutoRoleBasis.NoLocalMatch, r.Config.AutoRole.Value.Basis);
        Assert.AreEqual("100.79.198.78", r.Config.AutoRole.Value.Address);
    }

    [Test]
    public void hostAddress_가_없으면_127001_이라_호스트()
    {
        var r = Parse("{ \"role\": \"auto\" }", localAddresses: new string[0]);

        Assert.IsTrue(r.Config.IsHost);
        Assert.IsFalse(r.HasIssues);
        Assert.AreEqual(DemoAutoRoleBasis.LoopbackHostAddress, r.Config.AutoRole.Value.Basis);
    }

    [TestCase("host", DemoConnectionRole.Host)]
    [TestCase("client", DemoConnectionRole.Client)]
    public void host_client_명시는_자동_판정보다_우선(string role, DemoConnectionRole expected)
    {
        // 192.168.0.21 은 이 PC 주소지만 client 명시면 클라, 10.9.9.9 는 남의 주소지만 host 명시면 호스트.
        var address = expected == DemoConnectionRole.Host ? "10.9.9.9" : "192.168.0.21";
        var r = Parse($"{{ \"role\": \"{role}\", \"hostAddress\": \"{address}\" }}");

        Assert.AreEqual(expected, r.Config.Role);
        Assert.IsFalse(r.Config.AutoRole.HasValue);
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("localhost")]
    [TestCase("127.0.0.1")]
    [TestCase("127.1.2.3")]
    public void 판정_빈_주소_loopback_은_호스트(string hostAddress)
    {
        var d = DemoConnectionConfig.ResolveAutoRole(hostAddress, new string[0]);

        Assert.AreEqual(DemoConnectionRole.Host, d.Role);
        Assert.AreEqual(DemoAutoRoleBasis.LoopbackHostAddress, d.Basis);
    }

    [Test]
    public void 판정_IPv6_와_틀린_값은_무시한다()
    {
        var d = DemoConnectionConfig.ResolveAutoRole("192.168.0.10", new[] { "fe80::1", "::ffff:192.168.0.10", "192.168.0", null, " 192.168.0.11 " });

        Assert.AreEqual(DemoConnectionRole.Client, d.Role);
        Assert.AreEqual(DemoAutoRoleBasis.NoLocalMatch, d.Basis);
    }

    [Test]
    public void 판정_공백이_붙은_로컬_주소도_일치한다()
    {
        var d = DemoConnectionConfig.ResolveAutoRole(" 192.168.0.10 ", new[] { "fe80::1", " 192.168.0.10 " });

        Assert.AreEqual(DemoConnectionRole.Host, d.Role);
        Assert.AreEqual("192.168.0.10", d.Address);
    }

    [Test]
    public void 판정_로컬_목록이_null_이면_클라()
    {
        var d = DemoConnectionConfig.ResolveAutoRole("192.168.0.10", null);

        Assert.AreEqual(DemoConnectionRole.Client, d.Role);
        Assert.IsNotNull(d.LocalAddresses);
    }

    [TestCase("{ \"role\": \"client\", ")]
    [TestCase("not json")]
    [TestCase("")]
    [TestCase("   ")]
    public void 깨진_JSON_은_실패와_호스트_기본값(string json)
    {
        Assert.IsFalse(DemoConnectionConfig.TryParse(json, Pc, LocalAddresses, out var r));

        Assert.IsTrue(r.HasIssues);
        Assert.IsFalse(r.IsFileMissing);
        Assert.IsTrue(r.Config.IsHost);
        Assert.AreEqual("127.0.0.1", r.Config.HostAddress);
        Assert.AreEqual(7777, r.Config.Port);
        Assert.AreEqual(Pc, r.Config.PlayerId);
    }

    // ─── MPPM 태그 ────────────────────────────────────────────────────

    [Test]
    public void Client_태그면_클라_127001_로_덮고_playerId_에_인스턴스_접미사()
    {
        var host = Parse("{ \"role\": \"host\", \"hostAddress\": \"192.168.0.10\", \"port\": 7790 }");

        var r = DemoConnectionConfig.ApplyMppmTags(host, new[] { "Client" }, "mppm1");

        Assert.AreEqual(DemoConnectionRole.Client, r.Config.Role);
        Assert.AreEqual("127.0.0.1", r.Config.HostAddress);
        Assert.AreEqual(7790, r.Config.Port, "포트는 유지");
        Assert.AreEqual(Pc + "-mppm1", r.Config.PlayerId);
    }

    [Test]
    public void Client_태그는_자동_판정_호스트도_클라_127001_로_덮는다()
    {
        // 공용 파일(auto · 이 PC 주소)을 둔 에디터에서도 MPPM 가상 플레이어는 메인 에디터 호스트로 붙어야 한다.
        var auto = Parse("{ \"role\": \"auto\", \"hostAddress\": \"100.79.198.77\" }");
        Assert.IsTrue(auto.Config.IsHost);

        var r = DemoConnectionConfig.ApplyMppmTags(auto, new[] { "Client" }, "mppm1");

        Assert.AreEqual(DemoConnectionRole.Client, r.Config.Role);
        Assert.AreEqual("127.0.0.1", r.Config.HostAddress);
        Assert.AreEqual(Pc + "-mppm1", r.Config.PlayerId);
        Assert.IsFalse(r.Config.AutoRole.HasValue, "덮어쓴 role 은 자동 판정 근거를 보여 주지 않는다");
    }

    [Test]
    public void 가상_플레이어마다_playerId_가_겹치지_않는다()
    {
        var baseResult = DemoConnectionConfig.ForMissingFile(Pc);

        var a = DemoConnectionConfig.ApplyMppmTags(baseResult, new[] { "client" }, "mppmA");
        var b = DemoConnectionConfig.ApplyMppmTags(baseResult, new[] { "Client" }, "mppmB");

        Assert.AreNotEqual(a.Config.PlayerId, b.Config.PlayerId);
    }

    [Test]
    public void Client_태그로_덮으면_호스트_모드_안내는_내린다()
    {
        var r = DemoConnectionConfig.ApplyMppmTags(DemoConnectionConfig.ForMissingFile(Pc), new[] { "Client" }, "mppm1");
        Assert.IsFalse(r.IsFileMissing);
    }

    [Test]
    public void Client_태그가_없으면_그대로()
    {
        var original = DemoConnectionConfig.ForMissingFile(Pc);

        Assert.AreSame(original, DemoConnectionConfig.ApplyMppmTags(original, new[] { "FastNetwork" }, "mppm1"));
        Assert.AreSame(original, DemoConnectionConfig.ApplyMppmTags(original, new string[0], "mppm1"));
        Assert.AreSame(original, DemoConnectionConfig.ApplyMppmTags(original, null, "mppm1"));
    }
}
