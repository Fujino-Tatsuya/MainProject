#if UNITY_EDITOR
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 다리 개통 장치의 상태 소유자를 씬에 배치한다.
//
// 왜 씬에 있어야 하는가: 존 프리팹은 비네트워크라 패널·다리가 상태를 복제할 수 없다. 상태를
// 들고 있을 NetworkObject가 씬에 상주해야 하고, NGO는 씬 상주 NetworkObject를 씬 로드 시점에
// 인식하므로 런타임 생성으로는 대체할 수 없다.
//
// 이 도구는 **현재 열려 있는 씬**에 배치한다 — 씬을 강제로 바꾸면 저장 안 된 작업이 걸린다.
public static class ZoneBridgeGateManagerWiring
{
    const string ObjectName = "ZoneBridgeGateManager";
    const string ExpectedScene = "4.MapScene";

    [MenuItem("Tools/Map/Authoring/Wire Zone Bridge Gate Manager (active scene)")]
    public static void WireManager()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[BridgeGate] 열린 씬이 없습니다.");
            return;
        }

        if (scene.name != ExpectedScene)
        {
            Debug.LogWarning(
                $"[BridgeGate] 활성 씬이 '{scene.name}'입니다(기대 '{ExpectedScene}'). " +
                "의도한 씬이 아니면 취소하고 MapScene을 여세요 — 그대로 진행합니다.");
        }

        ZoneBridgeGateManager existing = Object.FindFirstObjectByType<ZoneBridgeGateManager>();
        if (existing != null)
        {
            Debug.Log($"[BridgeGate] 이미 '{existing.gameObject.name}'에 배치돼 있습니다 — 그대로 둡니다.");
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        var go = new GameObject(ObjectName);
        Undo.RegisterCreatedObjectUndo(go, "Wire Zone Bridge Gate Manager");

        // NetworkObject가 먼저 있어야 NetworkBehaviour가 정상 초기화된다.
        var netObj = go.AddComponent<NetworkObject>();
        netObj.AlwaysReplicateAsRoot = false;
        go.AddComponent<ZoneBridgeGateManager>();

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = go;

        Debug.Log(
            $"[BridgeGate] '{ObjectName}' 배치 완료 (씬 '{scene.name}') — NetworkObject + ZoneBridgeGateManager.\n" +
            "**씬 저장 필요**(Ctrl+S). 저장하지 않으면 Play에서 F 상호작용이 동작하지 않는다.");
    }
}
#endif
