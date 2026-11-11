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

    // ── 할 일 256 — 오너 과열 1차 예측(복제 기준점 + 서버 미반영 자기 발) ──

    // 복제가 아직 0 인데 자기 발 4×30 을 쏘면 예측은 과열(최대 100) — 다음 발을 쏘지 않는다.
    [Test]
    public void Prediction_AddsUnreflectedOwnShots()
    {
        var data = Data();
        var replicated = GunnerHeatModel.Reset(0);
        var prediction = new GunnerHeatPrediction();
        for (int i = 0; i < 4; i++)
            prediction.Record(1.0 + i * 0.1, 30f);

        GunnerHeatState predicted = prediction.Predict(replicated, 1.35, data);
        Assert.That(GunnerHeatModel.OverheatedAt(predicted, 1.35, data), Is.True);
        Assert.That(prediction.PendingCount, Is.EqualTo(4));
    }

    // 서버가 반영한 발(기준점 시각 이하 — 서버는 도착 시각에 반영해 오너 시각보다 뒤)은 다시 더하지 않는다.
    [Test]
    public void Prediction_DropsShotsReflectedByServer()
    {
        var data = Data();
        var prediction = new GunnerHeatPrediction();
        prediction.Record(1.0, 30f);
        prediction.Record(1.2, 30f);

        // 서버가 첫 발을 1.05(지연 0.05)에 반영 — 둘째 발만 미반영.
        var replicated = GunnerHeatModel.AddShot(GunnerHeatModel.Reset(0), 1.05, 30f, data);
        GunnerHeatState predicted = prediction.Predict(replicated, 1.25, data);
        Assert.That(prediction.PendingCount, Is.EqualTo(1));
        Assert.That(GunnerHeatModel.HeatAt(predicted, 1.25, data), Is.EqualTo(60f).Within(1e-4f));
    }

    // E 냉각(서버 초기화)은 앞선 미반영 발도 지운다. 거부돼 영영 반영되지 않은 발은 수명이 지나면 버린다.
    [Test]
    public void Prediction_ResetAndLifetimeDropPending()
    {
        var data = Data();
        var prediction = new GunnerHeatPrediction();
        prediction.Record(1.0, 30f);
        GunnerHeatState predicted = prediction.Predict(GunnerHeatModel.Reset(1.1), 1.1, data);
        Assert.That(prediction.PendingCount, Is.EqualTo(0), "E 냉각 뒤");
        Assert.That(GunnerHeatModel.HeatAt(predicted, 1.1, data), Is.EqualTo(0f));

        prediction.Record(2.0, 30f);
        prediction.Predict(GunnerHeatModel.Reset(0), 2.0 + GunnerHeatPrediction.PendingLifetime + 0.01, data);
        Assert.That(prediction.PendingCount, Is.EqualTo(0), "수명 지남(서버 거부·유실)");
    }
}
