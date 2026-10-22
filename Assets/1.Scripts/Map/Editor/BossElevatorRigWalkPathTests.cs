using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 엘리베이터 보행 경로(<see cref="BossElevatorRig.GetWalkPath"/>·<see cref="BossElevatorRig.MaxWalkDistance"/>, 할 일 64) —
/// 슬롯별 경유 지점 → 칸 안 지점, 빈 목록 = 기존 직선, 시간표 보행 거리 = 가장 긴 경로 총길이.
/// </summary>
public sealed class BossElevatorRigWalkPathTests
{
    const string ZonePath = "Assets/2.Prefabs/Environment/Layouts/Zones/ZoneS_typeBossEnter.prefab";
    const float Eps = 1e-4f;

    GameObject _root;

    [TearDown]
    public void TearDown()
    {
        if (_root != null) Object.DestroyImmediate(_root);
    }

    Transform Point(string name, Vector3 position)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(_root.transform, false);
        t.position = position;
        return t;
    }

    // 착지 중앙 (0,0,0), 문 앞 P1 (0,0,-4)·P2 (2,0,-4), 간격 배율 0.5 → 칸 안 P1 (0,0,0)·P2 (1,0,0).
    BossElevatorRig MakeRig(params Transform[][] waypoints)
    {
        _root = new GameObject("Rig");
        BossElevatorRig rig = _root.AddComponent<BossElevatorRig>();
        var so = new SerializedObject(rig);
        SerializedProperty front = so.FindProperty("_frontPoints");
        front.arraySize = 2;
        front.GetArrayElementAtIndex(0).objectReferenceValue = Point("P1", new Vector3(0f, 0f, -4f));
        front.GetArrayElementAtIndex(1).objectReferenceValue = Point("P2", new Vector3(2f, 0f, -4f));
        so.FindProperty("_insideSpread").floatValue = 0.5f;

        SerializedProperty paths = so.FindProperty("_walkWaypoints");
        paths.arraySize = waypoints.Length;
        for (int slot = 0; slot < waypoints.Length; slot++)
        {
            SerializedProperty points = paths.GetArrayElementAtIndex(slot).FindPropertyRelative("points");
            points.arraySize = waypoints[slot].Length;
            for (int i = 0; i < waypoints[slot].Length; i++)
                points.GetArrayElementAtIndex(i).objectReferenceValue = waypoints[slot][i];
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        return rig;
    }

    [Test]
    public void 경유_지점이_없으면_칸_안_지점_하나_직선()
    {
        BossElevatorRig rig = MakeRig();
        Assert.That(rig.GetWalkPath(0), Is.EqualTo(new[] { Vector3.zero }));
        Assert.That(rig.GetWalkPath(1), Is.EqualTo(new[] { new Vector3(1f, 0f, 0f) }));
        Assert.AreEqual(Mathf.Sqrt(17f), rig.MaxWalkDistance(), Eps, "P2 직선 (2,-4)→(1,0) 이 가장 길다");
    }

    [Test]
    public void 경유_지점을_차례로_거치고_시간표_거리는_가장_긴_경로_총길이()
    {
        var holder = new GameObject("Holder");
        try
        {
            Transform a = new GameObject("A").transform, b = new GameObject("B").transform;
            a.SetParent(holder.transform); b.SetParent(holder.transform);
            a.position = new Vector3(-3f, 0f, -4f);
            b.position = new Vector3(-3f, 0.5f, 0f);
            BossElevatorRig rig = MakeRig(new[] { a, null, b });

            Assert.That(rig.GetWalkPath(0), Is.EqualTo(new[] { a.position, b.position, Vector3.zero }), "빈 칸(null)은 건너뛰고 칸 안 지점이 마지막");
            Assert.That(rig.GetWalkPath(1), Is.EqualTo(new[] { new Vector3(1f, 0f, 0f) }), "목록이 없는 슬롯은 직선");
            Assert.That(rig.GetWalkPath(2), Is.EqualTo(rig.GetWalkPath(0)), "사람이 지점보다 많으면 문 앞 지점처럼 돌려 쓴다");
            Assert.AreEqual(3f + 4f + 3f, rig.MaxWalkDistance(), Eps, "P1 (0,-4)→(-3,-4)→(-3,0)→(0,0), 높이 차 제외");
        }
        finally
        {
            Object.DestroyImmediate(holder);
        }
    }

    [Test]
    public void 보스_진입_존은_경유_지점이_없으면_기존_직선_거리()
    {
        GameObject zone = AssetDatabase.LoadAssetAtPath<GameObject>(ZonePath);
        Assert.IsNotNull(zone, "보스 진입 존 프리팹 경로 확인");
        BossElevatorRig rig = zone.GetComponentInChildren<BossElevatorRig>(true);
        Assert.IsNotNull(rig);

        float straight = 0f;
        for (int slot = 0; slot < rig.FrontPoints.Count; slot++)
        {
            Assert.IsTrue(rig.GetFrontPose(slot, out Vector3 front, out _), $"P{slot + 1} 문 앞 지점");
            var path = rig.GetWalkPath(slot);
            Assert.AreEqual(rig.GetInsidePoint(slot), path[path.Count - 1], $"P{slot + 1} 경로 끝 = 칸 안 지점");
            if (path.Count == 1) straight = Mathf.Max(straight, Vector3.ProjectOnPlane(path[0] - front, Vector3.up).magnitude);
        }
        if (straight > 0f && rig.FrontPoints.Count > 0)
            Assert.GreaterOrEqual(rig.MaxWalkDistance(), straight - Eps, "보행 시간 기준 = 가장 긴 경로");
    }
}
