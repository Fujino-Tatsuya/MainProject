using UnityEngine;
using System.Collections.Generic;

public class MapGenerator : MonoBehaviour
{
    [Header("=== 설정 ===")]
    public MapGenConfigSO Config;
    public MapPrefabCatalogSO Catalog;
    public List<ZoneDefinitionSO> Zones;

    [Header("=== 컴포넌트 참조 ===")]
    public NodePlacer NodePlacer;
    public ObstaclePlacer ObstaclePlacer;
    public MapValidator Validator;
    public MapContentSpawner ContentSpawner;

    [Header("=== 자동 생성 ===")]
    [Tooltip("Start에서 자동 생성. 네트워크 세션 중이면 건너뜀 — MapNetworkSync(OnNetworkSpawn)가 서버 시드를 동기화해 호출 (클라가 제멋대로 다른 시드로 생성하는 디싱크 방지)")]
    public bool AutoGenerateOnStart = true;

    [Header("=== 생성 결과 (런타임, 디버그용) ===")]
    public List<GeneratedNodeData> Results = new List<GeneratedNodeData>();

    private bool _generated;

    private void Start()
    {
        if (!AutoGenerateOnStart || _generated) return;

        // 네트워크 세션이 돌고 있으면 MapNetworkSync가 시드 동기화 후 Generate를 호출한다.
        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm != null && nm.IsListening) return;

