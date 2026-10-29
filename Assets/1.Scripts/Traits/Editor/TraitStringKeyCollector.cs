using System.Collections.Generic;
using System.Linq;
using UnityEditor;

/// <summary>
/// 특성별 Strings 키(<see cref="TraitStringKeys"/> — <c>Trait.&lt;ID&gt;.Name/.Category/.Desc/.Conflict</c>)를 Export 때 Strings 시트에 모은다(할 일 125 5/5).
/// ko = 정의 인라인 값(상충 문구는 등록부 ko), en 빈 칸. 빈 값·레거시 폴더 정의는 뺀다. 이미 시트에 있는 키는 Export 가 건드리지 않는다.
/// </summary>
[InitializeOnLoad]
internal static class TraitStringKeyCollector
{
    static TraitStringKeyCollector()
    {
        StringKeyRegistry.Register(CollectProject);
    }

    private static IEnumerable<StringKeyInfo> CollectProject()
    {
        List<TraitDefinition> definitions = AssetDatabase.FindAssets("t:" + nameof(TraitDefinition), new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => !DataTableTemplate.IsExcludedPath(path))
            .Select(AssetDatabase.LoadAssetAtPath<TraitDefinition>)
            .Where(trait => trait != null)
            .ToList();
        return Collect(definitions, TraitContent.Registry);
    }

    /// <summary>정의 목록 + 등록부 → 키(정의 순서, 칸 순서 Name·Category·Desc 다음 상충 문구).</summary>
    public static List<StringKeyInfo> Collect(IEnumerable<TraitDefinition> definitions, TraitContentRegistry registry)
    {
        var keys = new List<StringKeyInfo>();
        foreach (TraitDefinition trait in definitions)
        {
            if (trait == null || !TraitStringKeys.IsValidId(trait.Id))
                continue;

            Add(keys, TraitStringKeys.Name(trait.Id), $"{trait.name} · 특성 이름", trait.InlineDisplayName);
            Add(keys, TraitStringKeys.Category(trait.Id), $"{trait.name} · 특성 분류(카드 이름 아래 줄)", trait.InlineCategory);
            Add(keys, TraitStringKeys.Description(trait.Id),
                $"{trait.name} · 특성 설명 템플릿({{필드}}·{{필드:%}}·{{dmg}} — 필드는 효과 칸 이름)", trait.InlineDescription);
        }

        if (registry != null)
        {
            foreach (string id in registry.ConflictSources)
            {
                if (registry.TryGetConflictText(id, out string ko))
                    Add(keys, TraitStringKeys.Conflict(id), $"{id} · 2차 상충 확인창({{traits}} = 효과를 잃는 보유 특성 이름)", ko);
            }
        }

        return keys;
    }

    private static void Add(List<StringKeyInfo> keys, string key, string description, string ko)
    {
        if (!string.IsNullOrEmpty(key) && !string.IsNullOrWhiteSpace(ko))
            keys.Add(new StringKeyInfo(key, description, ko));
    }
}
