using System;
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>튜토리얼 특성 선택 규칙 고정 (할 일 15 T1) — 후보 뽑기·중복 제외·선택 검증·자동 획득·라운드 완료.</summary>
public sealed class TutorialTraitSelectionModelTests
{
    const ulong Host = 0;
    const ulong Client1 = 1;

    // 항상 첫 칸을 고르는 난수 — 결과가 풀 순서 그대로라 기대값을 적기 쉽다.
    static readonly Func<int, int> First = _ => 0;

    static Func<int, int> Sequence(params int[] values)
    {
        int i = 0;
        return _ => values[i++ % values.Length];
    }

    [Test]
    public void OfferSeq_ChangesOnlyForThePlayerWhoGetsANewOffer()
    {
        // 할 일 30 4단계 — 창은 이 번호로 열고 닫는다. 다른 사람의 제시·선택·재선택에는 변하지 않는다.
        var model = new TutorialTraitSelectionModel(12);
        Assert.That(model.GetOfferSeq(Host), Is.EqualTo(0), "제시받은 적 없음");

        model.StartRound(new[] { Host }, 3, First);
        int hostSeq = model.GetOfferSeq(Host);
        Assert.That(hostSeq, Is.GreaterThan(0));

        model.AddParticipant(Client1, First);
        Assert.That(model.GetOfferSeq(Host), Is.EqualTo(hostSeq), "늦은 참가자 추가");
        model.TryChoose(Client1, model.GetOffered(Client1)[0]);
        model.StartPersonalRound(Client1, 3, First);
        Assert.That(model.GetOfferSeq(Host), Is.EqualTo(hostSeq), "다른 사람의 선택·재선택");
        Assert.That(model.Round, Is.EqualTo(1), "개인 라운드는 전역 라운드를 올리지 않는다");

        model.TryChoose(Host, model.GetOffered(Host)[0]);
        Assert.That(model.GetOfferSeq(Host), Is.EqualTo(hostSeq), "고른 뒤에도 번호는 남는다(창 닫힘은 제시 행으로)");

        model.StartRound(new[] { Host, Client1 }, 3, First);
        Assert.That(model.GetOfferSeq(Host), Is.GreaterThan(hostSeq), "새 복도 라운드 = 새 제시");
        Assert.That(model.GetOfferSeq(Client1), Is.Not.EqualTo(model.GetOfferSeq(Host)), "번호는 모델 전체에서 단조 증가");
        Assert.IsFalse(model.IsPersonalRound(Client1), "복도 라운드가 재선택 표시를 지운다");
    }

    [Test]
    public void PickCandidates_TakesCount_WithoutDuplicates()
    {
        var result = new List<int>();
        TutorialTraitSelectionModel.PickCandidates(6, null, null, 3, new Random(7).Next, result);

        Assert.That(result.Count, Is.EqualTo(3));
        Assert.That(result, Is.Unique);
        Assert.That(result, Is.All.InRange(0, 5));
    }

    [Test]
    public void PickCandidates_SkipsExcludedAndUnusable()
    {
        var result = new List<int>();
        TutorialTraitSelectionModel.PickCandidates(5, new[] { 0, 2 }, new[] { 4 }, 3, First, result);

        Assert.That(result, Is.EquivalentTo(new[] { 1, 3 }), "남은 2장만 제시");
    }

    [Test]
    public void PickCandidates_UsesInjectedRandom()
    {
        var result = new List<int>();
        // [0,1,2,3] 에서 i=0 → 0+2 = 2 와 교환, i=1 → 1+0 그대로
        TutorialTraitSelectionModel.PickCandidates(4, null, null, 2, Sequence(2, 0), result);

        Assert.That(result, Is.EqualTo(new[] { 2, 1 }));
    }

