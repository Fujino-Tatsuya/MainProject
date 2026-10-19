using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Secondary motion for the original seven four-bone hair chains. Rest curvature is
/// transported through each moving segment; only the root is attached to the head.
/// The mesh, bind transforms, bone names and skin weights are never changed.
/// </summary>
[DefaultExecutionOrder(21000)]
[DisallowMultipleComponent]
public sealed class AssassinHairMotion : MonoBehaviour
{
    [Serializable]
    public sealed class Chain
    {
        public Transform[] bones = new Transform[4];
        public Quaternion[] bindRotations = new Quaternion[4];
        public Vector3[] bindPositions = new Vector3[4];
        public Vector3 tipLocal;
        [HideInInspector] public float[] bindAllowanceReference;
        [NonSerialized] public Vector3[] points, velocity, currentTarget, previousTarget;
        [NonSerialized] public Quaternion[] targetRotations, previousRotations, lastApplied;
        [NonSerialized] public Vector3[] axisLocal, neutralHeadOffsets;
        [NonSerialized] public float[] lengths;
        [NonSerialized] public float[,] bindAllowance;
        [NonSerialized] internal Vector3[] restVectors, predicted, before, carried, transport;
        [NonSerialized] internal Vector3[] contactNormals, contactVelocities;
        [NonSerialized] internal int[] contactCounts;
        [NonSerialized] internal bool[] touching;
        [NonSerialized] internal Quaternion[] frames;
    }

    [Serializable]
    public sealed class BodyCapsule
    {
        public string name;
        public Transform first, last;
        public float radius = .06f;
        public Vector3 firstOffset, lastOffset;
        [NonSerialized] public Vector3 previousA, previousB, currentA, currentB;
        [NonSerialized] internal Vector3 stepA, stepB, velocityA, velocityB;
    }

    public Transform head;
    public Transform hips;
    public Chain[] chains = new Chain[7];
    public BodyCapsule[] capsules;
    [Range(0, 1)] public float weight = 1;
    public bool motionEnabled = true;
    public bool paused;
    public bool externalDrive;

    [Header("Natural motion")]
    [Tooltip("Small translation carry reduces excessive extension during fast root motion. It decreases toward the tips.")]
    [Range(0, 1)] public float rootCarry = .20f;
    [Tooltip("Exponential velocity damping, per second.")]
    public float damping = 10f;
    [Tooltip("Local rest-curvature spring strength. There is no world-position target spring.")]
    public float stiffness = 60f;
    [Tooltip("World gravity multiplier. Bind-pose support is calibrated locally to retain the original resting silhouette.")]
    public float gravitySupport = .65f;
    [Tooltip("World acceleration. Kept local to this component so no Unity Physics module is required.")]
    public Vector3 gravityAcceleration = new Vector3(0, -9.81f, 0);
    public float hairRadius = .018f;
    [Tooltip("Velocity limit relative to root translation; rapid root motion itself is not clipped.")]
    public float maximumDynamicSpeed = 2f;
    [Tooltip("Large discontinuity threshold. Ordinary dashes keep their simulation history.")]
    public float teleportDistance = 4f;
    [Tooltip("Rotation is only a reset criterion when accompanied by a large position discontinuity.")]
    public float teleportAngle = 175f;
    [Tooltip("Soft width and large-angle regularization between neighbours; it weakens near the tips and contacts.")]
    [Range(0, 1)] public float neighbourCoupling = .06f;
    public float neighbourOffsetLimit = .09f;
    [Tooltip("Tangential contact damping, per second. Normal inward velocity is removed separately.")]
    public float contactFriction = 4f;
    [Range(2, 12)] public int constraintIterations = 6;

    public bool captured;
    public int resetCount, teleportResetCount, collisionCorrections;
    public float maximumObservedSpeed, maximumLengthError, maximumBoneAngle;
    public float maximumContactResidual;
    public const float FixedStep = 1f / 120f;

    const int ChainCount = 7;
    const int SegmentCount = 4;
    const int PointCount = 5;
    const int CurrentSolverVersion = 13;
    const float Epsilon = 1e-7f;
    // These are joint limits relative to the deforming parent, not head-relative limits.
    static readonly float[] LocalBendAngles = { 12f, 28f, 35f, 40f };
    static readonly float[] NeighbourFrameAngles = { 45f, 75f, 100f, 120f };
    static readonly float[] ScaffoldFlexAngles = { 8f, 12f, 16f, 20f };
    static readonly float[] GravityRestBlend = { .05f, .4f, .75f, 1f };
    static readonly float[] GuideExcursion = { 6f, 10f, 16f, 20f };
    static readonly float[] MaterialFrameLimit = { 12f, 28f, 45f, 65f };
    static readonly float[] SpringScale = { 8f, 2.1f, 1.2f, .85f };

