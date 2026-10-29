using UnityEngine;

/// <summary>
/// 간파 연출 단계가 시간 레이어를 부르는 이음새(할 일 118) — 단계를 EditMode 에서 가짜로 검증하려고 둔다.
/// 실제 구현은 <see cref="GlobalTimeScalePort"/>. 서버 전용 호출만 있다.
/// </summary>
public interface ITimeScalePort
{
    /// <summary>
    /// <see cref="GlobalTimeScale.ServerStart"/> — 발동 번호, 거부 시 0.
    /// <paramref name="targetObjectId"/> = 간파 대상 NetworkObjectId(0 = 없음, 로컬 선착순 장부용 — 할 일 143-2).
    /// </summary>
    uint ServerStart(SlowMotionProfile profile, ulong triggerClientId, SlowMotionPurpose purpose, Vector3 targetPosition, bool hitStopOn,
        ulong targetObjectId);

    /// <summary><see cref="GlobalTimeScale.ServerFail"/> — 현재 발동 번호일 때만 실패 복귀.</summary>
    void ServerFail(uint triggerId);
}

/// <summary>
/// <see cref="GlobalTimeScale.Instance"/> 로 넘기는 실제 포트. 부를 때마다 인스턴스를 찾는다(없으면 0·무시 —
/// 시간 레이어가 없는 씬·오프라인).
/// </summary>
public sealed class GlobalTimeScalePort : ITimeScalePort
{
    public uint ServerStart(SlowMotionProfile profile, ulong triggerClientId, SlowMotionPurpose purpose, Vector3 targetPosition,
        bool hitStopOn, ulong targetObjectId)
    {
        GlobalTimeScale timeScale = GlobalTimeScale.Instance;
        return timeScale != null
            ? timeScale.ServerStart(profile, triggerClientId, purpose, targetPosition, hitStopOn, targetObjectId)
            : 0;
    }

    public void ServerFail(uint triggerId)
    {
        if (triggerId == 0)
            return;

        GlobalTimeScale timeScale = GlobalTimeScale.Instance;
        if (timeScale != null)
            timeScale.ServerFail(triggerId);
    }
}
