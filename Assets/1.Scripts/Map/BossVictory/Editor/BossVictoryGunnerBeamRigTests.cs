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
    public void 뼈대_원통은_Z_0에서_1_반지름_0점5_바깥_법선()
    {
        Mesh m = BossVictoryGunnerBeamAuthoring.BuildCylinder(16);
        try
        {
            Bounds b = m.bounds;
            Assert.AreEqual(0f, b.min.z, Tolerance);
            Assert.AreEqual(1f, b.max.z, Tolerance);
            Assert.AreEqual(0.5f, b.max.x, Tolerance);
            Vector3[] v = m.vertices, n = m.normals;
            Vector2[] uv = m.uv;
            for (int i = 0; i < v.Length; i++)
            {
                Assert.Greater(Vector3.Dot(new Vector3(v[i].x, v[i].y, 0f), n[i]), 0f, "법선이 바깥");
                Assert.AreEqual(v[i].z, uv[i].x, Tolerance, "UV.x = 길이");
            }
            // 바깥에서 본 삼각형이 앞면(시계 방향) — 면 법선(왼손 기준 cross)이 바깥을 향한다.
            int[] t = m.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b1 = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 face = Vector3.Cross(b1 - a, c - a);
                Vector3 mid = (a + b1 + c) / 3f;
                Assert.Greater(Vector3.Dot(face, new Vector3(mid.x, mid.y, 0f)), 0f, $"삼각형 {i / 3} 이 바깥을 향함");
            }
        }
        finally { Object.DestroyImmediate(m); }
    }
}
