using System;
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 전멸 리스폰 뒤 특성 초기화(할 일 30 3단계) — 팝업 응답 검증(예/아니오/중복/대상 아님/지난 세대)·원복 장부·4라운드 재선택.
/// 원본: Docs/tech/party-lives-respawn.md §4.
/// </summary>
public sealed class TutorialTraitResetModelTests
{
    const ulong Host = 0;
    const ulong Client1 = 1;
    const ulong Stranger = 7;

    static readonly Func<int, int> First = _ => 0;

    static TutorialTraitResetModel Prompted(out int generation, params ulong[] clients)
    {
        var model = new TutorialTraitResetModel();
        generation = model.BeginPrompts(clients, new List<ulong>());
        return model;
    }

    // ── 팝업 응답 ──────────────────────────────────────────────────────────

    [Test]
    public void BeginPrompts_EveryRespawnedClientWaits_AndPadIsHeld()
    {
        TutorialTraitResetModel model = Prompted(out int generation, Host, Client1);

        Assert.That(generation, Is.EqualTo(1));
        Assert.IsTrue(model.TryGetPrompt(Host, out int g0) && g0 == generation);
        Assert.IsTrue(model.TryGetPrompt(Client1, out int g1) && g1 == generation);
        Assert.IsFalse(model.TryGetPrompt(Stranger, out _));
        Assert.IsTrue(model.IsBusy, "팝업 대기 중 = 패드 잠금");
    }

    [Test]
    public void AnswerNo_KeepsTraits_AndClearsOnlyThatPlayer()
    {
        TutorialTraitResetModel model = Prompted(out int generation, Host, Client1);

        Assert.That(model.Answer(Host, generation, reset: false), Is.EqualTo(TutorialTraitResetAnswer.Kept));
        Assert.IsFalse(model.TryGetPrompt(Host, out _));
        Assert.IsFalse(model.IsReselecting(Host));
        Assert.IsTrue(model.TryGetPrompt(Client1, out _), "다른 사람 팝업은 그대로");
        Assert.IsTrue(model.IsBusy);

        model.Answer(Client1, generation, reset: false);
        Assert.IsFalse(model.IsBusy, "전원 답함 = 패드 풀림");
    }

    [Test]
    public void AnswerYes_StartsReselect_WithFourRounds()
    {
        TutorialTraitResetModel model = Prompted(out int generation, Host);

        Assert.That(model.Answer(Host, generation, reset: true), Is.EqualTo(TutorialTraitResetAnswer.Reset));
        Assert.IsFalse(model.TryGetPrompt(Host, out _), "팝업은 닫힌다");
        Assert.IsTrue(model.IsReselecting(Host));
        Assert.That(model.RoundsLeft(Host), Is.EqualTo(TutorialTraitResetModel.ReselectRounds));
        Assert.IsTrue(model.IsBusy, "재선택 중 = 패드 잠금");
    }

    [Test]
    public void DuplicateAnswer_IsRejected()
    {
        TutorialTraitResetModel model = Prompted(out int generation, Host);
        model.Answer(Host, generation, reset: false);

        Assert.That(model.Answer(Host, generation, reset: true), Is.EqualTo(TutorialTraitResetAnswer.NotPrompted));
        Assert.IsFalse(model.IsReselecting(Host), "아니오 뒤의 늦은 예는 무시");

        TutorialTraitResetModel yes = Prompted(out int g2, Client1);
        yes.Answer(Client1, g2, reset: true);
        Assert.That(yes.Answer(Client1, g2, reset: true), Is.EqualTo(TutorialTraitResetAnswer.NotPrompted), "재선택 중 다시 예");
        Assert.That(yes.RoundsLeft(Client1), Is.EqualTo(TutorialTraitResetModel.ReselectRounds), "라운드 수가 다시 차지 않는다");
    }

    [Test]
    public void NonTarget_IsRejected()
    {
        TutorialTraitResetModel model = Prompted(out int generation, Host);

        Assert.That(model.Answer(Stranger, generation, reset: true), Is.EqualTo(TutorialTraitResetAnswer.NotPrompted));
        Assert.IsFalse(model.IsReselecting(Stranger));
        Assert.That(new TutorialTraitResetModel().Answer(Host, 1, reset: true), Is.EqualTo(TutorialTraitResetAnswer.NotPrompted), "팝업을 띄운 적 없음");
    }

