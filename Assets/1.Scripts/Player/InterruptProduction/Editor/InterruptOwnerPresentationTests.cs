using System.Collections.Generic;
using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 오너 간파 연출 상태 기계(할 일 132 단계 2) — 강/약 예측, 예측 빗나감 걷기, 실패 걷기, 성공 인계, 중복 입력, 강제 리셋 즉시 0,
/// 그리고 클라 예측 입력 구성·창 추정(순수 함수).
/// </summary>
public sealed class InterruptOwnerPresentationTests
{
    private sealed class FakeTimings : IInterruptOwnerPresentationSettings
    {
        // 이진 소수로 딱 떨어지는 길이 — 램프 끝 비교가 부동소수 오차로 흔들리지 않게(디렉터 기본값은 아래 따로 확인).
        public float In = 0.125f;
        public float Out = 0.25f;
        public float OwnerDimInDuration => In;
        public float OwnerDimOutDuration => Out;
    }

    private sealed class FakeOutput : IInterruptOwnerDimOutput, IInterruptLightGatherOutput
    {
        public float Dim;
        public readonly List<string> Calls = new List<string>();
        public int GatherStarts;
        public int GatherStops;

        public void SetOwnerDim(float intensity)
        {
            Dim = intensity;
        }

        public void StartLightGather(PlayerInterruptSkillBase skill)
        {
            GatherStarts++;
            Calls.Add("start");
        }

        public void StopLightGather()
        {
            GatherStops++;
            Calls.Add("stop");
        }
    }

    private FakeTimings _timings;
    private FakeOutput _out;
    private InterruptOwnerPresentation _sm;

    [SetUp]
    public void SetUp()
    {
        _timings = new FakeTimings();
        _out = new FakeOutput();
        _sm = new InterruptOwnerPresentation(_timings, _out, _out);
    }

    // ─── 강·약 ───

    [Test]
    public void 강_예측은_암전이_들어옴_시간에_켜지고_빛_모임_시작()
    {
        Assert.IsTrue(_sm.Predict(InterruptAttemptForecast.Strong, null, 10f));
        Assert.AreEqual(InterruptOwnerPresentationState.AttemptStrong, _sm.State);
        Assert.IsTrue(_sm.LightGatherActive);
        Assert.AreEqual(1, _out.GatherStarts);

        _sm.Tick(10.0625f);
        Assert.AreEqual(0.5f, _out.Dim, 1e-4f, "들어옴 중간");
        _sm.Tick(10.125f);
        Assert.AreEqual(1f, _out.Dim);
    }

    [Test]
    public void 약_예측은_암전도_빛_모임도_없다()
    {
        Assert.IsTrue(_sm.Predict(InterruptAttemptForecast.Weak, null, 10f));
        _sm.Tick(10.5f);
        Assert.AreEqual(InterruptOwnerPresentationState.AttemptWeak, _sm.State);
        Assert.AreEqual(0f, _out.Dim);
        Assert.IsFalse(_sm.LightGatherActive);
        Assert.AreEqual(0, _out.GatherStarts);
    }

    // ─── 대상 없음(할 일 132 정정1) ───

    [Test]
    public void 대상_없음_예측은_로컬_연출을_아무것도_켜지_않는다()
    {
        Assert.IsFalse(_sm.Predict(InterruptAttemptForecast.NoTarget, null, 10f));
        _sm.Tick(10.5f);
        Assert.AreEqual(InterruptOwnerPresentationState.None, _sm.State);
        Assert.AreEqual(0f, _out.Dim);
        Assert.IsFalse(_sm.LightGatherActive);
        Assert.AreEqual(0, _out.GatherStarts);
        Assert.AreEqual(0, _out.Calls.Count);
    }

