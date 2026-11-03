using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 특성 아이콘 연결(할 일 165) — 아트 납품 <c>TraitIconMap.csv</c>(열 <c>id,…,icon,…</c>)를 읽어 각 <see cref="TraitDefinition"/> 의 <c>icon</c> 칸을 채운다.
/// <c>icon</c> 열은 아이콘 폴더(기본 <c>Assets/50.Art/UI/HUD/TraitIcon</c>) 기준 상대 경로. 빈 칸·없는 스프라이트·없는 ID 는 건너뛰고 경고.
/// 다시 돌려도 같은 결과(값이 같으면 저장 안 함). 다른 csv 는 <see cref="Apply"/> 에 경로를 넘긴다.
/// <c>inactive_icon</c> 열은 아직 읽지 않는다(후속).
/// </summary>
public static class TraitIconMapAuthoring
{
    public const string DefaultIconRoot = "Assets/50.Art/UI/HUD/TraitIcon";
    public const string DefaultCsvPath = DefaultIconRoot + "/TraitIconMap.csv";
    private const string DefinitionFolder = "Assets/9.ScriptableObject/Traits";

    [MenuItem("Tools/Tutorial/특성 아이콘 연결 (TraitIconMap.csv)")]
    public static void ApplyDefault() => Apply(DefaultCsvPath, DefaultIconRoot);

    /// <param name="csvPath">csv 경로(프로젝트 상대 또는 절대).</param>
    /// <param name="iconRoot">icon 열이 가리키는 기준 폴더(프로젝트 상대 <c>Assets/…</c>).</param>
    /// <returns>icon 칸을 바꾼 정의 수.</returns>
    public static int Apply(string csvPath, string iconRoot)
    {
        if (!File.Exists(csvPath))
        {
            Debug.LogError($"[Trait] 아이콘 맵 csv 가 없다 — {csvPath}");
            return 0;
        }

        Dictionary<string, string> map = Parse(File.ReadAllText(csvPath), out List<string> errors);
        foreach (string error in errors)
            Debug.LogWarning($"[Trait] 아이콘 맵: {error}");

        var seen = new HashSet<string>();
        int changed = 0, same = 0, skipped = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:TraitDefinition", new[] { DefinitionFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var definition = AssetDatabase.LoadAssetAtPath<TraitDefinition>(path);
            if (definition == null || string.IsNullOrEmpty(definition.Id) || !map.TryGetValue(definition.Id, out string relative))
                continue;

            seen.Add(definition.Id);
            string spritePath = $"{iconRoot.TrimEnd('/')}/{relative}";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if (sprite == null)
            {
                Debug.LogWarning($"[Trait] 아이콘 맵: '{definition.Id}' 스프라이트를 못 찾았다 — {spritePath}", definition);
                skipped++;
                continue;
            }

            var so = new SerializedObject(definition);
            SerializedProperty icon = so.FindProperty("icon");
            if (icon.objectReferenceValue == sprite)
            {
                same++;
                continue;
            }

            icon.objectReferenceValue = sprite;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            changed++;
        }

        foreach (string id in map.Keys)
        {
            if (!seen.Contains(id))
                Debug.LogWarning($"[Trait] 아이콘 맵: csv 의 '{id}' 에 맞는 특성 정의가 없다.");
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Trait] 아이콘 연결 — 바꿈 {changed} · 이미 같음 {same} · 건너뜀 {skipped} · csv 행 {map.Count} ({csvPath})");
        return changed;
    }

    /// <summary>csv 본문 → id → icon 상대 경로. 머리줄에서 <c>id</c>·<c>icon</c> 열을 찾는다. 따옴표 없는 단순 csv 기준.</summary>
    public static Dictionary<string, string> Parse(string text, out List<string> errors)
    {
        errors = new List<string>();
        var map = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(text))
        {
            errors.Add("빈 csv");
            return map;
        }

        string[] lines = text.TrimStart('﻿').Replace("\r", "").Split('\n');
        string[] header = lines[0].Split(',');
        int idColumn = System.Array.IndexOf(header, "id");
        int iconColumn = System.Array.IndexOf(header, "icon");
        if (idColumn < 0 || iconColumn < 0)
        {
            errors.Add("머리줄에 id·icon 열이 없다");
            return map;
        }

        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0)
                continue;

            string[] cells = lines[i].Split(',');
            if (cells.Length <= Mathf.Max(idColumn, iconColumn))
            {
                errors.Add($"{i + 1}줄: 열이 모자란다");
                continue;
            }

            string id = cells[idColumn].Trim();
            string icon = cells[iconColumn].Trim();
            if (id.Length == 0 || icon.Length == 0)
            {
                errors.Add($"{i + 1}줄: id 또는 icon 이 비었다");
                continue;
            }

            if (map.ContainsKey(id))
            {
                errors.Add($"{i + 1}줄: '{id}' 중복 — 뒤 행을 쓴다");
            }

            map[id] = icon;
        }

        return map;
    }
}
