using UnityEngine;

/// <summary>
/// 캐스트/정신집중형 스킬 타입 (R 최후의 심판).
/// Data.MaxActiveDuration을 채널 시간으로 사용해 완주 시 Completed로 정상 종료한다
/// (컨트롤러 안전망은 padding 이후에만 발동하므로 정상 경로가 항상 먼저다).
/// 경직 면역 등 보호 효과는 스킬이 Custom 애니 이벤트 구간에서 StatusEffectController로 처리한다.
/// </summary>
public abstract class PlayerChannelingSkill : PlayerSkillBase
{
    private float channelStartTime;
    private float lastTickTime;

    protected float ChannelElapsed => Time.time - channelStartTime;

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        State = SkillState.Channeling;
        channelStartTime = Time.time;
        lastTickTime = Time.time;
    }

    public override void OnTick()
    {
        if (State != SkillState.Channeling)
            return;

        float interval = Data != null ? Data.TickInterval : 0f;
        if (interval > 0f)
        {
            while (Time.time - lastTickTime >= interval)
            {
                lastTickTime += interval;
                OnChannelTick();

                if (State != SkillState.Channeling)
                    return;
            }
        }

        if (Data != null && ChannelElapsed >= Data.MaxActiveDuration)
        {
            OnChannelCompleted();
            EndSelf(SkillEndReason.Completed);
        }
    }

    // 서버 틱 훅 (채널 중 주기 효과). Data.TickInterval > 0일 때만 호출된다.
    protected virtual void OnChannelTick() { }

    // 채널 완주 직후, 종료 직전에 호출 (마무리 판정 등)
    protected virtual void OnChannelCompleted() { }
}
