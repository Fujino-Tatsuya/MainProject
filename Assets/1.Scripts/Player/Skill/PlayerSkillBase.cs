using UnityEngine;

// 스킬 내부 수명주기 상태. FSM(PlayerActionState)과 별개의 스킬 자체 장부.
public enum SkillState
{
    Ready,      // 시전 가능
    Charging,   // 홀드 유지 중
    Channeling, // 캐스트/정신집중 중
    Active,     // 효과 발동 중
    Cooldown    // 재사용 대기 (서버 장부는 PlayerSkillController가 관리)
}

// 스킬 클립에 심는 애니메이션 이벤트. Custom은 스킬 고유 타이밍(R 면역 구간, Q 견인 시작 등).
public enum SkillAnimationEventType
{
    Hit = 0,
    End = 1,
    Custom0 = 2,
    Custom1 = 3
}

public enum SkillEndReason
{
    Completed,          // 정상 완료 (End 이벤트 / 채널 완주)
    Released,           // 홀드 해제
    MaxDurationReached, // 서버 안전망 강제 종료
    Cancelled,          // 외부 요인 (넉백/그랩 등 상태 전환)
    CasterDied,         // 시전자 사망 — 쿨타임은 환불하지 않는다
    DashCancelled       // 공용 대시로 끊김(CanBeCanceledByDash 인 단계에서만 일어난다)
}

/// <summary>
/// 모든 액티브 스킬의 추상 베이스. Player 루트에 부착하고, 판정 앵커는 Armature 하위 노드를 참조한다
/// (회전이 armature에만 적용되므로 방향성 판정은 반드시 앵커 기준).
/// 수명주기는 PlayerSkillController가 서버 권위로 호출한다 — 스킬은 RPC를 직접 갖지 않는다.
/// </summary>
public abstract class PlayerSkillBase : MonoBehaviour, IAnimationEventRequirementSource
{
    private static readonly SkillAnimationEventType[] NoRequiredEvents = System.Array.Empty<SkillAnimationEventType>();

    [SerializeField] private PlayerSkillData data;
    // 판정 기준점 (MainSkill/SubSkill/InterruptAttack/UltimateSkill 앵커). E처럼 판정 없는 스킬은 비워도 된다.
    [SerializeField] private ColliderInfo hitboxAnchor;

    protected Player owner;
    protected PlayerSkillController controller;
    protected int damageSnapshot;

    public PlayerSkillData Data => data;
    public ColliderInfo HitboxAnchor => hitboxAnchor;
    public SkillState State { get; protected set; } = SkillState.Ready;

    // GroundPoint 조준으로 확정된 지면 지점 (월드). 컨트롤러가 OnServerStart/OnClientPlay 직전에 세팅한다.
    // SingleTarget/None 스킬은 무시. HasAimPoint로 유효성 판별.
    public Vector3 AimPoint { get; private set; }
    public bool HasAimPoint { get; private set; }

    public abstract PlayerSkillSlot Slot { get; }

    // 슬롯은 PlayerSkillController 바인딩과 각 구현의 Slot 계약으로 이미 검증된다.
    // 우클릭 Interrupt는 Q/E/R 슬롯이 아니므로 범용 Skill로 남긴다.
    protected AttackType DamageAttackType => Slot switch
    {
        PlayerSkillSlot.Main => AttackType.SkillQ,
        PlayerSkillSlot.Sub => AttackType.SkillE,
        PlayerSkillSlot.Ultimate => AttackType.SkillR,
        _ => AttackType.Skill
    };

    protected AttackHitPattern DamageHitPattern =>
        data != null ? data.HitPattern : AttackHitPattern.Single;

    // FSM(PlayerSkillState) 위임 질의 — E는 이동 자유, R은 완전 잠금 등 스킬이 결정한다.
    public virtual bool CanMoveWhileActive => false;
    public virtual bool CanMovementRotateWhileActive => CanMoveWhileActive;

    // 공용 대시가 이 스킬을 끊을 수 있는가 — phase = 현재 스킬 상태(Skill/Focus). 기본 아니오(기존 동작: 스킬 중 대시 입력 무시).
    // 끊기면 OnEnd(SkillEndReason.DashCancelled).
    public virtual bool CanBeCanceledByDash(PlayerActionState phase) => false;

    // 이 스킬의 조준 모드(SingleTarget/GroundPoint)를 공용 대시가 취소할 수 있는가. 기본 아니오(조준 중 대시 입력 무시).
    public virtual bool CanCancelAimByDash => false;

