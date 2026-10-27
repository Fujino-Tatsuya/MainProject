using System.Collections.Generic;
using NUnit.Framework;

/// <summary>로비 예외 상황 단축키 Alt+숫자 — 할 일 112(키 표·호스트 전용·멈춤 탈출·리로드 판정).</summary>
public sealed class LobbyRecoveryHotkeyRulesTests
{
    [TestCase(1, LobbyRecoveryAction.ToggleTrainingClear, false)]
    [TestCase(2, LobbyRecoveryAction.Unstuck, false)]
    [TestCase(3, LobbyRecoveryAction.ReloadLobby, true)]
    public void 키_표(int digit, LobbyRecoveryAction action, bool hostOnly)
    {
        Assert.That(LobbyRecoveryHotkeyRules.TryFromDigit(digit, out var hotkey), Is.True);
        Assert.That(hotkey.Action, Is.EqualTo(action));
        Assert.That(hotkey.HostOnly, Is.EqualTo(hostOnly));
        Assert.That(LobbyRecoveryHotkeyRules.TryGet(action, out var back), Is.True);
        Assert.That(back.Digit, Is.EqualTo(digit));
    }

    [TestCase(0)]
    [TestCase(4)]
    [TestCase(10)]
    [TestCase(-1)]
    public void 표에_없는_숫자는_무시(int digit)
    {
        Assert.That(LobbyRecoveryHotkeyRules.TryFromDigit(digit, out _), Is.False);
    }

    [Test]
    public void 표의_숫자와_동작은_겹치지_않고_0에서_9_사이()
    {
        var digits = new HashSet<int>();
        var actions = new HashSet<LobbyRecoveryAction>();
        foreach (var row in LobbyRecoveryHotkeyRules.Table)
        {
            Assert.That(row.Digit, Is.InRange(0, 9));
            Assert.That(row.Action, Is.Not.EqualTo(LobbyRecoveryAction.None));
            Assert.That(digits.Add(row.Digit), Is.True, $"숫자 {row.Digit} 중복");
            Assert.That(actions.Add(row.Action), Is.True, $"동작 {row.Action} 중복");
            Assert.That(row.Label, Is.Not.Empty);
        }
    }

    [Test]
    public void 호스트_전용은_클라가_보내지_않는다()
    {
        Assert.That(LobbyRecoveryHotkeyRules.CanRequest(LobbyRecoveryAction.ReloadLobby, false), Is.False);
        Assert.That(LobbyRecoveryHotkeyRules.CanRequest(LobbyRecoveryAction.ReloadLobby, true), Is.True);
        Assert.That(LobbyRecoveryHotkeyRules.CanRequest(LobbyRecoveryAction.ToggleTrainingClear, false), Is.True);
        Assert.That(LobbyRecoveryHotkeyRules.CanRequest(LobbyRecoveryAction.Unstuck, false), Is.True);
        Assert.That(LobbyRecoveryHotkeyRules.CanRequest(LobbyRecoveryAction.None, true), Is.False);
    }

    [TestCase(false, false, false, LobbyRecoveryResult.NoPlayer)]
    [TestCase(false, true, true, LobbyRecoveryResult.NoPlayer)]
    [TestCase(true, true, false, LobbyRecoveryResult.BriefingLocked)]
    [TestCase(true, true, true, LobbyRecoveryResult.BriefingLocked)]
    [TestCase(true, false, true, LobbyRecoveryResult.SceneTransition)]
    [TestCase(true, false, false, LobbyRecoveryResult.Unstuck)]
    public void 멈춤_탈출_판정(bool hasPlayer, bool briefingRunning, bool leaving, LobbyRecoveryResult expected)
    {
        Assert.That(LobbyRecoveryHotkeyRules.UnstuckVerdict(hasPlayer, briefingRunning, leaving), Is.EqualTo(expected));
    }

    [TestCase(false, false, LobbyRecoveryResult.HostOnly)]
    [TestCase(false, true, LobbyRecoveryResult.HostOnly)]
    [TestCase(true, true, LobbyRecoveryResult.SceneTransition)]
    [TestCase(true, false, LobbyRecoveryResult.Reloading)]
    public void 리로드_판정(bool isHost, bool leaving, LobbyRecoveryResult expected)
    {
        Assert.That(LobbyRecoveryHotkeyRules.ReloadVerdict(isHost, leaving), Is.EqualTo(expected));
    }

    [Test]
    public void 결과_문구는_키_번호를_앞에_붙인다()
    {
        Assert.That(LobbyRecoveryHotkeyRules.Describe(LobbyRecoveryAction.ReloadLobby, LobbyRecoveryResult.HostOnly), Does.StartWith("Alt+3: "));
        Assert.That(LobbyRecoveryHotkeyRules.Describe(LobbyRecoveryAction.ToggleTrainingClear, LobbyRecoveryResult.TrainingClearOn), Does.StartWith("Alt+1: "));
        foreach (LobbyRecoveryResult result in System.Enum.GetValues(typeof(LobbyRecoveryResult)))
            Assert.That(LobbyRecoveryHotkeyRules.Describe(LobbyRecoveryAction.Unstuck, result), Is.Not.Empty);
    }
}
