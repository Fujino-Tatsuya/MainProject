using System.Text;
using NUnit.Framework;

/// <summary>ConnectionData 의 playerId 인코딩·디코딩 — 빈 값·너무 긴 값·깨진 바이트.</summary>
public sealed class PlayerIdConnectionDataTests
{
    [TestCase("booth-1")]
    [TestCase("부스PC-2")]
    [TestCase("DESKTOP-R3NEADA-VP_abc")]
    public void 인코딩한_값은_그대로_디코딩된다(string playerId)
    {
        var payload = PlayerIdConnectionData.Encode(playerId);
        Assert.That(PlayerIdConnectionData.TryDecode(payload, out var decoded), Is.True);
        Assert.That(decoded, Is.EqualTo(playerId));
    }

    [Test]
    public void 앞뒤_공백은_지운다()
    {
        Assert.That(PlayerIdConnectionData.TryDecode(PlayerIdConnectionData.Encode("  booth-1 \t"), out var decoded), Is.True);
        Assert.That(decoded, Is.EqualTo("booth-1"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void 빈_값은_빈_배열이고_디코딩에_실패한다(string playerId)
    {
        var payload = PlayerIdConnectionData.Encode(playerId);
        Assert.That(payload, Is.Empty);
        Assert.That(PlayerIdConnectionData.TryDecode(payload, out var decoded), Is.False);
        Assert.That(decoded, Is.Empty);
        Assert.That(PlayerIdConnectionData.TryDecode(null, out _), Is.False);
    }

    [Test]
    public void 너무_긴_값은_상한까지_잘라_보낸다()
    {
        var longId = new string('a', 200);
        var payload = PlayerIdConnectionData.Encode(longId);
        Assert.That(payload.Length, Is.EqualTo(PlayerIdConnectionData.MaxBytes));
        Assert.That(PlayerIdConnectionData.TryDecode(payload, out var decoded), Is.True);
        Assert.That(decoded, Is.EqualTo(new string('a', PlayerIdConnectionData.MaxBytes)));
        Assert.That(PlayerIdConnectionData.Encode(longId), Is.EqualTo(payload), "자른 값도 매번 같다(재접속 키)");
    }

    [Test]
    public void 한글을_자를_때_글자를_가르지_않는다()
    {
        var longId = new string('가', 40); // 3바이트 × 40 = 120
        var payload = PlayerIdConnectionData.Encode(longId);
        Assert.That(payload.Length, Is.LessThanOrEqualTo(PlayerIdConnectionData.MaxBytes));
        Assert.That(PlayerIdConnectionData.TryDecode(payload, out var decoded), Is.True);
        Assert.That(decoded, Is.EqualTo(new string('가', PlayerIdConnectionData.MaxBytes / 3)));
    }

    [Test]
    public void 서로게이트_쌍을_반으로_가르지_않는다()
    {
        var longId = string.Concat(System.Linq.Enumerable.Repeat("😀", 20)); // 4바이트 × 20 = 80
        var payload = PlayerIdConnectionData.Encode(longId);
        Assert.That(PlayerIdConnectionData.TryDecode(payload, out var decoded), Is.True);
        Assert.That(decoded.Length % 2, Is.Zero);
        Assert.That(char.IsHighSurrogate(decoded[decoded.Length - 2]), Is.True);
    }

    [Test]
    public void 상한을_넘는_바이트는_거절()
    {
        var payload = Encoding.UTF8.GetBytes(new string('a', PlayerIdConnectionData.MaxBytes + 1));
        Assert.That(PlayerIdConnectionData.TryDecode(payload, out _), Is.False);
    }

    [Test]
    public void 깨진_UTF8_은_거절()
    {
        Assert.That(PlayerIdConnectionData.TryDecode(new byte[] { 0x61, 0xFF, 0x62 }, out _), Is.False);
        Assert.That(PlayerIdConnectionData.TryDecode(new byte[] { 0xEA, 0xB0 }, out _), Is.False, "잘린 멀티바이트");
    }

    [Test]
    public void 제어_문자가_섞이면_거절()
    {
        Assert.That(PlayerIdConnectionData.TryDecode(new byte[] { 0x61, 0x00, 0x62 }, out _), Is.False);
        Assert.That(PlayerIdConnectionData.TryDecode(Encoding.UTF8.GetBytes("a\nb"), out _), Is.False);
    }
}
