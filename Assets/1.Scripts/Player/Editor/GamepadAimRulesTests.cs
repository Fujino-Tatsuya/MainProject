using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>할 일 278 — 패드 조준 혼합(스틱 판정·월드 변환·기준 방향·자동 보정 선택).</summary>
public sealed class GamepadAimRulesTests
{
    const float Eps = 1e-4f;

    [Test]
    public void StickActive_OnlyAtOrAboveDeadzone()
    {
        Assert.IsFalse(GamepadAimRules.IsStickActive(Vector2.zero, 0.3f));
        Assert.IsFalse(GamepadAimRules.IsStickActive(new Vector2(0.2f, 0.2f), 0.3f)); // 0.28
        Assert.IsTrue(GamepadAimRules.IsStickActive(new Vector2(0.31f, 0f), 0.3f)); // 경계 바로 위(정확히 0.3 은 float 오차)
        Assert.IsTrue(GamepadAimRules.IsStickActive(new Vector2(0f, -1f), 0.3f));
    }

    [Test]
    public void StickToWorld_UsesViewYaw_LikeMovement()
    {
        AssertVec(Vector3.forward, GamepadAimRules.StickToWorld(new Vector2(0f, 1f), 0f));
        AssertVec(Vector3.right, GamepadAimRules.StickToWorld(new Vector2(1f, 0f), 0f));
        // 이동과 같은 식: Euler(0, yaw, 0) * (x, 0, y)
        Vector3 expected = (Quaternion.Euler(0f, -45f, 0f) * new Vector3(0.5f, 0f, 0.5f)).normalized;
        AssertVec(expected, GamepadAimRules.StickToWorld(new Vector2(0.5f, 0.5f), -45f));
        AssertVec(Vector3.zero, GamepadAimRules.StickToWorld(Vector2.zero, -45f));
    }

    [Test]
    public void BaseDirection_MoveThenLastAimThenFacing()
    {
        AssertVec(Vector3.right, GamepadAimRules.BaseDirection(Vector3.right * 3f, Vector3.forward, Vector3.back));
        AssertVec(Vector3.forward, GamepadAimRules.BaseDirection(Vector3.zero, Vector3.forward, Vector3.back));
        AssertVec(Vector3.back, GamepadAimRules.BaseDirection(Vector3.zero, Vector3.zero, Vector3.back));
        AssertVec(Vector3.forward, GamepadAimRules.BaseDirection(Vector3.zero, Vector3.zero, Vector3.up)); // 수직뿐이면 정면 기본
    }

    [Test]
    public void TryScore_RejectsOutsideConeOrRange()
    {
        Assert.IsTrue(GamepadAimRules.TryScore(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 5f), 10f, 45f, out float straight));
        Assert.AreEqual(0.5f, straight, Eps); // 각도 0 + 거리 5/10
        Assert.IsFalse(GamepadAimRules.TryScore(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 11f), 10f, 45f, out _));
        Assert.IsFalse(GamepadAimRules.TryScore(Vector3.zero, Vector3.forward, new Vector3(5f, 0f, 1f), 10f, 45f, out _));
        Assert.IsTrue(GamepadAimRules.TryScore(Vector3.zero, Vector3.forward, Vector3.zero, 10f, 45f, out float overlap));
        Assert.AreEqual(0f, overlap, Eps);
    }

    [Test]
    public void SelectAssist_PrefersCloserAngleAndDistance_SkipsUnselectable()
    {
        var list = new List<GamepadAimCandidate<string>>
        {
            new GamepadAimCandidate<string>("dead", new Vector3(0f, 0f, 2f), false),
            new GamepadAimCandidate<string>("far", new Vector3(0f, 0f, 9.8f), true), // 0 + 0.98 > near-off 0.59 + 0.34
            new GamepadAimCandidate<string>("near-off", new Vector3(1.5f, 0f, 3f), true),
            new GamepadAimCandidate<string>("behind", new Vector3(0f, 0f, -2f), true),
        };

        int index = GamepadAimRules.SelectAssist(list, Vector3.zero, Vector3.forward, 10f, 45f);
        Assert.AreEqual("near-off", list[index].Item);
    }

    [Test]
    public void SelectAssist_NoneInCone_ReturnsMinusOne()
    {
        var list = new List<GamepadAimCandidate<string>>
        {
            new GamepadAimCandidate<string>("left", new Vector3(-5f, 0f, 0f), true),
        };
        Assert.AreEqual(-1, GamepadAimRules.SelectAssist(list, Vector3.zero, Vector3.forward, 10f, 45f));
        Assert.AreEqual(-1, GamepadAimRules.SelectAssist<string>(null, Vector3.zero, Vector3.forward, 10f, 45f));
    }

    static void AssertVec(Vector3 expected, Vector3 actual)
    {
        Assert.AreEqual(expected.x, actual.x, Eps);
        Assert.AreEqual(expected.y, actual.y, Eps);
        Assert.AreEqual(expected.z, actual.z, Eps);
    }
}

