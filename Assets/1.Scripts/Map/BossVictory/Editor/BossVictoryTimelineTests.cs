using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 타임라인(PLAN-boss-victory-glitch "타임라인 에디터 창") — 클립 평가·TV 꺼짐·마커(정지 유지 트랙은 255 후속 2 삭제),
/// 코드 기본 클립이 옛 기본 곡선 × 옛 전체 세기(교체 직전 에셋 값)와 같은 화면인지(전체 세기를 녹인 0.08~0.26초만 오차 0.06 허용). 에셋은 사용자 튜닝 값이라 구조만 본다.
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictoryTimeline).
/// </summary>
public sealed class BossVictoryTimelineTests
{
    const string AssetPath = "Assets/Resources/BossVictory/BossVictoryGlitchSettings.asset";
    const float SampleEnd = 5.6f;   // 옛 연출 끝(3.5 + 유지 1 + CRT 꺼짐 1.1)
    const float Tolerance = 1e-3f;

    // 교체 직전 에셋 곡선 키(시각, 값) — 기울기 0(옛 Ramp/Pulses 기본값).
    // 옛 전체 세기(master)는 화면 효과 전체에 곱해졌다(셰이더 마지막 lerp(원래 화면, 효과, master)) — 11-09 삭제, 각 클립 시작·페이드 인으로 녹임.
    static readonly float[] OldMaster = { 0f, 0f, 0.08f, 0f, 0.15f, 1f, 10f, 1f };
    const float FoldStart = 0.08f, FoldEnd = 0.26f;   // 녹인 구간(첫 찢김 끝까지) — 곱한 값과 최대 0.06 차이 허용
    const float FoldTolerance = 0.06f;

    static readonly (BossVictoryTrack track, float[] keys)[] OldCurves =
    {
        (BossVictoryTrack.Desaturate, new[] { 0f, 0f, 0.1f, 0f, 0.2f, 1f, 10f, 1f }),
        (BossVictoryTrack.Amber, new[] { 0f, 0f, 0.3f, 0f, 0.45f, 1f, 10f, 1f }),
        (BossVictoryTrack.Scanline, new[] { 0f, 0f, 0.1f, 0f, 0.2f, 1f, 10f, 1f }),
        (BossVictoryTrack.BossNoise, new[] { 0f, 0f, 0.12f, 0f, 0.17f, 1f, 10f, 1f }),
        (BossVictoryTrack.HudHide, new[] { 0f, 0f, 0.08f, 0f, 0.12f, 1f, 10f, 1f }),
        (BossVictoryTrack.Tear, new[]
        {
            0f, 0f, 0.08f, 0f, 0.2f, 1f, 0.25f, 0f, 0.32f, 0f, 0.33f, 1f, 0.41f, 0f,
            1.42f, 0f, 1.52f, 1f, 1.61f, 0f, 1.62f, 0f, 1.67f, 1f, 1.73f, 0f, 2.43f, 0f, 2.55f, 1f, 2.67f, 0f,
        }),
        (BossVictoryTrack.Smear, new[]
        {
            0f, 0f, 0.08f, 0f, 0.2f, 1f, 0.25f, 0f, 0.32f, 0f, 0.33f, 1f, 0.41f, 0f,
            1.42f, 0f, 1.52f, 1f, 1.62f, 0f, 2.43f, 0f, 2.55f, 1f, 2.67f, 0f,
        }),
    };

    static AnimationCurve Curve(float[] kv)
    {
        var keys = new Keyframe[kv.Length / 2];
        for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(kv[i * 2], kv[i * 2 + 1], 0f, 0f);
        return new AnimationCurve(keys);
    }

    static void AssertSameAsOldCurves(BossVictoryTimeline tl, string label)
    {
        foreach ((BossVictoryTrack track, float[] keys) in OldCurves)
        {
            AnimationCurve old = Curve(keys);
            AnimationCurve master = Curve(OldMaster);
            bool screenEffect = track != BossVictoryTrack.HudHide;   // HUD 숨김은 master 와 무관했다
            for (float t = 0f; t <= SampleEnd + 1e-4f; t += 0.0025f)
            {
                float expected = old.Evaluate(t) * (screenEffect ? master.Evaluate(t) : 1f);
                float tol = screenEffect && t >= FoldStart && t < FoldEnd ? FoldTolerance : Tolerance;
                Assert.AreEqual(expected, tl.Evaluate(track, t), tol, $"{label} {track} t={t:0.0000}");
            }
        }
    }

