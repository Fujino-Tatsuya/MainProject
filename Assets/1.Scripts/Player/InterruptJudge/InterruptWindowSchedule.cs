using System.Collections.Generic;
using Unity.Netcode;

/// <summary>창이 어떻게 닫혔나(할 일 191 §3.2). 열린 동안은 <see cref="Open"/>.</summary>
public enum InterruptWindowCloseKind : byte
{
    Open = 0,
    /// <summary>닫힘 예정 시각에 닫혔다(허용 오차 안).</summary>
    OnTime = 1,
    /// <summary>예정보다 일찍 닫혔다 — 간파 성공·공격 취소·사망 등. 즉시 배포한다.</summary>
    Early = 2,
    /// <summary>예정보다 늦게 닫혔다 — 23호 게이트(타이머 끝 + 애니 Hit 도착)에서 애니가 늦은 경우. 실제 닫힘 때 갱신 배포한다.</summary>
    Extended = 3,
    /// <summary>예정 시각 없이 열린 창(잡기·허수아비 등)이 닫혔다.</summary>
    Unscheduled = 4,
}

/// <summary>
/// 간파 창 하나의 시간표(할 일 191 §3.1 ①·§3.2) — 서버가 만들고 모든 피어에 복제한다. 시각은 전부 서버 시각(<see cref="InterruptServerClock"/>).
/// 닫힘 예정 = +∞ 이면 길이를 모르는 창(닫힘 배포 전까지 열린 것으로 본다).
/// </summary>
public struct InterruptWindowRecord
{
    /// <summary>지목자 없음(누구나 간파 가능).</summary>
    public const ulong NoDesignee = ulong.MaxValue;

    public const int SerializedSize = sizeof(ulong) + sizeof(uint) + sizeof(double) * 4 + sizeof(ulong) + sizeof(byte);

    /// <summary>창 소유자(대상) NetworkObjectId.</summary>
    public ulong TargetId;
    /// <summary>서버가 매기는 창 번호(1부터, 0 = 없음). 모든 대상에 걸쳐 하나의 증가열.</summary>
    public uint WindowId;
    /// <summary>공격(패턴) 시작 시각. 모르면 열림 시각과 같다.</summary>
    public double AttackStartTime;
    public double OpenTime;
    /// <summary>닫힘 예정 시각. +∞ = 길이 모름.</summary>
    public double PlannedCloseTime;
    /// <summary>실제 닫힘 시각. +∞ = 아직 열림.</summary>
    public double CloseTime;
    /// <summary>레이드 기믹 지목자 clientId. <see cref="NoDesignee"/> = 누구나.</summary>
    public ulong DesignatedClientId;
    public InterruptWindowCloseKind CloseKind;

    public bool IsClosed => CloseKind != InterruptWindowCloseKind.Open;

    /// <summary>
    /// 지금 아는 닫힘 시각 — 닫혔으면 실제, 아니면 예정. 예정이 지났는데 닫힘 배포가 없으면 예정 시각에 닫힌 것으로 본다
    /// (늦어지면 서버가 실제 닫힘 때 갱신 배포한다).
    /// </summary>
    public double EffectiveCloseTime => IsClosed ? CloseTime : PlannedCloseTime;

    /// <summary><paramref name="time"/> 에 창이 열려 있나 — [열림, 닫힘).</summary>
    public bool IsOpenAt(double time) => time >= OpenTime && time < EffectiveCloseTime;

    public void Write(FastBufferWriter writer)
    {
        writer.WriteValueSafe(TargetId);
        writer.WriteValueSafe(WindowId);
        writer.WriteValueSafe(AttackStartTime);
        writer.WriteValueSafe(OpenTime);
        writer.WriteValueSafe(PlannedCloseTime);
        writer.WriteValueSafe(CloseTime);
        writer.WriteValueSafe(DesignatedClientId);
        writer.WriteValueSafe((byte)CloseKind);
    }

