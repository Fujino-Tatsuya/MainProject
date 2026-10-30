using NUnit.Framework;

/// <summary>간파 훈련 T32·T34 "새 시전" 판정 조합 — 할 일 157 ⑦(PLAN §2.5·§10.2-5, 명세 §5 T32·T34·§10 수용 기준 6·7·8).</summary>
public sealed class LobbyCastWindowTests
{
    const ulong Host = 0;
    const ulong Guest = 1;
    const int HostPlayer = 1001;      // Player 인스턴스 식별
    const int GuestPlayer = 2001;
    const int GuestSwapped = 2002;    // 교체로 생긴 새 인스턴스
    const int Start = 100;            // 판정 시작 프레임

    static LobbyCastWindow Open(LobbyCastWindowMode mode)
    {
        var w = new LobbyCastWindow();
        w.Reset();
        w.Begin(mode, Start);
        w.SetBaseline(Host, HostPlayer, 5);    // 진입 때 호스트는 시전 5까지 냈다
        w.SetBaseline(Guest, GuestPlayer, 3);
        return w;
    }

    // ── 시전 기준선 ──

    [Test]
    public void NewCast_IsAboveBaseline_SwappedInstanceStartsFromZero()
    {
        var w = Open(LobbyCastWindowMode.CrowdControl);
        Assert.That(w.IsNewCast(Host, HostPlayer, 5), Is.False);
        Assert.That(w.IsNewCast(Host, HostPlayer, 6), Is.True);
        Assert.That(w.IsNewCast(Guest, GuestSwapped, 1), Is.True, "교체로 생긴 새 인스턴스 = 기준선 0");
        Assert.That(w.IsNewCast(7, 7001, 1), Is.True, "진입 때 없던 사람 = 기준선 0");
    }

    // ── T32 CC ──

    [Test]
    public void Cc_NewCastSameFrame_Accepted()
    {
        var w = Open(LobbyCastWindowMode.CrowdControl);
        w.RecordCrowdControl(101, 1, TrainingSparringCrowdControl.Knockback, applied: true);
        w.RecordLanded(101, Host, HostPlayer, 6, tagOk: true);   // 순서 무관 — 적중 통지가 CC 뒤에 와도 된다

        Assert.That(w.Resolve(101).Any, Is.False, "지금 프레임은 짝이 덜 왔을 수 있어 기다린다");
        var r = w.Resolve(102);
        Assert.That(r.CrowdControlAccepted, Is.True);
        Assert.That(r.EventFrame, Is.EqualTo(101));
        Assert.That(r.ClientId, Is.EqualTo(Host));
        Assert.That(r.CastId, Is.EqualTo(6));
        Assert.That(w.Resolve(103).Any, Is.False, "처리한 사건은 지운다");
    }

    [Test]
    public void Cc_PreviousCastResidualHit_NotAccepted()
    {
        var w = Open(LobbyCastWindowMode.CrowdControl);
        w.RecordLanded(101, Host, HostPlayer, 5, true);   // 진입 전 시전(5)의 늦은 타격
        w.RecordCrowdControl(101, 1, TrainingSparringCrowdControl.Knockback, true);
        Assert.That(w.Resolve(102).CrowdControlAccepted, Is.False);
    }

    [Test]
    public void Cc_AutoInterruptKnockback_NotAccepted_ByBaselineOnly()
    {
        // 간파 성공은 T31 에서만 난다 — 그 시전(5)은 승인 때 ID 를 받아 T32 진입 기준선(5) 이하다. 따로 '간파 성공 시전' 기록 없이 기준선만으로 걸러진다(⑧).
        var w = Open(LobbyCastWindowMode.CrowdControl);
        w.RecordLanded(103, Host, HostPlayer, 5, true);   // 간파 시전의 딸린 넉백(다음 프레임)
        w.RecordCrowdControl(103, 1, TrainingSparringCrowdControl.Knockback, true);
        Assert.That(w.Resolve(104).CrowdControlAccepted, Is.False, "간파 성공에 딸린 자동 넉백 = 별도 CC 아님");

        // 같은 사람의 새 시전은 인정.
        w.RecordLanded(105, Host, HostPlayer, 6, true);
        w.RecordCrowdControl(105, 2, TrainingSparringCrowdControl.Knockback, true);
        Assert.That(w.Resolve(106).CrowdControlAccepted, Is.True);
    }