    [SerializeField, HideInInspector] int solverVersion;
    [SerializeField, HideInInspector] Vector3 bindGravityLocal = Vector3.down;
    bool initialized;
    float accumulator;
    Vector3 previousHeadPosition, previousHipsPosition;
    Quaternion previousHeadRotation, previousHipsRotation;
    Quaternion stepHeadRotation;
    Vector3[][] targets;
    Quaternion[][] materialFrames;
    Chain scaffold;
    readonly Quaternion[] scaffoldFrames = new Quaternion[SegmentCount];
    readonly Quaternion[] gravityRestFrames = new Quaternion[SegmentCount];

    /// <summary>Apply the v13 settings without recapturing or changing the rig.</summary>
    public void UseNaturalMotionPreset()
    {
        rootCarry = .20f;
        damping = 10f;
        stiffness = 60f;
        gravitySupport = .65f;
        gravityAcceleration = new Vector3(0, -9.81f, 0);
        hairRadius = .018f;
        maximumDynamicSpeed = 2f;
        teleportDistance = 4f;
        teleportAngle = 175f;
        neighbourCoupling = .06f;
        neighbourOffsetLimit = .09f;
        contactFriction = 4f;
        constraintIterations = 6;
        solverVersion = CurrentSolverVersion;
    }

    Transform Find(string boneName)
    {
        foreach (var bone in GetComponentsInChildren<Transform>(true))
            if (bone.name == boneName) return bone;
        return null;
    }

    public void CaptureBindPose(Vector3[] originalTipWorld = null)
    {
        head = Find("Head");
        hips = Find("Hips");
        if (!head || !hips) throw new InvalidOperationException("Head and Hips bones are required.");
        chains = new Chain[ChainCount];
        for (int c = 0; c < ChainCount; c++)
        {
            var chain = new Chain();
            chains[c] = chain;
            for (int b = 0; b < SegmentCount; b++)
            {
                var bone = Find("Hair_Source" + c.ToString("D2") + "_" + (b + 1).ToString("D2"));
                if (!bone) throw new InvalidOperationException("An original hair bone is missing.");
                chain.bones[b] = bone;
                chain.bindPositions[b] = bone.localPosition;
                chain.bindRotations[b] = bone.localRotation;
            }
            chain.tipLocal = originalTipWorld != null && originalTipWorld.Length == ChainCount
                ? chain.bones[3].InverseTransformPoint(originalTipWorld[c])
                : chain.bones[3].InverseTransformVector((chain.bones[3].position - chain.bones[2].position).normalized * .15f);
        }
        var body = new List<BodyCapsule>();
        AddCapsule(body, "Chest", "Spine", "UpperChest", .105f);
        AddCapsule(body, "Waist", "Hips", "Spine", .095f);
        foreach (var side in new[] { "Left", "Right" })
        {
            AddCapsule(body, side + " upper arm", side + "UpperArm", side + "LowerArm", .060f);
            AddCapsule(body, side + " forearm", side + "LowerArm", side + "Hand", .042f);
            AddCapsule(body, side + " thigh", side + "UpperLeg", side + "LowerLeg", .065f);
        }
        capsules = body.ToArray();
        bindGravityLocal = head.InverseTransformDirection(GravityDirection());
        captured = true;
        initialized = false;
        Initialize();
    }

    void AddCapsule(List<BodyCapsule> list, string label, string first, string last, float radius)
    {
        var a = Find(first);
        var b = Find(last);
        if (a && b) list.Add(new BodyCapsule { name = label, first = a, last = b, radius = radius });
    }

