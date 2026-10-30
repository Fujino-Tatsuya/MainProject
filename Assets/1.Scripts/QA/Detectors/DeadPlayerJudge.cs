using UnityEngine;

/// <summary>
/// P2 사망 플레이어 행동 판정기 — 씬/MonoBehaviour 없이 틱 입력만으로 판정한다(할 일 182).
/// 사망 연출(DeadPresentation)이 끝나 Soul 이 되면 이동은 정상이다(SoulAccess.AllowsMovement).
/// 그래서 사망은 PlayerLifeCycleController.State 로 보고, Soul 동안은 이동 검사 대신 기준 위치를
/// 계속 현재 위치로 다시 잡는다. 생명주기 컴포넌트가 없으면 예전처럼 "HP 0 또는 FSM Dead" 로 본다.
/// </summary>
public sealed class DeadPlayerJudge
{
    public const float MovementWindowSeconds = 2f;
    public const float MovementDistance = 1f;

    public struct Tick
    {
        public float Time;
        /// <summary>PlayerLifeCycleController 를 찾았는지 — false 면 Health/ActionState 로 폴백한다.</summary>
        public bool HasLifeState;
        public PlayerLifeState LifeState;
        public int Health;
        public PlayerActionState ActionState;
        public Vector3 Position;
        public bool PositionValid;
    }

    public struct Result
    {
        /// <summary>사망(DeadPresentation·PermanentDead, 폴백이면 HP 0·FSM Dead) 중인지.</summary>
        public bool Dead;
        public bool Soul;
        public bool Violation;
        public bool ActionState;
        public bool MovedWhileDying;
        public float Moved;
        public float Elapsed;
    }

    private bool _inDeath;
    private float _deadSince;
    private Vector3 _startPosition;

    public void Reset()
    {
        _inDeath = false;
    }

    public Result Step(Tick tick)
    {
        bool soul = tick.HasLifeState && tick.LifeState == PlayerLifeState.Soul;
        if (soul)
        {
            // Soul 은 자유 이동한다 — 기준을 계속 현재 위치로 옮기고, 다시 사망 연출에 들어가면 새로 센다.
            _inDeath = false;
            _startPosition = tick.Position;
            // Soul 은 전투 입력·대시가 막힌다(SoulAccess.AllowsCombatInput=false, DashValidationPolicy DeadOrSoul).
            bool soulAction =
                tick.ActionState == PlayerActionState.Attack ||
                tick.ActionState == PlayerActionState.Skill ||
                tick.ActionState == PlayerActionState.Dash;
            return new Result { Soul = true, Violation = soulAction, ActionState = soulAction };
        }

        bool dead = tick.HasLifeState
            ? tick.LifeState == PlayerLifeState.DeadPresentation || tick.LifeState == PlayerLifeState.PermanentDead
            : tick.Health == 0 || tick.ActionState == PlayerActionState.Dead;
        if (!dead)
        {
            _inDeath = false;
            return default;
        }

        if (!_inDeath)
        {
            _inDeath = true;
            _deadSince = tick.Time;
            _startPosition = tick.Position;
        }

        bool actionState =
            tick.ActionState == PlayerActionState.Move ||
            tick.ActionState == PlayerActionState.Attack ||
            tick.ActionState == PlayerActionState.Skill ||
            tick.ActionState == PlayerActionState.Dash;
        float elapsed = tick.Time - _deadSince;
        float moved = tick.PositionValid ? Vector3.Distance(_startPosition, tick.Position) : 0f;
        bool movedWhileDying =
            tick.PositionValid &&
            elapsed <= MovementWindowSeconds &&
            moved > MovementDistance;

        return new Result
        {
            Dead = true,
            Violation = actionState || movedWhileDying,
            ActionState = actionState,
            MovedWhileDying = movedWhileDying,
            Moved = moved,
            Elapsed = elapsed,
        };
    }
}
