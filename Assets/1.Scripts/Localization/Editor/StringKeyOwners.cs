using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Strings 자동 키(<c>주인ID/필드경로</c>)의 주인 ID → 실제 주인(SO·프리팹 컴포넌트). <see cref="StringTableValidator"/> 가
/// 툴팁 문구의 <c>{필드}</c> 를 그 주인의 필드로 검사할 때 쓴다(테이블 Play·빌드·인스펙터 Play·StringTable 갱신 메뉴).
/// <list type="bullet">
/// <item>먼저 <paramref name="known"/>(이번 Load 에서 수치 시트가 찾은 대상) — 추가 탐색 없음.</item>
/// <item>SO — <c>[DataTableSheet]</c> SO 타입 중 그 이름의 에셋(<see cref="AssetDatabaseLookup"/>).</item>
/// <item>컴포넌트 — 주인 ID = 타입 이름(<see cref="LocKey.OwnerId"/>). 그 스크립트에 직접 의존하는 프리팹(레거시·아트 폴더 제외, 경로 순) 중 첫 것.</item>
/// </list>
/// 한 번 만든 함수 안에서만 캐시한다(호출마다 새로 만든다).
/// </summary>
public static class StringKeyOwners
{
    public static Func<string, object> Resolver(IEnumerable<Object> known)
    {
        var byId = new Dictionary<string, Object>(StringComparer.Ordinal);
        foreach (Object target in known ?? Enumerable.Empty<Object>())
        {
            string id = LocKey.OwnerId(target);
            if (id != null && !byId.ContainsKey(id))
            {
                byId[id] = target;
            }
        }

        Type[] sheetTypes = TypeCache.GetTypesWithAttribute<DataTableSheetAttribute>().Where(t => !t.IsAbstract).ToArray();
        var lookup = new AssetDatabaseLookup();
        string[] prefabs = null;

        return id =>
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            if (byId.TryGetValue(id, out Object cached))
            {
                return cached;
            }

            Object found = null;
            Type component = sheetTypes.FirstOrDefault(t => typeof(MonoBehaviour).IsAssignableFrom(t) && t.Name == id);
            if (component != null)
            {
                prefabs ??= AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => !DataTableTemplate.IsExcludedPath(p))
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .ToArray();
                found = FindInPrefabs(component, prefabs);
            }
            else
            {
                foreach (Type type in sheetTypes.Where(t => typeof(ScriptableObject).IsAssignableFrom(t)))
                {
                    found = lookup.FindTarget(type, id, out _);
                    if (found != null)
                    {
                        break;
                    }
                }
            }

            byId[id] = found;
            return found;
        };
    }

    private static Object FindInPrefabs(Type component, string[] prefabs)
    {
        MonoScript script = MonoImporter.GetAllRuntimeMonoScripts().FirstOrDefault(s => s != null && s.GetClass() == component);
        string scriptPath = script == null ? null : AssetDatabase.GetAssetPath(script);
        if (string.IsNullOrEmpty(scriptPath))
        {
            return null;
        }

        foreach (string path in prefabs)
        {
            if (!AssetDatabase.GetDependencies(path, false).Contains(scriptPath))
            {
                continue;
            }

            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Component found = root == null ? null : root.GetComponentInChildren(component, true);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