    [Test]
    public void 대상_없음_예측은_진행_중인_강_시도를_건드리지_않는다()
    {
        _sm.Predict(InterruptAttemptForecast.Strong, null, 10f);
        Assert.IsFalse(_sm.Predict(InterruptAttemptForecast.NoTarget, null, 10.05f));
        Assert.AreEqual(InterruptOwnerPresentationState.AttemptStrong, _sm.State);
        Assert.IsTrue(_sm.LightGatherActive);
    }

    [Test]
    public void 대상_없음_뒤_서버_시도_슬로우가_와도_켜지_않는다()
    {
        _sm.Predict(InterruptAttemptForecast.NoTarget, null, 10f);
        _sm.ServerStarted(5, SlowMotionPurpose.Attempt, true, 10.0625f);
        _sm.Tick(10.5f);
        Assert.AreEqual(InterruptOwnerPresentationState.None, _sm.State);
        Assert.AreEqual(0f, _out.Dim);
        Assert.AreEqual(0, _out.GatherStarts);
    }

    [Test]
    public void 예측_결론_3상태()
    {
        var none = InterruptAttemptPrediction.BuildInput(10f, Vector3.zero, 0.3f, false, Vector3.zero, 0f, 0f, false, true, 0f, 1f);
        Assert.AreEqual(InterruptAttemptForecast.NoTarget, DashInterruptPredictor.Predict(none).Forecast);
        var closed = InterruptAttemptPrediction.BuildInput(10f, Vector3.zero, 0.3f, false, Vector3.zero, 0f, 0f, true, false, -1f, 1f);
        Assert.AreEqual(InterruptAttemptForecast.Weak, DashInterruptPredictor.Predict(closed).Forecast);
        var open = InterruptAttemptPrediction.BuildInput(10f, Vector3.zero, 0.3f, false, Vector3.zero, 0f, 0f, true, true, 0.1f, 1f);
        Assert.AreEqual(InterruptAttemptForecast.Strong, DashInterruptPredictor.Predict(open).Forecast);
    }

    // ─── 대쉬 예측 시점 = 우클릭 누른 순간(할 일 132 정정1) ───

    [Test]
    public void 대쉬_예측은_누른_순간으로_되돌린다()
    {
        // 처리 프레임(10.05) 값: 위치 (0.5,0,0) · 남은 대시 0.15 · 창 열린 뒤 0.2. 누른 뒤 0.05초.
        var now = InterruptAttemptPrediction.BuildInput(10.05f, new Vector3(0.5f, 0f, 0f), 0.3f,
            true, Vector3.right, 10f, 0.15f, true, true, 0.2f, 1f);
        DashInterruptPredictInput press = InterruptAttemptPrediction.AtPress(now, 0.05f);

        Assert.AreEqual(10f, press.CastTime, 1e-4f);
        Assert.AreEqual(10f, press.SkillStartTime, 1e-4f, "준비 기준점 = 누른 순간");
        Assert.AreEqual(0f, press.CastPosition.x, 1e-4f, "대시 선을 따라 누른 위치로");
        Assert.AreEqual(0.2f, press.DashRemaining, 1e-4f);
        Assert.AreEqual(0.15f, press.WindowOpenElapsed, 1e-4f);

        DashInterruptPrediction a = DashInterruptPredictor.Predict(now);
        DashInterruptPrediction b = DashInterruptPredictor.Predict(press);
        Assert.AreEqual(a.PredictedEndPosition.x, b.PredictedEndPosition.x, 1e-4f, "예상 대시 끝은 같다");
        Assert.AreEqual(10.3f, b.PredictedResolveTime, 1e-4f, "판정 시각 = 누른 순간 + 준비 시간(대시 끝 10.2 보다 늦음)");
        Assert.AreEqual(10.35f, a.PredictedResolveTime, 1e-4f, "처리 프레임 기준이었다면 늦게 잡힌다");
    }