    [Test]
    public void Cc_ImmuneOrRejected_NotAccepted()
    {
        var w = Open(LobbyCastWindowMode.CrowdControl);
        w.RecordLanded(101, Host, HostPlayer, 6, true);
        w.RecordCrowdControl(101, 0, TrainingSparringCrowdControl.Stun, applied: false);   // 면역으로 무시된 적중
        Assert.That(w.Resolve(102).CrowdControlAccepted, Is.False);
    }

    [Test]
    public void Cc_NoPairedHit_NotAccepted()
    {
        var w = Open(LobbyCastWindowMode.CrowdControl);
        w.RecordLanded(101, Host, HostPlayer, 6, true);
        w.RecordCrowdControl(102, 1, TrainingSparringCrowdControl.Knockback, true);   // 다른 프레임 — 지연 효과
        Assert.That(w.Resolve(103).CrowdControlAccepted, Is.False);
    }

    [Test]
    public void Cc_OtherPlayer_Accepted()
    {
        var w = Open(LobbyCastWindowMode.CrowdControl);
        // 간파는 호스트가(진입 전), CC 는 게스트가.
        w.RecordLanded(90, Host, HostPlayer, 5, true);
        w.RecordLanded(101, Guest, GuestPlayer, 4, true);
        w.RecordCrowdControl(101, 0, TrainingSparringCrowdControl.Stun, true);
        var r = w.Resolve(102);
        Assert.That(r.CrowdControlAccepted, Is.True);
        Assert.That(r.ClientId, Is.EqualTo(Guest));
    }

    [Test]
    public void Cc_SwappedCharacterNewInstance_Accepted()
    {
        var w = Open(LobbyCastWindowMode.CrowdControl);
        // 게스트가 진입 뒤 캐릭터를 바꿨다 — 새 인스턴스는 seq 1 부터. 기준선(3)보다 작아도 새 시전.
        w.RecordLanded(101, Guest, GuestSwapped, 1, true);
        w.RecordCrowdControl(101, 1, TrainingSparringCrowdControl.Knockback, true);
        Assert.That(w.Resolve(102).CrowdControlAccepted, Is.True);
    }

    [Test]
    public void Cc_AtOrBeforeStartFrame_NotAccepted()
    {
        var w = Open(LobbyCastWindowMode.CrowdControl);
        w.RecordLanded(Start, Host, HostPlayer, 6, true);
        w.RecordCrowdControl(Start, 1, TrainingSparringCrowdControl.Knockback, true);
        Assert.That(w.Resolve(Start + 1).CrowdControlAccepted, Is.False, "진입 프레임 사건 = 같은 프레임 연쇄 금지");
    }

    [Test]
    public void Cc_TagRule_OnlyWhenEnforced()
    {
        var w = Open(LobbyCastWindowMode.CrowdControl);
        w.RecordLanded(101, Host, HostPlayer, 6, tagOk: false);
        w.RecordCrowdControl(101, 1, TrainingSparringCrowdControl.Knockback, true);
        Assert.That(w.Resolve(102).CrowdControlAccepted, Is.False, "태그 검사 실패한 짝은 쓰지 않는다");

        var none = System.Array.Empty<SkillTooltipTag>();
        var kbStun = new[] { SkillTooltipTag.Knockback, SkillTooltipTag.Stun };
        Assert.That(LobbyCastWindow.PassesTagRule(false, kbStun, none), Is.True, "검사 꺼짐(기본) = 통과");
        Assert.That(LobbyCastWindow.PassesTagRule(true, none, none), Is.True, "요구 태그 없음 = 통과");
        Assert.That(LobbyCastWindow.PassesTagRule(true, kbStun, none), Is.False, "출처 태그 없음 = 실패");
        Assert.That(LobbyCastWindow.PassesTagRule(true, kbStun, new[] { SkillTooltipTag.Heal, SkillTooltipTag.Stun }), Is.True);
        Assert.That(LobbyCastWindow.PassesTagRule(true, new[] { SkillTooltipTag.Knockback }, new[] { SkillTooltipTag.Stun }), Is.False);
    }

    [Test]
    public void ModeNone_NothingAccepted()
    {
        var w = new LobbyCastWindow();
        w.Reset();   // T31 — 판정 없음
        w.RecordLanded(50, Host, HostPlayer, 9, true);
        w.RecordCrowdControl(50, 1, TrainingSparringCrowdControl.Knockback, true);
        Assert.That(w.Resolve(51).Any, Is.False);
    }

