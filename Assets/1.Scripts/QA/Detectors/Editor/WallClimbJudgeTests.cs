#if UNITY_EDITOR
using NUnit.Framework;

/// <summary>할 일 180 — P9 벽 타기 판정기는 스텝·접지 스냅·막힘 없는 상승을 무시하고, 막힌 채 계속 오를 때만 한 번 센다.</summary>
public sealed class WallClimbJudgeTests
{
    private const float StepOffset = 0.3f;
    private const float Dt = 0.02f;

    private WallClimbJudge _judge;
    private float _time;
    private float _y;
    private int _climbs;
    private int _climbsWithoutIntent;
    private WallClimbJudge.Result _lastTrigger;

    [SetUp]
    public void SetUp()
    {
        _judge = new WallClimbJudge(StepOffset);
        _time = 0f;
        _y = 0f;
        _climbs = 0;
        _climbsWithoutIntent = 0;
    }

    [Test]
    public void 턱_하나를_오르면_세지_않는다()
    {
        Run(1f, 0f, blocked: true);
        Run(7 * Dt, 0.04f, blocked: true); // 0.28m — StepOffset 이하
        Run(2f, 0f, blocked: true);

        Assert.AreEqual(0, _climbs);
    }

    [Test]
    public void 접지_스냅의_오르내림이_반복되어도_세지_않는다()
    {
        Run(1f, 0f, blocked: true);
        for (int i = 0; i < 500; i++)
            Advance(i % 2 == 0 ? 0.004f : -0.004f, blocked: true);

        Assert.AreEqual(0, _climbs);
    }

    [Test]
    public void 미세_상승_뒤_안정되기를_반복하면_기준을_다시_잡아_세지_않는다()
    {
        Run(1f, 0f, blocked: true);
        for (int i = 0; i < 30; i++)
        {
            Run(5 * Dt, 0.004f, blocked: true); // 2cm 스냅
            Run(0.6f, 0f, blocked: true);       // 안정 → 기준 재설정
        }

        Assert.Greater(_y, StepOffset, "누적 높이는 StepOffset 을 넘어야 의미 있는 시나리오다");
        Assert.AreEqual(0, _climbs);
    }

    [Test]
    public void 막힘_없이_오르면_세지_않는다()
    {
        Run(1f, 0f, blocked: false);
        Run(2f, 0.01f, blocked: false);

        Assert.Greater(_y, StepOffset);
        Assert.AreEqual(0, _climbs);
    }

    [Test]
    public void 벽에_막힌_채_계속_오르면_한_번만_센다()
    {
        Run(1f, 0f, blocked: true);
        Run(3f, 0.01f, blocked: true);

        Assert.AreEqual(1, _climbs);
        Assert.AreEqual(0f, _lastTrigger.BaselineY, 0.0001f);
        Assert.Greater(_lastTrigger.ClimbRise, StepOffset);
        Assert.GreaterOrEqual(_lastTrigger.ClimbSeconds, WallClimbJudge.ClimbSeconds);
        Assert.Greater(_lastTrigger.PeakY, StepOffset);
    }

    [Test]
    public void 막힘이_깜빡이는_좌우_연타_상승도_센다()
    {
        Run(1f, 0f, blocked: true);
        for (int i = 0; i < 150; i++)
            Advance(0.01f, blocked: i % 4 < 2);

        Assert.AreEqual(1, _climbs);
    }

    [Test]
    public void 막힘이_ClimbSeconds_보다_짧게_이어지면_세지_않는다()
    {
        Run(1f, 0f, blocked: true);
        Run(0.8f, 0.01f, blocked: true); // 0.4m 지만 0.8초
        Run(2f, 0f, blocked: true);

        Assert.AreEqual(0, _climbs);
    }

    [Test]
    public void 이동_의도가_없으면_오탐_제외로_분류한다()
    {
        Run(1f, 0f, blocked: true, moveIntent: false);
        Run(3f, 0.01f, blocked: true, moveIntent: false);

        Assert.AreEqual(0, _climbs);
        Assert.AreEqual(1, _climbsWithoutIntent);
    }

    [Test]
    public void 텔레포트급_단발_상승은_기준을_다시_잡는다()
    {
        Run(1f, 0f, blocked: true);
        Advance(5f, blocked: true);
        Run(2f, 0f, blocked: true);

        Assert.AreEqual(0, _climbs);
    }

    private void Run(float seconds, float deltaPerTick, bool blocked, bool moveIntent = true)
    {
        int ticks = UnityEngine.Mathf.RoundToInt(seconds / Dt);
        for (int i = 0; i < ticks; i++)
            Advance(deltaPerTick, blocked, moveIntent);
    }

    private void Advance(float deltaY, bool blocked, bool moveIntent = true)
    {
        _time += Dt;
        _y += deltaY;
        WallClimbJudge.Result result = _judge.Step(new WallClimbJudge.Tick
        {
            Time = _time,
            Y = _y,
            Grounded = true,
            Blocked = blocked,
            MoveIntent = moveIntent,
        });

        if (result.Verdict == WallClimbJudge.Verdict.Climb)
        {
            _climbs++;
            _lastTrigger = result;
        }
        else if (result.Verdict == WallClimbJudge.Verdict.ClimbWithoutIntent)
        {
            _climbsWithoutIntent++;
            _lastTrigger = result;
        }
    }
}
#endif
