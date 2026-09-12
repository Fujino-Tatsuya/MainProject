using UnityEngine;

/// <summary>
/// 한 물리 틱의 플레이어 회전, 보행 속도, Motor 이동을 계산한다.
/// 입력과 상태는 값으로만 받고 Rigidbody/Transform/Event/로그에는 손대지 않는다.
/// 충돌 쿼리는 같은 씬에서 재생할 수 있도록 읽기 전용 resolver로 주입한다.
/// </summary>
public static class PlayerMovementSimulation
{
    private const float MovementComparisonEpsilon = 0.00001f;
    private const float UpwardIntentEpsilon = 0.00005f;
    private const float BlockedRatioThreshold = 0.1f;

    public static PlayerSimulationResult Simulate(
        PlayerSimulationState state,
        PlayerSimulationInput input,
        PlayerSimulationSettings settings,
        IPlayerSimulationMotionResolver motionResolver,
        float deltaTime)
    {
        deltaTime = Mathf.Max(0f, deltaTime);

        SimulateRotation(ref state, input, settings, deltaTime);
        Vector3 walkingVelocity = SimulateWalking(ref state, input, settings, deltaTime);

        if (input.HasPoseTarget)
        {
            Vector3 requestedPoseDelta = input.PosePosition - state.Position;
            Quaternion rootDelta = input.PoseRotation * Quaternion.Inverse(state.RootRotation);
            state.ArmatureRotation = rootDelta * state.ArmatureRotation;
            state.Position = input.PosePosition;
            state.RootRotation = input.PoseRotation;
            state.VerticalVelocity = 0f;
            state.WasBlockedThisTick = false;

            return new PlayerSimulationResult(
                state,
                requestedPoseDelta,
                requestedPoseDelta,
                false,
                true);
        }

        Vector3 selfPropelledDelta =
            (walkingVelocity + input.AddedVelocity + state.KnockbackVelocity) * deltaTime +
            input.GroundedDisplacement;

        if (state.DashRemainingTime > 0f)
        {
            selfPropelledDelta += state.DashDirection * state.DashSpeed * deltaTime;
            state.DashRemainingTime = Mathf.Max(0f, state.DashRemainingTime - deltaTime);
        }

        Vector3 externalDelta = input.Displacement;
        bool hasUpwardIntent = selfPropelledDelta.y + externalDelta.y > UpwardIntentEpsilon;

        Vector3 gravityDelta = Vector3.zero;
        Vector3 groundSnapDelta = Vector3.zero;
        if (state.IsGrounded)
        {
            state.VerticalVelocity = 0f;
            if (!hasUpwardIntent)
                groundSnapDelta = Vector3.down * state.GroundSurfaceDistance;
        }
        else if (state.GravityEnabled)
        {
            state.VerticalVelocity += settings.GravityY * deltaTime;
            state.VerticalVelocity = Mathf.Max(state.VerticalVelocity, -settings.MaxFallSpeed);
            gravityDelta = Vector3.up * (state.VerticalVelocity * deltaTime);
        }

        Vector3 intentDelta = ProjectOntoGround(selfPropelledDelta, state) + externalDelta;
        Vector3 desiredDelta = intentDelta + gravityDelta + groundSnapDelta;
        Vector3 intentPlanar = new Vector3(intentDelta.x, 0f, intentDelta.z);

        Vector3 resolvedDelta = motionResolver != null
            ? motionResolver.Resolve(state, desiredDelta, intentPlanar, settings)
            : desiredDelta;

        Vector3 appliedPlanar = new Vector3(resolvedDelta.x, 0f, resolvedDelta.z);
        float intentDistance = intentPlanar.magnitude;
        bool wasBlocked =
            intentDistance > MovementComparisonEpsilon &&
            appliedPlanar.magnitude < intentDistance * BlockedRatioThreshold;

        state.Position += resolvedDelta;
        state.WasBlockedThisTick = wasBlocked;

        // 넉백의 벽 정지 정책도 상태 전이 계산에 포함한다. 이벤트 구독으로 처리하면 replay 때
        // 이벤트를 다시 발행해야 하므로 순수 계산과 commit을 분리할 수 없다.
        if (wasBlocked && state.KnockbackVelocity.sqrMagnitude > 0f)
            state.KnockbackVelocity = Vector3.zero;
        else
            state.KnockbackVelocity = Vector3.MoveTowards(
                state.KnockbackVelocity,
                Vector3.zero,
                settings.KnockbackDeceleration * deltaTime);

        return new PlayerSimulationResult(state, desiredDelta, resolvedDelta, wasBlocked, false);
    }

