using System;
using BeaverLobby.Player.Dash;
using UnityEngine;

/// <summary>예측·판정 결론(할 일 191 §3.1 ②).</summary>
public enum InterruptJudgeOutcome : byte
{
    /// <summary>판정 상자·정면 각도 안에 간파 대상이 없다(지목한 대상도 없다).</summary>
    NoTarget = 0,
    Failure = 1,
    Success = 2,
}

/// <summary>
/// 실패 이유(할 일 191 D12 QA 기록 칸). 값 순서 = 검사 순서 — 대상이 여럿이면 더 뒤 단계까지 간(성공에 가까운) 실패를 고른다.
/// </summary>
public enum InterruptJudgeFailReason : byte
{
    None = 0,
    /// <summary>판정 상자가 대상에 닿지 않는다(범위).</summary>
    OutOfRange = 1,
    /// <summary>판정 위치가 대상 정면 허용각 밖(23호 정면 판정 등).</summary>
    Angle = 2,
    /// <summary>시전자가 생존 상태가 아니다.</summary>
    CasterDown = 3,
    /// <summary>시전자가 상태 전환 CC 중(넉백·끌기·잡기) — 간파 불가·끊김(D5).</summary>
    CrowdControlled = 4,
    /// <summary>대상이 간파 불가 상태(취약·제압).</summary>
    TargetBlocked = 5,
    /// <summary>대상에 판정 시각 이전에 열린 창 기록이 없다.</summary>
    NoWindow = 6,
    /// <summary>판정 시각에 창이 이미 닫혔다(예정 닫힘·조기 닫힘 반영).</summary>
    WindowClosed = 7,
    /// <summary>창에 지목자가 따로 있다(레이드 연쇄 간파).</summary>
    NotDesignated = 8,
}

/// <summary>
/// 허용 오차(할 일 191 D11) — 서버 검증(3단계)이 서버 이력으로 같은 함수를 다시 돌릴 때 넣는다. 클라 예측·판정은 <see cref="Zero"/>.
/// </summary>
public struct InterruptJudgeTolerance
{
    /// <summary>창 열림·닫힘 시각 여유(초) — 열림은 그만큼 일찍, 닫힘은 그만큼 늦게 인정한다.</summary>
    public double TimeSlack;
    /// <summary>판정 상자 수평 여유(m) — 상자 반폭·반길이(구·캡슐은 반지름)를 그만큼 키운다.</summary>
    public float DistanceSlack;
    /// <summary>대상 정면 허용 반각 여유(도).</summary>
    public float AngleSlack;

    public static InterruptJudgeTolerance Zero => default;
}

/// <summary>
/// 누르는 순간 예측 입력(할 일 191 §3.1 ②) — 시각은 서버 시각. 판정 시각 = 근접은 누른 시각 + Hit 지연, 대쉬는 max(실제 대시 끝, 누른 시각 + Hit 지연).
/// </summary>
public struct InterruptCastRequest
{
    /// <summary>시전자 NetworkObjectId.</summary>
    public ulong ActorId;
    public bool ViaDash;
    /// <summary>누른 시각(서버 시각).</summary>
    public double PressTime;
    /// <summary>근접 = 시전(조준) 방향. 대쉬 간파는 쓰지 않는다(대시 방향).</summary>
    public Vector3 AttackDirection;
    /// <summary>누름 → Hit(초) — <see cref="InterruptHitDelay"/>(클립 Hit 이벤트 시각 ÷ 지금 애니 속도).</summary>
    public float HitDelay;
}

/// <summary>
/// Hit 타이밍 판정 입력(할 일 191 §3.1 ②) — 시전자 위치·상태·대상·창을 <see cref="JudgeTime"/> 의 세계 스냅샷에서 읽는다.
/// 클라 = 지금(로컬 이력), 서버 검증 = 클라가 보낸 판정 시각(서버 이력).
/// </summary>
public struct InterruptJudgeAttempt
{
    public ulong ActorId;
    /// <summary>판정 시각(서버 시각).</summary>
    public double JudgeTime;
    /// <summary>판정 상자가 향할 방향 — 근접 = 시전 방향, 대쉬 = 대시 방향(예측 결과의 <see cref="InterruptJudgeResult.AttackDirection"/>).</summary>
    public Vector3 AttackDirection;
    /// <summary>예측이 고른 대상(0 = 없음). 성공 대상이 없으면 이 대상의 실패 이유를 낸다.</summary>
    public ulong TargetId;

