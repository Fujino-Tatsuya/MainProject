using NUnit.Framework;

/// <summary>버튼 hover 흰 테두리(할 일 224) — 켜짐 진행도 한 프레임 계산.</summary>
public sealed class UiHoverBorderTests
{
    [Test]
    public void Step_FadesWithinFadeTime_ZeroIsInstant()
    {
        // 0.1초에 다 켜진다: 0.05초 = 절반, 넘치지 않음
        Assert.AreEqual(0.5f, UiHoverBorder.Step(0f, 1f, 0.05f, 0.1f), 1e-5f);
        Assert.AreEqual(1f, UiHoverBorder.Step(0.8f, 1f, 0.05f, 0.1f), 1e-6f);
        Assert.AreEqual(0.5f, UiHoverBorder.Step(1f, 0f, 0.05f, 0.1f), 1e-5f);
        Assert.AreEqual(0f, UiHoverBorder.Step(0.2f, 0f, 0.05f, 0.1f), 1e-6f);
        // 시간 0 = 즉시, 음수 dt = 그대로
        Assert.AreEqual(1f, UiHoverBorder.Step(0f, 1f, 0.01f, 0f), 1e-6f);
        Assert.AreEqual(0.3f, UiHoverBorder.Step(0.3f, 1f, -1f, 0.1f), 1e-6f);
    }
}
