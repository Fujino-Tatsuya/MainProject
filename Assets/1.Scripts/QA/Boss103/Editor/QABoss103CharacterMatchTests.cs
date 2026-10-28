#if UNITY_EDITOR
using NUnit.Framework;

/// <summary>할 일 129 — 자리 기대 캐릭터 vs 실제 스폰 프리팹 이름 불일치 판정(<see cref="QABoss103Plan.CharacterMismatch"/>).</summary>
public sealed class QABoss103CharacterMatchTests
{
    [Test]
    public void 일치하면_null()
    {
        Assert.IsNull(QABoss103Plan.CharacterMismatch(0, QABoss103Character.Paladin, "Player_Paladin"));
        Assert.IsNull(QABoss103Plan.CharacterMismatch(1, QABoss103Character.Assassin, "Player_Assassin"));
        Assert.IsNull(QABoss103Plan.CharacterMismatch(2, QABoss103Character.Gunner, "Player_Gunner"));
    }

    [Test]
    public void 불일치면_자리_기대_실제가_담긴_사유()
    {
        Assert.AreEqual("자리 2 캐릭터 불일치 — 기대 Gunner · 실제 Player_Paladin",
            QABoss103Plan.CharacterMismatch(2, QABoss103Character.Gunner, "Player_Paladin"));
    }

    [Test]
    public void Variant_프리팹_이름_매핑()
    {
        Assert.IsTrue(QABoss103Plan.TryCharacterOfPrefab("Player_Paladin", out QABoss103Character c));
        Assert.AreEqual(QABoss103Character.Paladin, c);
        Assert.IsTrue(QABoss103Plan.TryCharacterOfPrefab("Player_Assassin", out c));
        Assert.AreEqual(QABoss103Character.Assassin, c);
        Assert.IsTrue(QABoss103Plan.TryCharacterOfPrefab("player_gunner", out c));
        Assert.AreEqual(QABoss103Character.Gunner, c);
    }

    [Test]
    public void 알_수_없는_이름은_불일치()
    {
        Assert.IsFalse(QABoss103Plan.TryCharacterOfPrefab("Player", out _));
        Assert.IsFalse(QABoss103Plan.TryCharacterOfPrefab("Paladin", out _));
        Assert.IsFalse(QABoss103Plan.TryCharacterOfPrefab("Player_1", out _));
        Assert.IsFalse(QABoss103Plan.TryCharacterOfPrefab("(없음)", out _));
        Assert.IsFalse(QABoss103Plan.TryCharacterOfPrefab(null, out _));
        Assert.AreEqual("자리 0 캐릭터 불일치 — 기대 Paladin · 실제 Player",
            QABoss103Plan.CharacterMismatch(0, QABoss103Character.Paladin, "Player"));
    }
}
#endif
