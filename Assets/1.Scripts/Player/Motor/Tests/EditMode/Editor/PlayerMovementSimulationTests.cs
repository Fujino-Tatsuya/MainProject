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
