using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// 미들보스 WallBot 전용 공격 확장 (2026-09-08).
//
// 배경 — WallBot 은 공격 클립이 **1종뿐**이라(AttackStart → AttackEnd) 카운터 창만 얹으면
// "모든 공격이 카운터 대상"이 된다. 그래서 팀장 설계는 **같은 클립을 3단으로 쪼개 다른 패턴처럼
// 보이게** 하는 것이다 — 이펙트가 붙으면 같은 클립이라는 걸 인지하기 어렵다는 판단이 근거다.
//
//  ① **모으기(카운터 창)** — `AttackStart` 를 재생하고 기다린다. 이 상태는 나가는 전이가
//     `AttackEnd` 트리거뿐이라(exitTime 없음) **마지막 프레임에서 스스로 멈춘다** = 방패가 모인 자세.
//     그래서 애니메이터 정지(홀드)가 필요 없다. 이 동안 인터럽트 스킬을 맞으면 **성공**.
//  ② **실패 → 전방 돌진** — 창이 끝나면 바라보던 방향으로 돌진하고, 경로에 걸린 플레이어를
//     주기마다 때리며 넉백한다(SpinnerBot 과 같은 히트창 재사용).
//  ③ **도착 → 공격 애니 + 주변 충격파** — `attackFinishTrigger`(AttackEnd)로 클립 2단을 틀고
//     반경 안 전원에게 데미지 + 넉백.
//
// 🔴 base 의 히트 경로를 **창 동안 막는다**. `AttackStart` 의 `OnAttackHit` 는 정규화 0.4(≈0.33초)로
//    창 한가운데에 있는데, 그대로 두면 모으는 도중에 근접 판정이 나가고 `AttackEnd` 트리거까지
//    발동해 2단이 먼저 재생된다.
public class WallBot : MonsterBase
{
    enum ShieldPhase { None, Gather, Dash, Shock }

    [Header("돌진 (실패 시)")]
    [SerializeField, Min(0.1f)]
    [Tooltip("돌진 지속(초).")]
    float dashDuration = 0.8f;
    [SerializeField, Min(1f)]
    [Tooltip("돌진 속도 배수. 이동 속도 = MoveSpeed × 이 값(끝나면 복귀).")]
    float dashSpeedMultiplier = 6f;
    [SerializeField, Min(0.1f)]
    [Tooltip("돌진 최대 거리(m). navmesh 경계에서 잘림(낙하 방지).")]
    float dashMaxDistance = 8f;
    [SerializeField, Min(0.05f)]
    [Tooltip("돌진 중 반복 히트 주기(초). 경로 안에 계속 있으면 이 주기마다 한 번 맞는다.")]
    float dashRepeatInterval = 0.5f;

    [Header("착지 충격파 (돌진 종료)")]
    [SerializeField, Min(0.1f)]
    [Tooltip("충격파 반경(m).")]
    float shockRadius = 3f;
    [SerializeField, Min(0f)]
    [Tooltip("충격파 데미지 = attackDamage × 이 배율.")]
    float shockDamageMultiplier = 1.2f;
    [SerializeField, Min(0f)]
    [Tooltip("충격파 넉백 세기. 0 이면 밀지 않는다.")]
    float shockKnockback = 6f;
    [SerializeField, Min(1)]
    [Tooltip("충격파 OverlapSphere 결과 버퍼 크기.")]
    int shockMaxHitCount = 8;

    // 서버 전용 런타임
    ShieldPhase _phase;
    float _phaseTimer;
    float _nextRepeatHitTime;
    Vector3 _dashDir;
    Collider[] _shockBuffer;
    readonly HashSet<Unit> _shockHitUnits = new HashSet<Unit>();

    MonsterCounterWindow _counter;

    /// <summary>카운터 창 컴포넌트(없으면 null = 예전처럼 단발 공격).</summary>
    MonsterCounterWindow Counter =>
        _counter != null ? _counter : (_counter = GetComponent<MonsterCounterWindow>());

