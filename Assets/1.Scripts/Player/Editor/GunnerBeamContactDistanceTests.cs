using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 거너 좌클릭·Q 벽까지 거리 = 구 표면 <b>접촉 거리</b>(2026-11-06). 예전엔 SphereCast 구 중심 거리라 판정·빔·조준선이
/// 벽 표면보다 반경만큼 앞에서 끝났다(Q 가 차단 박스를 가진 상자를 정면에서 못 맞힘).
/// 순수 함수 <see cref="GunnerBeamAttack.ContactDistance(float, Vector3, Vector3, Vector3, float)"/> 와 실제 SphereCast 로 잰 <see cref="GunnerBeamAttack.CastLength"/>.
/// </summary>
public sealed class GunnerBeamContactDistanceTests
{
    static readonly Vector3 Far = new Vector3(20000f, 5000f, 20000f);

    readonly List<GameObject> created = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in created)
            if (go != null) Object.DestroyImmediate(go);
        created.Clear();
    }

    [Test]
    public void HeadOn_ContactIsCenterDistancePlusRadius()
    {
        // 구 중심 4m 이동, 접촉점은 축 위 4.6m(반경 0.6 정면 접촉)
        float d = GunnerBeamAttack.ContactDistance(4f, new Vector3(0f, 1f, 4.6f), new Vector3(0f, 1f, 0f), Vector3.forward, 0.6f);
        Assert.That(d, Is.EqualTo(4.6f).Within(1e-4f));
    }

    [Test]
    public void GrazingCorner_ContactIsShorterThanCenterPlusRadius()
    {
        // 옆으로 스친 접촉 — 축 투영 4.2m 는 [4, 4.6] 안이라 그대로
        float d = GunnerBeamAttack.ContactDistance(4f, new Vector3(0.5f, 1f, 4.2f), new Vector3(0f, 1f, 0f), Vector3.forward, 0.6f);
        Assert.That(d, Is.EqualTo(4.2f).Within(1e-4f));
    }

    [Test]
    public void ContactIsClampedBetweenCenterAndCenterPlusRadius()
    {
        Vector3 origin = Vector3.zero;
        Assert.That(GunnerBeamAttack.ContactDistance(4f, new Vector3(0f, 0f, 3f), origin, Vector3.forward, 0.6f), Is.EqualTo(4f).Within(1e-4f));
        Assert.That(GunnerBeamAttack.ContactDistance(4f, new Vector3(0f, 0f, 9f), origin, Vector3.forward, 0.6f), Is.EqualTo(4.6f).Within(1e-4f));
    }

    [Test]
    public void InitialOverlap_IsZero()
    {
        // 시작부터 겹친 접촉 — Unity 는 distance 0 · point 0 을 준다
        Assert.That(GunnerBeamAttack.ContactDistance(0f, Vector3.zero, new Vector3(3f, 1f, 3f), Vector3.forward, 0.6f), Is.EqualTo(0f));
    }

    [Test]
    public void CastLength_ReachesWallSurface()
    {
        // 정면 벽 앞면 z = 5. 반경 0.6 → 예전 값은 4.4(구 중심), 지금은 5(벽 표면)
        // 열린 씬의 지형과 겹치지 않게 먼 곳에서 잰다
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        created.Add(wall);
        wall.transform.position = Far + new Vector3(0f, 1f, 5.5f);   // 두께 1 → 앞면 z = 5
        wall.layer = 0;                                       // Default — 거너 차단 레이어
        Physics.SyncTransforms();

        GameObject shooter = new GameObject("BeamShooter");
        created.Add(shooter);
        GunnerBeamAttack beam = shooter.AddComponent<GunnerBeamAttack>();

        float length = beam.CastLength(Far + new Vector3(0f, 1f, 0f), Vector3.forward, 16f, 0.6f, 1 << 0);
        Assert.That(length, Is.EqualTo(5f).Within(0.01f));
    }

    [Test]
    public void CastLength_NoWall_IsRange()
    {
        GameObject shooter = new GameObject("BeamShooter");
        created.Add(shooter);
        GunnerBeamAttack beam = shooter.AddComponent<GunnerBeamAttack>();

        float length = beam.CastLength(Far + new Vector3(0f, 500f, 0f), Vector3.forward, 16f, 0.6f, 1 << 0);
        Assert.That(length, Is.EqualTo(16f).Within(1e-4f));
    }
}