    [Test]
    public void 기본_클립은_옛_기본_곡선과_같은_화면()
    {
        AssertSameAsOldCurves(BossVictoryTimeline.CreateDefault(), "코드 기본값");
    }

    [Test]
    // 에셋 = 사용자가 창으로 튜닝하는 값(11-09) — 옛 곡선과 같은지는 보지 않고 구조만 본다.
    public void 에셋_타임라인_구조가_유효하다()
    {
        var settings = AssetDatabase.LoadAssetAtPath<BossVictoryGlitchSettings>(AssetPath);
        Assert.IsNotNull(settings, AssetPath);
        // 타임라인은 캐릭터별 프로필(267) — 기본 + 표의 모든 프로필.
        Assert.IsNotNull(settings.defaultProfile, "기본 프로필");
        AssertProfileTimeline(settings.defaultProfile);
        foreach (BossVictoryGlitchSettings.CharacterProfile row in settings.characterProfiles)
            if (row.profile != null) AssertProfileTimeline(row.profile);
    }

    static void AssertProfileTimeline(BossVictoryProfile profile)
    {
        BossVictoryTimeline tl = profile.timeline;
        Assert.IsNotNull(tl, $"{profile.name} timeline");
        foreach (BossVictoryTrack track in BossVictoryTimeline.Tracks)
        {
            var clips = tl.Clips(track);
            Assert.IsNotNull(clips, track.ToString());
            for (int i = 0; i < clips.Count; i++)
            {
                BossVictoryClip c = clips[i];
                string at = $"{track} #{i}";
                Assert.GreaterOrEqual(c.start, 0f, at + " 시작");
                Assert.Greater(c.length, 0f, at + " 길이");
                Assert.That(c.fadeIn, Is.InRange(0f, c.length), at + " 페이드 인");
                Assert.That(c.fadeOut, Is.InRange(0f, c.length), at + " 페이드 아웃");
                Assert.That(c.weight, Is.InRange(0f, 1f), at + " 세기");
            }
        }
        Assert.LessOrEqual(tl.crtOff.Count, 1, "TV 꺼짐은 1개만(창이 막는다)");
        Assert.LessOrEqual(tl.killerAction.Count, 1, "처치자 평타는 1개만(창이 막는다)");
        if (tl.killerAction.Count == 1) Assert.AreEqual(0f, tl.killerAction[0].start, "처치자 평타 시작 = 0 고정");
        Assert.AreEqual(tl.cameraCut.Count, tl.cameraCutShots.Count, "카메라 컷 값 개수 = 컷 클립 개수");
        foreach (BossVictoryMarker marker in BossVictoryTimeline.Markers)
            Assert.GreaterOrEqual(tl.MarkerTime(marker), 0f, marker.ToString());
        Assert.Greater(tl.lobbyTransitionTime, 0f, "로비 전환 마커가 0 이면 처치 순간 바로 끝난다");
    }

    [Test]
    public void 기본_TV_꺼짐_마커()
    {
        BossVictoryTimeline tl = BossVictoryTimeline.CreateDefault();
        Assert.IsFalse(System.Enum.IsDefined(typeof(BossVictoryTrack), "Hold"), "정지 유지 트랙 삭제(255 후속 2)");

        Assert.IsTrue(tl.TryGetCrtOff(out BossVictoryClip crt));
        Assert.AreEqual(4.5f, crt.start, 1e-5f);
        Assert.AreEqual(1.1f, crt.length, 1e-5f);

        Assert.AreEqual(0.2f, tl.focusKillerTime, 1e-5f, "카메라 전환 = 첫 찢김 최고점");
        Assert.AreEqual(0.2f, tl.hideOthersTime, 1e-5f);
        Assert.AreEqual(5.6f, tl.lobbyTransitionTime, 1e-5f, "로비 전환 = TV 꺼짐 끝");
        Assert.AreEqual(1f, tl.Evaluate(BossVictoryTrack.Tear, tl.focusKillerTime), 1e-4f, "마커 순간 찢김 최고");
        Assert.AreEqual(5.6f, tl.Duration, 1e-4f);
    }

