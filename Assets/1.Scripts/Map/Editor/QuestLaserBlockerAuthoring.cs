using UnityEditor;
using UnityEngine;

// 레이저 배리어(layprefab)에 심어 둔 물리 차단벽을 **걷어내는** 도구.
//
// 원래는 벽을 심는 Setup 도 함께 있었으나 2026-08-18 에 삭제했다 — 그 방식이 폐기됐기 때문이다:
// layprefab 은 Stage1 통로 26곳에 들어 있어 프리팹에 벽을 넣으면 **보스 진입로까지 봉쇄**되고,
// 어느 슬롯이 Quest 가 되는지는 시드마다 달라 정적 배치로 맞출 수 없다.
//
// 🔴 대체 구현은 아직 없다. 구 주석은 "MapContentSpawner 가 역할 확정 시점에 해당 존만 감싼다
// (AttachQuestBlockade)" 고 적었지만 그 이름은 코드에 존재하지 않는다(2026-08-18 전수 확인).
// 즉 지금 Quest 통로 차단은 **미구현**이다.
//
// 이 도구를 남긴 이유: layprefab 에 아직 LaserBlockWall 이 남아 있다. 걷어낼 때 쓴다.
// 걷어내고 나면 이 파일도 지워도 된다.
public static class QuestLaserBlockerAuthoring
{
    const string LaserPrefabPath = "Assets/2.Prefabs/Map/Props/layprefab.prefab";
    const string BlockerName = "LaserBlockWall";

    /// <summary>레이저 프리팹에 심은 차단벽을 제거한다. 멱등 — 없으면 아무것도 하지 않는다.</summary>
    [MenuItem("Tools/Map/Authoring/Remove Quest Laser Blockers")]
    public static void RemoveLaserBlockers()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(LaserPrefabPath);

        try
        {
            Transform existing = root.transform.Find(BlockerName);
            if (existing == null)
            {
                Debug.Log($"[QuestLaserBlocker] {BlockerName}이 없다 — 제거할 것 없음.");
                return;
            }

            Object.DestroyImmediate(existing.gameObject);
            PrefabUtility.SaveAsPrefabAsset(root, LaserPrefabPath);
            Debug.Log($"[QuestLaserBlocker] {BlockerName} 제거 후 저장 — 레이저 통로 통행이 복구된다.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

}
