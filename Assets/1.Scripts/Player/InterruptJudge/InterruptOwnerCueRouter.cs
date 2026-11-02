using System.Collections.Generic;
using BeaverLobby.Player.Dash;
using UnityEngine;

/// <summary>오너 간파 연출을 즉시 끝내는 이유(할 일 191 4단계 — PLAN D4·D5, §3.3).</summary>
public enum InterruptOwnerEndReason : byte
{
    /// <summary>Hit 타이밍 판정 실패 — 강 예측이었으면 그 순간 걷는다(D4).</summary>
    JudgeFailed,
    /// <summary>서버가 내 신고를 거부했다 — 이미 튼 성공 섬광도 즉시 끈다.</summary>
    ServerRejected,
    /// <summary>판정 전에 넉백·끌기·잡기(상태 전환 CC)·사망·대시로 스킬이 끊겼다(D5).</summary>
    CrowdControl,
    /// <summary>판정 시각 표본을 끝내 못 얻어 판정을 버렸다(이력 1초 밖).</summary>
    JudgeDropped,
}

/// <summary>오너 연출 cue 한 건 — 매니저가 이력·등록 정보로 채운다.</summary>
public struct InterruptOwnerCue
{
    /// <summary>시전자 플레이어 NetworkObjectId.</summary>
    public ulong ActorId;
    /// <summary>예측(시도 cue) 또는 판정(결과 cue) 결과.</summary>
    public InterruptJudgeResult Result;
    /// <summary>암전에 남길 오너 캐릭터 루트.</summary>
    public GameObject Owner;
    /// <summary>결과의 대상(대상 없음이면 null) — 암전에 남긴다.</summary>
    public GameObject Target;
    /// <summary>시전자의 우클릭 간파 스킬(빛 모임 소켓 찾기용).</summary>
    public PlayerInterruptSkillBase Skill;
    /// <summary>공격 방향(근접 = 조준, 대쉬 = 대시 방향) — 섬광 ④ 스미어 방향.</summary>
    public Vector3 AttackDirection;
    /// <summary>섬광 기준 위치(대상 위치, 없으면 시전자 위치).</summary>
    public Vector3 FlashPosition;

    public InterruptAttemptForecast Forecast => Result.Forecast;
}

/// <summary>
/// 오너 연출 cue 를 받는 쪽(할 일 191 4단계) = 간파 연출 디렉터. 매니저(<see cref="InterruptOwnerCueRouter"/>)만 부른다.
/// 연출은 이 cue 만 보고 정한다 — 예측·판정 계산은 매니저 소관.
/// </summary>
public interface IInterruptOwnerCueSink
{
    /// <summary>[오너] 누른 순간 예측(대상 없음/약/강) — 시도 연출. 새 시도로 받아들였으면 true.</summary>
    bool OwnerAttempt(in InterruptOwnerCue cue);

    /// <summary>[오너] Hit 타이밍 판정 성공 — 서버 결과를 기다리지 않고 성공 섬광을 튼다(약 예측이어도 그대로, D4).</summary>
    void OwnerJudgeSucceeded(in InterruptOwnerCue cue);

    /// <summary>[오너] 즉시 종료 — 판정 실패(D4)·서버 거부·CC 끊김(D5)·판정 버림.</summary>
    void OwnerEnded(InterruptOwnerEndReason reason);

    /// <summary>
    /// [오너] 내 신고가 승인됐다 — <paramref name="approvedActorIds"/> = 같은 창 동시 성공자 전체(나 포함, 첫 승인자가 맨 앞).
    /// 디렉터가 D10 스위치로 함께 성공한 캐릭터를 암전에서 뺄지 정한다.
    /// </summary>
    void OwnerApproved(IReadOnlyList<ulong> approvedActorIds, ulong localActorId);
}

/// <summary>
/// 오너 연출 cue 판단(할 일 191 4단계, PLAN §3.3) — 순수 로직. <see cref="InterruptManager"/> 가 들고 예측·판정·서버 결과·스킬 끊김을 넘긴다.
/// <list type="bullet">
/// <item>예측 → 시도 cue. 같은 시전자의 앞 시전 상태는 버린다.</item>
/// <item>판정 성공 → 성공 cue(서버를 기다리지 않는다). 판정 실패 → 즉시 종료 cue(D4 — 약이면 걷을 암전이 없을 뿐 같은 cue).</item>
/// <item>서버 거부 → 그 시전(시전자·창 id)이 판정 성공 상태일 때만 즉시 종료 cue. 승인 → 내가 목록에 있으면 목록 cue.</item>
/// <item>스킬 끊김: 판정 전 + 정상 완료가 아님(넉백·잡기·사망·대시 = 상태 전환) → 즉시 종료 cue(D5). 일반 피해는 스킬을 끊지 않아 여기 오지 않는다.</item>
/// </list>
/// 오너 클라에는 내 플레이어 하나뿐이라 시전 상태는 하나만 든다.
/// </summary>
public sealed class InterruptOwnerCueRouter
{
    enum Phase : byte { None, Predicted, JudgedSuccess, Concluded }

