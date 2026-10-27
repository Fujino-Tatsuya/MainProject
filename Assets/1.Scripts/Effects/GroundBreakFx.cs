using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 23호 착지 균열 연출(FX_GroundBreak_Shock)의 재생 담당(2026-10-27 민경 grill — MainProject PLAN.md 「23호 착지 균열 VFX 재제작」).
///
/// - 재생(OnEnable — 풀에서 꺼낼 때마다)마다 균열 패턴 3벌 중 하나만 켜고 방향을 무작위로 돌린다.
/// - 깨진 바닥 = 착지점 둘레를 나눈 판 조각들(각 패턴 밑 'Slabs') — 판끼리 벌어진 틈으로 아래 깔린 빛이 보인다. 판 무늬는 <b>그 자리 실제 바닥 타일</b>에 맞춘다 —
///   씬의 4m 돌 타일(<see cref="tileMeshName"/>)을 찾아 판 윗면을 타일 경계로 자르고 타일마다의 UV 로 다시 쓴다.
///   그래서 타일 홈선이 판 위로 그대로 이어지고, 판들이 평평하게 돌아오면 숨겨 원래 바닥만 남는다.
///   돌 타일이 아닌 곳(트렌치·충전판·타일 밖)의 판은 프리팹에 구운 비슷한 무늬를 쓰고, 끝에 바닥 아래로 가라앉는다.
/// - 판 움직임: 평평 → <see cref="riseTime"/> 동안 들리고 기움(프리팹에 저장된 쉬는 자세) → <see cref="settleStart"/>~<see cref="settleEnd"/> 에 다시 평평.
///   바닥 재질이 불투명(컷아웃)이라 투명하게 지울 수 없어서 "원래대로 맞춰짐"으로 사라지게 한다.
/// - 판 위 금(정가운데에서 뻗는 뿌리 균열) = 각 판 밑 'Cracks'. 판과 함께 기울고, 밝기는 재질 칸마다 MaterialPropertyBlock 으로 움직인다
///   (채움·번짐 = 판 틈 빛과 같은 박자 · 홈 = 판이 평평해질 때 사라짐).
/// - 균열 빛을 받는 판을 위해 점광원 하나를 짧게 켠다. 입자는 각자 playOnAwake 로 재생된다. 네트워크·판정 없음(클라마다 따로 고른다).
/// MyProject 미리보기와 MainProject 가 같은 파일을 쓴다.
/// </summary>
public class GroundBreakFx : MonoBehaviour
{
    [Tooltip("균열 패턴 묶음 — 재생마다 하나만 켠다. 각 묶음 밑 'Slabs' 의 자식이 깨진 바닥 판이다")]
    [SerializeField] GameObject[] variants;
    [Tooltip("무작위 방향으로 돌릴 부모(패턴 묶음들의 부모)")]
    [SerializeField] Transform spin;

    [Header("깨진 바닥 판")]
    [SerializeField] float riseTime = 0.08f;
    [SerializeField] float settleStart = 1.1f;
    [SerializeField] float settleEnd = 1.4f;
    [Tooltip("평평할 때 판 윗면을 바닥보다 이만큼 띄운다(m) — 바닥과 같은 높이면 깜빡인다")]
    [SerializeField] float baseLift = 0.004f;
    [Tooltip("돌 타일에 못 맞춘 판이 끝에 가라앉는 깊이(m)")]
    [SerializeField] float fallbackSink = 1f;

    [Header("바닥 타일 맞춤(빌더가 채운다)")]
    [Tooltip("바닥 돌 타일 메시 이름(floor_stone.fbx)")]
    [SerializeField] string tileMeshName;
    [Tooltip("타일 메시 로컬 (x, z, 1) → 아틀라스 u")]
    [SerializeField] Vector3 tileU;
    [Tooltip("타일 메시 로컬 (x, z, 1) → 아틀라스 v")]
    [SerializeField] Vector3 tileV;
    [Tooltip("타일 메시 로컬에서 바닥 평면을 이루는 두 축(x, z 자리) — fbx 가 축을 돌려 들여올 수 있어 빌더가 고른다")]
    [SerializeField] Vector2Int tileAxes = new Vector2Int(0, 2);
    [Tooltip("타일 윗면 UV 범위(최소 u, 최소 v, 최대 u, 최대 v) — 타일 경계에서 이웃 아틀라스 칸이 번지지 않게 이 안으로 묶는다")]
    [SerializeField] Vector4 tileUVRect = new Vector4(0f, 0f, 1f, 1f);
    [SerializeField] float tileSize = 4f;