    protected override void StartAttack()
    {
        base.StartAttack();   // StopAgent + FaceTarget + (옵션)슈퍼아머 + SetState(Attack) + Attack 트리거

        if (Counter == null)
        {
            _phase = ShieldPhase.None;   // 컴포넌트가 없으면 base 단발 공격 그대로
            return;
        }

        Counter.Open();
        if (!Counter.IsOpen)
        {
            _phase = ShieldPhase.None;
            return;
        }

        _phase = ShieldPhase.Gather;
        _phaseTimer = Counter.WindowDuration;
        _dashDir = transform.forward;   // FaceTarget 후 전방 = 돌진 방향(모으는 동안 고정)

        // 🔴 애니를 멈춰도 이 타이머는 줄어든다 — 창 + 돌진 + 2단 애니를 모두 덮어야 한다.
        _stateTimer = Counter.WindowDuration + dashDuration + (data != null ? data.attackDuration : 1.6f);

        ServerSetCounterWindow(true);
    }

    protected override void HandleAttack(float dt)
    {
        if (_phase == ShieldPhase.None)
        {
            base.HandleAttack(dt);   // 예전 경로(단발 근접)
            return;
        }

        _stateTimer -= dt;
        _phaseTimer -= dt;

        switch (_phase)
        {
            case ShieldPhase.Gather:
                HoldAgent();   // 제자리에서 방패를 모은다 — 재조준하지 않는다(방향 고정)
                if (Counter != null) Counter.TickAndDetectExpiry(dt);
                if (_phaseTimer <= 0f) BeginDash();
                break;

            case ShieldPhase.Dash:
                // 반복 히트 — BeginHitWindow 가 "유닛당 1회" 집합을 비우므로 주기마다 다시 부른다.
                if (Time.time >= _nextRepeatHitTime)
                {
                    meleeAttack?.BeginHitWindow();
                    _nextRepeatHitTime = Time.time + Mathf.Max(0.05f, dashRepeatInterval);
                }
                meleeAttack?.Hit();

                if (_phaseTimer <= 0f) BeginShock();
                break;

            case ShieldPhase.Shock:
                // 2단 클립이 끝나면 OnAttackEnd 가 상태를 뺀다. 아래는 이벤트 유실 대비 안전망.
                if (_phaseTimer <= 0f) EndSequence();
                break;
        }

        if (_stateTimer <= 0f && _phase != ShieldPhase.None) EndSequence();
    }

    // 창 종료 = 인터럽트 실패 → 전방 돌진.
    void BeginDash()
    {
        if (Counter != null)
        {
            Counter.Close();
            ServerSetCounterWindow(false);
        }

        _phase = ShieldPhase.Dash;
        _phaseTimer = dashDuration;
        _nextRepeatHitTime = 0f;   // 첫 틱에 바로 열린다

        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        Vector3 origin = transform.position;
        Vector3 desired = origin + _dashDir * dashMaxDistance;
        if (NavMesh.Raycast(origin, desired, out NavMeshHit hit, NavMesh.AllAreas))
            desired = hit.position;

        agent.isStopped = false;
        agent.speed = Mathf.Max(0.1f, MoveSpeed * dashSpeedMultiplier);
        agent.SetDestination(desired);
    }

    // 돌진 종료 → 공격 애니 2단 + 주변 충격파.
    void BeginShock()
    {
        meleeAttack?.EndHitWindow();
        if (agent != null) agent.speed = MoveSpeed;
        HoldAgent();

        _phase = ShieldPhase.Shock;
        _phaseTimer = data != null ? data.attackDuration : 1.6f;

        // 클립 2단(AttackEnd)을 여기서 튼다 — base 는 히트 시점에 트리거를 치지만 그 히트를 막았다.
        if (IsSpawned && data != null && !string.IsNullOrEmpty(data.attackFinishTrigger))
            PlayFinishTriggerClientRpc();

        ApplyShockwave();
    }

    void EndSequence()
    {
        meleeAttack?.EndHitWindow();
        if (agent != null) agent.speed = MoveSpeed;
        HoldAgent();
        status?.RemoveStatus(StatusEffectType.SuperArmor);

        _phase = ShieldPhase.None;
        DecideNextAfterAction();
    }

    /// <summary>
    /// 인터럽트 카운터 판정. 데미지는 base 가 먼저 처리하고, 창이 열려 있을 때만 성공으로 센다.
    /// </summary>
    public override void TakeDamage(AttackInfo attackInfo)
    {
        base.TakeDamage(attackInfo);

        if (!IsServer || !attackInfo.isInterruptAttack || Counter == null) return;
        if (!Counter.TryConsumeInterrupt()) return;

        CounterSucceeded();
    }

