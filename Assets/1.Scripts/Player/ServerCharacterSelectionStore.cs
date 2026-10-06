using System.Collections.Generic;

/// <summary>
/// Lobby 씬 언로드 뒤에도 맵의 플레이어 스폰까지 서버 선택값을 유지한다.
/// 세션 경계를 넘겨서는 안 되므로 NetworkSessionLauncher가 시작/종료 때 비운다.
/// </summary>
public static class ServerCharacterSelectionStore
{
    private static readonly Dictionary<ulong, int> Selections = new Dictionary<ulong, int>();

    public static void Set(ulong clientId, int characterId)
    {
        Selections[clientId] = characterId;
    }

    public static bool TryGet(ulong clientId, out int characterId)
    {
        return Selections.TryGetValue(clientId, out characterId);
    }

    public static void Remove(ulong clientId)
    {
        Selections.Remove(clientId);
    }

    public static void Clear()
    {
        Selections.Clear();
    }
}