/// <summary>할 일 278 — 단일 대상 패드 후보 순환(돌아감·튕김 엣지·왼→오른쪽 정렬·처음 = 가장 가까운 것·유지).</summary>
public sealed class GamepadTargetCycleTests
{
    [Test]
    public void Step_WrapsBothWays()
    {
        Assert.AreEqual(-1, GamepadTargetCycle.Step(0, 0, 1));
        Assert.AreEqual(0, GamepadTargetCycle.Step(3, -1, 1));
        Assert.AreEqual(1, GamepadTargetCycle.Step(3, 0, 1));
        Assert.AreEqual(0, GamepadTargetCycle.Step(3, 2, 1));
        Assert.AreEqual(2, GamepadTargetCycle.Step(3, 0, -1));
    }

    [Test]
    public void FlickStep_FiresOncePerFlick_AndNeedsRearm()
    {
        bool armed = true;
        Assert.AreEqual(0, GamepadTargetCycle.FlickStep(ref armed, 0.4f));
        Assert.AreEqual(1, GamepadTargetCycle.FlickStep(ref armed, 0.8f));
        Assert.AreEqual(0, GamepadTargetCycle.FlickStep(ref armed, 0.9f)); // 계속 기울임 — 한 번만
        Assert.AreEqual(0, GamepadTargetCycle.FlickStep(ref armed, 0.4f)); // 아직 다시 장전 아님
        Assert.AreEqual(0, GamepadTargetCycle.FlickStep(ref armed, -0.7f)); // 장전 전 반대쪽도 무시
        Assert.AreEqual(0, GamepadTargetCycle.FlickStep(ref armed, 0.1f)); // 장전
        Assert.AreEqual(-1, GamepadTargetCycle.FlickStep(ref armed, -0.7f));
    }

    [Test]
    public void BuildOrder_LeftToRight_FiltersCone()
    {
        var list = new List<GamepadAimCandidate<string>>
        {
            new GamepadAimCandidate<string>("right", new Vector3(3f, 0f, 5f), true),
            new GamepadAimCandidate<string>("left", new Vector3(-3f, 0f, 5f), true),
            new GamepadAimCandidate<string>("center", new Vector3(0f, 0f, 6f), true),
            new GamepadAimCandidate<string>("behind", new Vector3(0f, 0f, -3f), true),
            new GamepadAimCandidate<string>("dead", new Vector3(0f, 0f, 3f), false),
        };
        var ordered = new List<GamepadAimCandidate<string>>();
        GamepadTargetCycle.BuildOrder(list, Vector3.zero, Vector3.forward, 10f, 60f, ordered);

        CollectionAssert.AreEqual(new[] { "left", "center", "right" }, ordered.ConvertAll(c => c.Item));
    }

