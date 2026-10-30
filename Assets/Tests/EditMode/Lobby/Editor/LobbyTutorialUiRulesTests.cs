using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>로비 튜토리얼 UI 순수 규칙 — 할 일 158. 상세 = Docs/tech/lobby-tutorial-ui.md.</summary>
public sealed class LobbyTutorialUiRulesTests
{
    [Test]
    public void PanelWidth_WideOnlyWithBody()
    {
        Assert.That(LobbyTutorialUiRules.PanelWidth(null, 560f, 680f), Is.EqualTo(560f));
        Assert.That(LobbyTutorialUiRules.PanelWidth(new LobbyObjectiveView(), 560f, 680f), Is.EqualTo(560f));
        Assert.That(LobbyTutorialUiRules.PanelWidth(new LobbyObjectiveView { BodyKeys = new[] { "", null } }, 560f, 680f), Is.EqualTo(560f));
        Assert.That(LobbyTutorialUiRules.PanelWidth(new LobbyObjectiveView { BodyKeys = new[] { "k" } }, 560f, 680f), Is.EqualTo(680f));
    }

    [Test]
    public void ShowProgress_NeedsKindAndValidNumbers()
    {
        Assert.That(LobbyTutorialUiRules.ShowProgress(LobbyTutorialProgress.None), Is.False);
        Assert.That(LobbyTutorialUiRules.ShowProgress(new LobbyTutorialProgress(LobbyTutorialProgressKind.Gather, 0, 3)), Is.True);
        Assert.That(LobbyTutorialUiRules.ShowProgress(new LobbyTutorialProgress(LobbyTutorialProgressKind.Gather, 0, 0)), Is.False);
        Assert.That(LobbyTutorialUiRules.ShowProgress(new LobbyTutorialProgress(LobbyTutorialProgressKind.TrainingDone, 1, 2)), Is.True);
        Assert.That(LobbyTutorialUiRules.ShowProgress(new LobbyTutorialProgress(LobbyTutorialProgressKind.Countdown, 3, 0)), Is.True);
        Assert.That(LobbyTutorialUiRules.ShowProgress(new LobbyTutorialProgress(LobbyTutorialProgressKind.Countdown, 0, 0)), Is.False);
    }

    [Test]
    public void ProgressArgs_WrapNumbersInGold()
    {
        var args = LobbyTutorialUiRules.ProgressArgs(new LobbyTutorialProgress(LobbyTutorialProgressKind.Gather, 2, 3), new Color32(0xF0, 0xC8, 0x70, 0xFF));
        Assert.That(args["n"], Is.EqualTo("<color=#F0C870>2</color>"));
        Assert.That(args["total"], Is.EqualTo("<color=#F0C870>3</color>"));
    }

    [Test]
    public void CheckRow_DoneIsDarkerWithStrikethrough()
    {
        Assert.That(LobbyTutorialUiRules.FormatCheckRow("Q 스킬", false, "□", "■", Color.gray), Is.EqualTo("□ Q 스킬"));
        string done = LobbyTutorialUiRules.FormatCheckRow("Q 스킬", true, "□", "■", new Color32(0x80, 0x80, 0x80, 0xFF));
        Assert.That(done, Is.EqualTo("<color=#808080FF><s>■ Q 스킬</s></color>"));
    }

    [Test]
    public void Darken_KeepsAlpha()
    {
        Color c = LobbyTutorialUiRules.Darken(new Color(1f, 0.5f, 0.2f, 0.7f), 0.5f);
        Assert.That(c.r, Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(c.g, Is.EqualTo(0.25f).Within(1e-5f));
        Assert.That(c.b, Is.EqualTo(0.1f).Within(1e-5f));
        Assert.That(c.a, Is.EqualTo(0.7f).Within(1e-5f));
    }

    [Test]
    public void ReplaceTags_RendersKnownAndKeepsUnknown()
    {
        string Render(string id) => id == "Knockback" ? "[넉백]" : id == "Stun" ? "[기절]" : null;
        Assert.That(LobbyTutorialUiRules.ReplaceTags("A {tag:Knockback} 또는 {tag:Stun} B", Render), Is.EqualTo("A [넉백] 또는 [기절] B"));
        Assert.That(LobbyTutorialUiRules.ReplaceTags("{tag:Nope} x", Render), Is.EqualTo("{tag:Nope} x"));
        Assert.That(LobbyTutorialUiRules.ReplaceTags("끝 {tag:Knockback", Render), Is.EqualTo("끝 {tag:Knockback"));
        Assert.That(LobbyTutorialUiRules.ReplaceTags(null, Render), Is.EqualTo(string.Empty));
        Assert.That(LobbyTutorialUiRules.ReplaceTags("태그 없음", Render), Is.EqualTo("태그 없음"));
    }

    [Test]
    public void DialogueGate_HidesDuringDialogueAndGrace()
    {
        var gate = new LobbyDialogueObjectiveGate(0.3f);
        Assert.That(gate.IsObjectiveHidden(0d), Is.False);

        gate.BeginDialogue();
        Assert.That(gate.IsObjectiveHidden(1d), Is.True);

        gate.EndDialogue(2d);
        Assert.That(gate.IsObjectiveHidden(2.1d), Is.True, "유예 안 — 아직 숨김");
        Assert.That(gate.IsObjectiveHidden(2.31d), Is.False, "유예 뒤 — 다시 보임");
    }

    [Test]
    public void DialogueGate_NextLineInsideGraceKeepsHidden()
    {
        var gate = new LobbyDialogueObjectiveGate(0.3f);
        gate.BeginDialogue();
        gate.EndDialogue(1d);
        gate.BeginDialogue(); // 연속 대사 — 유예 안
        Assert.That(gate.IsObjectiveHidden(1.5d), Is.True);
        gate.Reset();
        Assert.That(gate.IsObjectiveHidden(1.5d), Is.False, "Reset = 유예 없이 해제");
    }

    [Test]
    public void Strings_KeysUniqueAndInUiStringsAll()
    {
        var keys = LobbyTutorialUiStrings.All.Select(s => s.Key).ToList();
        Assert.That(keys.Distinct().Count(), Is.EqualTo(keys.Count), "중복 키");
        var all = new HashSet<string>(UiStrings.All.Select(s => s.Key));
        foreach (string key in keys)
            Assert.That(all.Contains(key), Is.True, key + " 가 UiStrings.All 에 없음");
        Assert.That(LobbyTutorialUiStrings.BasicChecks.Count, Is.EqualTo(6));
    }

    [Test]
    public void Strings_TagAndProgressLookup()
    {
        Assert.That(LobbyTutorialUiStrings.TryGetTagLabel("knockback", out UiString kb) && kb.Key == LobbyTutorialUiStrings.TagKnockback.Key, Is.True);
        Assert.That(LobbyTutorialUiStrings.TryGetTagLabel("Suppress", out _), Is.True);
        Assert.That(LobbyTutorialUiStrings.TryGetTagLabel("Heal", out _), Is.False);
        Assert.That(LobbyTutorialUiStrings.TryGetProgress(LobbyTutorialProgressKind.None, out _), Is.False);
        Assert.That(LobbyTutorialUiStrings.TryGetProgress(LobbyTutorialProgressKind.Countdown, out UiString cd) && cd.Ko.Contains("{n}"), Is.True);
    }
}
