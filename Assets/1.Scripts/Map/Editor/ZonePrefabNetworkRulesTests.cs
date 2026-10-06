using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 존 프리팹 규약 — **NetworkObject 금지**(MapContentSpawner: 존은 각 피어가 로컬 Instantiate, 네트워크 Spawn 안 함).
///
/// 🔴 왜 테스트로 잠그나: NGO 에디터가 NetworkBehaviour/NetworkObject 다이얼로그에서 기본 "Yes" 로 NetworkObject 를 다시 붙인다.
///    2026-09-09 에 한 번 지웠는데 09-18 에 7종에 다시 붙어 있었고(DefaultNetworkPrefabs 등록까지), 10-05 에 다시 지웠다.
///    붙어 있어도 아무도 Spawn 하지 않아 "동작은 같아 보여서" 눈치채기 어렵다 — 그래서 테스트가 잡는다.
/// bossroom 은 제외 — 내부(중첩 프리팹 오브젝트)에 NetworkObject 4개가 있고 용도 확인 전이다(CONTEXT 인수인계).
/// </summary>
public sealed class ZonePrefabNetworkRulesTests
{
    const string ZonesFolder = "Assets/2.Prefabs/Environment/Layouts/Zones";
    static readonly string[] Excluded = { "bossroom" };

    [Test]
    public void 존_프리팹에는_NetworkObject_가_없다()
    {
        string[] paths = AssetDatabase.FindAssets("t:Prefab", new[] { ZonesFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !Excluded.Contains(System.IO.Path.GetFileNameWithoutExtension(p)))
            .ToArray();
        Assert.That(paths, Is.Not.Empty, "존 프리팹을 못 찾았다 — 폴더 경로 확인");

        var offenders = paths
            .Where(p => AssetDatabase.LoadAssetAtPath<GameObject>(p).GetComponentsInChildren<NetworkObject>(true).Length > 0)
            .ToArray();

        Assert.That(offenders, Is.Empty,
            "존 프리팹에 NetworkObject 가 붙어 있다(존 규약 위반 — 로컬 Instantiate 라 아무도 Spawn 하지 않는다). " +
            "컴포넌트를 지우고 DefaultNetworkPrefabs 등록도 지울 것: " + string.Join(", ", offenders));
    }

    [Test]
    public void DefaultNetworkPrefabs_에_존_프리팹이_등록되지_않았다()
    {
        string[] zoneGuids = AssetDatabase.FindAssets("t:Prefab", new[] { ZonesFolder })
            .Where(g => !Excluded.Contains(System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g))))
            .ToArray();
        string text = System.IO.File.ReadAllText("Assets/DefaultNetworkPrefabs.asset");
        var registered = zoneGuids.Where(g => text.Contains(g)).Select(AssetDatabase.GUIDToAssetPath).ToArray();
        Assert.That(registered, Is.Empty, "DefaultNetworkPrefabs 에 존 프리팹이 등록돼 있다: " + string.Join(", ", registered));
    }
}