    // 카운터 성공 — 모으기를 취소하고 즉시 그로기. 그로기 자세는 `Hit` 클립으로 보여 준다
    // (이 컨트롤러엔 그로기 상태가 없다 — PLAN §9.1. `PlayStateAnimation` 에서 트리거를 친다).
    void CounterSucceeded()
    {
        ServerSetCounterWindow(false);

        meleeAttack?.EndHitWindow();
        if (agent != null) agent.speed = MoveSpeed;
        status?.RemoveStatus(StatusEffectType.SuperArmor);

        _phase = ShieldPhase.None;
        ForceGroggy(Counter.GroggyDuration);
    }

    /// <summary>
    /// 🔴 창이 열려 있는 동안 <b>히트와 2단 트리거를 막는다</b>. `AttackStart` 의 `OnAttackHit` 이
    /// 정규화 0.4(≈0.33초)로 창 한가운데에 있어, 그대로 두면 모으는 중에 근접 판정이 나가고
    /// 클립 2단까지 먼저 재생된다.
    /// </summary>
    public override void NotifyAttackHit()
    {
        if (Counter != null && Counter.IsOpen) return;
        base.NotifyAttackHit();
    }

    /// <summary>
    /// 모으기·돌진 구간에서는 클립의 종료 이벤트로 상태를 빼지 않는다 — 단계 타이머가 관리한다.
    /// 충격파 구간에서는 이벤트가 먼저 오면 그쪽으로 끝내고, 단계를 비워 타이머 경로가 두 번 끝내지 않게 한다.
    /// </summary>
    public override void NotifyAttackEnd()
    {
        if (_phase == ShieldPhase.Gather || _phase == ShieldPhase.Dash) return;

        _phase = ShieldPhase.None;
        base.NotifyAttackEnd();
    }

    // 그로기 자세 = Hit 클립(7프레임 ≈ 0.23초). 그로기 시간(0.5초)보다 짧아 뒤는 대기 자세로 선다 —
    // 전용 그로기 클립이 없어서다(아트 추가는 일정 밖). 전 피어에서 같은 콜백으로 재생된다.
    protected override void PlayStateAnimation(MonsterState s)
    {
        base.PlayStateAnimation(s);
        if (s == MonsterState.Groggy && data != null && !string.IsNullOrEmpty(data.hitTrigger))
            SafeSetTrigger(data.hitTrigger);
    }

    void HoldAgent()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
    }

    // 주변 충격파 — 반경 안 전원에게 데미지 + 넉백(유닛당 1회). GauntletBot 스매시와 같은 경로다.
    void ApplyShockwave()
    {
        if (!IsServer) return;

        if (_shockBuffer == null || _shockBuffer.Length != Mathf.Max(1, shockMaxHitCount))
            _shockBuffer = new Collider[Mathf.Max(1, shockMaxHitCount)];

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, shockRadius, _shockBuffer, playerMask, QueryTriggerInteraction.Collide);

        int damage = Mathf.RoundToInt(AttackDamage * shockDamageMultiplier);
        AttackInfo info = new AttackInfo(damage, AttackType.Default);

        _shockHitUnits.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider hit = _shockBuffer[i];
            if (hit == null) continue;

            Hurtbox hurtbox = hit.GetComponentInParent<Hurtbox>();
            Unit unit = hurtbox != null ? hurtbox.OwnerUnit : hit.GetComponentInParent<Unit>();
            if (unit == null || unit == this) continue;
            if (!_shockHitUnits.Add(unit)) continue;

            AttackHitContext ctx = new AttackHitContext(transform.position, transform, hit);
            bool applied = hurtbox != null
                ? hurtbox.ReceiveAttack(info, ctx)
                : unit.ReceiveAttack(info, ctx);

            // 🔴 넉백은 따로 불러야 한다 — ReceiveAttack 은 데미지만 처리한다(23호 실측 선례).
            if (!applied || shockKnockback <= 0f) continue;

            Vector3 dir = unit.transform.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                unit.Knockback(dir.normalized, shockKnockback);
        }
    }

    [Unity.Netcode.ClientRpc]
    void PlayFinishTriggerClientRpc() => SafeSetTrigger(data.attackFinishTrigger);
}
