using NUnit.Framework;
using UnityEngine;

/// <summary>상호작용 프롬프트 소유권 고정 — 마지막 Show 우선, 소유자만 Hide, 파괴된 대상·소유자 정리.</summary>
public sealed class InteractPromptSlotTests
{
    GameObject targetA;
    GameObject targetB;

    [SetUp]
    public void SetUp()
    {
        targetA = new GameObject("PromptTargetA");
        targetB = new GameObject("PromptTargetB");
        targetA.transform.position = new Vector3(1f, 0f, 2f);
    }

    [TearDown]
    public void TearDown()
    {
        if (targetA != null) Object.DestroyImmediate(targetA);
        if (targetB != null) Object.DestroyImmediate(targetB);
    }

    [Test]
    public void Show_AnchorIsTargetPlusWorldOffset()
    {
        var slot = new InteractPromptSlot();
        slot.Show("gate", targetA.transform, Vector3.up * 1.8f);

        Assert.That(slot.TryGetWorldAnchor(out Vector3 anchor), Is.True);
        Assert.That(anchor.x, Is.EqualTo(1f));
        Assert.That(anchor.y, Is.EqualTo(1.8f).Within(1e-5f));
        Assert.That(anchor.z, Is.EqualTo(2f));
    }

    [Test]
    public void Hide_ByOtherOwner_KeepsPrompt()
    {
        var slot = new InteractPromptSlot();
        slot.Show("gate", targetA.transform, Vector3.zero);

        Assert.That(slot.Hide("chest"), Is.False);
        Assert.That(slot.IsShownBy("gate"), Is.True);
    }

    [Test]
    public void Hide_ByOwner_ClearsPrompt()
    {
        var slot = new InteractPromptSlot();
        slot.Show("gate", targetA.transform, Vector3.zero);

        Assert.That(slot.Hide("gate"), Is.True);
        Assert.That(slot.IsShowing, Is.False);
        Assert.That(slot.TryGetWorldAnchor(out _), Is.False);
    }

    [Test]
    public void LastShowWins_AndPreviousOwnerCannotHideIt()
    {
        var slot = new InteractPromptSlot();
        slot.Show("gate", targetA.transform, Vector3.zero);
        slot.Show("revive", targetB.transform, Vector3.zero);

        Assert.That(slot.IsShownBy("revive"), Is.True);
        Assert.That(slot.Hide("gate"), Is.False);
        Assert.That(slot.Target, Is.SameAs(targetB.transform));
    }

    [Test]
    public void Show_WithNullTarget_HidesOwnersPrompt()
    {
        var slot = new InteractPromptSlot();
        slot.Show("gate", targetA.transform, Vector3.zero);
        slot.Show("gate", null, Vector3.zero);

        Assert.That(slot.IsShowing, Is.False);
    }

    [Test]
    public void Show_WithNullOwner_IsIgnored()
    {
        var slot = new InteractPromptSlot();
        slot.Show(null, targetA.transform, Vector3.zero);

        Assert.That(slot.IsShowing, Is.False);
    }

    [Test]
    public void DestroyedTarget_ClearsOnNextAnchorQuery()
    {
        var slot = new InteractPromptSlot();
        slot.Show("gate", targetA.transform, Vector3.zero);
        Object.DestroyImmediate(targetA);

        Assert.That(slot.TryGetWorldAnchor(out _), Is.False);
        Assert.That(slot.IsShowing, Is.False);
    }

    [Test]
    public void DestroyedUnityOwner_ClearsOnNextAnchorQuery()
    {
        var slot = new InteractPromptSlot();
        var owner = new GameObject("PromptOwner");
        slot.Show(owner, targetA.transform, Vector3.zero);
        Object.DestroyImmediate(owner);

        Assert.That(slot.TryGetWorldAnchor(out _), Is.False);
        Assert.That(slot.IsShowing, Is.False);
    }
}
