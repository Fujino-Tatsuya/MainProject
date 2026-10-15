using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;

/// <summary>
/// 전역 시간 레이어의 순수 규칙 — 발동 번호 문지기(<see cref="SlowMotionSession"/>), 이벤트 직렬화(<see cref="SlowMotionEvent"/>),
/// GameNow 단조 클램프(<see cref="MonotonicTime"/>).
/// 시각·길이는 2 진수로 딱 떨어지는 값을 쓴다(<see cref="SlowMotionTimelineTests"/> 와 같은 이유).
/// </summary>
public sealed class SlowMotionSessionTests
{
    const double T0 = 8.0;
    const float Eps = 1e-5f;

    // 배율 0.5, 진입 0.125, 유지 0.25, 복귀 0.25, 실패 복귀 0.125 → 정상 종료 = 0.625s.
    static SlowMotionProfile P(float scale = 0.5f) =>
        new SlowMotionProfile
        {
            scale = scale,
            enterDuration = 0.125f,
            holdDuration = 0.25f,
            exitDuration = 0.25f,
            failExitDuration = 0.125f,
        };

    static SlowMotionEvent Start(uint id, double t, ulong clientId = 1, float scale = 0.5f) =>
        SlowMotionEvent.CreateStart(id, t, clientId, P(scale));

    // ─── 발동 번호 문지기 ─────────────────────────────────────────────

    [Test]
    public void Start_는_슬로우를_시작하고_번호와_발동자를_기록()
    {
        var s = new SlowMotionSession();

        Assert.AreEqual(SlowMotionApplyResult.Started, s.Apply(Start(1, T0, clientId: 7)));
        Assert.AreEqual(1u, s.CurrentTriggerId);
        Assert.AreEqual(7ul, s.CurrentTriggerClientId);
        Assert.AreEqual(SlowMotionPhase.Hold, s.PhaseAt(T0 + 0.25));
        Assert.AreEqual(0.5f, s.ScaleAt(T0 + 0.25), Eps);
    }

    [Test]
    public void 같은_번호_Start_는_중복으로_버린다()
    {
        var s = new SlowMotionSession();
        s.Apply(Start(1, T0));

        Assert.AreEqual(SlowMotionApplyResult.Ignored, s.Apply(Start(1, T0 + 0.25, scale: 0.25f)));
        Assert.AreEqual(0.5f, s.ScaleAt(T0 + 0.25), Eps, "타임라인이 다시 시작되지 않았다");
    }

    [Test]
    public void 작은_번호_Start_는_역순으로_버린다()
    {
        var s = new SlowMotionSession();
        s.Apply(Start(2, T0, clientId: 2));

        Assert.AreEqual(SlowMotionApplyResult.Ignored, s.Apply(Start(1, T0 + 0.25, clientId: 1)));
        Assert.AreEqual(2u, s.CurrentTriggerId);
        Assert.AreEqual(2ul, s.CurrentTriggerClientId);
    }

    [Test]
    public void 새_번호_Start_는_마지막_것이_이긴다()
    {
        var s = new SlowMotionSession();
        s.Apply(Start(1, T0, clientId: 1));

        Assert.AreEqual(SlowMotionApplyResult.Started, s.Apply(Start(2, T0 + 0.25, clientId: 2, scale: 0.25f)));
        Assert.AreEqual(2u, s.CurrentTriggerId);
        Assert.AreEqual(2ul, s.CurrentTriggerClientId);
        Assert.AreEqual(SlowMotionPhase.Enter, s.PhaseAt(T0 + 0.25));
        Assert.AreEqual(0.25f, s.ScaleAt(T0 + 0.25 + 0.125), Eps);
    }

    [Test]
    public void 현재_번호의_Fail_은_실패_복귀()
    {
        var s = new SlowMotionSession();
        s.Apply(Start(1, T0));

        Assert.AreEqual(SlowMotionApplyResult.Failed, s.Apply(SlowMotionEvent.CreateFail(1, T0 + 0.25)));
        Assert.AreEqual(SlowMotionPhase.FailExit, s.PhaseAt(T0 + 0.25));
        Assert.AreEqual(SlowMotionPhase.None, s.PhaseAt(T0 + 0.375));
    }

