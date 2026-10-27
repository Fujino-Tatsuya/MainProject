using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public sealed class CooldownCoverBodyTests
{
    const float Eps = 1e-5f;

    [Test]
    public void CanvasFill_Zero_IsEmpty()
    {
        Assert.That(CooldownCoverBody.CanvasFill(0f, 0.2f, 0.8f), Is.EqualTo(0f));
    }

    [Test]
    public void CanvasFill_One_IsBodyEnd()
    {
        Assert.That(CooldownCoverBody.CanvasFill(1f, 0.2f, 0.8f), Is.EqualTo(0.8f).Within(Eps));
    }

    [Test]
    public void CanvasFill_Half_IsBodyMiddle()
    {
        Assert.That(CooldownCoverBody.CanvasFill(0.5f, 0.2f, 0.8f), Is.EqualTo(0.5f).Within(Eps));
        Assert.That(CooldownCoverBody.CanvasFill(0.25f, 0.2f, 0.8f), Is.EqualTo(0.35f).Within(Eps));
    }

    [TestCase(-0.5f, 0f)]
    [TestCase(1.5f, 0.8f)]
    public void CanvasFill_ClampsOutOfRange(float remaining, float expected)
    {
        Assert.That(CooldownCoverBody.CanvasFill(remaining, 0.2f, 0.8f), Is.EqualTo(expected).Within(Eps));
    }

    [Test]
    public void CanvasFill_JustAboveZero_StartsAtBodyEdge()
    {
        Assert.That(CooldownCoverBody.CanvasFill(1e-6f, 0.2f, 0.8f), Is.EqualTo(0.2f).Within(1e-4f));
    }

    // 실측 본체 y21..94(포함), 캔버스 101, 아래에서 채움 → 본체 = 아래에서 6..80.
    [TestCase(1f, 80f / 101f)]
    [TestCase(1e-6f, 6f / 101f)]
    [TestCase(0.5f, 43f / 101f)]
    [TestCase(0f, 0f)]
    public void Apply_SkillBody_FromBottom(float remaining, float expected)
    {
        Assert.That(ApplyTo(CooldownCoverBody.SkillBody, Image.OriginVertical.Bottom, remaining),
            Is.EqualTo(expected).Within(1e-4f));
    }

    // 본체 y7..94 → 아래에서 6..94.
    [TestCase(1f, 94f / 101f)]
    [TestCase(1e-6f, 6f / 101f)]
    public void Apply_SideBody_FromBottom(float remaining, float expected)
    {
        Assert.That(ApplyTo(CooldownCoverBody.SideBody, Image.OriginVertical.Bottom, remaining),
            Is.EqualTo(expected).Within(1e-4f));
    }

    // 위에서 채우면 본체 = 위에서 21..95.
    [TestCase(1f, 95f / 101f)]
    [TestCase(1e-6f, 21f / 101f)]
    public void Apply_SkillBody_FromTop(float remaining, float expected)
    {
        Assert.That(ApplyTo(CooldownCoverBody.SkillBody, Image.OriginVertical.Top, remaining),
            Is.EqualTo(expected).Within(1e-4f));
    }

    [Test]
    public void Apply_EmptyBody_PassesRemainingThrough()
    {
        Assert.That(ApplyTo(default, Image.OriginVertical.Bottom, 0.3f), Is.EqualTo(0.3f).Within(Eps));
    }

    static float ApplyTo(CooldownCoverBody body, Image.OriginVertical origin, float remaining)
    {
        var go = new GameObject("cover", typeof(RectTransform));
        try
        {
            var image = go.AddComponent<Image>();
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Vertical;
            image.fillOrigin = (int)origin;
            body.Apply(image, remaining);
            return image.fillAmount;
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
