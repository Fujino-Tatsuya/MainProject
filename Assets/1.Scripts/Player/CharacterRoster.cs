using System;
using System.Collections.Generic;
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
        [Tooltip("튜토리얼 안내 문구 변형(_PALADIN/_GUNNER/_ASSASSIN)에 쓸 캐릭터. None = 기본 문구.")]
        [SerializeField] private TutorialCharacter tutorialCharacter;
        [Tooltip("전투 HUD 체력바 채움(포트레잇 합성본, 435×74). 비우면 HUD 프리팹의 기본(거너) 그림.")]
        [SerializeField] private Sprite hudHpFill;

        /// <summary>현재 언어 이름 — Strings 시트 <see cref="NameKey"/>, 행이 없으면 인라인 <c>displayName</c>(ko).</summary>
        public string DisplayName => Loc.Get(NameKey, InlineDisplayName);

        /// <summary>인라인 ko 이름(Strings 시트 수집·폴백용).</summary>
        public string InlineDisplayName => displayName ?? string.Empty;

        /// <summary>
        /// <c>Character.&lt;플레이어 프리팹 이름&gt;.Name</c>(예 <c>Character.Player_Paladin.Name</c>). 프리팹이 없으면 null(인라인 이름).
        /// 배열 순서가 아니라 프리팹 이름으로 정해 칸을 옮겨도 번역이 따라간다. 프리팹 이름을 바꾸면 키도 바뀐다.
        /// </summary>
        public string NameKey => NameKeyFor(playerPrefab != null ? playerPrefab.name : null);

        public static string NameKeyFor(string prefabName) =>
            string.IsNullOrEmpty(prefabName) ? null : $"Character.{prefabName}.Name";
        public GameObject PlayerPrefab => playerPrefab;
        public Sprite Portrait => portrait;
        public bool Available => available;
        public TutorialCharacter TutorialCharacter => tutorialCharacter;
        public Sprite HudHpFill => hudHpFill;
    }

    [SerializeField] private Entry[] entries = new Entry[0];

    public int Count => entries != null ? entries.Length : 0;

    /// <summary>비활성 칸까지 포함한 전체 목록(읽기 전용). 에디터 도구의 목록 표시용.</summary>
    public IReadOnlyList<Entry> Entries => entries ?? Array.Empty<Entry>();

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