    // ── T34 벽 ──

    [Test]
    public void Wall_DesignatedImpactOfNewCastKnockback_Accepted()
    {
        var w = Open(LobbyCastWindowMode.Wall);
        w.RecordLanded(101, Host, HostPlayer, 6, true);
        w.RecordCrowdControl(101, 4, TrainingSparringCrowdControl.Knockback, true);
        Assert.That(w.Resolve(102).Any, Is.False, "CC 자체는 T34 완료가 아니다");

        w.RecordWallImpact(110, 4, designated: true);
        w.RecordKnockbackEnded(115, 4);
        var r = w.Resolve(116);
        Assert.That(r.WallAccepted, Is.True);
        Assert.That(r.RetryRequested, Is.False, "같은 결과에 인정이 있으면 재시도 아님");
    }

    [Test]
    public void Wall_OtherWall_NotAccepted_RetryRequested()
    {
        var w = Open(LobbyCastWindowMode.Wall);
        w.RecordLanded(101, Host, HostPlayer, 6, true);
        w.RecordCrowdControl(101, 4, TrainingSparringCrowdControl.Knockback, true);
        w.RecordWallImpact(110, 4, designated: false);   // 장식 벽
        w.RecordKnockbackEnded(112, 4);
        var r = w.Resolve(113);
        Assert.That(r.WallAccepted, Is.False);
        Assert.That(r.RetryRequested, Is.True, "넉백이 끝났는데 지정 벽 충돌 없음 → 위치 복구 후 재시도");
    }

    [Test]
    public void Wall_MissThenRetrySucceeds()
    {
        var w = Open(LobbyCastWindowMode.Wall);
        w.RecordLanded(101, Host, HostPlayer, 6, true);
        w.RecordCrowdControl(101, 4, TrainingSparringCrowdControl.Knockback, true);
        w.RecordKnockbackEnded(108, 4);   // 아무 벽에도 안 닿음
        Assert.That(w.Resolve(109).RetryRequested, Is.True);

        w.RecordLanded(120, Host, HostPlayer, 7, true);
        w.RecordCrowdControl(120, 5, TrainingSparringCrowdControl.Knockback, true);
        w.RecordWallImpact(125, 5, true);
        Assert.That(w.Resolve(126).WallAccepted, Is.True);
    }

    [Test]
    public void Wall_ResidualCastKnockback_NotAccepted()
    {
        var w = Open(LobbyCastWindowMode.Wall);
        w.RecordLanded(101, Host, HostPlayer, 5, true);   // 진입 전 시전
        w.RecordCrowdControl(101, 4, TrainingSparringCrowdControl.Knockback, true);
        w.RecordWallImpact(105, 4, true);
        var r = w.Resolve(106);
        Assert.That(r.WallAccepted, Is.False, "이전 시전 잔여 넉백의 벽 충돌 불인정");
    }

    [Test]
    public void Wall_StunOnlyNextToWall_NotAccepted()
    {
        var w = Open(LobbyCastWindowMode.Wall);
        w.RecordLanded(101, Host, HostPlayer, 6, true);
        w.RecordCrowdControl(101, 0, TrainingSparringCrowdControl.Stun, true);   // 벽 옆에서 기절만
        var r = w.Resolve(102);
        Assert.That(r.WallAccepted, Is.False);
        Assert.That(r.CrowdControlAccepted, Is.False, "T34 엔 CC 완료가 없다");
    }

    [Test]
    public void Wall_EpisodeCreditDoesNotCarryIntoNextStage()
    {
        var w = Open(LobbyCastWindowMode.CrowdControl);
        w.RecordLanded(101, Host, HostPlayer, 6, true);
        w.RecordCrowdControl(101, 4, TrainingSparringCrowdControl.Knockback, true);
        Assert.That(w.Resolve(102).CrowdControlAccepted, Is.True);

        w.Begin(LobbyCastWindowMode.Wall, 200);   // T34 — 새 기준선
        w.SetBaseline(Host, HostPlayer, 6);
        w.RecordWallImpact(205, 4, true);         // T32 의 넉백 에피소드가 남아 벽에 닿아도
        Assert.That(w.Resolve(206).WallAccepted, Is.False);
    }
}
