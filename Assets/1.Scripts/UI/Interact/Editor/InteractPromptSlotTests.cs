using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>상호작용 프롬프트 소유권·인스턴스 고정 — 마지막 Show 우선, 소유자만 Hide, 프리팹별 인스턴스 재사용·재생성, 누락, 파괴된 대상·소유자 정리.</summary>
public sealed class InteractPromptSlotTests
{
    GameObject targetA;
    GameObject targetB;
    InteractPromptView prefabA;
    InteractPromptView prefabB;
    readonly List<GameObject> created = new List<GameObject>();
    int instantiateCount;

    [SetUp]
    public void SetUp()
    {
        targetA = new GameObject("PromptTargetA");
        targetB = new GameObject("PromptTargetB");
        targetA.transform.position = new Vector3(1f, 0f, 2f);
        prefabA = new GameObject("PromptPrefabA").AddComponent<InteractPromptView>();
        prefabB = new GameObject("PromptPrefabB").AddComponent<InteractPromptView>();
        instantiateCount = 0;
    }

    [TearDown]
    public void TearDown()
    {
        if (targetA != null) Object.DestroyImmediate(targetA);
        if (targetB != null) Object.DestroyImmediate(targetB);
        if (prefabA != null) Object.DestroyImmediate(prefabA.gameObject);
        if (prefabB != null) Object.DestroyImmediate(prefabB.gameObject);
        foreach (GameObject go in created)
            if (go != null) Object.DestroyImmediate(go);
        created.Clear();
    }

    // 실제 서비스는 Object.Instantiate — 테스트는 호출 수만 세는 가짜 인스턴스로 대신한다.
    InteractPromptSlot NewSlot() => new InteractPromptSlot(prefab =>
    {
        instantiateCount++;
        var go = new GameObject($"{prefab.name}(Instance)");
        created.Add(go);
        return go.AddComponent<InteractPromptView>();
    });

    [Test]
    public void Show_AnchorIsTargetPlusWorldOffset()
    {
        var slot = NewSlot();
        slot.Show("gate", prefabA, targetA.transform, Vector3.up * 1.8f);

        Assert.That(slot.TryGetWorldAnchor(slot.View, out Vector3 anchor), Is.True);
        Assert.That(anchor.x, Is.EqualTo(1f));
        Assert.That(anchor.y, Is.EqualTo(1.8f).Within(1e-5f));
        Assert.That(anchor.z, Is.EqualTo(2f));
    }

    [Test]
    public void Show_DrawsOnInstance_NotOnPrefab()
    {
        var slot = NewSlot();
        slot.Show("gate", prefabA, targetA.transform, Vector3.zero);

        Assert.That(slot.View, Is.Not.SameAs(prefabA));
        Assert.That(slot.TryGetWorldAnchor(prefabA, out _), Is.False);
    }

    [Test]
    public void SamePrefab_ReusesOneInstance_AcrossOwnersAndTargets()
    {
        var slot = NewSlot();
        slot.Show("gateZone1", prefabA, targetA.transform, Vector3.zero);
        InteractPromptView first = slot.View;
        slot.Hide("gateZone1");
        slot.Show("gateZone2", prefabA, targetB.transform, Vector3.zero);

        Assert.That(slot.View, Is.SameAs(first));
        Assert.That(instantiateCount, Is.EqualTo(1));
    }

    [Test]
    public void DestroyedInstance_IsRecreatedOnNextShow()
    {
        var slot = NewSlot();
        slot.Show("gate", prefabA, targetA.transform, Vector3.zero);
        Object.DestroyImmediate(slot.View.gameObject);

        slot.Show("gate", prefabA, targetA.transform, Vector3.zero);

        Assert.That(instantiateCount, Is.EqualTo(2));
        Assert.That(slot.View != null, Is.True);
        Assert.That(slot.TryGetWorldAnchor(slot.View, out _), Is.True);
    }