    /// <summary>예측 결과로 판정 입력을 만든다 — 같은 판정 시각·공격 방향·대상.</summary>
    public static InterruptJudgeAttempt FromPrediction(ulong actorId, in InterruptJudgeResult prediction) =>
        new InterruptJudgeAttempt
        {
            ActorId = actorId,
            JudgeTime = prediction.JudgeTime,
            AttackDirection = prediction.AttackDirection,
            TargetId = prediction.TargetId,
        };
}

/// <summary>예측·판정 결과(할 일 191 §3.1 ②).</summary>
public struct InterruptJudgeResult
{
    public InterruptJudgeOutcome Outcome;
    public InterruptJudgeFailReason FailReason;
    /// <summary>성공·실패 대상 NetworkObjectId(대상 없음 = 0).</summary>
    public ulong TargetId;
    /// <summary>판정에 쓴 창 번호(창 없음 = 0).</summary>
    public uint WindowId;
    /// <summary>판정 시각(서버 시각).</summary>
    public double JudgeTime;
    /// <summary>누른 시각 + Hit 지연(판정 입력은 판정 시각과 같다).</summary>
    public double HitTime;
    /// <summary>대시 끝 시각(근접 = 누른 시각, 판정 입력 = 판정 시각).</summary>
    public double DashEndTime;
    /// <summary>판정 위치 — 근접 = 시전자 위치, 대쉬 = 실제 대시 끝.</summary>
    public Vector3 ResolvePosition;
    /// <summary>판정 상자가 향한 방향(평면 정규화).</summary>
    public Vector3 AttackDirection;
    /// <summary>대시가 유닛에 막혀 조기 종료한다(예측만).</summary>
    public bool DashEndedByUnit;
    /// <summary>대시가 벽·지형에 막혀 덜 간다(예측만).</summary>
    public bool DashBlockedByWall;

    public bool IsSuccess => Outcome == InterruptJudgeOutcome.Success;

    /// <summary>
    /// 연출 3상태 — 기존 <see cref="DashInterruptPrediction.Forecast"/> 와 같은 뜻: 대상 없음 = 상자·정면각 안에 대상 없음,
    /// 약 = 대상은 있는데 실패(창 닫힘 등), 강 = 성공. 예측(Predict)은 대상을 지목하지 않아 범위·각도 실패가 곧 대상 없음이다.
    /// </summary>
    public InterruptAttemptForecast Forecast =>
        Outcome == InterruptJudgeOutcome.Success ? InterruptAttemptForecast.Strong
        : Outcome == InterruptJudgeOutcome.Failure ? InterruptAttemptForecast.Weak
        : InterruptAttemptForecast.NoTarget;
}

/// <summary>
/// 예측·판정이 읽는 세계(할 일 191 §3.1 ①) — 런타임 = <see cref="InterruptManager"/>(세계 스냅샷 이력·창 시간표·물리 오버랩·대시 경로).
/// EditMode 는 가짜 세계로 바꿔 끼운다. 시각은 전부 서버 시각.
/// </summary>
public interface IInterruptJudgeWorld
{
    bool TryGetActorAt(ulong actorId, double time, out InterruptActorSample sample);

    int TargetCount { get; }
    ulong GetTargetId(int index);
    bool TryGetTargetAt(ulong targetId, double time, out InterruptTargetSample sample);

    /// <summary><paramref name="time"/> 이전(포함)에 열린 창 중 가장 늦게 열린 것(열림·닫힘 무관).</summary>
    bool TryGetLatestWindowOpenedBy(ulong targetId, double time, out InterruptWindowRecord record);

    /// <summary>
    /// 판정 상자(<paramref name="shape"/>, 월드)가 대상에 닿는가 — 대상이 <paramref name="targetPosition"/> 에 있다고 보고 묻는다
    /// (런타임 = 대상의 지금 콜라이더를 그 위치로 옮겨 본 것과 같게 상자를 반대로 옮겨 오버랩, 레이어 = <paramref name="layers"/>).
    /// </summary>
    bool ShapeHitsTarget(ulong targetId, in HitboxAnchorShape shape, Vector3 targetPosition, int layers);

    /// <summary>시전자의 대시를 <paramref name="start"/> 에서 끝까지 굴린 결과 — 실제 대시 이동과 같은 충돌 검사(<see cref="PlayerMotor.PredictDashPath"/>).</summary>
    PlayerDashPathResult SimulateDash(ulong actorId, Vector3 start, Vector3 direction, float speed, float remaining);
}

