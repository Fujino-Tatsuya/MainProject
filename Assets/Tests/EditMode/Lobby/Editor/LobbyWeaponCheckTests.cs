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

    // ─── 할 일 157 ⑤ — 명세 §5 T10 고정 ───────────────────────────────────

    [Test]
    public void T10_순서_자유()
    {
        var model = Started(1);
        var reversed = (LobbyWeaponCheckItem[])LobbyWeaponCheckRules.DisplayOrder.Clone();
        System.Array.Reverse(reversed);
        foreach (var item in reversed)
            model.Report(1, item);

        Assert.That(LobbyWeaponCheckRules.IsComplete(model.MaskOf(1)), Is.True);
    }

    [Test]
    public void T10_완료자는_연습을_계속해도_기록_그대로_다음_단계는_전원_완료_때만()
    {
        var model = Started(1, 2);
        CompleteAll(model, 1);

        foreach (var item in LobbyWeaponCheckRules.DisplayOrder)
            Assert.That(model.Report(1, item), Is.False, "완료 뒤 행동은 새 체크 없음(막지도 않음)");
        Assert.That(model.IsRunning, Is.True, "완료자 연습 중에도 진행 중");
        Assert.That(model.MaskOf(1), Is.EqualTo(LobbyWeaponCheckRules.FullMask));
        Assert.That(model.CompletedCount, Is.EqualTo(1), "하단 '훈련 완료 n/N' 의 n");
        Assert.That(model.TryConsumeAllComplete(), Is.False, "전원 완료 전 다음 단계 안 열림");
    }

    [Test]
    public void T10_캐릭터_교체_뒤에도_체크_마스크_유지()
    {
        // PLAN §10.2-4: 교체 = 같은 clientId 의 PlayerObject 디스폰·재스폰 — 장부는 clientId 키라 교체 경로는 모델을 부르지 않는다(연결 종료·클리어 플래그만 RemoveClient).
        var model = Started(1);
        model.Report(1, LobbyWeaponCheckItem.BasicAttack);
        model.Report(1, LobbyWeaponCheckItem.SkillQ);

        // (교체) 새 캐릭터의 행동도 같은 clientId 로 온다
        Assert.That(model.Report(1, LobbyWeaponCheckItem.SkillQ), Is.False, "이미 끝낸 항목은 다시 할 필요 없음");
        Assert.That(model.Report(1, LobbyWeaponCheckItem.SkillE), Is.True);
        Assert.That(LobbyWeaponCheckRules.CountOf(model.MaskOf(1)), Is.EqualTo(3));
        Assert.That(model.IsInRoster(1), Is.True);
    }

    [TestCase(PlayerSkillSlot.Main)]
    [TestCase(PlayerSkillSlot.Sub)]
    public void T10_변신_Q_E_는_키별_한_항목(PlayerSkillSlot slot)
    {
        // 변신 스킬(alternateSkills)은 같은 PlayerSkillSlot 으로 승인된다 → 일반·변신이 같은 항목.
        var model = Started(1);
        Assert.That(LobbyWeaponCheckRules.TryGetApprovalItem(slot, false, out var normal), Is.True);
        Assert.That(model.Report(1, normal), Is.True);
        Assert.That(LobbyWeaponCheckRules.TryGetApprovalItem(slot, false, out var transformed), Is.True);
        Assert.That(model.Report(1, transformed), Is.False);
        Assert.That(LobbyWeaponCheckRules.CountOf(model.MaskOf(1)), Is.EqualTo(1));
    }

    [Test]
    public void T10_항목은_6개뿐_패시브_WASD_없음()
    {
        Assert.That(System.Enum.GetValues(typeof(LobbyWeaponCheckItem)).Length, Is.EqualTo(6));

        var model = Started(1);
        Assert.That(model.Report(1, (LobbyWeaponCheckItem)6), Is.False, "표에 없는 항목(패시브·이동 등)은 기록되지 않는다");
        Assert.That(model.MaskOf(1), Is.Zero);
    }

    [Test]
    public void T10_표시_순서는_항목_값_순서()
    {
        // 체크리스트 키 i 번째 = (LobbyWeaponCheckItem)i 비트(계약 문서 §2 TryGetLocalChecklist).
        for (var i = 0; i < LobbyWeaponCheckRules.DisplayOrder.Length; i++)
            Assert.That((int)LobbyWeaponCheckRules.DisplayOrder[i], Is.EqualTo(i));
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
    public void Targets_OnlyDuringT10BasicCheck()
    {
        // 할 일 157 ②: 단계 = 단계표 Activity. L21 대사·T20 집결(완료 뒤)은 Activity None.
        Assert.That(LobbyTrainingSupportRules.Train01TargetsActive(LobbyTutorialActivity.BasicCheck), Is.True);
        Assert.That(LobbyTrainingSupportRules.Train01TargetsActive(LobbyTutorialActivity.None), Is.False, "시작 전·완료 뒤 집결");
        Assert.That(LobbyTrainingSupportRules.Train01TargetsActive(LobbyTutorialActivity.Combat), Is.False);
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

    // 단계별 범위(행 trainingProfile)는 LobbyTutorialRulesTests(실제 단계표) — 여기는 프로필 규칙만.
    [TestCase(LobbyTutorialTrainingProfile.None, 0)]
    [TestCase(LobbyTutorialTrainingProfile.Training, 1)]
    public void HealthFloor_FollowsRowTrainingProfile(LobbyTutorialTrainingProfile profile, int expected)
    {
        var step = new LobbyTutorialStep { trainingProfile = profile };
        Assert.That(LobbyTutorialRules.HealthFloorFor(step, inLockedRoster: true), Is.EqualTo(expected));
    }

    [Test]
    public void HealthFloor_LateJoinerHasNone()
    {
        var step = new LobbyTutorialStep { trainingProfile = LobbyTutorialTrainingProfile.Training };
        Assert.That(LobbyTutorialRules.HealthFloorFor(step, inLockedRoster: false), Is.Zero);
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
