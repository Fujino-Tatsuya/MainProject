using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 몬스터 머리 위 상태 이펙트 표(<c>Resources/MonsterStatusEffectVfxSet</c>) 테스트(팀장 10-26 — 팔라딘 Q 밀림·경직 = 기절 이펙트).
///
/// 왜 — 표는 런타임에 Resources 로 읽는다(<see cref="MonsterStatusEffectVfxPresenter.TryInstall"/>). 경로가 바뀌거나
/// 엔트리가 비면 경고 한 줄만 남기고 이펙트가 조용히 빠진다. 그 둘을 여기서 고정한다.
/// </summary>
public sealed class MonsterStatusEffectVfxSetTests
{
    [Test]
    public void ResourceSet_Loads()
    {
        Assert.That(Resources.Load<StatusEffectVfxSet>(MonsterStatusEffectVfxPresenter.ResourcePath), Is.Not.Null);
    }

    [Test]
    public void ResourceSet_HasStunnedOverheadWithEffect()
    {
        var set = Resources.Load<StatusEffectVfxSet>(MonsterStatusEffectVfxPresenter.ResourcePath);
        Assume.That(set, Is.Not.Null);

        StatusEffectVfxSet.Row stunned = null;
        foreach (StatusEffectVfxSet.Row row in set.Rows)
            if (row != null && row.status == StatusEffectType.Stunned)
                stunned = row;

        Assert.That(stunned, Is.Not.Null, "기절 줄이 없다 — Q 밀림·경직에 이펙트가 안 뜬다");
        Assert.That(stunned.effect, Is.Not.Null, "기절 줄의 엔트리가 비었다");
        Assert.That(stunned.anchor, Is.EqualTo(StatusEffectVfxSet.Anchor.Overhead));
    }
}
