using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// `PlayerMotionSweep`의 단차(stepOffset) 해석 테스트.
///
/// ⚠️ <b>왜 여기만 .asmdef가 없는가</b> — 옆 동네 `Player/Dash/Tests/EditMode/`는 전용 asmdef를
/// 갖는데 여기는 없다. 흉내 내지 않은 게 아니라 **낼 수 없다**: asmdef로 정의된 어셈블리는
/// 미리 정의된 `Assembly-CSharp`을 참조할 수 없는데, 대상인 `PlayerMotionSweep`이 거기 있다.
/// (Dash 쪽은 `BeaverLobby.Player.Dash` asmdef 안에 있어서 참조가 된다.)
/// 그래서 `Editor/` 폴더에 두어 `Assembly-CSharp-Editor`로 컴파일시킨다 — 이 어셈블리는
/// `nunit.framework`·`UnityEngine/UnityEditor.TestRunner`를 참조하므로 Test Runner가 발견한다.
///
/// 대가: `UNITY_INCLUDE_TESTS` define 제약을 걸 수 없어 테스트 코드가 항상 에디터 어셈블리에 들어간다.
/// 제대로 분리하려면 Motor 계열을 자체 asmdef로 옮겨야 하고, 그건 순수 로직 추출이 예정된
/// 4단계에서 같이 처리할 일이다. (PLAN-player-motor.md §3 4단계)
///
/// 실제 `Physics.CapsuleCast`를 타는 통합 테스트다 — 스윕 로직이 물리 쿼리 결과에 의존하므로
/// 순수 단위 테스트로는 의미가 없다. 그래서 매 테스트가 임시 콜라이더를 만들고 TearDown에서 지운다.
/// </summary>
public sealed class PlayerMotionSweepStepTests
{
    private const float StepOffset = 0.3f;
    private const float Skin = 0.02f;
    private const float MaxWalkableAngle = 45f;
    private const int MaxIterations = 3;
    private const float TestFloorY = 100f;

