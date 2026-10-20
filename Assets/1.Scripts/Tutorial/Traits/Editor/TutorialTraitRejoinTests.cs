using System;
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 판 도중 재합류 특성 보관·복원(할 일 48 C14 R4c) — 선택 모델 RestoreAcquired(순서·중복·멱등)·라운드 참가 복원·
/// 특성 초기화 팝업/재선택 보관·복원(Q-D b, 지난 세대·튜토리얼 아님 포함). 규칙 원본: <see cref="TutorialTraitRejoin"/>.
/// </summary>
public sealed class TutorialTraitRejoinTests
{
    const ulong Old = 1;
    const ulong New = 4;
    const ulong Other = 2;

    // 후보를 섞지 않는다 — 남은 인덱스 앞에서부터 제시.
    static readonly Func<int, int> First = _ => 0;

    static TutorialTraitSelectionModel NewModel(params int[] unusable) => new TutorialTraitSelectionModel(12, unusable);

    // 퇴장 처리(디렉터 HandleClientDisconnected 와 같은 순서): 보관 → 모델에서 지우기.
    static TutorialTraitRejoinSnapshot Leave(TutorialTraitSelectionModel model, TutorialTraitResetModel reset, ulong clientId)
    {
        TutorialTraitRejoinSnapshot snapshot = TutorialTraitRejoin.Capture(model, reset, clientId);
        model.RemovePlayer(clientId);
        reset?.Remove(clientId);
        return snapshot;
    }

    // ── 획득 목록 ──────────────────────────────────────────────────────────

    [Test]
    public void RestoreAcquired_KeepsOrder_SkipsDuplicateInvalidAndUnusable()
    {
        TutorialTraitSelectionModel model = NewModel(11);
        var added = new List<int>();

        int count = model.RestoreAcquired(New, new[] { 5, 2, 5, 11, 99, -1, 7 }, added);

        Assert.That(count, Is.EqualTo(3));
        CollectionAssert.AreEqual(new[] { 5, 2, 7 }, model.GetAcquired(New), "획득 순서 그대로");
        CollectionAssert.AreEqual(new[] { 5, 2, 7 }, added);
        Assert.That(model.GetOfferSeq(New), Is.EqualTo(0), "제시 번호를 올리지 않는다 — 창·토스트 없음");
    }

    [Test]
    public void RestoreAcquired_IsIdempotent_AndAppendsOnlyMissing()
    {
        TutorialTraitSelectionModel model = NewModel();
        model.RestoreAcquired(New, new[] { 3, 1 });

        Assert.That(model.RestoreAcquired(New, new[] { 3, 1 }), Is.EqualTo(0), "같은 목록 두 번 = 한 번");
        Assert.That(model.RestoreAcquired(New, new[] { 1, 8 }), Is.EqualTo(1));
        CollectionAssert.AreEqual(new[] { 3, 1, 8 }, model.GetAcquired(New));
    }

    [Test]
    public void RestoredTraits_AreExcludedFromNextOffer()
    {
        TutorialTraitSelectionModel model = NewModel();
        model.RestoreAcquired(New, new[] { 0, 1, 2 });

        model.StartRound(new[] { New }, 3, First);

        CollectionAssert.AreEqual(new[] { 3, 4, 5 }, model.GetOffered(New), "이미 가진 특성은 다시 제시하지 않는다");
    }

    [Test]
    public void CaptureAndRestore_CarriesAcquiredInOrder()
    {
        TutorialTraitSelectionModel model = NewModel();
        model.StartRound(new[] { Old }, 3, First);
        model.TryChoose(Old, 2);
        model.StartRound(new[] { Old }, 3, First);
        model.TryChoose(Old, 0);

        TutorialTraitRejoinSnapshot snapshot = Leave(model, null, Old);
        Assert.That(model.GetAcquired(Old), Is.Empty, "퇴장하면 모델에서는 지워진다");

        TutorialTraitRejoin.Restore(model, null, New, snapshot, allowReset: true);
        CollectionAssert.AreEqual(new[] { 2, 0 }, model.GetAcquired(New));
    }

    // ── 복도 라운드 참가 ──────────────────────────────────────────────────

    [Test]
    public void ChosenInCurrentRound_IsRestored_SoSameRoundGivesNoSecondTrait()
    {
        TutorialTraitSelectionModel model = NewModel();
        model.StartRound(new[] { Old, Other }, 3, First);
        model.TryChoose(Old, 1);

        TutorialTraitRejoinSnapshot snapshot = Leave(model, null, Old);
        Assert.IsTrue(snapshot.RoundDone);
        TutorialTraitRejoin.Restore(model, null, New, snapshot, allowReset: true);

        Assert.IsTrue(model.IsInRound(New));
        Assert.IsFalse(model.HasPendingChoice(New));
        Assert.That(model.AddParticipant(New, First), Is.EqualTo(TutorialTraitJoinResult.AlreadyInRound),
            "문 열림 자동 획득·늦은 도착이 한 장을 더 주지 않는다");
        Assert.IsTrue(model.HasPendingChoice(Other), "다른 사람의 제시는 그대로");
    }

