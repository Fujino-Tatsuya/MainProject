using NUnit.Framework;

/// <summary>
/// 로비 결과창 애니메이션(할 일 215) 계산 — 진행도·카운트업 지연·투명도/크기·카운트업 숫자.
/// </summary>
public sealed class LobbyResultPanelAnimMathTests
{
    [Test]
    public void Progress_ClampsAndZeroDurationIsInstant()
    {
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.Progress(0f, 0.2f), 1e-6f);
        Assert.AreEqual(0.5f, LobbyResultPanelAnimMath.Progress(0.1f, 0.2f), 1e-6f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.Progress(5f, 0.2f), 1e-6f);
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.Progress(-1f, 0.2f), 1e-6f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.Progress(0f, 0f), 1e-6f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.Progress(0f, -1f), 1e-6f);
    }

    [Test]
    public void CountUpProgress_WaitsForDelay()
    {
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.CountUpProgress(0.1f, 0.15f, 0.8f), 1e-6f);
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.CountUpProgress(0.15f, 0.15f, 0.8f), 1e-6f);
        Assert.AreEqual(0.5f, LobbyResultPanelAnimMath.CountUpProgress(0.55f, 0.15f, 0.8f), 1e-5f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.CountUpProgress(2f, 0.15f, 0.8f), 1e-6f);
        // 지연 음수 = 0 취급, 지속 0 = 지연 뒤 즉시 1
        Assert.AreEqual(0.5f, LobbyResultPanelAnimMath.CountUpProgress(0.4f, -1f, 0.8f), 1e-5f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.CountUpProgress(0.2f, 0.15f, 0f), 1e-6f);
    }

    [Test]
    public void AlphaAndScale_FromVisibility()
    {
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.Alpha(-0.2f), 1e-6f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.Alpha(1.1f), 1e-6f);
        Assert.AreEqual(0.95f, LobbyResultPanelAnimMath.Scale(0f, 0.95f), 1e-6f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.Scale(1f, 0.95f), 1e-6f);
        Assert.AreEqual(0.975f, LobbyResultPanelAnimMath.Scale(0.5f, 0.95f), 1e-6f);
        // 곡선 넘침(Back 류)은 크기에 그대로
        Assert.Greater(LobbyResultPanelAnimMath.Scale(1.1f, 0.95f), 1f);
    }

    [Test]
    public void CountUpValue_ZeroToFinal_NeverOvershoots()
    {
        Assert.AreEqual(0, LobbyResultPanelAnimMath.CountUpValue(48210, 0f));
        Assert.AreEqual(0, LobbyResultPanelAnimMath.CountUpValue(48210, -0.5f));
        Assert.AreEqual(24105, LobbyResultPanelAnimMath.CountUpValue(48210, 0.5f));
        Assert.AreEqual(48210, LobbyResultPanelAnimMath.CountUpValue(48210, 1f));
        Assert.AreEqual(48210, LobbyResultPanelAnimMath.CountUpValue(48210, 1.3f));
        Assert.AreEqual(0, LobbyResultPanelAnimMath.CountUpValue(0, 0.7f));
        Assert.AreEqual(int.MaxValue, LobbyResultPanelAnimMath.CountUpValue(int.MaxValue, 1f));
        Assert.LessOrEqual(LobbyResultPanelAnimMath.CountUpValue(int.MaxValue, 0.999999f), int.MaxValue);
    }
}