    [Test]
    public void 클립_페이드_세기_겹침()
    {
        BossVictoryClip c = BossVictoryClip.Make(1f, 2f, 0.5f, 0.5f, 0.8f);
        Assert.AreEqual(0f, c.Evaluate(0.99f));
        Assert.AreEqual(0f, c.Evaluate(3.01f));
        Assert.AreEqual(0.4f, c.Evaluate(1.25f), 1e-5f, "페이드 인 가운데 = smoothstep 0.5");
        Assert.AreEqual(0.8f, c.Evaluate(2f), 1e-5f);
        Assert.AreEqual(0.4f, c.Evaluate(2.75f), 1e-5f);
        Assert.AreEqual(0f, BossVictoryClip.Make(1f, 0f).Evaluate(1f), "길이 0 = 없음");

        var tl = new BossVictoryTimeline();
        tl.tear.Add(BossVictoryClip.Make(0f, 1f, 0f, 0f, 0.3f));
        tl.tear.Add(BossVictoryClip.Make(0.5f, 1f, 0f, 0f, 0.6f));
        Assert.AreEqual(0.6f, tl.Evaluate(BossVictoryTrack.Tear, 0.75f), 1e-5f, "겹치면 큰 값");
        Assert.AreEqual(0.3f, tl.Evaluate(BossVictoryTrack.Tear, 0.25f), 1e-5f);
        Assert.IsFalse(tl.TryGetCrtOff(out _), "TV 꺼짐 클립 없으면 false");
    }

    [Test]
    public void 마커_시각_설정은_음수를_막는다()
    {
        var tl = new BossVictoryTimeline();
        tl.SetMarkerTime(BossVictoryMarker.LobbyTransition, -1f);
        Assert.AreEqual(0f, tl.MarkerTime(BossVictoryMarker.LobbyTransition));
        tl.SetMarkerTime(BossVictoryMarker.FocusKiller, 1.23f);
        Assert.AreEqual(1.23f, tl.focusKillerTime, 1e-6f);
    }

    // ── 노이즈 UV 수직 이동(267 후속) ──

    static BossVictoryClip Uv(float start, float length, float from, float to, BossVictoryKeyCurve ease) =>
        new BossVictoryClip { start = start, length = length, weight = 1f, uvFrom = from, uvTo = to, uvEase = ease };

    [Test]
    public void UV_수직_이동_시작_전_0_구간_안_이징_끝_뒤_끝_값()
    {
        BossVictoryClip c = Uv(1f, 2f, 0.2f, 1.2f, BossVictoryKeyCurve.EaseInOut);
        Assert.AreEqual(0f, c.UvOffset(0.5f), 1e-6f, "시작 전 = 0");
        Assert.AreEqual(0.2f, c.UvOffset(1f), 1e-5f, "시작 = 시작 오프셋");
        Assert.AreEqual(0.7f, c.UvOffset(2f), 1e-5f, "가운데 = smoothstep 0.5");
        Assert.AreEqual(0.2f + 0.15625f, c.UvOffset(1.5f), 1e-5f, "1/4 = smoothstep 0.15625(ease in — 천천히 출발)");
        Assert.AreEqual(1.2f, c.UvOffset(3f), 1e-5f, "끝");
        Assert.AreEqual(1.2f, c.UvOffset(9f), 1e-5f, "끝 뒤 유지");
        Assert.AreEqual(0.45f, Uv(1f, 2f, 0.2f, 1.2f, BossVictoryKeyCurve.Linear).UvOffset(1.5f), 1e-5f, "선형");
        Assert.AreEqual(-1f, Uv(0f, 0f, 0f, -1f, BossVictoryKeyCurve.Linear).UvOffset(0f), 1e-6f, "길이 0 = 시작부터 끝 값");
    }

