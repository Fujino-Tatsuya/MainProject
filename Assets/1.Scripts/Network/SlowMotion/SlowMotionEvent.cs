using Unity.Netcode;

/// <summary>서버가 전 피어에 알리는 슬로우 모션 이벤트 종류.</summary>
public enum SlowMotionEventKind : byte
{
    Start = 1,
    Fail = 2,
    Reset = 3,
}

/// <summary>
/// 서버 → 전원 슬로우 모션 이벤트 한 건(<see cref="GlobalTimeScale"/> 의 named message 본문).
///
/// 프로필 값을 그대로 싣는다 — 데이터 테이블 값이 피어마다 달라도(빌드·xlsx 불일치) 서버가 고른 모양으로 같게 재생한다.
/// Fail·Reset 도 같은 고정 길이로 쓴다(프로필 칸은 0) — 분기 없이 읽고 쓰기 위해서다.
/// </summary>
public struct SlowMotionEvent
{
    /// <summary>직렬화 크기(바이트). kind 1 + id 4 + t 8 + clientId 8 + 프로필 5×4.</summary>
    public const int SerializedSize = sizeof(byte) + sizeof(uint) + sizeof(double) + sizeof(ulong) + sizeof(float) * 5;

    public SlowMotionEventKind Kind;
    /// <summary>서버 발동 번호(1 부터 증가). 0 = 없음.</summary>
    public uint TriggerId;
    /// <summary>이벤트 시각 — 슬로우 보정 전 게임시간(<see cref="NetworkClock.UnslowedGameNow"/> 도메인).</summary>
    public double Time;
    /// <summary>슬로우를 일으킨 플레이어의 clientId(오너 카메라 줌 판정용).</summary>
    public ulong TriggerClientId;

    public float Scale;
    public float EnterDuration;
    public float HoldDuration;
    public float ExitDuration;
    public float FailExitDuration;

    public static SlowMotionEvent CreateStart(uint triggerId, double time, ulong triggerClientId, SlowMotionProfile profile) =>
        new SlowMotionEvent
        {
            Kind = SlowMotionEventKind.Start,
            TriggerId = triggerId,
            Time = time,
            TriggerClientId = triggerClientId,
            Scale = profile.scale,
            EnterDuration = profile.enterDuration,
            HoldDuration = profile.holdDuration,
            ExitDuration = profile.exitDuration,
            FailExitDuration = profile.failExitDuration,
        };

    public static SlowMotionEvent CreateFail(uint triggerId, double time) =>
        new SlowMotionEvent { Kind = SlowMotionEventKind.Fail, TriggerId = triggerId, Time = time };

    public static SlowMotionEvent CreateReset(uint triggerId, double time) =>
        new SlowMotionEvent { Kind = SlowMotionEventKind.Reset, TriggerId = triggerId, Time = time };

    /// <summary>실린 값으로 프로필을 다시 만든다(Start 에서만 의미가 있다).</summary>
    public SlowMotionProfile ToProfile() =>
        new SlowMotionProfile
        {
            scale = Scale,
            enterDuration = EnterDuration,
            holdDuration = HoldDuration,
            exitDuration = ExitDuration,
            failExitDuration = FailExitDuration,
        };

    public void Write(FastBufferWriter writer)
    {
        writer.WriteValueSafe((byte)Kind);
        writer.WriteValueSafe(TriggerId);
        writer.WriteValueSafe(Time);
        writer.WriteValueSafe(TriggerClientId);
        writer.WriteValueSafe(Scale);
        writer.WriteValueSafe(EnterDuration);
        writer.WriteValueSafe(HoldDuration);
        writer.WriteValueSafe(ExitDuration);
        writer.WriteValueSafe(FailExitDuration);
    }

    public static SlowMotionEvent Read(FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte kind);
        reader.ReadValueSafe(out uint triggerId);
        reader.ReadValueSafe(out double time);
        reader.ReadValueSafe(out ulong triggerClientId);
        reader.ReadValueSafe(out float scale);
        reader.ReadValueSafe(out float enter);
        reader.ReadValueSafe(out float hold);
        reader.ReadValueSafe(out float exit);
        reader.ReadValueSafe(out float failExit);

        return new SlowMotionEvent
        {
            Kind = (SlowMotionEventKind)kind,
            TriggerId = triggerId,
            Time = time,
            TriggerClientId = triggerClientId,
            Scale = scale,
            EnterDuration = enter,
            HoldDuration = hold,
            ExitDuration = exit,
            FailExitDuration = failExit,
        };
    }
}
