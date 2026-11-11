using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// 처치 연출 배경 글자 'RECTIFY'(267 후속) — 글자 배치(순수 함수)와 SDF 글리프 준비.
/// 글리치 합성 셰이더가 글자마다 화면 사각형·아틀라스 UV 를 받아 SDF 외곽선만 그리고, 플레이어·보스 마스크가 0 인 곳에만 섞는다
/// → 인물·보스 뒤, 벽·바닥(배경) 앞. 폰트 = 설정 SO <c>rectifyFont</c>(SVN Assets/50.Art/UI/Fonts/JalnanGothicTTF).
/// 아틀라스는 켤 때 한 번 만든다(글자 7개 SDF, 512² Alpha8) — 끌 때 버린다.
/// </summary>
public static class BossVictoryRectifyText
{
    public const int MaxGlyphs = 8;
    public const int SamplingPointSize = 96;
    public const int AtlasPadding = 12;
    public const int AtlasSize = 512;

    /// <summary>글리프 하나 — 아틀라스 px 단위(샘플링 크기 기준).</summary>
    public struct Glyph
    {
        public float advance, bearingX, bearingY, width, height;
        public Rect atlasRect;   // 아틀라스 px(왼쪽 아래 원점, 패딩 제외)
    }

    /// <summary>
    /// 글자 배치 — 전체 진행 폭 = 화면 너비 × <paramref name="widthFrac"/>(1 넘으면 양끝이 잘림), 가로 가운데,
    /// 대문자 높이 가운데 = 뷰포트 <paramref name="centerY"/>(0 아래 · 1 위). 결과 = 글자마다 화면 사각형(뷰포트, 패딩 포함 — xy 최소 zw 최대)과
    /// 아틀라스 UV 사각형(xy 시작 zw 크기, 같은 패딩). <paramref name="aspect"/> = 화면 너비/높이. 개수 = 글리프 수(최대 <see cref="MaxGlyphs"/>).
    /// </summary>
    public static int Layout(IReadOnlyList<Glyph> glyphs, float capHeight, float padding, Vector2 atlasSize,
        float widthFrac, float centerY, float aspect, Vector4[] screenRects, Vector4[] uvRects)
    {
        int n = Mathf.Min(glyphs.Count, MaxGlyphs);
        float total = 0f;
        for (int i = 0; i < n; i++) total += glyphs[i].advance;
        if (n == 0 || total <= 0f) return 0;
        float sx = widthFrac / total;            // 뷰포트 x / 아틀라스 px
        float sy = sx * Mathf.Max(0.01f, aspect); // 뷰포트 y / 아틀라스 px(정사각 픽셀)
        float cursor = 0.5f - widthFrac * 0.5f;
        float baseline = centerY - capHeight * 0.5f * sy;
        for (int i = 0; i < n; i++)
        {
            Glyph g = glyphs[i];
            screenRects[i] = new Vector4(
                cursor + (g.bearingX - padding) * sx,
                baseline + (g.bearingY - g.height - padding) * sy,
                cursor + (g.bearingX + g.width + padding) * sx,
                baseline + (g.bearingY + padding) * sy);
            uvRects[i] = new Vector4(
                (g.atlasRect.x - padding) / atlasSize.x, (g.atlasRect.y - padding) / atlasSize.y,
                (g.atlasRect.width + 2f * padding) / atlasSize.x, (g.atlasRect.height + 2f * padding) / atlasSize.y);
            cursor += g.advance * sx;
        }
        return n;
    }

    /// <summary>외곽선 반두께(SDF 값 단위) — 두께 = 대문자 높이 × <paramref name="outline"/>. SDF 거리(px) = (값 − 0.5) × (패딩 + 1).</summary>
    public static float OutlineHalfWidthSdf(float capHeight, float outline, float padding) =>
        Mathf.Max(0f, capHeight * outline * 0.5f) / (padding + 1f);

    /// <summary>폰트에서 글자 SDF 아틀라스를 만든다. 실패하면 null(폰트 없음·글자 없음).</summary>
    public static TMP_FontAsset CreateAtlas(Font font, string text, List<Glyph> glyphs, out float capHeight)
    {
        glyphs.Clear();
        capHeight = 0f;
        if (font == null || string.IsNullOrEmpty(text)) return null;
        TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font, SamplingPointSize, AtlasPadding, GlyphRenderMode.SDFAA,
            AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, false);
        if (asset == null) return null;
        asset.name = "BossVictoryRectify (runtime)";
        asset.hideFlags = HideFlags.DontSave;
        asset.TryAddCharacters(text);
        for (int i = 0; i < text.Length && glyphs.Count < MaxGlyphs; i++)
        {
            if (!asset.characterLookupTable.TryGetValue(text[i], out TMP_Character ch) || ch.glyph == null) continue;
            var m = ch.glyph.metrics;
            var r = ch.glyph.glyphRect;
            glyphs.Add(new Glyph
            {
                advance = m.horizontalAdvance, bearingX = m.horizontalBearingX, bearingY = m.horizontalBearingY,
                width = m.width, height = m.height, atlasRect = new Rect(r.x, r.y, r.width, r.height),
            });
            capHeight = Mathf.Max(capHeight, m.horizontalBearingY);
        }
        if (glyphs.Count == 0) { Destroy(asset); return null; }
        return asset;
    }

    public static void Destroy(TMP_FontAsset asset)
    {
        if (asset == null) return;
        if (asset.atlasTextures != null)
            foreach (Texture2D t in asset.atlasTextures) if (t != null) Kill(t);
        if (asset.material != null) Kill(asset.material);
        Kill(asset);
    }

    static void Kill(Object o) { if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }
}
