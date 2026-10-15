using System.Collections.Generic;

/// <summary>로스터 역조회 결과. Resolved 가 아니면 호출부가 컴포넌트 판별로 폴백한다.</summary>
public enum TutorialCharacterLookupStatus
{
    Resolved,
    NoRoster,
    HashNotFound,
    NoMapping,
}

/// <summary>로스터 한 칸(인덱스 = 캐릭터 id) — 그 PlayerPrefab 의 NetworkObject PrefabIdHash 와 튜토리얼 캐릭터.</summary>
public readonly struct TutorialRosterRow
{
    public readonly uint PrefabIdHash;
    public readonly TutorialCharacter Character;

    public TutorialRosterRow(uint prefabIdHash, TutorialCharacter character)
    {
        PrefabIdHash = prefabIdHash;
        Character = character;
    }
}

/// <summary>
/// 스폰된 Player 의 PrefabIdHash → 로스터 캐릭터 id → <see cref="TutorialCharacter"/> (순수 함수, EditMode 테스트 대상).
/// 해시 0 칸(프리팹 없음)은 건너뛰고, 같은 해시가 둘이면 앞 칸이 이긴다.
/// </summary>
public static class TutorialCharacterLookup
{
    public static TutorialCharacterLookupStatus Resolve(
        uint prefabIdHash, IReadOnlyList<TutorialRosterRow> rows, out int rosterId, out TutorialCharacter character)
    {
        rosterId = -1;
        character = TutorialCharacter.None;
        if (rows == null || rows.Count == 0)
            return TutorialCharacterLookupStatus.NoRoster;

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].PrefabIdHash == 0 || rows[i].PrefabIdHash != prefabIdHash)
                continue;

            rosterId = i;
            character = rows[i].Character;
            return character == TutorialCharacter.None
                ? TutorialCharacterLookupStatus.NoMapping
                : TutorialCharacterLookupStatus.Resolved;
        }

        return TutorialCharacterLookupStatus.HashNotFound;
    }

    /// <summary>역조회가 성공했으면 그 캐릭터, 아니면 폴백(컴포넌트 판별) 결과.</summary>
    public static TutorialCharacter Choose(TutorialCharacterLookupStatus status, TutorialCharacter resolved, TutorialCharacter fallback)
        => status == TutorialCharacterLookupStatus.Resolved ? resolved : fallback;
}
