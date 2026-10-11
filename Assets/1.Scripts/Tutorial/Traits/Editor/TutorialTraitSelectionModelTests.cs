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
}
