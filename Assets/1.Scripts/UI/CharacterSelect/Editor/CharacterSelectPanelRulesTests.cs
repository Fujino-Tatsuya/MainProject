using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 로비 캐릭터 선택 패널 판단(할 일 48.2) — 첫 포커스·순환 이동·카드 자리·확정(잠금·불가·같은 캐릭터)·ESC·배경 흐름.
/// </summary>
public sealed class CharacterSelectPanelRulesTests
{
    static readonly bool[] AllAvailable = { true, true, true };

    static CharacterSelectPanelRules Opened(int current, bool locked = false, bool[] available = null)
    {
        var rules = new CharacterSelectPanelRules();
        rules.Open(current, locked, available ?? AllAvailable);
        return rules;
    }

    [Test]
    public void Open_FocusesCurrent_OrZeroWhenOutOfRange()
    {
        Assert.AreEqual(2, Opened(2).Focus);
        Assert.AreEqual(0, Opened(-1).Focus, "아직 고른 캐릭터가 없으면 0번");
        Assert.AreEqual(0, Opened(3).Focus, "범위 밖이면 0번");
        Assert.AreEqual(-1, CharacterSelectPanelRules.InitialFocus(0, 0), "카드가 없으면 -1");
        Assert.IsTrue(Opened(1).IsOpen);
    }

    [Test]
    public void MoveFocus_WrapsAtBothEnds()
    {
        var rules = Opened(0);

        Assert.IsTrue(rules.MoveFocus(-1));
        Assert.AreEqual(2, rules.Focus, "처음에서 위로 = 끝");
        Assert.IsTrue(rules.MoveFocus(1));
        Assert.AreEqual(0, rules.Focus, "끝에서 아래로 = 처음");
        Assert.IsTrue(rules.MoveFocus(4));
        Assert.AreEqual(1, rules.Focus);
    }

    [Test]
    public void MoveFocus_SingleCard_OrClosed_DoesNothing()
    {
        var single = Opened(0, available: new[] { true });
        Assert.IsFalse(single.MoveFocus(1), "한 장이면 포커스가 그대로");
        Assert.AreEqual(0, single.Focus);

        var closed = Opened(0);
        closed.Close();
        Assert.IsFalse(closed.MoveFocus(1));
        Assert.AreEqual(0, closed.Focus);
    }

    [Test]
    public void FocusCard_MovesToClickedCard_IgnoresOutOfRange()
    {
        var rules = Opened(0);

        Assert.IsTrue(rules.FocusCard(2));
        Assert.AreEqual(2, rules.Focus);
        Assert.IsFalse(rules.FocusCard(2), "같은 카드면 바뀐 것 없음");
        Assert.IsFalse(rules.FocusCard(5));
        Assert.IsFalse(rules.FocusCard(-1));
        Assert.AreEqual(2, rules.Focus);
    }

    [Test]
    public void SlotOffset_PreviousAboveNextBelow_Wraps()
    {
        // 3장, 포커스 0 → 2번이 위(-1), 1번이 아래(+1)
        Assert.AreEqual(0, CharacterSelectPanelRules.SlotOffset(0, 0, 3));
        Assert.AreEqual(1, CharacterSelectPanelRules.SlotOffset(1, 0, 3));
        Assert.AreEqual(-1, CharacterSelectPanelRules.SlotOffset(2, 0, 3));

        // 2장 — 다른 카드는 아래
        Assert.AreEqual(1, CharacterSelectPanelRules.SlotOffset(1, 0, 2));
        Assert.AreEqual(1, CharacterSelectPanelRules.SlotOffset(0, 1, 2));

        // 5장 — 포커스 0 이면 2·3번은 화면 밖
        var offsets = Enumerable.Range(0, 5).Select(i => CharacterSelectPanelRules.SlotOffset(i, 0, 5)).ToArray();
        CollectionAssert.AreEqual(new[] { 0, 1, 2, -2, -1 }, offsets);
    }

    [Test]
    public void Confirm_OtherAvailableCharacter_ChosenAndCloses()
    {
        var rules = Opened(0);
        rules.MoveFocus(1);

        Assert.AreEqual(CharacterSelectConfirmResult.Chosen, rules.Confirm());
        Assert.IsFalse(rules.IsOpen);
        Assert.AreEqual(1, rules.Focus, "고른 id = 포커스");
        Assert.AreEqual(CharacterSelectConfirmResult.NotOpen, rules.Confirm(), "닫힌 뒤에는 다시 고르지 않는다");
    }

    [Test]
    public void Confirm_SameAsCurrent_ClosesWithoutChoice()
    {
        var rules = Opened(1);

        Assert.AreEqual(CharacterSelectConfirmResult.Unchanged, rules.Confirm());
        Assert.IsFalse(rules.IsOpen);
    }

    [Test]
    public void Confirm_Locked_RejectsAndStaysOpen_EvenForCurrent()
    {
        var rules = Opened(0, locked: true);

        Assert.IsTrue(rules.MoveFocus(1), "잠겨도 구경(포커스 이동)은 된다");
        Assert.AreEqual(CharacterSelectConfirmResult.Locked, rules.Confirm());
        Assert.IsTrue(rules.IsOpen);

        rules.FocusCard(0);
        Assert.AreEqual(CharacterSelectConfirmResult.Locked, rules.Confirm(), "잠금이 지금 캐릭터 판단보다 먼저");
        Assert.IsTrue(rules.IsOpen);
    }

