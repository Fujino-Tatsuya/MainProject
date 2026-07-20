using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

// 미들보스 SpinnerBot 전용 공격 확장.
// 기본 = Whip 근접(base 데미지 경로) / 특수 = "스핀"(준비→돌진하며 회전공격→Dizzy).
//
// 스핀 시퀀스(모두 Attack 상태 안에서 SpinnerBot이 phase 타이머로 관리, 커밋 — 중간에 플레이어가 범위 밖 나가도 중단 X):
//  1) 준비: spinWindup(≥1s) 동안 제자리 회전(Spin Attack Start). 방향은 시작 시 고정 → 옆으로 회피 가능.
//  2) 돌진: 바라보던 방향으로 이동(agent 속도 = MoveSpeed × 배수), navmesh 경계 클램프(NavMesh.Raycast)로 낭떠러지 진입 불가.
//           이동 중 Spin Attack Loop 유지 + 경로상 적 1틱(히트 윈도우 dedup). 종료 시 속도 복귀.
//  3) Dizzy: IsDizzy 불로 Dizzy 애니, 슈퍼아머 해제(취약). 지속 후 FSM 복귀. (그로기 상태와 별개.)
//
// 애니는 상태복제가 아니라 ClientRpc로 확정(선택값 미복제 회피). Whip/Spin=CrossFade(트리거/Variation 배선 의존 X), Dizzy=IsDizzy 불.
public class SpinnerBot : MonsterBase
{
    [Header("스핀 — 선택")]
    [SerializeField, Range(0f, 1f)]
    [Tooltip("공격 시 스핀을 고를 확률(나머지는 기본 Whip).")]
    float spinChance = 0.4f;
    [SerializeField, Min(0f)]
    [Tooltip("스핀 재사용 대기(초).")]
    float spinCooldown = 6f;

    [Header("스핀 — 타이밍/이동 (플레이테스트 튜닝)")]
    [SerializeField, Min(1f)]
    [Tooltip("돌진 전 준비 시간(초, 최소 1). 방향은 준비 시작 시 고정 — 옆으로 피할 시간.")]
    float spinWindup = 1f;
    [SerializeField, Min(0.1f)]
    [Tooltip("돌진(회전공격) 지속(초).")]
    float dashDuration = 0.9f;
    [SerializeField, Min(1f)]
    [Tooltip("돌진 속도 배수. 이동 속도 = MoveSpeed × 이 값(끝나면 복귀).")]
    float dashSpeedMultiplier = 8f;
    [SerializeField, Min(0.1f)]
    [Tooltip("돌진 최대 거리(m). navmesh 경계에서 잘림(낙하 방지).")]
    float dashMaxDistance = 16f;
    [SerializeField, Min(0f)]
    [Tooltip("스핀 종료 후 Dizzy(취약) 지속(초).")]
    float dizzyDuration = 2.5f;

    [Header("애니 상태/파라미터 (컨트롤러 일치)")]
    [SerializeField] string spinStartState = "Spin Attack Start";
    [SerializeField] string spinLoopState = "Spin Attack Loop";
    [SerializeField] string whipStateR = "Attack Whip R Start";
    [SerializeField] string whipStateL = "Attack Whip L Start";
    [SerializeField] string dizzyBool = "IsDizzy";

    // 서버 전용 스핀 런타임
    bool _spinning;
    bool _dashStarted;
    bool _dizzyStarted;
    Vector3 _dashDir;
    float _lastSpinTime = -999f;
    bool _whipUseR;

    bool SpinReady => Time.time - _lastSpinTime >= spinCooldown;
    float SpinCommit => spinWindup + dashDuration + dizzyDuration;

