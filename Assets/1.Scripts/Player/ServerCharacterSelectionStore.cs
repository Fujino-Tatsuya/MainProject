/// <summary>
/// Lobby 씬 언로드 뒤에도 맵의 플레이어 스폰까지 서버 선택값을 유지한다.
/// 세션 경계를 넘겨서는 안 되므로 NetworkSessionLauncher가 시작/종료 때 비운다.
/// 퇴장(<see cref="Remove"/>)은 clientId 쪽만 지우고 playerId 기준 마지막 선택은 남긴다 — 재접속 승인 때
/// <see cref="BindPlayer"/> 가 새 clientId 에 되살린다(PLAN-48 Q4). 본체는 <see cref="CharacterSelectionMemory"/>.
/// </summary>
public static class ServerCharacterSelectionStore
{
    private static readonly CharacterSelectionMemory Memory = new CharacterSelectionMemory();

    /// <summary>[서버] 접속 승인 때 — clientId 에 playerId 를 묶고, 그 playerId 의 마지막 선택이 있으면 되살린다.</summary>
    public static void BindPlayer(ulong clientId, string playerId)
    {
        Memory.BindPlayer(clientId, playerId);
    }

    public static void Set(ulong clientId, int characterId)
    {
        Memory.Set(clientId, characterId);
    }

    public static bool TryGet(ulong clientId, out int characterId)
    {
        return Memory.TryGet(clientId, out characterId);
    }

    public static void Remove(ulong clientId)
    {
        Memory.RemoveClient(clientId);
    }

    public static void Clear()
    {
        Memory.Clear();
    }
}
