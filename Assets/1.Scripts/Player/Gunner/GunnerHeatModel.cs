using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 과열 상태의 기준점. 서버가 발사·E 사용 때만 갱신하고, 모든 피어는 이 기준점에서 같은 함수로 현재 과열도를 계산한다
/// (매 프레임 값을 복제하지 않는다). 시간은 NetworkClock.GameNow 도메인. (character_gunner.md §5 · D7)
/// </summary>
public struct GunnerHeatState : INetworkSerializable, IEquatable<GunnerHeatState>
{
    public float heat;       // anchorTime 시점의 과열도
    public double anchorTime; // 마지막 기본 공격 발사(또는 E 초기화) 시각 — 냉각 대기시간의 기준
    public bool overheated;  // anchorTime 시점에 과열 상태였는가

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref heat);
        serializer.SerializeValue(ref anchorTime);
        serializer.SerializeValue(ref overheated);
    }

    public bool Equals(GunnerHeatState other) =>
        heat == other.heat && anchorTime == other.anchorTime && overheated == other.overheated;
}

/// <summary>과열 계산 — 순수 함수(EditMode 테스트 대상).</summary>
public static class GunnerHeatModel
{
    /// <summary>now 시점의 과열도. 대기시간 전에는 그대로, 이후에는 (과열이면 빠른) 속도로 0 까지 감소.</summary>
    public static float HeatAt(in GunnerHeatState state, double now, GunnerHeatData data)
    {
        float elapsed = (float)(now - state.anchorTime) - data.CoolDelay;
        if (elapsed <= 0f)
            return state.heat;

        float rate = state.overheated ? data.OverheatCoolRate : data.CoolRate;
        return Mathf.Max(0f, state.heat - rate * elapsed);
    }

    /// <summary>과열 상태는 과열도가 0 이 될 때까지 유지된다(최대치 아래로 내려가도 해제되지 않음 — §5.4).</summary>
    public static bool OverheatedAt(in GunnerHeatState state, double now, GunnerHeatData data) =>
        state.overheated && HeatAt(state, now, data) > 0f;

    /// <summary>0 = 기본 단계, 1~3 = 강화 단계. 과열 상태는 3단계로 본다(§5.1).</summary>
    public static int StageAt(in GunnerHeatState state, double now, GunnerHeatData data)
    {
        if (OverheatedAt(state, now, data))
            return 3;
        return StageOf(HeatAt(state, now, data), data);
    }

    public static int StageOf(float heat, GunnerHeatData data)
    {
        int stage = 0;
        for (int i = 0; i < 3; i++)
        {
            if (heat >= data.StageThreshold(i + 1))
                stage = i + 1;
        }
        return stage;
    }

    /// <summary>기본 공격 1회 발사 — now 시점 값에 증가량을 더하고 최대치에서 자른다. 최대치면 과열 진입.</summary>
    public static GunnerHeatState AddShot(in GunnerHeatState state, double now, float amount, GunnerHeatData data)
    {
        float heat = Mathf.Min(data.MaxHeat, HeatAt(state, now, data) + Mathf.Max(0f, amount));
        bool overheated = OverheatedAt(state, now, data) || heat >= data.MaxHeat;
        return new GunnerHeatState { heat = heat, anchorTime = now, overheated = overheated };
    }

    /// <summary>E 냉각 — 과열도 0, 과열 해제, 대기시간 초기화(§5.5).</summary>
    public static GunnerHeatState Reset(double now) =>
        new GunnerHeatState { heat = 0f, anchorTime = now, overheated = false };
}
