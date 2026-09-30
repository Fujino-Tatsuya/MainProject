using NUnit.Framework;

/// <summary>과열 계산 규칙 고정 (character_gunner.md §5, PLAN-gunner.md G3).</summary>
public sealed class GunnerHeatModelTests
{
    // 최대 100, 단계 25/50/75, 대기 1초, 일반 10/s, 과열 50/s
    static GunnerHeatData Data() => GunnerHeatData.CreateForTest(100f, new[] { 25f, 50f, 75f }, 1f, 10f, 50f);

    [Test]
    public void Shot_AddsHeat_AndStagesFollowThresholds()
    {
        var data = Data();
        var s = GunnerHeatModel.Reset(0);
        s = GunnerHeatModel.AddShot(s, 0, 30f, data);

        Assert.That(GunnerHeatModel.HeatAt(s, 0, data), Is.EqualTo(30f));
        Assert.That(GunnerHeatModel.StageAt(s, 0, data), Is.EqualTo(1));
        Assert.That(GunnerHeatModel.OverheatedAt(s, 0, data), Is.False);
    }

    [Test]
    public void Cooling_WaitsDelay_ThenDecays()
    {
        var data = Data();
        var s = GunnerHeatModel.AddShot(GunnerHeatModel.Reset(0), 0, 30f, data);

        Assert.That(GunnerHeatModel.HeatAt(s, 0.9, data), Is.EqualTo(30f), "대기시간 전에는 그대로");
        Assert.That(GunnerHeatModel.HeatAt(s, 2.0, data), Is.EqualTo(20f).Within(1e-4f), "대기 1초 후 10/s");
        Assert.That(GunnerHeatModel.HeatAt(s, 10.0, data), Is.EqualTo(0f), "0 아래로 안 내려감");
    }

    [Test]
    public void ShotDuringCooling_RestartsDelay()
    {
        var data = Data();
        var s = GunnerHeatModel.AddShot(GunnerHeatModel.Reset(0), 0, 30f, data);
        s = GunnerHeatModel.AddShot(s, 2.0, 10f, data); // 2초 시점 20 + 10

        Assert.That(GunnerHeatModel.HeatAt(s, 2.5, data), Is.EqualTo(30f).Within(1e-4f), "대기시간 처음부터");
    }

    [Test]
    public void ReachingMax_Overheats_UntilZero_AtFasterRate()
    {
        var data = Data();
        var s = GunnerHeatModel.AddShot(GunnerHeatModel.Reset(0), 0, 150f, data);

        Assert.That(GunnerHeatModel.HeatAt(s, 0, data), Is.EqualTo(100f), "최대치에서 자름");
        Assert.That(GunnerHeatModel.OverheatedAt(s, 0, data), Is.True);
        Assert.That(GunnerHeatModel.StageAt(s, 0, data), Is.EqualTo(3), "과열 = 3단계");

        // 1초 대기 후 50/s → 1.5초에 75, 여전히 과열(최대치 아래여도 0 전에는 해제 안 됨)
        Assert.That(GunnerHeatModel.HeatAt(s, 1.5, data), Is.EqualTo(75f).Within(1e-4f));
        Assert.That(GunnerHeatModel.OverheatedAt(s, 1.5, data), Is.True);
        Assert.That(GunnerHeatModel.OverheatedAt(s, 3.0, data), Is.False, "0 이 되면 해제");
    }

    [Test]
    public void Reset_ClearsOverheat()
    {
        var data = Data();
        var s = GunnerHeatModel.AddShot(GunnerHeatModel.Reset(0), 0, 100f, data);
        s = GunnerHeatModel.Reset(0.5);

        Assert.That(GunnerHeatModel.HeatAt(s, 0.5, data), Is.EqualTo(0f));
        Assert.That(GunnerHeatModel.OverheatedAt(s, 0.5, data), Is.False);
        Assert.That(GunnerHeatModel.StageAt(s, 0.5, data), Is.EqualTo(0));
    }
}
