using NUnit.Framework;

/// <summary>
/// 판 도중 재합류 생명 규칙(할 일 48 C14 R5c) — 퇴장 기록(DeadPresentation 목적지 확정·체력 비율), 복원 판정, 복원 체력.
/// </summary>
public sealed class PlayerRejoinLifeRulesTests
{
    // ─── 퇴장 기록 ─────────────────────────────────────────────────────

    [Test]
    public void 살아서_나가면_체력_비율을_기록한다()
    {
        var record = PlayerRejoinLifeRules.Capture(PlayerLifeState.Alive, 30, 120, partyLives: 5, keepsSoul: false);
        Assert.AreEqual(PlayerLifeState.Alive, record.State);
        Assert.AreEqual(0.25f, record.HpRatio, 1e-5f);
    }

    [Test]
    public void 사망_연출_중_퇴장은_공용_목숨이_남으면_Soul()
    {
        var record = PlayerRejoinLifeRules.Capture(PlayerLifeState.DeadPresentation, 0, 120, partyLives: 1, keepsSoul: false);
        Assert.AreEqual(PlayerLifeState.Soul, record.State);
        Assert.AreEqual(0f, record.HpRatio);
    }

    [Test]
    public void 사망_연출_중_퇴장은_공용_목숨이_0_이면_PermanentDead()
    {
        var record = PlayerRejoinLifeRules.Capture(PlayerLifeState.DeadPresentation, 0, 120, partyLives: 0, keepsSoul: false);
        Assert.AreEqual(PlayerLifeState.PermanentDead, record.State);
    }

    [Test]
    public void 사망_연출_중_퇴장은_차감_없는_부활이_걸렸으면_목숨_0_이어도_Soul()
    {
        var record = PlayerRejoinLifeRules.Capture(PlayerLifeState.DeadPresentation, 0, 120, partyLives: 0, keepsSoul: true);
        Assert.AreEqual(PlayerLifeState.Soul, record.State, "튜토리얼 G11 전 사망");
    }

    [Test]
    public void 체력_0_인데_아직_Alive_면_사망으로_본다()
    {
        // 연출 잠금 중 사망 신호 보류 — 0 비율 Alive 로 돌려보내지 않는다
        var record = PlayerRejoinLifeRules.Capture(PlayerLifeState.Alive, 0, 120, partyLives: 2, keepsSoul: false);
        Assert.AreEqual(PlayerLifeState.Soul, record.State);
    }

    [Test]
    public void Soul_과_PermanentDead_는_그대로_기록한다()
    {
        Assert.AreEqual(PlayerLifeState.Soul,
            PlayerRejoinLifeRules.Capture(PlayerLifeState.Soul, 0, 120, 0, false).State, "목숨 0 이어도 이미 Soul 이면 Soul");
        Assert.AreEqual(PlayerLifeState.PermanentDead,
            PlayerRejoinLifeRules.Capture(PlayerLifeState.PermanentDead, 0, 120, 5, false).State, "목숨이 있어도 PermanentDead 그대로");
    }

    [Test]
    public void 최대_체력을_모르면_비율_1()
    {
        Assert.AreEqual(1f, PlayerRejoinLifeRules.Capture(PlayerLifeState.Alive, 0, 0, 5, false).HpRatio);
    }

    [Test]
    public void 비율은_0에서_1로_자르고_NaN_은_1()
    {
        Assert.AreEqual(1f, new PlayerRejoinLifeRecord(PlayerLifeState.Alive, 1.7f).HpRatio);
        Assert.AreEqual(0f, new PlayerRejoinLifeRecord(PlayerLifeState.Alive, -0.2f).HpRatio);
        Assert.AreEqual(1f, new PlayerRejoinLifeRecord(PlayerLifeState.Alive, float.NaN).HpRatio);
        Assert.AreEqual(0f, new PlayerRejoinLifeRecord(PlayerLifeState.Soul, 0.8f).HpRatio, "죽은 기록의 비율은 0");
    }

    // ─── 복원 ──────────────────────────────────────────────────────────

    [Test]
    public void 복원_판정은_상태별로_체력_비율_Soul_PermanentDead()
    {
        Assert.AreEqual(PlayerRejoinLifeRestore.HealthRatio, PlayerRejoinLifeRules.PlanRestore(PlayerLifeState.Alive));
        Assert.AreEqual(PlayerRejoinLifeRestore.Soul, PlayerRejoinLifeRules.PlanRestore(PlayerLifeState.Soul));
        Assert.AreEqual(PlayerRejoinLifeRestore.PermanentDead, PlayerRejoinLifeRules.PlanRestore(PlayerLifeState.PermanentDead));
        Assert.AreEqual(PlayerRejoinLifeRestore.Soul, PlayerRejoinLifeRules.PlanRestore(PlayerLifeState.DeadPresentation),
            "기록 규칙을 거치지 않은 사망 연출은 부활 가능한 쪽");
    }

    [Test]
    public void 살아_있으면_복원_체력은_비율_반올림_최소_1()
    {
        Assert.AreEqual(30, PlayerRejoinLifeRules.ResolveRestoreHealth(PlayerRejoinLifeRestore.HealthRatio, 0.25f, 120));
        Assert.AreEqual(1, PlayerRejoinLifeRules.ResolveRestoreHealth(PlayerRejoinLifeRestore.HealthRatio, 0.001f, 120), "살아 있는데 0 이 되지 않는다");
        Assert.AreEqual(1, PlayerRejoinLifeRules.ResolveRestoreHealth(PlayerRejoinLifeRestore.HealthRatio, 0f, 120));
        Assert.AreEqual(120, PlayerRejoinLifeRules.ResolveRestoreHealth(PlayerRejoinLifeRestore.HealthRatio, 1f, 120));
        Assert.AreEqual(61, PlayerRejoinLifeRules.ResolveRestoreHealth(PlayerRejoinLifeRestore.HealthRatio, 0.5f, 121), "0.5 는 올림");
    }

    [Test]
    public void 죽은_복원과_최대_0_은_체력_0()
    {
        Assert.AreEqual(0, PlayerRejoinLifeRules.ResolveRestoreHealth(PlayerRejoinLifeRestore.Soul, 1f, 120));
        Assert.AreEqual(0, PlayerRejoinLifeRules.ResolveRestoreHealth(PlayerRejoinLifeRestore.PermanentDead, 1f, 120));
        Assert.AreEqual(0, PlayerRejoinLifeRules.ResolveRestoreHealth(PlayerRejoinLifeRestore.HealthRatio, 1f, 0));
    }

    [Test]
    public void 전멸_리스폰_덮어쓰기_값은_Alive_만피()
    {
        var full = PlayerRejoinLifeRecord.AliveFull;
        Assert.AreEqual(PlayerLifeState.Alive, full.State);
        Assert.AreEqual(1f, full.HpRatio);
        Assert.AreEqual(PlayerRejoinLifeRestore.HealthRatio, PlayerRejoinLifeRules.PlanRestore(full.State));
        Assert.AreEqual(120, PlayerRejoinLifeRules.ResolveRestoreHealth(PlayerRejoinLifeRestore.HealthRatio, full.HpRatio, 120));
    }
}
