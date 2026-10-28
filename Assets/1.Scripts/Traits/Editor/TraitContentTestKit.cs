using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 특성 콘텐츠 테스트 공용 도구(할 일 125 5/5) — 메모리 정의 만들기 · 프로젝트 정의/풀 읽기 · 캐릭터별 공통 검사.
/// 캐릭터 레인 테스트(<c>Traits/Editor/&lt;캐릭터&gt;/</c>)도 이걸 쓴다 — 고치지 말고 필요하면 자기 파일에 도우미를 둔다.
/// </summary>
internal static class TraitContentTestKit
{
    public const string PoolPath = "Assets/9.ScriptableObject/Traits/TraitPool.asset";

    /// <summary>필드를 채운 메모리 정의(<typeparamref name="T"/> = 기반 또는 캐릭터 하위 타입). 다 쓰면 DestroyImmediate.</summary>
    public static T Make<T>(string id, string displayName, string category, string description, params TraitEffect[] effects)
        where T : TraitDefinition
    {
        var def = ScriptableObject.CreateInstance<T>();
        def.name = id;
        var so = new SerializedObject(def);
        so.FindProperty("id").stringValue = id;
        so.FindProperty("displayName").stringValue = displayName;
        so.FindProperty("category").stringValue = category;
        so.FindProperty("description").stringValue = description;
        SerializedProperty list = so.FindProperty("effects");
        list.arraySize = effects.Length;
        for (int i = 0; i < effects.Length; i++)
            list.GetArrayElementAtIndex(i).managedReferenceValue = effects[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        return def;
    }

    /// <summary>프로젝트의 특성 정의 전부(레거시 폴더 제외).</summary>
    public static List<TraitDefinition> ProjectDefinitions() =>
        AssetDatabase.FindAssets("t:" + nameof(TraitDefinition), new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => !DataTableTemplate.IsExcludedPath(path))
            .Select(AssetDatabase.LoadAssetAtPath<TraitDefinition>)
            .Where(def => def != null)
            .ToList();

    public static TraitPool ProjectPool() => AssetDatabase.LoadAssetAtPath<TraitPool>(PoolPath);

    public static TraitCharacterPool CharacterPool(TutorialCharacter character) =>
        ProjectPool()?.CharacterPools.FirstOrDefault(p => p != null && p.Character == character);

    /// <summary>
    /// 캐릭터 하나의 공통 검사 — 등록부 오류 없음 · 캐릭터 풀 연결 · 풀 검증 · 풀 정의의 카드 문구 검증(토큰·{dmg} 계산식).
    /// 캐릭터 레인 테스트 파일이 한 줄로 부른다.
    /// </summary>
    public static void AssertCharacterContent(TutorialCharacter character)
    {
        TraitContentRegistry registry = TraitContent.Build();
        List<string> own = registry.Errors.Where(e => e.StartsWith(character.ToString())).ToList();
        Assert.IsEmpty(own, string.Join("\n", own));

        TraitCharacterPool pool = CharacterPool(character);
        Assert.IsNotNull(pool, $"TraitPool.asset 에 {character} 캐릭터 풀이 연결돼 있어야 한다");

        var errors = new List<string>();
        pool.Validate(errors);
        foreach (TraitDefinition def in pool.Traits)
            TraitTooltip.ValidateDefinition(def, registry, errors);
        Assert.IsEmpty(errors, string.Join("\n", errors));
    }
}
