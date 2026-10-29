using System.Collections.Generic;
using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// 간파 예측 대상 캐시(할 일 132 정정1) — 간파 대상만 등록, 해제(디스폰·사망 = Unregister), 파괴·씬 언로드 정리, 거리·정면 허용각.
/// 등록·해제 훅 자체(MonsterBase 스폰·Dead 상태·디스폰, 허수아비 스폰·디스폰)는 네트워크 스폰이라 Play 확인.
/// </summary>
public sealed class InterruptTargetRegistryTests
{
    private readonly List<GameObject> _objects = new List<GameObject>();

    [SetUp]
    public void SetUp() => InterruptTargetRegistry.Clear();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in _objects)
            if (go != null)
                Object.DestroyImmediate(go);
        _objects.Clear();
        InterruptTargetRegistry.Clear();
    }

    // 허수아비 간파(IInterruptSlowMotionTarget·IInterruptWindowReplica) — RequireComponent 로 TrainingDummy(Unit)가 같이 붙는다.
    private TrainingDummy NewTarget(string name = "Target")
    {
        var go = new GameObject(name);
        _objects.Add(go);
        go.AddComponent<TrainingDummyInterrupt>();
        return go.GetComponent<TrainingDummy>();
    }

    [Test]
    public void 간파_대상만_등록되고_중복은_하나()
    {
        TrainingDummy target = NewTarget();
        Assert.IsTrue(InterruptTargetRegistry.Register(target));
        Assert.IsFalse(InterruptTargetRegistry.Register(target), "중복 등록 없음");
        Assert.AreEqual(1, InterruptTargetRegistry.Count);

        var plain = new GameObject("Plain");
        _objects.Add(plain);
        Assert.IsFalse(InterruptTargetRegistry.Register(plain.AddComponent<TrainingDummy>()), "간파 대상이 아닌 Unit 은 무시");
        Assert.IsFalse(InterruptTargetRegistry.Register(null));
        Assert.AreEqual(1, InterruptTargetRegistry.Count);
    }

    [Test]
    public void 사망_디스폰_해제()
    {
        TrainingDummy target = NewTarget();
        InterruptTargetRegistry.Register(target);
        Assert.IsTrue(InterruptTargetRegistry.Unregister(target));
        Assert.IsFalse(InterruptTargetRegistry.Contains(target));
        Assert.IsFalse(InterruptTargetRegistry.Unregister(target), "두 번째 해제는 없음");

        var live = new List<InterruptTargetRegistry.Entry>();
        InterruptTargetRegistry.CopyLiveTo(live);
        Assert.AreEqual(0, live.Count);
    }

    [Test]
    public void 파괴된_대상은_조회_때_정리()
    {
        TrainingDummy a = NewTarget("A");
        TrainingDummy b = NewTarget("B");
        InterruptTargetRegistry.Register(a);
        InterruptTargetRegistry.Register(b);
        Object.DestroyImmediate(a.gameObject);

        var live = new List<InterruptTargetRegistry.Entry>();
        InterruptTargetRegistry.CopyLiveTo(live);
        Assert.AreEqual(1, live.Count);
        Assert.AreSame(b, live[0].Unit);
        Assert.AreEqual(1, InterruptTargetRegistry.Count);
    }

    [Test]
    public void 씬_전환_언로드_씬의_대상은_빠진다()
    {
        TrainingDummy target = NewTarget();
        InterruptTargetRegistry.Register(target);

        Assert.AreEqual(0, InterruptTargetRegistry.PruneScene(default(Scene)), "다른 씬이면 그대로");
        Assert.AreEqual(1, InterruptTargetRegistry.Count);

        Assert.AreEqual(1, InterruptTargetRegistry.PruneScene(target.gameObject.scene));
        Assert.AreEqual(0, InterruptTargetRegistry.Count);
    }

    [Test]
    public void 거리는_판정_레이어_콜라이더_표면까지_정면_판정_없으면_180()
    {
        TrainingDummy target = NewTarget();
        var box = target.gameObject.AddComponent<BoxCollider>();
        box.size = new Vector3(2f, 2f, 2f);                  // 원점 중심, 표면 x = 1
        InterruptTargetRegistry.Register(target);

        var live = new List<InterruptTargetRegistry.Entry>();
        InterruptTargetRegistry.CopyLiveTo(live);
        InterruptTargetRegistry.Entry entry = live[0];

        int layerBit = 1 << target.gameObject.layer;
        Assert.IsTrue(entry.TryGetHorizontalDistance(new Vector3(3f, 0f, 0f), layerBit, out float distance));
        Assert.AreEqual(2f, distance, 1e-3f);
        Assert.IsFalse(entry.TryGetHorizontalDistance(new Vector3(3f, 0f, 0f), 0, out _), "판정 레이어 밖 콜라이더는 안 본다");
        Assert.AreEqual(InterruptTargetGeometry.NoFrontRule, entry.FrontAngle, "허수아비 = 정면 판정 없음");
    }
}
