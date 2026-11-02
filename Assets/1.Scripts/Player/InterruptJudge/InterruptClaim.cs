using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// [오너 → 서버] 간파 성공 신고 1회(할 일 191 §3.1 ③) — 오너 클라가 Hit 타이밍 판정(<see cref="InterruptManager.Judge(in InterruptJudgeAttempt)"/>)이 성공일 때만 보낸다.
/// 서버는 같은 판정 함수를 서버 이력으로 다시 돌려 검증한다(<see cref="InterruptClaimValidator"/>). 시각은 서버 시각.
/// </summary>
public struct InterruptClaim
{
    public const int SerializedSize = sizeof(ulong) * 2 + sizeof(uint) + sizeof(double) + sizeof(float) * 6 + sizeof(byte);

    /// <summary>시전자 NetworkObjectId.</summary>
    public ulong ActorId;
    /// <summary>성공 대상 NetworkObjectId.</summary>
    public ulong TargetId;
    /// <summary>판정에 쓴 창 번호(창 시간표 복제값 — 서버와 같은 번호).</summary>
    public uint WindowId;
    /// <summary>판정 시각(서버 시각).</summary>
    public double JudgeTime;
    /// <summary>판정 상자가 향한 방향(근접 = 시전 방향, 대쉬 = 대시 방향).</summary>
    public Vector3 AttackDirection;
    /// <summary>오너가 본 판정 위치(내 위치) — 기록·QA 용. 검증은 서버 이력 위치로 한다.</summary>
    public Vector3 Position;
    public bool ViaDash;

    /// <summary>판정 결과로 신고를 만든다(성공 결과만 뜻이 있다).</summary>
    public static InterruptClaim FromJudge(ulong actorId, bool viaDash, in InterruptJudgeResult result) =>
        new InterruptClaim
        {
            ActorId = actorId,
            TargetId = result.TargetId,
            WindowId = result.WindowId,
            JudgeTime = result.JudgeTime,
            AttackDirection = result.AttackDirection,
            Position = result.ResolvePosition,
            ViaDash = viaDash,
        };

    /// <summary>서버 재판정 입력 — 같은 판정 시각·공격 방향·대상.</summary>
    public InterruptJudgeAttempt ToAttempt(double sampleOffset = 0.0) =>
        new InterruptJudgeAttempt
        {
            ActorId = ActorId,
            JudgeTime = JudgeTime,
            AttackDirection = AttackDirection,
            TargetId = TargetId,
            SampleOffset = sampleOffset,
        };

    public void Write(FastBufferWriter writer)
    {
        writer.WriteValueSafe(ActorId);
        writer.WriteValueSafe(TargetId);
        writer.WriteValueSafe(WindowId);
        writer.WriteValueSafe(JudgeTime);
        writer.WriteValueSafe(AttackDirection.x);
        writer.WriteValueSafe(AttackDirection.y);
        writer.WriteValueSafe(AttackDirection.z);
        writer.WriteValueSafe(Position.x);
        writer.WriteValueSafe(Position.y);
        writer.WriteValueSafe(Position.z);
        writer.WriteValueSafe(ViaDash ? (byte)1 : (byte)0);
    }

    public static InterruptClaim Read(FastBufferReader reader)
    {
        var c = new InterruptClaim();
        reader.ReadValueSafe(out c.ActorId);
        reader.ReadValueSafe(out c.TargetId);
        reader.ReadValueSafe(out c.WindowId);
        reader.ReadValueSafe(out c.JudgeTime);
        reader.ReadValueSafe(out float dx);
        reader.ReadValueSafe(out float dy);
        reader.ReadValueSafe(out float dz);
        reader.ReadValueSafe(out float px);
        reader.ReadValueSafe(out float py);
        reader.ReadValueSafe(out float pz);
        reader.ReadValueSafe(out byte dash);
        c.AttackDirection = new Vector3(dx, dy, dz);
        c.Position = new Vector3(px, py, pz);
        c.ViaDash = dash != 0;
        return c;
    }
}