    [Test]
    public void 누른_뒤_지난_시간이_없으면_그대로()
    {
        var now = InterruptAttemptPrediction.BuildInput(10f, Vector3.one, 0.3f, true, Vector3.right, 10f, 0.2f, true, true, 0.2f, 1f);
        DashInterruptPredictInput press = InterruptAttemptPrediction.AtPress(now, 0f);
        Assert.AreEqual(now.CastTime, press.CastTime);
        Assert.AreEqual(now.CastPosition, press.CastPosition);
        Assert.AreEqual(now.DashRemaining, press.DashRemaining);
    }

    [Test]
    public void 서버_목적이_예측과_같으면_유지하고_번호를_기억()
    {
        _sm.Predict(InterruptAttemptForecast.Strong, null, 10f);
        _sm.ServerStarted(5, SlowMotionPurpose.Attempt, true, 10.0625f);
        _sm.Tick(10.25f);
        Assert.AreEqual(InterruptOwnerPresentationState.AttemptStrong, _sm.State);
        Assert.AreEqual(5u, _sm.AttemptTriggerId);
        Assert.IsFalse(_sm.Mispredicted);
        Assert.AreEqual(1f, _out.Dim);
        Assert.IsTrue(_sm.LightGatherActive);
    }

    // ─── 예측 빗나감 걷기 ───

    [Test]
    public void 강_예측인데_서버가_약이면_걷기_시간에_걷는다()
    {
        _sm.Predict(InterruptAttemptForecast.Strong, null, 10f);
        _sm.Tick(10.125f);
        _sm.ServerStarted(5, SlowMotionPurpose.AttemptWeak, true, 10.125f);

        Assert.IsTrue(_sm.Mispredicted);
        Assert.AreEqual(InterruptOwnerPresentationState.AttemptWeak, _sm.State);
        Assert.IsFalse(_sm.LightGatherActive, "빛 모임은 그 순간 멈춘다");
        Assert.AreEqual(1, _out.GatherStops);

        _sm.Tick(10.25f);
        Assert.AreEqual(0.5f, _out.Dim, 1e-4f, "걷기 중간");
        _sm.Tick(10.375f);
        Assert.AreEqual(0f, _out.Dim);
    }

    [Test]
    public void 약_예측인데_서버가_강이면_늦게_켜지_않는다()
    {
        _sm.Predict(InterruptAttemptForecast.Weak, null, 10f);
        _sm.ServerStarted(5, SlowMotionPurpose.Attempt, true, 10.05f);
        _sm.Tick(10.5f);
        Assert.IsTrue(_sm.Mispredicted);
        Assert.AreEqual(0f, _out.Dim);
        Assert.AreEqual(0, _out.GatherStarts);
    }

    [Test]
    public void 남의_슬로우는_무시한다_팀원_화면_무변화()
    {
        _sm.Predict(InterruptAttemptForecast.Strong, null, 10f);
        _sm.ServerStarted(5, SlowMotionPurpose.AttemptWeak, false, 10.0625f);
        _sm.ServerStarted(6, SlowMotionPurpose.Success, false, 10.0625f);
        _sm.Tick(10.125f);
        Assert.AreEqual(InterruptOwnerPresentationState.AttemptStrong, _sm.State);
        Assert.AreEqual(1f, _out.Dim);

        // 시도가 없던 화면(팀원)은 남의 슬로우로 아무것도 안 켠다.
        var other = new FakeOutput();
        var teammate = new InterruptOwnerPresentation(_timings, other, other);
        teammate.ServerStarted(5, SlowMotionPurpose.Attempt, false, 10f);
        teammate.Tick(10.5f);
        Assert.AreEqual(InterruptOwnerPresentationState.None, teammate.State);
        Assert.AreEqual(0f, other.Dim);
        Assert.AreEqual(0, other.GatherStarts);
    }

    // ─── 실패 걷기 ───

