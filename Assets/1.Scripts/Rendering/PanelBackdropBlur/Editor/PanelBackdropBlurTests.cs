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
    public void Drive_SetsAmountDirectly_IgnoresFade_UntilReleased()
    {
        var state = new PanelBackdropBlurState();
        object title = new object();
        var look = new PanelBackdropBlurLook(1.42f, 0f, 0f, true);

        Assert.IsFalse(state.Drive(title, 1f, look), "잡지 않은 owner 는 구동 못 함");
        Assert.IsFalse(state.TryGetDrivenLook(out _));

        state.Acquire(title, 0);
        Assert.IsTrue(state.Drive(title, 1f, look));
        Assert.AreEqual(1f, state.Amount, 1e-4f, "구동 값은 바로 반영");
        Assert.IsTrue(state.TryGetDrivenLook(out PanelBackdropBlurLook got));
        Assert.AreEqual(0f, got.saturation);

        state.Drive(title, 0.4f, look);
        state.Advance(1f, 0.15f);
        Assert.AreEqual(0.4f, state.Amount, 1e-4f, "구동 중에는 fade 보간이 덮지 않는다");

        state.Drive(title, 2f, look);
        Assert.AreEqual(1f, state.Amount, 1e-4f, "0~1 로 자른다");

        state.Drive(title, 0f, look);
        Assert.IsTrue(state.Release(title));
        Assert.IsFalse(state.TryGetDrivenLook(out _), "놓으면 구동도 풀린다");
        state.Advance(0.016f, 0.15f);
        Assert.AreEqual(0f, state.Amount, 1e-4f);
        Assert.IsFalse(state.WantsCanvasSwitch, "0 까지 구동한 뒤 놓으면 바로 꺼짐");
    }

    [Test]
    public void DrivenGlitch_OnlyWhileDriven_ResetByPlainDrive()
    {
        var state = new PanelBackdropBlurState();
        object title = new object();
        var look = new PanelBackdropBlurLook(1.42f, 0f, 0f, true);
        var glitchLook = new PanelBackdropGlitchLook(0.45f, 3f, 0.008f, 0.06f, 0.35f, 0.18f);

        state.SetDrivenGlitch(1f, glitchLook);
        Assert.AreEqual(0f, state.GetDrivenGlitch(out _), "구동 전에는 글리치를 걸 수 없다");

        state.Acquire(title, 0);
        state.Drive(title, 1f, look);
        state.SetDrivenGlitch(2f, glitchLook);
        Assert.AreEqual(1f, state.GetDrivenGlitch(out PanelBackdropGlitchLook got), 1e-4f, "0~1 로 자른다");
        Assert.AreEqual(0.06f, got.tearOffset, 1e-4f);

        state.Drive(title, 1f, look);
        Assert.AreEqual(0f, state.GetDrivenGlitch(out _), "글리치 없는 Drive 는 글리치 0");

        state.SetDrivenGlitch(0.3f, glitchLook);
        state.Release(title);
        Assert.AreEqual(0f, state.GetDrivenGlitch(out _), "놓으면 글리치도 꺼진다");
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