    protected override void StartAttack()
    {
        bool doSpin = SpinReady && Random.value < spinChance;

        base.StartAttack(); // StopAgent + FaceTarget + (옵션)슈퍼아머 + SetState(Attack)

        if (doSpin)
        {
            _spinning = true;
            _dashStarted = false;
            _dizzyStarted = false;
            _lastSpinTime = Time.time;
            _dashDir = transform.forward;   // FaceTarget 후 전방 = 돌진 방향(고정)
            _stateTimer = SpinCommit;       // base의 attackDuration을 스핀 전체 길이로 덮어씀

            // 준비+돌진 동안만 슈퍼아머(있으면). Dizzy 진입 시 해제해 취약.
            if (data != null && data.hasSuperArmorWhileAttacking && status != null)
                status.ApplyStatus(StatusEffectType.SuperArmor, spinWindup + dashDuration);

            PlaySpinStartClientRpc();
        }
        else
        {
            _spinning = false;
            _whipUseR = !_whipUseR;         // 좌우 번갈아
            PlayWhipClientRpc(_whipUseR);   // 기본 Whip 애니(데미지는 base.HandleAttack가 처리)
        }
    }

    protected override void HandleAttack(float dt)
    {
        if (!_spinning)
        {
            base.HandleAttack(dt);          // 기본 Whip 경로(windup 히트 + 종료)
            return;
        }

        _stateTimer -= dt;
        float elapsed = SpinCommit - _stateTimer;

        if (elapsed < spinWindup)
        {
            // 준비: 제자리 정지 + 방향 고정(재조준 X).
            HoldAgent();
        }
        else if (elapsed < spinWindup + dashDuration)
        {
            if (!_dashStarted)
            {
                _dashStarted = true;
                PlaySpinLoopClientRpc();
                meleeAttack?.BeginHitWindow(); // 돌진 동안 유닛당 1틱
                StartDash();
            }
            meleeAttack?.Hit();
        }
        else if (!_dizzyStarted)
        {
            // Dizzy 진입: 히트 종료, 속도 복귀, 정지, 슈퍼아머 해제(취약), Dizzy 애니.
            _dizzyStarted = true;
            meleeAttack?.EndHitWindow();
            if (agent != null) agent.speed = MoveSpeed;
            HoldAgent();
            status?.RemoveStatus(StatusEffectType.SuperArmor);
            PlayDizzyClientRpc(true);
        }

        if (_stateTimer <= 0f)
        {
            _spinning = false;
            // IsDizzy는 상태 이탈 시 base가 groggyBool(=IsDizzy) 토글로 자동 해제.
            DecideNextAfterAction();
        }
    }

    void HoldAgent()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
    }

    // navmesh 경계까지 클램프한 목표로 돌진 시작(낭떠러지 진입 불가 — 가장자리에서 정지).
    void StartDash()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        Vector3 origin = transform.position;
        Vector3 desired = origin + _dashDir * dashMaxDistance;
        if (NavMesh.Raycast(origin, desired, out NavMeshHit hit, NavMesh.AllAreas))
            desired = hit.position;

        agent.isStopped = false;
        agent.speed = Mathf.Max(0.1f, MoveSpeed * dashSpeedMultiplier);
        agent.SetDestination(desired);
    }

    // 공격 애니는 아래 RPC들이 담당하므로 Attack 상태의 기본 매핑은 건너뛴다.
    // 그 외 상태는 base(Groggy에서 groggyBool=IsDizzy 토글 → 상태 이탈 시 Dizzy 자동 해제 포함).
    protected override void PlayStateAnimation(MonsterState s)
    {
        if (s == MonsterState.Attack) return;
        base.PlayStateAnimation(s);
    }

    [ClientRpc] void PlaySpinStartClientRpc() => SafeCrossFade(spinStartState);
    [ClientRpc] void PlaySpinLoopClientRpc() => SafeCrossFade(spinLoopState);
    [ClientRpc] void PlayWhipClientRpc(bool useR) => SafeCrossFade(useR ? whipStateR : whipStateL);
    [ClientRpc] void PlayDizzyClientRpc(bool on) => SafeSetBool(dizzyBool, on);
}
