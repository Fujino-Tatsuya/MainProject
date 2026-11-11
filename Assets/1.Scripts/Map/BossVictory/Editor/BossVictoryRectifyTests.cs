using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>배경 글자 RECTIFY(267 후속) — 배치 순수 함수·기본 클립·실제 폰트 아틀라스.</summary>
public class BossVictoryRectifyTests
{
    static BossVictoryRectifyText.Glyph G(float adv, float w, float h, float bx = 0f, float by = -1f) =>
        new BossVictoryRectifyText.Glyph { advance = adv, width = w, height = h, bearingX = bx, bearingY = by < 0f ? h : by, atlasRect = new Rect(10f, 20f, w, h) };

    [Test]
    public void 배치_전체_폭_가운데_대문자_높이_가운데()
    {
        var glyphs = new List<BossVictoryRectifyText.Glyph> { G(50f, 40f, 70f, 5f), G(50f, 40f, 70f, 5f) };
        var scr = new Vector4[BossVictoryRectifyText.MaxGlyphs];
        var uv = new Vector4[BossVictoryRectifyText.MaxGlyphs];
        int n = BossVictoryRectifyText.Layout(glyphs, 70f, 0f, new Vector2(512f, 512f), 1f, 0.5f, 2f, scr, uv);
        Assert.AreEqual(2, n);
        // 진행 합 100px = 화면 폭 1 → 0.01/px. 첫 글자 x = 0 + 5px = 0.05 ~ 0.45.
        Assert.AreEqual(0.05f, scr[0].x, 1e-5f);
        Assert.AreEqual(0.45f, scr[0].z, 1e-5f);
        Assert.AreEqual(0.55f, scr[1].x, 1e-5f, "두 번째 글자 = 진행 50px 뒤");
        // 세로 = 0.01 × 화면비 2 = 0.02/px. 대문자 70px → 높이 1.4, 가운데 0.5 → −0.2 ~ 1.2.
        Assert.AreEqual(-0.2f, scr[0].y, 1e-5f);
        Assert.AreEqual(1.2f, scr[0].w, 1e-5f);
        Assert.AreEqual(new Vector4(10f / 512f, 20f / 512f, 40f / 512f, 70f / 512f), uv[0]);
    }

    [Test]
    public void 배치_패딩은_화면_사각형과_UV_둘_다_넓힌다()
    {
        var glyphs = new List<BossVictoryRectifyText.Glyph> { G(100f, 80f, 70f, 10f) };
        var scr = new Vector4[BossVictoryRectifyText.MaxGlyphs];
        var uv = new Vector4[BossVictoryRectifyText.MaxGlyphs];
        BossVictoryRectifyText.Layout(glyphs, 70f, 12f, new Vector2(512f, 512f), 1.06f, 0.55f, 16f / 9f, scr, uv);
        Assert.AreEqual(0.5f - 0.53f + (10f - 12f) * 0.0106f, scr[0].x, 1e-5f);
        Assert.AreEqual((10f - 12f) / 512f, uv[0].x, 1e-6f);
        Assert.AreEqual((80f + 24f) / 512f, uv[0].z, 1e-6f);
        Assert.AreEqual(0.55f, (scr[0].y + 12f * 0.0106f * 16f / 9f + scr[0].w - 12f * 0.0106f * 16f / 9f) * 0.5f, 1e-4f, "대문자(=글리프) 가운데 = 0.55");
    }

    [Test]
    public void 외곽선_반두께_SDF()
    {
        Assert.AreEqual(70f * 0.04f * 0.5f / 13f, BossVictoryRectifyText.OutlineHalfWidthSdf(70f, 0.04f, 12f), 1e-6f);
    }