    public void Initialize()
    {
        if (initialized) return;
        if (solverVersion < CurrentSolverVersion) UseNaturalMotionPreset();
        if (!captured) { CaptureBindPose(); return; }
        if (!head) head = Find("Head");
        if (!hips) hips = Find("Hips");
        if (!head || !hips || chains == null || chains.Length != ChainCount)
            throw new InvalidOperationException("The original seven-chain Assassin rig is required.");
        if (capsules == null) capsules = new BodyCapsule[0];
        targets = new Vector3[ChainCount][];
        materialFrames = new Quaternion[ChainCount][];
        for (int ci = 0; ci < ChainCount; ci++)
        {
            var c = chains[ci];
            targets[ci] = new Vector3[PointCount];
            materialFrames[ci] = new Quaternion[SegmentCount];
            c.points = new Vector3[PointCount];
            c.velocity = new Vector3[PointCount];
            c.currentTarget = new Vector3[PointCount];
            c.previousTarget = new Vector3[PointCount];
            c.neutralHeadOffsets = new Vector3[PointCount];
            c.targetRotations = new Quaternion[SegmentCount];
            c.previousRotations = new Quaternion[SegmentCount];
            c.lastApplied = new Quaternion[SegmentCount];
            c.axisLocal = new Vector3[SegmentCount];
            c.lengths = new float[SegmentCount];
            c.restVectors = new Vector3[SegmentCount];
            c.predicted = new Vector3[PointCount];
            c.before = new Vector3[PointCount];
            c.carried = new Vector3[PointCount];
            c.transport = new Vector3[PointCount];
            c.contactNormals = new Vector3[PointCount];
            c.contactVelocities = new Vector3[PointCount];
            c.contactCounts = new int[PointCount];
            c.touching = new bool[PointCount];
            c.frames = new Quaternion[SegmentCount];
            c.bindAllowance = new float[SegmentCount, capsules.Length];
        }
        CaptureTargets();
        foreach (var c in chains)
        {
            for (int p = 0; p < PointCount; p++)
                c.neutralHeadOffsets[p] = Quaternion.Inverse(head.rotation) * (c.currentTarget[p] - head.position);
            for (int b = 0; b < SegmentCount; b++)
            {
                c.restVectors[b] = c.neutralHeadOffsets[b + 1] - c.neutralHeadOffsets[b];
                c.lengths[b] = c.restVectors[b].magnitude;
                c.axisLocal[b] = c.bones[b].InverseTransformDirection(c.currentTarget[b + 1] - c.currentTarget[b]).normalized;
                for (int k = 0; k < capsules.Length; k++)
                {
                    float distance = SegmentDistance(c.currentTarget[b], c.currentTarget[b + 1], capsules[k].currentA, capsules[k].currentB);
                    c.bindAllowance[b, k] = c.bindAllowanceReference != null && c.bindAllowanceReference.Length == SegmentCount * capsules.Length
                        ? c.bindAllowanceReference[b * capsules.Length + k]
                        : Mathf.Max(0, capsules[k].radius + hairRadius - distance + .00002f);
                }
            }
            if (c.bindAllowanceReference == null || c.bindAllowanceReference.Length != SegmentCount * capsules.Length)
            {
                c.bindAllowanceReference = new float[SegmentCount * capsules.Length];
                for (int b = 0; b < SegmentCount; b++)
                    for (int k = 0; k < capsules.Length; k++)
                        c.bindAllowanceReference[b * capsules.Length + k] = c.bindAllowance[b, k];
            }
        }
        CreateScaffold();
        initialized = true;
        ResetSimulation(false);
    }

    public void RestoreBindRotations()
    {
        if (!captured || chains == null) return;
        foreach (var c in chains)
            if (c != null)
                for (int b = 0; b < SegmentCount; b++)
                    if (c.bones[b]) c.bones[b].localRotation = c.bindRotations[b];
    }

    void CaptureTargets()
    {
        RestoreBindRotations();
        foreach (var c in chains)
        {
            for (int b = 0; b < SegmentCount; b++)
            {
                c.currentTarget[b] = c.bones[b].position;
                c.targetRotations[b] = c.bones[b].rotation;
            }
            c.currentTarget[SegmentCount] = c.bones[3].TransformPoint(c.tipLocal);
        }
        foreach (var cap in capsules)
        {
            cap.currentA = cap.first.TransformPoint(cap.firstOffset);
            cap.currentB = cap.last.TransformPoint(cap.lastOffset);
        }
    }

    public void ResetSimulation(bool teleport = false)
    {
        if (!initialized) { Initialize(); return; }
        CaptureTargets();
        accumulator = 0;
        foreach (var c in chains)
        {
            for (int p = 0; p < PointCount; p++)
            {
                c.points[p] = c.currentTarget[p];
                c.previousTarget[p] = c.currentTarget[p];
                c.velocity[p] = Vector3.zero;
                c.transport[p] = Vector3.zero;
                c.touching[p] = false;
            }
            for (int b = 0; b < SegmentCount; b++)
            {
                c.previousRotations[b] = c.targetRotations[b];
                c.lastApplied[b] = c.bindRotations[b];
                c.frames[b] = head.rotation;
            }
        }
        foreach (var cap in capsules)
        {
            cap.previousA = cap.stepA = cap.currentA;
            cap.previousB = cap.stepB = cap.currentB;
            cap.velocityA = cap.velocityB = Vector3.zero;
        }
        for (int p = 0; p < PointCount; p++)
        {
            scaffold.points[p] = chains[3].currentTarget[p];
            scaffold.velocity[p] = scaffold.transport[p] = Vector3.zero;
            scaffold.touching[p] = false;
        }
        for (int b = 0; b < SegmentCount; b++) scaffold.frames[b] = scaffoldFrames[b] = gravityRestFrames[b] = head.rotation;
        SaveInputPose();
        resetCount++;
        if (teleport) teleportResetCount++;
    }