    [Test]
    public void Reselect_KeepsPrevious_ElseNearest()
    {
        var ordered = new List<GamepadAimCandidate<string>>
        {
            new GamepadAimCandidate<string>("a", new Vector3(-3f, 0f, 8f), true),
            new GamepadAimCandidate<string>("b", new Vector3(0f, 0f, 2f), true),
            new GamepadAimCandidate<string>("c", new Vector3(3f, 0f, 8f), true),
        };

        Assert.AreEqual(1, GamepadTargetCycle.Reselect(ordered, Vector3.zero, null, false)); // 처음 = 가장 가까운 b
        Assert.AreEqual(2, GamepadTargetCycle.Reselect(ordered, Vector3.zero, "c", true));
        Assert.AreEqual(1, GamepadTargetCycle.Reselect(ordered, Vector3.zero, "gone", true));
        Assert.AreEqual(-1, GamepadTargetCycle.Reselect(new List<GamepadAimCandidate<string>>(), Vector3.zero, "c", true));
    }
}

/// <summary>할 일 278 — 장치 모드 전환(마지막 장치·마우스 우선·축 엣지·마우스 떨림 무시).</summary>
public sealed class InputDeviceModeRulesTests
{
    [Test]
    public void Next_LastDeviceWins_MouseWinsTie_NoInputKeeps()
    {
        Assert.AreEqual(InputDeviceKind.Gamepad, InputDeviceModeRules.Next(InputDeviceKind.MouseKeyboard, false, true));
        Assert.AreEqual(InputDeviceKind.MouseKeyboard, InputDeviceModeRules.Next(InputDeviceKind.Gamepad, true, false));
        Assert.AreEqual(InputDeviceKind.MouseKeyboard, InputDeviceModeRules.Next(InputDeviceKind.Gamepad, true, true));
        Assert.AreEqual(InputDeviceKind.Gamepad, InputDeviceModeRules.Next(InputDeviceKind.Gamepad, false, false));
        Assert.AreEqual(InputDeviceKind.MouseKeyboard, InputDeviceModeRules.Next(InputDeviceKind.MouseKeyboard, false, false));
    }

    [Test]
    public void AxisEdge_OnlyOnCrossing()
    {
        bool over = false;
        Assert.IsFalse(InputDeviceModeRules.AxisEdge(ref over, 0.2f));
        Assert.IsTrue(InputDeviceModeRules.AxisEdge(ref over, 0.5f));
        Assert.IsFalse(InputDeviceModeRules.AxisEdge(ref over, 0.9f)); // 들고 있는 동안은 다시 주장하지 않는다
        Assert.IsFalse(InputDeviceModeRules.AxisEdge(ref over, 0.1f));
        Assert.IsTrue(InputDeviceModeRules.AxisEdge(ref over, 0.4f));
    }

    [Test]
    public void MouseMove_IgnoresJitter()
    {
        Assert.IsFalse(InputDeviceModeRules.IsMouseMove(new Vector2(1f, 1f)));
        Assert.IsTrue(InputDeviceModeRules.IsMouseMove(new Vector2(2f, 0f)));
    }

    [Test]
    public void Set_RaisesChangedOnlyOnChange()
    {
        InputDeviceKind original = InputDeviceMode.Current;
        int calls = 0;
        void Count(InputDeviceKind _) => calls++;
        InputDeviceMode.Changed += Count;
        try
        {
            InputDeviceMode.Set(InputDeviceKind.MouseKeyboard);
            int baseline = calls;
            InputDeviceMode.Set(InputDeviceKind.Gamepad);
            InputDeviceMode.Set(InputDeviceKind.Gamepad);
            Assert.IsTrue(InputDeviceMode.IsGamepad);
            Assert.AreEqual(baseline + 1, calls);
        }
        finally
        {
            InputDeviceMode.Changed -= Count;
            InputDeviceMode.Set(original);
        }
    }
}
