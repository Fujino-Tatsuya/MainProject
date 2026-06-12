using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

// 생성 결과(GeneratedNodeData가 채워진 SpawnPoint)를 실제 프리팹 인스턴스로 구현한다.
//
// 네트워크 분기:
//  - NetworkObject가 있는 프리팹  → 서버에서만 Instantiate + Spawn() (NGO가 클라에 자동 복제).
//  - NetworkObject가 없는 프리팹  → 서버/클라 각자 로컬 생성 (MapNetworkSync가 같은 시드를
//    방송하므로 결과가 동일함). 현재 카탈로그 프리팹은 전부 비네트워크(시각물)다.
public class MapContentSpawner : MonoBehaviour
{
    public const string RootName = "GeneratedMap";

    private Transform _root;
    private readonly List<NetworkObject> _spawnedNetObjs = new List<NetworkObject>();

    public void SpawnGenerated(MapGenerator gen)
    {
        ClearGenerated();
        _root = new GameObject(RootName).transform;

        MapPrefabCatalogSO catalog = gen.Catalog;
        if (catalog == null)
        {
            Debug.LogWarning("[MapContentSpawner] Catalog 미연결 — 스폰 생략.");
            return;
        }

        var nm = NetworkManager.Singleton;
        bool isServer = nm != null && nm.IsServer;
        bool isClientOnly = nm != null && nm.IsClient && !nm.IsServer;

        int local = 0, networked = 0;

        // 1) 노드/장애물 — 할당된 SpawnPoint 위치에 카탈로그 프리팹 생성
        foreach (var sp in FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None))
        {
            if (sp == null || !sp.IsAssigned) continue;

            GameObject prefab = catalog.GetPrefab(sp.NodeData.Tier, sp.NodeData.Content, sp.NodeData.PrefabId);
            if (prefab == null) continue;

            bool hasNetObj = prefab.GetComponent<NetworkObject>() != null;
            if (hasNetObj && isClientOnly) continue; // 서버가 Spawn() → 복제로 수신

            // NetworkObject는 비네트워크 부모 아래 두면 안 됨 → 루트 없이 생성
            bool isTier1 = sp.NodeData.Tier == NodeTier.Tier1_Large;
            Quaternion rot = isTier1 ? Quaternion.Euler(catalog.Tier1Rotation) : Quaternion.identity;
            Vector3 spawnPos = sp.transform.position;
            if (isTier1)
                spawnPos.y = FloorTopAt(spawnPos, spawnPos.y); // 1티어만 바닥 윗면 스냅 (FBX 피벗 보정 필요)

            GameObject go = Instantiate(prefab, spawnPos, rot, hasNetObj ? null : _root);

            if (isTier1)
            {
                go.transform.localScale = Vector3.one * catalog.Tier1Scale;
                LiftToFloor(go, spawnPos.y); // 1티어만 바운즈 바닥 안착 — 나머지는 스폰포인트 위치 그대로
            }
            else if (sp.NodeData.Tier == NodeTier.Tier3_Small)
                go.transform.localScale *= catalog.Tier3Scale; // 소형이 잘 안 보여서 배율 확대

            if (hasNetObj && isServer)
            {
                var netObj = go.GetComponent<NetworkObject>();
                netObj.Spawn();
                _spawnedNetObjs.Add(netObj);
                networked++;
            }
            else local++;
        }

        // 2) 플레이어 스폰 영역 구조물 (node_spownpoint, x100) — 뽑힌 스폰 존 중앙에 배치
        if (catalog.SpawnAreaStructure != null && TryGetRoleZoneCenter(gen, ZoneRole.PlayerSpawn, out var structPos))
        {
            GameObject go = Instantiate(catalog.SpawnAreaStructure, structPos,
                Quaternion.Euler(catalog.SpawnStructureRotation), _root);
            go.transform.localScale = Vector3.one * catalog.SpawnStructureScale;
            local++;
        }