    [Test]
    public void ChosenRound_IsNotRestored_WhenRoundMovedOn()
    {
        TutorialTraitSelectionModel model = NewModel();
        model.StartRound(new[] { Old }, 3, First);
        model.TryChoose(Old, 1);
        TutorialTraitRejoinSnapshot snapshot = Leave(model, null, Old);

        model.StartRound(new[] { Other }, 3, First); // 퇴장 중 다음 라운드
        TutorialTraitRejoin.Restore(model, null, New, snapshot, allowReset: true);

        Assert.IsFalse(model.IsInRound(New));
        Assert.That(model.AddParticipant(New, First), Is.EqualTo(TutorialTraitJoinResult.Offered), "새 라운드는 정상 참가");
    }

    [Test]
    public void PendingCorridorOffer_IsNotRestored_LeftToAbsentAutoChoice()
    {
        TutorialTraitSelectionModel model = NewModel();
        model.StartRound(new[] { Old }, 3, First);

        TutorialTraitRejoinSnapshot snapshot = Leave(model, null, Old);
        Assert.IsFalse(snapshot.RoundDone);
        TutorialTraitRejoin.Restore(model, null, New, snapshot, allowReset: true);

        Assert.IsFalse(model.IsInRound(New), "고르던 복도 제시는 되살리지 않는다(문 열림 자동 획득 규칙)");
        Assert.That(snapshot.IsEmpty, Is.True);
    }

    // ── 특성 초기화 팝업·재선택(Q-D b) ──────────────────────────────────────

    [Test]
    public void PendingPrompt_IsRestored_WithSameGeneration_AndAnswerable()
    {
        TutorialTraitSelectionModel model = NewModel();
        model.RestoreAcquired(Old, new[] { 4 });
        var reset = new TutorialTraitResetModel();
        int generation = reset.BeginPrompts(new[] { Old, Other }, new List<ulong>());

        TutorialTraitRejoinSnapshot snapshot = Leave(model, reset, Old);
        Assert.That(snapshot.PromptGeneration, Is.EqualTo(generation));
        Assert.IsTrue(reset.IsBusy, "다른 사람 팝업은 남아 있다");

        TutorialTraitRejoinReset result = TutorialTraitRejoin.Restore(model, reset, New, snapshot, allowReset: true);

        Assert.That(result, Is.EqualTo(TutorialTraitRejoinReset.Prompt));
        Assert.IsTrue(reset.TryGetPrompt(New, out int restored) && restored == generation);
        Assert.IsTrue(reset.TryGetPrompt(Other, out _), "다른 사람 팝업 무변화");
        Assert.That(reset.Answer(New, generation, reset: false), Is.EqualTo(TutorialTraitResetAnswer.Kept));
    }

    [Test]
    public void Prompt_IsNotRestored_OutsideTutorial()
    {
        TutorialTraitSelectionModel model = NewModel();
        var reset = new TutorialTraitResetModel();
        reset.BeginPrompts(new[] { Old }, new List<ulong>());
        TutorialTraitRejoinSnapshot snapshot = Leave(model, reset, Old);

        Assert.That(TutorialTraitRejoin.Restore(model, reset, New, snapshot, allowReset: false), Is.EqualTo(TutorialTraitRejoinReset.None));
        Assert.IsFalse(reset.Has(New));
        Assert.IsFalse(reset.IsBusy);
    }

    [Test]
    public void WipeWhileAbsent_RestoresPromptAtCurrentGeneration()
    {
        TutorialTraitSelectionModel model = NewModel();
        var reset = new TutorialTraitResetModel();
        reset.BeginPrompts(new[] { Old, Other }, new List<ulong>());
        TutorialTraitRejoinSnapshot snapshot = Leave(model, reset, Old);

        int current = reset.BeginPrompts(new[] { Other }, new List<ulong>()); // 퇴장 중 다시 전멸

        Assert.That(TutorialTraitRejoin.Restore(model, reset, New, snapshot, allowReset: true), Is.EqualTo(TutorialTraitRejoinReset.Prompt));
        Assert.IsTrue(reset.TryGetPrompt(New, out int generation) && generation == current, "지난 세대가 아니라 지금 세대 팝업");
    }

    [Test]
    public void NoResetState_GetsNoPrompt_EvenIfWipedWhileAbsent()
    {
        TutorialTraitSelectionModel model = NewModel();
        model.RestoreAcquired(Old, new[] { 1 });
        var reset = new TutorialTraitResetModel();
        TutorialTraitRejoinSnapshot snapshot = Leave(model, reset, Old);

        reset.BeginPrompts(new[] { Other }, new List<ulong>());

        Assert.That(TutorialTraitRejoin.Restore(model, reset, New, snapshot, allowReset: true), Is.EqualTo(TutorialTraitRejoinReset.None));
        Assert.IsFalse(reset.Has(New), "보관한 것만 되돌린다");
    }

