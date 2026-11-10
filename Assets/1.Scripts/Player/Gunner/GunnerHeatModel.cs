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

    /// <summary>
    /// 축열 포격(할 일 127) — now 시점에 남은 열을 모두 소모한다. 소모 비율(남은 열 ÷ 최대치, 0~1)을 내고 열 0·과열 해제 상태를 돌려준다.
    /// 충전 중 냉각·E 로 줄었으면 줄어든 값만 소모된다.
    /// </summary>
    public static GunnerHeatState SpendAll(in GunnerHeatState state, double now, GunnerHeatData data, out float spentRatio)
    {
        spentRatio = Mathf.Clamp01(HeatAt(state, now, data) / Mathf.Max(0.0001f, data.MaxHeat));
        return Reset(now);
    }

    /// <summary>E 냉각 — 과열도 0, 과열 해제, 대기시간 초기화(§5.5).</summary>
    public static GunnerHeatState Reset(double now) =>
        new GunnerHeatState { heat = 0f, anchorTime = now, overheated = false };
}

/// <summary>
/// [오너] 과열 1차 예측(할 일 256) — 복제된 기준점 + 서버에 아직 반영되지 않은 자기 발. 순수 클래스(EditMode 대상 GunnerHeatPredictionTests).
/// 발마다 오너 시계(GameNow 도메인)로 시각과 증가량을 기록한다. 서버는 그 발을 도착 시각(오너 시각 + 지연)에 반영해 기준점 시각이
/// 그보다 뒤가 되므로, 기준점 시각 이하의 발 = 반영됨으로 보고 버린다(E 냉각 초기화도 같은 비교로 앞선 발을 버린다).
/// 서버가 거부한 발(과열·간격)은 영영 반영되지 않으니 <see cref="PendingLifetime"/> 이 지나면 버린다.
/// 지연이 발사 간격보다 길면 반영 전 발을 반영됨으로 잘못 봐 한 발 덜 예측할 수 있다 — 서버가 원본이라 그 발은 레이저만 나가고 데미지 없음.
/// </summary>
public sealed class GunnerHeatPrediction
{
    /// <summary>미반영 발을 들고 있는 최대 시간(초) — 왕복 지연보다 넉넉히.</summary>
    public const double PendingLifetime = 0.5;

    private readonly System.Collections.Generic.List<double> times = new System.Collections.Generic.List<double>();
    private readonly System.Collections.Generic.List<float> amounts = new System.Collections.Generic.List<float>();

    public int PendingCount => times.Count;

    /// <summary>이 오너 시각에 한 발(증가량 <paramref name="amount"/>)을 쐈다.</summary>
    public void Record(double now, float amount)
    {
        times.Add(now);
        amounts.Add(Mathf.Max(0f, amount));
    }

    public void Clear()
    {
        times.Clear();
        amounts.Clear();
    }

    /// <summary>예측 기준점 = 복제 기준점에 미반영 발을 시각 순으로 더한 것. 반영된 발·오래된 발은 여기서 버린다.</summary>
    public GunnerHeatState Predict(in GunnerHeatState replicated, double now, GunnerHeatData data)
    {
        for (int i = times.Count - 1; i >= 0; i--)
        {
            if (times[i] <= replicated.anchorTime || now - times[i] > PendingLifetime)
            {
                times.RemoveAt(i);
                amounts.RemoveAt(i);
            }
        }

        GunnerHeatState state = replicated;
        for (int i = 0; i < times.Count; i++)
            state = GunnerHeatModel.AddShot(state, times[i], amounts[i], data);
        return state;
    }
}