    [Test]
    public void 서버_실패면_걷기_시간에_걷고_없음으로()
    {
        _sm.Predict(InterruptAttemptForecast.Strong, null, 10f);
        _sm.ServerStarted(5, SlowMotionPurpose.Attempt, true, 10f);
        _sm.Tick(10.125f);
        _sm.ServerFailed(5, 10.25f);

        Assert.AreEqual(InterruptOwnerPresentationState.Failed, _sm.State);
        Assert.IsFalse(_sm.LightGatherActive);
        _sm.Tick(10.375f);
        Assert.AreEqual(0.5f, _out.Dim, 1e-4f);
        _sm.Tick(10.5f);
        Assert.AreEqual(0f, _out.Dim);
        Assert.AreEqual(InterruptOwnerPresentationState.None, _sm.State);
    }

    [Test]
    public void 다른_번호의_실패는_무시()
    {
        _sm.Predict(InterruptAttemptForecast.Strong, null, 10f);
        _sm.ServerStarted(5, SlowMotionPurpose.Attempt, true, 10f);
        _sm.ServerFailed(4, 10.1f);
        _sm.ServerFailed(0, 10.1f);
        Assert.AreEqual(InterruptOwnerPresentationState.AttemptStrong, _sm.State);
    }

    [Test]
    public void 판정_전_종료_거부면_실패처럼_걷는다()
    {
        _sm.Predict(InterruptAttemptForecast.Strong, null, 10f);
        _sm.Tick(10.125f);
        _sm.LocalEnded(10.125f);
        Assert.AreEqual(InterruptOwnerPresentationState.Failed, _sm.State);
        _sm.Tick(10.375f);
        Assert.AreEqual(0f, _out.Dim);
    }

    // ─── 성공 인계 ───

    [Test]
    public void 서버_성공이면_암전_유지_뒤_섬광_순간에_즉시_끈다()
    {
        _sm.Predict(InterruptAttemptForecast.Strong, null, 10f);
        _sm.ServerStarted(5, SlowMotionPurpose.Attempt, true, 10f);
        _sm.Tick(10.125f);
        _sm.ServerStarted(6, SlowMotionPurpose.Success, true, 10.25f);

        Assert.AreEqual(InterruptOwnerPresentationState.Success, _sm.State);
        Assert.IsTrue(_sm.SuccessDimmed, "강 시도의 성공 = 섬광 오버레이 조건");
        Assert.IsFalse(_sm.LightGatherActive);
        _sm.Tick(30f);
        Assert.AreEqual(1f, _out.Dim, "섬광 순간 전엔 실시간이 얼마나 흘러도 암전 그대로(따로 재지 않는다)");
        Assert.AreEqual(InterruptOwnerPresentationState.Success, _sm.State);

        // 섬광 순간(진행기 사건) — 걷기 램프 없이 그 호출에서 0, 그리고 없음.
        _sm.SuccessFlashMoment(6, 30f);
        Assert.AreEqual(0f, _out.Dim);
        Assert.AreEqual(InterruptOwnerPresentationState.None, _sm.State);
    }

    [Test]
    public void 섬광_순간은_이번_성공_것만_암전을_끈다()
    {
        _sm.Predict(InterruptAttemptForecast.Strong, null, 10f);
        _sm.Tick(10.125f);
        _sm.ServerStarted(6, SlowMotionPurpose.Success, true, 10.25f);
        _sm.SuccessFlashMoment(5, 10.5f);
        Assert.AreEqual(1f, _out.Dim, "다른 성공(앞 판정)의 섬광 순간");

        _sm.ServerStarted(7, SlowMotionPurpose.Success, true, 10.5f);
        _sm.SuccessFlashMoment(6, 10.75f);
        Assert.AreEqual(1f, _out.Dim, "새 성공이 오면 새 섬광 순간을 기다린다");
        _sm.SuccessFlashMoment(7, 11f);
        Assert.AreEqual(0f, _out.Dim);
    }

