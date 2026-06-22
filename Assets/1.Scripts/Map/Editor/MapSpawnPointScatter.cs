using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

// 씬의 각 ZoneVolume 안에 티어별 SpawnPoint를 흩뿌려 생성한다.
// 재실행 시 각 볼륨의 "SpawnPoints" 홀더를 지우고 다시 만든다(멱등).
//
// 배치 제약:
//  - 가장자리 여백: 벽/존 경계 밖으로 노드가 삐져나가지 않게 (티어별 마진 — 1티어는 대형이라 큼)
//  - 최소 간격: MapGenConfig의 티어별 배제반경 기반, 서로 너무 붙지 않게 (rejection sampling)
public static class MapSpawnPointScatter
{
    private const int Attempts = 40;            // 포인트당 샘플링 시도 횟수
    private const float MouthClearance = 8f;    // 통로 입구 앞 비워둘 반경 (길막 방지)

    private static List<Vector3> _mouths = new List<Vector3>();

    [MenuItem("VeyTrace/Map/Scatter Spawn Points")]
    public static void Scatter()
    {
        var volumes = Object.FindObjectsByType<ZoneVolume>(FindObjectsSortMode.None);
        if (volumes.Length == 0)
        {
            Debug.LogWarning("[Scatter] 씬에 ZoneVolume이 없습니다. 각 영역에 ZoneVolume을 배치하고 Zone을 지정하세요.");
            return;
        }

        // 배제반경 (Config 없으면 기본값)
        var config = Object.FindFirstObjectByType<MapGenerator>()?.Config;
        float r1 = config != null ? config.Tier1ExclusionRadius : 30f;
        float r2 = config != null ? config.Tier2ExclusionRadius : 10f;
        float r3 = config != null ? config.Tier3ExclusionRadius : 5f;

        // 통로 입구 좌표 — 입구 앞 MouthClearance 반경엔 노드/장애물 배치 금지 (길막 방지)
        _mouths = MapCorridors.GetMouthPoints();

        int pointId = 1;
        int total = 0;
        foreach (var vol in volumes)
        {
            if (vol.Zone == null)
            {
                Debug.LogWarning($"[Scatter] {vol.name}: Zone 미지정 — 건너뜀");
                continue;
            }

            Transform old = vol.transform.Find("SpawnPoints");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var holder = new GameObject("SpawnPoints");
            holder.transform.SetParent(vol.transform, false);

            // 이 존에서 이미 배치된 포인트 (간격 검사용)
            var placed = new List<(Vector3 pos, float radius)>();

            Bounds b = vol.GetBounds();
            float minDim = Mathf.Min(b.size.x, b.size.z);
            float margin1 = Mathf.Min(16f, minDim * 0.3f); // 1티어: 대형(x1300)이라 더 깊숙이
            float margin2 = 3f;
            float margin3 = 2.5f;

            pointId = ScatterTier(vol, holder.transform, NodeTier.Tier1_Large, vol.Tier1Count, r1, margin1, placed, pointId, ref total);
            pointId = ScatterTier(vol, holder.transform, NodeTier.Tier2_Medium, vol.Tier2Count, r2, margin2, placed, pointId, ref total);
            pointId = ScatterTier(vol, holder.transform, NodeTier.Tier3_Small, vol.Tier3Count, r3, margin3, placed, pointId, ref total);
        }

        if (volumes.Length > 0)
            EditorSceneManager.MarkSceneDirty(volumes[0].gameObject.scene);
        Debug.Log($"[Scatter] 완료 — ZoneVolume {volumes.Length}개, SpawnPoint {total}개 생성 (간격/여백 제약 적용). (씬 저장 필요)");
    }

    private static int ScatterTier(ZoneVolume vol, Transform holder, NodeTier tier, int count,
        float radius, float margin, List<(Vector3 pos, float radius)> placed, int pointId, ref int total)
    {
        Bounds b = vol.GetBounds();
        float minX = b.min.x + margin, maxX = b.max.x - margin;
        float minZ = b.min.z + margin, maxZ = b.max.z - margin;
        if (minX > maxX) minX = maxX = b.center.x; // 존이 마진보다 작으면 중앙 고정
        if (minZ > maxZ) minZ = maxZ = b.center.z;
        float y = vol.transform.position.y;

        for (int i = 0; i < count; i++)
        {
            // rejection sampling: 간격 만족하는 첫 후보, 실패 시 가장 멀리 떨어진 후보
            Vector3 best = new Vector3(b.center.x, y, b.center.z);
            float bestScore = float.MinValue;

            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                var p = new Vector3(Random.Range(minX, maxX), y, Random.Range(minZ, maxZ));

                float score = float.MaxValue;
                foreach (var q in placed)
                {
                    // 요구 간격 = 두 반경 평균 (같은 티어끼리 = 반경 그대로)
                    float required = (radius + q.radius) * 0.5f;
                    score = Mathf.Min(score, Vector3.Distance(p, q.pos) - required);
                }
                // 통로 입구 앞은 비워둠 (지나갈 수 없게 막는 배치 방지)
                foreach (var m in _mouths)
                    score = Mathf.Min(score, Vector2.Distance(new Vector2(p.x, p.z), new Vector2(m.x, m.z)) - MouthClearance);

                if (score >= 0f) { best = p; bestScore = score; break; }
                if (score > bestScore) { bestScore = score; best = p; }
            }

            var go = new GameObject($"SP_{tier}_{pointId}");
            go.transform.SetParent(holder, false);
            go.transform.position = best;

            var sp = go.AddComponent<SpawnPoint>();
            sp.PointID = pointId;
            sp.ParentZone = vol.Zone;
            sp.AllowedTier = tier;

            placed.Add((best, radius));
            pointId++;
            total++;
        }
        return pointId;
    }
}
