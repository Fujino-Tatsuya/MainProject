using NUnit.Framework;
using UnityEditor;

// 숫자 글꼴 에셋이 시트 슬라이스와 맞는지 확인한다. 원본 시트는 1·2·…·9·0 순서라 숫자 d 의 슬라이스는
// d == 0 ? _9 : _(d-1) 이다. 아트(SVN)가 다시 잘리면 internalID 가 바뀌어 참조가 조용히 끊기므로 여기서 잡는다.
public sealed class FloatingDamageDigitSetTests
{
    const string AssetPath = "Assets/9.ScriptableObject/UI/FloatingDamageDigitSet.asset";

    [Test]
    public void EveryDigit_MapsToMatchingSlice()
    {
        var set = AssetDatabase.LoadAssetAtPath<FloatingDamageDigitSet>(AssetPath);
        Assert.That(set, Is.Not.Null, AssetPath);

        for (int digit = 0; digit < FloatingDamageDigitSet.DigitCount; digit++)
        {
            int slice = digit == 0 ? 9 : digit - 1;

            Assert.That(set.GetFill(digit), Is.Not.Null, $"fill {digit}");
            Assert.That(set.GetOutline(digit), Is.Not.Null, $"outline {digit}");
            Assert.That(set.GetFill(digit).name, Is.EqualTo($"damage_fill_{slice}"), $"fill {digit}");
            Assert.That(set.GetOutline(digit).name, Is.EqualTo($"damage_outline_{slice}"), $"outline {digit}");
        }
    }

    [Test]
    public void Settings_ReferencesDigitSet()
    {
        var settings = AssetDatabase.LoadAssetAtPath<FloatingDamageSettings>(
            "Assets/9.ScriptableObject/UI/FloatingDamageSettings.asset");

        Assert.That(settings, Is.Not.Null);
        Assert.That(settings.DigitSet, Is.Not.Null);
    }
}
