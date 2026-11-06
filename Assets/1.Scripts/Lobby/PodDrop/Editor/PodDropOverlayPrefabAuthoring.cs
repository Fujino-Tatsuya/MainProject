using UnityEditor;
using UnityEngine;

/// <summary>
/// 포드 출격·착륙 레터박스 오버레이 프리팹 저작(할 일 230).
/// <c>Tools/Lobby/포드 레터박스 프리팹 만들기</c> — 프리팹이 <b>없을 때만</b> 코드 기본 구성(<see cref="PodDropScreenOverlay.CreateDefault"/>)으로 만들고,
/// 띠를 <see cref="PodDropSettings.LetterboxHeight"/> 높이로 저장한다(프리팹 편집 화면에서 연출 때와 같은 높이로 보이게).
/// 이미 있으면 덮어쓰지 않는다. 어느 경우든 <c>PodDropSettings.overlayPrefab</c> 칸이 비어 있으면 채운다.
/// </summary>
public static class PodDropOverlayPrefabAuthoring
{
    const string Tag = "[PodDrop/Overlay]";
    public const string PrefabFolder = "Assets/2.Prefabs/UI/PodDrop";
    public const string PrefabPath = PrefabFolder + "/PodDropOverlay.prefab";
    const string SettingsPath = "Assets/9.ScriptableObject/Lobby/PodDropSettings.asset";

    [MenuItem("Tools/Lobby/포드 레터박스 프리팹 만들기 (없을 때만)")]
    public static void CreateIfMissing()
    {
        var settings = AssetDatabase.LoadAssetAtPath<PodDropSettings>(SettingsPath);
        if (settings == null)
        {
            Debug.LogError($"{Tag} {SettingsPath} 를 못 찾았다.");
            return;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder("Assets/2.Prefabs/UI", "PodDrop");
            GameObject root = PodDropScreenOverlay.CreateDefault(null, settings.LetterboxHeight);
            try
            {
                prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            Debug.Log($"{Tag} {PrefabPath} 생성 — 띠 높이 {settings.LetterboxHeight:0.###}(PodDropSettings.letterboxHeight).", prefab);
        }
        else
        {
            Debug.Log($"{Tag} {PrefabPath} 이미 있음 — 덮어쓰지 않는다.", prefab);
        }

        var so = new SerializedObject(settings);
        SerializedProperty slot = so.FindProperty("overlayPrefab");
        if (slot.objectReferenceValue == null)
        {
            slot.objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} PodDropSettings.overlayPrefab ← {PrefabPath}", settings);
        }
        else if (slot.objectReferenceValue != prefab)
        {
            Debug.Log($"{Tag} PodDropSettings.overlayPrefab 에 이미 다른 프리팹({AssetDatabase.GetAssetPath(slot.objectReferenceValue)})이 있다 — 그대로 둔다.", settings);
        }

        EditorGUIUtility.PingObject(prefab);
    }
}