    [Test]
    public void 성공_단계_밖_섬광_순간은_무시()
    {
        _sm.Predict(InterruptAttemptForecast.Strong, null, 10f);
        _sm.Tick(10.125f);
        _sm.SuccessFlashMoment(0, 10.25f);
        Assert.AreEqual(InterruptOwnerPresentationState.AttemptStrong, _sm.State);
        Assert.AreEqual(1f, _out.Dim);
    }

    [Test]
    public void 성공_암전_중_강제_리셋은_즉시_0이고_섬광_조건도_끈다()
    {
        _sm.Predict(InterruptAttemptForecast.Strong, null, 10f);
        _sm.Tick(10.125f);
        _sm.ServerStarted(6, SlowMotionPurpose.Success, true, 10.25f);
        _sm.ForcedReset();
        Assert.AreEqual(0f, _out.Dim);
        Assert.IsFalse(_sm.SuccessDimmed);
        _sm.Tick(10.8f);
        Assert.AreEqual(0f, _out.Dim, "예약된 끄기가 남아 다시 켜지지 않는다");
    }

    [Test]
    public void 같은_판정의_실패_직후_성공이면_암전을_되살린다()
    {
        _sm.Predict(InterruptAttemptForecast.Strong, null, 10f);
        _sm.ServerStarted(5, SlowMotionPurpose.Attempt, true, 10f);
        _sm.Tick(10.125f);
        _sm.ServerFailed(5, 10.25f);
        _sm.ServerStarted(6, SlowMotionPurpose.Success, true, 10.25f);
        _sm.Tick(10.375f);
        Assert.AreEqual(InterruptOwnerPresentationState.Success, _sm.State);
        Assert.AreEqual(1f, _out.Dim);
    }

    [Test]
    public void 약_시도가_성공하면_암전_없이_성공_단계()
    {
        _sm.Predict(InterruptAttemptForecast.Weak, null, 10f);
        _sm.ServerStarted(5, SlowMotionPurpose.AttemptWeak, true, 10f);
        _sm.ServerStarted(6, SlowMotionPurpose.Success, true, 10.2f);
        Assert.AreEqual(InterruptOwnerPresentationState.Success, _sm.State);
        Assert.IsFalse(_sm.SuccessDimmed, "약 시도의 성공 = 섬광 오버레이 없음");
        _sm.Tick(10.3f);
        Assert.AreEqual(0f, _out.Dim);
        Assert.AreEqual(InterruptOwnerPresentationState.None, _sm.State, "끌 암전이 없어 바로 결론");
    }

    // ─── 빛 모임 끝 = Hit ───

    [Test]
    public void Hit_이벤트에_빛_모임만_멈추고_암전은_유지()
    {
        _sm.Predict(InterruptAttemptForecast.Strong, null, 10f);
        _sm.Tick(10.125f);
        _sm.LocalHit();
        Assert.IsFalse(_sm.LightGatherActive);
        Assert.AreEqual(1, _out.GatherStops);
        _sm.Tick(10.25f);
        Assert.AreEqual(1f, _out.Dim);
        _sm.LocalHit();
        Assert.AreEqual(1, _out.GatherStops, "두 번 멈추지 않는다");
    }

    // ─── 중복 입력 ───

    [Test]
    public void 결론_전_중복_입력은_무시()
    {
        _sm.Predict(InterruptAttemptForecast.Strong, null, 10f);
        Assert.IsFalse(_sm.Predict(InterruptAttemptForecast.Weak, null, 10.05f));
        Assert.IsFalse(_sm.Predict(InterruptAttemptForecast.Strong, null, 10.05f));
        Assert.AreEqual(InterruptOwnerPresentationState.AttemptStrong, _sm.State);
        Assert.AreEqual(1, _out.GatherStarts);
    }

