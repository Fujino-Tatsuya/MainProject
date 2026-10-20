using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// QA 에이전트의 행동을 게임 플레이어에 주입한다. 게임 코드 무수정 원칙:
/// - 이동: PlayerMotor.AddGroundedDisplacement (걷기·대시와 같은 접지 투영 변위 통로).
/// - 스킬/대시/공격: 각 컨트롤러의 public Try* 메서드(내부 FSM 게이트를 그대로 통과).
///
/// 주의: 이동은 입력액션 레이어(PlayerInputReader)를 우회한다 — 입력→FSM 경로 자체의 버그는
/// 2단계(가상 Input System 디바이스 주입)에서 커버한다. 1단계는 물리/권한/스킬 경로 검증에 집중.
///
/// 모터 재정립(96c8350) 이전에는 PlayerMovement.MoveTowardsPoint가 rb.MovePosition을 직접
/// 때렸지만, 이제 모든 이동은 모터 시뮬레이션을 통과해야 한다. 그 자동이동 경로를 위해
/// PlayerMovement가 MaxResolvedMoveSpeed·AutoMoveRotationSpeed를 internal로 노출해 두었으므로
/// 속도·회전 보간은 수동 이동과 같은 값을 그대로 쓴다(상태이상 배율·Soul 고정속도 포함).
/// </summary>
public sealed class QAInputController : MonoBehaviour
{
    private Player _player;
    private PlayerMovement _movement;
    private PlayerMotor _motor;
    private PlayerSkillController _skills;
    private PlayerDashController _dash;
    private DefaultAttackController _attack;

    private Vector3 _desiredWorldMove; // -1~1 정규화 평면 방향(0이면 정지)

    public bool HasTarget => _player != null;

    /// <summary>지금 이동 입력을 넣고 있는가(실행 환경 측정 — 이동 속도 실측용).</summary>
    public bool IsMoving => _player != null && _desiredWorldMove != Vector3.zero;

    public Player Target => _player;

    /// <summary>수동 이동과 같은 최대 이동 속도(m/s). 대상이 없으면 0.</summary>
    public float ExpectedMoveSpeed => _movement != null ? _movement.MaxResolvedMoveSpeed : 0f;

    /// <summary>플레이어가 스폰되면 세션 컨트롤러가 대상을 넘긴다. null이면 조작 중단.</summary>
    public void SetTarget(Player player)
    {
        _player = player;
        if (player == null)
        {
            _movement = null; _motor = null; _skills = null; _dash = null; _attack = null;
            _desiredWorldMove = Vector3.zero;
            QABlackboard.Controlling = false;
            return;
        }

        _movement = player.GetComponent<PlayerMovement>();
        _motor = player.GetComponent<PlayerMotor>();
        _skills = player.GetComponent<PlayerSkillController>();
        _dash = player.GetComponent<PlayerDashController>();
        _attack = player.GetComponent<DefaultAttackController>();
        QABlackboard.Controlling = true;
    }

    /// <summary>이번 결정의 이동 방향(평면). 크기 0이면 정지.</summary>
    public void SetMove(Vector3 worldDir)
    {
        worldDir.y = 0f;
        _desiredWorldMove = worldDir.sqrMagnitude > 0.0001f ? worldDir.normalized : Vector3.zero;
        if (_desiredWorldMove != Vector3.zero)
            QABlackboard.LastMoveIntentTime = Time.time;
    }

    public bool Attack()
    {
        return _attack != null && _attack.TryStart();
    }

    public bool Dash()
    {
        return _dash != null && _dash.TryBeginPredictedDash();
    }

    /// <summary>스킬 시전. 대상이 있으면 조준 모드를 건너뛰고 바로 시전한다.</summary>
    public bool UseSkill(PlayerSkillSlot slot, Unit target)
    {
        if (_skills == null || !_skills.IsCooldownReady(slot))
            return false;

        return target != null ? _skills.TryUse(slot, target) : _skills.TryUse(slot);
    }

    /// <summary>조준 확정 경로로 지점 시전 — 조준 모드에 들어가지 않는다(대상 없는 로비·맵 시험용).</summary>
    public bool UseSkillAt(PlayerSkillSlot slot, Vector3 aimPoint)
    {
        return _skills != null && _skills.ExecuteTargetedSkill(slot, null, aimPoint, true);
    }

    /// <summary>
    /// 키 한 번 누름을 가상 키보드 상태 이벤트로 넣는다(누름 2프레임 → 뗌). 로비 F 상호작용처럼
    /// 게임이 Keyboard 를 직접 읽는 경로를 그대로 탄다. 창 포커스·배경 입력 설정에 따라 무시될 수 있어
    /// 호출 측이 상태 변화로 반영 여부를 확인해야 한다.
    /// </summary>
    public IEnumerator PressKey(Key key)
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            yield break;

        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
    }

    /// <summary>지금 시전이 막히는 사유(null = 시전 가능). PlayerSkillController.Cast 의 조기 거부 조건과 같은 순서.</summary>
    public string SkillBlockReason(PlayerSkillSlot slot)
    {
        if (_skills == null)
            return "스킬 컨트롤러 없음";
        PlayerSkillBase skill = _skills.GetSkill(slot);
        if (skill == null || skill.Data == null)
            return "슬롯 비어 있음";
        if (_skills.IsBusy)
            return "다른 스킬 실행·요청 중";
        if (!_skills.IsCooldownReady(slot))
            return $"쿨다운 {_skills.GetCooldownRemaining(slot):F1}s";
        return null;
    }

    public bool IsSkillReady(PlayerSkillSlot slot)
    {
        return _skills != null && _skills.IsCooldownReady(slot);
    }

    public int DashCharge => _dash != null ? _dash.PredictedCharge : 0;
    public int DashMaxCharge => _dash != null ? _dash.MaxCharge : 0;

    private void FixedUpdate()
    {
        if (_motor == null || _movement == null || _desiredWorldMove == Vector3.zero)
            return;

        // CC 등으로 이동이 막힌 틱은 정지한다 — 모터는 CanMove를 보지 않으므로 여기서 건다
        // (옛 MoveTowardsPoint가 하던 게이트와 동일).
        if (_player == null || !_player.CanMove)
            return;

        // 수평 변위만 제출한다. 경사 투영·충돌 해석·중력은 모터 시뮬레이션이 담당한다.
        float distance = _movement.MaxResolvedMoveSpeed * Time.fixedDeltaTime;
        _motor.AddGroundedDisplacement(_desiredWorldMove * distance);

        // 모터의 회전 시뮬레이션은 PlayerInputReader의 입력을 보는데 QA는 그 층을 우회하므로
        // (HasMoveInput=false) armature가 이동 방향을 따라오지 않는다. 여기서 직접 돌린다.
        _movement.RotateToward(_desiredWorldMove, _movement.AutoMoveRotationSpeed);
    }
}