    [Header("판 위 금(각 판 밑 'Cracks' — 재질 0 = 홈 · 1 = 채움 · 2 = 번짐)")]
    [Tooltip("빛(채움·번짐)이 다 빠지는 시간(초) — 0.06초에 가장 밝다")]
    [SerializeField] float crackGlowTime = 0.75f;
    [Tooltip("어두운 홈이 흐려지기 시작하는 때(초) — 판이 평평해지기 시작할 때")]
    [SerializeField] float crackDarkFadeStart = 1.1f;
    [Tooltip("어두운 홈이 다 사라지는 때(초)")]
    [SerializeField] float crackDarkFadeEnd = 1.4f;

    [Header("균열 빛 조명")]
    [SerializeField] Light flashLight;
    [SerializeField] float lightPeak = 12f;
    [SerializeField] float lightTime = 0.6f;

    Transform[][] _slabs;
    MeshFilter[][] _filters;
    Mesh[][] _source, _aligned;
    bool[][] _fallback;
    Vector3[][] _restPos;
    Quaternion[][] _restRot;
    Renderer[][] _cracks;
    Color[] _crackTint;   // 재질 칸마다 원래 색 — 알파만 곱해 쓴다
    MaterialPropertyBlock _block;
    static readonly int TintId = Shader.PropertyToID("_TintColor");
    int _active = -1;
    float _time;
    bool _needAlign;

    void Awake() => Cache();

    void Cache()
    {
        if (_slabs != null || variants == null) return;
        int n = variants.Length;
        _slabs = new Transform[n][]; _filters = new MeshFilter[n][]; _source = new Mesh[n][]; _aligned = new Mesh[n][];
        _fallback = new bool[n][]; _restPos = new Vector3[n][]; _restRot = new Quaternion[n][];
        _cracks = new Renderer[n][];
        _block = new MaterialPropertyBlock();
        for (int v = 0; v < n; v++)
        {
            Transform root = variants[v] != null ? variants[v].transform.Find("Slabs") : null;
            int count = root != null ? root.childCount : 0;
            _slabs[v] = new Transform[count]; _filters[v] = new MeshFilter[count]; _source[v] = new Mesh[count]; _aligned[v] = new Mesh[count];
            _fallback[v] = new bool[count]; _restPos[v] = new Vector3[count]; _restRot[v] = new Quaternion[count];
            var cracks = new List<Renderer>();
            for (int i = 0; i < count; i++)
            {
                Transform s = root.GetChild(i);
                _slabs[v][i] = s;
                _filters[v][i] = s.GetComponent<MeshFilter>();
                _source[v][i] = _filters[v][i] != null ? _filters[v][i].sharedMesh : null;
                _restPos[v][i] = s.localPosition;
                _restRot[v][i] = s.localRotation;
                Transform c = s.Find("Cracks");
                if (c != null && c.TryGetComponent(out Renderer r)) cracks.Add(r);
            }
            _cracks[v] = cracks.ToArray();
            if (_crackTint == null && cracks.Count > 0)
            {
                Material[] mats = cracks[0].sharedMaterials;
                _crackTint = new Color[mats.Length];
                for (int k = 0; k < mats.Length; k++) _crackTint[k] = mats[k] != null && mats[k].HasProperty(TintId) ? mats[k].GetColor(TintId) : Color.white;
            }
        }
    }

    void OnEnable()
    {
        Cache();
        int n = variants != null ? variants.Length : 0;
        Force(n > 0 ? Random.Range(0, n) : -1);
        if (spin != null) spin.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        _time = 0f;
        _needAlign = true;   // 풀은 켠 뒤에 위치를 옮길 수 있다 — 무늬 맞춤은 첫 LateUpdate 에서
        Evaluate(0f);
    }

    void Update()
    {
        _time += Time.deltaTime;
        Evaluate(_time);
    }

    void LateUpdate()
    {
        if (!_needAlign) return;
        _needAlign = false;
        AlignToFloor();
    }

    void OnDestroy()
    {
        if (_aligned == null) return;
        foreach (Mesh[] list in _aligned)
            foreach (Mesh m in list)
                if (m != null) Destroy(m);
    }

