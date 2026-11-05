using NUnit.Framework;
using UnityEngine;

public sealed class No23GrabVfxRulesTests
{
    [Test]
    public void Cone_RangeBoundaryAndFrontBackAgreeWithGrab()
    {
        Assert.That(No23GrabVfxRules.Contains(Vector3.zero, Vector3.forward, Vector3.forward * 8f, 8f, 90f));
        Assert.That(No23GrabVfxRules.Contains(Vector3.zero, Vector3.forward, Vector3.forward * 8.001f, 8f, 90f), Is.False);
        Assert.That(No23GrabVfxRules.Contains(Vector3.zero, Vector3.forward, Vector3.back, 8f, 90f), Is.False);
    }

    [TestCase(44.99f, true)]
    [TestCase(-44.99f, true)]
    [TestCase(45.01f, false)]
    [TestCase(-45.01f, false)]
    public void Cone_BothAngularEdges(float degrees, bool inside)
    {
        float angle = degrees * Mathf.Deg2Rad;
        Vector3 point = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * 4f;
        Assert.That(No23GrabVfxRules.Contains(Vector3.zero, Vector3.forward, point, 8f, 90f), Is.EqualTo(inside));
    }

    [Test]
    public void Cone_IgnoresHeightAndUsesBossFacing()
    {
        Vector3 origin = new Vector3(30f, 2f, -10f);
        Assert.That(No23GrabVfxRules.Contains(origin, Vector3.right, origin + new Vector3(4f, 15f, 0f), 8f, 90f));
        Assert.That(No23GrabVfxRules.Contains(origin, Vector3.right, origin + Vector3.forward * 4f, 8f, 90f), Is.False);
    }

    [Test]
    public void Cone_ZeroFacingAndAtOriginAreDefined()
    {
        Assert.That(No23GrabVfxRules.Contains(Vector3.zero, Vector3.zero, Vector3.forward, 8f, 90f));
        Assert.That(No23GrabVfxRules.Contains(Vector3.zero, Vector3.forward, Vector3.zero, 8f, 90f));
    }

    [Test]
    public void Circle_DoesNotIgnoreRange()
    {
        Assert.That(No23GrabVfxRules.Contains(Vector3.zero, Vector3.forward, Vector3.back * 7f, 8f, 360f));
        Assert.That(No23GrabVfxRules.Contains(Vector3.zero, Vector3.forward, Vector3.back * 9f, 8f, 360f), Is.False);
    }

    [Test]
    public void Arrival_MirrorsGrabPullFrontOffset()
    {
        Vector3 point = No23GrabVfxRules.Arrival(new Vector3(10f, 2f, 3f), Vector3.forward, 3.74f);
        Assert.That(point.z, Is.EqualTo(5.244f).Within(0.00001f));
        Assert.That(point.y, Is.EqualTo(2f));
        Assert.That(No23GrabVfxRules.Arrival(Vector3.zero, Vector3.zero, 0f).z, Is.EqualTo(0.5f));
    }
}