    public static InterruptWindowRecord Read(FastBufferReader reader)
    {
        var r = new InterruptWindowRecord();
        reader.ReadValueSafe(out r.TargetId);
        reader.ReadValueSafe(out r.WindowId);
        reader.ReadValueSafe(out r.AttackStartTime);
        reader.ReadValueSafe(out r.OpenTime);
        reader.ReadValueSafe(out r.PlannedCloseTime);
        reader.ReadValueSafe(out r.CloseTime);
        reader.ReadValueSafe(out r.DesignatedClientId);
        reader.ReadValueSafe(out byte kind);
        r.CloseKind = (InterruptWindowCloseKind)kind;
        return r;
    }
}

/// <summary>
/// 간파 창 시간표(할 일 191 §3.1 ①·§3.2) — 서버는 보스 알림(열림·닫힘)으로 기록을 만들고, 클라는 복제된 기록을 그대로 넣는다.
/// 대상마다 최근 <see cref="MaxRecordsPerTarget"/> 개만 든다(1초 이력 조회에 충분). 순수 C# — EditMode 에서 등록·조기 닫힘·갱신을 고정한다.
/// </summary>
public sealed class InterruptWindowSchedule
{
    /// <summary>예정 시각과 이만큼 안에 닫히면 제때 닫힘(<see cref="InterruptWindowCloseKind.OnTime"/>) — 서버 한두 프레임.</summary>
    public const double OnTimeTolerance = 0.034;

    public const int MaxRecordsPerTarget = 8;

    readonly Dictionary<ulong, List<InterruptWindowRecord>> _byTarget = new Dictionary<ulong, List<InterruptWindowRecord>>();
    uint _lastWindowId;

    public int TargetCount => _byTarget.Count;

    public void Clear()
    {
        _byTarget.Clear();
        _lastWindowId = 0;
    }

    public void Forget(ulong targetId) => _byTarget.Remove(targetId);

    /// <summary>
    /// [서버] 창 열림. 같은 대상에 열린 창이 있으면 그 창을 먼저 이 시각으로 닫는다(<paramref name="closedPrevious"/> — 그것도 배포해야 한다).
    /// <paramref name="plannedClose"/> 가 열림 시각 이하·NaN 이면 길이 모름(+∞).
    /// </summary>
    public InterruptWindowRecord ServerOpen(ulong targetId, double attackStart, double openAt, double plannedClose,
        ulong designatedClientId, out bool closedPrevious, out InterruptWindowRecord previous)
    {
        closedPrevious = ServerClose(targetId, openAt, out previous);

        uint id = ++_lastWindowId;
        if (id == 0) id = ++_lastWindowId;   // uint 한 바퀴 — 0 은 "없음"

        var record = new InterruptWindowRecord
        {
            TargetId = targetId,
            WindowId = id,
            AttackStartTime = double.IsNaN(attackStart) || attackStart > openAt ? openAt : attackStart,
            OpenTime = openAt,
            PlannedCloseTime = double.IsNaN(plannedClose) || plannedClose <= openAt ? double.PositiveInfinity : plannedClose,
            CloseTime = double.PositiveInfinity,
            DesignatedClientId = designatedClientId,
            CloseKind = InterruptWindowCloseKind.Open,
        };
        Upsert(record);
        return record;
    }

    /// <summary>[서버] 대상의 열린 창을 닫는다. 열린 창이 없으면 false. 닫힘 종류는 예정 시각과 비교해 정한다.</summary>
    public bool ServerClose(ulong targetId, double closeAt, out InterruptWindowRecord record)
    {
        record = default;
        if (!_byTarget.TryGetValue(targetId, out List<InterruptWindowRecord> list))
            return false;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            InterruptWindowRecord r = list[i];
            if (r.IsClosed)
                continue;

            r.CloseTime = closeAt < r.OpenTime ? r.OpenTime : closeAt;
            r.CloseKind = ClassifyClose(r.PlannedCloseTime, r.CloseTime);
            list[i] = r;
            record = r;
            return true;
        }