    [Test]
    public void PickCandidates_OutOfRangeRandom_IsClamped()
    {
        var result = new List<int>();
        TutorialTraitSelectionModel.PickCandidates(3, null, null, 3, _ => 99, result);

        Assert.That(result, Is.EquivalentTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void StartRound_OffersPerPlayer_AndIncrementsRound()
    {
        var model = new TutorialTraitSelectionModel(6);
        bool any = model.StartRound(new[] { Host, Client1 }, 3, First);

        Assert.That(any, Is.True);
        Assert.That(model.Round, Is.EqualTo(1));
        Assert.That(model.GetOffered(Host), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(model.GetOffered(Client1), Is.EqualTo(new[] { 0, 1, 2 }), "플레이어별 독립 — 같은 특성을 둘 다 받을 수 있다");
        Assert.That(model.HasPendingChoice(Host), Is.True);
        Assert.That(model.IsRoundComplete(), Is.False);
    }

    [Test]
    public void TryChoose_AcceptsOfferedOnce()
    {
        var model = new TutorialTraitSelectionModel(6);
        model.StartRound(new[] { Host }, 3, First);

        Assert.That(model.TryChoose(Host, 1), Is.EqualTo(TutorialTraitChoiceResult.Accepted));
        Assert.That(model.GetAcquired(Host), Is.EqualTo(new[] { 1 }));
        Assert.That(model.HasPendingChoice(Host), Is.False);
        Assert.That(model.TryChoose(Host, 2), Is.EqualTo(TutorialTraitChoiceResult.AlreadyChosen));
        Assert.That(model.GetAcquired(Host), Is.EqualTo(new[] { 1 }), "두 번째 선택은 무시");
    }

    [Test]
    public void TryChoose_RejectsNotOffered_AndNonParticipant()
    {
        var model = new TutorialTraitSelectionModel(6);
        model.StartRound(new[] { Host }, 3, First);

        Assert.That(model.TryChoose(Host, 5), Is.EqualTo(TutorialTraitChoiceResult.NotOffered));
        Assert.That(model.TryChoose(Host, -1), Is.EqualTo(TutorialTraitChoiceResult.NotOffered));
        Assert.That(model.TryChoose(Client1, 0), Is.EqualTo(TutorialTraitChoiceResult.NoOffer));
        Assert.That(model.HasPendingChoice(Host), Is.True, "거부는 상태를 바꾸지 않는다");
    }

    [Test]
    public void NextRound_ExcludesAcquired()
    {
        var model = new TutorialTraitSelectionModel(4);
        model.StartRound(new[] { Host }, 3, First);
        model.TryChoose(Host, 0);
        model.StartRound(new[] { Host }, 3, First);

        Assert.That(model.Round, Is.EqualTo(2));
        Assert.That(model.GetOffered(Host), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(model.GetAcquired(Host), Is.EqualTo(new[] { 0 }), "획득은 라운드를 넘어 유지");
    }

    [Test]
    public void PoolShortfall_OffersRemaining_ThenZero()
    {
        var model = new TutorialTraitSelectionModel(4);
        model.StartRound(new[] { Host }, 3, First);
        model.TryChoose(Host, 0);
        model.StartRound(new[] { Host }, 3, First);
        model.TryChoose(Host, 1);
        model.StartRound(new[] { Host }, 3, First);

        Assert.That(model.GetOffered(Host), Is.EqualTo(new[] { 2, 3 }), "남은 2장만");
        model.TryChoose(Host, 3);
        model.StartRound(new[] { Host }, 3, First);
        model.TryChoose(Host, 2);

        bool any = model.StartRound(new[] { Host }, 3, First);
        Assert.That(any, Is.False, "풀 소진 → 선택 단계 건너뜀");
        Assert.That(model.GetOffered(Host), Is.Empty);
        Assert.That(model.HasPendingChoice(Host), Is.False);
        Assert.That(model.IsRoundComplete(), Is.True);
        Assert.That(model.TryChoose(Host, 0), Is.EqualTo(TutorialTraitChoiceResult.NoOffer));
    }

    [Test]
    public void PickAutoChoice_OnlyForPending_FromOffered()
    {
        var model = new TutorialTraitSelectionModel(6);
        model.StartRound(new[] { Host, Client1 }, 3, First);

        Assert.That(model.PickAutoChoice(Host, _ => 2), Is.EqualTo(2));
        model.TryChoose(Client1, 0);
        Assert.That(model.PickAutoChoice(Client1, First), Is.EqualTo(-1), "이미 고른 플레이어는 대상 아님");
        Assert.That(model.PickAutoChoice(99, First), Is.EqualTo(-1), "참가자 아님");
        Assert.That(model.HasPendingChoice(Host), Is.True, "판정만 하고 획득하지 않는다");
    }

    [Test]
    public void RoundComplete_WhenAllParticipantsChose()
    {
        var model = new TutorialTraitSelectionModel(6);
        model.StartRound(new[] { Host, Client1 }, 3, First);
        var pending = new List<ulong>();

        model.TryChoose(Host, 0);
        model.GetPendingClients(pending);
        Assert.That(pending, Is.EqualTo(new[] { Client1 }));
        Assert.That(model.IsRoundComplete(), Is.False);

        model.TryChoose(Client1, model.PickAutoChoice(Client1, First));
        Assert.That(model.IsRoundComplete(), Is.True);
    }

    [Test]
    public void RemovePlayer_UnblocksRoundCompletion()
    {
        var model = new TutorialTraitSelectionModel(6);
        model.StartRound(new[] { Host, Client1 }, 3, First);
        model.TryChoose(Host, 0);

        model.RemovePlayer(Client1);

        Assert.That(model.IsRoundComplete(), Is.True);
    }

    [Test]
    public void NewRound_DropsPreviousParticipants()
    {
        var model = new TutorialTraitSelectionModel(6);
        model.StartRound(new[] { Host, Client1 }, 3, First);
        model.StartRound(new[] { Host }, 3, First);

        Assert.That(model.IsInRound(Client1), Is.False);
        Assert.That(model.GetOffered(Client1), Is.Empty);
        Assert.That(model.TryChoose(Client1, 0), Is.EqualTo(TutorialTraitChoiceResult.NoOffer));
    }

    // ── 선택 창 무적 계획(T3) ──

    [Test]
    public void Shields_AcquireForPendingNotYetShielded()
    {
        var model = new TutorialTraitSelectionModel(6);
        model.StartRound(new[] { Host, Client1 }, 3, First);
        var release = new List<ulong>();
        var acquire = new List<ulong>();

        // 제시 순간 Client1 은 플레이어 오브젝트가 없어 Host 만 걸렸다.
        model.PlanSelectionShields(new HashSet<ulong> { Host }, release, acquire);

        Assert.That(release, Is.Empty);
        Assert.That(acquire, Is.EqualTo(new[] { Client1 }), "스폰 뒤 재시도 = 아직 미선택인 Client1 만");
    }

    [Test]
    public void Shields_NotAcquiredAfterChoosing()
    {
        var model = new TutorialTraitSelectionModel(6);
        model.StartRound(new[] { Host, Client1 }, 3, First);
        model.TryChoose(Client1, model.GetOffered(Client1)[0]);
        var release = new List<ulong>();
        var acquire = new List<ulong>();

        // Client1 이 고른 뒤에야 준비됐다 — 걸지 않는다.
        model.PlanSelectionShields(new HashSet<ulong> { Host }, release, acquire);

        Assert.That(acquire, Is.Empty);
        Assert.That(release, Is.Empty);
    }

    [Test]
    public void Shields_ReleaseChosenAndDisconnected()
    {
        var model = new TutorialTraitSelectionModel(6);
        model.StartRound(new[] { Host, Client1 }, 3, First);
        model.TryChoose(Host, model.GetOffered(Host)[0]);
        model.RemovePlayer(Client1);
        var release = new List<ulong>();
        var acquire = new List<ulong>();

        model.PlanSelectionShields(new HashSet<ulong> { Host, Client1 }, release, acquire);

        Assert.That(release, Is.EquivalentTo(new[] { Host, Client1 }));
        Assert.That(acquire, Is.Empty);
    }

    [Test]
    public void Shields_ZeroOfferParticipantNeverShielded()
    {
        var model = new TutorialTraitSelectionModel(1);
        model.StartRound(new[] { Host }, 3, First);
        model.TryChoose(Host, 0);
        model.StartRound(new[] { Host }, 3, First); // 풀 소진 → 0장
        var release = new List<ulong>();
        var acquire = new List<ulong>();

        model.PlanSelectionShields(new HashSet<ulong>(), release, acquire);

        Assert.That(acquire, Is.Empty);
    }

    // ── 늦은 도착 참가자 추가 (S12 판단 1) ─────────────────────────────────

    const ulong Client2 = 2;

    [Test]
    public void AddParticipant_LateArrivalGetsOffer_ExistingChoiceKept()
    {
        var model = new TutorialTraitSelectionModel(6);
        model.StartRound(new[] { Host }, 3, First);
        model.TryChoose(Host, 1);

        Assert.That(model.IsInRound(Client1), Is.False, "아직 도착 안 한 사람은 제시받지 않는다");
        Assert.That(model.IsRoundComplete(), Is.True);

        TutorialTraitJoinResult result = model.AddParticipant(Client1, First);

        Assert.That(result, Is.EqualTo(TutorialTraitJoinResult.Offered));
        Assert.That(model.Round, Is.EqualTo(1), "같은 라운드 — 새 라운드를 열지 않는다");
        Assert.That(model.GetOffered(Client1), Is.EqualTo(new[] { 0, 1, 2 }), "라운드 시작 때와 같은 장 수");
        Assert.That(model.HasPendingChoice(Client1), Is.True);
        Assert.That(model.HasPendingChoice(Host), Is.False, "이미 고른 사람은 그대로");
        Assert.That(model.GetAcquired(Host), Is.EqualTo(new[] { 1 }));
        Assert.That(model.TryChoose(Host, 0), Is.EqualTo(TutorialTraitChoiceResult.AlreadyChosen));
        Assert.That(model.IsRoundComplete(), Is.False, "늦은 참가자가 고를 때까지 미완료");

        Assert.That(model.TryChoose(Client1, 2), Is.EqualTo(TutorialTraitChoiceResult.Accepted));
        Assert.That(model.IsRoundComplete(), Is.True);
    }

    [Test]
    public void AddParticipant_KeepsPendingOffersOfOthers()
    {
        var model = new TutorialTraitSelectionModel(6);
        model.StartRound(new[] { Host }, 2, Sequence(3, 0));
        var hostOffer = new List<int>(model.GetOffered(Host));

        model.AddParticipant(Client1, First);

        Assert.That(model.GetOffered(Host), Is.EqualTo(hostOffer), "먼저 제시받은 사람의 후보는 다시 뽑지 않는다");
        Assert.That(model.HasPendingChoice(Host), Is.True);
        Assert.That(model.GetOffered(Client1).Count, Is.EqualTo(2));
    }

    [Test]
    public void AddParticipant_DuplicateIgnored()
    {
        var model = new TutorialTraitSelectionModel(6);
        model.StartRound(new[] { Host }, 3, First);
        Assert.That(model.AddParticipant(Host, First), Is.EqualTo(TutorialTraitJoinResult.AlreadyInRound), "첫 도착자 자신");

        model.AddParticipant(Client1, Sequence(2, 0, 0));
        var offered = new List<int>(model.GetOffered(Client1));
        Assert.That(model.AddParticipant(Client1, First), Is.EqualTo(TutorialTraitJoinResult.AlreadyInRound));
        Assert.That(model.GetOffered(Client1), Is.EqualTo(offered), "다시 뽑지 않는다");

        model.TryChoose(Client1, offered[0]);
        Assert.That(model.AddParticipant(Client1, First), Is.EqualTo(TutorialTraitJoinResult.AlreadyInRound), "고른 뒤 왕복해도 창 없음");
        Assert.That(model.HasPendingChoice(Client1), Is.False);
    }

    [Test]
    public void AddParticipant_NoRoundOrEmptyPool()
    {
        var model = new TutorialTraitSelectionModel(1);
        Assert.That(model.AddParticipant(Host, First), Is.EqualTo(TutorialTraitJoinResult.NoRound));
        Assert.That(model.IsInRound(Host), Is.False);

        // Client1 은 이미 유일한 특성을 가졌다 → 다음 라운드 늦은 참가 = 0장 → 바로 완료.
        model.StartRound(new[] { Client1 }, 3, First);
        model.TryChoose(Client1, 0);
        model.StartRound(new[] { Host }, 3, First);
        Assert.That(model.AddParticipant(Client1, First), Is.EqualTo(TutorialTraitJoinResult.Empty));
        Assert.That(model.IsInRound(Client1), Is.True);
        Assert.That(model.HasPendingChoice(Client1), Is.False);
        Assert.That(model.TryChoose(Client1, 0), Is.EqualTo(TutorialTraitChoiceResult.NoOffer));
    }

    [Test]
    public void AutoChoose_IncludesAbsentAfterAddingThem()
    {
        // 교착 60초·타이머 만료: 미도착자도 참가자로 넣은 뒤 미선택 전원 자동 획득 → 라운드 완료.
        var model = new TutorialTraitSelectionModel(6);
        model.StartRound(new[] { Host }, 3, First);
        model.TryChoose(Host, 0);
        model.AddParticipant(Client1, First);   // 도착했지만 안 고름
        Assert.That(model.IsRoundComplete(), Is.False);

        model.AddParticipant(Client2, First);   // 미도착
        var pending = new List<ulong>();
        model.GetPendingClients(pending);
        Assert.That(pending, Is.EquivalentTo(new[] { Client1, Client2 }), "고른 Host 는 빠지고 미도착자는 들어간다");

        foreach (ulong id in pending)
        {
            int pick = model.PickAutoChoice(id, First);
            Assert.That(pick, Is.GreaterThanOrEqualTo(0));
            Assert.That(model.TryChoose(id, pick), Is.EqualTo(TutorialTraitChoiceResult.Accepted));
        }

        Assert.That(model.IsRoundComplete(), Is.True);
        Assert.That(model.GetAcquired(Client2).Count, Is.EqualTo(1));
        Assert.That(model.GetAcquired(Host), Is.EqualTo(new[] { 0 }), "이미 고른 사람은 추가 획득 없음");
    }

    [Test]
    public void AddParticipant_ShieldsOnlyNewPending()
    {
        var model = new TutorialTraitSelectionModel(6);
        model.StartRound(new[] { Host }, 3, First);
        model.TryChoose(Host, 0);
        model.AddParticipant(Client1, First);
        var release = new List<ulong>();
        var acquire = new List<ulong>();

        model.PlanSelectionShields(new HashSet<ulong>(), release, acquire);

        Assert.That(acquire, Is.EqualTo(new[] { Client1 }), "늦게 도착해 창이 뜬 사람만 무적");
    }

    [Test]
    public void NewRoundAfterLateJoin_ResetsParticipants()
    {
        var model = new TutorialTraitSelectionModel(6);
        model.StartRound(new[] { Host }, 3, First);
        model.AddParticipant(Client1, First);
        model.StartRound(new[] { Client1 }, 3, First);

        Assert.That(model.IsInRound(Host), Is.False, "다음 복도 라운드는 그 복도 첫 도착자만");
        Assert.That(model.AddParticipant(Host, First), Is.EqualTo(TutorialTraitJoinResult.Offered));
    }
}
