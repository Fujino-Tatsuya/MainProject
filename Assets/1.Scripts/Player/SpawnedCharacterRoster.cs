using System.Collections.Generic;
using Unity.Netcode;

/// <summary>
/// 스폰된 Player → 스폰 로스터(NetworkManager 의 <see cref="NetworkLoadingFlowController.CharacterRoster"/>) 칸.
/// 규칙은 <see cref="CharacterRosterLookup"/>. 로스터나 해시가 없으면 false — 호출부가 기본값을 쓴다.
/// </summary>
public static class SpawnedCharacterRoster
{
    private static CharacterRoster s_hashesRoster;
    private static uint[] s_hashes;

    public static bool TryGetEntry(Player player, out CharacterRoster.Entry entry)
    {
        entry = null;
        if (player == null || !player.TryGetComponent(out NetworkObject networkObject))
            return false;

        NetworkManager manager = NetworkManager.Singleton;
        NetworkLoadingFlowController flow = manager != null ? manager.GetComponent<NetworkLoadingFlowController>() : null;
        CharacterRoster roster = flow != null ? flow.CharacterRoster : null;
        if (roster == null)
            return false;

        int index = CharacterRosterLookup.FindIndex(networkObject.PrefabIdHash, GetHashes(roster));
        if (index < 0)
            return false;

        entry = roster.Entries[index];
        return entry != null;
    }

    // 로스터 프리팹 해시는 에셋 값이라 바뀌지 않는다 — 로스터가 같으면 다시 만들지 않는다.
    private static IReadOnlyList<uint> GetHashes(CharacterRoster roster)
    {
        if (roster == s_hashesRoster && s_hashes != null)
            return s_hashes;

        IReadOnlyList<CharacterRoster.Entry> entries = roster.Entries;
        s_hashes = new uint[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            CharacterRoster.Entry entry = entries[i];
            NetworkObject prefabObject = entry != null && entry.PlayerPrefab != null
                ? entry.PlayerPrefab.GetComponent<NetworkObject>()
                : null;
            s_hashes[i] = prefabObject != null ? prefabObject.PrefabIdHash : 0;
        }
        s_hashesRoster = roster;
        return s_hashes;
    }
}