    public void Advance(float deltaTime)
    {
        Initialize();
        if (!motionEnabled || weight <= 0) { RestoreBindRotations(); ResetSimulation(); return; }
        if (paused)
        {
            foreach (var c in chains)
                for (int b = 0; b < SegmentCount; b++) c.bones[b].localRotation = c.lastApplied[b];
            return;
        }
        CaptureTargets();
        float headTravel = Vector3.Distance(previousHeadPosition, head.position);
        bool largeDiscontinuity = headTravel > Mathf.Max(.5f, teleportDistance);
        bool discontinuousTurn = Quaternion.Angle(previousHeadRotation, head.rotation) > teleportAngle
            && headTravel > Mathf.Max(.5f, teleportDistance * .5f);
        if (!IsFinite(deltaTime) || deltaTime < 0 || deltaTime > .5f || largeDiscontinuity || discontinuousTurn)
        {
            ResetSimulation(true);
            return;
        }
        float carry = accumulator;
        accumulator += Mathf.Max(0, deltaTime);
        int step = 0;
        while (accumulator + Epsilon >= FixedStep)
        {
            float intoFrame = FixedStep - carry + step * FixedStep;
            float alpha = deltaTime > 0 ? Mathf.Clamp01(intoFrame / deltaTime) : 1;
            SimulateStep(alpha);
            accumulator -= FixedStep;
            step++;
        }
        accumulator = Mathf.Max(0, accumulator);
        ApplyPose();
        SaveInputPose();
    }

    void SaveInputPose()
    {
        foreach (var c in chains)
        {
            Array.Copy(c.currentTarget, c.previousTarget, PointCount);
            Array.Copy(c.targetRotations, c.previousRotations, SegmentCount);
        }
        foreach (var cap in capsules) { cap.previousA = cap.currentA; cap.previousB = cap.currentB; }
        previousHeadPosition = head.position;
        previousHeadRotation = head.rotation;
        previousHipsPosition = hips.position;
        previousHipsRotation = hips.rotation;
    }

    void SimulateStep(float alpha)
    {
        var anchor = Vector3.Lerp(previousHeadPosition, head.position, alpha);
        stepHeadRotation = Quaternion.Slerp(previousHeadRotation, head.rotation, alpha);
        UpdateCollisionPose(alpha);
        var gravity = gravityAcceleration * Mathf.Max(0, gravitySupport);
        float gravityMagnitude = gravity.magnitude;
        float decay = Mathf.Exp(-Mathf.Max(0, damping) * FixedStep);
        var gravityAligned = RotationBetween(stepHeadRotation * bindGravityLocal, GravityDirection()) * stepHeadRotation;
        for (int b = 0; b < SegmentCount; b++)
            gravityRestFrames[b] = Quaternion.Slerp(stepHeadRotation, gravityAligned, GravityRestBlend[b]);

        for (int ci = 0; ci < ChainCount; ci++)
            for (int p = 0; p < PointCount; p++) targets[ci][p] = anchor + stepHeadRotation * chains[ci].neutralHeadOffsets[p];

        // The broad shared-weight surface needs a common moving deformation frame.
        // This virtual guide has its own inertia/gravity and torso contacts; limb
        // contacts on a side chain cannot pull the whole curtain up through it.
        scaffold.bindAllowance = chains[3].bindAllowance;
        Predict(scaffold, targets[3][0], gravity, gravityMagnitude, decay);
        int iterations = Mathf.Clamp(constraintIterations, 2, 12);
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            scaffold.points[0] = targets[3][0];
            for (int b = 0; b < SegmentCount; b++)
            {
                ProjectSegment(scaffold, b, true);
                ProjectContacts(scaffold, b, true);
            }
            ProjectLengths(scaffold);
        }
        for (int b = 0; b < SegmentCount; b++)
        {
            ProjectSegment(scaffold, b, true);
            ProjectContacts(scaffold, b, false);
            scaffoldFrames[b] = scaffold.frames[b];
        }
        GatherFinalContacts(scaffold);
        UpdateVelocities(scaffold);