    [Test]
    public void Confirm_Unavailable_RejectsAndStaysOpen()
    {
        var rules = Opened(0, available: new[] { true, false, true });
        rules.MoveFocus(1);

        Assert.AreEqual(CharacterSelectConfirmResult.Unavailable, rules.Confirm());
        Assert.IsTrue(rules.IsOpen);
        Assert.IsFalse(rules.IsAvailable(1));
    }

    [Test]
    public void SetLocked_UnlocksWhileOpen()
    {
        var rules = Opened(0, locked: true);
        rules.MoveFocus(1);
        Assert.AreEqual(CharacterSelectConfirmResult.Locked, rules.Confirm());

        Assert.IsTrue(rules.SetLocked(false));
        Assert.IsFalse(rules.SetLocked(false), "같은 값이면 바뀐 것 없음");
        Assert.AreEqual(CharacterSelectConfirmResult.Chosen, rules.Confirm());
    }

    [Test]
    public void Confirm_EmptyRoster_IsEmpty()
    {
        var rules = new CharacterSelectPanelRules();
        rules.Open(0, false, new bool[0]);

        Assert.IsTrue(rules.IsOpen);
        Assert.AreEqual(-1, rules.Focus);
        Assert.AreEqual(CharacterSelectConfirmResult.Empty, rules.Confirm());
        Assert.IsTrue(rules.IsOpen);
    }

    [Test]
    public void Escape_IgnoredWhenClaimedByResultPanel()
    {
        var rules = Opened(0);

        Assert.IsFalse(rules.HandleEscape(true), "결과 패널이 ESC 를 쓰면 같이 닫히지 않는다");
        Assert.IsTrue(rules.IsOpen);
        Assert.IsTrue(rules.HandleEscape(false));
        Assert.IsFalse(rules.IsOpen);
        Assert.IsFalse(rules.HandleEscape(false), "닫혀 있을 때 ESC 는 패널이 쓰지 않는다");
    }

    [Test]
    public void BackButton_And_Close_OnlyOnce()
    {
        var rules = Opened(0);

        Assert.IsTrue(rules.HandleBackButton());
        Assert.IsFalse(rules.HandleBackButton());
        Assert.IsFalse(rules.Close());
    }

    [Test]
    public void Reopen_ResetsFocusAndLock()
    {
        var rules = Opened(0, locked: true);
        rules.MoveFocus(1);
        rules.Close();

        rules.Open(2, false, AllAvailable);
        Assert.IsTrue(rules.IsOpen);
        Assert.IsFalse(rules.IsLocked);
        Assert.AreEqual(2, rules.Focus);
    }

    [Test]
    public void BackgroundScroll_PhaseWrapsAndGridCoversScreen()
    {
        var tile = new Vector2(2900f, 1888f);
        Vector2 velocity = new Vector2(-1f, 0.3f).normalized * 40f; // 대각선도 확인

        // 26° 돌린 1920×1080 화면을 감싸는 상자 — 2×2 타일로 덮을 수 있어야 한다.
        Vector2 half = CharacterSelectBackgroundScroll.RotatedHalfExtents(new Vector2(1920f, 1080f), 26f);
        Assert.IsTrue(CharacterSelectBackgroundScroll.TryCoveringOrigin(tile, half, out Vector2 origin));

        foreach (float t in new[] { 0f, 1f, 72.5f, 1000f, 123456f })
        {
            Vector2 phase = CharacterSelectBackgroundScroll.Phase(velocity, t, tile);
            Assert.That(phase.x, Is.InRange(0f, tile.x));
            Assert.That(phase.y, Is.InRange(0f, tile.y));

            // 묶음(가운데 원점, y 위)이 회전 좌표의 화면 상자를 다 덮는다.
            Vector2 grid = CharacterSelectBackgroundScroll.GridPosition(phase, tile, origin);
            Assert.LessOrEqual(grid.x, -half.x);
            Assert.GreaterOrEqual(grid.x + tile.x * 2f, half.x);
            Assert.GreaterOrEqual(grid.y, half.y);
            Assert.LessOrEqual(grid.y - tile.y * 2f, -half.y);
        }

        Assert.IsFalse(CharacterSelectBackgroundScroll.TryCoveringOrigin(new Vector2(1000f, 600f), half, out _),
            "타일이 작으면 덮을 수 없다고 알린다");

        Vector2 oneSecond = CharacterSelectBackgroundScroll.Phase(velocity, 1f, tile);
        Vector2 zero = CharacterSelectBackgroundScroll.Phase(velocity, 0f, tile);
        float dx = Mathf.Repeat(oneSecond.x - zero.x + tile.x * 0.5f, tile.x) - tile.x * 0.5f;
        float dy = Mathf.Repeat(oneSecond.y - zero.y + tile.y * 0.5f, tile.y) - tile.y * 0.5f;
        Assert.AreEqual(40f, new Vector2(dx, dy).magnitude, 1e-2f, "1초에 40px");
    }
}
