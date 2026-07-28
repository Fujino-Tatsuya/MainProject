using UnityEditor;
using UnityEngine;

// Wells가 던지는 폭탄의 비주얼을 아트 모델(bomb.fbx)로 교체하는 저작 도구.
//
// 기존 비주얼은 기본 Sphere 프리미티브였다. 판정(SphereCollider radius 0.25 월드)과 BombController
// 배선은 그대로 두고 **보이는 것만** 교체한다 — 콜라이더를 건드리면 폭발 판정 반경이 달라진다.
//
// ⚠️ 아트가 원점 기준으로 내보내지지 않았다(자식 메시가 (-40.96, 1.80, 10.16) 오프셋). 그대로 붙이면
// 폭탄이 40m 떨어진 곳에 그려진다 — 렌더러 바운즈를 실측해 재중심하고 목표 지름에 맞춰 스케일한다.
public static class BombVisualAuthoring
{
    const string BombPrefabPath = "Assets/2.Prefabs/Wells&No.23/Bomb.prefab";
    const string BombModelPath = "Assets/50.Art/Char/Boss/bomb.fbx";

    const string VisualName = "BombVisual";
    const string LegacyVisualName = "Sphere";

    // 기존 Sphere 비주얼의 월드 지름(자식 스케일 0.5 × 기본 구 지름 1). 판정 반경과 일치시킨다.
    const float TargetDiameter = 0.5f;

    [MenuItem("Tools/Map/Authoring/Swap Bomb Visual (bomb.fbx)")]
    public static void SwapBombVisual()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(BombModelPath);
        if (model == null)
        {
            Debug.LogError($"[BombVisual] 아트 모델을 찾지 못했다: {BombModelPath}");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(BombPrefabPath);

        try
        {
            // 재실행 안전 — 이전에 만든 비주얼은 지우고 다시 만든다.
            Transform stale = root.transform.Find(VisualName);
            if (stale != null)
                Object.DestroyImmediate(stale.gameObject);

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            visual.name = VisualName;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;

            if (!TryMeasure(visual, out Bounds worldBounds))
            {
                Debug.LogError("[BombVisual] 모델에 렌더러가 없어 크기를 실측할 수 없다.");
                Object.DestroyImmediate(visual);
                return;
            }

            // 1) 목표 지름에 맞춘 균일 스케일.
            float largestAxis = Mathf.Max(worldBounds.size.x, Mathf.Max(worldBounds.size.y, worldBounds.size.z));
            if (largestAxis <= Mathf.Epsilon)
            {
                Debug.LogError("[BombVisual] 모델 바운즈가 0이다.");
                Object.DestroyImmediate(visual);
                return;
            }

            float scale = TargetDiameter / largestAxis;
            visual.transform.localScale = Vector3.one * scale;

            // 2) 스케일 적용 후 다시 실측해 중심을 원점으로 끌어온다(월드 → 루트 로컬 변환 필수).
            TryMeasure(visual, out worldBounds);
            Vector3 centerLocal = root.transform.InverseTransformPoint(worldBounds.center);
            visual.transform.localPosition = -centerLocal;

            // 3) 기존 프리미티브 비주얼은 렌더러만 끈다 — SphereCollider(판정)는 살려 둔다.
            Transform legacy = root.transform.Find(LegacyVisualName);
            if (legacy != null)
            {
                var legacyRenderer = legacy.GetComponent<MeshRenderer>();
                if (legacyRenderer != null)
                    legacyRenderer.enabled = false;
            }
            else
            {
                Debug.LogWarning($"[BombVisual] '{LegacyVisualName}' 자식을 찾지 못했다 — 판정 콜라이더 위치를 확인할 것.");
            }

            PrefabUtility.SaveAsPrefabAsset(root, BombPrefabPath);

            Debug.Log(
                $"[BombVisual] 교체 완료 — 모델 {model.name}, 스케일 {scale:F4} " +
                $"(원본 최대축 {largestAxis:F2}m → 목표 지름 {TargetDiameter}m), " +
                $"중심 보정 {-centerLocal}. 기존 Sphere 렌더러는 비활성(콜라이더 유지).");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static bool TryMeasure(GameObject target, out Bounds bounds)
    {
        bounds = default;
        bool hasAny = false;

        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
        {
            if (!hasAny)
            {
                bounds = renderer.bounds;
                hasAny = true;
                continue;
            }

            bounds.Encapsulate(renderer.bounds);
        }

        return hasAny;
    }
}