    private readonly List<GameObject> createdObjects = new List<GameObject>();
    private readonly RaycastHit[] castBuffer = new RaycastHit[8];

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject createdObject in createdObjects)
        {
            if (createdObject != null)
                Object.DestroyImmediate(createdObject);
        }

        createdObjects.Clear();
        Physics.SyncTransforms();
    }

    [Test]
    public void Grounded_HorizontalMove_ClimbsStepAtOffsetHeight()
    {
        CapsuleCollider capsule = CreateCapsule(TestFloorY + 1f);
        CreateBox("Floor", new Vector3(0f, TestFloorY - 0.5f, 0f), new Vector3(10f, 1f, 10f));
        CreateBox("Step", new Vector3(1f, TestFloorY + StepOffset * 0.5f, 0f), new Vector3(1f, StepOffset, 2f));
        Physics.SyncTransforms();

        Vector3 resolved = Resolve(capsule, new Vector3(0.6f, 0f, 0f), isGrounded: true, stepOffset: StepOffset);

        Assert.That(resolved.x, Is.GreaterThan(0.55f));
        Assert.That(resolved.y, Is.EqualTo(StepOffset).Within(0.001f));
    }

    [Test]
    public void Grounded_HorizontalMove_DoesNotClimbStepAboveOffset()
    {
        const float tooHighStep = StepOffset + 0.01f;
        CapsuleCollider capsule = CreateCapsule(TestFloorY + 1f);
        CreateBox("Floor", new Vector3(0f, TestFloorY - 0.5f, 0f), new Vector3(10f, 1f, 10f));
        CreateBox("Step", new Vector3(1f, TestFloorY + tooHighStep * 0.5f, 0f), new Vector3(1f, tooHighStep, 2f));
        Physics.SyncTransforms();

        Vector3 desired = new Vector3(0.6f, 0f, 0f);
        Vector3 withoutStep = Resolve(capsule, desired, isGrounded: true, stepOffset: 0f);
        Vector3 withStep = Resolve(capsule, desired, isGrounded: true, stepOffset: StepOffset);

        Assert.That(withStep.x, Is.EqualTo(withoutStep.x).Within(0.001f));
        Assert.That(withStep.y, Is.EqualTo(withoutStep.y).Within(0.001f));
        Assert.That(withStep.z, Is.EqualTo(withoutStep.z).Within(0.001f));
        Assert.That(withStep.x, Is.LessThan(desired.x * 0.9f));
    }

    [Test]
    public void Grounded_SteepSlope_IsNotBypassedByStepResolution()
    {
        CapsuleCollider capsule = CreateCapsule(TestFloorY + 1f);
        CreateBox("Floor", new Vector3(0f, TestFloorY - 0.5f, 0f), new Vector3(10f, 1f, 10f));

        GameObject ramp = CreateBox(
            "SteepRamp",
            new Vector3(1f, TestFloorY + 0.766f, 0f),
            new Vector3(4f, 0.1f, 2f));
        ramp.transform.rotation = Quaternion.Euler(0f, 0f, 60f);
        Physics.SyncTransforms();

        Vector3 desired = new Vector3(0.25f, 0f, 0f);
        Vector3 withoutStep = Resolve(capsule, desired, isGrounded: true, stepOffset: 0f);
        Vector3 withStep = Resolve(capsule, desired, isGrounded: true, stepOffset: StepOffset);

        Assert.That(withStep.x, Is.EqualTo(withoutStep.x).Within(0.001f));
        Assert.That(withStep.y, Is.EqualTo(withoutStep.y).Within(0.001f));
        Assert.That(withStep.z, Is.EqualTo(withoutStep.z).Within(0.001f));
    }

    [Test]
    public void Airborne_HorizontalMove_DoesNotUseStepResolution()
    {
        CapsuleCollider capsule = CreateCapsule(TestFloorY + 1.2f);
        CreateBox("Floor", new Vector3(0f, TestFloorY - 0.5f, 0f), new Vector3(10f, 1f, 10f));
        CreateBox("Step", new Vector3(1f, TestFloorY + StepOffset * 0.5f, 0f), new Vector3(1f, StepOffset, 2f));
        Physics.SyncTransforms();

        Vector3 desired = new Vector3(0.25f, 0f, 0f);
        Vector3 withoutStep = Resolve(capsule, desired, isGrounded: false, stepOffset: 0f);
        Vector3 withStep = Resolve(capsule, desired, isGrounded: false, stepOffset: StepOffset);

        Assert.That(withStep.x, Is.EqualTo(withoutStep.x).Within(0.001f));
        Assert.That(withStep.y, Is.EqualTo(withoutStep.y).Within(0.001f));
        Assert.That(withStep.z, Is.EqualTo(withoutStep.z).Within(0.001f));
    }

    [Test]
    public void GroundSnap_AfterClimbingStep_DoesNotPullPlayerBelowLandingSurface()
    {
        CapsuleCollider capsule = CreateCapsule(TestFloorY + 1f);
        CreateBox("Floor", new Vector3(0f, TestFloorY - 0.5f, 0f), new Vector3(10f, 1f, 10f));
        CreateBox("Step", new Vector3(1f, TestFloorY + StepOffset * 0.5f, 0f), new Vector3(1f, StepOffset, 2f));
        Physics.SyncTransforms();

        Vector3 desired = new Vector3(0.6f, -0.05f, 0f);
        Vector3 resolved = PlayerMotionSweep.Resolve(
            capsule,
            desired,
            new Vector3(desired.x, 0f, desired.z),
            true,
            StepOffset,
            MaxWalkableAngle,
            1 << 0,
            Skin,
            MaxIterations,
            castBuffer);

        Assert.That(resolved.x, Is.GreaterThan(0.55f));
        Assert.That(resolved.y, Is.EqualTo(StepOffset).Within(0.001f));
    }

    [Test]
    public void Airborne_Fall_IsStoppedByWalkableGround()
    {
        CapsuleCollider capsule = CreateCapsule(TestFloorY + 1f);
        CreateBox("Floor", new Vector3(0f, TestFloorY - 0.5f, 0f), new Vector3(10f, 1f, 10f));
        Physics.SyncTransforms();

        Vector3 resolved = PlayerMotionSweep.Resolve(
            capsule,
            Vector3.down * 0.5f,
            Vector3.zero,
            false,
            StepOffset,
            MaxWalkableAngle,
            1 << 0,
            Skin,
            MaxIterations,
            castBuffer);

        Assert.That(resolved.y, Is.GreaterThanOrEqualTo(-0.001f));
    }

    private Vector3 Resolve(CapsuleCollider capsule, Vector3 desired, bool isGrounded, float stepOffset)
    {
        return PlayerMotionSweep.Resolve(
            capsule,
            desired,
            new Vector3(desired.x, 0f, desired.z),
            isGrounded,
            stepOffset,
            MaxWalkableAngle,
            1 << 0,
            Skin,
            MaxIterations,
            castBuffer);
    }

    private CapsuleCollider CreateCapsule(float worldY)
    {
        GameObject gameObject = new GameObject("PlayerMotionSweepTestCapsule");
        createdObjects.Add(gameObject);
        gameObject.transform.position = new Vector3(0f, worldY, 0f);

        CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
        capsule.direction = 1;
        capsule.radius = 0.5f;
        capsule.height = 2f;
        return capsule;
    }

    private GameObject CreateBox(string name, Vector3 position, Vector3 size)
    {
        GameObject gameObject = new GameObject(name);
        createdObjects.Add(gameObject);
        gameObject.transform.position = position;

        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        box.size = size;
        return gameObject;
    }
}
