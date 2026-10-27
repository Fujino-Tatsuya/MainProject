using System.Collections.Generic;

/// <summary>
/// 스폰된 Player 의 NetworkObject.PrefabIdHash → 로스터 칸 인덱스(= 캐릭터 id). 순수 함수, EditMode 테스트 대상.
/// 해시는 모든 피어에서 같아 복제가 필요 없다. 해시 0 칸(프리팹 없음)은 건너뛰고, 같은 해시가 둘이면 앞 칸이 이긴다.
/// 튜토리얼 문구용 <see cref="TutorialCharacterLookup"/> 과 같은 규칙이다.
/// </summary>
public static class CharacterRosterLookup
{
    public static int FindIndex(uint prefabIdHash, IReadOnlyList<uint> rosterPrefabHashes)
    {
        if (prefabIdHash == 0 || rosterPrefabHashes == null)
            return -1;

        for (int i = 0; i < rosterPrefabHashes.Count; i++)
        {
            if (rosterPrefabHashes[i] != 0 && rosterPrefabHashes[i] == prefabIdHash)
                return i;
        }

        return -1;
    }
}