    /// <summary>재생 후 <paramref name="t"/> 초의 판 자세·조명. 미리보기 캡처도 이걸 부른다.</summary>
    public void Evaluate(float t)
    {
        if (_active >= 0 && _slabs != null)
        {
            float rise = riseTime > 0f ? Mathf.Clamp01(t / riseTime) : 1f;
            rise = 1f - (1f - rise) * (1f - rise);   // 빠르게 튀어 오르고 끝에서 멈춤
            float settle = Mathf.Clamp01((t - settleStart) / Mathf.Max(0.01f, settleEnd - settleStart));
            settle = settle * settle * (3f - 2f * settle);
            float pose = rise * (1f - settle);
            Transform[] slabs = _slabs[_active];
            for (int i = 0; i < slabs.Length; i++)
            {
                Vector3 rest = _restPos[_active][i];
                Vector3 flat = new Vector3(rest.x, baseLift, rest.z);
                Vector3 p = Vector3.LerpUnclamped(flat, rest, pose);
                if (_fallback[_active][i]) p.y -= settle * fallbackSink;
                slabs[i].localPosition = p;
                slabs[i].localRotation = Quaternion.SlerpUnclamped(Quaternion.identity, _restRot[_active][i], pose);
                // 평평하게 돌아온 뒤에는 숨긴다 — 원래 바닥만 남아 깨진 흔적이 완전히 지워진다.
                bool show = t < settleEnd;
                if (slabs[i].gameObject.activeSelf != show) slabs[i].gameObject.SetActive(show);
            }
            UpdateCracks(t);
        }

        if (flashLight != null)
        {
            float k = t < 0.03f ? t / 0.03f : Mathf.Clamp01(1f - (t - 0.03f) / Mathf.Max(0.01f, lightTime - 0.03f));
            flashLight.intensity = lightPeak * k * k;
            flashLight.enabled = k > 0f;
        }
    }

    // 판 위 금 밝기 — 빛은 판 틈과 같은 박자(0 → 0.06초에 가장 밝게 → crackGlowTime 에 꺼짐), 홈은 바로 짙어졌다가 판이 평평해질 때 사라진다.
    void UpdateCracks(float t)
    {
        Renderer[] cracks = _cracks[_active];
        if (cracks.Length == 0 || _crackTint == null) return;
        float g = Mathf.Clamp01(t / Mathf.Max(0.01f, crackGlowTime));
        float glow = g < 0.06f ? Mathf.Lerp(0.3f, 1f, g / 0.06f) : g < 0.22f ? Mathf.Lerp(1f, 0.8f, (g - 0.06f) / 0.16f) : Mathf.Lerp(0.8f, 0f, (g - 0.22f) / 0.78f);
        float dark = Mathf.Clamp01(t / 0.03f) * (1f - Mathf.Clamp01((t - crackDarkFadeStart) / Mathf.Max(0.01f, crackDarkFadeEnd - crackDarkFadeStart)));
        foreach (Renderer r in cracks)
            for (int k = 0; k < _crackTint.Length; k++)
            {
                Color c = _crackTint[k];
                c.a *= k == 0 ? dark : glow;
                _block.Clear();
                _block.SetColor(TintId, c);
                r.SetPropertyBlock(_block, k);
            }
    }

    /// <summary>패턴을 정해 켠다(방향은 그대로). 미리보기 캡처도 쓴다.</summary>
    public void Force(int variant)
    {
        Cache();
        if (variants == null || variants.Length == 0) { _active = -1; return; }
        _active = Mathf.Clamp(variant, 0, variants.Length - 1);
        for (int v = 0; v < variants.Length; v++)
            if (variants[v] != null) variants[v].SetActive(v == _active);
    }

    // ── 바닥 타일 맞춤 ──

    // 씬의 돌 타일 — 4m 격자 칸 → 타일. 처음 쓸 때 한 번 찾고, 타일이 사라졌거나(씬 전환) 칸을 못 찾으면 다시 찾는다.
    // 타일 사이 홈(약 8cm)도 판이 덮는다 — 판 아래 깔린 빛이 홈으로 새지 않게. 홈 자리 무늬는 타일 가장자리 색으로 묶인다(tileUVRect).
    struct Tile { public Transform transform; public Rect top; }
    static readonly Dictionary<Vector2Int, Tile> s_tiles = new Dictionary<Vector2Int, Tile>();
    static Vector2 s_origin;
    static string s_meshName;
    static float s_lastScan = -999f;

    static readonly List<Vector3> s_verts = new List<Vector3>(), s_normals = new List<Vector3>();
    static readonly List<Vector2> s_uvs = new List<Vector2>();
    static readonly List<int> s_tris = new List<int>();
    static readonly List<Vector2> s_poly = new List<Vector2>(16), s_clip = new List<Vector2>(16);