    // 시전 시 들어갈 상태기계 상태(스킬 상태 계열). 기본 Skill, 거너 Q = Focus(발사 시 Skill 로 넘어감).
    public virtual PlayerActionState EntryActionState => PlayerActionState.Skill;

    // 실행 중 좌클릭을 이 스킬이 받는가(거너 Q: 좌클릭 = 발사). true 면 컨트롤러가 오너의 좌클릭을 OnPrimaryPressed 로 넘긴다.
    public virtual bool ConsumesPrimaryInput => false;

    // 실행 중 같은 슬롯 재입력을 이 스킬이 받는가(팔라딘 E: 방어중 재입력 = 해제, 할 일 94). true 면 컨트롤러가 오너의 재입력을
    // 선입력 버퍼에서 지우고 OnSkillRePressed 로 넘긴다 — 받을지 말지는 스킬이 서버에서 정한다(안 받으면 그 입력은 그냥 사라진다).
    public virtual bool ConsumesSkillRePress => false;

    // [오너] 실행 중 같은 슬롯 키의 누름/뗌을 서버로 보고하는가(팔라딘 E 홀드 방식, 할 일 95). true 면 컨트롤러가 시작 첫 틱과
    // 바뀔 때마다 OnSkillHeldChanged 로 넘긴다. 오너 설정으로 정해지므로 오너에서만 의미가 있다(서버는 받은 통보로 판단한다).
    // Hold 입력 타입 스킬(OnReleased)과 별개다 — 이 스킬은 Press 로 시작하고 해제 여부는 스킬이 서버에서 정한다.
    public virtual bool ReportsHeldInput => false;

    // [서버] 타이머 안전망(maxActiveDuration)에 더할 초. 실행 길이가 조작에 따라 늘어나는 스킬(팔라딘 E 홀드)이 쓴다. 공격속도와 무관하게 더한다.
    public virtual float SafetyNetExtraSeconds => 0f;

    // 실행 중 오너 조준을 서버로 주기 전송할지. 기본은 홀드 스킬만(기존 동작).
    public virtual bool WantsAimUpdates => data != null && data.InputType == PlayerSkillInputType.Hold;

    /// <summary>
    /// 이 스킬이 상태 클립(<see cref="PlayerSkillData.AnimatorStateName"/>)에 요구하는 이벤트. 판정·종료·연출 시점을 이벤트로 정하는 스킬이 선언한다.
    /// 빠지면 에디터 경고(<see cref="Initialize"/>)와 EditMode 전수 검사(PlayerAnimationEventRequirementTests)가 잡는다.
    /// </summary>
    public virtual System.Collections.Generic.IReadOnlyList<SkillAnimationEventType> RequiredAnimationEvents => NoRequiredEvents;

    /// <summary>
    /// 이벤트 방식인가 — 요구 이벤트(<see cref="RequiredAnimationEvents"/>)에 End 가 있으면 참. 종료를 상태 클립 End 로 정하고,
    /// 서버 안전망은 maxActiveDuration 이 아니라 그 클립 실제 재생 시간 + padding 이다(<see cref="PlayerSkillSafetyNet"/>).
    /// 거짓 = 타이머 방식(안전망 = maxActiveDuration + padding). 채널·간파 베이스도 이 값 하나로 갈린다.
    /// </summary>
    public bool EndsOnClipEvent
    {
        get
        {
            System.Collections.Generic.IReadOnlyList<SkillAnimationEventType> events = RequiredAnimationEvents;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i] == SkillAnimationEventType.End)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 공격속도를 타는가(할 일 91 T5). 참이면 실행 중 Animator.speed = 배율이고 서버 임시 안전망도 배율로 나눈다(<see cref="PlayerSkillController"/>).
    /// 기본 = 이벤트 방식(<see cref="EndsOnClipEvent"/>) — 판정·종료가 클립 이벤트라 클립과 같이 빨라진다.
    /// 타이머 방식(가붕이 Q 홀드, 거너 Q 충전·E·R)은 판정·종료가 코드 시간이라 애니만 빨라지면 어긋나므로 타지 않는다.
    /// 예외는 스킬이 덮는다 — 클립 이벤트로만 도는데 End 를 선언하지 않은 스킬(가붕이 E)은 참, 이벤트 방식이어도
    /// 고정 속도·거리 이동이 동작을 정하는 스킬(어쌔신 Q 돌진)은 거짓.
    /// 상태별로 나눠 타는 스킬(팔라딘 E — 방어중만 제외)은 거짓으로 두고 애니메이터 상태 속도 파라미터(AttackSpeedRate)로 탄다.
    /// </summary>
    public virtual bool ScalesWithAttackSpeed => EndsOnClipEvent;

