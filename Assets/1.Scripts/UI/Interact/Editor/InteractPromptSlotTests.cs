using NUnit.Framework;
using UnityEngine;

/// <summary>상호작용 프롬프트 소유권 고정 — 마지막 Show 우선, 소유자만 Hide, 다른 뷰로 Show 시 이전 뷰 숨김, 누락, 파괴된 뷰·소유자 정리.</summary>
public sealed class InteractPromptSlotTests
{
    InteractPromptView viewA;
    InteractPromptView viewB;

    [SetUp]
    public void SetUp()
    {
        viewA = new GameObject("PromptViewA").AddComponent<InteractPromptView>();
        viewB = new GameObject("PromptViewB").AddComponent<InteractPromptView>();
    }

    [TearDown]
    public void TearDown()
    {
        if (viewA != null) Object.DestroyImmediate(viewA.gameObject);
        if (viewB != null) Object.DestroyImmediate(viewB.gameObject);
    }

    [Test]
    public void Show_DisplaysGivenView()
    {
        var slot = new InteractPromptSlot();
        slot.Show("gate", viewA);

        Assert.That(slot.View, Is.SameAs(viewA));
        Assert.That(slot.IsDisplaying(viewA), Is.True);
        Assert.That(slot.IsDisplaying(viewB), Is.False);
    }

    [Test]
    public void Hide_ByOtherOwner_KeepsPrompt()
    {
        var slot = new InteractPromptSlot();
        slot.Show("gate", viewA);

        Assert.That(slot.Hide("chest"), Is.False);
        Assert.That(slot.IsShownBy("gate"), Is.True);
    }

    [Test]
    public void Hide_ByOwner_ClearsPrompt()
    {
        var slot = new InteractPromptSlot();
        slot.Show("gate", viewA);

        Assert.That(slot.Hide("gate"), Is.True);
        Assert.That(slot.IsShowing, Is.False);
        Assert.That(slot.View, Is.Null);
        Assert.That(slot.IsDisplaying(viewA), Is.False);
    }

    [Test]
    public void LastShowWins_AndPreviousOwnerCannotHideIt()
    {
        var slot = new InteractPromptSlot();
        slot.Show("gate", viewA);
        slot.Show("revive", viewB);

        Assert.That(slot.IsShownBy("revive"), Is.True);
        Assert.That(slot.Hide("gate"), Is.False);
        Assert.That(slot.View, Is.SameAs(viewB));
    }

    [Test]
    public void ShowWithOtherView_PreviousViewNoLongerDraws()
    {
        var slot = new InteractPromptSlot();
        slot.Show("gate", viewA);
        slot.Show("gate", viewB);

        Assert.That(slot.IsDisplaying(viewA), Is.False);
        Assert.That(slot.IsDisplaying(viewB), Is.True);
    }

    [Test]
    public void Show_WithNullView_HidesOwnersPromptOnly()
    {
        var slot = new InteractPromptSlot();
        slot.Show("gate", viewA);
        slot.Show("chest", null);

        Assert.That(slot.IsShownBy("gate"), Is.True);

        slot.Show("gate", null);
        Assert.That(slot.IsShowing, Is.False);
    }

    [Test]
    public void Show_WithNullOwner_IsIgnored()
    {
        var slot = new InteractPromptSlot();
        slot.Show(null, viewA);

        Assert.That(slot.IsShowing, Is.False);
    }

    [Test]
    public void DestroyedView_ClearsOnNextQuery()
    {
        var slot = new InteractPromptSlot();
        slot.Show("gate", viewA);
        Object.DestroyImmediate(viewA.gameObject);

        Assert.That(slot.IsDisplaying(viewB), Is.False);
        Assert.That(slot.IsShowing, Is.False);
    }

    [Test]
    public void DestroyedUnityOwner_ClearsOnNextQuery()
    {
        var slot = new InteractPromptSlot();
        var owner = new GameObject("PromptOwner");
        slot.Show(owner, viewA);
        Object.DestroyImmediate(owner);

        Assert.That(slot.IsDisplaying(viewA), Is.False);
        Assert.That(slot.IsShowing, Is.False);
    }
}