    /// <summary>켜진 패턴의 판 윗면을 그 자리 바닥 타일 무늬로 다시 쓴다. 미리보기 캡처도 쓴다.</summary>
    public void AlignToFloor()
    {
        if (_active < 0 || _slabs == null || string.IsNullOrEmpty(tileMeshName)) return;
        Transform[] slabs = _slabs[_active];
        if (slabs.Length == 0) return;
        Transform slabRoot = slabs[0].parent;
        if (!EnsureTiles(tileMeshName, tileSize, transform.position)) { MarkAllFallback(); return; }

        for (int i = 0; i < slabs.Length; i++)
        {
            Mesh src = _source[_active][i];
            MeshFilter mf = _filters[_active][i];
            if (src == null || mf == null) continue;
            Vector3 rest = _restPos[_active][i];
            Matrix4x4 flat = slabRoot.localToWorldMatrix * Matrix4x4.Translate(new Vector3(rest.x, baseLift, rest.z));
            Matrix4x4 inv = flat.inverse;
            if (BuildAligned(src, flat, inv))
            {
                Mesh dst = _aligned[_active][i];
                if (dst == null) { dst = new Mesh { name = src.name + " (바닥 맞춤)" }; _aligned[_active][i] = dst; }
                dst.Clear();
                dst.SetVertices(s_verts);
                dst.SetNormals(s_normals);
                dst.SetUVs(0, s_uvs);
                dst.SetTriangles(s_tris, 0);
                dst.RecalculateTangents();
                dst.RecalculateBounds();
                mf.sharedMesh = dst;
                _fallback[_active][i] = false;
            }
            else
            {
                mf.sharedMesh = src;
                _fallback[_active][i] = true;
            }
        }
    }

    void MarkAllFallback()
    {
        for (int i = 0; i < _slabs[_active].Length; i++)
        {
            if (_filters[_active][i] != null) _filters[_active][i].sharedMesh = _source[_active][i];
            _fallback[_active][i] = true;
        }
    }

    // 옆면은 그대로, 윗면(y = 0 · 위를 보는 삼각형)은 타일 칸 경계로 잘라 칸마다 그 타일의 UV 를 쓴다. 못 맞춘 칸이 있으면 false.
    bool BuildAligned(Mesh src, Matrix4x4 flat, Matrix4x4 inv)
    {
        s_verts.Clear(); s_normals.Clear(); s_uvs.Clear(); s_tris.Clear();
        Vector3[] v = src.vertices; Vector3[] n = src.normals; Vector2[] uv = src.uv; int[] t = src.triangles;
        for (int k = 0; k < t.Length; k += 3)
        {
            int a = t[k], b = t[k + 1], c = t[k + 2];
            bool top = n[a].y > 0.9f && Mathf.Abs(v[a].y) < 0.001f && Mathf.Abs(v[b].y) < 0.001f && Mathf.Abs(v[c].y) < 0.001f;
            if (!top)
            {
                int o = s_verts.Count;
                s_verts.Add(v[a]); s_verts.Add(v[b]); s_verts.Add(v[c]);
                s_normals.Add(n[a]); s_normals.Add(n[b]); s_normals.Add(n[c]);
                s_uvs.Add(uv[a]); s_uvs.Add(uv[b]); s_uvs.Add(uv[c]);
                s_tris.Add(o); s_tris.Add(o + 1); s_tris.Add(o + 2);
                continue;
            }
            Vector3 wa = flat.MultiplyPoint3x4(v[a]), wb = flat.MultiplyPoint3x4(v[b]), wc = flat.MultiplyPoint3x4(v[c]);
            float y = wa.y;
            int x0 = Cell(Mathf.Min(wa.x, Mathf.Min(wb.x, wc.x)), s_origin.x), x1 = Cell(Mathf.Max(wa.x, Mathf.Max(wb.x, wc.x)), s_origin.x);
            int z0 = Cell(Mathf.Min(wa.z, Mathf.Min(wb.z, wc.z)), s_origin.y), z1 = Cell(Mathf.Max(wa.z, Mathf.Max(wb.z, wc.z)), s_origin.y);
            for (int cx = x0; cx <= x1; cx++)
                for (int cz = z0; cz <= z1; cz++)
                {
                    float minX = s_origin.x + cx * tileSize, minZ = s_origin.y + cz * tileSize;
                    s_poly.Clear();
                    s_poly.Add(new Vector2(wa.x, wa.z)); s_poly.Add(new Vector2(wb.x, wb.z)); s_poly.Add(new Vector2(wc.x, wc.z));
                    ClipRect(s_poly, minX, minZ, minX + tileSize, minZ + tileSize);
                    if (s_poly.Count < 3) continue;
                    if (!s_tiles.TryGetValue(new Vector2Int(cx, cz), out Tile cell) || cell.transform == null) return false;
                    Transform tile = cell.transform;
                    int o = s_verts.Count;
                    foreach (Vector2 q in s_poly)
                    {
                        Vector3 w = new Vector3(q.x, y, q.y);
                        s_verts.Add(inv.MultiplyPoint3x4(w));
                        s_normals.Add(Vector3.up);
                        Vector3 l = tile.InverseTransformPoint(w);
                        float lx = l[tileAxes.x], lz = l[tileAxes.y];
                        const float edge = 0.0012f;   // 약 5픽셀(4096) — 밉맵 번짐까지 막는다
                        s_uvs.Add(new Vector2(
                            Mathf.Clamp(tileU.x * lx + tileU.y * lz + tileU.z, tileUVRect.x + edge, tileUVRect.z - edge),
                            Mathf.Clamp(tileV.x * lx + tileV.y * lz + tileV.z, tileUVRect.y + edge, tileUVRect.w - edge)));
                    }
                    // 원래 삼각형의 감김 방향을 따른다(잘라도 볼록 다각형이라 부채꼴로 나눈다).
                    for (int p = 1; p + 1 < s_poly.Count; p++) { s_tris.Add(o); s_tris.Add(o + p); s_tris.Add(o + p + 1); }
                }
        }
        return true;
    }

