using NUnit.Framework;
using UnityEngine;

// 할 일 276 — 로비 상단 플레이어 슬롯 A안 상태 규칙(순수 함수).
public sealed class LobbyPlayerSlotViewTests
{
    [TestCase(false, false, LobbySlotState.Disconnected)]
    [TestCase(false, true, LobbySlotState.Disconnected)]
    [TestCase(true, false, LobbySlotState.Waiting)]
    [TestCase(true, true, LobbySlotState.Ready)]
    public void StateOf_MapsConnectedAndReady(bool connected, bool ready, LobbySlotState expected)
    {
        Assert.That(LobbyPlayerSlotView.StateOf(connected, ready), Is.EqualTo(expected));
    }

    [Test]
    public void StatusString_KeysAndKoreanFallbacks()
    {
        Assert.That(LobbyPlayerSlotView.StatusString(LobbySlotState.Ready).Ko, Is.EqualTo("준비 완료"));
        Assert.That(LobbyPlayerSlotView.StatusString(LobbySlotState.Waiting).Ko, Is.EqualTo("준비 중"));
        Assert.That(LobbyPlayerSlotView.StatusString(LobbySlotState.Disconnected).Ko, Is.EqualTo("미접속"));
        Assert.That(LobbyUiStrings.All, Has.Member(LobbyUiStrings.SlotEmptyName));
    }

    // assembly.json 아이콘 위치 − 캡슐 위치(0.402, -131.4225), 크기 = 디자인 sizeDelta
    [Test]
    public void StatusIconLayout_MatchesAssembly()
    {
        var capsule = new Vector2(0.402f, -131.4225f);
        var check = LobbyPlayerSlotView.StatusIconLayout(LobbySlotState.Ready);
        Assert.That(Vector2.Distance(check.position + capsule, new Vector2(-46.098f, -130.4225f)), Is.LessThan(0.6f));
        Assert.That(check.size, Is.EqualTo(new Vector2(22f, 19f)));
        var dot = LobbyPlayerSlotView.StatusIconLayout(LobbySlotState.Waiting);
        Assert.That(Vector2.Distance(dot.position + capsule, new Vector2(-47.598f, -131.4225f)), Is.LessThan(0.6f));
        Assert.That(dot.size, Is.EqualTo(new Vector2(11f, 11f)));
        var dash = LobbyPlayerSlotView.StatusIconLayout(LobbySlotState.Disconnected);
        Assert.That(dash.size, Is.EqualTo(new Vector2(15f, 7f)));
    }
}
