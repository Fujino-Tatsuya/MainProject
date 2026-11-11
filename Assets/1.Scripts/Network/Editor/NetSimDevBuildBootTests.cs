using NUnit.Framework;
using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 빌드 지연 테스트(할 일 258) — NetworkManager.prefab 의 Network Simulator 는 꺼진 채(에디터 기본 동작 불변)
/// 지연 25ms 만 든 사용자 정의 프리셋이고, 부트(<see cref="NetSimDevBuildBoot"/>)가 그것을 가리킨다.
/// </summary>
public sealed class NetSimDevBuildBootTests
{
    private const string NetworkManagerPrefabPath = "Assets/2.Prefabs/Network/NetworkManager.prefab";

    [Test]
    public void 프리팹_시뮬레이터는_꺼진채_지연25ms만()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPrefabPath);
        Assert.IsNotNull(prefab);

        var sim = prefab.GetComponent<NetworkSimulator>();
        Assert.IsNotNull(sim, "Network Simulator 컴포넌트가 없다");
        Assert.IsFalse(sim.enabled, "프리팹에서는 꺼져 있어야 한다(에디터·릴리스 기본 동작 불변)");
        Assert.IsNull(sim.Scenario, "시나리오는 비어 있어야 한다");

        var preset = sim.ConnectionPreset;
        Assert.IsNotNull(preset);
        Assert.AreEqual(25, preset.PacketDelayMs);
        Assert.AreEqual(0, preset.PacketJitterMs);
        Assert.AreEqual(0, preset.PacketLossInterval);
        Assert.AreEqual(0, preset.PacketLossPercent);
    }

    [Test]
    public void 부트가_같은_오브젝트의_시뮬레이터를_가리킨다()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPrefabPath);
        var boot = prefab.GetComponent<NetSimDevBuildBoot>();
        Assert.IsNotNull(boot, "NetSimDevBuildBoot 가 없다");

        var so = new SerializedObject(boot);
        Assert.AreSame(prefab.GetComponent<NetworkSimulator>(), so.FindProperty("simulator").objectReferenceValue);
    }
}
