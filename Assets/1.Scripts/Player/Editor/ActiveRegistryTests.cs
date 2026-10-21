using NUnit.Framework;

/// <summary>
/// 할 일 69 — 전멸 판정용 등록식 목록(<see cref="ActiveRegistry{T}"/>): 켜짐 등록·꺼짐 해제·중복 무시·초기화.
/// <c>PlayerLifeCycleController</c> 가 OnEnable/OnDisable 로 부르는 순서를 그대로 흉내 낸다. 원본: Docs/tech/party-lives-respawn.md §2.
/// </summary>
public sealed class ActiveRegistryTests
{
    sealed class Item { }

    [Test]
    public void EnableDisable_ListMatchesEnabledSet()
    {
        var registry = new ActiveRegistry<Item>();
        var a = new Item();
        var b = new Item();

        registry.Add(a);
        registry.Add(b);
        Assert.AreEqual(2, registry.Items.Count);

        // 디스폰·파괴·비활성 = OnDisable → 빠진다. 남은 쪽만 순회된다.
        registry.Remove(a);
        Assert.AreEqual(1, registry.Items.Count);
        Assert.AreSame(b, registry.Items[0]);
    }

    [Test]
    public void ReEnable_RegistersAgain_Once()
    {
        // 재합류·캐릭터 교체로 같은 인스턴스가 꺼졌다 켜져도 한 번만 센다.
        var registry = new ActiveRegistry<Item>();
        var a = new Item();

        registry.Add(a);
        registry.Add(a);
        Assert.AreEqual(1, registry.Items.Count);

        registry.Remove(a);
        registry.Add(a);
        Assert.AreEqual(1, registry.Items.Count);
    }

    [Test]
    public void RemoveUnknownOrNull_NoChange()
    {
        var registry = new ActiveRegistry<Item>();
        var a = new Item();
        registry.Add(a);
        registry.Add(null);

        registry.Remove(new Item());
        registry.Remove(null);
        Assert.AreEqual(1, registry.Items.Count);
    }

    [Test]
    public void Clear_EmptiesStaleEntries()
    {
        // 도메인 리로드 없는 Play 진입(SubsystemRegistration)에서 지난 세션 항목을 버린다. 0명 = 전멸 아님.
        var registry = new ActiveRegistry<Item>();
        registry.Add(new Item());
        registry.Clear();
        Assert.AreEqual(0, registry.Items.Count);
    }
}
