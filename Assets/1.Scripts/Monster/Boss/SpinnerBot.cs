using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

// 미들보스 SpinnerBot 전용 공격 확장.
// 기본 = Whip 근접(base 데미지 경로) / 특수 = "스핀"(카운터 창 → 돌진하며 회전공격).
//
// 스핀 시퀀스(모두 Attack 상태 안에서 SpinnerBot이 phase로 관리, 커밋 — 중간에 플레이어가 범위 밖 나가도 중단 X):
//  1) **카운터 창**: `MonsterCounterWindow` 저작값(기본 1.5초) 동안 준비 자세에서 **애니가 정지**한다.
//     방향은 시작 시 고정 → 옆으로 회피 가능. 이 동안 인터럽트 스킬을 맞으면 **성공**(아래).
//  2) 돌진: 바라보던 방향으로 이동(agent 속도 = MoveSpeed × 배수), navmesh 경계 클램프(NavMesh.Raycast)로
//     낭떠러지 진입 불가. 이동 중 Spin Attack Loop 유지 + **`dashRepeatInterval` 주기로 반복 히트**.
//  3) 종료: 속도 복귀 · 슈퍼아머 해제 · FSM 복귀.
//
// 🔴 **돌진 후 Dizzy 단계는 없앴다**(2026-09-08 팀장 확정). 인터럽트를 실패했는데도 그로기가 오면
//    "어차피 눕는다"가 되어 몹이 약해 보인다. 그로기는 **카운터 성공의 상자**로만 남는다.
//    같이 지운 것: `dizzyDuration`(2.5) · `dizzyBool`("IsDizzy") · `PlayDizzyClientRpc`.
//    되돌릴 값은 git 이력과 PLAN.md 에 있다 — 안 쓰는 필드를 남기면 CS0414 경고가 쌓이고,
//    그로기 애니는 이제 base 의 `groggyBool`(= 같은 `IsDizzy`) 경로가 담당한다.
//
// 애니는 상태복제가 아니라 ClientRpc로 확정(선택값 미복제 회피). Whip/Spin=CrossFade(트리거/Variation 배선 의존 X).
public class SpinnerBot : MonsterBase
{
    enum SpinPhase { None, Window, Dash }

    [Header("스핀 — 선택")]
    [SerializeField, Range(0f, 1f)]
    [Tooltip("공격 시 스핀을 고를 확률(나머지는 기본 Whip).")]
    float spinChance = 0.4f;
    [SerializeField, Min(0f)]
    [Tooltip("스핀 재사용 대기(초).")]
    float spinCooldown = 6f;

    [Header("스핀 — 타이밍/이동 (플레이테스트 튜닝)")]
    [SerializeField, Min(0f)]
    [Tooltip("카운터 창이 없을 때(컴포넌트 미부착·창 0초) 쓰는 준비 시간(초). 창이 있으면 창 길이가 이 값을 대신한다.")]
    float spinWindup = 1f;
    [SerializeField, Min(0.1f)]
    [Tooltip("돌진(회전공격) 지속(초).")]
    float dashDuration = 0.9f;
    [SerializeField, Min(0.05f)]
    [Tooltip("돌진 중 반복 히트 주기(초). 범위 안에 계속 있으면 이 주기마다 한 번 맞는다.")]
    float dashRepeatInterval = 0.5f;
    [SerializeField, Min(1f)]
    [Tooltip("돌진 속도 배수. 이동 속도 = MoveSpeed × 이 값(끝나면 복귀).")]
    float dashSpeedMultiplier = 8f;
    [SerializeField, Min(0.1f)]
    [Tooltip("돌진 최대 거리(m). navmesh 경계에서 잘림(낙하 방지).")]
    float dashMaxDistance = 16f;
    [Header("애니 상태 (컨트롤러 일치)")]
    [SerializeField] string spinStartState = "Spin Attack Start";
    [SerializeField] string spinLoopState = "Spin Attack Loop";
    [SerializeField] string whipStateR = "Attack Whip R Start";
    [SerializeField] string whipStateL = "Attack Whip L Start";
    [SerializeField]
    [Tooltip("스핀 이펙트의 id. 클립 이벤트 문자열(A_Spinner_AttackStart 의 StartEffect data)과 " +
             "프리팹 EffectSocketPlayer.Id 가 쓰는 그 값이다. 아트가 바꾸면 여기도 고칠 것.")]
    string spinEffectId = "Spin";

