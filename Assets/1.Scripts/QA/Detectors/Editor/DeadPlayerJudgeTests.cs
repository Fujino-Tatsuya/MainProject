#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

/// <summary>할 일 182 — P2 판정기는 Soul 정상 이동을 무시하고, 사망 연출 중 밀림·행동만 센다.</summary>
public sealed class DeadPlayerJudgeTests
{
    private const float Dt = 0.02f;
    private const float SoulSpeed = 5f;

    private DeadPlayerJudge _judge;
    private float _time;
    private Vector3 _position;
    private int _violationEntries;
    private bool _violating;

    [SetUp]
    public void SetUp()
    {
        _judge = new DeadPlayerJudge();
        _time = 0f;
        _position = Vector3.zero;
        _violationEntries = 0;
        _violating = false;
    }

    [Test]
    public void 사망_연출_뒤_Soul_이동은_세지_않는다()
    {
        Run(1.5f, PlayerLifeState.DeadPresentation, PlayerActionState.Dead, Vector3.zero, health: 0);
        Run(2f, PlayerLifeState.Soul, PlayerActionState.Move, Vector3.forward * SoulSpeed, health: 0);
        Run(1f, PlayerLifeState.Soul, PlayerActionState.Idle, Vector3.forward * SoulSpeed, health: 0);

        Assert.Greater(_position.magnitude, 1f, "Soul 이 실제로 1m 넘게 움직여야 의미 있는 시나리오다");
        Assert.AreEqual(0, _violationEntries);
    }

    [Test]
    public void 사망_연출_중_1m_넘게_밀리면_한_번_센다()
    {
        Run(0.3f, PlayerLifeState.DeadPresentation, PlayerActionState.Dead, Vector3.zero, health: 0);
        Run(0.5f, PlayerLifeState.DeadPresentation, PlayerActionState.Dead, Vector3.forward * 4f, health: 0);
        Run(0.5f, PlayerLifeState.DeadPresentation, PlayerActionState.Dead, Vector3.zero, health: 0);

        Assert.AreEqual(1, _violationEntries);
    }

    [Test]
    public void 사망_연출_중_Skill_상태면_한_번_센다()
    {
        Run(0.3f, PlayerLifeState.DeadPresentation, PlayerActionState.Dead, Vector3.zero, health: 0);
        Run(0.3f, PlayerLifeState.DeadPresentation, PlayerActionState.Skill, Vector3.zero, health: 0);

        Assert.AreEqual(1, _violationEntries);
    }

    [Test]
    public void Soul_에서_사망_연출로_다시_들어가면_기준을_새로_잡는다()
    {
        Run(1.5f, PlayerLifeState.DeadPresentation, PlayerActionState.Dead, Vector3.zero, health: 0);
        Run(2f, PlayerLifeState.Soul, PlayerActionState.Move, Vector3.forward * SoulSpeed, health: 0);

        DeadPlayerJudge.Result reentry = Advance(
            PlayerLifeState.DeadPresentation, PlayerActionState.Dead, Vector3.zero, health: 0);
        Assert.AreEqual(0f, reentry.Elapsed, 0.0001f);
        Assert.AreEqual(0f, reentry.Moved, 0.0001f);

        Run(1f, PlayerLifeState.DeadPresentation, PlayerActionState.Dead, Vector3.zero, health: 0);
        Assert.AreEqual(0, _violationEntries);

        // 재진입 기준에서 다시 1m 넘게 밀리면 센다.
        Run(0.5f, PlayerLifeState.DeadPresentation, PlayerActionState.Dead, Vector3.forward * 4f, health: 0);
        Assert.AreEqual(1, _violationEntries);
    }

    [Test]
    public void 생명주기가_없으면_HP_0_기준으로_폴백한다()
    {
        Run(0.5f, null, PlayerActionState.Idle, Vector3.forward * 4f, health: 0);

        Assert.AreEqual(1, _violationEntries);
    }

    [Test]
    public void Alive_는_HP_0_이어도_검사하지_않는다()
    {
        Run(0.5f, PlayerLifeState.Alive, PlayerActionState.Move, Vector3.forward * SoulSpeed, health: 0);

        Assert.AreEqual(0, _violationEntries);
    }

    private void Run(float seconds, PlayerLifeState? life, PlayerActionState state, Vector3 velocity, int health)
    {
        int ticks = Mathf.RoundToInt(seconds / Dt);
        for (int i = 0; i < ticks; i++)
            Advance(life, state, velocity, health);
    }

    private DeadPlayerJudge.Result Advance(PlayerLifeState? life, PlayerActionState state, Vector3 velocity, int health)
    {
        _time += Dt;
        _position += velocity * Dt;
        DeadPlayerJudge.Result result = _judge.Step(new DeadPlayerJudge.Tick
        {
            Time = _time,
            HasLifeState = life.HasValue,
            LifeState = life ?? PlayerLifeState.Alive,
            Health = health,
            ActionState = state,
            Position = _position,
            PositionValid = true,
        });

        // GameplayInvariantDetector.Evaluate 처럼 위반 진입만 센다.
        if (result.Violation && !_violating)
            _violationEntries++;
        _violating = result.Violation;
        return result;
    }
}
#endif