    [Test]
    public void UV_수직_이동_클립끼리_더하고_세기_트랙이_아니다()
    {
        var tl = new BossVictoryTimeline();
        Assert.AreEqual(0f, tl.NoiseScrollOffset(1f));
        tl.noiseScrollV.Add(Uv(0f, 1f, 0f, 1f, BossVictoryKeyCurve.Linear));
        tl.noiseScrollV.Add(Uv(2f, 1f, 0f, 0.5f, BossVictoryKeyCurve.Linear));
        Assert.AreEqual(0.5f, tl.NoiseScrollOffset(0.5f), 1e-5f);
        Assert.AreEqual(1f, tl.NoiseScrollOffset(1.5f), 1e-5f, "첫 클립 끝 값 유지");
        Assert.AreEqual(1.25f, tl.NoiseScrollOffset(2.5f), 1e-5f, "이어서 더함");
        Assert.IsFalse(BossVictoryTimeline.HasFades(BossVictoryTrack.NoiseScrollV), "세기 키 없음");
        Assert.IsTrue(BossVictoryTimeline.IsUvScroll(BossVictoryTrack.NoiseScrollV));
        Assert.AreSame(tl.noiseScrollV, tl.Clips(BossVictoryTrack.NoiseScrollV));
        Assert.AreEqual(0, tl.MigrateKeys(), "키 이관 대상 아님");
        Assert.AreEqual(1f, tl.noiseScrollV[0].uvTo);

        // 다른 캐릭터에 복사 = 클립 추가(값·이징 그대로)
        var dst = new BossVictoryTimeline();
        dst.noiseScrollV.Add(Uv(0f, 1f, 0f, 3f, BossVictoryKeyCurve.Step));
        dst.CopyClipFrom(tl, BossVictoryTrack.NoiseScrollV, 1);
        Assert.AreEqual(2, dst.noiseScrollV.Count);
        Assert.AreEqual(0.5f, dst.noiseScrollV[1].uvTo, 1e-6f);
        Assert.AreEqual(2f, dst.noiseScrollV[1].start, 1e-6f);
    }

    // ── 발밑 회색 원(246 복원 — 267 후속) ──

    [Test]
    public void 발밑_회색_원_기본_클립은_노란_틴트_시작부터_커져_끝까지_유지()
    {
        BossVictoryTimeline tl = BossVictoryTimeline.CreateDefault();
        Assert.AreEqual(1, tl.groundCircle.Count);
        float s = BossVictoryTimeline.DefaultGroundCircleStart, g = BossVictoryTimeline.DefaultGroundCircleGrow;
        Assert.AreEqual(0f, tl.Evaluate(BossVictoryTrack.GroundCircle, s - 0.01f), 1e-5f, "시작 전 0");
        Assert.AreEqual(0.5f, tl.Evaluate(BossVictoryTrack.GroundCircle, s + g * 0.5f), 0.02f, "커지는 중(smoothstep 0.5 근처)");
        Assert.AreEqual(1f, tl.Evaluate(BossVictoryTrack.GroundCircle, s + g), 0.01f, "목표 반경");
        Assert.AreEqual(1f, tl.Evaluate(BossVictoryTrack.GroundCircle, BossVictoryTimeline.DefaultEnd - 0.01f), 1e-4f, "끝까지 유지");
        Assert.IsTrue(BossVictoryTimeline.HasFades(BossVictoryTrack.GroundCircle), "세기 키 트랙");
    }

    [Test]
    public void 발밑_회색_원_옛_에셋에_한_번만_넣고_지우면_다시_안_넣는다()
    {
        var tl = new BossVictoryTimeline();   // 이 트랙 전 에셋과 같음(표시 없음·클립 없음)
        Assert.IsTrue(tl.SeedGroundCircle());
        Assert.AreEqual(1, tl.groundCircle.Count);
        Assert.IsFalse(tl.SeedGroundCircle(), "멱등");
        tl.groundCircle.Clear();
        Assert.IsFalse(tl.SeedGroundCircle(), "사용자가 지운 뒤 다시 안 넣음");
        Assert.AreEqual(0, tl.groundCircle.Count);

        // 다른 캐릭터에 복사 = 클립 추가
        var src = BossVictoryTimeline.CreateDefault();
        var dst = BossVictoryTimeline.CreateDefault();
        dst.CopyClipFrom(src, BossVictoryTrack.GroundCircle, 0);
        Assert.AreEqual(2, dst.groundCircle.Count);
    }

