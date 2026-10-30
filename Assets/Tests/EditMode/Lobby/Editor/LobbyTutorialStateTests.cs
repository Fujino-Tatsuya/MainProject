using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>로비 튜토리얼 파사드 — 할 일 157 계약 §1·§2(Publish 이벤트 순서·대사 보기·패널 숨김·벽 해제).</summary>
public sealed class LobbyTutorialStateTests
{
    LobbyTutorialStepTable _table;
    readonly List<string> _log = new List<string>();

    static LobbyTutorialStep Step(string id, LobbyTutorialStepKind kind, string dialogueId = null, string wall = null) =>
        new LobbyTutorialStep { stateId = id, kind = kind, dialogueId = dialogueId, targetWallId = wall, gatherPoint = LobbyGatherPoint.Sortie };

    static LobbyTutorialSnapshot Snap(int revision, int step, double start = 0, double end = 0) =>
        new LobbyTutorialSnapshot { RunId = 1, Revision = revision, StepIndex = (byte)step, TimerStartServerTime = start, TimerEndServerTime = end };

    [SetUp]
    public void SetUp()
    {
        LobbyTutorialState.ResetForTests();
        _log.Clear();
        _table = ScriptableObject.CreateInstance<LobbyTutorialStepTable>();
        _table.EditorSetSteps(new[]
        {
            Step("G", LobbyTutorialStepKind.Gather),
            Step("D1", LobbyTutorialStepKind.Dialogue, "DLG_1"),
            Step("D2", LobbyTutorialStepKind.Dialogue, "DLG_2"),
            Step("W", LobbyTutorialStepKind.Activity, wall: "WALL_A"),
            Step("L", LobbyTutorialStepKind.Loading),
        });
        LobbyTutorialState.StepChanged += (prev, next, rev) => _log.Add($"step {prev.StateId}>{next.StateId}@{rev}");
        LobbyTutorialState.DialogueStarted += d => _log.Add($"start {d.DialogueId}");
        LobbyTutorialState.DialogueEnded += (id, rev) => _log.Add($"end {id}@{rev}");
        LobbyTutorialState.TargetWallCleared += wall => _log.Add($"wall {wall}");
    }

    [TearDown]
    public void TearDown()
    {
        LobbyTutorialState.ResetForTests();
        Object.DestroyImmediate(_table);
    }

    [Test]
    public void Publish_전엔_사용_불가이고_패널_숨김()
    {
        Assert.That(LobbyTutorialState.IsAvailable, Is.False);
        Assert.That(LobbyTutorialState.ObjectivePanelVisible, Is.False);
        Assert.That(LobbyTutorialState.ActiveGatherPointId, Is.EqualTo(LobbyGatherPoint.None));
        Assert.That(LobbyTutorialState.TryGetProgress(out _, out _), Is.False);
    }

    [Test]
    public void 첫_Publish_는_StepChanged_한번()
    {
        LobbyTutorialState.Publish(_table, Snap(1, 0));

        Assert.That(LobbyTutorialState.IsAvailable, Is.True);
        Assert.That(LobbyTutorialState.CurrentStep.StateId, Is.EqualTo("G"));
        Assert.That(LobbyTutorialState.DisplayStateId, Is.EqualTo("G"));
        Assert.That(LobbyTutorialState.ActiveGatherPointId, Is.EqualTo(LobbyGatherPoint.Sortie));
        Assert.That(LobbyTutorialState.ObjectivePanelVisible, Is.True);
        Assert.That(_log, Is.EqualTo(new[] { "step >G@1" }));
    }

    [Test]
    public void 같은_revision_재Publish_는_이벤트_없이_시각만_갱신()
    {
        LobbyTutorialState.Publish(_table, Snap(2, 1));
        _log.Clear();
        LobbyTutorialState.Publish(_table, Snap(2, 1, 3, 6));

        Assert.That(_log, Is.Empty);
        Assert.That(LobbyTutorialState.Dialogue.StartServerTime, Is.EqualTo(3));
        Assert.That(LobbyTutorialState.Dialogue.EndServerTime, Is.EqualTo(6));
    }

    [Test]
    public void 연속_대사는_Ended_직후_Started_패널은_계속_숨김()
    {
        LobbyTutorialState.Publish(_table, Snap(1, 0));
        LobbyTutorialState.Publish(_table, Snap(2, 1));
        Assert.That(LobbyTutorialState.IsDialogueActive, Is.True);
        Assert.That(LobbyTutorialState.ObjectivePanelVisible, Is.False);

        _log.Clear();
        LobbyTutorialState.Publish(_table, Snap(3, 2));

        Assert.That(_log, Is.EqualTo(new[] { "end DLG_1@2", "step D1>D2@3", "start DLG_2" }));
        Assert.That(LobbyTutorialState.ObjectivePanelVisible, Is.False);
    }

    [Test]
    public void 벽_단계를_떠나면_벽_해제()
    {
        LobbyTutorialState.Publish(_table, Snap(4, 3));
        Assert.That(LobbyTutorialState.ActiveTargetWallId, Is.EqualTo("WALL_A"));

        _log.Clear();
        LobbyTutorialState.Publish(_table, Snap(5, 4));

        Assert.That(_log, Is.EqualTo(new[] { "step W>L@5", "wall WALL_A" }));
        Assert.That(LobbyTutorialState.ActiveTargetWallId, Is.Empty);
        Assert.That(LobbyTutorialState.ObjectivePanelVisible, Is.False);   // 로딩
    }

    [Test]
    public void 출격_확정_뒤엔_표식과_패널_정리()
    {
        var snap = Snap(1, 0);
        snap.LaunchCommitted = true;
        LobbyTutorialState.Publish(_table, snap);

        Assert.That(LobbyTutorialState.LaunchCommitted, Is.True);
        Assert.That(LobbyTutorialState.ObjectivePanelVisible, Is.False);
        Assert.That(LobbyTutorialState.ActiveGatherPointId, Is.EqualTo(LobbyGatherPoint.None));
    }

    [Test]
    public void 새_회차는_같은_revision_이어도_StepChanged()
    {
        LobbyTutorialState.Publish(_table, Snap(1, 0));
        _log.Clear();
        var next = Snap(1, 0);
        next.RunId = 2;
        LobbyTutorialState.Publish(_table, next);

        Assert.That(_log, Is.EqualTo(new[] { "step G>G@1" }));
        Assert.That(LobbyTutorialState.RunId, Is.EqualTo(2));
    }

    [Test]
    public void Clear_는_대사_중이면_Ended()
    {
        LobbyTutorialState.Publish(_table, Snap(2, 1));
        _log.Clear();
        LobbyTutorialState.Clear();

        Assert.That(_log, Is.EqualTo(new[] { "end DLG_1@2" }));
        Assert.That(LobbyTutorialState.IsAvailable, Is.False);
    }
}
