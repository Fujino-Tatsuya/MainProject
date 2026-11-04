using Unity.Netcode;
using UnityEngine;

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
    /// <summary>간파 시도 — 승인 시 성공 예측(강)으로 건 슬로우. Fail·Reset 도 이 값이다.</summary>
    Attempt = 0,
    /// <summary>간파 성공 — 판정에서 카운터 창을 소비했을 때 다시 건 슬로우.</summary>
    Success = 1,
    /// <summary>간파 시도 — 승인 시 실패 예측(약)으로 건 슬로우(할 일 132). 오너 줌 배율을 강과 가르려고 따로 둔다.</summary>
    AttemptWeak = 2,
}

/// <summary>
/// 서버 → 전원 슬로우 모션 이벤트 한 건(<see cref="GlobalTimeScale"/> 의 named message 본문).
///
/// 프로필 값을 그대로 싣는다 — 데이터 테이블 값이 피어마다 달라도(빌드·xlsx 불일치) 서버가 고른 모양으로 같게 재생한다.
/// Fail·Reset 도 같은 고정 길이로 쓴다(프로필 칸은 0) — 분기 없이 읽고 쓰기 위해서다.
/// </summary>
public struct SlowMotionEvent
{
    /// <summary>직렬화 크기(바이트). kind 1 + purpose 1 + id 4 + t 8 + clientId 8 + 프로필 7×4 + 대상 위치 3×4 + 간파 표식 1(비트 0 = 제압 간파 179 · 비트 1 = 대쉬 간파 192).</summary>
    public const int SerializedSize =
        sizeof(byte) + sizeof(byte) + sizeof(uint) + sizeof(double) + sizeof(ulong) + sizeof(float) * 7 + sizeof(float) * 3 + sizeof(byte);

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

    /// <summary>
    /// 연출 기준 월드 위치(할 일 87 C5) — 간파 성공이면 카운터 창을 소비한 첫 대상, 못 찾으면 시전자.
    /// Start 에서만 의미가 있고 Fail·Reset 은 0.
    /// </summary>
    public Vector3 TargetPosition;

    /// <summary>
    /// 제압 간파(할 일 179) — 간파 성공(<see cref="SlowMotionPurpose.Success"/>)이 제압 칸 마지막 1칸을 깎아 제압에 들어갔다.
    /// 연출 디렉터가 일반 성공 섬광 대신 제압 간파 섬광을 고른다. 서버만 정해 실어 보낸다(새 RPC 없음). Start 에서만 의미.
    /// </summary>
    public bool SuppressInterrupt;

    /// <summary>
    /// 대쉬 간파(할 일 192) — 이 간파 성공이 대시에 실린 간파였다. 연출 디렉터가 단계(일반/대쉬/제압)를 고른다.
    /// 제압 간파 칸과 같은 바이트의 비트 1 로 싣는다(직렬화 크기 그대로, 새 RPC 없음). Start 에서만 의미.
    /// </summary>
    public bool DashInterrupt;

    /// <summary>간파 표식 바이트 비트(할 일 179·192).</summary>
    public const byte SuppressInterruptBit = 1 << 0;
    public const byte DashInterruptBit = 1 << 1;

    /// <summary>간파 표식 두 칸 → 바이트 하나.</summary>
    public static byte PackInterruptFlags(bool suppressInterrupt, bool dashInterrupt) =>
        (byte)((suppressInterrupt ? SuppressInterruptBit : 0) | (dashInterrupt ? DashInterruptBit : 0));

    public static SlowMotionEvent CreateStart(uint triggerId, double time, ulong triggerClientId, SlowMotionProfile profile,
                                              SlowMotionPurpose purpose = SlowMotionPurpose.Attempt,
                                              Vector3 targetPosition = default, bool suppressInterrupt = false,
                                              bool dashInterrupt = false) =>
        new SlowMotionEvent
        {
            Kind = SlowMotionEventKind.Start,
            Purpose = purpose,
            TriggerId = triggerId,
            Time = time,
            TriggerClientId = triggerClientId,
            TargetPosition = targetPosition,
            SuppressInterrupt = suppressInterrupt,
            DashInterrupt = dashInterrupt,
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
        writer.WriteValueSafe(TargetPosition.x);
        writer.WriteValueSafe(TargetPosition.y);
        writer.WriteValueSafe(TargetPosition.z);
        writer.WriteValueSafe(PackInterruptFlags(SuppressInterrupt, DashInterrupt));
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
        reader.ReadValueSafe(out float targetX);
        reader.ReadValueSafe(out float targetY);
        reader.ReadValueSafe(out float targetZ);
        reader.ReadValueSafe(out byte interruptFlags);

        return new SlowMotionEvent
        {
            Kind = (SlowMotionEventKind)kind,
            Purpose = (SlowMotionPurpose)purpose,
            TriggerId = triggerId,
            Time = time,
            TriggerClientId = triggerClientId,
            TargetPosition = new Vector3(targetX, targetY, targetZ),
            SuppressInterrupt = (interruptFlags & SuppressInterruptBit) != 0,
            DashInterrupt = (interruptFlags & DashInterruptBit) != 0,
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
