using NUnit.Framework;
using UnityEngine;

/// <summary>대시 방향 옵션(할 일 25) — 모드 × 조준·이동 입력·정면 → 대시 방향.</summary>
public sealed class DashDirectionRulesTests
{
    static readonly Vector3 Aim = new Vector3(1f, 0f, 0f);
    static readonly Vector3 Move = new Vector3(0f, 0f, -1f);
    static readonly Vector3 Facing = new Vector3(-1f, 0f, 0f);
    static readonly Vector3 Forward = new Vector3(0f, 0f, 1f);

    [Test]
    public void Cursor_UsesAimFirst()
    {
        AssertVector3(Aim, DashDirectionRules.Resolve(DashDirectionMode.Cursor, Aim, Move, Facing, Forward));
    }

    [Test]
    public void Cursor_NoAim_FallsBackToMoveInput()
    {
        AssertVector3(Move, DashDirectionRules.Resolve(DashDirectionMode.Cursor, Vector3.zero, Move, Facing, Forward));
    }

    [Test]
    public void Cursor_NoAimNoInput_FallsBackToFacing()
    {
        AssertVector3(Facing, DashDirectionRules.Resolve(DashDirectionMode.Cursor, Vector3.zero, Vector3.zero, Facing, Forward));
    }

    [Test]
    public void CharacterFacing_IgnoresAim_UsesMoveInput()
    {
        AssertVector3(Move, DashDirectionRules.Resolve(DashDirectionMode.CharacterFacing, Aim, Move, Facing, Forward));
    }

    [Test]
    public void CharacterFacing_NoInput_UsesFacingEvenWithAim()
    {
        AssertVector3(Facing, DashDirectionRules.Resolve(DashDirectionMode.CharacterFacing, Aim, Vector3.zero, Facing, Forward));
    }

    [Test]
    public void NoFacing_FallsBackToTransformForward()
    {
        AssertVector3(Forward, DashDirectionRules.Resolve(DashDirectionMode.CharacterFacing, Aim, Vector3.zero, Vector3.zero, Forward));
    }

    [Test]
    public void Nothing_FallsBackToWorldForward()
    {
        AssertVector3(Vector3.forward, DashDirectionRules.Resolve(
            DashDirectionMode.Cursor, Vector3.zero, Vector3.zero, Vector3.zero, Vector3.up));
    }

    [Test]
    public void Result_IsPlanarAndNormalized()
    {
        Vector3 result = DashDirectionRules.Resolve(
            DashDirectionMode.Cursor, new Vector3(3f, 5f, 4f), Vector3.zero, Vector3.zero, Vector3.zero);

        AssertVector3(new Vector3(0.6f, 0f, 0.8f), result);
    }

    [Test]
    public void DefaultMode_IsCharacterFacing()
    {
        Assert.AreEqual(DashDirectionMode.CharacterFacing, default(DashDirectionMode));
    }

    static void AssertVector3(Vector3 expected, Vector3 actual)
    {
        Assert.AreEqual(expected.x, actual.x, 0.0001f, "x");
        Assert.AreEqual(expected.y, actual.y, 0.0001f, "y");
        Assert.AreEqual(expected.z, actual.z, 0.0001f, "z");
    }
}
