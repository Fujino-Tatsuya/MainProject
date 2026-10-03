using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 씬에 배치된 프리팹 인스턴스가 테이블 필드를 <b>오버라이드</b>하고 있는지 찾는다(PLAN-data-table.md D6-4).
/// 오버라이드된 인스턴스는 프리팹 에셋 값을 안 따르므로 테이블 값이 닿지 않는다 — 조용히 무시되는 걸 막으려 Verify 가 경고한다.
/// <para>
/// 씬을 열지 않고 <c>.unity</c> YAML 의 PrefabInstance 수정 목록(<c>target: {fileID, guid}</c> + <c>propertyPath</c>)을 텍스트로 훑는다.
/// 프리팹 안에 중첩된 인스턴스(다른 프리팹 속 프리팹)는 보지 않는다.
/// </para>
/// </summary>
public static class DataTableSceneOverrides
{
    private static readonly Regex Modification = new Regex(
        @"- target: \{fileID: (-?\d+), guid: ([0-9a-f]{32}), type: 3\}\s*\r?\n\s*propertyPath: (\S+)",
        RegexOptions.Compiled);

    public readonly struct Finding
    {
        public Finding(string scenePath, DataTableWrite write)
        {
            ScenePath = scenePath;
            Write = write;
        }

        public string ScenePath { get; }
        public DataTableWrite Write { get; }

        public override string ToString() =>
            $"{Path.GetFileNameWithoutExtension(ScenePath)} 씬의 '{Write.Source.AssetId}' 인스턴스가 {Write.Source.Field} 를 오버라이드 — " +
            $"그 인스턴스엔 테이블 값({Write.Source.Value})이 안 닿는다. 씬에서 오버라이드를 되돌릴 것(필드 우클릭 → Revert).";
    }

    /// <summary>씬 YAML 의 PrefabInstance 수정 항목 (대상 프리팹 guid, 프리팹 안 fileID, propertyPath).</summary>
    public static IEnumerable<(string guid, long fileId, string path)> ParseModifications(string sceneText)
    {
        foreach (Match match in Modification.Matches(sceneText))
        {
            yield return (match.Groups[2].Value, long.Parse(match.Groups[1].Value), match.Groups[3].Value);
        }
    }

    public static List<Finding> Find(IReadOnlyList<DataTableWrite> writes)
    {
        // 프리팹 컴포넌트 대상만. (guid, 프리팹 안 fileID, 경로) → 쓰기.
        var byKey = new Dictionary<(string guid, long fileId, string path), DataTableWrite>();
        foreach (DataTableWrite write in writes.Where(w => w.Target is Component))
        {
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(write.Target, out string guid, out long fileId))
            {
                byKey[(guid, fileId, write.PropertyPath)] = write;
            }
        }

        var findings = new List<Finding>();
        if (byKey.Count == 0)
        {
            return findings;
        }

        var guids = new HashSet<string>(byKey.Keys.Select(k => k.guid));
        foreach (string scenePath in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                     .Select(AssetDatabase.GUIDToAssetPath)
                     .Where(p => !DataTableTemplate.IsExcludedPath(p)))
        {
            string text;
            try
            {
                text = File.ReadAllText(scenePath);
            }
            catch (IOException)
            {
                continue;
            }

            if (!guids.Any(g => text.IndexOf(g, StringComparison.Ordinal) >= 0))
            {
                continue; // 이 씬엔 대상 프리팹 인스턴스가 없다
            }

            foreach ((string guid, long fileId, string path) key in ParseModifications(text))
            {
                if (byKey.TryGetValue(key, out DataTableWrite write))
                {
                    findings.Add(new Finding(scenePath, write));
                }
            }
        }

        return findings;
    }
}
