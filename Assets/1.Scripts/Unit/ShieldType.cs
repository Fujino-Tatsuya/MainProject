using System;
using Unity.Netcode;

/// <summary>
/// 보호막 종류. 보호막은 종류·출처별 인스턴스로 쌓이고, 쌓임 규칙은 종류가 정한다(<see cref="ShieldTypePolicies"/>).
/// 종류는 추후 HUD 에서 색을 나누는 기준이기도 하다. 값은 네트워크 직렬화되므로 순서를 바꾸지 않는다.
/// </summary>
public enum ShieldType : byte
{
    None = 0,
    HolyShield = 1,     // 가붕이 E 수호자의 의지
    GunnerCharge = 2,   // 거너 Q 충전 레이저
}

/// <summary>같은 종류·같은 출처의 보호막을 다시 받을 때의 처리.</summary>
public enum ShieldStackPolicy
{
    Replace, // 같은 종류·출처의 기존 인스턴스를 새것으로 교체
    Stack,   // 기존 인스턴스를 두고 새 인스턴스를 추가(합산, 지속시간 독립)
}

/// <summary>보호막이 사라진 이유. 소비자(연출)는 "깨짐"과 "걷힘"을 구분한다.</summary>
public enum ShieldEndReason
{
    Depleted, // 피해로 소진
    Expired,  // 지속시간 만료
    Cleared,  // 사망·추락 등 강제 제거
}

public static class ShieldTypePolicies
{
    // 종류별 쌓임 규칙 표. 새 종류는 여기에 추가한다.
    public static ShieldStackPolicy Of(ShieldType type) => type switch
    {
        ShieldType.GunnerCharge => ShieldStackPolicy.Stack,
        _ => ShieldStackPolicy.Replace,
    };
}

/// <summary>
/// 보호막 인스턴스 1개. 서버가 보관하고 <c>Unit</c> 이 NetworkList 로 전 피어에 복제한다.
/// expireTime 은 NetworkClock.GameNow 도메인이며, 0 이하면 시간 만료 없음.
/// </summary>
public struct ShieldInstance : INetworkSerializable, IEquatable<ShieldInstance>
{
    public ShieldType type;
    public ulong sourceId;
    public int amount;        // 남은 양
    public int grantedAmount; // 부여 당시 양 — HUD 비율(남은 합 / 부여 합)의 분모
    public double expireTime;

    public bool HasExpiry => expireTime > 0.0;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref type);
        serializer.SerializeValue(ref sourceId);
        serializer.SerializeValue(ref amount);
        serializer.SerializeValue(ref grantedAmount);
        serializer.SerializeValue(ref expireTime);
    }

    public bool Equals(ShieldInstance other)
    {
        return type == other.type &&
            sourceId == other.sourceId &&
            amount == other.amount &&
            grantedAmount == other.grantedAmount &&
            expireTime == other.expireTime;
    }
}
