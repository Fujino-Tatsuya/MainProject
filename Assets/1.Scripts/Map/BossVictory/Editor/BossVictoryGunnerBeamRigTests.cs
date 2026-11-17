using NUnit.Framework;
using UnityEngine;

/// <summary>할 일 302 — 처치 연출 전용 레이저 이펙트(BossVictoryBeamRig) 순수 함수·뼈대 메시.</summary>
public class BossVictoryGunnerBeamRigTests
{
    const float Tolerance = 1e-4f;

    [Test]
    public void 몸통은_Z만_길이만큼_굵기는_그대로()
    {
        Vector3 s = BossVictoryBeamRig.BodyScale(new Vector3(0.5f, 0.7f, 1f), 12f);
        Assert.AreEqual(0.5f, s.x, Tolerance);
        Assert.AreEqual(0.7f, s.y, Tolerance);
        Assert.AreEqual(12f, s.z, Tolerance);
        Assert.AreEqual(0f, BossVictoryBeamRig.BodyScale(Vector3.one, -3f).z, Tolerance, "음수 길이 = 0");
    }

    [Test]
    public void 굵기_배율은_몸통_XY에만_곱하고_길이는_그대로()
    {
        Vector3 s = BossVictoryBeamRig.BodyScale(new Vector3(0.5f, 0.7f, 1f), 12f, 3f);
        Assert.AreEqual(1.5f, s.x, Tolerance);
        Assert.AreEqual(2.1f, s.y, Tolerance);
        Assert.AreEqual(12f, s.z, Tolerance, "길이는 굵기 배율과 무관");
        Assert.AreEqual(0f, BossVictoryBeamRig.BodyScale(Vector3.one, 5f, -1f).x, Tolerance, "음수 배율 = 0");
    }

    [Test]
    public void 층_굵기는_XY만_곱하고_Z는_저작_그대로()
    {
        Vector3 s = BossVictoryBeamRig.LayerScale(new Vector3(1.7f, 1.7f, 1f), 0.5f);
        Assert.AreEqual(0.85f, s.x, Tolerance);
        Assert.AreEqual(0.85f, s.y, Tolerance);
        Assert.AreEqual(1f, s.z, Tolerance, "길이 방향은 부모 Body 가 늘인다");
        Assert.AreEqual(0f, BossVictoryBeamRig.LayerScale(Vector3.one, -2f).x, Tolerance, "음수 배율 = 0");
    }