    [Test]
    public void 기본_클립은_0_3초부터_0_1초에_들어와_TV_꺼짐까지()
    {
        BossVictoryTimeline tl = BossVictoryTimeline.CreateDefault();
        Assert.AreEqual(1, tl.rectify.Count);
        Assert.AreEqual(0f, tl.Evaluate(BossVictoryTrack.Rectify, 0.29f), 1e-6f);
        Assert.AreEqual(1f, tl.Evaluate(BossVictoryTrack.Rectify, 0.41f), 0.01f, "0.1초 뒤 다 켜짐");
        Assert.AreEqual(1f, tl.Evaluate(BossVictoryTrack.Rectify, 4.49f), 0.01f);
        Assert.AreEqual(0f, tl.Evaluate(BossVictoryTrack.Rectify, 4.6f), 1e-6f);
        var old = new BossVictoryTimeline();
        Assert.IsTrue(old.SeedRectify());
        old.rectify.Clear();
        Assert.IsFalse(old.SeedRectify(), "지운 뒤 다시 안 넣음");
        var dst = BossVictoryTimeline.CreateDefault();
        dst.CopyClipFrom(tl, BossVictoryTrack.Rectify, 0);
        Assert.AreEqual(2, dst.rectify.Count, "복사 = 추가");
    }

    [Test]
    public void 설정_폰트로_RECTIFY_7글자_아틀라스()
    {
        BossVictoryGlitchSettings s = BossVictoryGlitchSettings.Load();
        Assert.IsNotNull(s);
        if (s.rectifyFont == null) Assert.Ignore("폰트(SVN Assets/50.Art/UI/Fonts/JalnanGothicTTF) 없음 — SVN 업데이트·.meta guid 확인");
        var glyphs = new List<BossVictoryRectifyText.Glyph>();
        TMPro.TMP_FontAsset asset = BossVictoryRectifyText.CreateAtlas(s.rectifyFont, s.rectifyText, glyphs, out float cap);
        try
        {
            Assert.IsNotNull(asset);
            Assert.AreEqual(7, glyphs.Count);
            Assert.Greater(cap, 20f);
            foreach (BossVictoryRectifyText.Glyph g in glyphs) Assert.Greater(g.atlasRect.width, 0f);
        }
        finally { BossVictoryRectifyText.Destroy(asset); }
    }

    // ── 안쪽 색 — Ring Wipe Fill(267 후속) ──
    const float Delay = 4f / 60f, Fade = 0.15f, Expand = 0.6f;

    [Test]
    public void 링_생성은_간격마다_페이드와_확장이_동시_easeOutCubic()
    {
        Assert.IsFalse(BossVictoryRectifyFill.Evaluate(0, -0.01f, Delay, Fade, Expand).exists, "클립 전 없음");
        Assert.IsFalse(BossVictoryRectifyFill.Evaluate(1, Delay - 0.001f, Delay, Fade, Expand).exists, "링 1 은 4프레임 뒤");
        BossVictoryRectifyFill.Ring r = BossVictoryRectifyFill.Evaluate(1, Delay + 0.075f, Delay, Fade, Expand);
        Assert.IsTrue(r.exists);
        Assert.AreEqual(0.5f, r.alpha, 1e-4f, "페이드 반");
        Assert.AreEqual(1f - Mathf.Pow(1f - 0.075f / Expand, 3f), r.grow, 1e-5f, "페이드 동안에도 확장");
        r = BossVictoryRectifyFill.Evaluate(2, 2f * Delay + Expand * 0.5f, Delay, Fade, Expand);
        Assert.AreEqual(0.875f, r.grow, 1e-5f, "easeOutCubic(0.5) = 0.875");
        r = BossVictoryRectifyFill.Evaluate(2, 2f * Delay + Expand + 0.01f, Delay, Fade, Expand);
        Assert.AreEqual(1f, r.grow, 1e-6f);
        Assert.AreEqual(1f, r.alpha, 1e-6f);
        Assert.IsTrue(BossVictoryRectifyFill.Evaluate(2, 99f, Delay, Fade, Expand).exists, "마지막 링 = 끝까지 유지(반복 없음)");
        Assert.AreEqual(3, BossVictoryRectifyFill.RingCount);
    }

