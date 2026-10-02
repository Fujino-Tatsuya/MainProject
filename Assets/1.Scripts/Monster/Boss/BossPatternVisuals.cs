using UnityEngine;

/// <summary>
/// 전기 장판 · 자폭 드론의 <b>임시 연출</b> 재료 — 바닥에 눕힌 사각/원 면, 크로스헤어 판.
/// 판정과 무관한 전 피어 로컬 연출. 민경 VFX 가 오면 데이터 SO 의 프리팹 슬롯으로 갈아끼운다.
///
/// 셰이더는 <c>Sprites/Default</c> — URP 에서 빌트인 Default-Material 은 자홍색으로 깨지고
/// (이 프로젝트에서 이미 밟은 함정, <see cref="ZoneInteractRing"/>), 라이팅을 안 받아 어두운 방에서도 색이 그대로다.
/// 색·텍스처는 <see cref="MaterialPropertyBlock"/> 으로 렌더러별로 덮는다(머티리얼 한 장 공유).
/// </summary>
public static class BossPatternVisuals
{
    static Material _mat;
    static Mesh _quad;
    static Texture2D _square, _disc, _crosshair;
    static MaterialPropertyBlock _mpb;

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int MainTexId = Shader.PropertyToID("_MainTex");

    public static Texture2D SquareTexture => _square != null ? _square : (_square = BuildSquare(64, 0.1f));
    public static Texture2D DiscTexture => _disc != null ? _disc : (_disc = BuildDisc(128, 0.08f));
    public static Texture2D CrosshairTexture => _crosshair != null ? _crosshair :
        (_crosshair = Resources.Load<Texture2D>("BossPatterns/WellsDroneCrosshair") ?? BuildCrosshair(128));

    static Material Mat
    {
        get
        {
            if (_mat != null) return _mat;
            _mat = new Material(Shader.Find("Sprites/Default")) { name = "BossPatternTemp" };
            _mat.renderQueue = 3100;   // 투명 바닥 위
            return _mat;
        }
    }

    // 로컬 XZ 평면, 위(+Y)를 보는 한 변 1 짜리 사각형.
    static Mesh Quad
    {
        get
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "BossPatternQuad" };
            _quad.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f),
            };
            _quad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _quad.RecalculateNormals();
            _quad.RecalculateBounds();
            return _quad;
        }
    }

    /// <summary>바닥에 눕힌 판 하나. 비활성으로 만든다.</summary>
    public static MeshRenderer CreateFlat(string name, Transform parent)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = Quad;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = Mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        go.SetActive(false);
        return r;
    }

    public static void Paint(Renderer r, Color color, Texture tex)
    {
        if (r == null) return;
        _mpb ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(_mpb);
        _mpb.SetColor(ColorId, color);
        if (tex != null) _mpb.SetTexture(MainTexId, tex);
        r.SetPropertyBlock(_mpb);
    }

    /// <summary>
    /// 기준 높이(fallback = 방 바닥 추정)에 <b>가장 가까운</b> 충돌면 높이. 못 찾으면 fallback.
    /// 가장 낮은 면을 고르면 바닥 아래 기초 메시·Terrain 에 깔려 안 보일 수 있다(Codex 교차검증 10-02).
    /// </summary>
    public static float SampleFloorY(Vector3 at, float fallback, float searchUp = 6f, float searchDown = 6f)
    {
        Vector3 origin = new Vector3(at.x, fallback + searchUp, at.z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, searchUp + searchDown, ~0, QueryTriggerInteraction.Ignore);
        float best = fallback, bestGap = float.PositiveInfinity;
        foreach (RaycastHit h in hits)
        {
            // 캐릭터(플레이어·보스)·송전기 위에 깔리지 않게 리지드바디 붙은 것은 건너뛴다.
            if (h.rigidbody != null) continue;
            float gap = Mathf.Abs(h.point.y - fallback);
            if (gap < bestGap) { bestGap = gap; best = h.point.y; }
        }
        return best;
    }

    // ── 절차 텍스처 ─────────────────────────────────────────────

    // 채운 면 + 또렷한 테두리(채움이 없으면 범위로 안 읽힌다 — AoeTelegraph 2026-09-09 팀장 판정과 같은 이유).
    static Texture2D BuildSquare(int size, float rim)
    {
        var tex = NewTex("BossPatternSquare", size);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));   // 0 = 테두리
                float a = edge < rim ? 1f : 0.55f;
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels32(px);
        tex.Apply(false, false);
        return tex;
    }

    static Texture2D BuildDisc(int size, float rim)
    {
        var tex = NewTex("BossPatternDisc", size);
        var px = new Color32[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt(Sq(x + 0.5f - half) + Sq(y + 0.5f - half)) / half;
                float a = d > 1f ? 0f : (d > 1f - rim ? 1f : 0.55f);
                a *= Mathf.Clamp01((1f - d) * size * 0.5f);   // 바깥 1px 페더
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels32(px);
        tex.Apply(false, false);
        return tex;
    }

    // 원 테두리 + 안쪽으로 들어오는 네 눈금.
    static Texture2D BuildCrosshair(int size)
    {
        var tex = NewTex("BossPatternCrosshair", size);
        var px = new Color32[size * size];
        float half = size * 0.5f;
        float ring = 0.86f, ringW = 0.07f, tickIn = 0.55f, tickW = 0.05f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                bool onRing = Mathf.Abs(d - ring) < ringW;
                bool onTick = d > tickIn && d < 1f &&
                              (Mathf.Abs(dx) < tickW || Mathf.Abs(dy) < tickW);
                px[y * size + x] = new Color(1f, 1f, 1f, onRing || onTick ? 1f : 0f);
            }
        tex.SetPixels32(px);
        tex.Apply(false, false);
        return tex;
    }

    static Texture2D NewTex(string name, int size) => new Texture2D(size, size, TextureFormat.RGBA32, false)
    {
        name = name,
        wrapMode = TextureWrapMode.Clamp,
        filterMode = FilterMode.Bilinear,
    };

    static float Sq(float v) => v * v;
}
