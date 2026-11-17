using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 거너 레이저 3D 원통(할 일 300) — 레이저 몸통(Stretched Billboard, size3D) 파티클을 처치 연출 동안만
/// Mesh 렌더(원통)로 바꿔, 낮은 앵글·레이저 축을 따라 보는 컷에서도 종이처럼 얇아 보이지 않게 한다.
/// 프리팹(FX_GunnerQBeam, SVN 아트)은 그대로 — 풀 인스턴스의 렌더러·메인 모듈 값을 바꾸고, 입자가 다 사라진 뒤(또는 정리 때) 되돌린다.
///
/// 맞춤 규칙(Stretched 와 같은 자리·크기):
///  · Stretched 몸통은 속도가 −Z(로컬, 정렬 축 고정용 −0.01)라 꼬리가 +Z 로 뻗는다 — 소켓 +Z = 레이저 끝(AimAndStretch).
///    → 원통 메시는 y∈[0,1](길이 = startSizeY, 규약 "값 1 = 1m")·반지름 0.5(지름 = startSizeX), 입자 회전 X +90° 로 메시 +Y → 로컬 +Z.
///  · 정렬 = Local(시스템 로컬 — 소켓을 따라감). 원통 단면이 원이 되게 Z 크기 = X 크기.
///  · UV: U = 길이(0 = 총구, 1 = 끝 — 레이저 텍스처가 가로로 긴 Trail 이라 U 가 길이 축), V = 둘레 0→1→0(이음매 없이 양쪽 반이 폭 그라데이션).
/// </summary>
public sealed class GunnerBeamMeshSwap
{
    struct Home
    {
        public ParticleSystemRenderer renderer;
        public ParticleSystemRenderMode renderMode;
        public Mesh mesh;
        public ParticleSystemRenderSpace alignment;
        public bool rotation3D;
        public float rotX, rotY, rotZ;
        public float sizeZ;
    }

    static Mesh _cylinder;
    readonly Dictionary<ParticleSystem, Home> _homes = new Dictionary<ParticleSystem, Home>();
    bool _restorePending;

    /// <summary>바꾼 시스템이 있는가(되돌리기 대기 포함).</summary>
    public bool Active => _homes.Count > 0;

    /// <summary>원통 메시(공유, 한 번 만든다) — 축 +Y, y∈[0,1], 반지름 0.5.</summary>
    public static Mesh Cylinder => _cylinder != null ? _cylinder : (_cylinder = BuildCylinder(24));

    /// <summary>
    /// 몸통 파티클을 원통 렌더로(앞으로 방출될 입자부터 — Play 직후 같은 프레임에 부르면 첫 버스트부터 맞는다).
    /// 몸통 = size3D 이고 Stretch 인 시스템(불꽃·섬광·이동 빔은 그대로).
    /// </summary>
    public void Apply(List<ParticleSystem> systems)
    {
        _restorePending = false;
        for (int i = 0; i < systems.Count; i++)
        {
            ParticleSystem ps = systems[i];
            if (ps == null || _homes.ContainsKey(ps)) continue;
            ParticleSystem.MainModule main = ps.main;
            if (!main.startSize3D || !ps.TryGetComponent(out ParticleSystemRenderer r) || r.renderMode != ParticleSystemRenderMode.Stretch) continue;

            _homes[ps] = new Home
            {
                renderer = r, renderMode = r.renderMode, mesh = r.mesh, alignment = r.alignment,
                rotation3D = main.startRotation3D,
                rotX = main.startRotationXMultiplier, rotY = main.startRotationYMultiplier, rotZ = main.startRotationZMultiplier,
                sizeZ = main.startSizeZMultiplier,
            };
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = Cylinder;
            r.alignment = ParticleSystemRenderSpace.Local;
            main.startRotation3D = true;
            main.startRotationXMultiplier = 90f * Mathf.Deg2Rad;
            main.startRotationYMultiplier = 0f;
            main.startRotationZMultiplier = 0f;
            main.startSizeZMultiplier = main.startSizeXMultiplier;
        }
    }

