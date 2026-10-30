using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 153 3단계 — 가붕이 평타 판정 도형. 예전 프리팹 상자(<c>Armature/DefaultAttack/AAC1</c>, 4타 공용)를 옮긴 직사각형이
/// 바닥 평면에서 예전 상자와 같은 영역인지(모서리 안/밖·조준 방향 회전), SO 4타 값, 프리팹에 옛 상자가 없고 간파 상자는 남았는지.
/// </summary>
public sealed class PaladinStrikeAreaTests
{
    const string DataPath = "Assets/9.ScriptableObject/Player/Garen/PlayerDefaultAttackData.asset";
    const string PrefabPath = "Assets/2.Prefabs/Player/Paladin/Player_Paladin.prefab";

    // 예전 상자(Armature 기준 축): BoxCollider center (0, 1, 0.5)·size (2.6, 2, 2.1), DefaultAttack 로컬 y −0.03°.
    static readonly Vector3 OldCenter = new Vector3(0f, 1f, 0.5f);
    static readonly Vector3 OldSize = new Vector3(2.6f, 2f, 2.1f);
    const float OldYaw = -0.03f;
    const float Margin = 0.01f;

    static IEnumerable<Vector3> AimDirections()
    {
        yield return Vector3.forward;
        yield return Vector3.right;
        yield return new Vector3(-1f, 0f, 1f).normalized;
        yield return new Vector3(0.3f, 0f, -1f).normalized;
    }

    // 예전 상자 바닥 평면 모서리 근처 점 — 상자 축(조준 방향 + OldYaw)에서 inset 만큼 안(음수면 밖).
    static Vector3 OldBoxPoint(Vector3 caster, Vector3 aim, float sx, float sz, float inset, float height)
    {
        Quaternion axes = Quaternion.LookRotation(aim) * Quaternion.Euler(0f, OldYaw, 0f);
        float x = OldCenter.x + sx * (OldSize.x * 0.5f - inset);
        float z = OldCenter.z + sz * (OldSize.z * 0.5f - inset);
        return caster + axes * new Vector3(x, 0f, z) + Vector3.up * height;
    }

