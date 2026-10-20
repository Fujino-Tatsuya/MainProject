using UnityEngine;

/// <summary>
/// 할 일 57 S4c1 — 런타임에서 지운 예전 CPU 데칼 텍스처 굽기의 원본(에디터 전용). 픽셀 비교 기준으로만 쓴다.
/// 런타임은 같은 그림을 SG_EnemyC3Decal 의 채움 모드(EnemyC3Decal.hlsl — 3 부채꼴 · 6 원판)로 그리고 텍스처를 만들지 않는다.
///  - <see cref="BuildArcTexture"/> = BossDirectionIndicator.BuildArcTexture 그대로(방향 표식 · S4a 공격 예고 채움의 기준)
///  - <see cref="BuildDiscTexture"/> = AoeTelegraph.BuildDiscTexture 그대로(장판 원판)
///  - <see cref="BuildSquare"/>·<see cref="BuildDisc"/>·<see cref="BuildCrosshair"/> = BossPatternVisuals 판·조준 그대로(S4c2,
///    런타임은 Monster/TelegraphQuad 의 _CodeShape 모드)
/// 🔴 식을 고치지 말 것 — 고치면 비교 기준이 함께 움직여 테스트가 아무것도 지키지 못한다.
/// </summary>
public static class LegacyDecalTextureReference
{
    /// <summary>
    /// 환형 섹터 알파 마스크. 반경·각도 경계를 몇 픽셀/도 만큼 부드럽게 떨어뜨려 계단을 없앤다.
    /// 🔴 각도 0 = +V(보스 정면). 메시 경로의 "+Z 기준 yaw(x = sin, z = cos)"와 같은 규약이다.
    /// </summary>
    /// <param name="rimWidth">
    /// 0 보다 크면 <b>윤곽선 모드</b> — 섹터 경계에서 이 거리(월드 단위, <paramref name="outer"/> 와 같은
    /// 축)만큼만 진하게 그리고 내부는 <paramref name="interiorAlphaScale"/> 배로 흐려진다.
    /// 0 이면 면 전체를 균일하게 칠한다(방향 표식이 쓰던 경로).
    /// </param>
    /// <param name="interiorAlphaScale">윤곽선 모드에서 내부에 남길 알파 비율(0 = 완전히 빈 내부).</param>
    /// <param name="reuse">같은 크기의 기존 텍스처가 있으면 새로 만들지 않고 덮어쓴다.</param>
    public static Texture2D BuildArcTexture(Color color, float halfAngleDeg, bool isBack,
                                            float inner, float outer, int size,
                                            float rimWidth = 0f, float interiorAlphaScale = 0f,
                                            Texture2D reuse = null)
    {
        Texture2D tex = reuse != null && reuse.width == size && reuse.height == size
            ? reuse
            : new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = isBack ? "BossBackArcDecal" : "BossFrontArcDecal",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

        float half = size * 0.5f;
        float radiusFeather = outer * (2f / size) * 2f;   // 약 2픽셀
        const float AngleFeather = 2f;                    // 도

        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = ((x + 0.5f) - half) / half;   // 로컬 +X = 보스 오른쪽
                float v = ((y + 0.5f) - half) / half;   // 로컬 +Y = 보스 정면
                float r = Mathf.Sqrt(u * u + v * v) * outer;

                float a = color.a;

                // 반경 밴드
                a *= Mathf.Clamp01((r - inner) / radiusFeather);
                a *= Mathf.Clamp01((outer - r) / radiusFeather);

                // 각도 섹터 — 정면 0°, 후방 ±180°
                float angle = Mathf.Abs(Mathf.Atan2(u, v) * Mathf.Rad2Deg);
                float edge = isBack ? (angle - (180f - halfAngleDeg)) : (halfAngleDeg - angle);
                a *= Mathf.Clamp01(edge / AngleFeather);

                // 윤곽선 모드(rimWidth > 0) — 섹터 경계만 진하게, 내부는 흐리게 한다.
                if (rimWidth > 0f && a > 0f)
                {
                    // 바깥 호까지의 거리 + 두 직선 모서리까지의 거리(호 길이 근사) 중 가까운 쪽.
                    float toOuterArc = outer - r;
                    float toAngularEdge = (edge <= 0f ? 0f : edge) * Mathf.Deg2Rad * r;
                    float toBoundary = Mathf.Min(toOuterArc, toAngularEdge);

                    float rim = Mathf.Clamp01(1f - toBoundary / rimWidth);
                    a *= Mathf.Max(interiorAlphaScale, rim);
                }

                pixels[y * size + x] = new Color(color.r, color.g, color.b, Mathf.Clamp01(a));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        return tex;
    }

    /// <summary>
    /// 장판 텍스처 — <b>채워진 면 + 또렷한 테두리</b>. 지름이 텍스처 폭에 꽉 차는 디스크(런타임은 size 128 로 불렀다).
    /// </summary>
    public static Texture2D BuildDiscTexture(Color color, int size, float fill, float rim)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "AoeDecalDisc",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        // 바깥 경계를 부드럽게 떨어뜨릴 폭(정규화 반경 기준) — 계단 제거용.
        float feather = 1f / (size * 0.05f);
        float rimW = Mathf.Clamp(rim, 0f, 0.5f);
        float fillA = Mathf.Clamp01(fill);

        float half = size * 0.5f;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) - half;
                float dy = (y + 0.5f) - half;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / half;   // 0 = 중심, 1 = 테두리

                // 내부는 fillA 로 균일하게 채우고, 테두리 밴드로 갈수록 1 까지 올린다.
                float rimA = rimW > 0f ? Mathf.Clamp01((d - (1f - rimW)) / rimW) : 0f;
                float a = Mathf.Max(fillA, rimA);

                // 바깥으로 나가는 순간은 페더로 끊는다.
                a *= Mathf.Clamp01((1f - d) / feather);

                pixels[y * size + x] = new Color(color.r, color.g, color.b, color.a * a);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        return tex;
    }

    // ── BossPatternVisuals 판 텍스처(할 일 57 S4c2) — 런타임은 Monster/TelegraphQuad 의 _CodeShape 1·2·3 으로 그린다.
    //    런타임은 SquareTexture = BuildSquare(64, 0.1) · DiscTexture = BuildDisc(128, 0.08) · CrosshairTexture = BuildCrosshair(128),
    //    테두리 비율별 캐시(SquareTextureFor·DiscTextureFor)는 비율을 0.005 단위로 묶어 같은 함수를 불렀다.

    // 채운 면 + 또렷한 테두리(채움이 없으면 범위로 안 읽힌다 — AoeTelegraph 2026-09-09 팀장 판정과 같은 이유).
    public static Texture2D BuildSquare(int size, float rim)
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

    public static Texture2D BuildDisc(int size, float rim)
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
    public static Texture2D BuildCrosshair(int size)
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