/// <summary>
/// 간파 예측·판정 순수 계산(할 일 191 §3.1 ② — 스킬은 매니저의 Predict·Judge 만 부른다). 상태가 없고 세계는 <see cref="IInterruptJudgeWorld"/> 로 읽는다.
/// <list type="bullet">
/// <item><b>Predict</b>(누르는 순간): 누른 시각의 시전자 표본 → 근접 = 그 자리 / 대쉬 = 실제 대시 끝(같은 충돌 검사), 판정 시각 = max(대시 끝, 누른 시각 + Hit 지연).
///       대상·간파 불가·CC 는 누른 시각 표본, 창은 판정 시각의 시간표(예정 닫힘·조기 닫힘)로 본다 — 보스 이동은 예측하지 않는다.</item>
/// <item><b>Judge</b>(Hit 타이밍): 판정 시각의 시전자·대상 표본과 창으로 본다.</item>
/// </list>
/// 검사 순서(대상마다): 상자 닿음(범위) → 대상 정면 허용각 → 시전자 생존 → 시전자 CC → 대상 간파 불가 → 창 있음 → 판정 시각에 열림 → 지목자.
/// 범위·정면각은 기존 판정과 같은 기준 — 간파 상자를 공격 방향으로 돌려 판정 위치에 놓은 형상(<see cref="HitboxAnchorShape"/>)·대상 정면각(<see cref="InterruptTargetGeometry.FacesAttacker"/>).
/// 허용 오차(<see cref="InterruptJudgeTolerance"/>)는 서버 검증(3단계)용 — 클라는 0.
/// </summary>
public static class InterruptJudgeCalculator
{
    public static InterruptJudgeResult Predict(IInterruptJudgeWorld world, in InterruptCastRequest cast, in InterruptJudgeTolerance tolerance)
    {
        double hitTime = cast.PressTime + Math.Max(0f, cast.HitDelay);
        var result = new InterruptJudgeResult
        {
            JudgeTime = hitTime,
            HitTime = hitTime,
            DashEndTime = cast.PressTime,
        };

        if (world == null || !world.TryGetActorAt(cast.ActorId, cast.PressTime, out InterruptActorSample actor))
            return result;

        Vector3 resolvePosition = actor.Position;
        Vector3 attackDirection = cast.AttackDirection;
        if (cast.ViaDash && actor.Dashing)
        {
            attackDirection = actor.DashDirection;
            PlayerDashPathResult path = world.SimulateDash(cast.ActorId, actor.Position, actor.DashDirection, actor.DashSpeed, actor.DashRemaining);
            resolvePosition = path.End;
            result.DashEndTime = cast.PressTime + Math.Max(0f, path.Duration);
            result.DashEndedByUnit = path.EndedByUnit;
            result.DashBlockedByWall = path.BlockedByWall;
        }

        result.JudgeTime = Math.Max(result.DashEndTime, hitTime);
        result.ResolvePosition = resolvePosition;
        result.AttackDirection = AttackDirectionOr(attackDirection, actor.Forward);
        Evaluate(world, actor, cast.PressTime, 0, tolerance, ref result);
        return result;
    }

    public static InterruptJudgeResult Judge(IInterruptJudgeWorld world, in InterruptJudgeAttempt attempt, in InterruptJudgeTolerance tolerance)
    {
        var result = new InterruptJudgeResult
        {
            JudgeTime = attempt.JudgeTime,
            HitTime = attempt.JudgeTime,
            DashEndTime = attempt.JudgeTime,
        };

        if (world == null || !world.TryGetActorAt(attempt.ActorId, attempt.JudgeTime, out InterruptActorSample actor))
            return result;

        result.ResolvePosition = actor.Position;
        result.AttackDirection = AttackDirectionOr(attempt.AttackDirection, actor.Forward);
        Evaluate(world, actor, attempt.JudgeTime, attempt.TargetId, tolerance, ref result);
        return result;
    }

    /// <summary>
    /// 판정 때 놓일 간파 상자 — 시전자 기준 형상을 공격 방향으로 돌려 판정 위치에 놓고 수평 여유만큼 키운다.
    /// 기존 <see cref="HitboxAnchorShape.PlacedAt"/> 와 같은 회전·이동(시전자 위치를 원점으로 옮겨 둔 형상이라 회전축 = 원점).
    /// </summary>
    public static HitboxAnchorShape PlaceHitbox(in InterruptActorSample actor, Vector3 attackDirection, Vector3 resolvePosition,
        float distanceSlack)
    {
        HitboxAnchorShape shape = actor.Hitbox.Aimed(Vector3.zero, actor.HitboxForward, attackDirection).Offset(resolvePosition);
        if (distanceSlack <= 0f)
            return shape;

        shape.Box.halfExtents.x += distanceSlack;
        shape.Box.halfExtents.z += distanceSlack;
        shape.Sphere.radius += distanceSlack;
        shape.Capsule.radius += distanceSlack;
        return shape;
    }

