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

    // ── 안쪽 원 3개(267 후속 — 원 통합) ──
    const float Fade = 0.15f, Expand = 0.6f, GrayToWhite = 0.1f;

    [Test]
    public void 원은_자기_생성_시각에_시작_반지름으로_생겨_페이드한_뒤_easeOutCubic_확장()
    {
        const float start = 4f / 60f;
        Assert.IsFalse(BossVictoryRectifyFill.Circle(-0.01f, 0f, Fade, Expand).exists, "클립 전 없음");
        Assert.IsFalse(BossVictoryRectifyFill.Circle(start - 0.001f, start, Fade, Expand).exists, "생성 시각 전 없음");
        BossVictoryRectifyFill.Ring r = BossVictoryRectifyFill.Circle(start + 0.075f, start, Fade, Expand);
        Assert.IsTrue(r.exists);
        Assert.AreEqual(0.5f, r.alpha, 1e-4f, "페이드 반");
        Assert.AreEqual(0f, r.grow, 1e-6f, "페이드 동안 크기 그대로");
        r = BossVictoryRectifyFill.Circle(start + Fade, start, Fade, Expand);
        Assert.AreEqual(1f, r.alpha, 1e-5f);
        Assert.AreEqual(1f, r.white, 1e-6f, "전환 0 = 페이드 끝에 바로");
        Assert.AreEqual(0f, r.grow, 1e-5f, "불투명해진 순간 확장 시작");
        r = BossVictoryRectifyFill.Circle(start + Fade + Expand * 0.5f, start, Fade, Expand);
        Assert.AreEqual(0.875f, r.grow, 1e-5f, "easeOutCubic(0.5) = 0.875");
        Assert.AreEqual(1f, BossVictoryRectifyFill.Circle(99f, start, Fade, Expand).grow, 1e-6f, "끝까지 유지(반복 없음)");
        Assert.AreEqual(3, BossVictoryRectifyFill.CircleCount);
        Assert.AreEqual(0.15f, BossVictoryRectifyFill.Radius(0f, 0.15f, 3f, 0.01f), 1e-6f, "생성 = 시작 반지름");
        Assert.AreEqual(3.01f, BossVictoryRectifyFill.Radius(1f, 0.15f, 3f, 0.01f), 1e-5f, "다 퍼지면 Rmax + 경계");
    }

    [Test]
    public void 흰_원은_회색으로_페이드_뒤_흰색으로_바뀐_순간부터_확장()
    {
        const float start = 0.2f;
        BossVictoryRectifyFill.Ring w = BossVictoryRectifyFill.Circle(start + 0.075f, start, Fade, Expand, GrayToWhite);
        Assert.AreEqual(0.5f, w.alpha, 1e-4f, "회색으로 페이드");
        Assert.AreEqual(0f, w.white, 1e-6f);
        w = BossVictoryRectifyFill.Circle(start + Fade + GrayToWhite * 0.5f, start, Fade, Expand, GrayToWhite);
        Assert.AreEqual(1f, w.alpha, 1e-5f, "불투명한 회색");
        Assert.AreEqual(0.5f, w.white, 1e-4f, "회색 → 흰 전환 반");
        Assert.AreEqual(0f, w.grow, 1e-6f, "전환 동안 크기 그대로");
        w = BossVictoryRectifyFill.Circle(start + Fade + GrayToWhite, start, Fade, Expand, GrayToWhite);
        Assert.AreEqual(1f, w.white, 1e-6f);
        Assert.AreEqual(0f, w.grow, 1e-5f, "흰색이 된 순간 확장 시작");
        w = BossVictoryRectifyFill.Circle(start + Fade + GrayToWhite + Expand * 0.5f, start, Fade, Expand, GrayToWhite);
        Assert.AreEqual(0.875f, w.grow, 1e-5f);
    }

    [Test]
    public void 원_색은_왼쪽에서_오른쪽으로_보간()
    {
        Color l = new Color(1f, 0.2f, 0.7f), r = new Color(0.6f, 0.25f, 1f);
        Assert.AreEqual(l, BossVictoryRectifyFill.CircleColor(0f, l, r));
        Assert.AreEqual(r, BossVictoryRectifyFill.CircleColor(1f, l, r));
        Assert.AreEqual(0.8f, BossVictoryRectifyFill.CircleColor(0.5f, l, r).r, 1e-5f);
        Assert.AreEqual(r, BossVictoryRectifyFill.CircleColor(2f, l, r), "단어 밖은 끝 색");
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
        Assert.AreEqual(Vector4.zero, BossVictoryRectifyFill.WordRect(rects, 0));
    }

    [Test]
    public void 원_코드_기본값은_사양값()
    {
        // 에셋 값은 사용자가 튜닝하므로 코드 기본값만 고정한다.
        var s = ScriptableObject.CreateInstance<BossVictoryGlitchSettings>();
        try
        {
            Assert.AreEqual(3, s.rectifyCircles.Length);
            Assert.Less(Vector4.Distance((Color)new Color32(0xFF, 0x2D, 0xB4, 0xFF), s.rectifyCircles[0].leftColor), 1e-4f, "① 왼쪽 마젠타");
            Assert.Less(Vector4.Distance((Color)new Color32(0xA0, 0x40, 0xFF, 0xFF), s.rectifyCircles[0].rightColor), 1e-4f, "① 오른쪽 보라");
            Assert.Less(Vector4.Distance((Color)new Color32(0x5A, 0xC8, 0xFF, 0xFF), s.rectifyCircles[1].leftColor), 1e-4f, "② 왼쪽 하늘");
            Assert.Less(Vector4.Distance((Color)new Color32(0x24, 0x38, 0xA8, 0xFF), s.rectifyCircles[1].rightColor), 1e-4f, "② 오른쪽 남색");
            Assert.AreEqual(Color.white, s.rectifyCircles[2].leftColor, "③ 왼쪽 흰색");
            Assert.Less(Vector4.Distance((Color)new Color32(0xFF, 0xF6, 0xE0, 0xFF), s.rectifyCircles[2].rightColor), 1e-4f, "③ 오른쪽 연크림");
            Assert.AreEqual(0f, s.rectifyCircles[0].start, 1e-6f);
            Assert.AreEqual(4f / 60f, s.rectifyCircles[1].start, 1e-5f);
            Assert.AreEqual(0.2f, s.rectifyCircles[2].start, 1e-5f);
            Assert.AreEqual(0.46f, s.rectifyCircles[2].center.x, 1e-5f);
            Assert.AreEqual(0.15f, s.rectifyCircles[2].startRadius, 1e-5f);
            Assert.Less(Vector4.Distance((Color)new Color32(0x8A, 0x8A, 0x8A, 0xFF), s.rectifyCircleGrayColor), 1e-4f, "③ 회색");
            Assert.AreEqual(0.1f, s.rectifyCircleGrayToWhite, 1e-6f);
            Assert.IsFalse(s.MigrateRectifyCircles(), "새 인스턴스 = 옛 값 없음 → 옮기지 않음");
            Assert.AreEqual(0.6f, s.skyAmberMinLuma, 1e-5f);
        }
        finally { Object.DestroyImmediate(s); }
    }

    [Test]
    public void 옛_링_흰원_값은_원_3개로_한_번만_옮긴다()
    {
        var s = ScriptableObject.CreateInstance<BossVictoryGlitchSettings>();
        try
        {
            Color magenta = new Color(1f, 0.18f, 0.7f), blue = new Color(0.23f, 0.42f, 1f), gray = new Color(0.4f, 0.4f, 0.4f);
            s.rectifyFillColors = new[] { magenta, blue, gray };
            s.rectifyFillCenter = new Vector2(0.4f, 0.55f);
            s.rectifyFillStartRadius = 0.12f;
            s.rectifyFillDelay = 0.1f;
            s.rectifyFillFade = 0.2f;
            s.rectifyFillExpand = 0.5f;
            s.rectifyWhiteColor = new Color(1f, 1f, 0.9f);
            s.rectifyWhiteStart = 0.3f;
            s.rectifyWhiteCenter = new Vector2(0.5f, 0.45f);
            s.rectifyWhiteStartRadius = 0.18f;
            s.rectifyWhiteFade = 0.15f;
            s.rectifyWhiteExpand = 0.1f;
            Assert.IsTrue(s.MigrateRectifyCircles());
            BossVictoryGlitchSettings.RectifyCircle[] c = s.rectifyCircles;
            Assert.AreEqual(3, c.Length);
            Assert.AreEqual(0f, c[0].start);
            Assert.AreEqual(magenta, c[0].leftColor, "① = 옛 링 0 색");
            Assert.AreEqual(0.12f, c[0].startRadius, 1e-6f);
            Assert.AreEqual(new Vector2(0.4f, 0.55f), c[0].center);
            Assert.AreEqual(0.1f, c[1].start, 1e-6f, "② 생성 = 옛 간격");
            Assert.AreEqual(0.2f, c[1].fade, 1e-6f);
            Assert.AreEqual(0.5f, c[1].expand, 1e-6f);
            Assert.AreEqual(0.3f, c[2].start, 1e-6f, "③ = 옛 흰 원");
            Assert.AreEqual(0.18f, c[2].startRadius, 1e-6f);
            Assert.AreEqual(0.1f, c[2].expand, 1e-6f);
            Assert.AreEqual(new Vector2(0.5f, 0.45f), c[2].center);
            Assert.AreEqual(new Color(1f, 1f, 0.9f), c[2].leftColor);
            Assert.AreEqual(gray, s.rectifyCircleGrayColor, "회색 = 옛 링 2 색");
            Assert.AreEqual(0, s.rectifyFillColors.Length, "옛 칸 비움");
            s.rectifyFillColors = new[] { blue };
            Assert.IsFalse(s.MigrateRectifyCircles(), "한 번만");
        }
        finally { Object.DestroyImmediate(s); }
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

    [Test]
    public void 주황은_흰_원이_다_퍼지고_머문_뒤_와이프()
    {
        float start = BossVictoryRectifyFill.OrangeStart(0.2f, 0.15f, 0.1f, 0.5f, 0.05f);   // 흰 원 생성 + 페이드 + 회색→흰 + 확장 + 유지
        Assert.AreEqual(1.0f, start, 1e-5f);
        Assert.AreEqual(0f, BossVictoryRectifyFill.OrangeProgress(0.99f, start, 0.1f));
        Assert.AreEqual(0.5f, BossVictoryRectifyFill.OrangeProgress(1.05f, start, 0.1f), 1e-4f);
        Assert.AreEqual(1f, BossVictoryRectifyFill.OrangeProgress(3f, start, 0.1f), "끝까지 주황 유지");
        Assert.AreEqual(1f, BossVictoryRectifyFill.OrangeProgress(1f, start, 0f), "와이프 0초 = 바로");
    }

    [Test]
    public void 와이프는_진행_0이면_없고_1이면_전부_줄마다_어긋남()
    {
        for (float off = -0.5f; off <= 0.5f; off += 0.25f)
            for (float x = 0f; x <= 1f; x += 0.1f)
            {
                Assert.IsFalse(BossVictoryRectifyFill.WipeCovered(x, 0f, 0.2f, off, true), $"진행 0 x={x} off={off}");
                Assert.IsTrue(BossVictoryRectifyFill.WipeCovered(x, 1f, 0.2f, off, true), $"진행 1 x={x} off={off}");
            }
        Assert.IsTrue(BossVictoryRectifyFill.WipeCovered(0.95f, 0.3f, 0.2f, 0f, true), "오른쪽→왼쪽 — 오른쪽 먼저");
        Assert.IsFalse(BossVictoryRectifyFill.WipeCovered(0.05f, 0.3f, 0.2f, 0f, true));
        Assert.IsTrue(BossVictoryRectifyFill.WipeCovered(0.05f, 0.3f, 0.2f, 0f, false), "왼쪽→오른쪽 — 왼쪽 먼저");
        Assert.AreNotEqual(BossVictoryRectifyFill.WipeCovered(0.5f, 0.5f, 0.2f, -0.5f, true),
            BossVictoryRectifyFill.WipeCovered(0.5f, 0.5f, 0.2f, 0.5f, true), "줄마다 경계 어긋남");
    }

    [Test]
    public void 주황_색은_왼쪽에서_오른쪽으로_보간_세기_0이면_왼쪽만()
    {
        Color l = new Color(1f, 0.5f, 0f), r = new Color(1f, 1f, 0f);
        Assert.AreEqual(l, BossVictoryRectifyFill.OrangeColor(0f, l, r, 1f));
        Assert.AreEqual(r, BossVictoryRectifyFill.OrangeColor(1f, l, r, 1f));
        Assert.AreEqual(0.75f, BossVictoryRectifyFill.OrangeColor(0.5f, l, r, 1f).g, 1e-5f);
        Assert.AreEqual(l, BossVictoryRectifyFill.OrangeColor(1f, l, r, 0f));
        var p = BossVictoryProfile.CreateDefault();   // 주황 채움 = 캐릭터별 프로필(11-12)
        try
        {
            Assert.IsFalse(p.rectifyOrangeRightToLeft);
            Assert.AreEqual(1f, p.rectifyOrangeGradient, 1e-5f);
        }
        finally { Object.DestroyImmediate(p); }
    }
}
