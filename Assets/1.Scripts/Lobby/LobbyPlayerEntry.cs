using System;
using Unity.Netcode;

/// <summary><see cref="LobbySession"/> 의 복제 행 하나 = 로비 자리(접속 순서). 서버가 <see cref="LobbyReadyRules"/> 상태를 그대로 옮긴다.</summary>
public struct LobbyPlayerEntry : INetworkSerializable, IEquatable<LobbyPlayerEntry>
{
    public ulong ClientId;
    public int CharacterId;
    public bool Ready;

    public LobbyPlayerEntry(ulong clientId, int characterId, bool ready)
    {
        ClientId = clientId;
        CharacterId = characterId;
        Ready = ready;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref ClientId);
        s.SerializeValue(ref CharacterId);
        s.SerializeValue(ref Ready);
    }

    public bool Equals(LobbyPlayerEntry o) => ClientId == o.ClientId && CharacterId == o.CharacterId && Ready == o.Ready;
}
