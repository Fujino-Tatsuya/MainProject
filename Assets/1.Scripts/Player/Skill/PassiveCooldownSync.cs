using System;
using Unity.Netcode;

/// <summary>
/// 패시브 내부 쿨 복제값(할 일 91 T4). 절대시각이 아니라 "남은 초 + 그 값을 적은 서버 시각" 이다 —
/// 쿨 시계는 피어마다 원점이 달라(<see cref="PlayerCooldownClock"/>) 서버의 끝 시각을 그대로 읽을 수 없다.
/// 늦게 들어온 피어도 적은 뒤 흐른 시간을 빼서 맞춘다.
/// </summary>
public struct PassiveCooldownStamp : INetworkSerializable, IEquatable<PassiveCooldownStamp>
{
    public float remaining;       // 적은 순간의 남은 쿨(쿨 시계 초)
    public double stampServerTime; // 적은 순간의 NetworkManager.ServerTime

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref remaining);
        serializer.SerializeValue(ref stampServerTime);
    }

    public bool Equals(PassiveCooldownStamp other) =>
        remaining == other.remaining && stampServerTime == other.stampServerTime;
}

/// <summary>패시브 내부 쿨 ↔ 쿨 시계 환산 — 순수 함수(EditMode 테스트 대상).</summary>
public static class PassiveCooldownSync
{
    /// <summary>[서버] 쿨 끝 시각(서버 쿨 시계)을 복제값으로 적는다.</summary>
    public static PassiveCooldownStamp Stamp(double readyAt, double clockNow, double serverTime) =>
        new PassiveCooldownStamp
        {
            remaining = (float)Math.Max(0.0, readyAt - clockNow),
            stampServerTime = serverTime,
        };

    /// <summary>
    /// [받는 피어] 복제값을 이 피어 쿨 시계의 끝 시각으로 바꾼다.
    /// 적은 뒤 흐른 실시간 × 지금 배율만큼 남은 초에서 뺀다(그 사이 배율이 바뀌었으면 그만큼 오차 — HUD 표시용 허용).
    /// </summary>
    public static double LocalReadyAt(in PassiveCooldownStamp stamp, double clockNow, double serverTimeNow, float rate)
    {
        double elapsed = Math.Max(0.0, serverTimeNow - stamp.stampServerTime);
        double scaledRate = rate > 0f && !float.IsInfinity(rate) ? rate : 0.0;
        return clockNow + Math.Max(0.0, stamp.remaining - elapsed * scaledRate);
    }

    /// <summary>남은 쿨(쿨 시계 초). 0 아래로 내려가지 않는다.</summary>
    public static float Remaining(double readyAt, double clockNow) => (float)Math.Max(0.0, readyAt - clockNow);

    /// <summary>[서버] 피격 1회 감소 — 끝 시각을 당기되 지금보다 앞으로는 안 간다.</summary>
    public static double Reduce(double readyAt, double clockNow, float seconds) =>
        readyAt > clockNow ? Math.Max(clockNow, readyAt - Math.Max(0f, seconds)) : readyAt;
}