    [Test]
    public void StrikeRect_MatchesOldBoxCorners_ForEveryAim()
    {
        var area = new AttackShapeSet(AttackShapeOrigin.CasterAim, DefaultAttackStep.PaladinStrikeRect());
        var caster = new Vector3(2f, 0.3f, -1f);

        foreach (Vector3 aim in AimDirections())
        {
            AttackShapePose pose = PlayerDefaultAttack.ResolvePose(new DefaultAttackStep(), caster, aim);
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Assert.IsTrue(area.Contains(pose, OldBoxPoint(caster, aim, sx, sz, Margin, 1f)), $"모서리 안 aim={aim} ({sx},{sz})");
                Assert.IsFalse(area.Contains(pose, OldBoxPoint(caster, aim, sx, sz, -Margin, 1f)), $"모서리 밖 aim={aim} ({sx},{sz})");
            }

            // 변 가운데 밖(앞·뒤·좌·우)
            Assert.IsFalse(area.Contains(pose, OldBoxPoint(caster, aim, 0, 1, -Margin, 1f)), "앞 끝 밖");
            Assert.IsFalse(area.Contains(pose, OldBoxPoint(caster, aim, 0, -1, -Margin, 1f)), "뒤 끝 밖");
            Assert.IsTrue(area.Contains(pose, caster + Vector3.up * 0.5f), "발밑(뒤 0.55 안)");
        }
    }

    [Test]
    public void StrikeRect_Height_FeetToBoxTop()
    {
        var area = new AttackShapeSet(AttackShapeOrigin.CasterAim, DefaultAttackStep.PaladinStrikeRect());
        var caster = new Vector3(0f, 1f, 0f);
        AttackShapePose pose = PlayerDefaultAttack.ResolvePose(null, caster, Vector3.forward);
        Vector3 front = caster + Vector3.forward;

        Assert.IsTrue(area.Contains(pose, front), "발 높이");
        Assert.IsTrue(area.Contains(pose, front + Vector3.up * 2.03f), "상자 위 끝(Armature 0.039 + 2) 안");
        Assert.IsFalse(area.Contains(pose, front + Vector3.up * 2.05f), "상자 위 끝 밖");
        Assert.IsFalse(area.Contains(pose, front + Vector3.down * 0.05f), "발보다 아래(예전 상자도 못 닿음)");
    }

    [Test]
    public void StrikeRect_RealOverlap_CornerGraze_MatchesOldBox()
    {
        var area = new AttackShapeSet(AttackShapeOrigin.CasterAim, DefaultAttackStep.PaladinStrikeRect());
        var caster = new Vector3(7000f, 0f, 7000f); // 열린 씬 콜라이더와 떨어진 곳
        var created = new List<GameObject>();
        try
        {
            foreach (Vector3 aim in AimDirections())
            {
                AttackShapePose pose = PlayerDefaultAttack.ResolvePose(null, caster, aim);
                Quaternion axes = Quaternion.LookRotation(aim) * Quaternion.Euler(0f, OldYaw, 0f);
                // 예전 상자 = Armature 높이 0.039 위 center (0, 1, 0.5)·size (2.6, 2, 2.1).
                Vector3 oldCenter = caster + axes * new Vector3(OldCenter.x, 0f, OldCenter.z) + Vector3.up * (0.039f + OldCenter.y);
                Vector3 corner = caster + axes * new Vector3(OldSize.x * 0.5f, 0f, OldCenter.z + OldSize.z * 0.5f) + Vector3.up * 1f;
                Vector3 diagonal = axes * new Vector3(1f, 0f, 1f).normalized;
                Vector3 rightEdge = caster + axes * new Vector3(OldSize.x * 0.5f, 0f, 0.5f) + Vector3.up * 1f;

                // 모서리 바깥 대각선 구(반경 0.5) — 표면이 모서리를 0.05 m 넘음/0.05 m 못 미침, 45° 돌린 상자 꼭짓점이 옆 변을 0.05 m 파고듦/0.05 m 떨어짐.
                var bodies = new List<(GameObject go, bool expected, string label)>();
                GameObject Sphere(Vector3 at, float radius)
                {
                    var go = new GameObject("PaladinStrikeAreaTests_Body") { hideFlags = HideFlags.DontSave };
                    created.Add(go);
                    go.transform.position = at;
                    go.AddComponent<SphereCollider>().radius = radius;
                    return go;
                }
                GameObject Diamond(Vector3 at)
                {
                    var go = new GameObject("PaladinStrikeAreaTests_Body") { hideFlags = HideFlags.DontSave };
                    created.Add(go);
                    go.transform.SetPositionAndRotation(at, axes * Quaternion.Euler(0f, 45f, 0f));
                    go.AddComponent<BoxCollider>().size = Vector3.one * 0.8f;
                    return go;
                }
                float halfDiagonal = 0.8f * Mathf.Sqrt(2f) * 0.5f;
                Vector3 side = axes * Vector3.right;
                bodies.Add((Sphere(corner + diagonal * 0.45f, 0.5f), true, "모서리 구 걸침"));
                bodies.Add((Sphere(corner + diagonal * 0.55f, 0.5f), false, "모서리 구 떨어짐"));
                bodies.Add((Diamond(rightEdge + side * (halfDiagonal - 0.05f)), true, "돌린 상자 꼭짓점 걸침"));
                bodies.Add((Diamond(rightEdge + side * (halfDiagonal + 0.05f)), false, "돌린 상자 꼭짓점 떨어짐"));
                Physics.SyncTransforms();

                var oldBuffer = new Collider[32];
                int oldCount = Physics.OverlapBoxNonAlloc(oldCenter, OldSize * 0.5f, oldBuffer, axes, ~0, QueryTriggerInteraction.Collide);
                var oldHits = new HashSet<GameObject>();
                for (int i = 0; i < oldCount; i++)
                    oldHits.Add(oldBuffer[i].gameObject);

                var mine = new HashSet<GameObject>();
                foreach (var body in bodies)
                    mine.Add(body.go);
                var hits = new List<AttackShapeHit>();
                AttackShapeQuery.Collect(area, pose, ~0, QueryTriggerInteraction.Collide, new Collider[32],
                    c => mine.Contains(c.gameObject) ? c.gameObject : null, new HashSet<Object>(), hits);
                var newHits = new HashSet<Object>();
                foreach (AttackShapeHit hit in hits)
                    newHits.Add(hit.Target);

                foreach (var body in bodies)
                {
                    Assert.AreEqual(body.expected, oldHits.Contains(body.go), $"예전 상자 {body.label} aim={aim}");
                    Assert.AreEqual(body.expected, newHits.Contains(body.go), $"도형 {body.label} aim={aim}");
                }

                foreach (var body in bodies)
                    Object.DestroyImmediate(body.go);
                created.Clear();
            }
        }
        finally
        {
            foreach (GameObject go in created)
                if (go != null)
                    Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void DataAsset_FourSteps_HaveStrikeRect()
    {
        var data = AssetDatabase.LoadAssetAtPath<DefaultAttackData>(DataPath);
        Assert.IsNotNull(data, DataPath);
        Assert.AreEqual(4, data.AttackSteps.Length);
        AttackRectShape expected = DefaultAttackStep.PaladinStrikeRect();

        for (int i = 0; i < data.AttackSteps.Length; i++)
        {
            AttackShapeSet area = data.AttackSteps[i].HitArea;
            Assert.IsNotNull(area, $"{i + 1}타 hitArea");
            Assert.AreEqual(AttackShapeOrigin.CasterAim, area.Origin, $"{i + 1}타 기준점");
            Assert.AreEqual(1, area.Shapes.Count, $"{i + 1}타 도형 수");
            var rect = area.Shapes[0] as AttackRectShape;
            Assert.IsNotNull(rect, $"{i + 1}타 = 직사각형");
            Assert.AreEqual(expected.Length, rect.Length, 1e-4f, $"{i + 1}타 length");
            Assert.AreEqual(expected.Width, rect.Width, 1e-4f, $"{i + 1}타 width");
            Assert.AreEqual(expected.BackOffset, rect.BackOffset, 1e-4f, $"{i + 1}타 backOffset");
            Assert.AreEqual(expected.ForwardOffset, rect.ForwardOffset, 1e-4f, $"{i + 1}타 forwardOffset");
            Assert.AreEqual(expected.SideOffset, rect.SideOffset, 1e-4f, $"{i + 1}타 sideOffset");
            Assert.AreEqual(expected.YawOffset, rect.YawOffset, 1e-4f, $"{i + 1}타 yawOffset");
            Assert.AreEqual(expected.MinHeight, rect.MinHeight, 1e-4f, $"{i + 1}타 minHeight");
            Assert.AreEqual(expected.MaxHeight, rect.MaxHeight, 1e-4f, $"{i + 1}타 maxHeight");

            var errors = new List<string>();
            area.Validate(errors);
            CollectionAssert.IsEmpty(errors, $"{i + 1}타 검증");
        }
    }

    [Test]
    public void Prefab_NoOldStrikeBoxes_InterruptAnchorKept()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab, PrefabPath);

        Transform defaultAttack = prefab.transform.Find("Armature/DefaultAttack");
        if (defaultAttack != null)
            Assert.AreEqual(0, defaultAttack.childCount, "옛 평타 상자(AAC1~4) 없음");

        foreach (ColliderInfo info in prefab.GetComponentsInChildren<ColliderInfo>(true))
            StringAssert.DoesNotStartWith("AAC", info.name, "옛 평타 상자 남음");

        Transform interrupt = prefab.transform.Find("Armature/InterruptAttack");
        Assert.IsNotNull(interrupt, "간파 상자");
        Assert.IsNotNull(interrupt.GetComponent<BoxCollider>(), "간파 상자 BoxCollider");
        Assert.IsNotNull(interrupt.GetComponent<ColliderInfo>(), "간파 상자 ColliderInfo");
        // Q 앵커(Armature/MainSkill)는 5단계에서 도형으로 옮겨 지웠다 — PaladinSkillAreaTests.

        var controller = prefab.GetComponent<DefaultAttackController>();
        Assert.IsNotNull(controller, "DefaultAttackController");
    }
}