    /// <summary>
    /// 서버 타이머 안전망(maxActiveDuration)을 공격속도로 늘리는가(<see cref="PlayerAttackSpeedRules.SafetyNetDuration"/>, 감속만).
    /// 기본 = <see cref="ScalesWithAttackSpeed"/>. 상태 파라미터로 일부 상태만 타는 스킬(팔라딘 E)이 따로 켠다.
    /// </summary>
    public virtual bool ScalesSafetyNetWithAttackSpeed => ScalesWithAttackSpeed;

    // 시작 상태 말고 다른 상태(코드 상수로 CrossFade 하는 상태 등)에도 요구가 있는 스킬은 base 를 부른 뒤 덧붙인다(팔라딘 E 반격 상태).
    public virtual void CollectAnimationEventRequirements(System.Collections.Generic.List<AnimationEventRequirement> requirements)
    {
        if (data == null || string.IsNullOrEmpty(data.AnimatorStateName))
            return;

        foreach (SkillAnimationEventType eventType in RequiredAnimationEvents)
            requirements.Add(AnimationEventRequirement.ForSkillState(data.AnimatorStateName, eventType));
    }

    public virtual void Initialize(Player owner, PlayerSkillController controller)
    {
        this.owner = owner;
        this.controller = controller;
        AnimationEventRequirements.WarnMissing(this, owner != null ? owner.GetComponentInChildren<Animator>(true) : null, this);
    }

    // 쿨타임/FSM 외 추가 시전 조건 (사거리, 대상 생존 등). 서버 승인 시점에만 호출된다.
    public virtual bool CanUse(Vector3 direction, Unit target)
    {
        return true;
    }

    public void SetDamageSnapshot(int value)
    {
        damageSnapshot = Mathf.Max(0, value);
    }

    public void SetAimPoint(Vector3 point, bool hasPoint)
    {
        AimPoint = point;
        HasAimPoint = hasPoint;
    }

    public void ResetToReady()
    {
        if (State == SkillState.Cooldown)
            State = SkillState.Ready;
    }

    // ── 수명주기 (PlayerSkillController 호출) ──

    // 서버 전용: 판정/상태부여의 시작점
    public abstract void OnServerStart(Vector3 direction, Unit target);

    // 모든 피어(호스트 포함): 연출 시작
    public abstract void OnClientPlay(Vector3 direction);

    // 서버 전용: 실행 중 매 프레임 (틱 피해, 이동 갱신 등)
    public virtual void OnTick() { }

    // 서버 전용: 애니메이션 이벤트 수신 (판정 ON 등)
    public virtual void OnAnimationEvent(SkillAnimationEventType eventType) { }

    // 모든 피어(이 스킬이 실행 중으로 보이는 피어 — 서버·오너·관전 클라): 이 피어 애니메이터의 클립 이벤트.
    // 오너 이동 시작(owner.IsSimulating 일 때만)·로컬 연출 시점용이다. 🔴 판정·피해·상태 변경 금지 — 그건 OnAnimationEvent(서버).
    // 서버에서는 같은 이벤트로 이것이 먼저, OnAnimationEvent 가 뒤에 불린다.
    public virtual void OnLocalAnimationEvent(SkillAnimationEventType eventType) { }

    // 서버 전용: 이벤트 방식(EndsOnClipEvent) 스킬의 상태 클립 실제 길이를 컨트롤러가 읽었다(시전 뒤 애니메이터 첫 갱신 후,
    // 그리고 원격 오너 동기로 상태가 다시 시작돼 안전망을 늦출 때마다).
    // remainingSeconds = 지금부터 클립 끝까지 실제 초. 안전망은 이 시각 + padding 에 끊는다. 클립 길이로 맞출 보호 지속(R 슈퍼아머)용.
    public virtual void OnClipPlaybackResolved(float remainingSeconds) { }

    // 서버 전용: 홀드 조향 갱신 (오너가 주기 전송)
    public virtual void OnAimUpdated(Vector3 direction) { }

    // 서버 전용: 홀드 해제 통보
    public virtual void OnReleased() { }

    // 서버 전용: 실행 중 좌클릭(ConsumesPrimaryInput 일 때만). direction = 누른 순간의 조준.
    public virtual void OnPrimaryPressed(Vector3 direction) { }

    // 서버 전용: 실행 중 같은 슬롯 재입력(ConsumesSkillRePress 일 때만). 원격 오너면 RPC 지연만큼 늦게 온다 — 지금 서버 상태로 판정한다.
    public virtual void OnSkillRePressed() { }