    [Test]
    public void 옛_번호의_늦은_Fail_은_새_슬로우를_죽이지_않는다()
    {
        var s = new SlowMotionSession();
        s.Apply(Start(1, T0));
        s.Apply(Start(2, T0 + 0.25));

        Assert.AreEqual(SlowMotionApplyResult.Ignored, s.Apply(SlowMotionEvent.CreateFail(1, T0 + 0.3125)));
        Assert.AreEqual(SlowMotionPhase.Hold, s.PhaseAt(T0 + 0.25 + 0.25));
    }

    [Test]
    public void 미래_번호의_Fail_도_버린다()
    {
        var s = new SlowMotionSession();
        s.Apply(Start(1, T0));

        Assert.AreEqual(SlowMotionApplyResult.Ignored, s.Apply(SlowMotionEvent.CreateFail(2, T0 + 0.25)));
        Assert.AreEqual(1u, s.CurrentTriggerId);
    }

    [Test]
    public void 번호_0_Fail_은_버린다()
    {
        var s = new SlowMotionSession();
        Assert.AreEqual(SlowMotionApplyResult.Ignored, s.Apply(SlowMotionEvent.CreateFail(0, T0)));
    }

    [Test]
    public void 끝난_슬로우의_Fail_은_효과_없음()
    {
        var s = new SlowMotionSession();
        s.Apply(Start(1, T0));

        Assert.AreEqual(SlowMotionApplyResult.NoEffect, s.Apply(SlowMotionEvent.CreateFail(1, T0 + 1.0)));
        Assert.AreEqual(SlowMotionPhase.None, s.PhaseAt(T0 + 1.0));
    }

    [Test]
    public void 두_번째_Fail_은_효과_없음()
    {
        var s = new SlowMotionSession();
        s.Apply(Start(1, T0));
        s.Apply(SlowMotionEvent.CreateFail(1, T0 + 0.25));

        Assert.AreEqual(SlowMotionApplyResult.NoEffect, s.Apply(SlowMotionEvent.CreateFail(1, T0 + 0.3125)));
    }

    [Test]
    public void Reset_은_진행_중이면_즉시_1()
    {
        var s = new SlowMotionSession();
        s.Apply(Start(1, T0));
        double lost = s.LostTimeUntil(T0 + 0.25);

        Assert.AreEqual(SlowMotionApplyResult.Reset, s.Apply(SlowMotionEvent.CreateReset(1, T0 + 0.25)));
        Assert.AreEqual(1f, s.ScaleAt(T0 + 0.25));
        Assert.AreEqual(lost, s.LostTimeUntil(T0 + 5.0), 1e-12, "리셋 시각까지의 손실만 남는다");
    }

    [Test]
    public void 옛_번호의_Reset_은_새_슬로우를_끊지_않는다()
    {
        var s = new SlowMotionSession();
        s.Apply(Start(1, T0));
        s.Apply(Start(2, T0 + 0.25));

        Assert.AreEqual(SlowMotionApplyResult.Ignored, s.Apply(SlowMotionEvent.CreateReset(1, T0 + 0.3125)));
        Assert.IsTrue(s.IsActiveAt(T0 + 0.3125));
    }

    [Test]
    public void 유휴_Reset_은_효과_없음()
    {
        var s = new SlowMotionSession();
        Assert.AreEqual(SlowMotionApplyResult.NoEffect, s.Apply(SlowMotionEvent.CreateReset(0, T0)));
    }

    [Test]
    public void 시각이_역행한_이벤트는_번호가_맞아도_버린다()
    {
        var s = new SlowMotionSession();
        s.Apply(Start(1, T0 + 0.25));

        Assert.AreEqual(SlowMotionApplyResult.Ignored, s.Apply(Start(2, T0)));
        Assert.AreEqual(1u, s.CurrentTriggerId, "번호만 오르고 타임라인엔 안 들어가는 어긋남이 없다");
        Assert.AreEqual(SlowMotionApplyResult.Ignored, s.Apply(SlowMotionEvent.CreateFail(1, T0)));
    }