/// <summary>서버 거부 이유(할 일 191 D12 QA 기록 칸과 같은 말).</summary>
public enum InterruptClaimRejectReason : byte
{
    None = 0,
    /// <summary>허용 오차를 넣어도 판정 상자가 대상에 닿지 않는다.</summary>
    OutOfRange = 1,
    /// <summary>허용 오차를 넣어도 대상 정면 허용각 밖.</summary>
    Angle = 2,
    /// <summary>허용 오차를 넣어도 판정 시각에 창이 없거나 닫혔다(조기 닫힘 포함, D7).</summary>
    WindowClosed = 3,
    /// <summary>같은 창의 첫 승인 뒤 동시 인정 시간(D8)을 넘겨 왔다.</summary>
    FirstComeLost = 4,
    /// <summary>그 밖의 서버 거부 — 시전자 소유 불일치·이력 없음·시전자 쓰러짐/CC·대상 간파 불가·지목자 아님·대상 규칙(23호 기믹 등)·중복 신고.</summary>
    ServerRejected = 5,
}

/// <summary>동시 성공 장부 결과(할 일 191 D8).</summary>
public enum InterruptClaimOrder : byte
{
    /// <summary>창의 첫 승인 — 이것만 대상 성공 효과를 준다(D9).</summary>
    First = 0,
    /// <summary>첫 승인 뒤 동시 인정 시간 안 — 승인(성공 기록만, 효과 없음).</summary>
    Simultaneous = 1,
    /// <summary>동시 인정 시간 뒤 — 선착순 패배(거부).</summary>
    Lost = 2,
    /// <summary>같은 시전자가 같은 창에 다시 신고 — 거부.</summary>
    Duplicate = 3,
}

/// <summary>[서버] 신고 하나의 처리 결과.</summary>
public struct InterruptClaimOutcome
{
    public InterruptClaim Claim;
    public ulong SenderClientId;
    public bool Approved;
    /// <summary>승인이면서 창의 첫 승인(대상 성공 효과를 준 것).</summary>
    public bool IsFirst;
    public InterruptClaimRejectReason RejectReason;
    /// <summary>서버 재판정의 실패 이유(검증 통과 = None).</summary>
    public InterruptJudgeFailReason JudgeFailReason;
    /// <summary>서버 재판정이 고른 창 번호(동시 성공 장부 키). 검증 실패 = 0.</summary>
    public uint WindowId;
}

/// <summary>[모든 피어] 승인 배포 — 같은 창의 지금까지 승인된 시전자 전체(첫 승인 + 동시 성공자).</summary>
public readonly struct InterruptClaimApproval
{
    public InterruptClaimApproval(ulong targetId, uint windowId, ulong firstActorId, ulong newActorId, IReadOnlyList<ulong> approvedActorIds)
    {
        TargetId = targetId;
        WindowId = windowId;
        FirstActorId = firstActorId;
        NewActorId = newActorId;
        ApprovedActorIds = approvedActorIds;
    }

    public ulong TargetId { get; }
    public uint WindowId { get; }
    /// <summary>효과를 준 첫 승인 시전자.</summary>
    public ulong FirstActorId { get; }
    /// <summary>이번 메시지로 새로 승인된 시전자.</summary>
    public ulong NewActorId { get; }
    public IReadOnlyList<ulong> ApprovedActorIds { get; }
}

/// <summary>[오너] 거부 통지.</summary>
public readonly struct InterruptClaimRejection
{
    public InterruptClaimRejection(ulong actorId, ulong targetId, uint windowId, InterruptClaimRejectReason reason,
        InterruptJudgeFailReason judgeFailReason)
    {
        ActorId = actorId;
        TargetId = targetId;
        WindowId = windowId;
        Reason = reason;
        JudgeFailReason = judgeFailReason;
    }

    public ulong ActorId { get; }
    public ulong TargetId { get; }
    public uint WindowId { get; }
    public InterruptClaimRejectReason Reason { get; }
    public InterruptJudgeFailReason JudgeFailReason { get; }
}