        return false;
    }

    /// <summary>예정 시각과 실제 닫힘으로 닫힘 종류를 정한다.</summary>
    public static InterruptWindowCloseKind ClassifyClose(double plannedClose, double closeAt)
    {
        if (double.IsPositiveInfinity(plannedClose))
            return InterruptWindowCloseKind.Unscheduled;
        if (closeAt < plannedClose - OnTimeTolerance)
            return InterruptWindowCloseKind.Early;
        if (closeAt > plannedClose + OnTimeTolerance)
            return InterruptWindowCloseKind.Extended;
        return InterruptWindowCloseKind.OnTime;
    }

    /// <summary>[클라] 서버가 배포한 기록을 넣는다(같은 창 번호면 덮어쓴다).</summary>
    public void Apply(in InterruptWindowRecord record)
    {
        if (record.WindowId == 0)
            return;
        Upsert(record);
    }

    /// <summary><paramref name="time"/> 에 열려 있던 창. 여럿이면 가장 늦게 열린 것.</summary>
    public bool TryGetWindowAt(ulong targetId, double time, out InterruptWindowRecord record)
    {
        record = default;
        if (!_byTarget.TryGetValue(targetId, out List<InterruptWindowRecord> list))
            return false;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (!list[i].IsOpenAt(time))
                continue;
            record = list[i];
            return true;
        }

        return false;
    }

    /// <summary><paramref name="time"/> 이전(같은 시각 포함)에 열린 창 중 가장 늦게 열린 것(열림·닫힘 무관) — 판정의 창 닫힘/창 없음 구분.</summary>
    public bool TryGetLatestOpenedBy(ulong targetId, double time, out InterruptWindowRecord record)
    {
        record = default;
        if (!_byTarget.TryGetValue(targetId, out List<InterruptWindowRecord> list))
            return false;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].OpenTime > time)
                continue;
            record = list[i];
            return true;
        }

        return false;
    }

    /// <summary>대상의 가장 최근 창(열림·닫힘 무관).</summary>
    public bool TryGetLatest(ulong targetId, out InterruptWindowRecord record)
    {
        record = default;
        if (!_byTarget.TryGetValue(targetId, out List<InterruptWindowRecord> list) || list.Count == 0)
            return false;

        record = list[list.Count - 1];
        return true;
    }

    /// <summary>창 번호로 찾는다.</summary>
    public bool TryGetById(ulong targetId, uint windowId, out InterruptWindowRecord record)
    {
        record = default;
        if (!_byTarget.TryGetValue(targetId, out List<InterruptWindowRecord> list))
            return false;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].WindowId != windowId)
                continue;
            record = list[i];
            return true;
        }

        return false;
    }

    /// <summary>열린 창 전부(재접속 클라에 보낼 때 등).</summary>
    public void CopyOpenTo(List<InterruptWindowRecord> results)
    {
        results.Clear();
        foreach (List<InterruptWindowRecord> list in _byTarget.Values)
            for (int i = 0; i < list.Count; i++)
                if (!list[i].IsClosed)
                    results.Add(list[i]);
    }

    // 창 번호 순서(= 열림 순서)를 유지한다 — 복제가 순서를 바꿔 와도 번호로 자리를 찾는다.
    void Upsert(in InterruptWindowRecord record)
    {
        if (!_byTarget.TryGetValue(record.TargetId, out List<InterruptWindowRecord> list))
        {
            list = new List<InterruptWindowRecord>(MaxRecordsPerTarget);
            _byTarget.Add(record.TargetId, list);
        }

        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].WindowId == record.WindowId)
            {
                list[i] = record;
                return;
            }
        }

        int insertAt = list.Count;
        while (insertAt > 0 && list[insertAt - 1].WindowId > record.WindowId)
            insertAt--;
        list.Insert(insertAt, record);

        while (list.Count > MaxRecordsPerTarget)
            list.RemoveAt(0);
    }
}
