using Unity.Netcode;

/// <summary>서버가 전 피어에 알리는 슬로우 모션 이벤트 종류.</summary>
public enum SlowMotionEventKind : byte
{
    Start = 1,
    Fail = 2,
    Reset = 3,
}

/// <summary>슬로우를 왜 걸었는가(할 일 87). 연출 쪽이 시도·성공을 가른다.</summary>
public enum SlowMotionPurpose : byte
{
    /// <summary>간파 시도 — 승인 시 예측으로 건 슬로우. Fail·Reset 도 이 값이다.</summary>
    Attempt = 0,
    /// <summary>간파 성공 — 판정에서 카운터 창을 소비했을 때 다시 건 슬로우.</summary>
    Success = 1,
}

/// <summary>
/// 서버 → 전원 슬로우 모션 이벤트 한 건(<see cref="GlobalTimeScale"/> 의 named message 본문).
///
/// 프로필 값을 그대로 싣는다 — 데이터 테이블 값이 피어마다 달라도(빌드·xlsx 불일치) 서버가 고른 모양으로 같게 재생한다.
/// Fail·Reset 도 같은 고정 길이로 쓴다(프로필 칸은 0) — 분기 없이 읽고 쓰기 위해서다.
/// </summary>
public struct SlowMotionEvent
{
    /// <summary>직렬화 크기(바이트). kind 1 + purpose 1 + id 4 + t 8 + clientId 8 + 프로필 7×4.</summary>
    public const int SerializedSize =
        sizeof(byte) + sizeof(byte) + sizeof(uint) + sizeof(double) + sizeof(ulong) + sizeof(float) * 7;

    public SlowMotionEventKind Kind;
    /// <summary>슬로우 목적. Start 에서만 의미가 있고 Fail·Reset 은 <see cref="SlowMotionPurpose.Attempt"/> 고정.</summary>
    public SlowMotionPurpose Purpose;
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
    public float HitStopDuration;
    public float HitStopScale;

    public static SlowMotionEvent CreateStart(uint triggerId, double time, ulong triggerClientId, SlowMotionProfile profile,
                                              SlowMotionPurpose purpose = SlowMotionPurpose.Attempt) =>
        new SlowMotionEvent
        {
            Kind = SlowMotionEventKind.Start,
            Purpose = purpose,
            TriggerId = triggerId,
            Time = time,
            TriggerClientId = triggerClientId,
            Scale = profile.scale,
            EnterDuration = profile.enterDuration,
            HoldDuration = profile.holdDuration,
            ExitDuration = profile.exitDuration,
            FailExitDuration = profile.failExitDuration,
            HitStopDuration = profile.hitStopDuration,
            HitStopScale = profile.hitStopScale,
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
            hitStopDuration = HitStopDuration,
            hitStopScale = HitStopScale,
        };

    public void Write(FastBufferWriter writer)
    {
        writer.WriteValueSafe((byte)Kind);
        writer.WriteValueSafe((byte)Purpose);
        writer.WriteValueSafe(TriggerId);
        writer.WriteValueSafe(Time);
        writer.WriteValueSafe(TriggerClientId);
        writer.WriteValueSafe(Scale);
        writer.WriteValueSafe(EnterDuration);
        writer.WriteValueSafe(HoldDuration);
        writer.WriteValueSafe(ExitDuration);
        writer.WriteValueSafe(FailExitDuration);
        writer.WriteValueSafe(HitStopDuration);
        writer.WriteValueSafe(HitStopScale);
    }

    public static SlowMotionEvent Read(FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte kind);
        reader.ReadValueSafe(out byte purpose);
        reader.ReadValueSafe(out uint triggerId);
        reader.ReadValueSafe(out double time);
        reader.ReadValueSafe(out ulong triggerClientId);
        reader.ReadValueSafe(out float scale);
        reader.ReadValueSafe(out float enter);
        reader.ReadValueSafe(out float hold);
        reader.ReadValueSafe(out float exit);
        reader.ReadValueSafe(out float failExit);
        reader.ReadValueSafe(out float hitStop);
        reader.ReadValueSafe(out float hitStopScale);

        return new SlowMotionEvent
        {
            Kind = (SlowMotionEventKind)kind,
            Purpose = (SlowMotionPurpose)purpose,
            TriggerId = triggerId,
            Time = time,
            TriggerClientId = triggerClientId,
            Scale = scale,
            EnterDuration = enter,
            HoldDuration = hold,
            ExitDuration = exit,
            FailExitDuration = failExit,
            HitStopDuration = hitStop,
            HitStopScale = hitStopScale,
        };
    }
}
