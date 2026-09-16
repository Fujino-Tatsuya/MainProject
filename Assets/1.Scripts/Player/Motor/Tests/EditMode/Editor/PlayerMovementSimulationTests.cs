using NUnit.Framework;
using UnityEngine;

/// <summary>
/// `PlayerMovementSimulation`의 결정론/무부작용 테스트.
///
/// ⚠️ 이 폴더에는 .asmdef를 만들지 않는다. 대상 런타임 코드가 미리 정의된
/// `Assembly-CSharp`에 있어 asmdef 어셈블리에서 참조할 수 없기 때문이다.
/// `Editor/`의 `Assembly-CSharp-Editor` 경로로만 Test Runner에 포함한다.
/// </summary>
public sealed class PlayerMovementSimulationTests
{
    private const float DeltaTime = 0.02f;

    [Test]
    public void SameInitialStateAndInputSequence_ProducesBitIdenticalState()
    {
        PlayerSimulationState first = CreateInitialState();
        PlayerSimulationState second = CreateInitialState();
        PlayerSimulationSettings settings = CreateSettings();
        PlayerSimulationInput[] inputs =
        {
            CreateInput(new Vector2(1f, 0f)),
            CreateInput(new Vector2(1f, 1f)),
            CreateInput(new Vector2(0f, 1f)),
            CreateInput(Vector2.zero),
            CreateInput(new Vector2(-1f, 0.5f))
        };

        for (int i = 0; i < inputs.Length; i++)
        {
            first = PlayerMovementSimulation.Simulate(first, inputs[i], settings, null, DeltaTime).State;
            second = PlayerMovementSimulation.Simulate(second, inputs[i], settings, null, DeltaTime).State;
        }

        Assert.That(second.Position, Is.EqualTo(first.Position));
        Assert.That(second.RootRotation, Is.EqualTo(first.RootRotation));
        Assert.That(second.ArmatureRotation, Is.EqualTo(first.ArmatureRotation));
        Assert.That(second.VerticalVelocity, Is.EqualTo(first.VerticalVelocity));
        Assert.That(second.CurrentSpeed, Is.EqualTo(first.CurrentSpeed));
        Assert.That(second.PreviousRotateDirection, Is.EqualTo(first.PreviousRotateDirection));
        Assert.That(second.HasRotate, Is.EqualTo(first.HasRotate));
        Assert.That(second.KnockbackVelocity, Is.EqualTo(first.KnockbackVelocity));
        Assert.That(second.DashRemainingTime, Is.EqualTo(first.DashRemainingTime));
        Assert.That(second.DashDirection, Is.EqualTo(first.DashDirection));
    }

    [Test]
    public void RewindAndReplay_SameAuthoritativeStateAndInputs_ProducesBitIdenticalTimeline()
    {
        PlayerSimulationState authoritative = CreateInitialState();
        PlayerSimulationSettings settings = CreateSettings();
        PlayerSimulationInput[] inputs =
        {
            CreateInput(new Vector2(1f, 0f)),
            CreateInput(new Vector2(1f, 1f)),
            CreateInput(new Vector2(0f, 1f)),
            CreateInput(Vector2.zero),
            CreateInput(new Vector2(-1f, 0.5f))
        };

        PlayerSimulationState[] first = PlayerSimulationReplay.Replay(
            authoritative, inputs, settings, null, DeltaTime);
        PlayerSimulationState[] second = PlayerSimulationReplay.Replay(
            authoritative, inputs, settings, null, DeltaTime);

        Assert.That(second.Length, Is.EqualTo(first.Length));
        for (int i = 0; i < first.Length; i++)
            AssertStatesAreBitIdentical(first[i], second[i], i);
    }