        // 3) 역할 영역 마커 (Quad) — 보스/스폰/퀘스트 존 중앙 바닥에 표시
        local += SpawnRoleMarker(gen, ZoneRole.BossRoom, catalog.BossAreaMarker, catalog.AreaMarkerSize);
        local += SpawnRoleMarker(gen, ZoneRole.PlayerSpawn, catalog.SpawnAreaMarker, catalog.AreaMarkerSize);
        local += SpawnRoleMarker(gen, ZoneRole.Quest, catalog.QuestAreaMarker, catalog.AreaMarkerSize);

        Debug.Log($"[MapContentSpawner] 스폰 완료 — 로컬 {local} / 네트워크 {networked}.");
    }

    // 해당 위치의 바닥 타일 윗면 높이 (타일 두께 보정 — 레이캐스트, 실패 시 fallback)
    private static float FloorTopAt(Vector3 pos, float fallbackY)
    {
        if (Physics.Raycast(new Vector3(pos.x, pos.y + 80f, pos.z), Vector3.down, out var hit, 200f))
            return hit.point.y;
        return fallbackY;
    }

    // 렌더러 바운즈 바닥이 floorY에 오도록 들어올림 (FBX 피벗이 모델 중앙이라 바닥에 꺼지는 문제 보정)
    private static void LiftToFloor(GameObject go, float floorY)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;

        Bounds b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);

        float lift = floorY - b.min.y;
        if (Mathf.Abs(lift) > 0.001f)
            go.transform.position += Vector3.up * lift;
    }

    // 해당 역할로 뽑힌 존 중앙 바닥에 마커(Quad) 배치. 생성 수 반환.
    private int SpawnRoleMarker(MapGenerator gen, ZoneRole role, GameObject markerPrefab, float size)
    {
        if (markerPrefab == null || !TryGetRoleZoneCenter(gen, role, out var pos)) return 0;

        // Quad는 기본이 수직(법선 -Z) → X+90으로 눕혀 바닥에 깔기. 바닥 z-fighting 방지로 살짝 띄움.
        GameObject go = Instantiate(markerPrefab, pos + Vector3.up * 0.05f,
            Quaternion.Euler(90f, 0f, 0f), _root);
        go.transform.localScale = new Vector3(size, size, 1f);

        // 마커는 순수 표시용 — 콜라이더 제거 (이동/레이캐스트 방해 방지)
        foreach (var col in go.GetComponentsInChildren<Collider>())
        {
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }
        return 1;
    }

    // 해당 역할로 뽑힌 존의 ZoneVolume 중앙 위치 찾기
    private bool TryGetRoleZoneCenter(MapGenerator gen, ZoneRole role, out Vector3 pos)
    {
        pos = Vector3.zero;
        ZoneDefinitionSO target = null;
        foreach (var zone in gen.Zones)
            if (zone != null && gen.GetZoneRole(zone) == role) { target = zone; break; }
        if (target == null) return false;

        foreach (var vol in FindObjectsByType<ZoneVolume>(FindObjectsSortMode.None))
            if (vol.Zone == target) { pos = vol.transform.position; return true; }

        Debug.LogWarning($"[MapContentSpawner] {role} 존({target.ZoneName})의 ZoneVolume을 못 찾음.");
        return false;
    }

    // 이전 생성물 제거 (재생성/디버그용). 서버라면 네트워크 오브젝트도 despawn.
    public void ClearGenerated()
    {
        foreach (var netObj in _spawnedNetObjs)
        {
            if (netObj != null && netObj.IsSpawned) netObj.Despawn();
            else if (netObj != null) Destroy(netObj.gameObject);
        }
        _spawnedNetObjs.Clear();

        var existing = GameObject.Find(RootName);
        if (existing != null)
        {
            if (Application.isPlaying) Destroy(existing);
            else DestroyImmediate(existing);
        }
    }
}
