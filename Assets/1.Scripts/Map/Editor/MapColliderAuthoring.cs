using UnityEditor;
using UnityEngine;

// 일회성 저작 도구 — 맵 프리팹의 바닥/벽/경사로 메시에 MeshCollider 부착 (PLAN 2026-07-21 §1).
//
// 배경: 기존 맵 프리팹(Stage1/WallPrefabs/Zoneprefab)의 바닥·벽은 fbx 모델 인스턴스가 아니라
// "언팩된 사본"(MeshFilter+MeshRenderer만)이라, fbx 임포터 addColliders를 켜도 전파되지 않는다.
// 그래서 프리팹을 직접 순회하며 이름이 floor/wall/hallway 계열인 메시에 MeshCollider를 붙인다.
// (소품/장식은 이름 필터로 제외 — 통행 방해 방지. fbx addColliders는 신규 배치용으로 별도 유지.)
//
// 2026-07-28: slope/stairs 추가. Play 검증에서 경사로를 밟으면 아래로 떨어지는 것이 확인됐다.
// 경사로·계단도 바닥과 같은 "밟고 지나가는" 지오메트리인데 이름 필터에서 빠져 있었다.
// 대상 17개(slope 12 · stairs 5) — ZoneL_typeA/B · ZoneM_typeA/B · Zone_typeQuest01/02.
public static class MapColliderAuthoring
{
    const string TargetFolder = "Assets/2.Prefabs/Map";
    static readonly string[] NameKeywords = { "floor", "wall", "hallway", "slope", "stair" };

    [MenuItem("Tools/Map/Authoring/Add Floor+Wall MeshColliders")]
    public static void AddFloorWallColliders()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { TargetFolder });
        int prefabsChanged = 0, collidersAdded = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            int added = 0;

            try
            {
                foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    // 중첩 프리팹 인스턴스 내부는 건너뜀 — 원본 프리팹이 이 스캔에 포함되므로
                    // 원본에서 1회만 부착(인스턴스 오버라이드 오염 방지).
                    if (PrefabUtility.IsPartOfPrefabInstance(mf.gameObject))
                        continue;

                    if (!IsFloorOrWall(mf))
                        continue;

                    bool isStair = IsStair(mf);
                    Collider existing = mf.GetComponent<Collider>();

                    if (existing != null)
                    {
                        // 기존 콜라이더는 유지(수동 저작 존중)하되, 계단만 예외로 볼록 승격한다.
                        if (isStair && existing is MeshCollider existingMesh && !existingMesh.convex)
                        {
                            existingMesh.convex = true;
                            added++;
                            Debug.Log($"[MapColliderAuthoring] 계단 볼록 승격: {mf.gameObject.name}");
                        }
                        continue;
                    }

                    var collider = mf.gameObject.AddComponent<MeshCollider>(); // sharedMesh는 MeshFilter에서 자동 참조

                    // 계단을 실제 메시(턱 있는 형상)로 두면 Rigidbody 캡슐이 턱에 막혀 못 올라간다.
                    // 플레이어 이동은 MovePosition 기반이라 CharacterController의 stepOffset 같은
                    // 계단 오르기 보정이 없다. 볼록 껍질을 씌우면 계단 위를 잇는 램프가 되어
                    // 걸어 올라갈 수 있고 NavMesh 베이크에도 유리하다.
                    if (isStair)
                        collider.convex = true;

                    added++;
                }

                if (added > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    prefabsChanged++;
                    collidersAdded += added;
                    Debug.Log($"[MapColliderAuthoring] {path} — MeshCollider {added}개 부착.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        Debug.Log($"[MapColliderAuthoring] 완료 — 프리팹 {prefabsChanged}개 수정, 콜라이더 총 {collidersAdded}개 부착.");
    }

    // 오브젝트명 · 메시명 · 머티리얼명 중 하나라도 키워드를 포함하면 대상.
    static bool IsFloorOrWall(MeshFilter mf)
    {
        if (MatchesKeyword(mf.gameObject.name))
            return true;
        if (mf.sharedMesh != null && MatchesKeyword(mf.sharedMesh.name))
            return true;

        // 아트가 바닥 메시를 Cube.209 처럼 무의미한 이름으로 내보내는 경우가 있어
        // 이름만으로는 바닥과 소품을 가르지 못한다. 머티리얼로 한 번 더 판정한다.
        // (MA_floor_urethane → 바닥으로 잡히고, MA_prop01 환풍구류는 그대로 제외된다.)
        MeshRenderer renderer = mf.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            foreach (Material mat in renderer.sharedMaterials)
                if (mat != null && MatchesKeyword(mat.name))
                    return true;
        }

        return false;
    }

    // 계단 판정 — 볼록 승격 대상. 경사로(slope)는 이미 램프라 원본 메시 그대로 둔다.
    static bool IsStair(MeshFilter mf)
    {
        if (mf.gameObject.name.ToLowerInvariant().Contains("stair"))
            return true;
        return mf.sharedMesh != null && mf.sharedMesh.name.ToLowerInvariant().Contains("stair");
    }

    static bool MatchesKeyword(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        string lower = name.ToLowerInvariant();
        foreach (string k in NameKeywords)
            if (lower.Contains(k))
                return true;
        return false;
    }
}
