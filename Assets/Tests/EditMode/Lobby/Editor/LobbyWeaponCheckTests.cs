using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 할 일 86 T3 — TRAIN_01 무기 점검 체크 마스크(<see cref="LobbyWeaponCheckModel"/>·<see cref="LobbyWeaponCheckRules"/>),
/// 훈련 리셋 규칙(<see cref="TrainingResetRules"/>·<see cref="TrainingResetReservations"/>), 훈련 보정 최소 체력(<see cref="LobbyTrainingSupportRules"/>·<see cref="HealthFloorRules"/>).
/// </summary>
public sealed class LobbyWeaponCheckTests
{
    static LobbyWeaponCheckModel Started(params ulong[] roster)
    {
        var model = new LobbyWeaponCheckModel();
        model.Begin(roster);
        return model;
    }

    static void CompleteAll(LobbyWeaponCheckModel model, ulong clientId)
    {
        foreach (var item in LobbyWeaponCheckRules.DisplayOrder)
            model.Report(clientId, item);
    }

    // ─── 체크 마스크 ─────────────────────────────────────────────────────

    [Test]
    public void BeforeStart_ReportsAreIgnored()
    {
        var model = new LobbyWeaponCheckModel();
        Assert.That(model.Report(1, LobbyWeaponCheckItem.Dash), Is.False);

        model.Begin(new ulong[] { 1 });
        Assert.That(model.MaskOf(1), Is.Zero, "시작 전 행동은 시작 뒤에도 남지 않는다");
    }

    [Test]
    public void EachItem_CountsOnlyFirstTime()
    {
        var model = Started(1);
        Assert.That(model.Report(1, LobbyWeaponCheckItem.SkillQ), Is.True);
        Assert.That(model.Report(1, LobbyWeaponCheckItem.SkillQ), Is.False);
        Assert.That(LobbyWeaponCheckRules.CountOf(model.MaskOf(1)), Is.EqualTo(1));
    }

    [Test]
    public void Report_OnlyMarksTheActor()
    {
        var model = Started(1, 2);
        model.Report(1, LobbyWeaponCheckItem.BasicAttack);
        Assert.That(LobbyWeaponCheckRules.Has(model.MaskOf(1), LobbyWeaponCheckItem.BasicAttack), Is.True);
        Assert.That(model.MaskOf(2), Is.Zero, "다른 플레이어가 대신 완료할 수 없다");
    }

    [Test]
    public void LateJoiner_NotInRoster_IsIgnored()
    {
        var model = Started(1);
        Assert.That(model.Report(7, LobbyWeaponCheckItem.Dash), Is.False);
        Assert.That(model.IsInRoster(7), Is.False);
        Assert.That(model.Roster.Count, Is.EqualTo(1));
    }

    [Test]
    public void AllComplete_ReportedExactlyOnce()
    {
        var model = Started(1, 2);
        CompleteAll(model, 1);
        Assert.That(model.CompletedCount, Is.EqualTo(1));
        Assert.That(model.TryConsumeAllComplete(), Is.False, "한 명만 끝났다");

        CompleteAll(model, 2);
        Assert.That(model.IsAllComplete, Is.True);
        Assert.That(model.TryConsumeAllComplete(), Is.True);
        Assert.That(model.TryConsumeAllComplete(), Is.False, "보고는 1회");
    }

    [Test]
    public void NoTimeout_PartialNeverCompletes()
    {
        var model = Started(1);
        foreach (var item in new[] { LobbyWeaponCheckItem.BasicAttack, LobbyWeaponCheckItem.SkillQ, LobbyWeaponCheckItem.SkillE,
                     LobbyWeaponCheckItem.SkillR, LobbyWeaponCheckItem.RightClick })
            model.Report(1, item);

        Assert.That(model.IsAllComplete, Is.False, "대시가 남았다");
        Assert.That(model.TryConsumeAllComplete(), Is.False);
    }

    [Test]
    public void Disconnect_RemovesFromRoster_AndCanCompleteParty()
    {
        var model = Started(1, 2);
        CompleteAll(model, 1);
        Assert.That(model.RemoveClient(2), Is.True);
        Assert.That(model.TryConsumeAllComplete(), Is.True, "남은 사람만으로 완료");
        Assert.That(model.RemoveClient(2), Is.False);
    }

