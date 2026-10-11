using NUnit.Framework;
using UnityEngine;

/// <summary>할 일 15 S6 — 자막 큐(순서·지속시간·만료·Clear).</summary>
public sealed class TutorialGuideSubtitleQueueTests
{
    static TutorialSubtitle Line(string id, float seconds) => new TutorialSubtitle(id, id, seconds);

    [Test]
    public void FirstEnqueue_BecomesCurrentImmediately()
    {
        var queue = new TutorialSubtitleQueue();

        Assert.IsTrue(queue.Enqueue(Line("A", 5f)));
        Assert.IsFalse(queue.Enqueue(Line("B", 5f)));
        Assert.AreEqual("A", queue.Current.Id);
        Assert.AreEqual(1, queue.PendingCount);
    }

    [Test]
    public void Overlapping_PlaysInOrderWithOwnDuration()
    {
        var queue = new TutorialSubtitleQueue();
        queue.Enqueue(Line("A", 5f));
        queue.Enqueue(Line("B", 8f));

        Assert.IsFalse(queue.Advance(4.9f));
        Assert.AreEqual("A", queue.Current.Id);

        Assert.IsTrue(queue.Advance(0.2f));
        Assert.AreEqual("B", queue.Current.Id);
        Assert.AreEqual(0.1f, queue.Elapsed, 1e-4f, "남은 시간을 다음 자막에 이어 쓴다");

        queue.Advance(7.8f);
        Assert.AreEqual("B", queue.Current.Id);
    }

    [Test]
    public void LastLine_ExpiresToEmpty()
    {
        var queue = new TutorialSubtitleQueue();
        queue.Enqueue(Line("A", 5f));

        Assert.IsTrue(queue.Advance(5f));
        Assert.IsFalse(queue.HasCurrent);
        Assert.AreEqual(0f, queue.Remaining);

        Assert.IsTrue(queue.Enqueue(Line("B", 6f)), "빈 큐에 넣으면 바로 뜬다");
    }

    [Test]
    public void ZeroDuration_UsesDefault()
    {
        Assert.AreEqual(TutorialSubtitleQueue.DefaultDurationSeconds, Line("A", 0f).DurationSeconds);
        Assert.AreEqual(TutorialSubtitleQueue.DefaultDurationSeconds, Line("A", -1f).DurationSeconds);
    }

    [Test]
    public void Clear_DropsCurrentAndPending()
    {
        var queue = new TutorialSubtitleQueue();
        queue.Enqueue(Line("A", 5f));
        queue.Enqueue(Line("B", 5f));

        queue.Clear();

        Assert.IsFalse(queue.HasCurrent);
        Assert.AreEqual(0, queue.PendingCount);
        Assert.IsFalse(queue.Advance(10f));
    }
}

/// <summary>할 일 15 S6 — 목표·대기·강조 상태와 RebuildFromState(자막 미복원).</summary>
public sealed class TutorialGuideModelTests
{
    [Test]
    public void Rebuild_RestoresStateButNotSubtitles()
    {
        var model = new TutorialGuideModel();
        model.Subtitles.Enqueue(new TutorialSubtitle("TUT_S0_WELCOME", "환영", 6f));
        model.Subtitles.Enqueue(new TutorialSubtitle("TUT_Z1_MOVE", "이동", 6f));

        model.Rebuild(new TutorialGuideState
        {
            ObjectiveId = "TUT_Z2_GOAL",
            Checklist = new[] { new TutorialChecklistItem("Q", true), new TutorialChecklistItem("E", false) },
            Progress = new TutorialProgress(1, 3),
            Wait = new TutorialWaitState(1, 3, 42f),
            Highlights = new[] { new TutorialHighlightRequest(TutorialHudTarget.SkillSlotQ, TutorialHighlightStyle.Pulse) },
        });

        Assert.IsFalse(model.Subtitles.HasCurrent);
        Assert.AreEqual(0, model.Subtitles.PendingCount);
        Assert.AreEqual("TUT_Z2_GOAL", model.ObjectiveId);
        Assert.AreEqual(2, model.Checklist.Count);
        Assert.IsTrue(model.Checklist[0].Done);
        Assert.AreEqual("1/3", model.Progress.ToString());
        Assert.IsTrue(model.Wait.Active);
        Assert.AreEqual(42f, model.Wait.SecondsLeft);
        Assert.AreEqual(1, model.Highlights.Count);
        Assert.AreEqual(TutorialHudTarget.SkillSlotQ, model.Highlights[0].HudTarget);
    }