    [Test]
    public void 결론_뒤_새_입력은_새_시도()
    {
        _sm.Predict(InterruptAttemptForecast.Strong, null, 10f);
        _sm.ServerStarted(5, SlowMotionPurpose.Attempt, true, 10f);
        _sm.ServerFailed(5, 10.1f);
        Assert.IsTrue(_sm.Predict(InterruptAttemptForecast.Strong, null, 10.15f));
        Assert.AreEqual(InterruptOwnerPresentationState.AttemptStrong, _sm.State);
        Assert.AreEqual(0u, _sm.AttemptTriggerId, "새 시도는 번호를 새로 받는다");
        Assert.IsFalse(_sm.Mispredicted);
    }

    // ─── 강제 리셋 ───

    [Test]
    public void 강제_리셋은_즉시_0()
    {
        _sm.Predict(InterruptAttemptForecast.Strong, null, 10f);
        _sm.Tick(10.125f);
        Assert.AreEqual(1f, _out.Dim);
        _sm.ForcedReset();
        Assert.AreEqual(0f, _out.Dim, "램프 없이 그 자리에서");
        Assert.AreEqual(InterruptOwnerPresentationState.None, _sm.State);
        Assert.IsFalse(_sm.LightGatherActive);
        _sm.Tick(10.15f);
        Assert.AreEqual(0f, _out.Dim);
    }

    [Test]
    public void 출력_교체는_지금_값을_새_출력에_넘긴다()
    {
        _sm.Predict(InterruptAttemptForecast.Strong, null, 10f);
        _sm.Tick(10.125f);
        var next = new FakeOutput();
        _sm.SetOutputs(next, next);
        Assert.AreEqual(0f, _out.Dim, "옛 출력은 끈다");
        Assert.AreEqual(1, _out.GatherStops);
        Assert.AreEqual(1f, next.Dim);
        Assert.AreEqual(1, next.GatherStarts);
    }

    // ─── 디렉터 칸 기본값 ───

