using UnityEngine;

/// <summary>
/// 홀드형 스킬 타입 (Q 진격의 방패).
/// 누르는 동안 Charging 유지, 릴리즈(Released) 또는 최대 지속시간(컨트롤러 안전망)에 종료.
/// 조향은 OnAimUpdated로 들어온다 (오너가 주기 전송, 서버는 최신값만 사용).
/// </summary>
public abstract class PlayerHoldSkill : PlayerSkillBase
{
    private float holdStartTime;
    private float lastTickTime;

    protected float HoldElapsed => Time.time - holdStartTime;

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        State = SkillState.Charging;
        holdStartTime = Time.time;
        lastTickTime = Time.time;
    }

    public override void OnTick()
    {
        if (State != SkillState.Charging)
            return;

        float interval = Data != null ? Data.TickInterval : 0f;
        if (interval <= 0f)
            return;

        while (Time.time - lastTickTime >= interval)
        {
            lastTickTime += interval;
            OnHoldTick();

            // 틱 처리 중 스킬이 종료됐으면 중단
            if (State != SkillState.Charging)
                return;
        }
    }

    public override void OnReleased()
    {
        if (State == SkillState.Charging)
            EndSelf(SkillEndReason.Released);
    }

    // 서버 틱 훅 (Q의 정예/보스 반복 피해 등). Data.TickInterval > 0일 때만 호출된다.
    protected virtual void OnHoldTick() { }
}
