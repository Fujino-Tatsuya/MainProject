using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>할 일 300 — 처치 연출 거너 레이저 3D 원통(GunnerBeamMeshSwap).</summary>
public class BossVictoryGunnerBeamMeshTests
{
    const float Tolerance = 1e-4f;

    [Test]
    public void 원통_메시는_y_0에서_1_반지름_0점5()
    {
        Mesh mesh = GunnerBeamMeshSwap.BuildCylinder(16);
        try
        {
            Bounds b = mesh.bounds;
            Assert.AreEqual(0f, b.min.y, Tolerance);
            Assert.AreEqual(1f, b.max.y, Tolerance);
            Assert.AreEqual(0.5f, b.max.x, Tolerance);
            Assert.AreEqual(-0.5f, b.min.x, Tolerance);
            Assert.AreEqual(0.5f, b.max.z, 0.01f);
            Assert.AreEqual(16 * 6, mesh.triangles.Length);
        }
        finally { Object.DestroyImmediate(mesh); }
    }

    [Test]
    public void 원통_UV_U는_길이_V는_둘레_삼각파()
    {
        Mesh mesh = GunnerBeamMeshSwap.BuildCylinder(8);
        try
        {
            Vector3[] v = mesh.vertices;
            Vector2[] uv = mesh.uv;
            for (int i = 0; i < v.Length; i++)
                Assert.AreEqual(v[i].y, uv[i].x, Tolerance, "U = 길이(y)");
        }
        finally { Object.DestroyImmediate(mesh); }

        Assert.AreEqual(0f, GunnerBeamMeshSwap.SectionV(0f), Tolerance);
        Assert.AreEqual(1f, GunnerBeamMeshSwap.SectionV(0.5f), Tolerance);
        Assert.AreEqual(0f, GunnerBeamMeshSwap.SectionV(1f), Tolerance, "이음매 없음 — 한 바퀴 끝 = 시작");
        Assert.AreEqual(0.5f, GunnerBeamMeshSwap.SectionV(0.25f), Tolerance);
    }

    [Test]
    public void 원통_옆면은_바깥을_본다()
    {
        Mesh mesh = GunnerBeamMeshSwap.BuildCylinder(12);
        try
        {
            Vector3[] v = mesh.vertices;
            int[] t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 face = Vector3.Cross(b - a, c - a);   // Unity 시계 방향 = 앞면 → 이 법선이 바깥
                Vector3 center = (a + b + c) / 3f;
                Vector3 outward = new Vector3(center.x, 0f, center.z);
                Assert.Greater(Vector3.Dot(face, outward), 0f, $"삼각형 {i / 3}");
            }
        }
        finally { Object.DestroyImmediate(mesh); }
    }

    [Test]
    public void 몸통만_원통으로_바꾸고_되돌린다()
    {
        var go = new GameObject("beam");
        var bodyGo = new GameObject("LaserCore");
        bodyGo.transform.SetParent(go.transform);
        var sparkGo = new GameObject("Sparks");
        sparkGo.transform.SetParent(go.transform);
        try
        {
            ParticleSystem body = bodyGo.AddComponent<ParticleSystem>();
            var bm = body.main;
            bm.startSize3D = true;
            bm.startSizeXMultiplier = 0.5f;
            bm.startSizeYMultiplier = 10f;
            bm.startSizeZMultiplier = 1f;
            var br = bodyGo.GetComponent<ParticleSystemRenderer>();
            br.renderMode = ParticleSystemRenderMode.Stretch;

            ParticleSystem spark = sparkGo.AddComponent<ParticleSystem>();
            var sr = sparkGo.GetComponent<ParticleSystemRenderer>();
            sr.renderMode = ParticleSystemRenderMode.Stretch;   // size3D 아님 → 불꽃, 그대로

            var swap = new GunnerBeamMeshSwap();
            swap.Apply(new List<ParticleSystem> { body, spark });

            Assert.AreEqual(ParticleSystemRenderMode.Mesh, br.renderMode);
            Assert.AreEqual(ParticleSystemRenderSpace.Local, br.alignment);
            Assert.AreEqual(GunnerBeamMeshSwap.Cylinder, br.mesh);
            Assert.IsTrue(body.main.startRotation3D);
            Assert.AreEqual(90f * Mathf.Deg2Rad, body.main.startRotationXMultiplier, Tolerance);
            Assert.AreEqual(0.5f, body.main.startSizeZMultiplier, Tolerance, "단면 원 — Z = X");
            Assert.AreEqual(ParticleSystemRenderMode.Stretch, sr.renderMode, "불꽃(size3D 아님)은 그대로");

            var m = body.main;
            m.startSizeXMultiplier = 3f;
            swap.SyncRoundSection();
            Assert.AreEqual(3f, body.main.startSizeZMultiplier, Tolerance);

            swap.RequestRestore();
            swap.Tick();   // 입자 없음 → 바로 되돌림
            Assert.IsFalse(swap.Active);
            Assert.AreEqual(ParticleSystemRenderMode.Stretch, br.renderMode);
            Assert.IsFalse(body.main.startRotation3D);
            Assert.AreEqual(1f, body.main.startSizeZMultiplier, Tolerance);
        }
        finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void 메시_입자_회전_X_90은_메시_Y를_로컬_Z로()
    {
        Assert.AreEqual(Vector3.forward.z, (Quaternion.Euler(90f, 0f, 0f) * Vector3.up).z, Tolerance);
    }
}
