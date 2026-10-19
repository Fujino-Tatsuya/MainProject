using UnityEngine;

/// <summary>
/// 캐스트/정신집중형 스킬 타입 (R 최후의 심판).
/// 기본: Data.MaxActiveDuration을 채널 시간으로 사용해 완주 시 Completed로 정상 종료한다
/// (컨트롤러 안전망은 padding 이후에만 발동하므로 정상 경로가 항상 먼저다).
///
/// 이벤트 방식(<see cref="PlayerSkillBase.EndsOnClipEvent"/> — RequiredAnimationEvents 에 End 를 선언)인 스킬은 완주를
/// <b>클립 이벤트</b>로 정한다 — Hit = 완주 판정(<see cref="OnChannelCompleted"/>, 시전당 1회), End = Completed 종료.
/// 그때 타이머 완주는 타지 않고 MaxActiveDuration 도 쓰지 않는다. 안전망 = 상태 클립 실제 재생 시간 + padding
/// (컨트롤러, <see cref="PlayerSkillSafetyNet"/>)이라 클립 안 어디로 이벤트를 옮겨도 안전망이 먼저 끊지 않는다.
/// 경직 면역 등 보호 효과는 스킬이 StatusEffectController로 처리한다.
/// </summary>
public abstract class PlayerChannelingSkill : PlayerSkillBase
{
    private float channelStartTime;
    private float lastTickTime;
    private bool channelCompleted;

    protected float ChannelElapsed => Time.time - channelStartTime;

    // [서버] 이번 시전에 완주 판정(OnChannelCompleted)이 이미 났는지.
    protected bool ChannelCompleted => channelCompleted;

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        State = SkillState.Channeling;
        channelStartTime = Time.time;
        lastTickTime = Time.time;
        channelCompleted = false;
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

        if (!EndsOnClipEvent && Data != null && ChannelElapsed >= Data.MaxActiveDuration)
        {
            CompleteChannel();
            EndSelf(SkillEndReason.Completed);
        }
    }

    public override void OnAnimationEvent(SkillAnimationEventType eventType)
    {
        if (!EndsOnClipEvent || State != SkillState.Channeling)
            return;

        switch (eventType)
        {
            case SkillAnimationEventType.Hit:
                if (!channelCompleted)
                    CompleteChannel();
                break;

            case SkillAnimationEventType.End:
                // 🔴 Hit 없이 온 End 는 무시한다 — 피해 없이 Completed 로 끝내면 낙뢰 등 '완주' 연출이 남는다.
                //    그대로 두면 안전망(MaxDurationReached, 클립 길이 + padding)이 끊는다. 클립에서 End 가 Hit 앞에 있다는 저작 실수다.
                if (!channelCompleted)
                {
                    Edit.LogWarning($"[Skill] {GetType().Name} — Hit 이벤트 전에 End 가 왔습니다. 무시하고 안전망 종료를 기다립니다(피해 없음).", this);
                    break;
                }

                EndSelf(SkillEndReason.Completed);
                break;
        }
    }

    private void CompleteChannel()
    {
        channelCompleted = true;
        OnChannelCompleted();
    }

    // 서버 틱 훅 (채널 중 주기 효과). Data.TickInterval > 0일 때만 호출된다.
    protected virtual void OnChannelTick() { }

    // 채널 완주 판정 (마무리 판정 등). 타이머 완주면 종료 직전, 이벤트 완주면 Hit 이벤트 시점에 1회.
    protected virtual void OnChannelCompleted() { }
}
