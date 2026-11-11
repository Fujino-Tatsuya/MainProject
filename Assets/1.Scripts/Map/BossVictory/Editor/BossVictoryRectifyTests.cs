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

    // ── 안쪽 색 — 원형 확장 색상 전환(267 후속) ──
    const float Fade = 0.08f, Expand = 0.35f, NextAt = 0.6f;

    [Test]
    public void 안쪽색_주기는_페이드_더하기_확장_곱하기_다음원비율()
    {
        Assert.AreEqual(0.29f, BossVictoryRectifyFill.Period(Fade, Expand, NextAt), 1e-5f);
        Assert.AreEqual(-1, BossVictoryRectifyFill.Latest(-0.01f, 0.29f));
        Assert.AreEqual(0, BossVictoryRectifyFill.Latest(0f, 0.29f));
        Assert.AreEqual(0, BossVictoryRectifyFill.Latest(0.28f, 0.29f));
        Assert.AreEqual(3, BossVictoryRectifyFill.Latest(0.88f, 0.29f));
        Assert.AreEqual(0.01f, BossVictoryRectifyFill.Period(0f, 0f, 0f), 1e-6f, "최소 간격");
    }

    [Test]
    public void 안쪽색_원은_페이드_뒤_확장_ease_out_색은_차례로_반복()
    {
        BossVictoryRectifyFill.Circle c = BossVictoryRectifyFill.Evaluate(1, 0.2f, Fade, Expand, NextAt, 5);
        Assert.IsFalse(c.exists, "원 1 은 0.29초에 생김");
        c = BossVictoryRectifyFill.Evaluate(1, 0.29f + 0.04f, Fade, Expand, NextAt, 5);
        Assert.IsTrue(c.exists);
        Assert.AreEqual(0.5f, c.alpha, 1e-4f, "페이드 반");
        Assert.AreEqual(0f, c.grow, 1e-6f, "페이드 동안 크기 그대로");
        c = BossVictoryRectifyFill.Evaluate(1, 0.29f + Fade + Expand * 0.5f, Fade, Expand, NextAt, 5);
        Assert.AreEqual(1f, c.alpha, 1e-6f);
        Assert.Greater(c.grow, 0.5f, "ease-out — 반 시간에 반보다 더 감");
        Assert.Less(c.grow, 1f);
        c = BossVictoryRectifyFill.Evaluate(1, 0.29f + Fade + Expand + 0.01f, Fade, Expand, NextAt, 5);
        Assert.AreEqual(1f, c.grow, 1e-6f);
        for (int k = 0; k < 12; k++)
            Assert.AreEqual(k % 5, BossVictoryRectifyFill.Evaluate(k, 10f, Fade, Expand, NextAt, 5).colorIndex);
        Assert.IsFalse(BossVictoryRectifyFill.Evaluate(-1, 1f, Fade, Expand, NextAt, 5).exists);
    }

    [Test]
    public void 안쪽색_앞앞_원은_지금_원이_생길_때_이미_다_커져_바탕이_된다()
    {
        float period = BossVictoryRectifyFill.Period(Fade, Expand, NextAt);
        for (int latest = 2; latest < 10; latest++)
        {
            float t = latest * period;   // 지금 원이 막 생긴 순간
            BossVictoryRectifyFill.Circle base2 = BossVictoryRectifyFill.Evaluate(latest - 2, t, Fade, Expand, NextAt, 5);
            Assert.AreEqual(1f, base2.grow, 1e-6f);
            Assert.AreEqual(1f, base2.alpha, 1e-6f);
        }
    }

    [Test]
    public void 안쪽색_글자_점은_시드로_고정되고_범위_안()
    {
        for (int k = 0; k < 50; k++)
        {
            int g = BossVictoryRectifyFill.GlyphFor(k, 267, 7);
            Assert.That(g, Is.InRange(0, 6));
            Assert.AreEqual(g, BossVictoryRectifyFill.GlyphFor(k, 267, 7), "같은 시드 = 같은 글자");
            Vector2 p = BossVictoryRectifyFill.PointInGlyph(k, 267);
            Assert.That(p.x, Is.InRange(0.3f, 0.7f));
            Assert.That(p.y, Is.InRange(0.25f, 0.75f));
            Assert.AreEqual(p, BossVictoryRectifyFill.PointInGlyph(k, 267));
        }
        Assert.AreEqual(0, BossVictoryRectifyFill.GlyphFor(3, 267, 0));
        Assert.AreEqual(0.15f, BossVictoryRectifyFill.Radius(0f, 0.15f, 2f), 1e-6f);
        Assert.AreEqual(2f, BossVictoryRectifyFill.Radius(1f, 0.15f, 2f), 1e-6f);
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
