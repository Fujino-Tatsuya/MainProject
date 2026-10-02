using System.Collections.Generic;
using NUnit.Framework;

// 테이블 Play 중 저장 차단(PLAN R8) — 테이블이 덮어쓴 에셋만 빼고 나머지는 그대로 저장한다.
public sealed class DataTableSaveGuardTests
{
    [Test]
    public void FilterSaves_RemovesGuardedPaths_KeepsOthers()
    {
        var guarded = new HashSet<string> { "Assets/2.Prefabs/Player/Gunner/Player_Gunner.prefab" };
        string[] paths = { "Assets/2.Prefabs/Player/Gunner/Player_Gunner.prefab", "Assets/0.Scenes/Other.unity" };

        string[] allowed = DataSourcePlayMode.FilterSaves(paths, guarded, out string[] blocked);

        Assert.That(allowed, Is.EqualTo(new[] { "Assets/0.Scenes/Other.unity" }));
        Assert.That(blocked, Is.EqualTo(new[] { "Assets/2.Prefabs/Player/Gunner/Player_Gunner.prefab" }));
    }

    [Test]
    public void FilterSaves_NothingGuarded_PassesThrough()
    {
        string[] paths = { "Assets/A.asset" };

        string[] allowed = DataSourcePlayMode.FilterSaves(paths, new HashSet<string>(), out string[] blocked);

        Assert.That(allowed, Is.SameAs(paths));
        Assert.That(blocked, Is.Empty);
    }
}