        Generate(System.Environment.TickCount, Difficulty.Normal);
    }

    private System.Random _rng;
    // 영역 역할은 SO를 변경하지 않고 런타임 dict로만 관리 (SO는 공유 에셋이라 변경 금지)
    private readonly Dictionary<ZoneDefinitionSO, ZoneRole> _zoneRoles = new Dictionary<ZoneDefinitionSO, ZoneRole>();
    // 씬의 SpawnPoint를 ParentZone 별로 수집 (SO가 씬 참조를 못 들기 때문)
    private readonly Dictionary<ZoneDefinitionSO, List<SpawnPoint>> _zonePoints = new Dictionary<ZoneDefinitionSO, List<SpawnPoint>>();
    private static readonly List<SpawnPoint> _empty = new List<SpawnPoint>();

    public List<GeneratedNodeData> Generate(int mapSeed, Difficulty selectedDifficulty)
    {
        _generated = true; // Start 자동 생성과 네트워크 호출 중복 방지
        _rng = new System.Random(mapSeed);
        Results.Clear();

        GatherSpawnPoints();                       // 씬 스캔 → ParentZone 별 그룹핑 + 초기화
        NodePlacer.Initialize(Config, Catalog, _rng);

        // 1. 영역 역할 배정 (퀘스트/보스/스폰 각 후보 중 1곳, 나머지 전투)
        AssignZoneRoles();

        // 2. 전투 영역 분류 (A등급 = 1티어 가능). 스폰포인트 목록으로 모음.
        var combatZonePoints = new List<List<SpawnPoint>>();
        var combatAZonePoints = new List<List<SpawnPoint>>();
        foreach (var zone in Zones)
        {
            if (zone == null || _zoneRoles[zone] != ZoneRole.Combat) continue;
            var pts = PointsOf(zone);
            combatZonePoints.Add(pts);
            if (zone.DefaultGrade == ZoneGrade.A_UpToTier1) combatAZonePoints.Add(pts);
        }

        // 3. 노드 배치 (Min/Max = 맵 전체 총량 기반)
        NodePlacer.PlaceTier1(combatAZonePoints);
        NodePlacer.PlaceTier2(combatZonePoints);
        NodePlacer.PlaceTier3(combatZonePoints);

        // 4. 몬스터/보상 (현재는 데이터 골격만 — 추후)
        AssignMonsterGroups(selectedDifficulty);

        // 5. 결과 수집 (디버그 UI / 서버 스폰이 읽어갈 목록)
        CollectResults();

        // 6. 경로 검증
        if (Validator != null && !Validator.ValidateMapPaths())
            Debug.LogWarning("[MapGenerator] 경로 검증 실패 — 재시도 로직 필요.");

        // 7. 실제 콘텐츠 스폰 (프리팹 인스턴스화 + 네트워크 오브젝트는 서버 Spawn)
        ContentSpawner?.SpawnGenerated(this);

        Debug.Log($"[MapGenerator] 생성 완료. Seed:{mapSeed} / 전투영역 {combatZonePoints.Count} / 노드 {Results.Count}개.");
        return Results;
    }

    public ZoneRole GetZoneRole(ZoneDefinitionSO zone)
    {
        return _zoneRoles.TryGetValue(zone, out var role) ? role : ZoneRole.Combat;
    }

    // 씬의 모든 SpawnPoint를 ParentZone 별로 모으고 런타임 상태 초기화
    private void GatherSpawnPoints()
    {
        _zonePoints.Clear();
        var all = FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None);
        foreach (var sp in all)
        {
            if (sp == null || sp.ParentZone == null) continue;
            if (!_zonePoints.TryGetValue(sp.ParentZone, out var list))
            {
                list = new List<SpawnPoint>();
                _zonePoints[sp.ParentZone] = list;
            }
            list.Add(sp);
            sp.ResetRuntime();
        }
        // 결정적 순서를 위해 PointID로 정렬
        foreach (var list in _zonePoints.Values)
            list.Sort((a, b) => a.PointID.CompareTo(b.PointID));
    }

    private List<SpawnPoint> PointsOf(ZoneDefinitionSO zone)
    {
        return _zonePoints.TryGetValue(zone, out var list) ? list : _empty;
    }

    private void AssignZoneRoles()
    {
        // 후보 풀에서 1곳씩 선택 (이미 다른 역할로 뽑힌 곳은 제외)
        var taken = new HashSet<ZoneDefinitionSO>();

        ZoneDefinitionSO quest = PickCandidate(z => z.IsQuestZoneCandidate, taken);
        ZoneDefinitionSO boss  = PickCandidate(z => z.IsBossGateCandidate, taken);
        ZoneDefinitionSO spawn = PickCandidate(z => z.IsPlayerSpawnCandidate, taken);

        foreach (var zone in Zones)
        {
            if (zone == null) continue;
            ZoneRole role = ZoneRole.Combat;
            if (zone == quest)      role = ZoneRole.Quest;
            else if (zone == boss)  role = ZoneRole.BossRoom;
            else if (zone == spawn) role = ZoneRole.PlayerSpawn;
            _zoneRoles[zone] = role;
        }

        Debug.Log($"[MapGenerator] 역할 — 퀘스트:{NameOf(quest)} / 보스:{NameOf(boss)} / 스폰:{NameOf(spawn)}");
    }

    private ZoneDefinitionSO PickCandidate(System.Func<ZoneDefinitionSO, bool> predicate, HashSet<ZoneDefinitionSO> taken)
    {
        var pool = new List<ZoneDefinitionSO>();
        foreach (var zone in Zones)
            if (zone != null && predicate(zone) && !taken.Contains(zone)) pool.Add(zone);

        if (pool.Count == 0) return null;
        ZoneDefinitionSO chosen = pool[_rng.Next(pool.Count)];
        taken.Add(chosen);
        return chosen;
    }

    private void AssignMonsterGroups(Difficulty diff)
    {
        // TODO: 몬스터 기획/에셋 확정 후 그룹·마릿수 배정. 현재는 데이터 골격만.
    }

    private void CollectResults()
    {
        foreach (var list in _zonePoints.Values)
            foreach (var sp in list)
                if (sp != null && sp.IsAssigned) Results.Add(sp.NodeData);
    }

    private static string NameOf(ZoneDefinitionSO z) => z != null ? z.ZoneName : "(없음)";

#if UNITY_EDITOR
    // 인스펙터 우클릭 → 에디터에서 즉시 생성 테스트 (랜덤 시드, 매번 다른 결과)
    [ContextMenu("▶ Test Generate (random seed)")]
    private void EditorTestGenerate()
    {
        Generate(System.Environment.TickCount, Difficulty.Normal);
    }
#endif
}
