using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dev Boot 캐릭터 드롭다운(<see cref="DevBootToolbar"/>)의 목록 원본 — 프로젝트의 <see cref="CharacterRoster"/> 에셋.
/// 선택값은 프리팹 경로로 <see cref="DevBootTarget.PlayerPrefabPath"/> 에 저장된다(빈 문자열 = NetworkManager 기본값).
/// </summary>
public static class DevBootCharacterCatalog
{
    public const string DefaultLabel = "기본값";

    public readonly struct Character
    {
        public Character(string displayName, string prefabPath, bool selectable)
        {
            DisplayName = displayName;
            PrefabPath = prefabPath;
            Selectable = selectable;
        }

        public string DisplayName { get; }

        /// <summary>프리팹이 비어 있으면 빈 문자열.</summary>
        public string PrefabPath { get; }

        /// <summary>Available 이고 프리팹이 있어야 고를 수 있다.</summary>
        public bool Selectable { get; }
    }

    /// <summary>roster 의 전체 항목(비활성 포함). roster 에셋이 없으면 빈 목록.</summary>
    public static IReadOnlyList<Character> GetCharacters()
    {
        var result = new List<Character>();
        CharacterRoster roster = FindRoster();
        if (roster == null)
        {
            return result;
        }

        foreach (CharacterRoster.Entry entry in roster.Entries)
        {
            if (entry == null)
            {
                continue;
            }

            string prefabPath = entry.PlayerPrefab != null ? AssetDatabase.GetAssetPath(entry.PlayerPrefab) : string.Empty;
            string displayName = string.IsNullOrEmpty(entry.DisplayName)
                ? Path.GetFileNameWithoutExtension(prefabPath)
                : entry.DisplayName;
            result.Add(new Character(displayName, prefabPath, entry.Available && !string.IsNullOrEmpty(prefabPath)));
        }

        return result;
    }

    /// <summary>빈 경로 = "기본값", roster 에 있으면 DisplayName, 없으면 프리팹 파일 이름.</summary>
    public static string GetDisplayName(string prefabPath)
    {
        if (string.IsNullOrEmpty(prefabPath))
        {
            return DefaultLabel;
        }

        foreach (Character character in GetCharacters())
        {
            if (string.Equals(character.PrefabPath, prefabPath, StringComparison.OrdinalIgnoreCase))
            {
                return character.DisplayName;
            }
        }

        return Path.GetFileNameWithoutExtension(prefabPath);
    }

    private static CharacterRoster FindRoster()
    {
        string[] guids = AssetDatabase.FindAssets("t:" + nameof(CharacterRoster));
        return guids.Length == 0
            ? null
            : AssetDatabase.LoadAssetAtPath<CharacterRoster>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }
}
