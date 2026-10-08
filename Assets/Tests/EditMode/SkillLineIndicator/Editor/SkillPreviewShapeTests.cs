using NUnit.Framework;
using UnityEngine;

public sealed class SkillPreviewShapeTests
{
    [Test]
    public void ChargeLaser_UsesMinimumFillAndMaximumGhost()
    {
        SkillPreviewShape shape = SkillPreviewShapes.ChargeLaser(6f, 16f, 1.2f, 6f, 16f);

        Assert.That(shape.HasFill, Is.True);
        Assert.That(shape.Fill.StartOffset, Is.EqualTo(0f));
        Assert.That(shape.Fill.Length, Is.EqualTo(6f));
        Assert.That(shape.Fill.Width, Is.EqualTo(1.2f));
        Assert.That(shape.HasGhost, Is.True);
        Assert.That(shape.Ghost.Length, Is.EqualTo(16f));
        Assert.That(shape.Ghost.Width, Is.EqualTo(1.2f));
    }

    [Test]
    public void ChargeLaser_ClampsEachLayerToItsPhysicsClip()
    {
        SkillPreviewShape shape = SkillPreviewShapes.ChargeLaser(6f, 16f, 1.2f, 4.25f, 9.5f);

        Assert.That(shape.Fill.Length, Is.EqualTo(4.25f));
        Assert.That(shape.Ghost.Length, Is.EqualTo(9.5f));
    }

    [Test]
    public void GunnerInterruptBox_MatchesAuthoredFrontRange()
    {
        SkillPreviewShape shape = SkillPreviewShapes.Hitbox(
            new Vector3(0f, 0f, 0.9f),
            new Vector3(1.4f, 1.4f, 1.4f));

        Assert.That(shape.Fill.StartOffset, Is.EqualTo(0.2f).Within(0.0001f));
        Assert.That(shape.Fill.Length, Is.EqualTo(1.4f));
        Assert.That(shape.Fill.Width, Is.EqualTo(1.4f));
    }

    [Test]
    public void PaladinInterruptBox_MatchesAuthoredFrontRange()
    {
        SkillPreviewShape shape = SkillPreviewShapes.Hitbox(
            new Vector3(0f, 0f, 0.9f),
            new Vector3(1.6f, 1f, 1.6f));

        Assert.That(shape.Fill.StartOffset, Is.EqualTo(0.1f).Within(0.0001f));
        Assert.That(shape.Fill.Length, Is.EqualTo(1.6f));
        Assert.That(shape.Fill.Width, Is.EqualTo(1.6f));
    }

    [Test]
    public void BackwardArrow_UsesNegativeDirectionAndClippedLength()
    {
        SkillPreviewShape shape = SkillPreviewShapes.Arrow(-1f, 3f, 1.75f);

        Assert.That(shape.HasArrow, Is.True);
        Assert.That(shape.ArrowDirection, Is.EqualTo(-1f));
        Assert.That(shape.ArrowLength, Is.EqualTo(1.75f));
    }

    [Test]
    public void InterruptPreview_CombinesHitboxAndBackwardArrow()
    {
        SkillPreviewShape shape = SkillPreviewShapes.HitboxWithArrow(
            new Vector3(0f, 0f, 0.9f),
            new Vector3(1.4f, 1.4f, 1.4f),
            -1f,
            1.5f,
            0.8f);

        Assert.That(shape.HasFill, Is.True);
        Assert.That(shape.HasArrow, Is.True);
        Assert.That(shape.ArrowDirection, Is.EqualTo(-1f));
        Assert.That(shape.ArrowLength, Is.EqualTo(0.8f));
    }
}