    [Test]
    public void Hide_ByOtherOwner_KeepsPrompt()
    {
        var slot = NewSlot();
        slot.Show("gate", prefabA, targetA.transform, Vector3.zero);

        Assert.That(slot.Hide("chest"), Is.False);
        Assert.That(slot.IsShownBy("gate"), Is.True);
    }

    [Test]
    public void Hide_ByOwner_ClearsPrompt()
    {
        var slot = NewSlot();
        slot.Show("gate", prefabA, targetA.transform, Vector3.zero);
        InteractPromptView instance = slot.View;

        Assert.That(slot.Hide("gate"), Is.True);
        Assert.That(slot.IsShowing, Is.False);
        Assert.That(slot.View, Is.Null);
        Assert.That(slot.TryGetWorldAnchor(instance, out _), Is.False);
    }

    [Test]
    public void LastShowWins_AndPreviousOwnerCannotHideIt()
    {
        var slot = NewSlot();
        slot.Show("gate", prefabA, targetA.transform, Vector3.zero);
        slot.Show("revive", prefabA, targetB.transform, Vector3.zero);

        Assert.That(slot.IsShownBy("revive"), Is.True);
        Assert.That(slot.Hide("gate"), Is.False);
        Assert.That(slot.Target, Is.SameAs(targetB.transform));
    }

    [Test]
    public void ShowWithOtherPrefab_PreviousInstanceNoLongerDraws()
    {
        var slot = NewSlot();
        slot.Show("gate", prefabA, targetA.transform, Vector3.zero);
        InteractPromptView instanceA = slot.View;
        slot.Show("revive", prefabB, targetB.transform, Vector3.zero);

        Assert.That(slot.TryGetWorldAnchor(instanceA, out _), Is.False);
        Assert.That(slot.TryGetWorldAnchor(slot.View, out _), Is.True);
        Assert.That(instantiateCount, Is.EqualTo(2));
    }

    [Test]
    public void Show_WithNullTarget_HidesOwnersPrompt()
    {
        var slot = NewSlot();
        slot.Show("gate", prefabA, targetA.transform, Vector3.zero);
        slot.Show("gate", prefabA, null, Vector3.zero);

        Assert.That(slot.IsShowing, Is.False);
    }

    [Test]
    public void Show_WithNullPrefab_HidesOwnersPromptOnly()
    {
        var slot = NewSlot();
        slot.Show("gate", prefabA, targetA.transform, Vector3.zero);
        slot.Show("chest", null, targetB.transform, Vector3.zero);

        Assert.That(slot.IsShownBy("gate"), Is.True);

        slot.Show("gate", null, targetA.transform, Vector3.zero);
        Assert.That(slot.IsShowing, Is.False);
        Assert.That(instantiateCount, Is.EqualTo(1));
    }

    [Test]
    public void Show_WithNullOwner_IsIgnored()
    {
        var slot = NewSlot();
        slot.Show(null, prefabA, targetA.transform, Vector3.zero);

        Assert.That(slot.IsShowing, Is.False);
        Assert.That(instantiateCount, Is.EqualTo(0));
    }

    [Test]
    public void DestroyedTarget_ClearsOnNextAnchorQuery()
    {
        var slot = NewSlot();
        slot.Show("gate", prefabA, targetA.transform, Vector3.zero);
        InteractPromptView instance = slot.View;
        Object.DestroyImmediate(targetA);

        Assert.That(slot.TryGetWorldAnchor(instance, out _), Is.False);
        Assert.That(slot.IsShowing, Is.False);
    }

    [Test]
    public void DestroyedUnityOwner_ClearsOnNextAnchorQuery()
    {
        var slot = NewSlot();
        var owner = new GameObject("PromptOwner");
        slot.Show(owner, prefabA, targetA.transform, Vector3.zero);
        InteractPromptView instance = slot.View;
        Object.DestroyImmediate(owner);

        Assert.That(slot.TryGetWorldAnchor(instance, out _), Is.False);
        Assert.That(slot.IsShowing, Is.False);
    }
}