    [Test]
    public void 발밑_회색_원_중심은_카메라_아래_바닥_마커_전엔_처치자()
    {
        var killer = new Vector3(1f, 2f, 3f);
        var cam = new Vector3(10f, 15f, -4f);
        Assert.AreEqual(killer, BossVictoryTimeline.GroundCircleCenter(0.1f, 0.2f, killer, true, cam), "마커 전 = 처치자(피어마다 카메라 다름)");
        Assert.AreEqual(new Vector3(10f, 2f, -4f), BossVictoryTimeline.GroundCircleCenter(0.2f, 0.2f, killer, true, cam), "마커부터 카메라 x·z, 높이 = 처치자 발");
        Assert.AreEqual(new Vector3(10f, 2f, -4f), BossVictoryTimeline.GroundCircleCenter(3f, 0.2f, killer, true, cam));
        Assert.AreEqual(killer, BossVictoryTimeline.GroundCircleCenter(3f, 0.2f, killer, false, cam), "카메라 없음 = 처치자");
    }

    // ── 카메라 두둥실(267 후속) ──

    [Test]
    public void 카메라_두둥실은_사인_진폭_주기_감쇠_세기_곱()
    {
        var c = new BossVictoryClip { start = 1f, length = 4f, weight = 1f, bobAmplitude = 0.5f, bobPeriod = 2f };
        Assert.AreEqual(0f, c.BobOffset(0.5f), 1e-6f, "클립 밖");
        Assert.AreEqual(0f, c.BobOffset(1f), 1e-5f, "위상 0 = 가운데 출발");
        Assert.AreEqual(0.5f, c.BobOffset(1.5f), 1e-5f, "1/4 주기 = 꼭대기");
        Assert.AreEqual(-0.5f, c.BobOffset(2.5f), 1e-5f, "3/4 주기 = 바닥");
        c.bobPhase = 0.25f;
        Assert.AreEqual(0.5f, c.BobOffset(1f), 1e-5f, "위상 0.25 = 꼭대기 출발");
        c.bobDamping = 1f;
        Assert.AreEqual(0.5f * Mathf.Exp(-2f), c.BobOffset(3f), 1e-5f, "감쇠 e^(−1·2)");
        c.bobDamping = 0f; c.weight = 0.5f;
        Assert.AreEqual(0.25f, c.BobOffset(1f), 1e-5f, "세기 곱");
        c.bobPeriod = 0f;
        Assert.AreEqual(0f, c.BobOffset(2f), 1e-6f, "주기 0 = 없음");
    }

    [Test]
    public void 카메라_두둥실_클립끼리_더하고_복사는_추가()
    {
        var tl = new BossVictoryTimeline();
        Assert.AreEqual(0f, tl.CameraBobOffset(1f));
        tl.cameraBob.Add(new BossVictoryClip { start = 0f, length = 4f, weight = 1f, bobAmplitude = 0.2f, bobPeriod = 2f, bobPhase = 0.25f });
        tl.cameraBob.Add(new BossVictoryClip { start = 0f, length = 4f, weight = 1f, bobAmplitude = 0.1f, bobPeriod = 2f, bobPhase = 0.25f });
        Assert.AreEqual(0.3f, tl.CameraBobOffset(0f), 1e-5f);
        Assert.IsTrue(BossVictoryTimeline.IsBob(BossVictoryTrack.CameraBob));
        Assert.IsTrue(BossVictoryTimeline.HasFades(BossVictoryTrack.CameraBob), "들어오고 나감 = 세기 키");
        var dst = new BossVictoryTimeline();
        dst.CopyClipFrom(tl, BossVictoryTrack.CameraBob, 1);
        dst.CopyClipFrom(tl, BossVictoryTrack.CameraBob, 0);
        Assert.AreEqual(2, dst.cameraBob.Count);
        Assert.AreEqual(0.2f, dst.cameraBob[1].bobAmplitude, 1e-6f);
    }
}