    // 서버 전용: 실행 중 같은 슬롯 키 누름/뗌(오너가 ReportsHeldInput 일 때만 보낸다). 첫 통보는 시작 직후, 그 뒤는 바뀔 때마다.
    public virtual void OnSkillHeldChanged(bool held) { }

    // 서버 전용: 오너 권위 결과 보고(PlayerSkillController.ReportOwnerSkillResult). 값 검증은 스킬 몫이다(어쌔신 Q: 실제 이동 거리).
    public virtual void OnOwnerResultReported(float value) { }

    // 오너 + 서버(FSM 을 틱하는 피어): 실행 중 물리 틱. 스킬 자체 이동은 여기서 owner.IsSimulating 일 때만 모터에 제출한다.
    public virtual void OnFixedTick() { }

    // 오너 전용: 실행 중 매 프레임(조준 방향 회전 등 로컬 조작 반영).
    public virtual void OnOwnerTick(Vector3 aimDirection) { }

    // 오너 전용: 조준 모드에 들어갔다(PlayerSkillTargeting.Begin). "시전했다"가 아니라 "조준을 켰다"는 신호다.
    // 🔴 오너에서만 돈다 — 여기서 연출을 바로 켜면 남의 화면에는 보이지 않는다. 전파는 NetworkBehaviour 창구의 몫이다.
    public virtual void OnOwnerAimStart() { }

    // 오너 전용: 조준이 시전으로 이어지지 않고 물렸다(취소 입력·빈 곳 클릭·자동 접근 중단·사망·권한 상실).
    // 시전으로 이어졌으면 호출되지 않는다 — 그쪽 연출 수명은 OnClientPlay/OnEnd 가 이어받는다.
    public virtual void OnOwnerAimCancelled() { }

    // 종료/취소 단일 정리 경로. 모든 피어에서 호출될 수 있으므로 로컬 정리만 담당한다.
    public virtual void OnEnd(SkillEndReason reason)
    {
        State = SkillState.Cooldown;
    }

    // 스킬이 스스로 종료를 요청할 때 사용 (서버에서만 실제 동작)
    protected void EndSelf(SkillEndReason reason)
    {
        if (controller != null)
            controller.EndActiveSkillServer(reason);
    }

    // ── 판정 공용 유틸 (서버 전용 경로에서만 호출할 것) ──

    /// <summary>
    /// 판정 앵커(<see cref="HitboxAnchor"/>)의 콜라이더 형태로 Overlap 질의한다.
    /// 앵커는 아마추어 하위 노드이므로 회전이 반영된 방향성 판정이 된다.
    /// </summary>
    /// <returns>results에 채워진 개수. 앵커/데이터가 없으면 0.</returns>
    protected int OverlapHitboxAnchor(Collider[] results)
    {
        if (hitboxAnchor == null || data == null || results == null)
            return 0;

        switch (hitboxAnchor.OverlapCollider)
        {
            case OverlapCollider.Box:
                BoxColliderInfo boxInfo = default;
                hitboxAnchor.GetBoxColliderInfo(ref boxInfo);
                return Physics.OverlapBoxNonAlloc(
                    boxInfo.center, boxInfo.halfExtents, results, boxInfo.orientation,
                    data.HittableLayers, QueryTriggerInteraction.Collide);

            case OverlapCollider.Sphere:
                SphereColliderInfo sphereInfo = default;
                hitboxAnchor.GetSphereColliderInfo(ref sphereInfo);
                return Physics.OverlapSphereNonAlloc(
                    sphereInfo.center, sphereInfo.radius, results,
                    data.HittableLayers, QueryTriggerInteraction.Collide);

            case OverlapCollider.Capsule:
                CapsuleColliderInfo capsuleInfo = default;
                hitboxAnchor.GetCapsuleColliderInfo(ref capsuleInfo);
                return Physics.OverlapCapsuleNonAlloc(
                    capsuleInfo.point0, capsuleInfo.point1, capsuleInfo.radius, results,
                    data.HittableLayers, QueryTriggerInteraction.Collide);

            default:
                return 0;
        }
    }

    /// <summary>
    /// 히트 콜라이더에서 피해 수신자를 해석한다. Hurtbox가 있으면 그쪽이 우선(소유 Unit 반환).
    /// </summary>
    protected static Unit ResolveHitUnit(Collider hit, out Hurtbox hurtbox)
    {
        hurtbox = hit.GetComponentInParent<Hurtbox>();
        if (hurtbox != null && hurtbox.TryGetOwner(out Unit hurtboxOwner))
            return hurtboxOwner;

        return hit.GetComponentInParent<Unit>();
    }
}