    [Test]
    public void StaleGeneration_IsRejected_AfterSecondWipe()
    {
        var model = new TutorialTraitResetModel();
        var interrupted = new List<ulong>();
        int first = model.BeginPrompts(new[] { Host }, interrupted);
        int second = model.BeginPrompts(new[] { Host }, interrupted);

        Assert.That(second, Is.GreaterThan(first));
        Assert.That(model.Answer(Host, first, reset: true), Is.EqualTo(TutorialTraitResetAnswer.StaleGeneration));
        Assert.IsTrue(model.TryGetPrompt(Host, out int g) && g == second, "새 팝업은 그대로 대기");
        Assert.That(model.Answer(Host, second, reset: false), Is.EqualTo(TutorialTraitResetAnswer.Kept));
    }

    [Test]
    public void SecondWipe_InterruptsReselect_AndPromptsAgain()
    {
        var model = new TutorialTraitResetModel();
        var interrupted = new List<ulong>();
        int first = model.BeginPrompts(new[] { Host, Client1 }, interrupted);
        model.Answer(Host, first, reset: true);
        model.TryTakeReselectRound(Host);

        int second = model.BeginPrompts(new[] { Host, Client1 }, interrupted);

        Assert.That(interrupted, Is.EqualTo(new[] { Host }), "재선택 도중이던 사람만 — 호출자가 열린 제시를 거둔다");
        Assert.IsFalse(model.IsReselecting(Host));
        Assert.IsTrue(model.TryGetPrompt(Host, out int g) && g == second, "기존 팝업·재선택을 닫고 새로");
        Assert.IsTrue(model.TryGetPrompt(Client1, out _));
    }

    [Test]
    public void Disconnect_RemovesWaitingState_AndReleasesPad()
    {
        TutorialTraitResetModel model = Prompted(out int generation, Host, Client1);
        model.Answer(Host, generation, reset: false);

        Assert.IsTrue(model.Remove(Client1));
        Assert.IsFalse(model.IsBusy, "끊긴 사람이 패드를 잠그고 있지 않는다");
        Assert.That(model.Answer(Client1, generation, reset: true), Is.EqualTo(TutorialTraitResetAnswer.NotPrompted));
    }

    // ── 원복 장부 ──────────────────────────────────────────────────────────

    [Test]
    public void Ledger_TakeAll_EmptiesOnlyThatPlayer()
    {
        var ledger = new TutorialTraitEffectLedger<string>();
        ledger.Add(Host, 0, "host-atk");
        ledger.Add(Client1, 0, "c1-atk");
        ledger.Add(Host, 2, "host-cd");

        var taken = new List<TutorialTraitEffectLedger<string>.Record>();
        Assert.That(ledger.TakeAll(Host, taken), Is.EqualTo(2));
        Assert.That(taken.ConvertAll(r => r.Handle), Is.EqualTo(new[] { "host-atk", "host-cd" }), "적용 순서");
        Assert.That(taken.ConvertAll(r => r.TraitIndex), Is.EqualTo(new[] { 0, 2 }));

        Assert.That(ledger.CountOf(Host), Is.EqualTo(0), "초기화 뒤 원복 목록이 빈다");
        Assert.That(ledger.CountOf(Client1), Is.EqualTo(1), "다른 플레이어 것은 남는다");
        Assert.That(ledger.TakeAll(Host, taken), Is.EqualTo(0), "두 번 원복하지 않는다");
    }

    // ── 4라운드 재선택(선택 모델과 함께) ─────────────────────────────────────

