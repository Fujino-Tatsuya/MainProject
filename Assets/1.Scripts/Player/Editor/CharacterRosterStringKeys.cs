using System.Collections.Generic;
using System.Linq;
using UnityEditor;

/// <summary>
/// 캐릭터 이름(<see cref="CharacterRoster.Entry.NameKey"/>)을 Export 때 Strings 시트에 모은다(로컬라이징 L2).
/// ko = 지금 roster 의 <c>displayName</c>, en 은 빈 칸. 프리팹이 없거나 이름이 빈 칸은 뺀다.
/// </summary>
[InitializeOnLoad]
internal static class CharacterRosterStringKeys
{
    static CharacterRosterStringKeys()
    {
        StringKeyRegistry.Register(CollectProject);
    }

    private static IEnumerable<StringKeyInfo> CollectProject() =>
        AssetDatabase.FindAssets("t:" + nameof(CharacterRoster), new[] { "Assets" })
            .Select(guid => AssetDatabase.LoadAssetAtPath<CharacterRoster>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(roster => roster != null)
            .SelectMany(Collect)
            .ToList();

    public static List<StringKeyInfo> Collect(CharacterRoster roster)
    {
        var keys = new List<StringKeyInfo>();
        IReadOnlyList<CharacterRoster.Entry> entries = roster.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            CharacterRoster.Entry entry = entries[i];
            if (entry == null || entry.NameKey == null || string.IsNullOrWhiteSpace(entry.InlineDisplayName))
                continue;

            keys.Add(new StringKeyInfo(entry.NameKey, $"{roster.name} · entries[{i}].displayName", entry.InlineDisplayName));
        }

        return keys;
    }
}