    static Vector3 AttackDirectionOr(Vector3 direction, Vector3 fallback)
    {
        Vector3 planar = InterruptSampleMath.Planar(direction);
        return planar != Vector3.zero ? planar : InterruptSampleMath.Planar(fallback);
    }

    static void Evaluate(IInterruptJudgeWorld world, in InterruptActorSample actor, double sampleTime, ulong focusTargetId,
        in InterruptJudgeTolerance tolerance, ref InterruptJudgeResult result)
    {
        result.Outcome = InterruptJudgeOutcome.NoTarget;
        result.FailReason = InterruptJudgeFailReason.None;
        result.TargetId = 0;
        result.WindowId = 0;
        if (!actor.HasHitbox)
            return;

        HitboxAnchorShape shape = PlaceHitbox(actor, result.AttackDirection, result.ResolvePosition, tolerance.DistanceSlack);

        bool hasSuccess = false;
        ulong successId = 0;
        uint successWindow = 0;
        bool focusFound = false;
        InterruptJudgeFailReason focusReason = InterruptJudgeFailReason.None;
        uint focusWindow = 0;
        InterruptJudgeFailReason bestReason = InterruptJudgeFailReason.None;
        ulong bestId = 0;
        uint bestWindow = 0;

        int count = world.TargetCount;
        for (int i = 0; i < count; i++)
        {
            ulong id = world.GetTargetId(i);
            if (!world.TryGetTargetAt(id, sampleTime, out InterruptTargetSample target))
                continue;

            InterruptJudgeFailReason reason = EvaluateTarget(world, actor, id, target, shape, result.ResolvePosition,
                result.JudgeTime, tolerance, out uint windowId);

            if (reason == InterruptJudgeFailReason.None)
            {
                if (!hasSuccess || id == focusTargetId)
                {
                    successId = id;
                    successWindow = windowId;
                }
                hasSuccess = true;
                continue;
            }

            if (focusTargetId != 0 && id == focusTargetId)
            {
                focusFound = true;
                focusReason = reason;
                focusWindow = windowId;
            }
            else if (reason > InterruptJudgeFailReason.Angle && reason > bestReason)
            {
                bestReason = reason;
                bestId = id;
                bestWindow = windowId;
            }
        }

        if (hasSuccess)
        {
            result.Outcome = InterruptJudgeOutcome.Success;
            result.TargetId = successId;
            result.WindowId = successWindow;
        }
        else if (focusFound)
        {
            result.Outcome = InterruptJudgeOutcome.Failure;
            result.FailReason = focusReason;
            result.TargetId = focusTargetId;
            result.WindowId = focusWindow;
        }
        else if (bestReason != InterruptJudgeFailReason.None)
        {
            result.Outcome = InterruptJudgeOutcome.Failure;
            result.FailReason = bestReason;
            result.TargetId = bestId;
            result.WindowId = bestWindow;
        }
    }

    static InterruptJudgeFailReason EvaluateTarget(IInterruptJudgeWorld world, in InterruptActorSample actor, ulong targetId,
        in InterruptTargetSample target, in HitboxAnchorShape shape, Vector3 resolvePosition, double judgeTime,
        in InterruptJudgeTolerance tolerance, out uint windowId)
    {
        windowId = 0;
        if (!world.ShapeHitsTarget(targetId, shape, target.Position, actor.HitLayers))
            return InterruptJudgeFailReason.OutOfRange;

        float halfAngle = target.FrontAngle >= InterruptTargetGeometry.NoFrontRule
            ? InterruptTargetGeometry.NoFrontRule
            : Mathf.Min(InterruptTargetGeometry.NoFrontRule, target.FrontAngle + Mathf.Max(0f, tolerance.AngleSlack));
        if (!InterruptTargetGeometry.FacesAttacker(resolvePosition, target.Position, target.Forward, halfAngle))
            return InterruptJudgeFailReason.Angle;

        if (!actor.Alive)
            return InterruptJudgeFailReason.CasterDown;
        if (actor.CrowdControlled)
            return InterruptJudgeFailReason.CrowdControlled;
        if (target.BlockedByState)
            return InterruptJudgeFailReason.TargetBlocked;

        double slack = Math.Max(0.0, tolerance.TimeSlack);
        if (!world.TryGetLatestWindowOpenedBy(targetId, judgeTime + slack, out InterruptWindowRecord window))
            return InterruptJudgeFailReason.NoWindow;

        windowId = window.WindowId;
        if (judgeTime >= window.EffectiveCloseTime + slack)
            return InterruptJudgeFailReason.WindowClosed;

        if (window.DesignatedClientId != InterruptWindowRecord.NoDesignee && window.DesignatedClientId != actor.ClientId)
            return InterruptJudgeFailReason.NotDesignated;

        return InterruptJudgeFailReason.None;
    }
}

