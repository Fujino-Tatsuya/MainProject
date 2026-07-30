using UnityEditor;
using UnityEngine;

// 아트 모델의 "구워진 오프셋"을 실측하는 도구.
//
// 배경: 이 프로젝트의 아트 fbx는 정점이 원점이 아닌 곳에 구워져 있다(맵 오브젝트도 같은 패턴).
// 그래서 인스턴스 트랜스폼에 큰 값을 넣어 상쇄하는데, **피벗을 원점에 맞추는 것과 보이는 지오메트리를
// 원점에 맞추는 것은 다르다.** Bomb.prefab이 그 함정에 걸려 있었다 — 피벗은 폭탄 원점에 4mm 내로
// 맞았지만 렌더되는 메시는 Z로 3.5m 떨어져 보였다.
//
// 눈대중으로 값을 넣지 말고 이 도구로 렌더러 바운즈 중심을 재서 그만큼 되돌린다.
public static class VisualOffsetMeasure
{
    const string BombPrefabPath = "Assets/2.Prefabs/Wells&No.23/Bomb.prefab";
    const string VisualChildName = "BombVisual";

    [MenuItem("Tools/Map/Authoring/Measure Bomb Visual Offset")]
    public static void MeasureBombVisual()
    {
        Measure(BombPrefabPath, VisualChildName);
    }

    static void Measure(string prefabPath, string visualChildName)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);

        try
        {
            Transform visual = root.transform.Find(visualChildName);
            if (visual == null)
            {
                Debug.LogError($"[VisualOffsetMeasure] {prefabPath}에서 '{visualChildName}'을 찾지 못했습니다.");
                return;
            }

            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Debug.LogError($"[VisualOffsetMeasure] '{visualChildName}' 하위에 Renderer가 없습니다.");
                return;
            }

            // renderer.bounds는 월드 좌표다. LoadPrefabContents는 프리팹을 원점이 아닌 프리뷰 씬에
            // 올리므로, 루트 로컬로 환산해야 의미가 있다(BossRoomAuthoring에서 한 번 밟은 함정).
            Bounds local = ToRootLocalBounds(root.transform, renderers[0].bounds);
            for (int i = 1; i < renderers.Length; i++)
                local.Encapsulate(ToRootLocalBounds(root.transform, renderers[i].bounds));

            Debug.Log(
                $"[VisualOffsetMeasure] {visualChildName} 렌더 바운즈(루트 로컬) — " +
                $"center {local.center} / size {local.size}\n" +
                $"  → 지오메트리를 원점에 맞추려면 {visualChildName}.localPosition 에서 center를 빼면 된다: " +
                $"{visual.localPosition} - {local.center} = {visual.localPosition - local.center}\n" +
                $"  (바닥에 닿는 물건이면 보통 center.y 대신 bounds.min.y 기준으로 올린다)");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static Bounds ToRootLocalBounds(Transform root, Bounds world)
    {
        Vector3 center = world.center;
        Vector3 extents = world.extents;

        Bounds result = new Bounds(root.InverseTransformPoint(center), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            Vector3 offset = new Vector3(
                (i & 1) == 0 ? -extents.x : extents.x,
                (i & 2) == 0 ? -extents.y : extents.y,
                (i & 4) == 0 ? -extents.z : extents.z);
            result.Encapsulate(root.InverseTransformPoint(center + offset));
        }

        return result;
    }
}
