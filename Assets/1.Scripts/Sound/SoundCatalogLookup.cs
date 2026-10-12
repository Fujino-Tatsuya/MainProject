using System;
using System.Collections.Generic;

/// <summary>
/// <see cref="SoundEntry.Key"/>를 엔트리로 바꾸는 순수 조회 캐시.
/// 비어 있는 키와 중복 키를 수집하며, 중복일 때는 배열에서 먼저 나온 엔트리를 사용한다.
/// </summary>
public sealed class SoundCatalogLookup
{
    private readonly Dictionary<string, SoundEntry> _entries =
        new Dictionary<string, SoundEntry>(StringComparer.Ordinal);

    private readonly List<int> _emptyKeyIndices = new List<int>();
    private readonly List<string> _duplicateKeys = new List<string>();

    public SoundCatalogLookup(IReadOnlyList<SoundEntry> entries)
    {
        if (entries == null)
        {
            return;
        }

        var reportedDuplicates = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < entries.Count; i++)
        {
            SoundEntry entry = entries[i];
            string key = entry?.Key;
            if (string.IsNullOrWhiteSpace(key))
            {
                _emptyKeyIndices.Add(i);
                continue;
            }

            if (!_entries.TryAdd(key, entry) && reportedDuplicates.Add(key))
            {
                _duplicateKeys.Add(key);
            }
        }
    }

    public IReadOnlyList<int> EmptyKeyIndices => _emptyKeyIndices;
    public IReadOnlyList<string> DuplicateKeys => _duplicateKeys;

    public bool TryGet(string key, out SoundEntry entry)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            entry = null;
            return false;
        }

        return _entries.TryGetValue(key, out entry);
    }
}