    [Test]
    public void RepeatedSimulation_DoesNotMoveRigidbody()
    {
        GameObject gameObject = new GameObject("PlayerMovementSimulationSideEffectTest");
        Rigidbody rigidbody = gameObject.AddComponent<Rigidbody>();
        rigidbody.isKinematic = true;
        rigidbody.position = new Vector3(17f, 23f, -9f);
        rigidbody.rotation = Quaternion.Euler(0f, 37f, 0f);

        Vector3 originalPosition = rigidbody.position;
        Quaternion originalRotation = rigidbody.rotation;
        PlayerSimulationState state = CreateInitialState();
        PlayerSimulationInput input = CreateInput(new Vector2(1f, 1f));
        PlayerSimulationSettings settings = CreateSettings();

        try
        {
            for (int i = 0; i < 10; i++)
                state = PlayerMovementSimulation.Simulate(state, input, settings, null, DeltaTime).State;

            Assert.That(rigidbody.position, Is.EqualTo(originalPosition));
            Assert.That(rigidbody.rotation, Is.EqualTo(originalRotation));
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void TickRingBuffer_WrapsAndKeepsLatestEntries()
    {
        var history = new PlayerTickRingBuffer<PlayerRawSimulationInput>(3);

        history.Store(10, new PlayerRawSimulationInput(Vector2.right, true));
        history.Store(11, new PlayerRawSimulationInput(Vector2.up, true));
        history.Store(12, new PlayerRawSimulationInput(Vector2.left, true));
        history.Store(13, new PlayerRawSimulationInput(Vector2.down, true));

        Assert.That(history.Count, Is.EqualTo(3));
        Assert.That(history.TryGet(10, out _), Is.False);
        Assert.That(history.TryGet(11, out PlayerRawSimulationInput eleven), Is.True);
        Assert.That(eleven.MoveDirection, Is.EqualTo(Vector2.up));
        Assert.That(history.TryGet(13, out PlayerRawSimulationInput thirteen), Is.True);
        Assert.That(thirteen.MoveDirection, Is.EqualTo(Vector2.down));
    }

    [Test]
    public void TickRingBuffer_CanReadPastTickAfterCircularWrap()
    {
        var history = new PlayerTickRingBuffer<PlayerSimulationState>(3);
        history.Store(20, new PlayerSimulationState { Position = Vector3.right });
        history.Store(21, new PlayerSimulationState { Position = Vector3.up });
        history.Store(22, new PlayerSimulationState { Position = Vector3.forward });
        history.Store(23, new PlayerSimulationState { Position = Vector3.left });

        Assert.That(history.TryGet(21, out PlayerSimulationState past), Is.True);
        Assert.That(past.Position, Is.EqualTo(Vector3.up));
    }

    [Test]
    public void TickRingBuffer_RejectsOlderTickWithoutOverwritingHistory()
    {
        var history = new PlayerTickRingBuffer<PlayerRawSimulationInput>(2);
        var latest = new PlayerRawSimulationInput(new Vector2(0.25f, -0.5f), true);

        Assert.That(history.Store(30, latest), Is.True);
        Assert.That(history.Store(29, new PlayerRawSimulationInput(Vector2.one, true)), Is.False);
        Assert.That(history.Count, Is.EqualTo(1));
        Assert.That(history.TryGet(29, out _), Is.False);
        Assert.That(history.TryGet(30, out PlayerRawSimulationInput stored), Is.True);
        Assert.That(stored.MoveDirection, Is.EqualTo(latest.MoveDirection));
    }

    [Test]
    public void TickRingBuffer_DiscardThrough_KeepsOnlyUnacknowledgedInputs()
    {
        var history = new PlayerTickRingBuffer<PlayerRawSimulationInput>(4);
        history.Store(40, new PlayerRawSimulationInput(Vector2.right, true));
        history.Store(41, new PlayerRawSimulationInput(Vector2.up, true));
        history.Store(42, new PlayerRawSimulationInput(Vector2.left, true));

        history.DiscardThrough(41);

        Assert.That(history.Count, Is.EqualTo(1));
        Assert.That(history.TryGet(40, out _), Is.False);
        Assert.That(history.TryGet(41, out _), Is.False);
        Assert.That(history.TryGet(42, out _), Is.True);
        Assert.That(history.TryGetLatestTick(out long latestTick), Is.True);
        Assert.That(latestTick, Is.EqualTo(42));
    }

    private static void AssertStatesAreBitIdentical(
        PlayerSimulationState expected,
        PlayerSimulationState actual,
        int replayIndex)
    {
        Assert.That(actual.Position, Is.EqualTo(expected.Position), $"Position at replay index {replayIndex}");
        Assert.That(actual.RootRotation, Is.EqualTo(expected.RootRotation), $"RootRotation at replay index {replayIndex}");
        Assert.That(actual.ArmatureRotation, Is.EqualTo(expected.ArmatureRotation), $"ArmatureRotation at replay index {replayIndex}");
        Assert.That(actual.VerticalVelocity, Is.EqualTo(expected.VerticalVelocity), $"VerticalVelocity at replay index {replayIndex}");
        Assert.That(actual.CurrentSpeed, Is.EqualTo(expected.CurrentSpeed), $"CurrentSpeed at replay index {replayIndex}");
        Assert.That(actual.PreviousRotateDirection, Is.EqualTo(expected.PreviousRotateDirection), $"PreviousRotateDirection at replay index {replayIndex}");
        Assert.That(actual.HasRotate, Is.EqualTo(expected.HasRotate), $"HasRotate at replay index {replayIndex}");
        Assert.That(actual.IsGrounded, Is.EqualTo(expected.IsGrounded), $"IsGrounded at replay index {replayIndex}");
        Assert.That(actual.GroundNormal, Is.EqualTo(expected.GroundNormal), $"GroundNormal at replay index {replayIndex}");
        Assert.That(actual.GroundSurfaceDistance, Is.EqualTo(expected.GroundSurfaceDistance), $"GroundSurfaceDistance at replay index {replayIndex}");
        Assert.That(actual.GravityEnabled, Is.EqualTo(expected.GravityEnabled), $"GravityEnabled at replay index {replayIndex}");
        Assert.That(actual.KnockbackVelocity, Is.EqualTo(expected.KnockbackVelocity), $"KnockbackVelocity at replay index {replayIndex}");
        Assert.That(actual.DashDirection, Is.EqualTo(expected.DashDirection), $"DashDirection at replay index {replayIndex}");
        Assert.That(actual.DashSpeed, Is.EqualTo(expected.DashSpeed), $"DashSpeed at replay index {replayIndex}");
        Assert.That(actual.DashRemainingTime, Is.EqualTo(expected.DashRemainingTime), $"DashRemainingTime at replay index {replayIndex}");
    }

    private static PlayerSimulationState CreateInitialState()
    {
        return new PlayerSimulationState
        {
            Position = new Vector3(1.25f, 4f, -2.5f),
            RootRotation = Quaternion.identity,
            ArmatureRotation = Quaternion.Euler(0f, 135f, 0f),
            VerticalVelocity = -2.75f,
            CurrentSpeed = 1.5f,
            PreviousRotateDirection = new Vector2(0f, -1f),
            HasRotate = true,
            IsGrounded = false,
            GroundNormal = Vector3.up,
            GravityEnabled = true,
            KnockbackVelocity = new Vector3(2f, 0f, -1f),
            DashRemainingTime = 0.06f,
            DashDirection = Vector3.right,
            DashSpeed = 8f
        };
    }

    private static PlayerSimulationInput CreateInput(Vector2 direction)
    {
        return new PlayerSimulationInput
        {
            MoveDirection = direction,
            HasMoveInput = direction.sqrMagnitude > 0f,
            CanMove = true,
            CanRotate = true,
            MoveSpeedMultiplier = 0.8f,
            Displacement = new Vector3(0.01f, 0f, -0.005f)
        };
    }

    private static PlayerSimulationSettings CreateSettings()
    {
        return new PlayerSimulationSettings
        {
            RotateSpeed = 10f,
            MaxSpeed = 5f,
            MidSpeed = 3f,
            Acceleration = 80f,
            AlignThreshold = 0.98f,
            ViewYaw = -45f,
            GravityY = -9.81f,
            MaxFallSpeed = 30f,
            KnockbackDeceleration = 6f,
            StepOffset = 0.3f,
            MaxWalkableSlopeAngle = 60f,
            CollisionSkin = 0.02f,
            MaxSweepIterations = 3
        };
    }
}
