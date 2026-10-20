using UnityEngine;

/// <summary>
/// 전기 장판 · 자폭 드론의 <b>임시 연출</b> 재료 — 바닥에 눕힌 사각/원 면, 크로스헤어 판.
/// 판정과 무관한 전 피어 로컬 연출. 민경 VFX 가 오면 데이터 SO 의 프리팹 슬롯으로 갈아끼운다.
///
/// 셰이더는 <c>Sprites/Default</c> — URP 에서 빌트인 Default-Material 은 자홍색으로 깨지고
/// (이 프로젝트에서 이미 밟은 함정, 예전 존 바닥 링 ZoneInteractRing — 10-06 삭제), 라이팅을 안 받아 어두운 방에서도 색이 그대로다.
/// 색·텍스처는 <see cref="MaterialPropertyBlock"/> 으로 렌더러별로 덮는다(머티리얼 한 장 공유).
/// 코드로 그린 기본 모양(사각·원판·조준)은 텍스처 없이 Monster/TelegraphQuad 의 _CodeShape 모드로 그린다(할 일 57 S4c2 —
/// 예전엔 64²/128² 텍스처를 런타임에 구웠다. 원본 식 = Editor/LegacyDecalTextureReference).
/// </summary>
public static class BossPatternVisuals
{
    static Material _mat;
    static Mesh _quad;
    static MaterialPropertyBlock _mpb;

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    static readonly int CodeShapeId = Shader.PropertyToID("_CodeShape");

    /// <summary>코드로 그린 판 모양 — 값 = TelegraphQuad 의 _CodeShape.x(0 = _MainTex 텍스처).</summary>
    public enum CodeShape { Square = 1, Disc = 2, Crosshair = 3 }

    /// <summary>기본 테두리 비율(예전 SquareTexture·DiscTexture).</summary>
    public const float SquareRim = 0.1f, DiscRim = 0.08f;

    static Material Mat
    {
        get
        {
            if (_mat != null) return _mat;
            // 🎨 예고 판 셰이더(Monster/TelegraphQuad) — Sprites/Default 와 같은 블렌드·속성명 + 색·마스크(PLAN-telegraph-style).
            //    Resources 재질로 불러 빌드에 셰이더가 항상 들어가게 한다. 없으면 예전 Sprites/Default(마스크 없음).
            Material source = Resources.Load<Material>("Telegraph/MA_TelegraphQuad");
            _mat = source != null
                ? new Material(source) { name = "BossPatternTelegraph" }
                : new Material(Shader.Find("Sprites/Default")) { name = "BossPatternTemp" };
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

    /// <summary>텍스처 모양으로 칠한다. <paramref name="tex"/> 가 null 이면 모양(텍스처·코드 모양)은 그대로 두고 색만 바꾼다.</summary>
    public static void Paint(Renderer r, Color color, Texture tex)
    {
        if (r == null) return;
        _mpb ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(_mpb);
        _mpb.SetColor(ColorId, color);
        if (tex != null)
        {
            _mpb.SetTexture(MainTexId, tex);
            _mpb.SetVector(CodeShapeId, Vector4.zero);
        }
        // 마스크는 끈다 — 같은 판을 예고(마스크 있음) → 발동 표시(마스크 없음)로 재사용할 때 남지 않게. 스타일은 PaintStyled.
        _mpb.SetVector(MaskParamsId, new Vector4(0f, 0f, 1f, 0f));
        r.SetPropertyBlock(_mpb);
    }

    /// <summary>코드 모양으로 칠한다(텍스처 없음). 테두리 비율은 사각·원판만 — 생략하면 <see cref="SquareRim"/>·<see cref="DiscRim"/>.</summary>
    public static void Paint(Renderer r, Color color, CodeShape shape, float rimRatio = -1f)
    {
        if (r == null) return;
        _mpb ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(_mpb);
        _mpb.SetColor(ColorId, color);
        _mpb.SetVector(CodeShapeId, new Vector4((float)shape, RimFor(shape, rimRatio), 0f, 0f));
        _mpb.SetVector(MaskParamsId, new Vector4(0f, 0f, 1f, 0f));
        r.SetPropertyBlock(_mpb);
    }

    // 테두리 비율은 0.005 단위로 묶는다 — 예전 비율별 텍스처 캐시(DiscTextureFor·SquareTextureFor)와 같은 값.
    static float RimFor(CodeShape shape, float rimRatio)
    {
        if (shape == CodeShape.Crosshair) return 0f;
        if (rimRatio < 0f) return shape == CodeShape.Square ? SquareRim : DiscRim;
        return Mathf.RoundToInt(Mathf.Clamp(rimRatio, 0.005f, 0.5f) * 200f) / 200f;
    }

    static readonly int MaskTexId = Shader.PropertyToID("_MaskTex");
    static readonly int MaskParamsId = Shader.PropertyToID("_MaskParams");
    static readonly int MaskOriginId = Shader.PropertyToID("_MaskOrigin");
    static readonly int MaskAxesId = Shader.PropertyToID("_MaskAxes");
    static readonly int MaskExtentId = Shader.PropertyToID("_MaskExtent");

    /// <summary>
    /// 스타일 층(색 + 마스크)으로 칠한다(PLAN-telegraph-style). 마스크 좌표계 = <paramref name="origin"/> 기준,
    /// <paramref name="right"/>·<paramref name="forward"/> 축, 늘이기 기준 크기 <paramref name="extent"/>(m).
    /// 🔴 origin 은 **판 자신의 위치가 아니라 예고 전체의 기준점**을 넘긴다 — 채움 판은 크기를 키우며 자라서,
    ///    판 기준이면 패턴이 같이 커지며 미끄러진다.
    /// </summary>
    public static void PaintStyled(Renderer r, TelegraphLayer layer, Texture shape,
                                   Vector3 origin, Vector3 right, Vector3 forward, Vector2 extent)
    {
        if (r == null || layer == null) return;
        Paint(r, layer.color, shape);
        ApplyMask(r, layer.mask, origin, right, forward, extent);
    }

    /// <summary>코드 모양(텍스처 없음) + 스타일 층. 테두리 비율은 <see cref="Paint(Renderer, Color, CodeShape, float)"/>, 마스크 좌표는 위와 같다.</summary>
    public static void PaintStyled(Renderer r, TelegraphLayer layer, CodeShape shape, float rimRatio,
                                   Vector3 origin, Vector3 right, Vector3 forward, Vector2 extent)
    {
        if (r == null || layer == null) return;
        Paint(r, layer.color, shape, rimRatio);
        ApplyMask(r, layer.mask, origin, right, forward, extent);
    }

    static void ApplyMask(Renderer r, TelegraphMask mask, Vector3 origin, Vector3 right, Vector3 forward, Vector2 extent)
    {
        bool on = mask != null && mask.Active;
        r.GetPropertyBlock(_mpb);
        _mpb.SetTexture(MaskTexId, on ? mask.texture : Texture2D.whiteTexture);
        _mpb.SetVector(MaskParamsId, new Vector4(
            on ? (float)mask.mode : 0f, on ? mask.strength : 0f,
            on ? mask.tileSize : 1f, on ? mask.rotation * Mathf.Deg2Rad : 0f));
        _mpb.SetVector(MaskOriginId, new Vector4(origin.x, origin.y, origin.z, 0f));
        _mpb.SetVector(MaskAxesId, new Vector4(right.x, right.z, forward.x, forward.z));
        _mpb.SetVector(MaskExtentId, new Vector4(Mathf.Max(0.01f, extent.x), Mathf.Max(0.01f, extent.y), 0f, 0f));
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
}