    [Test]
    public void Stop_IgnoresLaterReports_AndBeginRestartsClean()
    {
        var model = Started(1);
        model.Report(1, LobbyWeaponCheckItem.Dash);
        model.Stop();
        Assert.That(model.IsRunning, Is.False);
        Assert.That(model.Report(1, LobbyWeaponCheckItem.SkillR), Is.False);

        model.Begin(new ulong[] { 1 });
        Assert.That(model.MaskOf(1), Is.Zero);
    }

    [Test]
    public void EmptyRoster_NeverCompletes()
    {
        var model = Started();
        Assert.That(model.IsAllComplete, Is.False);
        Assert.That(model.TryConsumeAllComplete(), Is.False);
    }

    [Test]
    public void DuplicateRosterIds_CountOnce()
    {
        var model = Started(1, 1);
        Assert.That(model.Roster.Count, Is.EqualTo(1));
    }

    // ─── 판정 규칙 ───────────────────────────────────────────────────────

    [TestCase(PlayerSkillSlot.Main, LobbyWeaponCheckItem.SkillQ)]
    [TestCase(PlayerSkillSlot.Sub, LobbyWeaponCheckItem.SkillE)]
    [TestCase(PlayerSkillSlot.Ultimate, LobbyWeaponCheckItem.SkillR)]
    [TestCase(PlayerSkillSlot.Interrupt, LobbyWeaponCheckItem.RightClick)]
    public void SkillSlot_MapsToItem(PlayerSkillSlot slot, LobbyWeaponCheckItem expected)
    {
        Assert.That(LobbyWeaponCheckRules.TryGetApprovalItem(slot, false, out var item), Is.True);
        Assert.That(item, Is.EqualTo(expected));
    }

    [Test]
    public void FireCountedSkill_ApprovalIsIgnored()
    {
        Assert.That(LobbyWeaponCheckRules.TryGetApprovalItem(PlayerSkillSlot.Main, true, out _), Is.False, "거너 Q 는 발사 신호로 센다");
    }

    [Test]
    public void FullMask_HasSixItems()
    {
        Assert.That(LobbyWeaponCheckRules.DisplayOrder.Length, Is.EqualTo(LobbyWeaponCheckRules.ItemCount));
        Assert.That(LobbyWeaponCheckRules.CountOf(LobbyWeaponCheckRules.FullMask), Is.EqualTo(6));
        Assert.That(LobbyWeaponCheckRules.IsComplete(LobbyWeaponCheckRules.FullMask), Is.True);
        Assert.That(LobbyWeaponCheckRules.IsComplete(LobbyWeaponCheckRules.FullMask & ~LobbyWeaponCheckRules.BitOf(LobbyWeaponCheckItem.Dash)), Is.False);
    }

    [Test]
    public void Targets_OnlyDuringTrain01InProgress()
    {
        Assert.That(LobbyTrainingSupportRules.Train01TargetsActive(LobbyTrainingStage.Train01, LobbyTrainingPhase.InProgress), Is.True);
        Assert.That(LobbyTrainingSupportRules.Train01TargetsActive(LobbyTrainingStage.Train01, LobbyTrainingPhase.Gathering), Is.False, "시작 전");
        Assert.That(LobbyTrainingSupportRules.Train01TargetsActive(LobbyTrainingStage.Train02, LobbyTrainingPhase.Gathering), Is.False, "완료 뒤");
        Assert.That(LobbyTrainingSupportRules.Train01TargetsActive(LobbyTrainingStage.Lobby, LobbyTrainingPhase.Gathering), Is.False);
    }

    // ─── 훈련 리셋 ───────────────────────────────────────────────────────

    [Test]
    public void Reset_IncludesAllFourSlots()
    {
        foreach (PlayerSkillSlot slot in System.Enum.GetValues(typeof(PlayerSkillSlot)))
            Assert.That(TrainingResetRules.IncludesSlot(slot), Is.True, slot.ToString());
    }

    [Test]
    public void Reset_HealsToMax_ButNotTheDowned()
    {
        Assert.That(TrainingResetRules.HealAmount(30, 100), Is.EqualTo(70));
        Assert.That(TrainingResetRules.HealAmount(100, 100), Is.Zero);
        Assert.That(TrainingResetRules.HealAmount(0, 100), Is.Zero, "쓰러짐·사망은 회복으로 되살리지 않는다");
    }

