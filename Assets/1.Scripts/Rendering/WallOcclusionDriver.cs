using System.Collections.Generic;
using UnityEngine;
using VeyTrace.Rendering.Occlusion;

[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class WallOcclusionDriver : MonoBehaviour
{
    [SerializeField] private WallOcclusionSettings settings;

    private readonly Dictionary<ElevationStack, ElevationStackState> stackStates = new();
    private readonly HashSet<ElevationLevel> hitLevels = new();
    private readonly HashSet<OcclusionSection> hitSections = new();
    private readonly List<ElevationStack> staleStacks = new();
    private readonly List<Vector3> debugHitPoints = new();

    private WallOcclusionRendererController rendererController;
    private RaycastHit[] castHits;
    private Ray[] sightlineRays;
    private float[] sightlineDistances;
    private Transform activeTargetRoot;
    private float previousFootY;
    private bool hasPreviousFootY;
    private bool warnedCastBufferFull;
    private Vector3 debugCastOrigin;
    private Vector3 debugCastCenter;
    private float debugCastRadius;

    public WallOcclusionSettings Settings => settings;

    public void SetSettings(WallOcclusionSettings newSettings)
    {
        if (settings == newSettings)
            return;

        rendererController?.RestoreAllImmediate();
        settings = newSettings;
        CreateRuntimeBuffers();
    }

    private void OnEnable()
    {
        CreateRuntimeBuffers();
        WallOcclusionGlobals.Disable();
    }

    private void OnDisable()
    {
        rendererController?.RestoreAllImmediate();
        WallOcclusionGlobals.Disable();
        stackStates.Clear();
        activeTargetRoot = null;
        hasPreviousFootY = false;
    }

    private void LateUpdate()
    {
        EnsureRuntimeBuffers();
        rendererController.BeginFrame();
        debugHitPoints.Clear();

        CameraTargetSwitcher switcher = CameraTargetSwitcher.Active;
        Camera gameplayCamera = switcher != null ? switcher.GameplayCamera : null;
        Transform followTarget = switcher != null ? switcher.CurrentFollowTarget : null;

        if (settings == null || gameplayCamera == null || !gameplayCamera.isActiveAndEnabled ||
            followTarget == null || !TryResolveTarget(followTarget, out TargetSample target))
        {
            DeactivateFrame();
            return;
        }

        if (activeTargetRoot != target.Root)
        {
            activeTargetRoot = target.Root;
            stackStates.Clear();
            hasPreviousFootY = false;
        }

        OcclusionVerticalMotion motion = ResolveVerticalMotion(target);
        UpdateElevationStates(target, motion);
        float projectedRadius = CalculateProjectedRadiusPixels(gameplayCamera, target);
        ApplyScreenMask(gameplayCamera, target, projectedRadius);
        CollectOcclusionHits(gameplayCamera, target, projectedRadius);

        foreach (ElevationLevel level in hitLevels)
            rendererController.AddLevel(level);
        foreach (OcclusionSection section in hitSections)
            rendererController.AddSection(section);

        rendererController.EndFrame(Time.deltaTime);
        previousFootY = target.FootWorldY;
        hasPreviousFootY = true;
    }

    private void CreateRuntimeBuffers()
    {
        rendererController = settings != null
            ? new WallOcclusionRendererController(settings)
            : null;
        castHits = new RaycastHit[Mathf.Max(8, settings != null ? settings.maxCastHits : 8)];
        sightlineRays = new Ray[WallOcclusionSightlineFilter.RequiredSampleCapacity];
        sightlineDistances = new float[WallOcclusionSightlineFilter.RequiredSampleCapacity];
        warnedCastBufferFull = false;
    }

    private void EnsureRuntimeBuffers()
    {
        if (rendererController == null && settings != null)
            rendererController = new WallOcclusionRendererController(settings);

        int required = Mathf.Max(8, settings != null ? settings.maxCastHits : 8);
        if (castHits == null || castHits.Length != required)
        {
            castHits = new RaycastHit[required];
            warnedCastBufferFull = false;
        }

        if (sightlineRays == null ||
            sightlineRays.Length != WallOcclusionSightlineFilter.RequiredSampleCapacity)
        {
            sightlineRays = new Ray[WallOcclusionSightlineFilter.RequiredSampleCapacity];
            sightlineDistances = new float[WallOcclusionSightlineFilter.RequiredSampleCapacity];
        }
    }

    private void DeactivateFrame()
    {
        WallOcclusionGlobals.Disable();
        rendererController?.EndFrame(Time.deltaTime);
        activeTargetRoot = null;
        stackStates.Clear();
        hasPreviousFootY = false;
    }

    private void UpdateElevationStates(TargetSample target, OcclusionVerticalMotion motion)
    {
        staleStacks.Clear();
        foreach (ElevationStack existing in stackStates.Keys)
            staleStacks.Add(existing);

        foreach (KeyValuePair<ElevationStack, List<ElevationLevel>> entry in
                 WallOcclusionRegistry.EnumerateStacks())
        {
            ElevationStack stack = entry.Key;
            if (stack == null || !stack.isActiveAndEnabled)
                continue;

            if (!stackStates.TryGetValue(stack, out ElevationStackState state))
            {
                state = new ElevationStackState(stack);
                stackStates.Add(stack, state);
            }

            state.Update(
                target.Center,
                target.FootWorldY,
                motion,
                target.IsGrounded,
                target.HasGroundSensor,
                ResolveGroundedLevel(target, stack),
                settings.risingProgress,
                settings.fallingProgress);
            staleStacks.Remove(stack);
        }

        for (int i = 0; i < staleStacks.Count; i++)
            stackStates.Remove(staleStacks[i]);
    }

    private static ElevationLevel ResolveGroundedLevel(TargetSample target, ElevationStack stack)
    {
        if (!target.IsGrounded || target.GroundCollider == null ||
            !WallOcclusionRegistry.TryGetLevel(target.GroundCollider, out ElevationLevel level))
        {
            return null;
        }

        return level.Stack == stack ? level : null;
    }

    private void CollectOcclusionHits(
        Camera camera,
        TargetSample target,
        float projectedRadius)
    {
        hitLevels.Clear();
        hitSections.Clear();

        Vector3 origin = camera.transform.position;
        Vector3 toTarget = target.Center - origin;
        float distance = toTarget.magnitude;
        if (distance <= 0.001f)
            return;

        Vector3 direction = toTarget / distance;
        float axisFacing = Mathf.Abs(Vector3.Dot(target.Axis, direction));
        float projectedHalfCylinder = target.HalfCylinderLength *
            Mathf.Sqrt(Mathf.Max(0f, 1f - axisFacing * axisFacing));
        float castRadius = target.Radius + projectedHalfCylinder + settings.castPadding;

        debugCastOrigin = origin;
        debugCastCenter = target.Center;
        debugCastRadius = castRadius;

        int sightlineCount = WallOcclusionSightlineFilter.BuildSamples(
            camera,
            target.EndpointA,
            target.EndpointB,
            projectedRadius * settings.screenCapsuleRadiusScale,
            sightlineRays,
            sightlineDistances);
        if (sightlineCount == 0)
            return;

        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            castRadius,
            direction,
            castHits,
            distance,
            settings.castMask,
            QueryTriggerInteraction.Ignore);

        if (hitCount >= castHits.Length && !warnedCastBufferFull)
        {
            warnedCastBufferFull = true;
            Debug.LogWarning(
                $"[WallOcclusion] SphereCast buffer is full ({castHits.Length}). " +
                "Increase maxCastHits in WallOcclusionSettings.",
                this);
        }

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = castHits[i];
            Collider collider = hit.collider;
            if (collider == null || IsTargetCollider(collider, target.Root))
                continue;

            if (!WallOcclusionSightlineFilter.BlocksAnySample(
                    collider,
                    sightlineRays,
                    sightlineDistances,
                    sightlineCount))
            {
                continue;
            }

            if (settings.drawRuntimeGizmos)
                debugHitPoints.Add(hit.point);

            if (WallOcclusionRegistry.TryGetLevel(collider, out ElevationLevel level) &&
                IsLevelAbove(level))
            {
                hitLevels.Add(level);
            }

            if (WallOcclusionRegistry.TryGetSection(collider, out OcclusionSection section))
                hitSections.Add(section);
        }
    }

    private bool IsLevelAbove(ElevationLevel level)
    {
        if (level == null || level.Stack == null)
            return false;
        return stackStates.TryGetValue(level.Stack, out ElevationStackState state) &&
            state.IsAboveActiveLevel(level);
    }

    private void ApplyScreenMask(
        Camera camera,
        TargetSample target,
        float projectedRadius)
    {
        Vector3 screenA = camera.WorldToScreenPoint(target.EndpointA);
        Vector3 screenB = camera.WorldToScreenPoint(target.EndpointB);
        if (screenA.z <= 0f || screenB.z <= 0f)
        {
            WallOcclusionGlobals.Disable();
            return;
        }

        float targetDepth = Vector3.Dot(
            target.Center - camera.transform.position,
            camera.transform.forward);
        WallOcclusionGlobals.ApplyScreenCapsule(
            new Vector2(screenA.x, screenA.y),
            new Vector2(screenB.x, screenB.y),
            projectedRadius * settings.screenCapsuleRadiusScale + settings.holePaddingPixels,
            settings.CalculateFeatherPixels(projectedRadius),
            camera,
            targetDepth,
            settings.behindFalloff);
    }

    private static float CalculateProjectedRadiusPixels(Camera camera, TargetSample target)
    {
        Vector3[] centers = { target.EndpointA, target.Center, target.EndpointB };
        float maxRadius = 1f;
        for (int i = 0; i < centers.Length; i++)
        {
            Vector3 centerScreen = camera.WorldToScreenPoint(centers[i]);
            Vector3 rightScreen = camera.WorldToScreenPoint(
                centers[i] + camera.transform.right * target.Radius);
            Vector3 upScreen = camera.WorldToScreenPoint(
                centers[i] + camera.transform.up * target.Radius);
            maxRadius = Mathf.Max(
                maxRadius,
                Vector2.Distance(centerScreen, rightScreen),
                Vector2.Distance(centerScreen, upScreen));
        }

        return maxRadius;
    }

    private OcclusionVerticalMotion ResolveVerticalMotion(TargetSample target)
    {
        if (target.GroundSensor != null)
        {
            return target.GroundSensor.VerticalState switch
            {
                PlayerGroundingSensor.VerticalMotionState.Rising => OcclusionVerticalMotion.Rising,
                PlayerGroundingSensor.VerticalMotionState.Falling => OcclusionVerticalMotion.Falling,
                _ => OcclusionVerticalMotion.Stable
            };
        }

        if (!hasPreviousFootY)
            return OcclusionVerticalMotion.Stable;

        float delta = target.FootWorldY - previousFootY;
        if (delta > 0.001f)
            return OcclusionVerticalMotion.Rising;
        if (delta < -0.001f)
            return OcclusionVerticalMotion.Falling;
        return OcclusionVerticalMotion.Stable;
    }

    private static bool TryResolveTarget(Transform followTarget, out TargetSample sample)
    {
        sample = default;
        PlayerGroundingSensor grounding = followTarget.GetComponentInParent<PlayerGroundingSensor>();
        CapsuleCollider capsule = grounding != null
            ? grounding.GetComponent<CapsuleCollider>()
            : FindBaseCapsule(followTarget);
        if (capsule == null || !capsule.enabled || capsule.isTrigger)
            return false;

        GetWorldCapsule(
            capsule,
            out Vector3 center,
            out Vector3 axis,
            out float radius,
            out float halfCylinderLength,
            out Vector3 endpointA,
            out Vector3 endpointB);

        bool sensorUsable = grounding != null &&
            (!grounding.IsSpawned || grounding.IsOwner || grounding.IsServer);
        sample = new TargetSample
        {
            Root = capsule.transform,
            Center = center,
            Axis = axis,
            Radius = radius,
            HalfCylinderLength = halfCylinderLength,
            EndpointA = endpointA,
            EndpointB = endpointB,
            FootWorldY = Mathf.Min(endpointA.y, endpointB.y) - radius,
            GroundSensor = sensorUsable ? grounding : null,
            HasGroundSensor = sensorUsable,
            IsGrounded = sensorUsable && grounding.IsGrounded,
            GroundCollider = sensorUsable ? grounding.GroundCollider : null
        };
        return true;
    }

    private static CapsuleCollider FindBaseCapsule(Transform followTarget)
    {
        CapsuleCollider[] candidates = followTarget.GetComponentsInParent<CapsuleCollider>(true);
        for (int i = 0; i < candidates.Length; i++)
        {
            if (candidates[i] != null && candidates[i].enabled && !candidates[i].isTrigger)
                return candidates[i];
        }

        return null;
    }

    private static void GetWorldCapsule(
        CapsuleCollider capsule,
        out Vector3 center,
        out Vector3 axis,
        out float radius,
        out float halfCylinderLength,
        out Vector3 endpointA,
        out Vector3 endpointB)
    {
        Vector3 localAxis = capsule.direction switch
        {
            0 => Vector3.right,
            2 => Vector3.forward,
            _ => Vector3.up
        };
        Vector3 localPerpendicularA = capsule.direction == 0 ? Vector3.up : Vector3.right;
        Vector3 localPerpendicularB = capsule.direction == 2 ? Vector3.up : Vector3.forward;

        Vector3 worldAxisVector = capsule.transform.TransformVector(localAxis);
        axis = worldAxisVector.sqrMagnitude > 0f ? worldAxisVector.normalized : Vector3.up;
        float axisScale = worldAxisVector.magnitude;
        float radiusScale = Mathf.Max(
            capsule.transform.TransformVector(localPerpendicularA).magnitude,
            capsule.transform.TransformVector(localPerpendicularB).magnitude);
        radius = capsule.radius * radiusScale;
        float height = Mathf.Max(capsule.height * axisScale, radius * 2f);
        halfCylinderLength = Mathf.Max(0f, height * 0.5f - radius);
        center = capsule.transform.TransformPoint(capsule.center);
        endpointA = center + axis * halfCylinderLength;
        endpointB = center - axis * halfCylinderLength;
    }

    private static bool IsTargetCollider(Collider collider, Transform targetRoot)
    {
        Transform candidate = collider.transform;
        return candidate == targetRoot || candidate.IsChildOf(targetRoot) || targetRoot.IsChildOf(candidate);
    }

    private void OnDrawGizmos()
    {
        if (settings == null || !settings.drawRuntimeGizmos)
            return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(debugCastOrigin, debugCastCenter);
        Gizmos.DrawWireSphere(debugCastOrigin, debugCastRadius);
        Gizmos.DrawWireSphere(debugCastCenter, debugCastRadius);
        Gizmos.color = Color.yellow;
        for (int i = 0; i < debugHitPoints.Count; i++)
            Gizmos.DrawSphere(debugHitPoints[i], 0.08f);
    }

    private struct TargetSample
    {
        public Transform Root;
        public Vector3 Center;
        public Vector3 Axis;
        public float Radius;
        public float HalfCylinderLength;
        public Vector3 EndpointA;
        public Vector3 EndpointB;
        public float FootWorldY;
        public PlayerGroundingSensor GroundSensor;
        public Collider GroundCollider;
        public bool HasGroundSensor;
        public bool IsGrounded;
    }
}
