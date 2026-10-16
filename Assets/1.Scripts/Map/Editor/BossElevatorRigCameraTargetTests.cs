using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 엘리베이터 카메라 지점(<see cref="BossElevatorRig.CameraTarget"/>) — 칸 안 <c>CameraFollowTarget</c> 을 찾고(없으면 칸),
/// 그 지점이 플레이어 추적 태그를 달지 않는다(관전 목록·안개 기준·스무더에 섞이지 않게).
/// </summary>
public sealed class BossElevatorRigCameraTargetTests
{
    const string ZonePath = "Assets/2.Prefabs/Environment/Layouts/Zones/ZoneS_typeBossEnter.prefab";
    const string PlayerFollowTag = "CameraFollowTarget";

    [Test]
    public void 보스_진입_존_리그는_칸_안_CameraFollowTarget_을_카메라_지점으로_쓰고_태그는_없다()
    {
        GameObject zone = AssetDatabase.LoadAssetAtPath<GameObject>(ZonePath);
        Assert.IsNotNull(zone, "보스 진입 존 프리팹 경로 확인");
        BossElevatorRig rig = zone.GetComponentInChildren<BossElevatorRig>(true);
        Assert.IsNotNull(rig, "존에 BossElevatorRig 가 없다");
        Assert.IsNotNull(rig.Cab, "리그 _cab 참조가 끊겼다");

        Transform target = rig.CameraTarget;
        Assert.AreNotSame(rig.Cab, target, "칸 안 CameraFollowTarget 을 못 찾아 칸으로 폴백했다");
        Assert.AreEqual(BossElevatorRig.CameraTargetName, target.name);
        Assert.IsTrue(target.IsChildOf(rig.Cab), "카메라 지점은 칸과 함께 움직여야 한다(칸 아래)");
        Assert.IsFalse(target.CompareTag(PlayerFollowTag), "칸 지점이 플레이어 추적 태그를 달고 있다 — 관전 목록·안개 기준에 섞인다");

        string[] tagged = zone.GetComponentsInChildren<Transform>(true)
            .Where(t => t.CompareTag(PlayerFollowTag))
            .Select(t => t.name)
            .ToArray();
        Assert.That(tagged, Is.Empty, "존 안에 플레이어 추적 태그 오브젝트가 있다: " + string.Join(", ", tagged));
    }

    [Test]
    public void 카메라_지점이_없으면_칸으로_폴백한다()
    {
        GameObject cab = new GameObject("Cab");
        try
        {
            new GameObject("Door").transform.SetParent(cab.transform, false);
            Assert.AreSame(cab.transform, BossElevatorRig.FindCameraTarget(cab.transform));

            Transform point = new GameObject(BossElevatorRig.CameraTargetName).transform;
            point.SetParent(cab.transform.GetChild(0), false);
            Assert.AreSame(point, BossElevatorRig.FindCameraTarget(cab.transform), "깊이와 무관하게 찾는다");
            Assert.IsNull(BossElevatorRig.FindCameraTarget(null));
        }
        finally
        {
            Object.DestroyImmediate(cab);
        }
    }
}
