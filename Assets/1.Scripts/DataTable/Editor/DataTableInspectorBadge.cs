using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 인스펙터 머리에 "이 객체의 이 필드들은 데이터 테이블이 관리한다" 를 표시한다(PLAN-data-table.md D6-5).
/// 개발자가 인스펙터 값을 고쳐 놓고 테이블 모드에서 "왜 안 바뀌지" 하는 걸 줄인다 — 테이블과 다른 값은 나란히 보여 준다.
/// <para>
/// 대상: SO 에셋 · 프리팹 에셋 · 씬의 프리팹 인스턴스(원본 프리팹의 행을 찾는다) · 프리팹 편집 모드.
/// 테이블은 xlsx 가 바뀔 때만 다시 읽는다(폴더 파일들의 수정 시각을 1초에 한 번 확인).
/// </para>
/// </summary>
[InitializeOnLoad]
public static class DataTableInspectorBadge
{
    private const int MaxListed = 6;
    private const double StampCheckIntervalSeconds = 1.0;

    private static Dictionary<Object, List<DataTableWrite>> writesByTarget = new Dictionary<Object, List<DataTableWrite>>();
    private static bool tableHasErrors;
    private static string loadedStamp;
    private static double nextStampCheck;

    static DataTableInspectorBadge()
    {
        Editor.finishedDefaultHeaderGUI -= OnHeaderGUI;
        Editor.finishedDefaultHeaderGUI += OnHeaderGUI;
    }

    private static void OnHeaderGUI(Editor editor)
    {
        if (editor.targets.Length != 1)
        {
            return;
        }

        RefreshIfTableChanged();
        if (tableHasErrors)
        {
            EditorGUILayout.HelpBox("데이터 테이블에 오류가 있다 — Tools/Data/Verify 로 확인.", MessageType.Warning);
            return;
        }

        if (writesByTarget.Count == 0)
        {
            return;
        }

        foreach ((Object shown, List<DataTableWrite> writes) in Matches(editor.target))
        {
            Draw(shown, writes);
        }
    }

    /// <summary>인스펙터에 보이는 객체(들) ↔ 그것을 덮어쓰는 테이블 쓰기. 씬 인스턴스면 원본 프리팹 컴포넌트의 쓰기를 쓴다.</summary>
    private static IEnumerable<(Object shown, List<DataTableWrite> writes)> Matches(Object inspected)
    {
        if (inspected is ScriptableObject so)
        {
            if (writesByTarget.TryGetValue(so, out List<DataTableWrite> soWrites))
            {
                yield return (so, soWrites);
            }

            yield break;
        }

        if (!(inspected is GameObject go))
        {
            yield break;
        }

        foreach (Component component in go.GetComponents<Component>())
        {
            if (component == null)
            {
                continue;
            }

            Object source = SourceOf(component);
            if (source != null && writesByTarget.TryGetValue(source, out List<DataTableWrite> componentWrites))
            {
                yield return (component, componentWrites);
            }
        }
    }

    /// <summary>테이블 쓰기의 대상(프리팹 에셋 컴포넌트)으로 거슬러 올라간다.</summary>
    private static Object SourceOf(Component component)
    {
        if (EditorUtility.IsPersistent(component))
        {
            return component; // 프리팹 에셋 자체
        }

        Component fromInstance = PrefabUtility.GetCorrespondingObjectFromSource(component);
        if (fromInstance != null)
        {
            return fromInstance; // 씬 인스턴스 → 바로 위 원본(Variant 인스턴스면 Variant)
        }

        // 프리팹 편집 모드: 편집 중인 임시 사본 ↔ 에셋의 같은 타입 컴포넌트(프리팹당 하나 규칙이라 타입으로 찾는다).
        PrefabStage stage = PrefabStageUtility.GetPrefabStage(component.gameObject);
        if (stage != null)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(stage.assetPath);
            Component[] same = root == null
                ? Array.Empty<Component>()
                : root.GetComponentsInChildren(component.GetType(), true).Where(c => c.GetType() == component.GetType()).ToArray();
            return same.Length == 1 ? same[0] : null;
        }

        return null;
    }

    private static void Draw(Object shown, List<DataTableWrite> writes)
    {
        var current = new SerializedObject(shown);
        var differing = new List<string>();
        foreach (DataTableWrite write in writes)
        {
            if (!DataTableApplier.MatchesTable(current, write, out string value))
            {
                differing.Add($"{write.Source.Field}(인스펙터 {value} / 테이블 {write.Source.Value})");
            }
        }

        var text = new StringBuilder();
        text.Append($"데이터 테이블 관리 — {shown.GetType().Name} 필드 {writes.Count}개는 테이블 모드 Play·빌드에서 xlsx 값으로 덮어쓴다.");
        if (differing.Count == 0)
        {
            text.Append(" 지금 값은 테이블과 같다.");
        }
        else
        {
            text.Append($"\n테이블과 다른 값 {differing.Count}개: ");
            text.Append(string.Join(", ", differing.Take(MaxListed)));
            if (differing.Count > MaxListed)
            {
                text.Append($" 외 {differing.Count - MaxListed}개");
            }
        }

        EditorGUILayout.HelpBox(text.ToString(), differing.Count == 0 ? MessageType.Info : MessageType.Warning);
    }

    private static void RefreshIfTableChanged()
    {
        if (EditorApplication.timeSinceStartup < nextStampCheck)
        {
            return;
        }

        nextStampCheck = EditorApplication.timeSinceStartup + StampCheckIntervalSeconds;
        string stamp = Stamp();
        if (stamp == loadedStamp)
        {
            return;
        }

        loadedStamp = stamp;
        DataTableSource.Result result = DataTableSource.Load();
        tableHasErrors = result.Issues.HasErrors;
        writesByTarget = result.Writes
            .GroupBy(w => w.Target)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>폴더 xlsx 들의 이름·수정 시각 — 바뀌었을 때만 다시 읽는다.</summary>
    private static string Stamp()
    {
        if (!Directory.Exists(DataTableSource.Folder))
        {
            return string.Empty;
        }

        return string.Join("|", Directory.GetFiles(DataTableSource.Folder, "*.xlsx")
            .Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => f + "@" + File.GetLastWriteTimeUtc(f).Ticks));
    }
}