    int Cell(float x, float origin) => Mathf.FloorToInt((x - origin) / tileSize);

    static bool EnsureTiles(string meshName, float size, Vector3 near)
    {
        bool stale = s_meshName != meshName || s_tiles.Count == 0;
        if (!stale)
            foreach (Tile t in s_tiles.Values) if (t.transform == null) { stale = true; break; }
        if (!stale)
        {
            var key = new Vector2Int(Mathf.FloorToInt((near.x - s_origin.x) / size), Mathf.FloorToInt((near.z - s_origin.y) / size));
            if (s_tiles.ContainsKey(key)) return true;
            stale = true;
        }
        // 다시 찾기는 1초에 한 번까지 — 돌 타일이 없는 씬에서 착지할 때마다 씬 전체를 뒤지지 않는다.
        if (Time.unscaledTime - s_lastScan < 1f) return s_tiles.Count > 0;
        s_lastScan = Time.unscaledTime;
        s_tiles.Clear();
        s_meshName = meshName;
        bool hasOrigin = false;
        foreach (MeshFilter mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
        {
            if (mf.sharedMesh == null || mf.sharedMesh.name != meshName) continue;
            var r = mf.GetComponent<Renderer>();
            if (r == null) continue;
            Bounds b = r.bounds;
            if (Mathf.Abs(b.size.x - size) > 0.2f || Mathf.Abs(b.size.z - size) > 0.2f) continue;   // 눕지 않은 타일은 빼 둔다
            if (!hasOrigin) { s_origin = new Vector2(b.min.x, b.min.z); hasOrigin = true; }
            var key = new Vector2Int(Mathf.FloorToInt((b.center.x - s_origin.x) / size), Mathf.FloorToInt((b.center.z - s_origin.y) / size));
            s_tiles[key] = new Tile { transform = mf.transform, top = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z) };
        }
        return s_tiles.Count > 0;
    }

    // 볼록 다각형을 축 정렬 사각형으로 자른다(Sutherland–Hodgman).
    static void ClipRect(List<Vector2> poly, float minX, float minZ, float maxX, float maxZ)
    {
        ClipEdge(poly, 0, minX, true); ClipEdge(poly, 0, maxX, false);
        ClipEdge(poly, 1, minZ, true); ClipEdge(poly, 1, maxZ, false);
    }

    static void ClipEdge(List<Vector2> poly, int axis, float bound, bool keepGreater)
    {
        if (poly.Count == 0) return;
        s_clip.Clear();
        for (int i = 0; i < poly.Count; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
            float da = (axis == 0 ? a.x : a.y) - bound, db = (axis == 0 ? b.x : b.y) - bound;
            if (!keepGreater) { da = -da; db = -db; }
            bool ina = da >= 0f, inb = db >= 0f;
            if (ina) s_clip.Add(a);
            if (ina != inb) s_clip.Add(Vector2.Lerp(a, b, da / (da - db)));
        }
        poly.Clear();
        poly.AddRange(s_clip);
    }
}
