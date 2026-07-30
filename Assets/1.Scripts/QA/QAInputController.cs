using UnityEngine;

/// <summary>
/// QA 에이전트의 행동을 게임 플레이어에 주입한다. 게임 코드 무수정 원칙:
/// - 이동: PlayerMovement.MoveTowardsPoint (오너 자동이동용 public 메서드, CanMove 존중).
/// - 스킬/대시/공격: 각 컨트롤러의 public Try* 메서드(내부 FSM 게이트를 그대로 통과).
///
/// 주의: 이동은 입력액션 레이어(PlayerInputReader)를 우회한다 — 입력→FSM 경로 자체의 버그는
/// 2단계(가상 Input System 디바이스 주입)에서 커버한다. 1단계는 물리/권한/스킬 경로 검증에 집중.
/// </summary>
public sealed class QAInputController : MonoBehaviour
{
    [Tooltip("이동 목표점을 플레이어 앞 몇 m 지점으로 잡을지(방향 유지용).")]
    [SerializeField] private float moveLookahead = 3f;

    private Player _player;
    private PlayerMovement _movement;
    private PlayerSkillController _skills;
    private PlayerDashController _dash;
    private DefaultAttackController _attack;

    private Vector3 _desiredWorldMove; // -1~1 정규화 평면 방향(0이면 정지)

    public bool HasTarget => _player != null;

    /// <summary>플레이어가 스폰되면 세션 컨트롤러가 대상을 넘긴다. null이면 조작 중단.</summary>
    public void SetTarget(Player player)
    {
        _player = player;
        if (player == null)
        {
            _movement = null; _skills = null; _dash = null; _attack = null;
            _desiredWorldMove = Vector3.zero;
            QABlackboard.Controlling = false;
            return;
        }

        _movement = player.GetComponent<PlayerMovement>();
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

    public bool IsSkillReady(PlayerSkillSlot slot)
    {
        return _skills != null && _skills.IsCooldownReady(slot);
    }

    public int DashCharge => _dash != null ? _dash.PredictedCharge : 0;
    public int DashMaxCharge => _dash != null ? _dash.MaxCharge : 0;

    private void FixedUpdate()
    {
        if (_movement == null || _desiredWorldMove == Vector3.zero)
            return;

        // 방향 유지를 위해 매 물리틱 앞쪽 목표점으로 자동이동.
        Vector3 target = _player.transform.position + _desiredWorldMove * moveLookahead;
        _movement.MoveTowardsPoint(target);
    }
}