    [Test]
    public void NaN_시각과_알_수_없는_종류는_버린다()
    {
        var s = new SlowMotionSession();
        Assert.AreEqual(SlowMotionApplyResult.Ignored, s.Apply(Start(1, double.NaN)));

        var unknown = Start(1, T0);
        unknown.Kind = (SlowMotionEventKind)99;
        Assert.AreEqual(SlowMotionApplyResult.Ignored, s.Apply(unknown));
        Assert.AreEqual(0u, s.CurrentTriggerId);
    }

    [Test]
    public void Clear_는_번호와_타임라인을_처음으로()
    {
        var s = new SlowMotionSession();
        s.Apply(Start(5, T0, clientId: 3));
        s.Clear();

        Assert.AreEqual(0u, s.CurrentTriggerId);
        Assert.AreEqual(0ul, s.CurrentTriggerClientId);
        Assert.AreEqual(0.0, s.LostTimeUntil(T0 + 1.0));
        Assert.AreEqual(SlowMotionApplyResult.Started, s.Apply(Start(1, 0.5)), "새 세션은 번호 1·시각 0 부근부터 다시 받는다");
    }

    [Test]
    public void 서버와_클라는_같은_이벤트로_같은_값을_낸다()
    {
        var server = new SlowMotionSession();
        var client = new SlowMotionSession();
        var events = new[]
        {
            Start(1, T0, clientId: 1),
            Start(2, T0 + 0.25, clientId: 2, scale: 0.25f),
            SlowMotionEvent.CreateFail(1, T0 + 0.3),   // 옛 번호 — 둘 다 버린다
            SlowMotionEvent.CreateFail(2, T0 + 0.5),
            Start(3, T0 + 2.0),
            SlowMotionEvent.CreateReset(3, T0 + 2.25),
        };

        foreach (var e in events)
        {
            server.Apply(e);
            client.Apply(RoundTrip(e));
        }

        for (int i = 0; i <= 400; i++)
        {
            double t = T0 + i * 0.01;
            Assert.AreEqual(server.ScaleAt(t), client.ScaleAt(t), $"t={t}");
            Assert.AreEqual(server.LostTimeUntil(t), client.LostTimeUntil(t), $"t={t}");
        }
    }

    // ─── 직렬화 ───────────────────────────────────────────────────────

    static SlowMotionEvent RoundTrip(in SlowMotionEvent e)
    {
        using (var writer = new FastBufferWriter(SlowMotionEvent.SerializedSize, Allocator.Temp))
        {
            e.Write(writer);
            Assert.AreEqual(SlowMotionEvent.SerializedSize, writer.Length, "선언한 크기와 실제 기록 크기가 같다");

            using (var reader = new FastBufferReader(writer, Allocator.Temp))
                return SlowMotionEvent.Read(reader);
        }
    }

    [Test]
    public void Start_이벤트_직렬화_왕복()
    {
        var profile = new SlowMotionProfile
        {
            scale = 0.3f, enterDuration = 0.05f, holdDuration = 0.35f, exitDuration = 0.25f, failExitDuration = 0.1f,
        };
        var e = SlowMotionEvent.CreateStart(uint.MaxValue, 12345.678901234, ulong.MaxValue - 1, profile);

        var r = RoundTrip(e);

        Assert.AreEqual(SlowMotionEventKind.Start, r.Kind);
        Assert.AreEqual(uint.MaxValue, r.TriggerId);
        Assert.AreEqual(12345.678901234, r.Time);
        Assert.AreEqual(ulong.MaxValue - 1, r.TriggerClientId);
        Assert.AreEqual(0.3f, r.Scale);
        Assert.AreEqual(0.05f, r.EnterDuration);
        Assert.AreEqual(0.35f, r.HoldDuration);
        Assert.AreEqual(0.25f, r.ExitDuration);
        Assert.AreEqual(0.1f, r.FailExitDuration);
    }