    readonly System.Func<IInterruptOwnerCueSink> _sink;
    Phase _phase;
    ulong _actorId;
    ulong _targetId;
    uint _windowId;

    public InterruptOwnerCueRouter(System.Func<IInterruptOwnerCueSink> sink)
    {
        _sink = sink;
    }

    /// <summary>이 시전자의 판정이 아직 안 났다(예측만 있음).</summary>
    public bool AwaitsJudge(ulong actorId) => _phase == Phase.Predicted && _actorId == actorId;

    /// <summary>이 시전자의 시전을 cue 경로가 맡고 있다(예측 뒤 ~ 결론 cue 전후). 스킬 끝 처리에서 기존 걷기를 건너뛸지 묻는다.</summary>
    public bool Tracks(ulong actorId) => _phase != Phase.None && _actorId == actorId;

    /// <summary>판정 끊김(D5) — 정상 완료(End)가 아니면 상태 전환으로 끊긴 것.</summary>
    public static bool EndsBeforeJudge(SkillEndReason reason) => reason != SkillEndReason.Completed;

    public void Predicted(in InterruptOwnerCue cue)
    {
        _phase = Phase.Predicted;
        _actorId = cue.ActorId;
        _targetId = 0;
        _windowId = 0;
        _sink()?.OwnerAttempt(cue);
    }

    public void Judged(in InterruptOwnerCue cue)
    {
        if (_phase != Phase.Predicted || _actorId != cue.ActorId)
            return;

        if (cue.Result.IsSuccess)
        {
            _phase = Phase.JudgedSuccess;
            _targetId = cue.Result.TargetId;
            _windowId = cue.Result.WindowId;
            _sink()?.OwnerJudgeSucceeded(cue);
            return;
        }

        _phase = Phase.Concluded;
        _sink()?.OwnerEnded(InterruptOwnerEndReason.JudgeFailed);
    }

    /// <summary>[오너] 판정 시각 표본을 못 얻어 판정을 버렸다.</summary>
    public void Dropped(ulong actorId)
    {
        if (!AwaitsJudge(actorId))
            return;

        _phase = Phase.Concluded;
        _sink()?.OwnerEnded(InterruptOwnerEndReason.JudgeDropped);
    }

    public void Rejected(in InterruptClaimRejection rejection)
    {
        if (_phase != Phase.JudgedSuccess || rejection.ActorId != _actorId ||
            rejection.TargetId != _targetId || rejection.WindowId != _windowId)
            return;

        _phase = Phase.Concluded;
        _sink()?.OwnerEnded(InterruptOwnerEndReason.ServerRejected);
    }

    public void Approved(in InterruptClaimApproval approval)
    {
        if (_phase != Phase.JudgedSuccess || approval.TargetId != _targetId || approval.WindowId != _windowId ||
            approval.ApprovedActorIds == null || !Contains(approval.ApprovedActorIds, _actorId))
            return;

        _sink()?.OwnerApproved(approval.ApprovedActorIds, _actorId);
    }

    /// <summary>
    /// [오너] 간파 스킬이 끝났다. 판정 전 + 상태 전환 끊김이면 즉시 종료 cue(D5). 판정 전 정상 완료는 판정을 기다린다(판정 cue 가 결론).
    /// cue 경로가 이 시전을 맡고 있으면 true(호출측은 기존 걷기를 건너뛴다).
    /// </summary>
    public bool SkillEnded(ulong actorId, SkillEndReason reason)
    {
        if (!Tracks(actorId))
            return false;

        if (_phase == Phase.Predicted && EndsBeforeJudge(reason))
        {
            _phase = Phase.Concluded;
            _sink()?.OwnerEnded(InterruptOwnerEndReason.CrowdControl);
        }

        return true;
    }

    /// <summary>[오너] 시전 자체가 없어졌다(서버 시전 거부) — cue 없이 잊는다(호출측이 기존 걷기를 부른다).</summary>
    public void Forget(ulong actorId)
    {
        if (_actorId == actorId)
            _phase = Phase.None;
    }

    public void Clear() => _phase = Phase.None;

    static bool Contains(IReadOnlyList<ulong> list, ulong id)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == id)
                return true;
        return false;
    }
}
