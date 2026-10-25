using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 전기 장판 칸 하나의 갈라지는 번개(2026-10-23 민경 — 레퍼런스 「전기장판 번개 레퍼런스.mp4」, 3~4배 빠르게 · 천둥 느낌).
/// 칸 가운데 근처 한 점에서 번개 줄기가 사방으로 갈라져 칸 네 변까지 뻗는다. 메시 하나(2겹: 넓은 번짐 + 가는 흰 심)를 코드로 짠다.
///
/// - 변 위 끝점은 <b>이웃 칸과 같은 자리</b>(변 번호·방전 번호로 정한다) — 터진 칸들의 번개가 칸 경계에서 이어져 판 전체가 한 그물처럼 보인다.
/// - <see cref="Strike"/> = 새 모양(방전 한 번). <see cref="SetReveal"/> = 시작점에서 몇 m 까지 보일지(뻗어 나가기).
///   <see cref="SetLevel"/> = 밝기(_ColorFactor). 언제 다시 치고 얼마나 밝을지는 <see cref="ElectricFloorTileFx"/> 가 정한다.
/// - 모양 수치는 두 벌 — 평소(<see cref="normal"/>)와 즉사 방전(<see cref="lethal"/>: 줄기·곁가지가 많고 더 꺾인다, 10-23 민경). 굵기는 같다.
/// - 메시는 칸 로컬 XZ 평면(+Y 위). 칸 크기는 <see cref="Setup"/> 에서 받는다 — 루트 배율 1.
///
/// MyProject 미리보기와 MainProject 가 <b>같은 guid</b> 로 공유한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ElectricFloorBolts : MonoBehaviour
{
    [Serializable]
    public struct Shape
    {
        [Tooltip("시작점이 칸 가운데에서 벗어나는 정도(칸 크기 비율)")]
        [Range(0f, 0.4f)] public float originJitter;
        [Tooltip("칸 변마다 줄기가 닿을 확률 — 변 단위로 정해서 이웃 칸도 같은 답을 얻는다(닿으면 양쪽 줄기가 이어진다). 낮을수록 한산하다")]
        [Range(0f, 1f)] public float edgeChance;
        [Tooltip("변까지 가는 줄기 말고 칸 안에서 끝나는 줄기 수(최소~최대)")]
        public Vector2Int freeBolts;
        [Tooltip("지그재그 세기(줄기 길이 비율) — 1단계 값, 단계마다 반으로")]
        [Range(0f, 0.5f)] public float roughness;
        [Range(1, 6)] public int detail;
        [Tooltip("줄기 위 점마다 곁가지가 날 확률")]
        [Range(0f, 1f)] public float branchChance;
    }

    [Header("모양")]
    [Tooltip("평소 장판")]
    [SerializeField] Shape normal = new Shape
    {
        originJitter = 0.2f, edgeChance = 0.5f, freeBolts = new Vector2Int(0, 1), roughness = 0.16f, detail = 3, branchChance = 0.15f,
    };
    [Tooltip("즉사 방전 — 추가 연출 없이 번개 모양만 다르다(10-23 민경)")]
    [SerializeField] Shape lethal = new Shape
    {
        originJitter = 0.2f, edgeChance = 0.709f, freeBolts = new Vector2Int(0, 10), roughness = 0.5f, detail = 3, branchChance = 0.4f,
    };

    [Header("굵기(m)")]
    [SerializeField, Min(0.001f)] float coreWidth = 0.05f;
    [SerializeField, Min(0.001f)] float glowWidth = 0.45f;
    [Tooltip("끝으로 갈수록 가늘어지는 정도(끝 굵기 = 1 − 이 값)")]
    [SerializeField, Range(0f, 1f)] float taper = 0.6f;
    [SerializeField, Range(0.1f, 1f)] float branchWidth = 0.55f;
    [Tooltip("뻗어 나가는 머리 부분이 흐려지는 길이(m)")]
    [SerializeField, Min(0.01f)] float revealSoftness = 0.4f;

    static readonly int ColorFactorId = Shader.PropertyToID("_ColorFactor");

    struct Line
    {
        public int start, count;   // _pts 안의 범위
        public float width;        // 굵기 배율(곁가지 < 1)
    }

    readonly List<Vector3> _pts = new List<Vector3>(256);
    readonly List<float> _dist = new List<float>(256);   // 시작점에서 줄기를 따라 잰 거리
    readonly List<Line> _lines = new List<Line>(32);
    readonly List<Vector3> _verts = new List<Vector3>(1024);
    readonly List<Color> _colors = new List<Color>(1024);
    readonly List<Vector2> _uvs = new List<Vector2>(1024);
    readonly List<float> _vDist = new List<float>(1024);
    readonly List<int> _glowTris = new List<int>(1536);
    readonly List<int> _coreTris = new List<int>(1536);
    readonly List<Vector3> _tmp = new List<Vector3>(64);

    Mesh _mesh;
    MeshRenderer _renderer;
    MaterialPropertyBlock _block;
    float[] _authoredFactor;
    Vector2 _extent = Vector2.one;
    int _row, _col;
    float _maxDist = 1f;
    float _reveal = float.PositiveInfinity;
    System.Random _rng;
    Shape _shape;
    int _detail;

    /// <summary>시작점에서 가장 먼 줄기 끝까지(m) — 뻗어 나가기 진행률을 거리로 바꿀 때.</summary>
    public float MaxDistance => _maxDist;

    void Awake() => Cache();

    void Cache()
    {
        if (_mesh != null) return;
        _mesh = new Mesh { name = "ElectricFloorBolts" };
        _mesh.MarkDynamic();
        _mesh.subMeshCount = 2;
        GetComponent<MeshFilter>().sharedMesh = _mesh;
        _renderer = GetComponent<MeshRenderer>();
        _block = new MaterialPropertyBlock();
        Material[] mats = _renderer.sharedMaterials;
        _authoredFactor = new float[mats.Length];
        for (int i = 0; i < mats.Length; i++)
            _authoredFactor[i] = mats[i] != null && mats[i].HasProperty(ColorFactorId) ? mats[i].GetFloat(ColorFactorId) : 1f;
        _renderer.enabled = false;
    }

    void OnDestroy()
    {
        if (_mesh != null) Destroy(_mesh);
    }

    public void Setup(Vector2 extent, int row, int col)
    {
        Cache();
        _extent = extent;
        _row = row;
        _col = col;
    }

    /// <summary>
    /// 새 모양. <paramref name="seed"/> = 이번 발동 번호(판 전체 공통) · <paramref name="strike"/> = 발동 안에서 몇 번째 방전인지 —
    /// 둘이 같으면 이웃 칸과 변 끝점이 맞는다. <paramref name="isLethal"/> = 즉사 방전 모양(<see cref="lethal"/>).
    /// </summary>
    public void Strike(int seed, int strike, bool isLethal = false)
    {
        Cache();
        _shape = isLethal ? lethal : normal;
        _detail = Mathf.Max(1, _shape.detail);
        _rng = new System.Random(Hash(_row, _col, seed, strike, 7));
        _pts.Clear();
        _dist.Clear();
        _lines.Clear();

        float ex = _extent.x, ey = _extent.y;
        float jitter = _shape.originJitter;
        var origin = new Vector3(Range(-jitter, jitter) * ex, 0f, Range(-jitter, jitter) * ey);

        // 네 변 중 닿는 변만 — 끝점 = 변 번호로 정해진 자리(이웃 칸이 같은 값을 얻는다). 행 0 = 칸 로컬 +Z 쪽.
        int edges = 0;
        if (EdgeOn(0, _row, _col, seed, strike))         // 위(행 −1 쪽)
        { AddBolt(origin, 0f, new Vector3((EdgeT(0, _row, _col, seed, strike) - 0.5f) * ex, 0f, 0.5f * ey), 1f, true); edges++; }
        if (EdgeOn(0, _row + 1, _col, seed, strike))     // 아래
        { AddBolt(origin, 0f, new Vector3((EdgeT(0, _row + 1, _col, seed, strike) - 0.5f) * ex, 0f, -0.5f * ey), 1f, true); edges++; }
        if (EdgeOn(1, _row, _col, seed, strike))         // 왼쪽(열 −1 쪽)
        { AddBolt(origin, 0f, new Vector3(-0.5f * ex, 0f, (0.5f - EdgeT(1, _row, _col, seed, strike)) * ey), 1f, true); edges++; }
        if (EdgeOn(1, _row, _col + 1, seed, strike))     // 오른쪽
        { AddBolt(origin, 0f, new Vector3(0.5f * ex, 0f, (0.5f - EdgeT(1, _row, _col + 1, seed, strike)) * ey), 1f, true); edges++; }

        // 닿는 변이 없으면 칸 안 줄기라도 둘 — 빈 칸이 생기지 않게.
        int free = _rng.Next(_shape.freeBolts.x, Mathf.Max(_shape.freeBolts.x, _shape.freeBolts.y) + 1) + (edges == 0 ? 2 : 0);
        for (int i = 0; i < free; i++)
        {
            float a = Range(0f, Mathf.PI * 2f);
            float r = Range(0.25f, 0.45f);
            var end = new Vector3(Mathf.Cos(a) * r * ex, 0f, Mathf.Sin(a) * r * ey);
            AddBolt(origin, 0f, end, 0.75f, true);
        }

        _maxDist = 0.01f;
        foreach (float d in _dist) _maxDist = Mathf.Max(_maxDist, d);
        Build();
    }

    /// <summary>시작점에서 <paramref name="meters"/> 까지만 보인다(머리는 부드럽게). 무한대 = 전부.</summary>
    public void SetReveal(float meters)
    {
        if (Mathf.Approximately(meters, _reveal)) return;
        _reveal = meters;
        if (_verts.Count == 0) return;
        for (int i = 0; i < _colors.Count; i++)
            _colors[i] = new Color(1f, 1f, 1f, RevealAlpha(_vDist[i]));
        _mesh.SetColors(_colors);
    }

    /// <summary>밝기(재질 _ColorFactor 저작값 × level). 0 이면 끈다.</summary>
    public void SetLevel(float level)
    {
        Cache();
        bool on = level > 0.001f && _verts.Count > 0;
        if (_renderer.enabled != on) _renderer.enabled = on;
        if (!on) return;
        _renderer.GetPropertyBlock(_block);
        _block.SetFloat(ColorFactorId, _authoredFactor.Length > 0 ? _authoredFactor[0] * level : level);
        _renderer.SetPropertyBlock(_block);
    }

    float RevealAlpha(float d) => float.IsPositiveInfinity(_reveal) ? 1f : Mathf.Clamp01((_reveal - d) / revealSoftness);

    // ── 모양 ──

    // a → b 지그재그 줄기 하나(+ 곁가지). startDist = a 까지 이미 온 거리.
    void AddBolt(Vector3 a, float startDist, Vector3 b, float width, bool branches)
    {
        _tmp.Clear();
        _tmp.Add(a);
        _tmp.Add(b);
        float amp = _shape.roughness * Vector3.Distance(a, b);
        for (int level = 0; level < _detail; level++, amp *= 0.5f)
        {
            for (int i = _tmp.Count - 1; i > 0; i--)
            {
                Vector3 p = _tmp[i - 1], q = _tmp[i];
                Vector3 dir = q - p;
                var side = new Vector3(-dir.z, 0f, dir.x).normalized;
                _tmp.Insert(i, (p + q) * 0.5f + side * Range(-amp, amp));
            }
        }

        int start = _pts.Count;
        float d = startDist;
        for (int i = 0; i < _tmp.Count; i++)
        {
            if (i > 0) d += Vector3.Distance(_tmp[i - 1], _tmp[i]);
            _pts.Add(_tmp[i]);
            _dist.Add(d);
        }
        _lines.Add(new Line { start = start, count = _tmp.Count, width = width });
        if (!branches) return;

        // 곁가지 — 줄기 진행 방향에서 25~55° 꺾여 남은 길이의 25~50%.
        int count = _tmp.Count;
        for (int i = 2; i < count - 2; i++)
        {
            if (_rng.NextDouble() > _shape.branchChance) continue;
            Vector3 p = _pts[start + i];
            Vector3 dir = (_pts[start + i + 1] - _pts[start + i - 1]).normalized;
            float remain = _dist[start + count - 1] - _dist[start + i];
            float len = Range(0.25f, 0.5f) * Mathf.Max(remain, 0.3f * _extent.x);
            float ang = Range(25f, 55f) * (_rng.Next(2) == 0 ? -1f : 1f);
            Vector3 end = p + Quaternion.Euler(0f, ang, 0f) * dir * len;
            end.x = Mathf.Clamp(end.x, -0.5f * _extent.x, 0.5f * _extent.x);
            end.z = Mathf.Clamp(end.z, -0.5f * _extent.y, 0.5f * _extent.y);
            int saved = _detail;
            _detail = Mathf.Max(1, _detail - 1);
            AddBolt(p, _dist[start + i], end, width * branchWidth, false);
            _detail = saved;
        }
    }

    // ── 메시 ──

    void Build()
    {
        _verts.Clear();
        _colors.Clear();
        _uvs.Clear();
        _vDist.Clear();
        _glowTris.Clear();
        _coreTris.Clear();
        foreach (Line l in _lines)
        {
            Ribbon(l, glowWidth, _glowTris);
            Ribbon(l, coreWidth, _coreTris);
        }
        _mesh.Clear();
        _mesh.subMeshCount = 2;
        _mesh.SetVertices(_verts);
        _mesh.SetColors(_colors);
        _mesh.SetUVs(0, _uvs);
        _mesh.SetTriangles(_glowTris, 0, false);
        _mesh.SetTriangles(_coreTris, 1, false);
        _mesh.RecalculateBounds();
    }

    // 끊김 없는 띠(꺾이는 곳은 양쪽 진행 방향의 평균으로) — 마디마다 겹쳐 그리면 가산이라 마디가 점처럼 밝아진다.
    void Ribbon(Line l, float baseWidth, List<int> tris)
    {
        int first = _verts.Count;
        for (int i = 0; i < l.count; i++)
        {
            int k = l.start + i;
            Vector3 prev = _pts[Mathf.Max(l.start, k - 1)], next = _pts[Mathf.Min(l.start + l.count - 1, k + 1)];
            Vector3 dir = next - prev;
            var side = new Vector3(-dir.z, 0f, dir.x).normalized;
            float t = l.count > 1 ? i / (float)(l.count - 1) : 0f;
            float w = baseWidth * l.width * (1f - taper * t) * 0.5f;
            float d = _dist[k];
            Vector3 p = _pts[k];
            _verts.Add(p - side * w);
            _verts.Add(p + side * w);
            _uvs.Add(new Vector2(t, 0f));
            _uvs.Add(new Vector2(t, 1f));
            var c = new Color(1f, 1f, 1f, RevealAlpha(d));
            _colors.Add(c);
            _colors.Add(c);
            _vDist.Add(d);
            _vDist.Add(d);
            if (i == 0) continue;
            int v = first + i * 2;
            tris.Add(v - 2); tris.Add(v); tris.Add(v - 1);
            tris.Add(v - 1); tris.Add(v); tris.Add(v + 1);
        }
    }

    // ── 난수 ──

    float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

    bool EdgeOn(int kind, int a, int b, int seed, int strike) =>
        ((Hash(kind, a, b, seed, strike + 9973) & 0xFFFF) / 65535f) < _shape.edgeChance;

    // 변 위 끝점 위치(0.2~0.8) — kind 0 = 가로 변(행 경계 line, 열 c), 1 = 세로 변(행 r, 열 경계 line).
    static float EdgeT(int kind, int a, int b, int seed, int strike) =>
        0.2f + 0.6f * ((Hash(kind, a, b, seed, strike) & 0xFFFF) / 65535f);

    static int Hash(int a, int b, int c, int d, int e)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)a) * 16777619u;
            h = (h ^ (uint)b) * 16777619u;
            h = (h ^ (uint)c) * 16777619u;
            h = (h ^ (uint)d) * 16777619u;
            h = (h ^ (uint)e) * 16777619u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return (int)(h & 0x7FFFFFFF);
        }
    }
}
