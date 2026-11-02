using UnityEngine;

/// <summary>이력 버퍼가 두 표본 사이를 보간하는 규칙(할 일 191). <paramref name="t"/> = 0(this) ~ 1(<paramref name="next"/>).</summary>
public interface IInterruptSampleLerp<T> where T : struct
{
    T LerpTo(in T next, float t);
}

/// <summary>
/// [모든 피어] 플레이어 하나의 간파 판정 데이터 한 프레임(할 일 191 §3.1 ①) — <see cref="InterruptManager"/> 가 매 프레임 샘플한다.
/// 클라는 보는 값, 서버는 실제 값이다. 위치·방향은 보간하고, 상태 칸은 앞 표본 값을 쓴다(상태는 사이 값이 없다).
/// </summary>
public struct InterruptActorSample : IInterruptSampleLerp<InterruptActorSample>
{
    public Vector3 Position;
    /// <summary>바라보는 방향(평면 정규화).</summary>
    public Vector3 Forward;
    /// <summary>생명주기 Alive.</summary>
    public bool Alive;
    /// <summary>상태 전환 CC 중(넉백·끌기·잡기 = <see cref="PlayerActionState.Knockback"/>·<see cref="PlayerActionState.Restrained"/>) — 간파 불가·연출 끊김(D5).</summary>
    public bool CrowdControlled;

    /// <summary>진행 중 대시가 있다. 아래 대시 칸은 이때만 뜻이 있다.</summary>
    public bool Dashing;
    /// <summary>대시 시작 시각(서버 시각).</summary>
    public double DashStartTime;
    /// <summary>대시 방향(평면 정규화).</summary>
    public Vector3 DashDirection;
    public float DashSpeed;
    /// <summary>이 표본 시각 기준 남은 대시 시간(초).</summary>
    public float DashRemaining;

    public InterruptActorSample LerpTo(in InterruptActorSample next, float t)
    {
        InterruptActorSample s = this;
        s.Position = Vector3.Lerp(Position, next.Position, t);
        s.Forward = InterruptSampleMath.LerpPlanarDirection(Forward, next.Forward, t);
        if (Dashing && next.Dashing)
            s.DashRemaining = Mathf.Lerp(DashRemaining, next.DashRemaining, t);
        return s;
    }
}

/// <summary>
/// [모든 피어] 간파 대상(23호·중간보스·허수아비) 하나의 판정 데이터 한 프레임(할 일 191 §3.1 ①).
/// 창 시간표는 여기 없다 — <see cref="InterruptWindowSchedule"/> 가 따로 든다.
/// </summary>
public struct InterruptTargetSample : IInterruptSampleLerp<InterruptTargetSample>
{
    public Vector3 Position;
    /// <summary>대상 정면(평면 정규화).</summary>
    public Vector3 Forward;
    /// <summary>간파 불가 상태(취약·제압) — <see cref="IInterruptJudgeTarget.InterruptBlockedByState"/>.</summary>
    public bool BlockedByState;
    /// <summary>레이드 기믹 지목자 clientId(지금 열린 창의 지목자). 없음 = <see cref="InterruptWindowRecord.NoDesignee"/>.</summary>
    public ulong DesignatedClientId;

    public InterruptTargetSample LerpTo(in InterruptTargetSample next, float t)
    {
        InterruptTargetSample s = this;
        s.Position = Vector3.Lerp(Position, next.Position, t);
        s.Forward = InterruptSampleMath.LerpPlanarDirection(Forward, next.Forward, t);
        return s;
    }
}

public static class InterruptSampleMath
{
    /// <summary>수평 성분만 남겨 정규화. 수평 성분이 없으면 0.</summary>
    public static Vector3 Planar(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.000001f ? v.normalized : Vector3.zero;
    }

    /// <summary>평면 방향 보간(구면) — 한쪽이 0 이면 다른 쪽, 둘 다 0 이면 0.</summary>
    public static Vector3 LerpPlanarDirection(Vector3 a, Vector3 b, float t)
    {
        a.y = 0f;
        b.y = 0f;
        bool hasA = a.sqrMagnitude > 0.000001f;
        bool hasB = b.sqrMagnitude > 0.000001f;
        if (!hasA) return hasB ? b.normalized : Vector3.zero;
        if (!hasB) return a.normalized;

        Vector3 r = Vector3.Slerp(a.normalized, b.normalized, Mathf.Clamp01(t));
        r.y = 0f;
        return r.sqrMagnitude > 0.000001f ? r.normalized : a.normalized;
    }
}