    [Test]
    public void Reselect_RestoresRoundsLeft_AndSameOpenOffer()
    {
        TutorialTraitSelectionModel model = NewModel();
        var reset = new TutorialTraitResetModel();
        int generation = reset.BeginPrompts(new[] { Old }, new List<ulong>());
        reset.Answer(Old, generation, reset: true);
        model.ResetPlayer(Old);
        reset.TryTakeReselectRound(Old);
        model.StartPersonalRound(Old, 3, First);
        model.TryChoose(Old, 1);
        reset.TryTakeReselectRound(Old);
        model.StartPersonalRound(Old, 3, First); // 열린 후보 0·2·3

        TutorialTraitRejoinSnapshot snapshot = Leave(model, reset, Old);
        Assert.IsTrue(snapshot.Reselecting);
        Assert.That(snapshot.ReselectRoundsLeft, Is.EqualTo(TutorialTraitResetModel.ReselectRounds - 2));
        CollectionAssert.AreEqual(new[] { 0, 2, 3 }, snapshot.PersonalOffered);
        Assert.IsFalse(reset.IsBusy, "퇴장하면 패드 잠금이 풀린다");

        TutorialTraitRejoinReset result = TutorialTraitRejoin.Restore(model, reset, New, snapshot, allowReset: true);

        Assert.That(result, Is.EqualTo(TutorialTraitRejoinReset.ReselectOffer));
        CollectionAssert.AreEqual(new[] { 1 }, model.GetAcquired(New));
        CollectionAssert.AreEqual(new[] { 0, 2, 3 }, model.GetOffered(New), "같은 후보로 창을 다시 연다");
        Assert.IsTrue(model.HasPendingChoice(New) && model.IsPersonalRound(New));
        Assert.That(model.GetOfferSeq(New), Is.GreaterThan(0), "새 제시 번호 — 그 사람 창만 열린다");
        Assert.IsTrue(reset.IsReselecting(New) && reset.IsBusy);
        Assert.That(reset.RoundsLeft(New), Is.EqualTo(TutorialTraitResetModel.ReselectRounds - 2));
        Assert.That(model.TryChoose(New, 2), Is.EqualTo(TutorialTraitChoiceResult.Accepted));
    }

    [Test]
    public void Reselect_WithoutOpenOffer_AsksCallerToContinue()
    {
        TutorialTraitSelectionModel model = NewModel();
        var reset = new TutorialTraitResetModel();
        int generation = reset.BeginPrompts(new[] { Other }, new List<ulong>());
        var snapshot = new TutorialTraitRejoinSnapshot(new[] { 0 }, 0, false, generation, 0, true, 2, Array.Empty<int>());

        Assert.That(TutorialTraitRejoin.Restore(model, reset, New, snapshot, allowReset: true), Is.EqualTo(TutorialTraitRejoinReset.ReselectContinue));
        Assert.IsTrue(reset.IsReselecting(New));
        Assert.That(reset.RoundsLeft(New), Is.EqualTo(2));
        Assert.IsFalse(model.IsInRound(New), "열 라운드는 호출자(ContinueReselect)가 연다");
    }

    [Test]
    public void RestorePersonalOffer_DropsAcquiredCandidates_AndFailsWhenNoneLeft()
    {
        TutorialTraitSelectionModel model = NewModel();
        model.RestoreAcquired(New, new[] { 0, 2 });

        Assert.IsFalse(model.RestorePersonalOffer(New, new[] { 0, 2 }));
        Assert.IsTrue(model.RestorePersonalOffer(New, new[] { 0, 5, 2, 5 }));
        CollectionAssert.AreEqual(new[] { 5 }, model.GetOffered(New));
    }

    [Test]
    public void Restore_Twice_IsIdempotent()
    {
        TutorialTraitSelectionModel model = NewModel();
        model.RestoreAcquired(Old, new[] { 6, 3 });
        var reset = new TutorialTraitResetModel();
        reset.BeginPrompts(new[] { Old }, new List<ulong>());
        TutorialTraitRejoinSnapshot snapshot = Leave(model, reset, Old);

        TutorialTraitRejoin.Restore(model, reset, New, snapshot, allowReset: true);
        TutorialTraitRejoinReset second = TutorialTraitRejoin.Restore(model, reset, New, snapshot, allowReset: true);

        Assert.That(second, Is.EqualTo(TutorialTraitRejoinReset.None), "이미 팝업 상태가 있으면 다시 만들지 않는다");
        CollectionAssert.AreEqual(new[] { 6, 3 }, model.GetAcquired(New));
    }

    [Test]
    public void Capture_UnknownClient_IsEmpty()
    {
        TutorialTraitRejoinSnapshot snapshot = TutorialTraitRejoin.Capture(NewModel(), new TutorialTraitResetModel(), 9);
        Assert.IsTrue(snapshot.IsEmpty);
        Assert.IsFalse(snapshot.HadResetState);
    }

    [Test]
    public void Ledger_Has_FindsOnlyThatPlayersTrait()
    {
        var ledger = new TutorialTraitEffectLedger<string>();
        ledger.Add(New, 3, "a");

        Assert.IsTrue(ledger.Has(New, 3));
        Assert.IsFalse(ledger.Has(New, 4));
        Assert.IsFalse(ledger.Has(Old, 3));
    }
}