    /// <summary>굵기가 바뀐 뒤(X 배율) 원통 단면을 다시 원으로 — Z = X.</summary>
    public void SyncRoundSection()
    {
        foreach (ParticleSystem ps in _homes.Keys)
        {
            if (ps == null) continue;
            ParticleSystem.MainModule main = ps.main;
            main.startSizeZMultiplier = main.startSizeXMultiplier;
        }
    }

    /// <summary>되돌리기 예약 — 남은 입자(소멸 꼬리)가 다 사라지면 <see cref="Tick"/> 이 되돌린다(도중에 빌보드로 바뀌지 않게).</summary>
    public void RequestRestore() => _restorePending = _homes.Count > 0;

    /// <summary>매 프레임 — 예약돼 있고 바꾼 시스템에 입자가 없으면 되돌린다.</summary>
    public void Tick()
    {
        if (!_restorePending) return;
        foreach (ParticleSystem ps in _homes.Keys)
            if (ps != null && ps.particleCount > 0) return;
        RestoreNow();
    }

    /// <summary>지금 바로 전부 되돌린다(정리·파괴).</summary>
    public void RestoreNow()
    {
        foreach (KeyValuePair<ParticleSystem, Home> kv in _homes)
        {
            ParticleSystem ps = kv.Key;
            if (ps == null) continue;
            Home h = kv.Value;
            ParticleSystem.MainModule main = ps.main;
            main.startRotation3D = h.rotation3D;
            main.startRotationXMultiplier = h.rotX;
            main.startRotationYMultiplier = h.rotY;
            main.startRotationZMultiplier = h.rotZ;
            main.startSizeZMultiplier = h.sizeZ;
            if (h.renderer != null)
            {
                h.renderer.renderMode = h.renderMode;
                h.renderer.mesh = h.mesh;
                h.renderer.alignment = h.alignment;
            }
        }
        _homes.Clear();
        _restorePending = false;
    }

    /// <summary>원통 메시 — 축 +Y, y∈[0,1], 반지름 0.5, 옆면만(양 끝은 레이저 끝 섬광이 덮는다). U = 길이, V = 둘레 0→1→0.</summary>
    public static Mesh BuildCylinder(int segments)
    {
        segments = Mathf.Max(3, segments);
        int ring = segments + 1;
        var vertices = new Vector3[ring * 2];
        var normals = new Vector3[ring * 2];
        var uvs = new Vector2[ring * 2];
        var triangles = new int[segments * 6];
        for (int i = 0; i < ring; i++)
        {
            float a = (float)i / segments * Mathf.PI * 2f;
            var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float v = SectionV((float)i / segments);
            vertices[i] = n * 0.5f;
            vertices[ring + i] = n * 0.5f + Vector3.up;
            normals[i] = normals[ring + i] = n;
            uvs[i] = new Vector2(0f, v);
            uvs[ring + i] = new Vector2(1f, v);
        }
        for (int i = 0; i < segments; i++)
        {
            int t = i * 6;
            int b0 = i, b1 = i + 1, t0 = ring + i, t1 = ring + i + 1;
            // 바깥에서 보이게(Unity 시계 방향 = 앞면).
            triangles[t] = b0; triangles[t + 1] = t0; triangles[t + 2] = b1;
            triangles[t + 3] = b1; triangles[t + 4] = t0; triangles[t + 5] = t1;
        }
        var mesh = new Mesh { name = "GunnerBeamCylinder", hideFlags = HideFlags.DontSave };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>둘레 비율(0~1) → V(0→1→0, 삼각파) — 이음매가 없고, 어느 쪽에서 봐도 반 바퀴가 폭 그라데이션 하나.</summary>
    public static float SectionV(float around) => 1f - Mathf.Abs(Mathf.Repeat(around, 1f) * 2f - 1f);
}
