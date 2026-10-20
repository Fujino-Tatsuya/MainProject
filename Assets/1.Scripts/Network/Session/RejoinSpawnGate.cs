/// <summary>재합류 스폰 판정 종류 — <see cref="RejoinSpawnGate.Evaluate"/>.</summary>
public enum RejoinSpawnDecision
{
    /// <summary>지금 스폰한다.</summary>
    Ready,
    /// <summary>다음 프레임에 다시 본다(사유 = <see cref="RejoinSpawnVerdict.Reason"/>).</summary>
    Hold,
    /// <summary>대기열에서 뺀다 — 스폰하지 않는다.</summary>
    Drop,
}

/// <summary>재합류 스폰을 보류·포기한 사유. <see cref="None"/> = Ready.</summary>
public enum RejoinSpawnReason
{
    None,
    // ── Drop ──
    /// <summary>대기 중에 나갔다.</summary>
    NotConnected,
    /// <summary>판이 끝나는 중(보스 격파·결과 전환 예약·명단 종료) — 로비 Single 전환이 데려간다.</summary>
    ResultTransitionReserved,
    /// <summary>이미 PlayerObject 가 있다(판 재시작 스폰 등이 먼저 세움) — 중복 스폰 방지.</summary>
    AlreadyHasPlayerObject,
    // ── Hold ──
    /// <summary>씬 동기화(SynchronizeComplete)를 아직 마치지 않았다.</summary>
    NotSynchronized,
    /// <summary>맵 로딩 흐름이 끝나지 않았다(Completed·MainGameReady 전, 판 재시작 중).</summary>
    MapNotReady,
    /// <summary>엘리베이터·보스 텔레포트 카운트다운·도착 대기 중.</summary>
    ElevatorOrTeleportBusy,
    /// <summary>보스 등장 연출 중.</summary>
    BossCinematic,
    /// <summary>슬로모션 중 — 접속 때 다시 보내지 않는 named message 라 끝난 뒤 세운다.</summary>
    SlowMotion,
    /// <summary>전멸 유예 중(리스폰·결과 판정 직전).</summary>
    WipePending,
}

/// <summary>재합류 스폰 판정 1건.</summary>
public readonly struct RejoinSpawnVerdict
{
    public RejoinSpawnVerdict(RejoinSpawnDecision decision, RejoinSpawnReason reason)
    {
        Decision = decision;
        Reason = reason;
    }

    public RejoinSpawnDecision Decision { get; }
    public RejoinSpawnReason Reason { get; }

    public static RejoinSpawnVerdict Ready => new RejoinSpawnVerdict(RejoinSpawnDecision.Ready, RejoinSpawnReason.None);
    public static RejoinSpawnVerdict Hold(RejoinSpawnReason reason) => new RejoinSpawnVerdict(RejoinSpawnDecision.Hold, reason);
    public static RejoinSpawnVerdict Drop(RejoinSpawnReason reason) => new RejoinSpawnVerdict(RejoinSpawnDecision.Drop, reason);

    public override string ToString() => Decision == RejoinSpawnDecision.Ready ? "Ready" : $"{Decision}({Reason})";
}

/// <summary>재합류 스폰 판정 입력 — 서버가 매 프레임 채운다(NetworkLoadingFlowController).</summary>
public readonly struct RejoinSpawnSnapshot
{
    public RejoinSpawnSnapshot(
        bool clientConnected,
        bool clientSynchronized,
        bool mapReady,
        bool resultTransitionReserved,
        bool hasPlayerObject,
        bool elevatorOrTeleportBusy,
        bool bossCinematic,
        bool slowMotionActive,
        bool wipePending)
    {
        ClientConnected = clientConnected;
        ClientSynchronized = clientSynchronized;
        MapReady = mapReady;
        ResultTransitionReserved = resultTransitionReserved;
        HasPlayerObject = hasPlayerObject;
        ElevatorOrTeleportBusy = elevatorOrTeleportBusy;
        BossCinematic = bossCinematic;
        SlowMotionActive = slowMotionActive;
        WipePending = wipePending;
    }

    public bool ClientConnected { get; }
    public bool ClientSynchronized { get; }
    /// <summary>로딩 흐름 Completed && 서버 MainGameReady && 타깃 씬 로드됨 && 전환 중 아님.</summary>
    public bool MapReady { get; }
    public bool ResultTransitionReserved { get; }
    public bool HasPlayerObject { get; }
    public bool ElevatorOrTeleportBusy { get; }
    public bool BossCinematic { get; }
    public bool SlowMotionActive { get; }
    public bool WipePending { get; }
}

/// <summary>
/// 판 도중 재합류 스폰 보류 판정(C14 설계 메모 §3.4) — 순수 함수(EditMode 테스트 대상).
/// 순서: Drop(나감 → 판 끝남 → 이미 있음) → Hold(동기화 → 맵 준비 → 엘리베이터·텔레포트 → 보스 연출 → 슬로모션 → 전멸 유예) → Ready.
/// 보류 상한 시간은 두지 않는다(연출마다 끝이 있다). 오래 보류되면 호출자가 경고만 남긴다.
/// </summary>
public static class RejoinSpawnGate
{
    public static RejoinSpawnVerdict Evaluate(in RejoinSpawnSnapshot s)
    {
        if (!s.ClientConnected)
            return RejoinSpawnVerdict.Drop(RejoinSpawnReason.NotConnected);
        if (s.ResultTransitionReserved)
            return RejoinSpawnVerdict.Drop(RejoinSpawnReason.ResultTransitionReserved);
        if (s.HasPlayerObject)
            return RejoinSpawnVerdict.Drop(RejoinSpawnReason.AlreadyHasPlayerObject);

        if (!s.ClientSynchronized)
            return RejoinSpawnVerdict.Hold(RejoinSpawnReason.NotSynchronized);
        if (!s.MapReady)
            return RejoinSpawnVerdict.Hold(RejoinSpawnReason.MapNotReady);
        if (s.ElevatorOrTeleportBusy)
            return RejoinSpawnVerdict.Hold(RejoinSpawnReason.ElevatorOrTeleportBusy);
        if (s.BossCinematic)
            return RejoinSpawnVerdict.Hold(RejoinSpawnReason.BossCinematic);
        if (s.SlowMotionActive)
            return RejoinSpawnVerdict.Hold(RejoinSpawnReason.SlowMotion);
        if (s.WipePending)
            return RejoinSpawnVerdict.Hold(RejoinSpawnReason.WipePending);

        return RejoinSpawnVerdict.Ready;
    }
}