    [Test]
    public void 링_Rmax_는_중심에서_단어_사각형_네_모서리까지_종횡비_보정()
    {
        var rects = new[] { new Vector4(0.1f, 0.4f, 0.3f, 0.6f), new Vector4(0.25f, 0.42f, 0.9f, 0.58f) };
        Vector4 w = BossVictoryRectifyFill.WordRect(rects, 2);
        Assert.AreEqual(new Vector4(0.1f, 0.4f, 0.9f, 0.6f), w);
        float aspect = BossVictoryRectifyFill.WordAspect(w, 16f / 9f);
        Assert.AreEqual(0.8f * 16f / 9f / 0.2f, aspect, 1e-4f);
        float rMax = BossVictoryRectifyFill.MaxRadius(new Vector2(0.46f, 0.5f), aspect);
        Assert.AreEqual(Mathf.Sqrt(0.54f * aspect * 0.54f * aspect + 0.25f), rMax, 1e-4f, "먼 쪽(오른쪽) 모서리");
        Assert.AreEqual(rMax + 0.01f, BossVictoryRectifyFill.Radius(1f, rMax, 0.01f), 1e-6f, "다 퍼지면 경계까지 덮음");
        Assert.AreEqual(0f, BossVictoryRectifyFill.Radius(0f, rMax, 0.01f), 1e-6f);
        Assert.AreEqual(Vector4.zero, BossVictoryRectifyFill.WordRect(rects, 0));
    }

    [Test]
    public void 링_기본_설정은_사양값()
    {
        BossVictoryGlitchSettings s = BossVictoryGlitchSettings.Load();
        Assert.IsNotNull(s);
        Assert.AreEqual(3, s.rectifyFillColors.Length);
        Assert.Less(Vector4.Distance((Color)new Color32(0xFF, 0x2D, 0xB4, 0xFF), s.rectifyFillColors[0]), 1e-4f, "마젠타");
        Assert.Less(Vector4.Distance((Color)new Color32(0x8A, 0x8A, 0x8A, 0xFF), s.rectifyFillColors[2]), 1e-4f, "그레이");
        Assert.AreEqual(0.46f, s.rectifyFillCenter.x, 1e-5f);
        Assert.AreEqual(4f / 60f, s.rectifyFillDelay, 1e-5f);
        Assert.AreEqual(0.15f, s.rectifyFillFade, 1e-5f);
        Assert.AreEqual(0.6f, s.rectifyFillExpand, 1e-5f);
        Assert.AreEqual(0.35f, s.rectifyFillGradient, 1e-5f);
    }

    [Test]
    public void 안쪽색_기본_클립은_0_4초부터_4_5초까지_시작_기준_시간()
    {
        BossVictoryTimeline tl = BossVictoryTimeline.CreateDefault();
        Assert.AreEqual(1, tl.rectifyFill.Count);
        Assert.IsFalse(tl.TryActiveClip(BossVictoryTrack.RectifyFill, 0.39f, out _));
        Assert.IsTrue(tl.TryActiveClip(BossVictoryTrack.RectifyFill, 1f, out BossVictoryClip c));
        Assert.AreEqual(0.4f, c.start, 1e-6f);
        Assert.AreEqual(4.5f, c.End, 1e-5f);
        Assert.AreEqual(1f, c.Evaluate(1f), 1e-6f);
        Assert.IsFalse(tl.TryActiveClip(BossVictoryTrack.RectifyFill, 4.6f, out _));
        var old = new BossVictoryTimeline();
        Assert.IsTrue(old.SeedRectifyFill());
        old.rectifyFill.Clear();
        Assert.IsFalse(old.SeedRectifyFill(), "지운 뒤 다시 안 넣음");
        var dst = BossVictoryTimeline.CreateDefault();
        dst.CopyClipFrom(tl, BossVictoryTrack.RectifyFill, 0);
        Assert.AreEqual(2, dst.rectifyFill.Count, "복사 = 추가");
        Assert.AreEqual("RECTIFY 안쪽 색", BossVictoryTimeline.TrackLabel(BossVictoryTrack.RectifyFill));
    }
}
