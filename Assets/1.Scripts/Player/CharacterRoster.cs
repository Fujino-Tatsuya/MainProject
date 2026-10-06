using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Characters/Character Roster")]
public class CharacterRoster : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        [SerializeField] private string displayName;
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private Sprite portrait;
        [SerializeField] private bool available = true;

        public string DisplayName => displayName;
        public GameObject PlayerPrefab => playerPrefab;
        public Sprite Portrait => portrait;
        public bool Available => available;
    }

    [SerializeField] private Entry[] entries = new Entry[0];

    public int Count => entries != null ? entries.Length : 0;

    /// <summary>
    /// 배열 인덱스를 캐릭터 id로 사용한다. 범위 밖이거나 비활성인 칸은 선택할 수 없다.
    /// </summary>
    public bool TryGetAvailableCharacter(int characterId, out Entry entry)
    {
        entry = null;
        if (entries == null || characterId < 0 || characterId >= entries.Length)
        {
            return false;
        }

        var candidate = entries[characterId];
        if (candidate == null || !candidate.Available)
        {
            return false;
        }

        entry = candidate;
        return true;
    }
}
