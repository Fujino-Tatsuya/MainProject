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
    CasterDied          // 시전자 사망 — 쿨타임은 환불하지 않는다
}

/// <summary>
/// 모든 액티브 스킬의 추상 베이스. Player 루트에 부착하고, 판정 앵커는 Armature 하위 노드를 참조한다
/// (회전이 armature에만 적용되므로 방향성 판정은 반드시 앵커 기준).
/// 수명주기는 PlayerSkillController가 서버 권위로 호출한다 — 스킬은 RPC를 직접 갖지 않는다.
/// </summary>
public abstract class PlayerSkillBase : MonoBehaviour
{
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

    // FSM(PlayerSkillState) 위임 질의 — E는 이동 자유, R은 완전 잠금 등 스킬이 결정한다.
    public virtual bool CanMoveWhileActive => false;
    public virtual bool CanMovementRotateWhileActive => CanMoveWhileActive;

    public virtual void Initialize(Player owner, PlayerSkillController controller)
    {
        this.owner = owner;
        this.controller = controller;
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

    // 서버 전용: 홀드 조향 갱신 (오너가 주기 전송)
    public virtual void OnAimUpdated(Vector3 direction) { }

    // 서버 전용: 홀드 해제 통보
    public virtual void OnReleased() { }

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
}
