/// <summary>서버가 끊김 콜백을 받은 클라 하나의 기록 — <see cref="GhostClientSweepRule.Decide"/> 의 입력.</summary>
public readonly struct DisconnectedClientRecord
{
    public DisconnectedClientRecord(ulong clientId, int frame, float time)
    {
        ClientId = clientId;
        Frame = frame;
        Time = time;
    }

    public ulong ClientId { get; }
    /// <summary>끊김 콜백을 받은 프레임(<c>Time.frameCount</c>).</summary>
    public int Frame { get; }
    /// <summary>끊김 콜백을 받은 시각(<c>Time.realtimeSinceStartup</c>).</summary>
    public float Time { get; }
}

public enum GhostClientAction
{
    /// <summary>아직 판단하지 않는다 — 끊긴 프레임이거나, 늦은 승인이 아직 올 수 있다.</summary>
    Wait,
    /// <summary>끊겼는데 NGO 접속 목록에 남아 있다(유령) — <c>NetworkManager.DisconnectClient</c> 로 치우고 기록을 뺀다.</summary>
    Disconnect,
    /// <summary>더 볼 필요 없다 — 기록만 뺀다.</summary>
    Forget,
}

/// <summary>
/// [서버] 끊긴 뒤 NGO 에 남은 유령 클라 판정 — 할 일 188.
///
/// NGO 는 같은 끊김 처리 안에서 그 클라의 ConnectionRequest 를 먼저 승인하고(우리 승인 콜백),
/// 끊김 콜백을 부른 뒤, 나중에 <c>ProcessPendingApprovals</c> 로 이미 끊긴 클라를 <c>ConnectedClientsIds</c> 에 넣을 수 있다.
/// 그 유령은 승인 인원을 부풀려(LobbyFull) 이후 접속을 막는다. NGO clientId 는 재사용되지 않으므로
/// 끊김 콜백을 받은 id 가 다음 프레임 이후에도 접속 목록에 있으면 유령이다.
/// </summary>
public static class GhostClientSweepRule
{
    /// <summary>끊긴 id 를 이만큼 지켜본다 — 늦은 승인은 끊김 직후 NGO 갱신 한두 번 안에 온다.</summary>
    public const float RetentionSeconds = 2f;

    public static GhostClientAction Decide(
        DisconnectedClientRecord record,
        bool isStillConnected,
        ulong serverClientId,
        int currentFrame,
        float now,
        float retentionSeconds = RetentionSeconds)
    {
        // 호스트 자신은 DisconnectClient 로 끊을 수 없다(NGO 가 경고만 한다).
        if (record.ClientId == serverClientId)
            return GhostClientAction.Forget;
        // 끊긴 그 프레임에는 NGO 가 아직 정리 중일 수 있다.
        if (currentFrame <= record.Frame)
            return GhostClientAction.Wait;
        if (isStillConnected)
            return GhostClientAction.Disconnect;
        return now - record.Time >= retentionSeconds ? GhostClientAction.Forget : GhostClientAction.Wait;
    }
}
