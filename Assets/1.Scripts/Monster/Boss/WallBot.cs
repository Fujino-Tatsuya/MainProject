using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// 미들보스 WallBot 전용 공격 확장 (2026-09-08).
//
// 배경 — WallBot 은 공격 클립이 **1종뿐**이라(AttackStart → AttackEnd) 카운터 창만 얹으면
// "모든 공격이 카운터 대상"이 된다. 그래서 팀장 설계는 **같은 클립을 3단으로 쪼개 다른 패턴처럼
// 보이게** 하는 것이다 — 이펙트가 붙으면 같은 클립이라는 걸 인지하기 어렵다는 판단이 근거다.
//
// 🔴 **매 공격이 3단인 것은 아니다.** `shieldCooldown`(기본 8초) + `shieldChance`(기본 0.5)로 고른다 —
//    안 걸리면 같은 클립의 **단발 평타**(창·돌진·충격파 없음, base 경로)다. 중간보스 3종이 다 이렇다
//    (SpinnerBot = 쿨+확률로 스핀 / GauntletBot = 7종 룰렛에서 Smash 일 때만).
//    쿨 하한이 8초인 이유는 **플레이어 인터럽트 스킬 쿨이 8초**라서다 — 더 짧으면 못 끊는 돌진이 생긴다.
//
//  ① **모으기(카운터 창)** — `AttackStart` 를 한 바퀴 재생한 뒤 **마지막 자세에서 정지**한다
//     (`ServerHoldActionPoseAtClipEnd`). 이 동안 인터럽트 스킬을 맞으면 **성공**.
//     ⚠️ 정정(2026-09-08 Play 검증): 이전 주석은 "나가는 전이가 `AttackEnd` 트리거뿐이라 마지막
//        프레임에서 스스로 멈춘다 = 홀드 불필요" 였는데 **틀렸다**. `A_WallBot_AttackStart` 는
//        임포터에서 `loopTime: 1` 이라 25프레임(≈0.8초)을 계속 반복한다. 그래서 창 1.5초 동안
//        클립이 두 바퀴 돌았고, 창이 닫힌 뒤(돌진 중) 루프한 `OnAttackHit` 이 2단 트리거까지 쳐서
//        **돌진 한복판에서 2단 클립이 나갔다.** 클립이 멈추는지는 전이가 아니라 loopTime 이 정한다.
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

    [Header("방패 3단 — 선택")]
    [SerializeField, Range(0f, 1f)]
    [Tooltip("공격 시 방패 3단(카운터)을 고를 확률. 나머지는 기본 평타 — 같은 클립의 단발 스윙이다.")]
    float shieldChance = 0.5f;
    [SerializeField, Min(0f)]
    [Tooltip("방패 3단 재사용 대기(초). 🔴 플레이어 인터럽트 스킬 쿨(8초)보다 짧게 두지 말 것 — " +
             "짧으면 스킬이 안 도는 사이에 3단이 또 나와 '못 끊는 돌진'이 생긴다(팀장 Play 판정 2026-09-08).")]
    float shieldCooldown = 8f;

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

    [SerializeField, Min(0.1f)]
    [Tooltip("충격파(2단 클립) 구간 길이(초). A_WallBot_AttackEnd 는 17프레임(≈0.6초)이고 67%에서 " +
             "Movement 로 빠진다 — data.attackDuration(1.6초)을 쓰면 끝난 뒤 1초를 멀뚱히 서 있다.")]
    float shockDuration = 0.7f;

    [SerializeField]
    [Tooltip("모으기 자세를 붙잡을 애니메이터 **상태** 이름. 트리거 이름(Attack)이 아니라 상태 이름이다 " +
             "— Controller_WallBot 실물 기준 'AttackStart'. 아트가 상태를 개명하면 여기도 고칠 것.")]
    string gatherStateName = "AttackStart";

    [Header("진단")]
    [SerializeField]
    [Tooltip("단계 전이·차단된 히트를 콘솔에 찍는다. 상태가 바뀔 때만 찍으므로 스팸이 아니다.")]
    bool debugLog = false;

    // 서버 전용 런타임
    ShieldPhase _phase;
    float _phaseTimer;
    float _nextRepeatHitTime;
    Vector3 _dashDir;
    Collider[] _shockBuffer;
    readonly HashSet<Unit> _shockHitUnits = new HashSet<Unit>();

    float _lastShieldTime = -999f;

    MonsterCounterWindow _counter;

    // 초기값 -999 = "아주 오래 전" → 첫 공격이 쿨에 안 걸린다(SpinnerBot 과 같은 규약).
    // 교전 시작하자마자 3단이 나오는 게 거슬리면 스폰 시 Time.time 으로 시드하면 된다.
    bool ShieldReady => Time.time - _lastShieldTime >= shieldCooldown;

    /// <summary>카운터 창 컴포넌트(없으면 null = 예전처럼 단발 공격).</summary>
    MonsterCounterWindow Counter =>
        _counter != null ? _counter : (_counter = GetComponent<MonsterCounterWindow>());

    protected override void StartAttack()
    {
        // 🔴 매 공격이 카운터가 되면 안 된다(팀장 Play 판정 2026-09-08). 중간보스 3종의 규약이
        //    이미 그렇다 — SpinnerBot 은 쿨+확률로 스핀을 고르고, GauntletBot 은 7종 룰렛에서
        //    Smash 일 때만 창을 연다. WallBot 만 게이트가 없어 **평타가 한 번도 안 나왔다.**
        //    WallBot 은 공격 클립이 1종이라 "평타"도 같은 클립을 쓴다 — 창·돌진·충격파 없이
        //    AttackStart → (히트) → AttackEnd 로 끝나는 단발 스윙이고, 그 경로는 base 가 처리한다.
        bool doShield = ShieldReady && Random.value < shieldChance;

        base.StartAttack();   // StopAgent + FaceTarget + (옵션)슈퍼아머 + SetState(Attack) + Attack 트리거

        if (!doShield || Counter == null)
        {
            _phase = ShieldPhase.None;   // 기본 평타 — base.HandleAttack 이 히트·종료를 처리한다
            Log(doShield ? "평타 (카운터 창 컴포넌트 없음)" : $"평타 (방패 쿨 {Mathf.Max(0f, shieldCooldown - (Time.time - _lastShieldTime)):0.#}s 남음 또는 확률 미당첨)");
            return;
        }

        Counter.Open();
        if (!Counter.IsOpen)
        {
            _phase = ShieldPhase.None;
            Log("평타 (창 길이가 0 — 카운터 없음으로 읽음)");
            return;
        }

        _lastShieldTime = Time.time;
        _phase = ShieldPhase.Gather;
        _phaseTimer = Counter.WindowDuration;

        // 🔴 조준은 여기 한 번뿐이다 — 그런데 base.StartAttack 의 FaceTarget() 은 data.turnSpeed(10)
        //    감속 회전이라 **한 프레임에 목표각의 ~16%만 돈다.** 그 어중간한 각도로 _dashDir 를 굳히면
        //    히트박스(전방 박스 z 0~2m)가 타깃을 정면으로 안 봐서 모서리만 걸치고, 돌진도 빗나간다.
        //    base 가 바로 이 자리를 위해 FaceTargetImmediate() 를 갖고 있다("돌아본 뒤 그 방향을
        //    즉시 쓰는 자리에서만"). 커밋형은 유지한다 — 스냅 뒤로는 재조준하지 않는다(팀장 확정).
        FaceTargetImmediate();
        _dashDir = transform.forward;

        // 🔴 애니를 멈춰도 이 타이머는 줄어든다 — 창 + 돌진 + 2단 애니를 모두 덮어야 한다.
        _stateTimer = Counter.WindowDuration + dashDuration + shockDuration;

        // 🔴 슈퍼아머를 시퀀스 전체로 다시 건다. base 는 data.attackDuration(1.6초)으로만 걸어
        //    3.9초짜리 시퀀스의 t=1.6 이후(돌진 후반 + 충격파 전체)가 무방비였다 — 거기서 스킬을
        //    맞으면 EnterHit 으로 Attack 을 벗어나며 시퀀스가 정리 없이 얼어붙었다.
        //    인터럽트는 카운터 창으로만 통해야 한다는 설계 의도와도 이쪽이 맞다.
        if (data != null && data.hasSuperArmorWhileAttacking && status != null)
            status.ApplyStatus(StatusEffectType.SuperArmor, _stateTimer);

        ServerSetCounterWindow(true);

        // 방패를 다 모은 자세에서 정지 — 클립이 루프라 자동으로 멈추지 않는다(파일 상단 ⚠️ 정정 참조).
        ServerHoldActionPoseAtClipEnd(gatherStateName);

        Log($"Gather 시작 — 창 {Counter.WindowDuration:0.##}s · 시퀀스 {_stateTimer:0.##}s");
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
        Log("돌진 시작 (인터럽트 실패)");

        // 자세는 계속 붙잡아 둔다 — 방패를 든 채 돌진하는 그림이고, 풀면 AttackStart 가 다시
        // 루프를 돌며 클립 안의 OnAttackHit 이 재발화한다(그게 2단이 돌진 중에 나가던 원인이다).

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
        _phaseTimer = shockDuration;

        // 🔴 순서가 중요하다 — **자세를 먼저 풀고** 2단 트리거를 친다. 애니메이터 speed 가 0 인 채로
        //    트리거를 치면 전이가 진행되지 않아 모으기 자세에 눌러앉는다(base 의 ApplyLocomotionFreeze
        //    주석과 같은 함정). 풀면 AttackStart 로 돌아오고, 거기서 AttackStart→AttackEnd 가 성립한다.
        ServerReleaseActionPose();
        ServerPlayFinishTrigger();

        Log($"충격파 — 반경 {shockRadius:0.##}m · {shockDuration:0.##}s");
        ApplyShockwave();
    }

    // 2단 클립(AttackEnd) 재생을 전 피어에 건다. base 의 FireAttackHitOnce 경로는 서버에서만
    // 트리거를 쳐서 클라 애니가 안 넘어간다 — 그 경로를 막았으므로 여기서 직접 옮긴다.
    void ServerPlayFinishTrigger()
    {
        if (data == null || string.IsNullOrEmpty(data.attackFinishTrigger)) return;

        SafeSetTrigger(data.attackFinishTrigger);           // 호스트 자신
        if (IsSpawned) PlayFinishTriggerClientRpc();
    }

    void EndSequence()
    {
        ClearSequenceRuntime();
        HoldAgent();
        Log("시퀀스 종료");
        DecideNextAfterAction();
    }

    /// <summary>
    /// 시퀀스가 남긴 런타임을 전부 되돌린다. <b>모든 종료·중단 경로가 여기를 지나야 한다</b> —
    /// 하나라도 빠지면 히트 윈도우가 열린 채 남거나(다음 공격의 유닛당 1회가 깨진다),
    /// 카운터 창이 안 닫혀 <see cref="NotifyAttackHit"/> 가 이후 근접 판정을 영구히 막는다.
    /// 멱등이다.
    /// </summary>
    void ClearSequenceRuntime()
    {
        if (Counter != null) Counter.Close();
        ServerSetCounterWindow(false);
        ServerReleaseActionPose();

        meleeAttack?.EndHitWindow();
        if (agent != null) agent.speed = MoveSpeed;
        status?.RemoveStatus(StatusEffectType.SuperArmor);

        _phase = ShieldPhase.None;
    }

    /// <summary>
    /// 🔴 <c>Attack</c> 밖으로 튕기는 <b>다른</b> 경로(피격 경직 · 넉백 · 리쉬 복귀 · 사망)에서
    /// 시퀀스를 정리한다. <see cref="HandleAttack"/> 은 <c>Attack</c> 상태에서만 도므로 그런 이탈에서는
    /// <see cref="EndSequence"/> 가 영영 안 불린다 — 그러면 돌진 속도(6배)·히트 윈도우·카운터 창이
    /// 그대로 남는다. 카운터 성공(→ Groggy)은 자기가 이미 정리했으므로 여기서 두 번 하지 않는다.
    /// </summary>
    protected override void OnMonsterStateChanged(MonsterState previous, MonsterState next)
    {
        base.OnMonsterStateChanged(previous, next);

        if (!IsServer) return;
        if (next == MonsterState.Attack || _phase == ShieldPhase.None) return;

        Log($"시퀀스 중단 — {_phase} 중에 {previous}→{next} 로 이탈");
        ClearSequenceRuntime();
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
        float groggy = Counter.GroggyDuration;

        // 🔴 자세 홀드도 여기서 풀린다(ClearSequenceRuntime). 안 풀면 애니메이터 speed 가 0 이라
        //    아래 ForceGroggy 가 치는 Hit 트리거의 전이가 진행되지 않아 모으기 자세에 눌러앉는다.
        //    OnStateChanged 는 next == Groggy 일 때 홀드를 **일부러 유지**하므로 자동으로 안 풀린다.
        ClearSequenceRuntime();

        Log($"카운터 성공 — 그로기 {groggy:0.##}s");
        ForceGroggy(groggy);
    }

    /// <summary>
    /// 🔴 <b>시퀀스가 도는 동안 base 의 히트 경로를 통째로 막는다</b>. 히트도 2단 트리거도
    /// WallBot 이 직접 낸다(돌진 = <c>meleeAttack.Hit()</c> · 충격파 = <see cref="ApplyShockwave"/>).
    ///
    /// ⚠️ 정정(2026-09-08 Play 검증): 이전에는 <c>Counter.IsOpen</c> 으로만 막았다. 그런데
    ///    <c>AttackStart</c> 가 루프 클립이라 <b>창이 닫힌 뒤에도</b> 클립 안의 <c>OnAttackHit</c> 이
    ///    다시 발화했고, 그게 <c>FireAttackHitOnce</c> → <c>attackFinishTrigger</c> 로 이어져
    ///    <b>돌진 한복판에서 2단 클립이 재생</b>됐다. 그 다음 <see cref="BeginShock"/> 의 트리거는
    ///    갈 전이가 없어 래치로 남아 **다음 공격까지 망가뜨렸다.** 창이 아니라 단계로 막아야 한다.
    /// </summary>
    public override void NotifyAttackHit()
    {
        if (_phase != ShieldPhase.None)
        {
            Log($"근접 히트 이벤트 차단 ({_phase})");
            return;
        }
        base.NotifyAttackHit();
    }

    /// <summary>
    /// 모으기·돌진 구간에서는 클립의 종료 이벤트로 상태를 빼지 않는다 — 단계 타이머가 관리한다.
    /// 충격파 구간에서는 이벤트가 먼저 오면 그쪽으로 끝내고, 단계를 비워 타이머 경로가 두 번 끝내지 않게 한다.
    /// </summary>
    public override void NotifyAttackEnd()
    {
        if (_phase == ShieldPhase.Gather || _phase == ShieldPhase.Dash) return;

        if (_phase == ShieldPhase.Shock)
        {
            // 🔴 base.NotifyAttackEnd 로 곧장 빠지면 DecideNextAfterAction 만 돌고 시퀀스 정리가
            //    통째로 빠진다(히트 윈도우·돌진 속도·카운터 창). 정리를 지나는 경로로 끝낸다.
            Log("2단 클립 종료 이벤트로 마감");
            EndSequence();
            return;
        }

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

    // 진단 로그 — 단계가 **바뀔 때만** 찍는다(고빈도 로그는 정작 필요한 1회성 로그를 밀어낸다).
    void Log(string message)
    {
        if (!debugLog) return;
        Debug.Log($"[WallBot:{name}] t={Time.time:0.00} {message}", this);
    }
}
