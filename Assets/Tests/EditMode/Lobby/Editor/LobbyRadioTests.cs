using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

/// <summary>
/// 로비 무전 — 할 일 86 T6b(회차당 1회, 단계 바뀜 = 미재생 폐기, 늦은 클라, 문구 행).
/// 할 일 157 ②(Q6): 단계 진입 대사는 껐다 — 단계 = 스냅샷 revision, 무전 모델은 단계 번호(미재생 폐기)만 맞춘다. id·문구 행은 남긴다.
/// </summary>
public sealed class LobbyRadioTests
{
    [Test]
    public void 처음_본_단계는_기준만_잡는다()
    {
        var radio = new LobbyRadioModel();
        Assert.That(radio.OnStepChanged(1), Is.False);
        Assert.That(radio.StageSerial, Is.EqualTo(0));
    }

    [Test]
    public void 늦게_구독해도_지난_대사를_틀지_않는다()
    {
        var radio = new LobbyRadioModel();
        radio.OnStepChanged(9);
        Assert.That(radio.HasPlayed(LobbyRadioIds.Train02Start), Is.False);
        Assert.That(radio.StageSerial, Is.EqualTo(0));
    }

    [Test]
    public void 단계가_바뀔_때마다_번호_1씩_진입_대사는_없다()
    {
        var radio = new LobbyRadioModel();
        var serials = new List<int>();
        radio.OnStepChanged(1);
        for (var revision = 2; revision <= 22; revision++)
        {
            if (radio.OnStepChanged(revision))
                serials.Add(radio.StageSerial);
        }

        CollectionAssert.AreEqual(Enumerable.Range(1, 21), serials);
        foreach (var id in LobbyRadioIds.All)
            Assert.That(radio.HasPlayed(id), Is.False, $"{id} — 진입 대사 꺼짐(Q6)");
    }

    [Test]
    public void 같은_단계_중복_알림은_무시된다()
    {
        var radio = new LobbyRadioModel();
        radio.OnStepChanged(1);
        radio.OnStepChanged(2);
        Assert.That(radio.OnStepChanged(2), Is.False);
        Assert.That(radio.StageSerial, Is.EqualTo(1));
    }

    [Test]
    public void 단계_안_사건_대사는_회차당_한_번()
    {
        var radio = new LobbyRadioModel();
        Assert.That(radio.TryPlay(LobbyRadioIds.Train03Counter, true), Is.True);
        Assert.That(radio.TryPlay(LobbyRadioIds.Train03Counter, true), Is.False);
        Assert.That(radio.TryPlay(LobbyRadioIds.Train03Counter, false), Is.True, "oncePerRound=false 는 반복 허용");
        Assert.That(radio.TryPlay(string.Empty, false), Is.False);
    }

    [Test]
    public void 새_회차는_다시_틀_수_있고_단계_번호는_계속_오른다()
    {
        var radio = new LobbyRadioModel();
        radio.OnStepChanged(1);
        radio.OnStepChanged(2);
        radio.TryPlay(LobbyRadioIds.Train03Wall, true);
        var before = radio.StageSerial;

        radio.Reset();
        Assert.That(radio.StageSerial, Is.GreaterThan(before));
        Assert.That(radio.OnStepChanged(1), Is.False, "새 회차 첫 단계는 기준만");
        Assert.That(radio.TryPlay(LobbyRadioIds.Train03Wall, true), Is.True);
    }

    // ---------------- 클라 수신 ----------------

    [Test]
    public void 수신_더_큰_단계_번호면_대기_줄을_먼저_버린다()
    {
        var receiver = new LobbyRadioReceiver();
        Assert.That(receiver.AcceptLine(0, out var discard), Is.True);
        Assert.That(discard, Is.False);

        Assert.That(receiver.AcceptLine(2, out discard), Is.True);
        Assert.That(discard, Is.True);
        Assert.That(receiver.AcceptLine(2, out discard), Is.True);
        Assert.That(discard, Is.False);
    }

    [Test]
    public void 수신_지난_단계_대사는_버린다()
    {
        var receiver = new LobbyRadioReceiver();
        Assert.That(receiver.OnStageAdvanced(3), Is.True);
        Assert.That(receiver.OnStageAdvanced(3), Is.False);
        Assert.That(receiver.AcceptLine(2, out _), Is.False);
    }

    [Test]
    public void 늦게_붙은_클라는_다음_대사부터_받는다()
    {
        var receiver = new LobbyRadioReceiver();   // 서버는 이미 번호 5 — 지난 대사는 다시 오지 않는다
        Assert.That(receiver.AcceptLine(5, out var discard), Is.True);
        Assert.That(discard, Is.True, "비어 있는 대기 줄을 비우는 것뿐 — 해 없음");
    }

    // ---------------- 큐 ----------------

    [Test]
    public void 큐_일반_무전은_하나씩_순서대로()
    {
        var queue = new TutorialSubtitleQueue();
        Assert.That(queue.Enqueue(new TutorialSubtitle("A", "a", 2f)), Is.True);
        Assert.That(queue.Enqueue(new TutorialSubtitle("B", "b", 2f)), Is.False);
        Assert.That(queue.Current.Id, Is.EqualTo("A"));
        queue.Advance(2f);
        Assert.That(queue.Current.Id, Is.EqualTo("B"));
    }

    [Test]
    public void 큐_미재생_폐기는_지금_뜬_줄을_남기고_대기_줄만_버린다()
    {
        var queue = new TutorialSubtitleQueue();
        queue.Enqueue(new TutorialSubtitle("A", "a", 2f));
        queue.Enqueue(new TutorialSubtitle("B", "b", 2f));
        queue.Advance(0.5f);

        queue.ClearPending();
        Assert.That(queue.HasCurrent, Is.True);
        Assert.That(queue.Current.Id, Is.EqualTo("A"));
        Assert.That(queue.Elapsed, Is.EqualTo(0.5f).Within(1e-4f));
        Assert.That(queue.PendingCount, Is.EqualTo(0));

        queue.Enqueue(new TutorialSubtitle("C", "c", 2f));
        queue.Advance(1.5f);
        Assert.That(queue.Current.Id, Is.EqualTo("C"));
    }

    // ---------------- 문구 ----------------

    [Test]
    public void 문구_초안이_모든_무전_id_를_한_번씩_덮는다()
    {
        CollectionAssert.AreEquivalent(LobbyRadioIds.All, TutorialGuideUIAuthoring.LobbyRadioTexts.Select(t => t.id));
        Assert.That(TutorialGuideUIAuthoring.LobbyRadioTexts.All(t => !string.IsNullOrWhiteSpace(t.text) && t.seconds > 0f), Is.True);
    }

    [Test]
    public void 문구_에셋에_모든_무전_행이_있다()
    {
        var table = AssetDatabase.LoadAssetAtPath<TutorialGuideTextTable>(TutorialGuideTextTable.AssetPath);
        Assume.That(table, Is.Not.Null, "TutorialGuideTexts 에셋 없음");
        foreach (var id in LobbyRadioIds.All)
        {
            Assert.That(table.TryGetRow(id, out var row), Is.True, $"{id} 행 없음 — 메뉴 Tools/UI/로비 무전 문구 추가");
            Assert.That(row.Text, Is.Not.Empty, id);
        }
    }
}
