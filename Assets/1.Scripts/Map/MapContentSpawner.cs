using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

// 배치 결과(ZonePlacement)를 실제 인스턴스로 구현한다.
//  - 존 레이아웃 프리팹(바닥/벽/테마/노드 마커) = 비네트워크 시각물 → 서버/클라 각자 로컬 생성
//    (같은 시드라 결과 동일). 규약: 이 프리팹에는 NetworkObject를 두지 않는다.
//  - 몬스터 = 서버만 ZoneLayout의 스폰 마커 위치에 NetworkObject 프리팹을 Spawn() → NGO 복제.
public class MapContentSpawner : MonoBehaviour
{
    public const string RootName = "GeneratedMap";

    private Transform _root;
    private readonly List<NetworkObject> _spawnedNetObjs = new List<NetworkObject>();

    public void SpawnPlacements(MapGenerator gen, List<ZonePlacement> placements)
    {
        ClearGenerated();
        _root = new GameObject(RootName).transform;

        var nm = NetworkManager.Singleton;
        bool isServer = nm != null && nm.IsServer;

        int visuals = 0, monsters = 0;
        if (placements != null)
        {
            foreach (var p in placements)
            {
                if (p.Slot == null || p.LayoutPrefab == null) continue;

                // 존 비주얼 — 양쪽 로컬 생성 (슬롯 앵커 위치/방향)
                GameObject zoneGo = Instantiate(p.LayoutPrefab,
                    p.Slot.transform.position, p.Slot.transform.rotation, _root);
                p.Slot.IsFilled = true;
                visuals++;

                // 몬스터 — 서버만 스폰 (NetworkObject → NGO 복제, 클라는 수신)
                if (isServer) monsters += SpawnMonstersFor(zoneGo, gen);
            }
        }

        Edit.Log($"[MapContentSpawner] 존 비주얼 {visuals} / 몬스터 {monsters} 스폰 (서버:{isServer}).");
    }

    private int SpawnMonstersFor(GameObject zoneGo, MapGenerator gen)
    {
        var layout = zoneGo.GetComponent<ZoneLayout>();
        if (layout == null || layout.MonsterSpawnPoints == null || layout.MonsterSpawnPoints.Count == 0)
            return 0;

        GameObject monsterPrefab = ResolveMonsterPrefab(gen, layout.MonsterGroupID);
        if (monsterPrefab == null) return 0; // 몬스터 에셋 확정 전엔 마커만 두고 스킵

        int n = 0;
        foreach (var marker in layout.MonsterSpawnPoints)
        {
            if (marker == null) continue;
            GameObject go = Instantiate(monsterPrefab, marker.position, marker.rotation);
            var netObj = go.GetComponent<NetworkObject>();
            if (netObj != null) { netObj.Spawn(); _spawnedNetObjs.Add(netObj); } // NGO 복제 + despawn 추적
            else go.transform.SetParent(_root, true);  // 비네트워크 몬스터 → 루트 하위로 ClearGenerated가 정리(누수 방지)
            n++;
        }
        return n;
    }

    // MonsterGroupID → 프리팹 (Config.MonsterGroups). 실제 몬스터 에셋은 추후.
    private static GameObject ResolveMonsterPrefab(MapGenerator gen, int groupId)
    {
        var cfg = gen != null ? gen.Config : null;
        if (cfg == null || cfg.MonsterGroups == null) return null;
        foreach (var g in cfg.MonsterGroups)
            if (g.GroupID == groupId) return g.MonsterPrefab;
        return null;
    }

    // 이전 생성물 제거 (재생성/디버그). 서버라면 네트워크 오브젝트도 despawn.
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