    [Test]
    public void 리그_층별_굵기는_안쪽과_바깥을_따로_저작_기준으로()
    {
        var root = new GameObject("rig");
        try
        {
            var body = new GameObject("Body").transform; body.SetParent(root.transform, false);
            var glow = new GameObject("Glow").transform; glow.SetParent(body, false); glow.localScale = new Vector3(1.1f, 1.1f, 1f);
            var wisp = new GameObject("Wisp").transform; wisp.SetParent(body, false); wisp.localScale = new Vector3(1.7f, 1.7f, 1f);
            root.SetActive(false);
            var rig = root.AddComponent<BossVictoryBeamRig>();
            var so = new UnityEditor.SerializedObject(rig);
            so.FindProperty("body").objectReferenceValue = body;
            var inner = so.FindProperty("innerBody"); inner.arraySize = 1; inner.GetArrayElementAtIndex(0).objectReferenceValue = glow;
            var air = so.FindProperty("outerAir"); air.arraySize = 1; air.GetArrayElementAtIndex(0).objectReferenceValue = wisp;
            so.ApplyModifiedPropertiesWithoutUndo();
            rig.SetLayerScales(0.5f, 2f);
            rig.Drive(Vector3.zero, new Vector3(0f, 0f, 10f), 1f, 1f, false, 1f);
            Assert.AreEqual(0.55f, glow.localScale.x, Tolerance, "안쪽 몸통 = 저작 × 0.5");
            Assert.AreEqual(3.4f, wisp.localScale.x, Tolerance, "바깥 공기막 = 저작 × 2");
            rig.SetLayerScales(1f, 1f);
            rig.Drive(Vector3.zero, new Vector3(0f, 0f, 10f), 1f, 1f, false, 1f);
            Assert.AreEqual(1.1f, glow.localScale.x, Tolerance, "되돌리면 저작 크기(누적 아님)");
            Assert.AreEqual(1.7f, wisp.localScale.x, Tolerance);
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void 층별_굵기_기본값은_1()
    {
        var q = new BossVictoryGunnerQ();
        Assert.AreEqual(1f, q.victoryBeamInnerScale, Tolerance);
        Assert.AreEqual(1f, q.victoryBeamAirScale, Tolerance);
    }

    [Test]
    public void 리그_Drive_는_굵기_배율을_몸통과_총구_끝_플레어에_곱한다()
    {
        var root = new GameObject("rig");
        try
        {
            var body = new GameObject("Body").transform; body.SetParent(root.transform, false); body.localScale = new Vector3(0.4f, 0.4f, 1f);
            var muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(root.transform, false); muzzle.localScale = Vector3.one * 0.8f;
            var end = new GameObject("End").transform; end.SetParent(root.transform, false);
            root.SetActive(false);
            var rig = root.AddComponent<BossVictoryBeamRig>();
            var so = new UnityEditor.SerializedObject(rig);
            so.FindProperty("body").objectReferenceValue = body;
            so.FindProperty("muzzle").objectReferenceValue = muzzle;
            so.FindProperty("end").objectReferenceValue = end;
            so.ApplyModifiedPropertiesWithoutUndo();
            rig.Drive(Vector3.zero, new Vector3(0f, 0f, 10f), 1f, 1f, false, 2f);
            Assert.AreEqual(0.8f, body.localScale.x, Tolerance);
            Assert.AreEqual(10f, body.localScale.z, Tolerance);
            Assert.AreEqual(1.6f, muzzle.localScale.x, Tolerance);
            Assert.AreEqual(2f, end.localScale.x, Tolerance);
            rig.Drive(Vector3.zero, new Vector3(0f, 0f, 10f), 1f, 1f, false, 1f);
            Assert.AreEqual(0.4f, body.localScale.x, Tolerance, "배율을 되돌리면 저작 굵기(누적 아님)");
            Assert.AreEqual(0.8f, muzzle.localScale.x, Tolerance);
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void 색은_밝기만_곱하고_페이드는_알파에만()
    {
        Color c = BossVictoryBeamRig.Look(new Color(2f, 1f, 0.5f, 0.8f), 3f, 0.5f);
        Assert.AreEqual(6f, c.r, Tolerance);
        Assert.AreEqual(3f, c.g, Tolerance);
        Assert.AreEqual(1.5f, c.b, Tolerance);
        Assert.AreEqual(0.4f, c.a, Tolerance);
        Color off = BossVictoryBeamRig.Look(Color.white, 1f, 0f);
        Assert.AreEqual(0f, off.a, Tolerance, "페이드 0 = 알파 0(뼈대 셰이더 rgb × 알파 → 완전히 사라짐)");
        Assert.AreEqual(1f, off.r, Tolerance, "rgb 에는 페이드를 안 곱한다(페이드² 방지)");
    }

    [Test]
    public void 기본값이면_새_레이저_없음()
    {
        var q = new BossVictoryGunnerQ();
        Assert.IsNull(q.victoryBeamPrefab);
        Assert.IsFalse(q.victoryBeamFreeze);
    }

    [Test]
    public void 뼈대_캡슐은_옆면_Z_0에서_1_반구는_UV2로_밀고_법선은_바깥()
    {
        Mesh m = BossVictoryGunnerBeamAuthoring.BuildCapsule(16, 6);
        try
        {
            Vector3[] v = m.vertices, n = m.normals;
            Vector2[] uv = m.uv, uv2 = m.uv2;
            for (int i = 0; i < v.Length; i++)
            {
                Assert.IsTrue(Mathf.Approximately(v[i].z, 0f) || Mathf.Approximately(v[i].z, 1f), "정점은 z 0·1 에만(반구는 셰이더가 민다)");
                Assert.AreEqual(v[i].z, uv[i].x, Tolerance, "UV.x = 길이");
                float e = uv2[i].x;
                Assert.AreEqual(v[i].z > 0.5f ? 1f : -1f, e == 0f ? (v[i].z > 0.5f ? 1f : -1f) : Mathf.Sign(e), "총구 쪽 −, 끝 쪽 +");
                // 실제 캡슐 위치(반지름 0.5 만큼 민 점) 기준 법선이 바깥 — 단위 길이.
                Vector3 p = v[i] + new Vector3(0f, 0f, e * 0.5f);
                Vector3 axis = new Vector3(0f, 0f, Mathf.Clamp(p.z, 0f, 1f));
                Assert.AreEqual(1f, n[i].magnitude, 1e-3f);
                Assert.Greater(Vector3.Dot(p - axis, n[i]) + 1e-4f, 0f, "법선이 바깥");
                Assert.AreEqual(0.5f, (p - axis).magnitude, 1e-3f, "캡슐 표면(반지름 0.5)");
            }
            // 삼각형이 바깥을 향함(시계 방향 = 앞면) — 밀어 둔 실제 위치로 판정.
            int[] t = m.triangles;
            Vector3 P(int k) => v[k] + new Vector3(0f, 0f, uv2[k].x * 0.5f);
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = P(t[i]), b = P(t[i + 1]), c = P(t[i + 2]);
                Vector3 face = Vector3.Cross(b - a, c - a);
                if (face.sqrMagnitude < 1e-10f) continue;   // 극의 퇴화 삼각형
                Vector3 mid = (a + b + c) / 3f;
                Vector3 axis = new Vector3(0f, 0f, Mathf.Clamp(mid.z, 0f, 1f));
                Assert.Greater(Vector3.Dot(face, mid - axis), 0f, $"삼각형 {i / 3} 이 바깥을 향함");
            }
        }
        finally { Object.DestroyImmediate(m); }
    }

    [Test]
    public void 번개_메시는_줄기마다_축_위_점_좌우_UV2는_줄기_번호_앞면은_우가_옆()
    {
        Mesh m = BossVictoryGunnerBeamAuthoring.BuildBolts(4, 65);
        try
        {
            Vector3[] v = m.vertices;
            Vector2[] uv = m.uv, uv2 = m.uv2;
            Assert.AreEqual(4 * 65 * 2, v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                Assert.AreEqual(0f, v[i].x, Tolerance, "정점은 축 위(셰이더가 옮긴다)");
                Assert.AreEqual(v[i].z, uv[i].x, Tolerance, "UV.x = 길이");
                Assert.IsTrue(uv[i].y == 0f || uv[i].y == 1f, "UV.y = 좌 0 · 우 1");
                Assert.AreEqual(i / (65 * 2), (int)uv2[i].x, "UV2.x = 줄기 번호");
            }
            // 셰이더가 우(UV.y 1)를 옆 = cross(진행, 카메라 방향)으로 펴면 면 법선 cross(옆, 진행)이 카메라 쪽 — 옆 = +X, 진행 = +Z 로 놓으면 −Y.
            Vector3 P(int k) => new Vector3(uv[k].y - 0.5f, 0f, v[k].z);
            int[] t = m.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Assert.AreEqual((int)uv2[t[i]].x, (int)uv2[t[i + 2]].x, "삼각형이 줄기를 건너지 않음");
                Vector3 face = Vector3.Cross(P(t[i + 1]) - P(t[i]), P(t[i + 2]) - P(t[i]));
                Assert.Less(face.y, 0f, $"삼각형 {i / 3} 감는 방향");
            }
        }
        finally { Object.DestroyImmediate(m); }
    }

    [Test]
    public void 새_레이저_페이드_0이면_기본_0_15초_기존_레이저와_칸값은_그대로()
    {
        var q = new BossVictoryGunnerQ();
        Assert.AreEqual(0f, BossVictoryGunnerCharge.EffectiveBeamFadeSeconds(q), Tolerance, "기존 레이저 = 0 그대로(원래 소멸 꼬리)");
        var prefab = new GameObject("beam");
        try
        {
            q.victoryBeamPrefab = prefab;
            Assert.AreEqual(BossVictoryGunnerCharge.VictoryBeamDefaultFadeSeconds, BossVictoryGunnerCharge.EffectiveBeamFadeSeconds(q), Tolerance);
            q.beamFadeSeconds = 0.4f;
            Assert.AreEqual(0.4f, BossVictoryGunnerCharge.EffectiveBeamFadeSeconds(q), Tolerance, "칸 값이 있으면 그 값");
            q.beamFadeSeconds = 0f;
            var times = new BossVictoryGunnerCharge.Times { Fire = 1f };
            q.beamHoldSeconds = 0.5f;
            Assert.IsTrue(BossVictoryGunnerCharge.Evaluate(times, q, 1.55f).LaserOn, "유지 끝 뒤 기본 페이드 동안 켜져 있다");
            Assert.IsFalse(BossVictoryGunnerCharge.Evaluate(times, q, 1.7f).LaserOn);
        }
        finally { Object.DestroyImmediate(prefab); }
    }

    [Test]
    public void 리그_색상은_hue만_바꾸고_흰_심과_밝기는_그대로()
    {
        var root = new GameObject("rig");
        var shader = Shader.Find("BeaverLobby/VFX/BossVictoryGunnerBeam");
        Assume.That(shader, Is.Not.Null);
        var mat = new Material(shader);
        try
        {
            mat.SetColor("_Color", Color.white);
            mat.SetColor("_GlowColor", new Color(1f, 2.5f, 6f, 1f));
            mat.SetColor("_CoreColor", new Color(8f, 8f, 8f, 1f));
            var body = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            var r = body.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            root.SetActive(false);
            var rig = root.AddComponent<BossVictoryBeamRig>();
            rig.SetTint(EffectTint.FromColor(Color.red));
            rig.Drive(Vector3.zero, Vector3.forward * 5f, 1f, 1f, false);
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            Color glow = block.GetColor("_GlowColor");
            Color.RGBToHSV(glow, out float h, out _, out float v);
            Assert.AreEqual(0f, Mathf.Min(h, 1f - h), 1e-3f, "파랑 둘레 → 빨강");
            Assert.AreEqual(6f, v, 1e-3f, "밝기(HDR) 그대로");
            Color core = block.GetColor("_CoreColor");
            Assert.AreEqual(8f, core.r, 1e-3f, "흰 심(채도 0)은 흰 채로");
            Assert.AreEqual(8f, core.b, 1e-3f);
            rig.SetTint(EffectTint.None);
            rig.Drive(Vector3.zero, Vector3.forward * 5f, 1f, 1f, false);
            r.GetPropertyBlock(block);
            Assert.AreEqual(6f, block.GetColor("_GlowColor").b, 1e-3f, "색상 없음 = 저작 색");
        }
        finally { Object.DestroyImmediate(root); Object.DestroyImmediate(mat); }
    }
}
