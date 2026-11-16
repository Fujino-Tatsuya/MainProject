using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 로비 브리핑 영상 순수 규칙(할 일 292) — 길이·재생 위치(늦은 참가자)·다시 맞춤·건너뛰기 표 받기·'n/m' 글자,
/// 그리고 표 집계(<see cref="BossIntroSkipVotes"/>, 보스 등장 연출과 같은 규칙)를 로비 조건(접속자 전원·이탈·늦은 참가)으로 확인한다.
/// </summary>
public sealed class LobbyBriefingVideoTests
{
    // ── 길이 ──

    [Test]
    public void 길이는_클립_길이가_있으면_그것()
    {
        Assert.That(LobbyBriefingVideoRules.VideoSeconds(57.03d, 30f), Is.EqualTo(57.03d));
    }

    [Test]
    public void 클립이_없으면_행_칸_음수는_0()
    {
        Assert.That(LobbyBriefingVideoRules.VideoSeconds(0d, 57f), Is.EqualTo(57d));
        Assert.That(LobbyBriefingVideoRules.VideoSeconds(-1d, -3f), Is.EqualTo(0d));
    }

    [Test]
    public void 대사와_영상_모두_타이머_행이고_나머지는_아니다()
    {
        Assert.That(LobbyBriefingRules.IsTimed(LobbyTutorialStepKind.Dialogue), Is.True);
        Assert.That(LobbyBriefingRules.IsTimed(LobbyTutorialStepKind.Video), Is.True);
        Assert.That(LobbyBriefingRules.IsTimed(LobbyTutorialStepKind.Gather), Is.False);
        Assert.That(LobbyBriefingRules.IsTimed(LobbyTutorialStepKind.Activity), Is.False);
        Assert.That(LobbyBriefingRules.IsTimed(LobbyTutorialStepKind.Loading), Is.False);
        Assert.That(LobbyBriefingRules.IsTimed(LobbyTutorialStepKind.LockerVisit), Is.False);
    }

    [Test]
    public void 잠금_영상_행도_브리핑이고_잠금_없는_영상은_아니다()
    {
        var locked = new LobbyTutorialStep { stateId = "V", kind = LobbyTutorialStepKind.Video, locksInput = true };
        var free = new LobbyTutorialStep { stateId = "V2", kind = LobbyTutorialStepKind.Video, locksInput = false };

        Assert.That(LobbyBriefingRules.IsBriefing(new LobbyTutorialStepView(0, locked)), Is.True);
        Assert.That(LobbyBriefingRules.IsBriefing(new LobbyTutorialStepView(0, free)), Is.False);
    }

    // ── 재생 위치 ──

    [Test]
    public void 시작_시각이_아직_없으면_기다린다()
    {
        Assert.That(LobbyBriefingVideoRules.PlaybackTime(100d, 0d, 57d), Is.EqualTo(-1d));
    }

    [Test]
    public void 재생_위치는_서버_시각_빼기_시작_늦은_참가자는_중간부터()
    {
        Assert.That(LobbyBriefingVideoRules.PlaybackTime(100d, 100d, 57d), Is.EqualTo(0d), "제때 = 처음부터");
        Assert.That(LobbyBriefingVideoRules.PlaybackTime(130.5d, 100d, 57d), Is.EqualTo(30.5d), "30.5초 늦게 붙음 = 30.5초부터");
    }

    [Test]
    public void 재생_위치는_0_과_길이_사이로_자른다()
    {
        Assert.That(LobbyBriefingVideoRules.PlaybackTime(99.9d, 100d, 57d), Is.EqualTo(0d), "서버 시각 추정이 살짝 앞");
        Assert.That(LobbyBriefingVideoRules.PlaybackTime(200d, 100d, 57d), Is.EqualTo(57d));
    }

    [Test]
    public void 다시_맞춤은_1_5초_넘게_벌어졌을_때만()
    {
        Assert.That(LobbyBriefingVideoRules.NeedsResync(10d, 11.4d), Is.False);
        Assert.That(LobbyBriefingVideoRules.NeedsResync(10d, 11.6d), Is.True);
        Assert.That(LobbyBriefingVideoRules.NeedsResync(12d, 10d), Is.True, "앞서도 맞춘다");
    }

    // ── 건너뛰기 표 받기 ──