    // 서버 전용 스핀 런타임
    SpinPhase _phase;
    float _phaseTimer;          // 현재 단계의 남은 시간(초)
    float _nextRepeatHitTime;   // 다음 반복 히트 시각
    Vector3 _dashDir;
    float _lastSpinTime = -999f;
    bool _whipUseR;

    MonsterCounterWindow _counter;
    EffectAnimEvents _effects;

    bool SpinReady => Time.time - _lastSpinTime >= spinCooldown;

    /// <summary>카운터 창 컴포넌트(없으면 null = 카운터 없는 몹으로 동작).</summary>
    MonsterCounterWindow Counter =>
        _counter != null ? _counter : (_counter = GetComponent<MonsterCounterWindow>());

    // 준비 구간 길이 — 창이 저작돼 있으면 창 길이가 곧 준비 시간이다.
    float WindupDuration =>
        Counter != null && Counter.WindowDuration > 0f ? Counter.WindowDuration : spinWindup;

    protected override void StartAttack()
    {
        bool doSpin = SpinReady && Random.value < spinChance;

        base.StartAttack(); // StopAgent + FaceTarget + (옵션)슈퍼아머 + SetState(Attack)

        if (!doSpin)
        {
            _phase = SpinPhase.None;
            _whipUseR = !_whipUseR;         // 좌우 번갈아
            PlayWhipClientRpc(_whipUseR);   // 기본 Whip 애니(데미지는 base.HandleAttack가 처리)
            return;
        }

        _lastSpinTime = Time.time;
        _dashDir = transform.forward;   // FaceTarget 후 전방 = 돌진 방향(고정)

        // 🔴 base 의 attackDuration 을 스핀 전체 길이로 덮어쓴다. 애니를 멈춰도 이 타이머는 계속 줄기
        //    때문에(MonsterBase.HandleAttack) 창 길이를 반드시 더해야 한다 — 안 더하면 돌진 전에 Attack 이 끝난다.
        _stateTimer = WindupDuration + dashDuration;

        // 준비+돌진 동안만 슈퍼아머(있으면). 종료 시 해제한다.
        if (data != null && data.hasSuperArmorWhileAttacking && status != null)
            status.ApplyStatus(StatusEffectType.SuperArmor, WindupDuration + dashDuration);

        PlaySpinStartClientRpc();

        _phase = SpinPhase.Window;
        _phaseTimer = WindupDuration;

        // 창이 저작돼 있으면 자세를 정지시켜 "끊을 수 있다"를 보여 준다. 없으면 예전처럼 그냥 회전한다.
        if (Counter != null)
        {
            Counter.Open();
            if (Counter.IsOpen) ServerSetCounterWindow(true);
        }
    }

    protected override void HandleAttack(float dt)
    {
        if (_phase == SpinPhase.None)
        {
            base.HandleAttack(dt);          // 기본 Whip 경로(애니 이벤트 히트 + 종료)
            return;
        }

        _stateTimer -= dt;
        _phaseTimer -= dt;

        switch (_phase)
        {
            case SpinPhase.Window:
                HoldAgent();                // 제자리 정지 + 방향 고정(재조준 X)
                // 창 만료 = **인터럽트 실패 확정**. 자세를 풀어 돌진으로 넘어간다.
                if (Counter != null) Counter.TickAndDetectExpiry(dt);
                if (_phaseTimer <= 0f) BeginDash();
                break;

            case SpinPhase.Dash:
                // 반복 히트: BeginHitWindow 가 "유닛당 1회" 집합을 비우므로 주기마다 다시 부르면 재히트가 된다.
                // ⚠️ 유닛별 0.5초 쿨다운이 아니라 **집합 리셋 경계**다 — 경계 직전/직후에 연속 2히트가 가능하다.
                if (Time.time >= _nextRepeatHitTime)
                {
                    meleeAttack?.BeginHitWindow();
                    _nextRepeatHitTime = Time.time + Mathf.Max(0.05f, dashRepeatInterval);
                }
                meleeAttack?.Hit();

                if (_phaseTimer <= 0f) EndSpin();
                break;
        }

        // 안전망 — 단계 타이머가 어긋나도 Attack 에 영구 고착되지 않게 한다.
        if (_stateTimer <= 0f && _phase != SpinPhase.None) EndSpin();
    }

    // 창 종료 → 돌진 시작. 자세 홀드를 풀어 클립을 이어 재생한다.
    void BeginDash()
    {
        if (Counter != null)
        {
            Counter.Close();
            ServerSetCounterWindow(false);
        }

        _phase = SpinPhase.Dash;
        _phaseTimer = dashDuration;
        _nextRepeatHitTime = 0f;    // 첫 틱에 바로 열린다

        PlaySpinLoopClientRpc();
        StartDash();
    }