    private static void SimulateRotation(
        ref PlayerSimulationState state,
        PlayerSimulationInput input,
        PlayerSimulationSettings settings,
        float deltaTime)
    {
        if (!input.CanRotate)
            return;

        if (input.HasMoveInput)
        {
            state.PreviousRotateDirection = input.MoveDirection;
            state.HasRotate = true;
        }

        if (!state.HasRotate)
            return;

        Vector3 direction = new Vector3(
            state.PreviousRotateDirection.x,
            0f,
            state.PreviousRotateDirection.y);
        direction = Quaternion.Euler(0f, settings.ViewYaw, 0f) * direction;
        if (direction.sqrMagnitude <= Mathf.Epsilon)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        Vector3 currentForward = state.ArmatureRotation * Vector3.forward;
        if (Vector3.Dot(direction, currentForward) > 0.999f)
        {
            state.ArmatureRotation = targetRotation;
            state.HasRotate = false;
            return;
        }

        state.ArmatureRotation = Quaternion.Slerp(
            state.ArmatureRotation,
            targetRotation,
            settings.RotateSpeed * deltaTime);
    }

    private static Vector3 SimulateWalking(
        ref PlayerSimulationState state,
        PlayerSimulationInput input,
        PlayerSimulationSettings settings,
        float deltaTime)
    {
        if (!input.CanMove || !input.HasMoveInput)
        {
            state.CurrentSpeed = 0f;
            return Vector3.zero;
        }

        Vector3 localDirection = new Vector3(input.MoveDirection.x, 0f, input.MoveDirection.y);
        Vector3 worldDirection = Quaternion.Euler(0f, settings.ViewYaw, 0f) * localDirection;
        worldDirection.Normalize();

        Vector3 forward = state.ArmatureRotation * Vector3.forward;
        float alignment = Vector3.Dot(worldDirection, forward);
        if (alignment >= settings.AlignThreshold)
        {
            state.CurrentSpeed = settings.MaxSpeed;
        }
        else
        {
            if (state.CurrentSpeed > settings.MidSpeed)
                state.CurrentSpeed = settings.MidSpeed;

            state.CurrentSpeed = Mathf.MoveTowards(
                state.CurrentSpeed,
                settings.MaxSpeed,
                settings.Acceleration * deltaTime);
        }

        float resolvedSpeed = input.HasFixedMoveSpeed
            ? input.FixedMoveSpeed
            : state.CurrentSpeed * input.MoveSpeedMultiplier;
        return worldDirection * resolvedSpeed;
    }

    private static Vector3 ProjectOntoGround(Vector3 move, PlayerSimulationState state)
    {
        if (!state.IsGrounded)
            return move;

        float distance = move.magnitude;
        if (distance <= Mathf.Epsilon || state.GroundNormal.y >= 0.999f)
            return move;

        Vector3 projected = Vector3.ProjectOnPlane(move, state.GroundNormal);
        return projected.sqrMagnitude <= 1e-6f
            ? move
            : projected.normalized * distance;
    }
}

public interface IPlayerSimulationMotionResolver
{
    Vector3 Resolve(
        PlayerSimulationState state,
        Vector3 desiredDelta,
        Vector3 horizontalStepDelta,
        PlayerSimulationSettings settings);
}

public struct PlayerSimulationState
{
    public Vector3 Position;
    public Quaternion RootRotation;
    public Quaternion ArmatureRotation;
    public float VerticalVelocity;
    public float CurrentSpeed;
    public Vector2 PreviousRotateDirection;
    public bool HasRotate;

    public bool IsGrounded;
    public Vector3 GroundNormal;
    public float GroundSurfaceDistance;
    public bool IsSoul;

    public bool GravityEnabled;
    public bool WasBlockedThisTick;

    public Vector3 KnockbackVelocity;
    public float DashRemainingTime;
    public Vector3 DashDirection;
    public float DashSpeed;
}

public struct PlayerSimulationInput
{
    public Vector2 MoveDirection;
    public bool HasMoveInput;
    public bool CanMove;
    public bool CanRotate;
    public bool HasFixedMoveSpeed;
    public float FixedMoveSpeed;
    public float MoveSpeedMultiplier;

    public Vector3 AddedVelocity;
    public Vector3 GroundedDisplacement;
    public Vector3 Displacement;

    public bool HasPoseTarget;
    public Vector3 PosePosition;
    public Quaternion PoseRotation;
}

public struct PlayerSimulationSettings
{
    public float RotateSpeed;
    public float MaxSpeed;
    public float MidSpeed;
    public float Acceleration;
    public float AlignThreshold;
    public float ViewYaw;

    public float GravityY;
    public float MaxFallSpeed;
    public float KnockbackDeceleration;
    public float StepOffset;
    public float MaxWalkableSlopeAngle;
    public LayerMask ObstacleMask;
    public float CollisionSkin;
    public int MaxSweepIterations;
}

public readonly struct PlayerSimulationResult
{
    public PlayerSimulationResult(
        PlayerSimulationState state,
        Vector3 requestedDelta,
        Vector3 resolvedDelta,
        bool wasBlocked,
        bool appliesPoseRotation)
    {
        State = state;
        RequestedDelta = requestedDelta;
        ResolvedDelta = resolvedDelta;
        WasBlocked = wasBlocked;
        AppliesPoseRotation = appliesPoseRotation;
    }

    public PlayerSimulationState State { get; }
    public Vector3 RequestedDelta { get; }
    public Vector3 ResolvedDelta { get; }
    public bool WasBlocked { get; }
    public bool AppliesPoseRotation { get; }
}