    [Test]
    public void 디렉터_오너_연출_칸_기본값()
    {
        var go = new GameObject("Director");
        try
        {
            var so = new SerializedObject(go.AddComponent<InterruptProductionDirector>());
            Assert.AreEqual(0.1f, so.FindProperty("ownerDimInDuration").floatValue, 1e-6f);
            Assert.AreEqual(0.2f, so.FindProperty("ownerDimOutDuration").floatValue, 1e-6f);
            Assert.IsNull(so.FindProperty("ownerSuccessHoldDuration"), "임시 성공 유지 칸은 섬광 순간으로 대체돼 없다(할 일 132 단계 5)");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // ─── 클라 예측 입력 구성(순수 함수) ───

    [Test]
    public void 근접_입력은_대시_칸_0_판정_시각_지금_더하기_준비()
    {
        DashInterruptPredictInput input = InterruptAttemptPrediction.BuildInput(10f, new Vector3(1f, 0f, 2f), 0.3f,
            false, Vector3.forward, 20f, 0.2f, true, true, 0.1f, 0.5f);
        Assert.AreEqual(0f, input.DashRemaining, "근접은 대시 칸을 무시");
        Assert.AreEqual(0f, input.DashSpeed);
        Assert.AreEqual(10f, input.SkillStartTime);
        Assert.IsTrue(input.HasTargetInRange);
        Assert.IsTrue(input.TargetWindowOpen);
        Assert.AreEqual(0.1f, input.WindowOpenElapsed);
        Assert.AreEqual(0.5f, input.WindowExpectedDuration);
        Assert.AreEqual(10.3f, DashInterruptPredictor.Predict(input).PredictedResolveTime, 1e-4f);
        Assert.AreEqual(Vector3.zero, InterruptAttemptPrediction.PredictOffset(false, Vector3.forward, 20f, 0.2f));
    }

    [Test]
    public void 대쉬_입력은_예상_대시_끝_위치와_max_판정_시각()
    {
        DashInterruptPredictInput input = InterruptAttemptPrediction.BuildInput(10f, new Vector3(1f, 0f, 2f), 0.1f,
            true, Vector3.forward, 20f, 0.2f, true, true, 0.1f, 0.5f);
        Assert.AreEqual(0.2f, input.DashRemaining);
        DashInterruptPrediction p = DashInterruptPredictor.Predict(input);
        Assert.AreEqual(6f, p.PredictedEndPosition.z, 1e-4f, "2 + 20 × 0.2");
        Assert.AreEqual(10.2f, p.PredictedResolveTime, 1e-4f, "대시 끝 0.2 > 준비 0.1");
        Assert.AreEqual(4f, InterruptAttemptPrediction.PredictOffset(true, Vector3.forward, 20f, 0.2f).z, 1e-4f);
    }

    [Test]
    public void 대상_없음_창_닫힘이면_창_칸은_모름()
    {
        DashInterruptPredictInput none = InterruptAttemptPrediction.BuildInput(10f, Vector3.zero, 0.3f,
            false, Vector3.zero, 0f, 0f, false, true, 0.1f, 0.5f);
        Assert.IsFalse(none.HasTargetInRange);
        Assert.IsFalse(none.TargetWindowOpen, "대상이 없으면 열림도 없다");
        Assert.AreEqual(-1f, none.WindowOpenElapsed);
        Assert.AreEqual(-1f, none.WindowExpectedDuration);
        Assert.AreEqual(DashInterruptPredictReason.NoTargetInRange, DashInterruptPredictor.Predict(none).Reason);

        DashInterruptPredictInput closed = InterruptAttemptPrediction.BuildInput(10f, Vector3.zero, 0.3f,
            false, Vector3.zero, 0f, 0f, true, false, 0.1f, 0.5f);
        Assert.AreEqual(-1f, closed.WindowOpenElapsed, "닫혀 있으면 열린 뒤 경과는 모름");
        Assert.AreEqual(0.5f, closed.WindowExpectedDuration);
        Assert.AreEqual(DashInterruptPredictReason.WindowClosedNow, DashInterruptPredictor.Predict(closed).Reason);
    }

    [Test]
    public void 창_남은_시간이_판정까지보다_짧으면_약()
    {
        // 클라 창 추정 = 열린 뒤 0.3 / 추정 0.5 → 남은 0.2 < 준비 0.3
        DashInterruptPredictInput weak = InterruptAttemptPrediction.BuildInput(10f, Vector3.zero, 0.3f,
            false, Vector3.zero, 0f, 0f, true, true, 0.3f, 0.5f);
        Assert.IsFalse(DashInterruptPredictor.Predict(weak).PredictsSuccess);
        DashInterruptPredictInput strong = InterruptAttemptPrediction.BuildInput(10f, Vector3.zero, 0.3f,
            false, Vector3.zero, 0f, 0f, true, true, 0.1f, 0.5f);
        Assert.IsTrue(DashInterruptPredictor.Predict(strong).PredictsSuccess);
    }

    [Test]
    public void 창_기록_추정은_지금_기준_경과와_중앙값_길이()
    {
        var history = new InterruptWindowHistory();
        history.Record(1.0, true);
        history.Record(1.5, false);
        history.Record(3.0, true);

        // 클라: 기록 = 받은 순간 ServerTime, 추정 = LocalTime(요청 도착 시각) — 두 값을 그대로 뺀다.
        history.Estimate(3.25, true, out float elapsed, out float expected);
        Assert.AreEqual(0.25f, elapsed, 1e-5f);
        Assert.AreEqual(0.5f, expected, 1e-5f);

        history.Estimate(3.25, false, out elapsed, out expected);
        Assert.AreEqual(-1f, elapsed, "지금 닫혀 있다고 보면 경과는 모름");
        Assert.AreEqual(0.5f, expected, 1e-5f);

        new InterruptWindowHistory().Estimate(1.0, true, out elapsed, out expected);
        Assert.AreEqual(-1f, elapsed);
        Assert.AreEqual(-1f, expected);
    }
}