    // 돌진 종료 — 히트창 종료 · 속도 복귀 · 정지 · 슈퍼아머 해제.
    // 🔴 이 넷은 예전에 Dizzy 진입 분기에 묶여 있었다. Dizzy 를 없앤 지금은 여기가 유일한 정리 지점이다.
    void EndSpin()
    {
        meleeAttack?.EndHitWindow();
        if (agent != null) agent.speed = MoveSpeed;
        HoldAgent();
        status?.RemoveStatus(StatusEffectType.SuperArmor);

        _phase = SpinPhase.None;
        DecideNextAfterAction();
    }

    /// <summary>
    /// 인터럽트 카운터 판정 지점. 데미지는 base 가 먼저 처리하고, 창이 열려 있을 때만 성공으로 센다.
    ///
    /// 🔴 창 <b>밖</b>의 인터럽트는 데미지만 남는다 — 누적식(`maxGroggyCount`)은 데이터에서 0 으로 껐다.
    /// </summary>
    public override void TakeDamage(AttackInfo attackInfo)
    {
        base.TakeDamage(attackInfo);

        if (!IsServer || !attackInfo.isInterruptAttack || Counter == null) return;
        if (!Counter.TryConsumeInterrupt()) return;

        CounterSucceeded();
    }

    // 카운터 성공 — 돌진을 취소하고 즉시 그로기. 그로기 애니는 `IsDizzy` 불로 컨트롤러가 잡는다
    // (2026-09-08 SVN r292 에서 `AnyState → Dizzy` 전이를 넣었다. 그 전에는 논리만 그로기였다).
    void CounterSucceeded()
    {
        ServerSetCounterWindow(false);

        meleeAttack?.EndHitWindow();
        if (agent != null) agent.speed = MoveSpeed;
        status?.RemoveStatus(StatusEffectType.SuperArmor);

        _phase = SpinPhase.None;
        ForceGroggy(Counter.GroggyDuration);
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
    // 그 외 상태는 base(Groggy 에서 groggyBool=IsDizzy 토글 → 상태 이탈 시 Dizzy 자동 해제 포함).
    protected override void PlayStateAnimation(MonsterState s)
    {
        if (s == MonsterState.Attack) return;
        base.PlayStateAnimation(s);

        // 🔴 스핀 이펙트를 코드에서 끈다 (2026-09-09 Play 에서 잡힘).
        //    `StartEffect "Spin"` 은 `A_Spinner_AttackStart` 에 있는데 `StopEffect "Spin"` 은
        //    **`A_Spinner_Dizzy` 에만** 있다. 그런데 이 파일 상단대로 **돌진 후 Dizzy 단계를 없앴다**
        //    → 인터럽트를 실패하면 그 클립을 안 지나가서 종료 이벤트가 영영 오지 않는다.
        //    (성공하면 ForceGroggy → IsDizzy → Dizzy 클립이 돌아 정상적으로 꺼졌다. 그래서
        //     카운터 경로만 멀쩡해 보였다.)
        //    `safetyTimeout`(5초)이 강제 회수하긴 하지만 그동안 이펙트가 남고 경고가 쌓인다.
        //    ⚠️ `EndSpin()` 한 곳만 막으면 사망·리쉬·넉백 이탈에서 다시 샌다 —
        //       그래서 상태 이탈 지점에서 끈다(GauntletBot 의 smashTelegraph 와 같은 규약).
        StopSpinEffect();
    }

    /// <summary>
    /// 스핀 이펙트를 끈다. 이미 꺼져 있으면 무해한 no-op 이다
    /// (<c>EffectSocketPlayer.Stop</c> 이 <c>_handle.IsSet</c> 으로 가드한다).
    /// 전 피어에서 불린다 — 이펙트는 로컬 연출이라 서버로 게이트하면 호스트에서만 꺼진다.
    /// </summary>
    void StopSpinEffect()
    {
        if (_effects == null && !TryGetComponent(out _effects)) return;
        _effects.StopEffect(spinEffectId);
    }

    [ClientRpc] void PlaySpinStartClientRpc() => SafeCrossFade(spinStartState);
    [ClientRpc] void PlaySpinLoopClientRpc() => SafeCrossFade(spinLoopState);
    [ClientRpc] void PlayWhipClientRpc(bool useR) => SafeCrossFade(useR ? whipStateR : whipStateL);
}
