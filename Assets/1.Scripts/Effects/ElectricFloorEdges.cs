using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 전기 장판 칸 가장자리를 따라 흐르는 전류(2026-10-23 민경). 칸 테두리 위에 살짝 떨리는 선(은은한 번짐)과
/// 그 위를 빠르게 미끄러지는 빛 줄기(재질 UV 흐름)를 메시 하나(2겹)로 그린다.
///
/// - 한 변은 한 칸만 그린다 — 칸은 위·왼쪽 변을 늘 그리고, 아래·오른쪽 변은 그쪽 이웃이 같이 터지지 않을 때만 그린다
///   (<see cref="Build"/> 의 플래그, 쓰는 쪽 = <see cref="ElectricFloorFxPool"/>). 겹쳐 그리면 가산이라 공유 변만 두 배로 밝아진다.
/// - 떨림은 <see cref="Strike"/> 때마다 새로 뽑는다(번개 다시 치기와 같은 박자). 모서리 점은 고정 — 이웃 칸 선과 끊기지 않는다.
/// - 메시는 칸 로컬 XZ 평면(+Y 위), UV.x = 변을 따라 잰 거리 ÷ <see cref="flowPeriod"/>.
///
/// MyProject 미리보기와 MainProject 가 <b>같은 guid</b> 로 공유한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ElectricFloorEdges : MonoBehaviour
{
    public const int Top = 1, Bottom = 2, Left = 4, Right = 8;

    [Tooltip("떨림 세기(m)")]
    [SerializeField, Min(0f)] float jitter = 0.05f;
    [Tooltip("떨림 마디 간격(m)")]
    [SerializeField, Min(0.05f)] float segment = 0.3f;
    [Tooltip("은은한 번짐 굵기(m)")]
    [SerializeField, Min(0.001f)] float glowWidth = 0.22f;
    [Tooltip("흐르는 빛 줄기 굵기(m)")]
    [SerializeField, Min(0.001f)] float flowWidth = 0.12f;
    [Tooltip("빛 줄기 무늬 한 장이 덮는 길이(m) — 흐름 재질 텍스처 한 장 = 이 길이")]
    [SerializeField, Min(0.05f)] float flowPeriod = 1.2f;

    static readonly int ColorFactorId = Shader.PropertyToID("_ColorFactor");

    readonly List<Vector3> _verts = new List<Vector3>(256);
    readonly List<Vector2> _uvs = new List<Vector2>(256);
    readonly List<int> _glowTris = new List<int>(384);
    readonly List<int> _flowTris = new List<int>(384);
    readonly List<Vector3> _line = new List<Vector3>(32);

    Mesh _mesh;
    MeshRenderer _renderer;
    MaterialPropertyBlock _block;
    float _authoredFactor = 1f;
    Vector2 _extent = Vector2.one;
    int _row, _col, _flags;
    bool _hasMesh;

    void Awake() => Cache();

    void Cache()
    {
        if (_mesh != null) return;
        _mesh = new Mesh { name = "ElectricFloorEdges" };
        _mesh.MarkDynamic();
        GetComponent<MeshFilter>().sharedMesh = _mesh;
        _renderer = GetComponent<MeshRenderer>();
        _block = new MaterialPropertyBlock();
        Material m = _renderer.sharedMaterial;
        _authoredFactor = m != null && m.HasProperty(ColorFactorId) ? m.GetFloat(ColorFactorId) : 1f;
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

    /// <summary>그릴 변(<see cref="Top"/>·<see cref="Bottom"/>·<see cref="Left"/>·<see cref="Right"/> 조합). 모양은 <see cref="Strike"/> 에서.</summary>
    public void Build(int flags)
    {
        Cache();
        _flags = flags;
    }

    /// <summary>떨림을 새로 뽑아 메시를 다시 짠다.</summary>
    public void Strike(int seed, int strike)
    {
        Cache();
        _verts.Clear();
        _uvs.Clear();
        _glowTris.Clear();
        _flowTris.Clear();
        var rng = new System.Random(unchecked(_row * 92821 + _col * 68917 + seed * 31 + strike * 7919));

        float hx = 0.5f * _extent.x, hz = 0.5f * _extent.y;
        // 칸 로컬 — 행 0 = +Z 쪽(위), 열 0 = −X 쪽(왼쪽). 흐름 방향은 변마다 한쪽으로(위·아래 = +X, 왼·오른 = −Z).
        if ((_flags & Top) != 0) Edge(new Vector3(-hx, 0f, hz), new Vector3(hx, 0f, hz), rng);
        if ((_flags & Bottom) != 0) Edge(new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz), rng);
        if ((_flags & Left) != 0) Edge(new Vector3(-hx, 0f, hz), new Vector3(-hx, 0f, -hz), rng);
        if ((_flags & Right) != 0) Edge(new Vector3(hx, 0f, hz), new Vector3(hx, 0f, -hz), rng);

        _mesh.Clear();
        _mesh.subMeshCount = 2;
        _mesh.SetVertices(_verts);
        _mesh.SetUVs(0, _uvs);
        _mesh.SetTriangles(_glowTris, 0, false);
        _mesh.SetTriangles(_flowTris, 1, false);
        _mesh.RecalculateBounds();
        _hasMesh = _verts.Count > 0;
    }

    /// <summary>밝기(재질 _ColorFactor 저작값 × level). 0 이면 끈다.</summary>
    public void SetLevel(float level)
    {
        Cache();
        bool on = level > 0.001f && _hasMesh;
        if (_renderer.enabled != on) _renderer.enabled = on;
        if (!on) return;
        _renderer.GetPropertyBlock(_block);
        _block.SetFloat(ColorFactorId, _authoredFactor * level);
        _renderer.SetPropertyBlock(_block);
    }

    void Edge(Vector3 a, Vector3 b, System.Random rng)
    {
        float len = Vector3.Distance(a, b);
        int n = Mathf.Max(2, Mathf.CeilToInt(len / segment));
        Vector3 dir = (b - a) / len;
        var side = new Vector3(-dir.z, 0f, dir.x);
        _line.Clear();
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n;
            Vector3 p = Vector3.Lerp(a, b, t);
            if (i > 0 && i < n) p += side * ((float)rng.NextDouble() * 2f - 1f) * jitter;   // 모서리는 고정
            _line.Add(p);
        }
        Ribbon(glowWidth, _glowTris);
        Ribbon(flowWidth, _flowTris);
    }

    void Ribbon(float width, List<int> tris)
    {
        int first = _verts.Count;
        float d = 0f;
        for (int i = 0; i < _line.Count; i++)
        {
            if (i > 0) d += Vector3.Distance(_line[i - 1], _line[i]);
            Vector3 prev = _line[Mathf.Max(0, i - 1)], next = _line[Mathf.Min(_line.Count - 1, i + 1)];
            Vector3 dir = next - prev;
            var side = new Vector3(-dir.z, 0f, dir.x).normalized * (0.5f * width);
            _verts.Add(_line[i] - side);
            _verts.Add(_line[i] + side);
            float u = d / flowPeriod;
            _uvs.Add(new Vector2(u, 0f));
            _uvs.Add(new Vector2(u, 1f));
            if (i == 0) continue;
            int v = first + i * 2;
            tris.Add(v - 2); tris.Add(v); tris.Add(v - 1);
            tris.Add(v - 1); tris.Add(v); tris.Add(v + 1);
        }
    }
}