    [Test]
    public void SetObjective_ReportsStepChangeOnlyWhenIdChanges()
    {
        var model = new TutorialGuideModel();

        Assert.IsTrue(model.SetObjective("TUT_Z1_MOVE", null, null, new TutorialProgress(0, 9)));
        Assert.IsFalse(model.SetObjective("TUT_Z1_MOVE", null, null, new TutorialProgress(3, 9)));
        Assert.AreEqual("3/9", model.Progress.ToString());
        Assert.IsTrue(model.SetObjective("TUT_Z2_GOAL", null, null, default));
        Assert.AreEqual(string.Empty, model.Progress.ToString(), "Total 0 = 진행도 숨김");
    }

    [Test]
    public void Checklist_IsCopied()
    {
        var model = new TutorialGuideModel();
        var items = new[] { new TutorialChecklistItem("A", false) };

        model.SetObjective("X", null, items, default);
        items[0].Done = true;

        Assert.IsFalse(model.Checklist[0].Done);
    }

    [Test]
    public void TickWait_StopsAtZero()
    {
        var model = new TutorialGuideModel();
        model.SetWait(1, 3, 1.5f);

        model.TickWait(1f);
        Assert.AreEqual(0.5f, model.Wait.SecondsLeft, 1e-4f);
        model.TickWait(5f);
        Assert.AreEqual(0f, model.Wait.SecondsLeft);

        model.ClearWait();
        Assert.IsFalse(model.Wait.Active);
    }

    [Test]
    public void Highlight_SameTargetReplacesStyle_ClearRemoves()
    {
        var model = new TutorialGuideModel();
        var go = new GameObject("StairsMarker");
        try
        {
            model.AddHighlight(new TutorialHighlightRequest(TutorialHudTarget.SkillSlotE, TutorialHighlightStyle.Pulse));
            model.AddHighlight(new TutorialHighlightRequest(TutorialHudTarget.SkillSlotE, TutorialHighlightStyle.Arrow));
            model.AddHighlight(new TutorialHighlightRequest(go.transform, TutorialHighlightStyle.Pulse));

            Assert.AreEqual(2, model.Highlights.Count);
            Assert.AreEqual(TutorialHighlightStyle.Arrow, model.Highlights[0].Style);

            Assert.IsTrue(model.RemoveHighlight(new TutorialHighlightRequest(go.transform, default)));
            Assert.AreEqual(1, model.Highlights.Count);

            model.ClearHighlights();
            Assert.AreEqual(0, model.Highlights.Count);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void Clear_ResetsEverything()
    {
        var model = new TutorialGuideModel();
        model.Subtitles.Enqueue(new TutorialSubtitle("A", "a", 6f));
        model.SetObjective("X", null, new[] { new TutorialChecklistItem("A", false) }, new TutorialProgress(0, 4));
        model.SetWait(0, 3, 10f);
        model.AddHighlight(new TutorialHighlightRequest(TutorialHudTarget.BossTimer, TutorialHighlightStyle.Pulse));

        model.Clear();

        Assert.IsFalse(model.Subtitles.HasCurrent);
        Assert.IsFalse(model.HasObjective);
        Assert.AreEqual(0, model.Checklist.Count);
        Assert.IsFalse(model.Progress.HasValue);
        Assert.IsFalse(model.Wait.Active);
        Assert.AreEqual(0, model.Highlights.Count);
    }
}