    [Test]
    public void Fail_Reset_이벤트_직렬화_왕복()
    {
        var fail = RoundTrip(SlowMotionEvent.CreateFail(3, 1.5));
        Assert.AreEqual(SlowMotionEventKind.Fail, fail.Kind);
        Assert.AreEqual(3u, fail.TriggerId);
        Assert.AreEqual(1.5, fail.Time);

        var reset = RoundTrip(SlowMotionEvent.CreateReset(4, 2.5));
        Assert.AreEqual(SlowMotionEventKind.Reset, reset.Kind);
        Assert.AreEqual(4u, reset.TriggerId);
        Assert.AreEqual(2.5, reset.Time);
    }

    [Test]
    public void 저작값은_클램프하지_않고_그대로_실린다()
    {
        // 수신측도 같은 SlowMotionProfile 클램프를 거치므로 원값을 보내야 서버와 같은 결과가 난다.
        var profile = new SlowMotionProfile { scale = -2f, enterDuration = -1f, holdDuration = float.NaN };
        var r = RoundTrip(SlowMotionEvent.CreateStart(1, 0.0, 0, profile));

        Assert.AreEqual(-2f, r.Scale);
        Assert.AreEqual(-1f, r.EnterDuration);
        Assert.IsTrue(float.IsNaN(r.HoldDuration));
        Assert.AreEqual(profile.ClampedScale, r.ToProfile().ClampedScale);
    }

    // ─── GameNow 단조 클램프 ──────────────────────────────────────────

    [Test]
    public void 단조_클램프는_증가는_통과_감소는_멈춤()
    {
        var m = new MonotonicTime();
        Assert.AreEqual(1.0, m.Next(1.0));
        Assert.AreEqual(2.0, m.Next(2.0));
        Assert.AreEqual(2.0, m.Next(1.5), "소급 손실로 되감긴 값은 최댓값에서 멈춘다");
        Assert.AreEqual(2.0, m.Next(2.0));
        Assert.AreEqual(2.5, m.Next(2.5), "실제 값이 다시 넘으면 따라간다");
    }

    [Test]
    public void 단조_클램프_NaN_은_기록하지_않는다()
    {
        var m = new MonotonicTime();
        Assert.IsTrue(double.IsNaN(m.Next(double.NaN)));
        Assert.AreEqual(1.0, m.Next(1.0));
        Assert.AreEqual(1.0, m.Next(double.NaN));
    }

    [Test]
    public void 단조_클램프_Reset_후엔_작은_값도_받는다()
    {
        var m = new MonotonicTime();
        m.Next(100.0);
        m.Reset();
        Assert.AreEqual(0.25, m.Next(0.25), "새 세션은 시계가 0 부근부터 다시 간다");
    }

    [Test]
    public void 클라_소급_시나리오에서_GameNow_는_역행하지_않는다()
    {
        // 클라: 서버 시각 T0 에 시작한 슬로우를 0.0625s 늦게 받는다. 받기 전엔 손실 0 으로 GameNow 를 내준다.
        var s = new SlowMotionSession();
        var m = new MonotonicTime();
        const double Delay = 0.0625;

        double prev = double.NegativeInfinity;
        for (int i = 0; i <= 200; i++)
        {
            double t = T0 - 0.25 + i * 0.005;
            if (t >= T0 + Delay && s.CurrentTriggerId == 0)
                s.Apply(Start(1, T0));

            double gameNow = m.Next(t - s.LostTimeUntil(t));
            Assert.GreaterOrEqual(gameNow, prev, $"t={t}");
            prev = gameNow;
        }

        Assert.AreEqual(T0 + 0.75 - s.LostTimeUntil(T0 + 0.75), prev, 1e-9, "끝나면 서버와 같은 값으로 수렴");
    }
}
