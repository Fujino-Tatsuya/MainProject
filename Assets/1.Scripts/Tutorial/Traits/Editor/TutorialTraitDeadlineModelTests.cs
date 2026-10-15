using NUnit.Framework;

/// <summary>특성 창 교착 방지 게이지 고정 (할 일 15 후속) — 표시·비율·10초 강조·로컬 감소·재동기화·문구.</summary>
public sealed class TutorialTraitDeadlineModelTests
{
    [Test]
    public void HiddenByDefault()
    {
        var model = new TutorialTraitDeadlineModel();

        Assert.That(model.Visible, Is.False);
        Assert.That(model.IsUrgent, Is.False, "숨김이면 0초여도 강조하지 않는다");
        Assert.That(model.Fill, Is.EqualTo(0f));
    }

    [Test]
    public void Sync_FillIsRemainingOverTotal()
    {
        var model = new TutorialTraitDeadlineModel();
        model.Sync(true, 45f, 60f, 1, 3);

        Assert.That(model.Visible, Is.True);
        Assert.That(model.Fill, Is.EqualTo(0.75f).Within(1e-5f));
        Assert.That(model.Done, Is.EqualTo(1));
        Assert.That(model.Count, Is.EqualTo(3));
    }

    [Test]
    public void Fill_ClampsAndZeroTotalIsEmpty()
    {
        var model = new TutorialTraitDeadlineModel();
        model.Sync(true, 90f, 60f, 1, 2);
        Assert.That(model.Fill, Is.EqualTo(1f), "남은 > 전체(인스펙터 값 불일치)면 가득");

        model.Sync(true, -5f, 60f, 1, 2);
        Assert.That(model.Remaining, Is.EqualTo(0f));
        Assert.That(model.Fill, Is.EqualTo(0f));

        model.Sync(true, 10f, 0f, 1, 2);
        Assert.That(model.Fill, Is.EqualTo(0f), "전체 0 이하 → 0(0 나누기 없음)");
    }

    [Test]
    public void Urgent_AtTenSecondsOrLess()
    {
        var model = new TutorialTraitDeadlineModel();
        model.Sync(true, 10.01f, 60f, 1, 2);
        Assert.That(model.IsUrgent, Is.False);

        model.Sync(true, TutorialTraitDeadlineModel.UrgentSeconds, 60f, 1, 2);
        Assert.That(model.IsUrgent, Is.True, "10초 이하");

        model.Sync(true, 0f, 60f, 1, 2);
        Assert.That(model.IsUrgent, Is.True);
    }

    [Test]
    public void Tick_DecreasesLocallyAndStopsAtZero()
    {
        var model = new TutorialTraitDeadlineModel();
        model.Sync(true, 11f, 60f, 1, 2);

        model.Tick(0.5f);
        Assert.That(model.Remaining, Is.EqualTo(10.5f).Within(1e-5f));
        Assert.That(model.IsUrgent, Is.False);

        model.Tick(1f);
        Assert.That(model.IsUrgent, Is.True, "로컬 감소로 10초 구간에 들어간다");

        model.Tick(100f);
        Assert.That(model.Remaining, Is.EqualTo(0f), "0 에서 멈춤 — 만료 처리는 서버");

        model.Tick(-1f);
        Assert.That(model.Remaining, Is.EqualTo(0f), "음수 델타 무시");
    }

    [Test]
    public void Tick_IgnoredWhileHidden()
    {
        var model = new TutorialTraitDeadlineModel();
        model.Tick(5f);

        Assert.That(model.Remaining, Is.EqualTo(0f));
        Assert.That(model.Visible, Is.False);
    }

    [Test]
    public void Sync_ResyncsToServerValue()
    {
        var model = new TutorialTraitDeadlineModel();
        model.Sync(true, 40f, 60f, 1, 3);
        model.Tick(3f);
        Assert.That(model.Remaining, Is.EqualTo(37f).Within(1e-5f));

        // 다음 대기 상태 변경(두 번째 선택) — 서버 시각 기준 값으로 덮는다(로컬 드리프트 제거).
        model.Sync(true, 36.2f, 60f, 2, 3);
        Assert.That(model.Remaining, Is.EqualTo(36.2f).Within(1e-5f));
        Assert.That(model.Done, Is.EqualTo(2));
    }

    [Test]
    public void SyncHidden_ClearsValues()
    {
        var model = new TutorialTraitDeadlineModel();
        model.Sync(true, 5f, 60f, 1, 2);
        model.Sync(false, 5f, 60f, 1, 2);

        Assert.That(model.Visible, Is.False);
        Assert.That(model.IsUrgent, Is.False);
        Assert.That(model.Remaining, Is.EqualTo(0f));
        Assert.That(model.Done + model.Count, Is.EqualTo(0));
    }

    [Test]
    public void Texts_RoundSecondsUpAndShowDoneOverTotal()
    {
        var model = new TutorialTraitDeadlineModel();
        model.Sync(true, 9.2f, 60f, 2, 3);

        Assert.That(model.DisplaySeconds, Is.EqualTo(10), "올림 — 0.x초 남아도 0 으로 보이지 않게");
        Assert.That(model.FormatSeconds(), Is.EqualTo("10초 뒤 자동 선택"));
        Assert.That(model.FormatDone(), Is.EqualTo("선택 완료 2/3"));

        model.Sync(true, 0f, 60f, 2, 3);
        Assert.That(model.DisplaySeconds, Is.EqualTo(0));
    }
}
