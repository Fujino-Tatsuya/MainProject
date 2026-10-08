using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// base Player.prefab 의 SkillRangeIndicator 에 지점 지정 마커 데칼(groundMarkerDecal)을 멱등 저작한다.
/// 사거리 원 데칼과 같은 머티리얼·투영 설정을 쓰고, 크기·위치는 런타임(SkillRangeIndicator.SetGroundMarker)이 정한다.
/// </summary>
public static class SkillGroundMarkerAuthoring
{
    private const string PlayerPrefabPath = "Assets/2.Prefabs/Player/Player.prefab";
    private const string MarkerName = "GroundMarker";

    [MenuItem("Tools/Player/Authoring/Wire Skill Ground Marker")]
    public static void Wire()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        if (root == null)
        {
            Debug.LogError($"[SkillGroundMarkerAuthoring] 프리팹을 열 수 없습니다: {PlayerPrefabPath}");
            return;
        }

        try
        {
            SkillRangeIndicator indicator = root.GetComponentInChildren<SkillRangeIndicator>(true);
            if (indicator == null)
            {
                Debug.LogError("[SkillGroundMarkerAuthoring] Player.prefab 에 SkillRangeIndicator 가 없습니다.");
                return;
            }

            var serialized = new SerializedObject(indicator);
            var rangeDecal = serialized.FindProperty("rangeDecal").objectReferenceValue as DecalProjector;
            if (rangeDecal == null)
            {
                Debug.LogError("[SkillGroundMarkerAuthoring] SkillRangeIndicator.rangeDecal 이 비어 있습니다.");
                return;
            }

            Transform marker = indicator.transform.Find(MarkerName);
            if (marker == null)
            {
                marker = new GameObject(MarkerName).transform;
                marker.SetParent(indicator.transform, false);
            }

            // 부모(SkillRangeIndicator)가 이미 아래를 향해 있다 — 자식은 회전 없이 투영 방향을 물려받는다.
            marker.localPosition = Vector3.zero;
            marker.localRotation = Quaternion.identity;
            marker.localScale = Vector3.one;

            DecalProjector markerDecal = marker.GetComponent<DecalProjector>();
            if (markerDecal == null)
                markerDecal = marker.gameObject.AddComponent<DecalProjector>();

            markerDecal.material = rangeDecal.material;
            markerDecal.drawDistance = rangeDecal.drawDistance;
            markerDecal.fadeScale = rangeDecal.fadeScale;
            markerDecal.startAngleFade = rangeDecal.startAngleFade;
            markerDecal.endAngleFade = rangeDecal.endAngleFade;
            markerDecal.uvScale = rangeDecal.uvScale;
            markerDecal.uvBias = rangeDecal.uvBias;
            markerDecal.renderingLayerMask = rangeDecal.renderingLayerMask;
            markerDecal.scaleMode = rangeDecal.scaleMode;
            markerDecal.pivot = Vector3.zero;
            markerDecal.size = new Vector3(1f, 1f, rangeDecal.size.z);
            markerDecal.fadeFactor = rangeDecal.fadeFactor;
            markerDecal.enabled = false;

            serialized.FindProperty("groundMarkerDecal").objectReferenceValue = markerDecal;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[SkillGroundMarkerAuthoring] 완료 — Player.prefab/SkillRangeIndicator/GroundMarker 배선 " +
                $"(머티리얼 {(rangeDecal.material != null ? rangeDecal.material.name : "없음")}).");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