    [Test]
    public void RageGrant_RaisesToMinTransformOnly()
    {
        Assert.That(TrainingResetRules.TryGetRageGrant(10f, 40f, false, out var target), Is.True);
        Assert.That(target, Is.EqualTo(40f));
        Assert.That(TrainingResetRules.TryGetRageGrant(55f, 40f, false, out target), Is.False, "이미 넘으면 깎지 않는다");
        Assert.That(target, Is.EqualTo(55f));
        Assert.That(TrainingResetRules.TryGetRageGrant(0f, 40f, true, out _), Is.False, "변신 중엔 지급 없음");
    }

    [Test]
    public void TrainingStartOptions_ResetEverything()
    {
        var options = TrainingResetOptions.TrainingStart;
        Assert.That(options.FullHealth && options.ResetCooldowns && options.RefillDash && options.GrantAssassinTransformRage, Is.True);
    }

    [Test]
    public void Reservation_FiresOnceWhenActionEnds()
    {
        var reservations = new TrainingResetReservations();
        reservations.OnReset(1, busy: true);
        reservations.OnReset(2, busy: false);
        Assert.That(reservations.IsReserved(1), Is.True);
        Assert.That(reservations.IsReserved(2), Is.False);

        Assert.That(reservations.Tick(1, busy: true), Is.False, "행동 중엔 기다린다");
        Assert.That(reservations.Tick(1, busy: false), Is.True);
        Assert.That(reservations.Tick(1, busy: false), Is.False, "한 번만");

        reservations.OnReset(1, busy: true);
        Assert.That(reservations.IsReserved(1), Is.True, "다음 리셋에서 다시 걸 수 있다");
        reservations.Forget(1);
        Assert.That(reservations.Count, Is.Zero);
    }

    [Test]
    public void Reservation_CopyTo_AllowsRemovalWhileIterating()
    {
        var reservations = new TrainingResetReservations();
        reservations.OnReset(1, true);
        reservations.OnReset(2, true);
        var ids = new List<ulong>();
        reservations.CopyTo(ids);
        foreach (var id in ids)
            reservations.Tick(id, false);
        Assert.That(reservations.Count, Is.Zero);
    }

    // ─── 훈련 보정 최소 체력 ─────────────────────────────────────────────

    [TestCase(LobbyTrainingStage.Lobby, 0)]
    [TestCase(LobbyTrainingStage.Train01, 1)]
    [TestCase(LobbyTrainingStage.Train02, 1)]
    [TestCase(LobbyTrainingStage.Train03A, 1)]
    [TestCase(LobbyTrainingStage.Train03B, 1)]
    [TestCase(LobbyTrainingStage.Train03C, 1)]
    [TestCase(LobbyTrainingStage.Train03D, 1)]
    [TestCase(LobbyTrainingStage.TrainEnd, 0)]
    [TestCase(LobbyTrainingStage.Brief, 0)]
    [TestCase(LobbyTrainingStage.Sortie, 0)]
    public void HealthFloor_OnlyDuringTrainingStages(LobbyTrainingStage stage, int expected)
    {
        Assert.That(LobbyTrainingSupportRules.HealthFloorFor(stage, isInTrainingRoster: true), Is.EqualTo(expected));
    }

    [Test]
    public void HealthFloor_LateJoinerHasNone()
    {
        Assert.That(LobbyTrainingSupportRules.HealthFloorFor(LobbyTrainingStage.Train01, isInTrainingRoster: false), Is.Zero);
    }

    [Test]
    public void HealthFloorClamp_StopsAtFloor()
    {
        Assert.That(HealthFloorRules.Clamp(50, 0, 1), Is.EqualTo(1), "치명타도 1 에서 멈춘다");
        Assert.That(HealthFloorRules.Clamp(50, 20, 1), Is.EqualTo(20), "하한 위면 그대로");
        Assert.That(HealthFloorRules.Clamp(1, 0, 1), Is.EqualTo(1), "1 에서 또 맞아도 1");
        Assert.That(HealthFloorRules.Clamp(50, 0, 0), Is.Zero, "하한 없음 = 실전 그대로");
    }

    [Test]
    public void HealthFloorClamp_NeverHeals()
    {
        Assert.That(HealthFloorRules.Clamp(3, 0, 5), Is.EqualTo(3), "피해 전보다 올리지 않는다");
    }
}