/// <summary>
/// 누름 → 판정(Hit) 지연(할 일 191 §3.1 ② 근접) = 클립 Hit 이벤트 시각 ÷ 지금 애니 속도. 공격속도가 바뀐 직후에도 그 순간 속도로 계산한다.
/// 클립 시각은 상태 속도 1 기준 초(상태 Speed·Multiplier 를 나눈 값).
/// </summary>
public static class InterruptHitDelay
{
    /// <summary>클립 Hit 시각(상태 속도 1 기준 초) ÷ 재생 속도(<see cref="PlayerAttackSpeedRules.AnimatorSpeed"/> — 하한·비정상 값 규칙 <see cref="PlayerAttackSpeedRules.Rate"/>).</summary>
    public static float FromClip(float clipHitSeconds, float playbackSpeed) =>
        clipHitSeconds <= 0f ? 0f : clipHitSeconds / PlayerAttackSpeedRules.Rate(playbackSpeed);

    /// <summary>클립의 스킬 Hit 이벤트(<see cref="PlayerAnimationEventRelay.HandleSkillEvent"/>(Hit)) 중 가장 이른 시각(초). 없으면 false.</summary>
    public static bool TryFindClipHitTime(AnimationClip clip, out float seconds)
    {
        seconds = float.MaxValue;
        if (clip == null)
            return false;

        bool found = false;
        foreach (AnimationEvent e in clip.events)
        {
            if (e.functionName != nameof(PlayerAnimationEventRelay.HandleSkillEvent) || e.intParameter != (int)SkillAnimationEventType.Hit)
                continue;
            found = true;
            seconds = Mathf.Min(seconds, e.time);
        }

        if (!found)
            seconds = 0f;
        return found;
    }

    static readonly System.Collections.Generic.List<AnimatorClipInfo> ClipInfoBuffer = new System.Collections.Generic.List<AnimatorClipInfo>();

    /// <summary>
    /// [런타임] <paramref name="stateHash"/>(상태 짧은 이름 해시) 상태가 지금 재생 중인 레이어에서 클립 Hit 시각을 상태 속도 1 기준으로 읽는다(전이 중이면 다음 상태 먼저).
    /// 런타임 컨트롤러는 상태별 클립을 노출하지 않아 재생 중일 때만 읽힌다(<see cref="PlayerSkillSafetyNet.Probe"/> 와 같은 방식). 재생 중이 아니거나 Hit 이벤트가 없으면 false.
    /// </summary>
    public static bool TryReadPlayingStateHitTime(Animator animator, int stateHash, out float unitSpeedSeconds)
    {
        unitSpeedSeconds = 0f;
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
            return false;

        for (int layer = 0; layer < animator.layerCount; layer++)
        {
            bool isNext = false;
            AnimatorStateInfo state = default;
            if (animator.IsInTransition(layer))
            {
                state = animator.GetNextAnimatorStateInfo(layer);
                isNext = state.shortNameHash == stateHash;
            }

            if (!isNext)
            {
                state = animator.GetCurrentAnimatorStateInfo(layer);
                if (state.shortNameHash != stateHash)
                    continue;
            }

            ClipInfoBuffer.Clear();
            if (isNext)
                animator.GetNextAnimatorClipInfo(layer, ClipInfoBuffer);
            else
                animator.GetCurrentAnimatorClipInfo(layer, ClipInfoBuffer);

            AnimationClip clip = null;
            float bestWeight = -1f;
            foreach (AnimatorClipInfo info in ClipInfoBuffer)
            {
                if (info.clip == null || info.weight <= bestWeight)
                    continue;
                bestWeight = info.weight;
                clip = info.clip;
            }

            float stateSpeed = Mathf.Abs(state.speed * state.speedMultiplier);
            if (!TryFindClipHitTime(clip, out float clipSeconds) || stateSpeed <= 0.0001f)
                return false;

            unitSpeedSeconds = clipSeconds / stateSpeed;
            return true;
        }

        return false;
    }
}
