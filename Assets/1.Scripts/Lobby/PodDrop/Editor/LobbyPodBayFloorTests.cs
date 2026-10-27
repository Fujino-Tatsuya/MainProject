using NUnit.Framework;

/// <summary>
/// 로비 발사대 둘레 생성 바닥(<see cref="LobbyPodBayFloorBuilder"/>, 할 일 100 floor2) 회귀 검사.
/// 커밋된 메시가 발사대(난간 플랫폼·아래 가운데 홈 포함)를 침범하지 않고, 기존 바닥 타일과 겹치지 않으며, 바깥 범위 둘레에 틈이 없는지 본다.
/// 실패하면 발사대·벽·타일이 옮겨진 것 — 메뉴 <c>Tools/Lobby/Rebuild Pod Bay Floor</c> 로 다시 만든다.
/// </summary>
public sealed class LobbyPodBayFloorTests
{
    static LobbyPodBayFloorBuilder.CheckResult Run()
    {
        var res = LobbyPodBayFloorBuilder.Check();
        Assert.That(res.HasValue, $"{LobbyPodBayFloorBuilder.FloorObjectName} 이 로비 프리팹에 없다.");
        return res.Value;
    }

    [Test]
    public void Floor_DoesNotIntrudeLaunchStation()
    {
        var r = Run();
        Assert.AreEqual(0, r.Overlaps, r.ToString());
        Assert.AreEqual(0, r.BlockedFloorCells, r.ToString());
        Assert.GreaterOrEqual(r.MinGap, LobbyPodBayFloorBuilder.Margin - 1e-4f, r.ToString());
    }

    [Test]
    public void Floor_DoesNotOverlapExistingTiles()
    {
        var r = Run();
        Assert.Greater(r.Tiles, 0, r.ToString());
        Assert.AreEqual(0, r.TilePairs, r.ToString());
    }

    [Test]
    public void Floor_HasNoSeamGaps()
    {
        var r = Run();
        Assert.Greater(r.SeamSamples, 0, r.ToString());
        Assert.AreEqual(0, r.SeamGaps, r.ToString());
        Assert.AreEqual(0, r.HoleCells, r.ToString());
    }
}
