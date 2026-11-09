using NUnit.Framework;
using UnityEngine;

public sealed class NativeCursorPixelsTests
{
    [Test]
    public void ToTopDownBgra_FlipsRows_AndSwapsToBgra_KeepingStraightAlpha()
    {
        // 2×2 — Unity 행 0 = 아래.
        var pixels = new[]
        {
            new Color32(10, 20, 30, 40), new Color32(11, 21, 31, 41), // 아래 줄
            new Color32(50, 60, 70, 80), new Color32(51, 61, 71, 0)   // 위 줄
        };

        byte[] bgra = NativeCursorPixels.ToTopDownBgra(pixels, 2, 2);

        Assert.That(bgra, Is.EqualTo(new byte[]
        {
            70, 60, 50, 80, 71, 61, 51, 0,  // DIB 행 0 = 위 줄
            30, 20, 10, 40, 31, 21, 11, 41  // DIB 행 1 = 아래 줄
        }));
    }

    [Test]
    public void ScaledHotspot_MultipliesAndRounds()
    {
        Assert.That(NativeCursorPixels.ScaledHotspot(new Vector2(10f, 5f), 1.2f, 64, 64),
            Is.EqualTo(new Vector2Int(12, 6)));
    }

    [Test]
    public void ScaledHotspot_ClampsInsideScaledTexture()
    {
        Assert.That(NativeCursorPixels.ScaledHotspot(new Vector2(40f, -3f), 2f, 64, 64),
            Is.EqualTo(new Vector2Int(63, 0)));
    }
}
