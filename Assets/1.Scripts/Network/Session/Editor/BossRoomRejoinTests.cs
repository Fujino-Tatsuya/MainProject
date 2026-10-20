using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 보스 룸 재합류(C14 R3c) — 모서리 선택(거리·동률·결정적), 경계 계산 폴백(회전된 룸 포함), InBossRoom 판정 표.
/// </summary>
public sealed class BossRoomRejoinTests
{
    readonly List<GameObject> _created = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (var go in _created)
        {
            if (go != null) Object.DestroyImmediate(go);
        }

        _created.Clear();
    }

    // ── 모서리 선택 ─────────────────────────────────────────

    static readonly Pose[] Corners =
    {
        new Pose(new Vector3(-10f, 0.5f, -10f), Quaternion.identity),
        new Pose(new Vector3(10f, 0.5f, -10f), Quaternion.identity),
        new Pose(new Vector3(10f, 0.5f, 10f), Quaternion.identity),
        new Pose(new Vector3(-10f, 0.5f, 10f), Quaternion.identity),
    };

    static RejoinSpawnContext BossRoomContext(bool inBossRoom, Vector3[] others, Vector3[] bosses, IReadOnlyList<Pose> corners = null)
    {
        return new RejoinSpawnContext(
            inBossRoom, true, new Vector3(100f, 2f, -50f), Quaternion.identity, new Vector3(0f, 10f, 0f),
            new List<Vector3>(others ?? new Vector3[0]),
            corners ?? Corners,
            bosses != null ? new List<Vector3>(bosses) : null);
    }

    [Test]
    public void 보스_룸이면_다른_플레이어와_보스에서_가장_먼_모서리()
    {
        // 플레이어(-9,-9)는 Corner1, 보스(9,-8)는 Corner2 근처.
        // Corner3 최소 거리 = 보스까지 √(1+324)≈18.0 / Corner4 최소 거리 = 플레이어까지 √(1+361)≈19.0 → Corner4.
        var pick = RejoinSpawnPicker.Pick(BossRoomContext(true,
            new[] { new Vector3(-9f, 0f, -9f) },
            new[] { new Vector3(9f, 0f, -8f) }));
        Assert.That(pick.IsBossRoomCorner, Is.True);
        Assert.That(pick.SlotIndex, Is.EqualTo(3));
        Assert.That(pick.Pose.position, Is.EqualTo(Corners[3].position), "모서리 Pose 그대로(spawn_Offset 낙하 없음)");
    }

    [Test]
    public void 최소_거리로_비교한다_가까운_한_명이_있으면_그_모서리는_진다()
    {
        // Corner1 에 한 명 가까이(최소 거리 1), 다른 셋은 멀리 떨어진 보스 하나만 본다.
        var others = new[] { new Vector3(-9f, 0f, -10f) };
        var bosses = new[] { new Vector3(0f, 0f, 0f) };
        var index = RejoinSpawnPicker.FarthestCorner(Corners, others, bosses);
        Assert.That(index, Is.Not.EqualTo(0));
        // Corner2·3·4 의 최소 거리: C2 → 플레이어 19, 보스 14.1 → 14.1 / C3 → 보스 14.1 / C4 → 플레이어 20.02, 보스 14.1 → 14.1 — 동률 → 앞 index
        Assert.That(index, Is.EqualTo(1));
    }

    [Test]
    public void 아무도_없으면_첫_모서리()
    {
        Assert.That(RejoinSpawnPicker.Pick(BossRoomContext(true, null, null)).SlotIndex, Is.EqualTo(0));
        Assert.That(RejoinSpawnPicker.FarthestCorner(Corners, null, null), Is.EqualTo(0));
    }

    [Test]
    public void 동률이면_낮은_index()
    {
        // 정중앙의 보스 하나 → 네 모서리 거리 같음.
        Assert.That(RejoinSpawnPicker.FarthestCorner(Corners, null, new[] { Vector3.zero }), Is.EqualTo(0));
        // Corner1 과 Corner2 사이 한가운데 → Corner3·4 가 동률 → 3(index 2).
        Assert.That(RejoinSpawnPicker.FarthestCorner(Corners, new[] { new Vector3(0f, 0f, -10f) }, null), Is.EqualTo(2));
    }

    [Test]
    public void 높이는_보지_않는다()
    {
        var flying = new[] { new Vector3(-10f, 50f, -10f) };
        var ground = new[] { new Vector3(-10f, 0f, -10f) };
        Assert.That(RejoinSpawnPicker.FarthestCorner(Corners, flying, null),
            Is.EqualTo(RejoinSpawnPicker.FarthestCorner(Corners, ground, null)));
        Assert.That(RejoinSpawnPicker.FarthestCorner(Corners, ground, null), Is.EqualTo(2), "Corner1 대각선");
    }

    [Test]
    public void 같은_입력이면_같은_결과_순서와_무관()
    {
        var a = new[] { new Vector3(3f, 0f, 4f), new Vector3(-7f, 0f, 2f) };
        var b = new[] { new Vector3(-7f, 0f, 2f), new Vector3(3f, 0f, 4f) };
        var bosses = new[] { new Vector3(1f, 0f, -6f) };
        var first = RejoinSpawnPicker.FarthestCorner(Corners, a, bosses);
        Assert.That(RejoinSpawnPicker.FarthestCorner(Corners, a, bosses), Is.EqualTo(first));
        Assert.That(RejoinSpawnPicker.FarthestCorner(Corners, b, bosses), Is.EqualTo(first));
    }

    [Test]
    public void 보스_룸_밖이면_모서리가_있어도_첫_스폰_공식()
    {
        var pick = RejoinSpawnPicker.Pick(BossRoomContext(false, null, null));
        Assert.That(pick.IsBossRoomCorner, Is.False);
        Assert.That(pick.Pose.position, Is.EqualTo(new Vector3(100f, 12f, -50f)));
    }

    [Test]
    public void 보스_룸이어도_모서리가_없으면_첫_스폰_공식()
    {
        var pick = RejoinSpawnPicker.Pick(BossRoomContext(true, null, null, new List<Pose>()));
        Assert.That(pick.IsBossRoomCorner, Is.False);
        Assert.That(pick.Pose.position, Is.EqualTo(new Vector3(100f, 12f, -50f)));

        var nullCorners = new RejoinSpawnContext(true, true, new Vector3(100f, 2f, -50f), Quaternion.identity,
            new Vector3(0f, 10f, 0f), null);
        Assert.That(RejoinSpawnPicker.Pick(nullCorners).IsBossRoomCorner, Is.False);
    }

    // ── 계산 폴백 ─────────────────────────────────────────

    [Test]
    public void 로컬_모서리는_안쪽_면에서_인셋만큼_들어온다()
    {
        var output = new Vector3[4];
        BossRoomRejoin.ComputeLocalCorners(-13f, 13f, -12f, 12f, 0.5f, 2f, output);
        Assert.That(output[0], Is.EqualTo(new Vector3(-11f, 0.5f, -10f)));
        Assert.That(output[1], Is.EqualTo(new Vector3(11f, 0.5f, -10f)));
        Assert.That(output[2], Is.EqualTo(new Vector3(11f, 0.5f, 10f)));
        Assert.That(output[3], Is.EqualTo(new Vector3(-11f, 0.5f, 10f)));
    }

    [Test]
    public void 인셋보다_좁은_축은_가운데로_모은다()
    {
        var output = new Vector3[4];
        BossRoomRejoin.ComputeLocalCorners(0f, 3f, -10f, 10f, 0f, 2f, output);
        Assert.That(output[0].x, Is.EqualTo(1.5f));
        Assert.That(output[1].x, Is.EqualTo(1.5f));
        Assert.That(output[0].z, Is.EqualTo(-8f));
        Assert.That(output[2].z, Is.EqualTo(8f));
    }

    // bossroom 과 같은 구성: 루트 원점·항등의 InvisibleBoundaries, 안쪽 면 ±13.37, 박스 바닥 y=0.5.
    Transform BuildArena(Vector3 position, float yaw)
    {
        var room = new GameObject("bossroom_test");
        _created.Add(room);
        room.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

        var boundaries = new GameObject(BossRoomRejoin.BoundariesName).transform;
        boundaries.SetParent(room.transform, false);

        const float inner = 13.37f, t = 1f, h = 8f, len = 28.74f, cy = 0.5f + h * 0.5f;
        AddWall(boundaries, "Boundary_XMin", new Vector3(-inner - t * 0.5f, cy, 0f), new Vector3(t, h, len));
        AddWall(boundaries, "Boundary_XMax", new Vector3(inner + t * 0.5f, cy, 0f), new Vector3(t, h, len));
        AddWall(boundaries, "Boundary_ZMin", new Vector3(0f, cy, -inner - t * 0.5f), new Vector3(len, h, t));
        AddWall(boundaries, "Boundary_ZMax", new Vector3(0f, cy, inner + t * 0.5f), new Vector3(len, h, t));
        return boundaries;
    }

    static void AddWall(Transform parent, string name, Vector3 center, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var box = go.AddComponent<BoxCollider>();
        box.center = center;
        box.size = size;
    }

    [Test]
    public void 경계를_방_로컬_축으로_읽는다()
    {
        var boundaries = BuildArena(new Vector3(500f, -0.5f, 0f), -90f);
        Assert.That(BossRoomRejoin.TryReadArenaLocal(boundaries, out var minX, out var maxX, out var minZ, out var maxZ, out var floorY), Is.True);
        Assert.That(minX, Is.EqualTo(-13.37f).Within(1e-3f));
        Assert.That(maxX, Is.EqualTo(13.37f).Within(1e-3f));
        Assert.That(minZ, Is.EqualTo(-13.37f).Within(1e-3f));
        Assert.That(maxZ, Is.EqualTo(13.37f).Within(1e-3f));
        Assert.That(floorY, Is.EqualTo(0.5f).Within(1e-3f));
    }

    [Test]
    public void 경계가_하나라도_없으면_폴백_실패()
    {
        var boundaries = BuildArena(Vector3.zero, 0f);
        Object.DestroyImmediate(boundaries.Find("Boundary_ZMax").gameObject);
        Assert.That(BossRoomRejoin.TryReadArenaLocal(boundaries, out _, out _, out _, out _, out _), Is.False);
        var output = new List<Pose>();
        Assert.That(BossRoomRejoin.TryComputeFallbackCorners(boundaries, output), Is.False);
        Assert.That(output, Is.Empty);
    }

    [Test]
    public void 회전된_룸의_폴백_모서리는_방을_따라_돈다()
    {
        var position = new Vector3(500f, -0.5f, 0f);
        var boundaries = BuildArena(position, -90f);
        var output = new List<Pose>();
        Assert.That(BossRoomRejoin.TryComputeFallbackCorners(boundaries, output), Is.True);
        Assert.That(output.Count, Is.EqualTo(4));

        var inset = 13.37f - BossRoomRejoin.Inset;
        var rotation = Quaternion.Euler(0f, -90f, 0f);
        var expectedLocal = new[]
        {
            new Vector3(-inset, 0.5f, -inset), new Vector3(inset, 0.5f, -inset),
            new Vector3(inset, 0.5f, inset), new Vector3(-inset, 0.5f, inset),
        };
        var center = position + new Vector3(0f, 0.5f, 0f);
        for (var i = 0; i < 4; i++)
        {
            var expected = position + rotation * expectedLocal[i];
            Assert.That(Vector3.Distance(output[i].position, expected), Is.LessThan(1e-3f), $"Corner{i + 1}");
            // 월드 y = 방 y(-0.5) + 바닥 0.5 = 0
            Assert.That(output[i].position.y, Is.EqualTo(0f).Within(1e-3f));
            // 방 중심을 본다.
            var toCenter = center - output[i].position;
            toCenter.y = 0f;
            Assert.That(Vector3.Angle(output[i].rotation * Vector3.forward, toCenter), Is.LessThan(0.1f), $"Corner{i + 1} 방향");
        }

        // -90° 방: 로컬 Corner1(-x,-z) → 월드 (+x,-z). 월드 축으로 계산했다면 (-x,-z) 가 나온다.
        Assert.That(output[0].position.x, Is.EqualTo(position.x + inset).Within(1e-3f));
        Assert.That(output[0].position.z, Is.EqualTo(position.z - inset).Within(1e-3f));
    }

    [Test]
    public void 회전되지_않은_룸도_같은_계산()
    {
        var boundaries = BuildArena(new Vector3(10f, 0f, -20f), 0f);
        var output = new List<Pose>();
        Assert.That(BossRoomRejoin.TryComputeFallbackCorners(boundaries, output), Is.True);
        var inset = 13.37f - BossRoomRejoin.Inset;
        Assert.That(Vector3.Distance(output[0].position, new Vector3(10f - inset, 0.5f, -20f - inset)), Is.LessThan(1e-3f));
        Assert.That(Vector3.Distance(output[2].position, new Vector3(10f + inset, 0.5f, -20f + inset)), Is.LessThan(1e-3f));
    }

    // ── InBossRoom 판정 표 ─────────────────────────────────

    [TestCase(BossEncounterPhase.Idle, false, false)]
    [TestCase(BossEncounterPhase.Idle, true, false)]
    [TestCase(BossEncounterPhase.AwaitingArrival, false, false)]
    [TestCase(BossEncounterPhase.AwaitingArrival, true, true)]
    [TestCase(BossEncounterPhase.Preparing, true, true)]
    [TestCase(BossEncounterPhase.Dialogue, true, true)]
    [TestCase(BossEncounterPhase.Combat, false, true)]
    [TestCase(BossEncounterPhase.Combat, true, true)]
    [TestCase(BossEncounterPhase.FailedSafe, false, false)]
    [TestCase(BossEncounterPhase.FailedSafe, true, true)]
    public void 보스_룸_판정(BossEncounterPhase phase, bool anyOtherInArea, bool expected)
    {
        Assert.That(BossRoomRejoin.IsInBossRoom(phase, anyOtherInArea), Is.EqualTo(expected));
    }

    [Test]
    public void BossArea_안_판정은_박스_로컬_수평만_본다()
    {
        var room = new GameObject("area_test");
        _created.Add(room);
        room.transform.SetPositionAndRotation(new Vector3(500f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f));
        var box = room.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 1f, 0f);
        box.size = new Vector3(20f, 2f, 4f); // 로컬 X 로 길다 → -90° 회전이면 월드 Z 로 길다

        Assert.That(BossRoomRejoin.IsInsideBoxXZ(box, new Vector3(500f, 0f, 9f)), Is.True, "월드 Z 9 = 로컬 X 쪽");
        Assert.That(BossRoomRejoin.IsInsideBoxXZ(box, new Vector3(509f, 0f, 0f)), Is.False, "월드 X 9 = 로컬 Z 쪽(반폭 2)");
        Assert.That(BossRoomRejoin.IsInsideBoxXZ(box, new Vector3(500f, 30f, 0f)), Is.True, "높이는 보지 않는다");
    }
}