        for (int ci = 0; ci < ChainCount; ci++) Predict(chains[ci], targets[ci][0], gravity, gravityMagnitude, decay);
        CoupleNeighbourWidths();
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            for (int ci = 0; ci < ChainCount; ci++)
            {
                var c = chains[ci];
                c.points[0] = targets[ci][0];
                for (int b = 0; b < SegmentCount; b++)
                {
                    ProjectSegment(c, b, true);
                    ProjectContacts(c, b, true);
                }
                ProjectLengths(c);
            }
        }
        for (int ci = 0; ci < ChainCount; ci++)
        {
            var c = chains[ci];
            for (int b = 0; b < SegmentCount; b++)
            {
                ProjectSegment(c, b, b == 0);
                ProjectContacts(c, b, false);
                maximumLengthError = Mathf.Max(maximumLengthError, Mathf.Abs(Vector3.Distance(c.points[b], c.points[b + 1]) - c.lengths[b]));
            }
            GatherFinalContacts(c);
            UpdateVelocities(c);
            MeasureContactResidual(c);
        }
    }

    void CreateScaffold()
    {
        var source = chains[3];
        scaffold = new Chain
        {
            points = new Vector3[PointCount], velocity = new Vector3[PointCount],
            predicted = new Vector3[PointCount], before = new Vector3[PointCount],
            carried = new Vector3[PointCount], transport = new Vector3[PointCount],
            contactNormals = new Vector3[PointCount], contactVelocities = new Vector3[PointCount],
            contactCounts = new int[PointCount], touching = new bool[PointCount],
            frames = new Quaternion[SegmentCount],
            restVectors = source.restVectors, lengths = source.lengths,
            neutralHeadOffsets = source.neutralHeadOffsets, currentTarget = source.currentTarget,
            bindAllowance = source.bindAllowance
        };
    }

    void Predict(Chain c, Vector3 root, Vector3 gravity, float gravityMagnitude, float decay)
    {
            var rootDelta = root - c.points[0];
            var rootVelocity = rootDelta / FixedStep;
            c.points[0] = root;
            for (int p = 1; p < PointCount; p++)
            {
                // Carry is deliberately small and does not rotate the strand with the head.
                c.carried[p] = rootDelta * Mathf.Clamp01(rootCarry) * Mathf.Lerp(1f, .3f, (p - 1) / 3f);
                c.transport[p] = rootVelocity - c.carried[p] / FixedStep;
                c.points[p] += c.carried[p];
                c.before[p] = c.points[p];
                c.contactNormals[p] = c.contactVelocities[p] = Vector3.zero;
                c.contactCounts[p] = 0;
            }
            for (int b = 0; b < SegmentCount; b++)
            {
                var frame = ParentFrame(c, b);
                var restTip = c.points[b] + frame * c.restVectors[b];
                float spring = Mathf.Max(0, stiffness) * SpringScale[b];
                // This is the neutral load supported by the styled hair, transported by
                // the local parent. It preserves the original upright rest shape while
                // gravity remains in world space during leaning and swinging.
                var restLoad = frame * bindGravityLocal * gravityMagnitude;
                var acceleration = (restTip - c.points[b + 1]) * spring + gravity - restLoad;
                // Dampen motion within the translating strand frame. Damping world
                // velocity would add a large artificial drag even at constant speed.
                var transportVelocity = c.transport[b + 1];
                var damped = transportVelocity + (c.velocity[b + 1] - transportVelocity) * decay;
                c.velocity[b + 1] = transportVelocity + Vector3.ClampMagnitude(damped + acceleration * FixedStep - transportVelocity, Mathf.Max(.01f, maximumDynamicSpeed));
                c.predicted[b + 1] = c.points[b + 1] + c.velocity[b + 1] * FixedStep;
            }
            for (int p = 1; p < PointCount; p++) c.points[p] = c.predicted[p];
    }

    void UpdateVelocities(Chain c)
    {
            for (int p = 1; p < PointCount; p++)
            {
                var v = (c.points[p] - c.before[p]) / FixedStep;
                if (c.contactCounts[p] > 0)
                {
                    var normal = c.contactNormals[p].normalized;
                    var bodyVelocity = c.contactVelocities[p] / c.contactCounts[p];
                    var carriedVelocity = c.carried[p] / FixedStep;
                    var relative = v + carriedVelocity - bodyVelocity;
                    float normalSpeed = Vector3.Dot(relative, normal);
                    var tangent = relative - normal * normalSpeed;
                    float integratedNormalSpeed = Vector3.Dot(c.velocity[p] + carriedVelocity - bodyVelocity, normal);
                    // A positional penetration correction must not create an outward
                    // bounce. Preserve only outward speed already present before it.
                    float outward = Mathf.Min(Mathf.Max(0, normalSpeed), Mathf.Max(0, integratedNormalSpeed));
                    relative = normal * outward + tangent * Mathf.Exp(-Mathf.Max(0, contactFriction) * FixedStep);
                    v = relative + bodyVelocity - carriedVelocity;
                }
                var dynamicVelocity = Vector3.ClampMagnitude(v - c.transport[p], Mathf.Max(.01f, maximumDynamicSpeed));
                c.velocity[p] = c.transport[p] + dynamicVelocity;
                c.touching[p] = c.contactCounts[p] > 0;
                maximumObservedSpeed = Mathf.Max(maximumObservedSpeed, dynamicVelocity.magnitude);
            }
    }

    Quaternion ParentFrame(Chain c, int segment)
    {
        var frame = stepHeadRotation;
        for (int b = 0; b < segment; b++)
        {
            var rest = frame * c.restVectors[b];
            var actual = c.points[b + 1] - c.points[b];
            frame = RotationBetween(rest, actual) * frame;
        }
        return frame;
    }

    void RegularizeNeighbourFrames()
    {
        // A hard neighbour projection transmits an arm contact through the whole
        // curtain. This weak, time-based pass only discourages extreme divergence;
        // local bend limits and transported frames handle ordinary shape continuity.
        float rate = 1f - Mathf.Exp(-Mathf.Max(0, neighbourCoupling) * 12f * FixedStep);
        if (rate <= 0) return;
        for (int ci = 0; ci < ChainCount; ci++)
        {
            var c = chains[ci];
            var frame = stepHeadRotation;
            for (int b = 0; b < SegmentCount; b++)
            {
                frame = RotationBetween(frame * c.restVectors[b], c.points[b + 1] - c.points[b]) * frame;
                materialFrames[ci][b] = frame;
            }
        }
        for (int b = 0; b < SegmentCount; b++)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                for (int pair = 0; pair < ChainCount - 1; pair++)
                {
                    int a = pass == 0 ? pair : ChainCount - 2 - pair;
                    int z = a + 1;
                    float limit = NeighbourFrameAngles[b];
                    float angle = Quaternion.Angle(materialFrames[a][b], materialFrames[z][b]);
                    if (angle <= limit) continue;
                    float blend = (angle - limit) / (2f * angle) * rate;
                    if (chains[a].touching[b + 1] || chains[z].touching[b + 1]) blend *= .25f;
                    var left = materialFrames[a][b];
                    var right = materialFrames[z][b];
                    materialFrames[a][b] = Quaternion.Slerp(left, right, blend);
                    materialFrames[z][b] = Quaternion.Slerp(right, left, blend);
                }
            }
        }
        for (int ci = 0; ci < ChainCount; ci++)
        {
            var c = chains[ci];
            c.points[0] = targets[ci][0];
            for (int b = 0; b < SegmentCount; b++)
                c.points[b + 1] = c.points[b] + materialFrames[ci][b] * c.restVectors[b];
        }
    }

    void ProjectSegment(Chain c, int b, bool limitBend)
    {
        var rest = ParentFrame(c, b) * c.restVectors[b].normalized;
        var direction = c.points[b + 1] - c.points[b];
        direction = direction.sqrMagnitude > Epsilon ? direction.normalized : rest;
        if (limitBend) direction = LimitDirection(rest, direction, LocalBendAngles[b]);
        if (c != scaffold) direction = LimitDirection(scaffoldFrames[b] * c.restVectors[b].normalized, direction, ScaffoldFlexAngles[b]);
        SetSegmentFrame(c, b, direction);
    }

    void SetSegmentFrame(Chain c, int b, Vector3 direction)
    {
        var reference = c == scaffold ? ParentFrame(c, b) : scaffoldFrames[b];
        var frame = RotationBetween(reference * c.restVectors[b], direction) * reference;
        // The coarse, shared-weight hair cannot support a buckled rod. A smooth
        // gravity-oriented rest frame gives a one-sided drape reference, while
        // bounded material rotation also prevents axial twist from folding panels.
        float excursion = GuideExcursion[b] + (c == scaffold ? 0 : ScaffoldFlexAngles[b]);
        frame = LimitFrame(gravityRestFrames[b], frame, excursion);
        frame = LimitFrame(stepHeadRotation, frame, MaterialFrameLimit[b]);
        c.frames[b] = frame;
        c.points[b + 1] = c.points[b] + frame * c.restVectors[b];
    }

    void ProjectLengths(Chain c)
    {
        // Contact gradients move both endpoints. Distribute the resulting extension
        // back through the chain instead of sending every correction into the tip.
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < SegmentCount; i++)
            {
                int b = pass == 0 ? SegmentCount - 1 - i : i;
                var delta = c.points[b + 1] - c.points[b];
                float length = delta.magnitude;
                if (length < Epsilon) continue;
                float firstMass = b == 0 ? 0 : 1;
                var correction = delta * ((length - c.lengths[b]) / (length * (firstMass + 1)));
                c.points[b] += correction * firstMass;
                c.points[b + 1] -= correction;
            }
    }

    void CoupleNeighbourWidths()
    {
        for (int p = 1; p < PointCount; p++)
        {
            float depth = p / 4f;
            for (int ci = 0; ci < ChainCount - 1; ci++)
            {
                var a = chains[ci];
                var b = chains[ci + 1];
                var delta = b.points[p] - a.points[p];
                float distance = delta.magnitude;
                if (distance < Epsilon) continue;
                float restWidth = Vector3.Distance(a.neutralHeadOffsets[p], b.neutralHeadOffsets[p]);
                float minimum = restWidth * .55f;
                float maximum = restWidth + Mathf.Max(.01f, neighbourOffsetLimit) * (.5f + depth);
                float correction = distance - Mathf.Clamp(distance, minimum, maximum);
                if (Mathf.Abs(correction) < Epsilon) continue;
                float blend = Mathf.Clamp01(neighbourCoupling) * Mathf.Lerp(1f, .25f, depth);
                if (a.touching[p] || b.touching[p]) blend *= .25f;
                // Keep contact freedom; this constraint only protects broad surface width.
                var shift = delta * (correction / distance * .5f * blend);
                a.points[p] += shift;
                b.points[p] -= shift;
            }
        }
    }

    void UpdateCollisionPose(float alpha)
    {
        var anchor = Vector3.Lerp(previousHipsPosition, hips.position, alpha);
        var rotation = Quaternion.Slerp(previousHipsRotation, hips.rotation, alpha);
        var previousInverse = Quaternion.Inverse(previousHipsRotation);
        var currentInverse = Quaternion.Inverse(hips.rotation);
        foreach (var cap in capsules)
        {
            var a = anchor + rotation * Vector3.Lerp(previousInverse * (cap.previousA - previousHipsPosition), currentInverse * (cap.currentA - hips.position), alpha);
            var b = anchor + rotation * Vector3.Lerp(previousInverse * (cap.previousB - previousHipsPosition), currentInverse * (cap.currentB - hips.position), alpha);
            cap.velocityA = (a - cap.stepA) / FixedStep;
            cap.velocityB = (b - cap.stepB) / FixedStep;
            cap.stepA = a;
            cap.stepB = b;
        }
    }

    void ProjectContacts(Chain c, int b, bool distribute)
    {
        for (int k = 0; k < capsules.Length; k++)
        {
            var cap = capsules[k];
            if (c == scaffold && !IsTorso(cap)) continue;
            float radius = Mathf.Max(.006f, cap.radius + hairRadius - c.bindAllowance[b, k]);
            for (int sampleIndex = 1; sampleIndex <= 3; sampleIndex++)
            {
                float u = sampleIndex / 3f;
                var sample = Vector3.Lerp(c.points[b], c.points[b + 1], u);
                float capsuleU;
                var close = Nearest(sample, cap.stepA, cap.stepB, out capsuleU);
                var away = sample - close;
                float distance = away.magnitude;
                if (distance >= radius - .00001f) continue;
                var normal = distance > Epsilon ? away / distance : ContactFallback(c, b, close);
                float penetration = radius - distance + .00001f;
                if (distribute && b > 0)
                {
                    float first = 1f - u;
                    float inverseGradient = 1f / (first * first + u * u);
                    c.points[b] += normal * (penetration * first * inverseGradient);
                    c.points[b + 1] += normal * (penetration * u * inverseGradient);
                    collisionCorrections++;
                    continue;
                }
                var proposed = c.points[b + 1] + normal * (penetration / u);
                var direction = proposed - c.points[b];
                if (direction.sqrMagnitude < Epsilon) continue;
                direction.Normalize();
                // The existing fixed crown cannot be lifted out of an intersecting
                // arm proxy. Keep its transition cone and report any infeasible contact.
                if (b == 0 || c == scaffold) direction = LimitDirection(ParentFrame(c, b) * c.restVectors[b].normalized, direction, LocalBendAngles[b]);
                if (c != scaffold) direction = LimitDirection(scaffoldFrames[b] * c.restVectors[b].normalized, direction, ScaffoldFlexAngles[b]);
                SetSegmentFrame(c, b, direction);
                c.contactNormals[b + 1] += normal;
                c.contactVelocities[b + 1] += Vector3.Lerp(cap.velocityA, cap.velocityB, capsuleU);
                c.contactCounts[b + 1]++;
                collisionCorrections++;
            }
        }
    }

    Vector3 ContactFallback(Chain c, int segment, Vector3 close)
    {
        var away = c.currentTarget[segment + 1] - close;
        if (away.sqrMagnitude < Epsilon) away = stepHeadRotation * Vector3.back;
        return away.normalized;
    }

    void GatherFinalContacts(Chain c)
    {
        for (int p = 1; p < PointCount; p++)
        {
            c.contactCounts[p] = 0;
            c.contactNormals[p] = c.contactVelocities[p] = Vector3.zero;
        }
        for (int b = 0; b < SegmentCount; b++)
            for (int k = 0; k < capsules.Length; k++)
            {
                var cap = capsules[k];
                if (c == scaffold && !IsTorso(cap)) continue;
                float radius = Mathf.Max(.006f, cap.radius + hairRadius - c.bindAllowance[b, k]);
                for (int sampleIndex = 1; sampleIndex <= 3; sampleIndex++)
                {
                    var p = Vector3.Lerp(c.points[b], c.points[b + 1], sampleIndex / 3f);
                    float u;
                    var close = Nearest(p, cap.stepA, cap.stepB, out u);
                    var away = p - close;
                    float distance = away.magnitude;
                    if (distance > radius + .0005f) continue;
                    c.contactNormals[b + 1] += distance > Epsilon ? away / distance : ContactFallback(c, b, close);
                    c.contactVelocities[b + 1] += Vector3.Lerp(cap.velocityA, cap.velocityB, u);
                    c.contactCounts[b + 1]++;
                }
            }
    }

    void MeasureContactResidual(Chain c)
    {
        for (int b = 0; b < SegmentCount; b++)
            for (int k = 0; k < capsules.Length; k++)
            {
                var cap = capsules[k];
                float radius = Mathf.Max(.006f, cap.radius + hairRadius - c.bindAllowance[b, k]);
                for (int sample = 1; sample <= 3; sample++)
                {
                    float unused;
                    var p = Vector3.Lerp(c.points[b], c.points[b + 1], sample / 3f);
                    float residual = radius - Vector3.Distance(p, Nearest(p, cap.stepA, cap.stepB, out unused));
                    maximumContactResidual = Mathf.Max(maximumContactResidual, residual);
                }
            }
    }

    void ApplyPose()
    {
        foreach (var c in chains)
        {
            var inverseHead = Quaternion.Inverse(head.rotation);
            for (int b = 0; b < SegmentCount; b++)
            {
                var world = c.frames[b] * inverseHead * c.targetRotations[b];
                c.bones[b].rotation = Quaternion.Slerp(c.targetRotations[b], world, Mathf.Clamp01(weight));
                c.lastApplied[b] = c.bones[b].localRotation;
                maximumBoneAngle = Mathf.Max(maximumBoneAngle, Quaternion.Angle(c.targetRotations[b], c.bones[b].rotation));
            }
        }
    }

    Vector3 GravityDirection() { return gravityAcceleration.sqrMagnitude > Epsilon ? gravityAcceleration.normalized : Vector3.down; }
    static bool IsTorso(BodyCapsule cap) { return cap.name == "Chest" || cap.name == "Waist"; }
    static Quaternion LimitFrame(Quaternion reference, Quaternion actual, float degrees)
    {
        float angle = Quaternion.Angle(reference, actual);
        return angle > degrees ? Quaternion.Slerp(reference, actual, degrees / angle) : actual;
    }
    // Native convenience rotation functions can discard very small angular changes.
    // Returning the actual direction when already inside the limit prevents that
    // dead zone from turning small fixed-step input into rigid root carry.
    static Vector3 LimitDirection(Vector3 rest, Vector3 actual, float degrees)
    {
        float dot = Mathf.Clamp(Vector3.Dot(rest, actual), -1f, 1f);
        if (dot >= Mathf.Cos(degrees * Mathf.Deg2Rad)) return actual;
        var axis = Vector3.Cross(rest, actual);
        if (axis.sqrMagnitude < 1e-14f) axis = Vector3.Cross(rest, Mathf.Abs(rest.y) < .9f ? Vector3.up : Vector3.right);
        return Quaternion.AngleAxis(degrees, axis.normalized) * rest;
    }
    static Quaternion RotationBetween(Vector3 from, Vector3 to)
    {
        if (from.sqrMagnitude < 1e-14f || to.sqrMagnitude < 1e-14f) return Quaternion.identity;
        from.Normalize();
        to.Normalize();
        float dot = Mathf.Clamp(Vector3.Dot(from, to), -1f, 1f);
        var cross = Vector3.Cross(from, to);
        if (dot < -.999999f)
        {
            var axis = Vector3.Cross(from, Mathf.Abs(from.y) < .9f ? Vector3.up : Vector3.right);
            return Quaternion.AngleAxis(180f, axis.normalized);
        }
        var rotation = new Quaternion(cross.x, cross.y, cross.z, 1f + dot);
        return rotation.normalized;
    }
    static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    static Vector3 Nearest(Vector3 point, Vector3 a, Vector3 b, out float t)
    {
        var d = b - a;
        t = d.sqrMagnitude < Epsilon ? 0 : Mathf.Clamp01(Vector3.Dot(point - a, d) / d.sqrMagnitude);
        return a + d * t;
    }
    static float SegmentDistance(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        float best = float.PositiveInfinity;
        for (int i = 0; i <= 12; i++)
        {
            var p = Vector3.Lerp(a, b, i / 12f);
            float unused;
            best = Mathf.Min(best, Vector3.Distance(p, Nearest(p, c, d, out unused)));
        }
        return best;
    }

    void Awake() { Initialize(); }
    void OnEnable() { if (initialized) ResetSimulation(); }
    void OnDisable() { RestoreBindRotations(); }
    void LateUpdate() { if (!externalDrive) Advance(Time.deltaTime); }
}
