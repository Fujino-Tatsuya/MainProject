using NUnit.Framework;

// 패널 배경 블러(할 일 201) 순수 규칙 — 참조 카운트·가장 위 패널·세기 보간·캔버스 전환 판정.
public sealed class PanelBackdropBlurTests
{
    [Test]
    public void OverlappingPanels_StayOn_UntilLastReleased()
    {
        var state = new PanelBackdropBlurState();
        object a = new object(), b = new object();

        Assert.IsTrue(state.Acquire(a, 200));
        Assert.IsTrue(state.Acquire(b, 900));
        Assert.IsFalse(state.Acquire(a, 200), "같은 패널 두 번은 한 번으로");
        Assert.AreEqual(2, state.Count);
        Assert.AreEqual(900, state.TopOrder);

        Assert.IsTrue(state.Release(b));
        Assert.AreEqual(200, state.TopOrder, "위 패널이 닫히면 아래 패널 순서로");
        Assert.IsFalse(state.Release(b), "잡지 않은 owner 는 무시");

        state.Release(a);
        Assert.AreEqual(0, state.Count);
        Assert.AreEqual(int.MinValue, state.TopOrder);
    }

    [Test]
    public void Amount_FadesInAndOut_OverFadeSeconds()
    {
        var state = new PanelBackdropBlurState();
        object panel = new object();

        state.Acquire(panel, 60);
        state.Advance(0.075f, 0.15f);
        Assert.AreEqual(0.5f, state.Amount, 1e-4f);
        state.Advance(0.2f, 0.15f);
        Assert.AreEqual(1f, state.Amount, 1e-4f);

        state.Release(panel);
        Assert.IsTrue(state.WantsCanvasSwitch, "닫히는 보간 중에는 HUD 를 카메라 모드로 둔다");
        state.Advance(0.15f, 0.15f);
        Assert.AreEqual(0f, state.Amount, 1e-4f);
        Assert.IsFalse(state.WantsCanvasSwitch);
    }

    [Test]
    public void Amount_ZeroFade_JumpsToTarget()
    {
        var state = new PanelBackdropBlurState();
        state.Acquire(this, 60);
        state.Advance(0f, 0f);
        Assert.AreEqual(1f, state.Amount);
    }

    [Test]
    public void ShouldSwitch_OnlyOverlayRootsBelowTopPanel_NotPanelsThemselves()
    {
        const int top = 900;
        Assert.IsTrue(PanelBackdropBlurState.ShouldSwitch(true, true, false, 0, top), "HUD(0)");
        Assert.IsTrue(PanelBackdropBlurState.ShouldSwitch(true, true, false, 200, top), "아래 패널(결과창 200)도 흐림");
        Assert.IsFalse(PanelBackdropBlurState.ShouldSwitch(true, true, true, 900, top), "블러를 잡은 패널 자신");
        Assert.IsFalse(PanelBackdropBlurState.ShouldSwitch(true, true, false, 900, top), "같은 순서는 위로 본다");
        Assert.IsFalse(PanelBackdropBlurState.ShouldSwitch(true, true, false, short.MaxValue - 2, top), "간파 암전 HUD 덮개");
        Assert.IsFalse(PanelBackdropBlurState.ShouldSwitch(false, true, false, 0, top), "이미 카메라·월드 캔버스");
        Assert.IsFalse(PanelBackdropBlurState.ShouldSwitch(true, false, false, 0, top), "중첩 캔버스는 루트를 따른다");
        Assert.IsFalse(PanelBackdropBlurState.ShouldSwitch(true, true, false, 0, int.MinValue), "열린 패널 없음");
    }
}
