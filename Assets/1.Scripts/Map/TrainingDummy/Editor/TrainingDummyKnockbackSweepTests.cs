using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 허수아비 지속 넉백의 벽 앞 정지(할 일 51) — 거리 규칙(순수) + 실제 CapsuleCast 통합 1건.
/// 통합 테스트 패턴은 <see cref="PlayerMotionSweepStepTests"/> 를 따랐다(임시 콜라이더 → TearDown 에서 삭제).
/// </summary>
public sealed class TrainingDummyKnockbackSweepTests
{
    const float Skin = 0.02f;
    const float TestY = 500f;   // 씬 지오메트리와 겹치지 않게 멀리

    readonly List<GameObject> _created = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in _created)
            if (go != null) Object.DestroyImmediate(go);
        _created.Clear();
        Physics.SyncTransforms();
    }

    static KnockbackSweepHit Hit(float distance, bool overlap = false, bool self = false) =>
        new KnockbackSweepHit { Distance = distance, StartedOverlapping = overlap, IsSelf = self };

    static float Resolve(float requested, params KnockbackSweepHit[] hits) =>
        TrainingDummyKnockbackSweep.ResolveDistance(requested, hits, hits.Length, Skin);

    // ─── 거리 규칙 ─────────────────────────────────────────────────────

    [Test]
    public void 히트가_없으면_요청_거리()
    {
        Assert.AreEqual(0.3f, Resolve(0.3f), 1e-6f);
    }

    [Test]
    public void 히트가_요청보다_멀면_요청_거리()
    {
        Assert.AreEqual(0.3f, Resolve(0.3f, Hit(0.5f)), 1e-6f);
    }

    [Test]
    public void 히트가_가까우면_히트에서_스킨을_뺀_거리()
    {
        Assert.AreEqual(0.2f - Skin, Resolve(0.3f, Hit(0.2f)), 1e-6f);
    }

    [Test]
    public void 여러_히트_중_가장_가까운_것에서_멈춘다()
    {
        Assert.AreEqual(0.1f - Skin, Resolve(0.3f, Hit(0.25f), Hit(0.1f), Hit(0.2f)), 1e-6f);
    }

    [Test]
    public void 히트가_스킨보다_가까우면_0()
    {
        Assert.AreEqual(0f, Resolve(0.3f, Hit(Skin * 0.5f)), 1e-6f);
    }

    [Test]
    public void 시작부터_겹치면_0()
    {
        Assert.AreEqual(0f, Resolve(0.3f, Hit(0.25f), Hit(0f, overlap: true)), 1e-6f);
    }

    [Test]
    public void 자기_콜라이더는_무시한다()
    {
        Assert.AreEqual(0.3f, Resolve(0.3f, Hit(0f, overlap: true, self: true), Hit(0.05f, self: true)), 1e-6f);
    }

    [Test]
    public void 요청이_0이나_음수면_0()
    {
        Assert.AreEqual(0f, Resolve(0f), 1e-6f);
        Assert.AreEqual(0f, Resolve(-0.5f), 1e-6f);
        Assert.AreEqual(0f, Resolve(float.NaN), 1e-6f);
    }

    // ─── 실제 물리 ─────────────────────────────────────────────────────

    [Test]
    public void 실제_Wall_레이어_벽_앞에서_멈춘다()
    {
        int wallLayer = LayerMask.NameToLayer(GameLayers.Wall);
        if (wallLayer < 0)
            Assert.Ignore("Wall 레이어가 TagManager 에 없다");

        // 몸: 반경 0.5 캡슐(허수아비 프리팹과 같은 치수), 원점에 서 있다.
        var body = new GameObject("DummySweepTestBody");
        _created.Add(body);
        body.transform.position = new Vector3(0f, TestY, 0f);
        var capsule = body.AddComponent<CapsuleCollider>();
        capsule.radius = 0.5f;
        capsule.height = 2f;
        capsule.center = new Vector3(0f, 1f, 0f);

        // 벽: 앞면이 z = 2.0, 두께 1.27(튜토리얼 북벽과 같은 두께).
        var wall = new GameObject("DummySweepTestWall") { layer = wallLayer };
        _created.Add(wall);
        wall.transform.position = new Vector3(0f, TestY + 1.5f, 2f + 0.635f);
        wall.AddComponent<BoxCollider>().size = new Vector3(6f, 3f, 1.27f);
        Physics.SyncTransforms();

        Vector3 p1 = body.transform.position + Vector3.up * 0.5f;
        Vector3 p2 = body.transform.position + Vector3.up * 1.5f;
        const float radius = 0.5f;
        int mask = TrainingDummyKnockbackSweep.BlockMask;

        // 벽보다 한참 멀리 밀어도 벽 면 − 반경 − 스킨 에서 멈춘다(관통 없음).
        float allowed = TrainingDummyKnockbackSweep.Sweep(p1, p2, radius, Vector3.forward, 5f, body.transform, mask, Skin);
        Assert.AreEqual(2f - radius - Skin, allowed, 0.01f);

        // 벽에서 먼 쪽(뒤)으로는 요청 거리 그대로. 자기 캡슐은 막힘이 아니다(마스크 밖이어도 확인).
        float back = TrainingDummyKnockbackSweep.Sweep(p1, p2, radius, Vector3.back, 1f, body.transform, mask | (1 << body.layer), Skin);
        Assert.AreEqual(1f, back, 1e-4f);

        // 벽을 Wall 레이어에서 빼면(Default) 막지 않는다 — 범위는 Wall·Barrier 만.
        wall.layer = 0;
        Physics.SyncTransforms();
        float notWall = TrainingDummyKnockbackSweep.Sweep(p1, p2, radius, Vector3.forward, 5f, body.transform, mask, Skin);
        Assert.AreEqual(5f, notWall, 1e-4f);
    }
}
