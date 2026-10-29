using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 스킬 범위 외곽선(할 일 58 후속) — 대상 고르기(합집합·중복·상한·가까운 순·사망 제외)와
/// 미리보기 영역 = 서버 판정 영역(같은 입력 → 같은 기하·같은 겹침 결과).
/// 서버 쪽 "기존 식"은 리팩터 전 코드(GunnerBeamAttack.OverlapBeamBox · AssassinCircleStrikeSkill.StrikeArea)를 그대로 옮겨 고정한다.
/// </summary>
public sealed class SkillRangeOutlineTests
{
    const string ChargeLaserDataPath = "Assets/9.ScriptableObject/Player/Gunner/GunnerChargeLaserData.asset";
    const string CircleStrikeDataPath = "Assets/9.ScriptableObject/Player/Assassin/AssassinCircleStrikeSkillData.asset";

    readonly List<Object> _cleanup = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null)
                Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
    }

    // ── 대상 고르기 ──

    sealed class Target
    {
        public readonly string Name;
        public Target(string name) => Name = name;
        public override string ToString() => Name;
    }

    static List<OutlineCandidate<Target>> Candidates(params (Target t, float d, bool ok)[] items)
    {
        var list = new List<OutlineCandidate<Target>>();
        foreach (var (t, d, ok) in items)
            list.Add(new OutlineCandidate<Target>(t, d, ok));
        return list;
    }

    [Test]
    public void 범위_세트는_가까운_순()
    {
        Target a = new Target("a"), b = new Target("b"), c = new Target("c");
        var result = new List<Target>();
        SkillRangeOutlineSelection.Select(Candidates((c, 9f, true), (a, 1f, true), (b, 4f, true)), null, 16, result);
        CollectionAssert.AreEqual(new[] { a, b, c }, result);
    }

    [Test]
    public void 같은_대상_여러_콜라이더는_한_번_가까운_거리로()
    {
        Target a = new Target("a"), b = new Target("b");
        var result = new List<Target>();
        // a 의 콜라이더 둘(9, 1) — 가까운 1 로 b(4) 보다 앞선다.
        SkillRangeOutlineSelection.Select(Candidates((a, 9f, true), (b, 4f, true), (a, 1f, true)), null, 16, result);
        CollectionAssert.AreEqual(new[] { a, b }, result);
    }

    [Test]
    public void 상한을_넘으면_가까운_것만()
    {
        var targets = new Target[20];
        var items = new (Target, float, bool)[20];
        for (int i = 0; i < 20; i++)
        {
            targets[i] = new Target("t" + i);
            items[i] = (targets[i], 20f - i, true); // 뒤로 갈수록 가깝다
        }

        var result = new List<Target>();
        SkillRangeOutlineSelection.Select(Candidates(items), null, SkillRangeOutlineSelection.DefaultMaxTargets, result);

        Assert.AreEqual(16, result.Count);
        Assert.AreSame(targets[19], result[0]);
        Assert.AreSame(targets[4], result[15]);
        CollectionAssert.DoesNotContain(result, targets[0]);
    }

    [Test]
    public void 사망_자기자신_null은_제외()
    {
        Target alive = new Target("alive"), dead = new Target("dead");
        var result = new List<Target>();
        SkillRangeOutlineSelection.Select(Candidates((dead, 1f, false), (null, 0f, true), (alive, 2f, true)), null, 16, result);
        CollectionAssert.AreEqual(new[] { alive }, result);
    }

    [Test]
    public void 호버_대상은_범위_세트에서_빠진다_호버_우선()
    {
        Target hovered = new Target("hovered"), other = new Target("other");
        var result = new List<Target>();
        SkillRangeOutlineSelection.Select(Candidates((hovered, 1f, true), (other, 2f, true)), hovered, 16, result);
        // 합집합 = 호버 {hovered} ∪ 범위 {other} — 같은 몬스터가 두 겹을 받지 않는다.
        CollectionAssert.AreEqual(new[] { other }, result);
    }

    [Test]
    public void 상한_0이면_비어있다()
    {
        var result = new List<Target> { new Target("stale") };
        SkillRangeOutlineSelection.Select(Candidates((new Target("a"), 1f, true)), null, 0, result);
        Assert.AreEqual(0, result.Count);
    }

    [Test]
    public void 고르기는_사망_규칙을_호버와_같이_쓴다()
    {
        // 컴포넌트는 Selectable 에 CursorUnitPick.IsSelectable 을 넣는다 — HP 0 은 범위 안이어도 제외.
        Assert.IsFalse(CursorUnitPick.IsSelectable(false, 0));
        Assert.IsTrue(CursorUnitPick.IsSelectable(false, 1));
    }

    // ── 거너 Q: 관통 직육면체 ──

    static readonly Vector3[] Directions =
    {
        Vector3.forward, Vector3.right, new Vector3(1f, 0f, 1f).normalized, new Vector3(-0.3f, 0f, -0.95f).normalized,
    };

    [Test]
    public void 직선_상자는_기존_서버식과_같다()
    {
        Vector3 origin = new Vector3(3f, 1f, -2f);
        foreach (Vector3 dir in Directions)
        {
            SkillHitVolume v = SkillHitVolume.Beam(origin, dir, 9.5f, 1.2f, 2f, 123);

            // 리팩터 전 GunnerBeamAttack.OverlapBeamBox 식
            Vector3 center = origin + dir * (9.5f * 0.5f);
            var half = new Vector3(1.2f * 0.5f, 2f * 0.5f, 9.5f * 0.5f);
            Assert.That(Vector3.Distance(center, v.Center), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(half, v.HalfExtents), Is.LessThan(1e-5f));
            Assert.That(Quaternion.Angle(Quaternion.LookRotation(dir), v.Rotation), Is.LessThan(1e-3f));
            Assert.AreEqual(123, v.Mask);
            Assert.IsFalse(v.IsCircle);
        }
    }

    [Test]
    public void 거너Q_미리보기_상자는_발사_인자와_같다()
    {
        var data = AssetDatabase.LoadAssetAtPath<GunnerChargeLaserData>(ChargeLaserDataPath);
        Assert.IsNotNull(data, ChargeLaserDataPath);

        Vector3 origin = new Vector3(0f, data.MuzzleHeight, 0f);
        Vector3 dir = Directions[2];
        float length = data.RangeAt(0.5f);

        SkillHitVolume preview = GunnerChargeLaserSkill.FireVolume(origin, dir, length, data);
        // 발사(Fire) → FirePiercing(FireArea(length, BeamWidth), FirePose(origin, dir)) → AttackShapeQuery.Collect 의 넓은 질의 상자(할 일 153)
        data.FireArea(length, data.BeamWidth).Shapes[0].GetBroadBox(data.FirePose(origin, dir),
            out Vector3 center, out Vector3 halfExtents, out Quaternion rotation);
        SkillHitVolume server = SkillHitVolume.Box(center, halfExtents, rotation, data.HittableLayers);

        AssertSameVolume(server, preview);
        // 예전 발사 상자(SkillHitVolume.Beam, 높이 2)와도 같은 상자(경계 허용 오차만큼만 크다).
        SkillHitVolume old = SkillHitVolume.Beam(origin, dir, length, data.BeamWidth, 2f, data.HittableLayers);
        Assert.That(Vector3.Distance(old.Center, preview.Center), Is.LessThan(1e-4f));
        Assert.That(Vector3.Distance(old.HalfExtents, preview.HalfExtents), Is.LessThan(1e-3f));
    }

    // ── 어쌔신 변신 E: 원기둥 ──

    [Test]
    public void 원기둥은_기존_서버식과_같다()
    {
        var data = AssetDatabase.LoadAssetAtPath<AssassinCircleStrikeSkillData>(CircleStrikeDataPath);
        Assert.IsNotNull(data, CircleStrikeDataPath);

        Vector3 areaCenter = new Vector3(1.5f, 0.2f, 4f);
        SkillHitVolume v = AssassinCircleStrikeSkill.HitVolume(areaCenter, data);

        // 리팩터 전 AssassinCircleStrikeSkill.StrikeArea 식
        float radius = data.AreaRadius;
        Vector3 boxCenter = areaCenter + Vector3.up * (data.AreaHeight * 0.5f);
        var half = new Vector3(radius, data.AreaHeight * 0.5f, radius);
        Assert.That(Vector3.Distance(boxCenter, v.Center), Is.LessThan(1e-5f));
        Assert.That(Vector3.Distance(half, v.HalfExtents), Is.LessThan(1e-5f));
        Assert.AreEqual(Quaternion.identity, v.Rotation);
        Assert.AreEqual(data.HittableLayers.value, v.Mask);

        var samples = new[]
        {
            new Bounds(areaCenter, Vector3.one * 0.5f),                                   // 안
            new Bounds(areaCenter + new Vector3(radius * 0.95f, 0f, radius * 0.95f), Vector3.one * 0.2f), // 상자 모서리(원 밖)
            new Bounds(areaCenter + new Vector3(radius + 0.4f, 0f, 0f), Vector3.one * 1f), // 가장자리 걸침
        };
        foreach (Bounds b in samples)
            Assert.AreEqual(AssassinCircleStrikeRules.OverlapsCircle(areaCenter, radius, b), v.Accepts(b), b.ToString());
    }

    [Test]
    public void 어쌔신E_미리보기_원은_서버_확정_원과_같다()
    {
        var data = AssetDatabase.LoadAssetAtPath<AssassinCircleStrikeSkillData>(CircleStrikeDataPath);
        Assert.IsNotNull(data, CircleStrikeDataPath);

        Vector3 caster = new Vector3(2f, 0.5f, -1f);
        Vector3 cursor = new Vector3(6f, 0f, 3f);

        // 오너: 고정 거리 투영 → 그 지점의 판정 영역
        Vector3 aimPoint = PlayerGroundPointProjection.ProjectFixedDistance(caster, cursor, data.CastRange, Vector3.forward);
        SkillHitVolume preview = AssassinCircleStrikeSkill.HitVolume(AssassinCircleStrikeSkill.AreaCenter(caster, aimPoint), data);

        // 서버: 컨트롤러 재투영(같은 위치) → OnServerStart 의 areaCenter(리팩터 전: AimPoint, y = 시전자 발) → StrikeArea
        Vector3 submitted = PlayerGroundPointProjection.ReprojectServerFixedDistance(caster, aimPoint, data.CastRange, Vector3.forward);
        Vector3 serverCenter = submitted;
        serverCenter.y = caster.y;
        SkillHitVolume server = AssassinCircleStrikeSkill.HitVolume(serverCenter, data);

        AssertSameVolume(server, preview);
        Assert.That(Vector3.Distance(serverCenter, preview.CircleCenter), Is.LessThan(1e-5f));
    }

    // ── 같은 입력 → 같은 겹침 결과(실제 물리 질의) ──

    [Test]
    public void 직선_겹침_결과가_기존_서버_질의와_같다()
    {
        int layer = LayerMask.NameToLayer("Enemy");
        int mask = 1 << layer;
        Vector3 origin = new Vector3(0f, 1f, 0f);
        Vector3 dir = Directions[2];
        float length = 8f, width = 1.2f, height = 2f;

        var inside = NewBox("inside", origin + dir * 4f, layer);
        var edge = NewBox("edge", origin + dir * 7.9f + Vector3.Cross(Vector3.up, dir) * 0.7f, layer);
        var past = NewBox("past", origin + dir * 9.2f, layer);
        var side = NewBox("side", origin + dir * 3f + Vector3.Cross(Vector3.up, dir) * 2f, layer);
        Physics.SyncTransforms();

        var oldHits = new Collider[16];
        Vector3 center = origin + dir * (length * 0.5f);
        var half = new Vector3(width * 0.5f, height * 0.5f, length * 0.5f);
        int oldCount = Physics.OverlapBoxNonAlloc(center, half, oldHits, Quaternion.LookRotation(dir), mask, QueryTriggerInteraction.Collide);

        var newHits = new Collider[16];
        SkillHitVolume v = SkillHitVolume.Beam(origin, dir, length, width, height, mask);
        int newCount = v.Overlap(newHits, v.Mask, QueryTriggerInteraction.Collide);

        CollectionAssert.AreEquivalent(Slice(oldHits, oldCount), Filter(v, newHits, newCount));
        CollectionAssert.Contains(Slice(oldHits, oldCount), inside);
        CollectionAssert.Contains(Slice(oldHits, oldCount), edge);
        CollectionAssert.DoesNotContain(Slice(oldHits, oldCount), past);
        CollectionAssert.DoesNotContain(Slice(oldHits, oldCount), side);
    }

    [Test]
    public void 원기둥_겹침_결과가_기존_서버_질의와_같다()
    {
        int layer = LayerMask.NameToLayer("Enemy");
        int mask = 1 << layer;
        Vector3 areaCenter = new Vector3(10f, 0f, 10f);
        float radius = 2f, height = 3f;

        var inside = NewBox("inside", areaCenter + new Vector3(0.5f, 1f, 0f), layer);
        var corner = NewBox("corner", areaCenter + new Vector3(1.9f, 1f, 1.9f), layer, 0.1f); // 상자 안·원 밖
        var rim = NewBox("rim", areaCenter + new Vector3(2.3f, 1f, 0f), layer, 1f);          // 가장자리 걸침
        var above = NewBox("above", areaCenter + new Vector3(0f, 4f, 0f), layer, 0.5f);      // 높이 밖
        Physics.SyncTransforms();

        // 리팩터 전 StrikeArea: 상자 겹침 → OverlapsCircle(areaCenter, radius, bounds)
        var oldHits = new Collider[16];
        int oldCount = Physics.OverlapBoxNonAlloc(areaCenter + Vector3.up * (height * 0.5f),
            new Vector3(radius, height * 0.5f, radius), oldHits, Quaternion.identity, mask, QueryTriggerInteraction.Collide);
        var oldAccepted = new List<Collider>();
        for (int i = 0; i < oldCount; i++)
            if (AssassinCircleStrikeRules.OverlapsCircle(areaCenter, radius, oldHits[i].bounds))
                oldAccepted.Add(oldHits[i]);

        var newHits = new Collider[16];
        SkillHitVolume v = SkillHitVolume.CircleColumn(areaCenter, radius, height, mask);
        int newCount = v.Overlap(newHits, v.Mask, QueryTriggerInteraction.Collide);

        CollectionAssert.AreEquivalent(oldAccepted, Filter(v, newHits, newCount));
        CollectionAssert.Contains(oldAccepted, inside);
        CollectionAssert.Contains(oldAccepted, rim);
        CollectionAssert.DoesNotContain(oldAccepted, corner);
        CollectionAssert.DoesNotContain(oldAccepted, above);
    }

    // ── 도우미 ──

    BoxCollider NewBox(string name, Vector3 position, int layer, float size = 0.5f)
    {
        var go = new GameObject("SkillRangeOutlineTest_" + name) { hideFlags = HideFlags.DontSave };
        _cleanup.Add(go);
        go.layer = layer;
        go.transform.position = position;
        var box = go.AddComponent<BoxCollider>();
        box.size = Vector3.one * size;
        return box;
    }

    static List<Collider> Slice(Collider[] hits, int count)
    {
        var list = new List<Collider>();
        for (int i = 0; i < count; i++)
            list.Add(hits[i]);
        return list;
    }

    static List<Collider> Filter(SkillHitVolume v, Collider[] hits, int count)
    {
        var list = new List<Collider>();
        for (int i = 0; i < count; i++)
            if (v.Accepts(hits[i].bounds))
                list.Add(hits[i]);
        return list;
    }

    static void AssertSameVolume(SkillHitVolume expected, SkillHitVolume actual)
    {
        Assert.That(Vector3.Distance(expected.Center, actual.Center), Is.LessThan(1e-5f), "center");
        Assert.That(Vector3.Distance(expected.HalfExtents, actual.HalfExtents), Is.LessThan(1e-5f), "halfExtents");
        Assert.That(Quaternion.Angle(expected.Rotation, actual.Rotation), Is.LessThan(1e-3f), "rotation");
        Assert.AreEqual(expected.IsCircle, actual.IsCircle, "isCircle");
        Assert.AreEqual(expected.Radius, actual.Radius, 1e-5f, "radius");
        Assert.AreEqual(expected.Mask, actual.Mask, "mask");
    }
}
