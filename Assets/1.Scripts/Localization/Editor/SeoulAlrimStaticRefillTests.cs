using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine.TextCore;

public sealed class SeoulAlrimStaticRefillTests
{
    [Test]
    public void ExistingCharacters_KeepsBmp_DropsOutsideBmpAndNull()
    {
        var table = new List<TMP_Character>
        {
            new TMP_Character(0xAC00, new Glyph()), // 가
            new TMP_Character(0x1F600, new Glyph()), // 😀 — char 하나로 못 담는다
            null,
            new TMP_Character(0x0041, new Glyph()), // A
        };

        Assert.That(SeoulAlrimStaticRefill.ExistingCharacters(table), Is.EquivalentTo(new[] { '가', 'A' }));
    }

    [Test]
    public void CharacterSet_IncludesCurrentAssetCharacters()
    {
        var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/SeoulAlrim/SeoulAlrimTTF-Medium SDF.asset");
        Assert.That(asset, Is.Not.Null);

        var sources = new List<(string name, int count)>();
        var set = new HashSet<char>(SeoulAlrimStaticRefill.CharacterSet(sources));

        Assert.That(sources.Any(s => s.name == "현재 에셋 글자" && s.count > 0), Is.True);
        string missing = new string(SeoulAlrimStaticRefill.ExistingCharacters(asset.characterTable).Where(c => !set.Contains(c)).ToArray());
        Assert.That(missing, Is.Empty, "에셋에 있는데 구울 집합에 빠진 글자(Bakeable 이 거른 것)");
    }
}