    [Test]
    public void 표는_지금_영상의_회차_revision_만_받는다()
    {
        Assert.That(LobbyBriefingVideoRules.AcceptsSkipVote(LobbyTutorialStepKind.Video, 3, 40, 3, 40, false), Is.True);
        Assert.That(LobbyBriefingVideoRules.AcceptsSkipVote(LobbyTutorialStepKind.Video, 3, 41, 3, 40, false), Is.False, "옛 revision");
        Assert.That(LobbyBriefingVideoRules.AcceptsSkipVote(LobbyTutorialStepKind.Video, 4, 40, 3, 40, false), Is.False, "옛 회차");
        Assert.That(LobbyBriefingVideoRules.AcceptsSkipVote(LobbyTutorialStepKind.Dialogue, 3, 40, 3, 40, false), Is.False, "영상 행 아님(L34)");
        Assert.That(LobbyBriefingVideoRules.AcceptsSkipVote(LobbyTutorialStepKind.Video, 3, 40, 3, 40, true), Is.False, "다음 행 전이 대기 중");
    }

    [Test]
    public void 표시_글자는_n_slash_m_이고_투표자_0명이면_비운다()
    {
        Assert.That(LobbyBriefingVideoRules.CountText(1, 3), Is.EqualTo("1/3"));
        Assert.That(LobbyBriefingVideoRules.CountText(0, 1), Is.EqualTo("0/1"));
        Assert.That(LobbyBriefingVideoRules.CountText(4, 3), Is.EqualTo("3/3"), "이탈 직후 한 프레임 — 분자가 분모를 넘지 않게");
        Assert.That(LobbyBriefingVideoRules.CountText(0, 0), Is.Empty);
    }

    // ── 표 집계(접속자 전원 만장일치) ──

    [Test]
    public void 혼자면_Esc_한_번에_건너뛴다()
    {
        var voters = new List<ulong> { 0 };
        var votes = new BossIntroSkipVotes();

        Assert.That(votes.TryVote(0, voters), Is.True);
        Assert.That(votes.IsUnanimous(voters), Is.True);
    }

    [Test]
    public void 일인_일표_다시_눌러도_한_표_명단_밖은_못_낸다()
    {
        var voters = new List<ulong> { 0, 1, 2 };
        var votes = new BossIntroSkipVotes();

        Assert.That(votes.TryVote(1, voters), Is.True);
        Assert.That(votes.TryVote(1, voters), Is.False, "같은 사람 두 번째 Esc");
        Assert.That(votes.TryVote(9, voters), Is.False, "접속자 아님");
        Assert.That(votes.Count, Is.EqualTo(1));
        Assert.That(votes.IsUnanimous(voters), Is.False);
    }

    [Test]
    public void 안_누른_사람이_나가면_남은_전원_동의로_건너뛴다()
    {
        var voters = new List<ulong> { 0, 1, 2 };
        var votes = new BossIntroSkipVotes();
        votes.TryVote(0, voters);
        votes.TryVote(1, voters);
        Assert.That(votes.IsUnanimous(voters), Is.False, "2/3");

        voters.Remove(2);   // 이탈 — 매 프레임 접속자 전원으로 다시 맞춘다
        Assert.That(votes.Prune(voters), Is.False, "나간 사람은 표가 없었다");
        Assert.That(votes.IsUnanimous(voters), Is.True, "2/2");
    }

    [Test]
    public void 누른_사람이_나가면_그_표도_빠진다()
    {
        var voters = new List<ulong> { 0, 1 };
        var votes = new BossIntroSkipVotes();
        votes.TryVote(1, voters);

        voters.Remove(1);
        Assert.That(votes.Prune(voters), Is.True);
        Assert.That(votes.Count, Is.EqualTo(0));
        Assert.That(votes.IsUnanimous(voters), Is.False, "0/1");
    }

    [Test]
    public void 늦은_참가자가_오면_분모가_늘어_다시_기다린다()
    {
        var voters = new List<ulong> { 0 };
        var votes = new BossIntroSkipVotes();
        voters.Add(1);   // 영상 중 접속 — 투표자에 들어간다
        votes.TryVote(0, voters);

        Assert.That(votes.IsUnanimous(voters), Is.False, "1/2 — 늦은 참가자도 동의해야 한다");
        Assert.That(votes.TryVote(1, voters), Is.True);
        Assert.That(votes.IsUnanimous(voters), Is.True);
    }

    [Test]
    public void 접속자_0명이면_건너뛰지_않는다()
    {
        Assert.That(new BossIntroSkipVotes().IsUnanimous(new List<ulong>()), Is.False);
    }
}
