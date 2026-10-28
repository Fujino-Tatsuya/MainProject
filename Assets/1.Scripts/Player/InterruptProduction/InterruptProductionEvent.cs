using UnityEngine;

/// <summary>간파 사건 단계(할 일 118) — 스킬이 디렉터에 알리는 사건 4개.</summary>
public enum InterruptProductionPhase
{
    Attempt,
    Resolve,
    Success,
    Fail,
}

/// <summary>실패 사건의 사유.</summary>
public enum InterruptFailReason
{
    None,
    /// <summary>판정했지만 카운터 창을 하나도 맞히지 못했다(시도 슬로우가 있을 때만 의미).</summary>
    Missed,
    /// <summary>판정 전에 스킬이 끝났다(완료·취소·사망).</summary>
    EndedBeforeResolve,
    /// <summary>판정 앵커가 없어 판정을 못 했다.</summary>
    NoHitboxAnchor,
}

/// <summary>
/// 간파 사건 한 건(할 일 118 설계 §2.1). 서버 사건과 피어 사건이 같은 모양이고, 사건마다 채워지는 칸만 다르다.
/// 판정 결과를 담을 뿐 판정은 하지 않는다 — 판정은 스킬(서버)이 이미 끝냈다.
/// </summary>
public readonly struct InterruptProductionEvent
{
    public readonly InterruptProductionPhase Phase;
    /// <summary>간파한 사람 — 개인/공용 채널 기준.</summary>
    public readonly ulong CasterClientId;
    /// <summary>[서버 사건·피어 시도] 시전자. 피어 성공은 비어 있을 수 있다.</summary>
    public readonly Player Caster;
    /// <summary>[서버 사건·피어 시도] 간파 스킬 인스턴스(대체 슬롯 포함) — 캐릭터 구분.</summary>
    public readonly PlayerInterruptSkillBase Skill;
    /// <summary>[판정·실패] 시도 슬로우 발동 번호. 0 = 실패 복귀할 시도 슬로우 없음.</summary>
    public readonly uint AttemptTriggerId;
    /// <summary>[시도·서버] 승인 시 성공 예측(DashInterruptPredictor) — true = 강, false = 약 시도 슬로우(할 일 132). 예측 계산은 스킬이 한다.</summary>
    public readonly bool PredictsHit;
    /// <summary>[판정] 예측 대기 중 카운터 성공이 났는가.</summary>
    public readonly bool Landed;
    /// <summary>[판정] 카운터 창 소비가 한 건이라도 있었는가.</summary>
    public readonly bool Succeeded;
    /// <summary>[판정] 수신이 받아들여진 대상 수.</summary>
    public readonly int ResolvedCount;
    /// <summary>[성공] 카운터 창을 소비한 첫 대상의 판정 순간 위치. <see cref="HasTarget"/> 가 false 면 의미 없다.</summary>
    public readonly Vector3 TargetPosition;
    public readonly bool HasTarget;
    /// <summary>[성공] 사건 순간 시전자 위치 — 대상이 없을 때 연출 기준점.</summary>
    public readonly Vector3 CasterPosition;
    /// <summary>[실패] 사유.</summary>
    public readonly InterruptFailReason FailReason;
    /// <summary>[피어 성공] 슬로우 발동 번호(중복 방지용).</summary>
    public readonly uint TriggerId;

    private InterruptProductionEvent(InterruptProductionPhase phase, ulong casterClientId, Player caster, PlayerInterruptSkillBase skill,
        uint attemptTriggerId = 0, bool predictsHit = false, bool landed = false, bool succeeded = false, int resolvedCount = 0,
        Vector3 targetPosition = default, bool hasTarget = false, Vector3 casterPosition = default,
        InterruptFailReason failReason = InterruptFailReason.None, uint triggerId = 0)
    {
        Phase = phase;
        CasterClientId = casterClientId;
        Caster = caster;
        Skill = skill;
        AttemptTriggerId = attemptTriggerId;
        PredictsHit = predictsHit;
        Landed = landed;
        Succeeded = succeeded;
        ResolvedCount = resolvedCount;
        TargetPosition = targetPosition;
        HasTarget = hasTarget;
        CasterPosition = casterPosition;
        FailReason = failReason;
        TriggerId = triggerId;
    }

    /// <summary>성공 연출 기준 위치 — 대상이 있으면 대상, 없으면 시전자(할 일 87 C5).</summary>
    public Vector3 EffectPosition => HasTarget ? TargetPosition : CasterPosition;

    private static ulong ClientIdOf(Player caster) => caster != null ? caster.OwnerClientId : 0;

    /// <summary>[서버] 시도 — 스킬 승인 직후.</summary>
    public static InterruptProductionEvent Attempt(Player caster, PlayerInterruptSkillBase skill, bool predictsHit) =>
        Attempt(ClientIdOf(caster), caster, skill, predictsHit);

    public static InterruptProductionEvent Attempt(ulong casterClientId, Player caster, PlayerInterruptSkillBase skill, bool predictsHit) =>
        new InterruptProductionEvent(InterruptProductionPhase.Attempt, casterClientId, caster, skill, predictsHit: predictsHit);

    /// <summary>[서버] 판정 끝.</summary>
    public static InterruptProductionEvent Resolve(Player caster, PlayerInterruptSkillBase skill, uint attemptTriggerId,
        bool landed, bool succeeded, int resolvedCount) =>
        Resolve(ClientIdOf(caster), caster, skill, attemptTriggerId, landed, succeeded, resolvedCount);

    public static InterruptProductionEvent Resolve(ulong casterClientId, Player caster, PlayerInterruptSkillBase skill,
        uint attemptTriggerId, bool landed, bool succeeded, int resolvedCount) =>
        new InterruptProductionEvent(InterruptProductionPhase.Resolve, casterClientId, caster, skill,
            attemptTriggerId: attemptTriggerId, landed: landed, succeeded: succeeded, resolvedCount: resolvedCount);

    /// <summary>[서버] 성공 — 판정과 같은 호출 안, 판정 사건 바로 뒤.</summary>
    public static InterruptProductionEvent Success(Player caster, PlayerInterruptSkillBase skill, bool hasTarget, Vector3 targetPosition) =>
        Success(ClientIdOf(caster), caster, skill, hasTarget, targetPosition,
            caster != null ? caster.transform.position : default);

    public static InterruptProductionEvent Success(ulong casterClientId, Player caster, PlayerInterruptSkillBase skill,
        bool hasTarget, Vector3 targetPosition, Vector3 casterPosition) =>
        new InterruptProductionEvent(InterruptProductionPhase.Success, casterClientId, caster, skill,
            succeeded: true, targetPosition: targetPosition, hasTarget: hasTarget, casterPosition: casterPosition);

    /// <summary>[서버] 실패 — 판정 전 종료·앵커 누락. 빗나감은 판정 사건이 처리한다.</summary>
    public static InterruptProductionEvent Fail(Player caster, PlayerInterruptSkillBase skill, InterruptFailReason reason, uint attemptTriggerId) =>
        Fail(ClientIdOf(caster), caster, skill, reason, attemptTriggerId);

    public static InterruptProductionEvent Fail(ulong casterClientId, Player caster, PlayerInterruptSkillBase skill,
        InterruptFailReason reason, uint attemptTriggerId) =>
        new InterruptProductionEvent(InterruptProductionPhase.Fail, casterClientId, caster, skill,
            attemptTriggerId: attemptTriggerId, failReason: reason);

    /// <summary>[각 피어] 슬로우 Started(Success) 로 받은 성공 — 시전자 객체는 비어 있다(필요하면 받는 쪽이 찾는다).</summary>
    public static InterruptProductionEvent LocalSuccess(uint triggerId, ulong casterClientId, Vector3 targetPosition) =>
        new InterruptProductionEvent(InterruptProductionPhase.Success, casterClientId, null, null,
            succeeded: true, targetPosition: targetPosition, hasTarget: true, triggerId: triggerId);

    /// <summary>[각 피어] 시도 — 시전 시작(OnClientPlay). 예측은 서버만 알아 비어 있다.</summary>
    public static InterruptProductionEvent LocalAttempt(Player caster, PlayerInterruptSkillBase skill) =>
        new InterruptProductionEvent(InterruptProductionPhase.Attempt, ClientIdOf(caster), caster, skill);

    /// <summary>
    /// [각 피어] 캐릭터·대상 RPC 로 받은 cue(B3·C2 = 성공, E1 = 성공 직전 대상 수신). RPC 는 인자가 없어
    /// 시전자 id 만 아는 대로 채운다(대상 섬광은 0) — 받는 쪽은 cue 만 보고 튼다.
    /// </summary>
    public static InterruptProductionEvent LocalCue(InterruptProductionPhase phase, ulong casterClientId = 0, Player caster = null) =>
        new InterruptProductionEvent(phase, casterClientId, caster, null);

    /// <summary>[각 피어] 슬로우 Failed 로 받은 실패.</summary>
    public static InterruptProductionEvent LocalFail(uint triggerId) =>
        new InterruptProductionEvent(InterruptProductionPhase.Fail, 0, null, null, triggerId: triggerId);
}