    [Test]
    public void Reselect_FourRounds_ThreeOffers_PickedExcluded_ThenEnds()
    {
        var selection = new TutorialTraitSelectionModel(8);
        // 복도 4라운드로 0~3 을 얻은 상태
        for (int r = 0; r < 4; r++)
        {
            selection.StartRound(new[] { Host }, 3, First);
            selection.TryChoose(Host, selection.GetOffered(Host)[0]);
        }
        Assert.That(selection.GetAcquired(Host), Is.EqualTo(new[] { 0, 1, 2, 3 }));

        TutorialTraitResetModel reset = Prompted(out int generation, Host);
        reset.Answer(Host, generation, reset: true);
        selection.ResetPlayer(Host);
        Assert.That(selection.GetAcquired(Host), Is.Empty, "초기화 = 획득 비움");

        var picked = new List<int>();
        int rounds = 0;
        while (reset.TryTakeReselectRound(Host))
        {
            int roundBefore = selection.Round;
            int seqBefore = selection.GetOfferSeq(Host);
            Assert.IsTrue(selection.StartPersonalRound(Host, 3, First));
            Assert.That(selection.GetOfferSeq(Host), Is.GreaterThan(seqBefore), "라운드마다 새 창(내 제시 번호)");
            Assert.That(selection.Round, Is.EqualTo(roundBefore), "전역(복도) 라운드 번호는 그대로");
            Assert.IsTrue(selection.IsPersonalRound(Host));
            IReadOnlyList<int> offered = selection.GetOffered(Host);
            Assert.That(offered.Count, Is.EqualTo(3), "후보 3장");
            Assert.That(offered, Is.Unique);
            foreach (int index in offered)
                Assert.That(picked, Has.No.Member(index), "고른 건 다음 후보에서 빠진다");

            int choice = offered[offered.Count - 1];
            Assert.That(selection.TryChoose(Host, choice), Is.EqualTo(TutorialTraitChoiceResult.Accepted));
            picked.Add(choice);
            rounds++;
        }

        Assert.That(rounds, Is.EqualTo(TutorialTraitResetModel.ReselectRounds), "4번 연속 선택");
        Assert.IsFalse(reset.IsReselecting(Host), "4회 후 종료");
        Assert.IsFalse(reset.IsBusy);
        Assert.That(selection.GetAcquired(Host), Is.EqualTo(picked));
        Assert.IsFalse(selection.HasPendingChoice(Host));
    }

    [Test]
    public void PersonalRound_DoesNotTouchOtherPlayers()
    {
        var selection = new TutorialTraitSelectionModel(8);
        selection.StartRound(new[] { Host, Client1 }, 3, First);
        selection.TryChoose(Client1, selection.GetOffered(Client1)[0]);
        int hostSeq = selection.GetOfferSeq(Host);
        selection.StartPersonalRound(Client1, 3, First);    // Client1 재선택 1라운드 — Host 는 아직 복도 라운드 고르는 중

        Assert.IsTrue(selection.HasPendingChoice(Host), "다른 사람의 제시는 그대로");
        Assert.That(selection.GetOfferSeq(Host), Is.EqualTo(hostSeq), "다른 사람의 제시 번호도 그대로(창이 안 닫힌다)");
        Assert.IsFalse(selection.IsPersonalRound(Host), "Host 는 복도 라운드 참가");
        Assert.IsTrue(selection.IsPersonalRound(Client1));
        Assert.That(selection.GetOffered(Host).Count, Is.EqualTo(3));
        Assert.IsTrue(selection.HasPendingChoice(Client1));
        Assert.That(selection.GetOffered(Client1), Has.No.Member(selection.GetAcquired(Client1)[0]));
    }

    [Test]
    public void ResetPlayer_KeepsOthers_AndCancelOfferStopsPending()
    {
        var selection = new TutorialTraitSelectionModel(8);
        selection.StartRound(new[] { Host, Client1 }, 3, First);
        selection.TryChoose(Host, selection.GetOffered(Host)[0]);
        selection.TryChoose(Client1, selection.GetOffered(Client1)[0]);

        selection.ResetPlayer(Host);
        Assert.That(selection.GetAcquired(Host), Is.Empty);
        Assert.That(selection.GetAcquired(Client1).Count, Is.EqualTo(1), "다른 플레이어 특성은 그대로");

        selection.StartPersonalRound(Host, 3, First);
        Assert.IsTrue(selection.CancelOffer(Host));
        Assert.IsFalse(selection.HasPendingChoice(Host), "다시 전멸 — 열린 제시를 거둔다");
        Assert.IsTrue(selection.IsRoundComplete());
        Assert.IsFalse(selection.CancelOffer(Host), "거둘 게 없으면 false");
    }

    [Test]
    public void Reselect_SkipsEmptyRounds_WhenPoolRunsOut()
    {
        var selection = new TutorialTraitSelectionModel(2);
        TutorialTraitResetModel reset = Prompted(out int generation, Host);
        reset.Answer(Host, generation, reset: true);

        int offeredRounds = 0;
        while (reset.TryTakeReselectRound(Host))
        {
            if (!selection.StartPersonalRound(Host, 3, First))
                continue; // 0장 = 건너뜀(디렉터와 같은 규칙)
            offeredRounds++;
            selection.TryChoose(Host, selection.GetOffered(Host)[0]);
        }

        Assert.That(offeredRounds, Is.EqualTo(2), "풀 2장 — 2라운드만 제시");
        Assert.IsFalse(reset.IsReselecting(Host), "남은 라운드를 다 써서 종료");
    }
}
