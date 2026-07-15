using UnityEngine;
using System.Collections.Generic;

// 맵 생성 v2 — 사전 디자인 존 프리팹을 슬롯에 배치(위치 셔플). (Docs/design/level-system.md, map-generation.md §5.2 / PLAN)
public class MapGenerator : MonoBehaviour
{
    [Header("=== 설정 ===")]
    public MapGenConfigSO Config;                 // 몬스터 그룹 풀 등
    public ZoneLayoutCatalogSO ZoneLayoutCatalog; // (Size×Difficulty) 전투 풀 + 역할 고정 디자인
    public MapPrefabCatalogSO Catalog;            // 오버뷰 역할 아이콘용 (노드 스폰엔 미사용)

    [Header("=== 컴포넌트 참조 ===")]
    public LayoutPlacer LayoutPlacer;
    public MapContentSpawner ContentSpawner;

    [Header("=== 자동 생성 ===")]
    [Tooltip("Start에서 자동 생성. 네트워크 세션 중이면 MapNetworkSync(OnNetworkSpawn)가 서버 시드를 동기화해 호출.")]
    public bool AutoGenerateOnStart = true;

    [Header("=== 생성 결과 (런타임, 디버그용) ===")]
    public List<ZonePlacement> Placements = new List<ZonePlacement>();

    private bool _generated;
    private System.Random _rng;
    private readonly List<ZoneSlot> _slots = new List<ZoneSlot>();

    // 현재 슬롯 목록 (오버뷰/스포너 — 생성 후 유효)
    public IReadOnlyList<ZoneSlot> Slots => _slots;

    private void Start()
    {
        if (!AutoGenerateOnStart || _generated) return;

        // 네트워크 세션이 돌면 MapNetworkSync가 시드 동기화 후 Generate 호출 (클라 디싱크 방지)
        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm != null && nm.IsListening) return;

        Generate(System.Environment.TickCount, 0);
    }

    // 시드 + 난이도로 존 배치 생성. 결정적(같은 시드 → 서버/클라 동일).
    public List<ZonePlacement> Generate(int mapSeed, int difficultyLevel)
    {
        _generated = true;
        Placements = ComputePlacements(mapSeed, difficultyLevel);

        // 스폰: 존 비주얼(양쪽 로컬) + 몬스터(서버) — NGO 자동 복제
        ContentSpawner?.SpawnPlacements(this, Placements);

        Debug.Log($"[MapGenerator] 생성 완료. Seed:{mapSeed} / 난이도 Lv{difficultyLevel} / 슬롯 {_slots.Count} / 배치 {Placements.Count}.");
        OnGenerated?.Invoke(this); // 미니맵 베이크 등 후처리 훅 (생성물 배치 완료 시점)
        return Placements;
    }

    // 스폰 없이 배치만 계산 (Generate 공용 + 회전 저작 창의 조합 시뮬용). 결정적(같은 시드 → 서버/클라 동일).
    public List<ZonePlacement> ComputePlacements(int mapSeed, int difficultyLevel)
    {
        _rng = new System.Random(mapSeed);
        GatherSlots();       // 씬 ZoneSlot 수집 + SlotID 정렬 + 초기화
        AssignSlotRoles();   // 후보 중 퀘스트/보스/스폰 1곳씩
        if (LayoutPlacer == null)
        {
            Debug.LogWarning("[MapGenerator] LayoutPlacer 미연결 — 배치 계산 불가.");
            return new List<ZonePlacement>();
        }
        return LayoutPlacer.SelectLayouts(_slots, ZoneLayoutCatalog, difficultyLevel, _rng);
    }

    // 생성 완료 이벤트 — 구독자: MinimapController(지형 베이크/마커 수집)
    public static event System.Action<MapGenerator> OnGenerated;

    // 씬의 ZoneSlot 수집 + SlotID 정렬(결정성) + 런타임 초기화
    private void GatherSlots()
    {
        _slots.Clear();
        _slots.AddRange(FindObjectsByType<ZoneSlot>(FindObjectsSortMode.None));

        // 결정적 전순서(NGO 디싱크 방지): SlotID → 위치(x→z, 라운딩) 2차 키.
        // List.Sort는 불안정 정렬이라 SlotID 동률 시 FindObjectsByType 비결정 순서에 의존하므로 2차 키 필수.
        _slots.Sort((a, b) =>
        {
            int c = a.SlotID.CompareTo(b.SlotID);
            if (c != 0) return c;
            Vector3 pa = a.transform.position, pb = b.transform.position;
            c = Mathf.RoundToInt(pa.x * 100f).CompareTo(Mathf.RoundToInt(pb.x * 100f));
            if (c != 0) return c;
            return Mathf.RoundToInt(pa.z * 100f).CompareTo(Mathf.RoundToInt(pb.z * 100f));
        });

        // 중복 SlotID 조기 노출 (침묵 디싱크 방지)
        for (int i = 1; i < _slots.Count; i++)
            if (_slots[i].SlotID == _slots[i - 1].SlotID)
                Debug.LogError($"[MapGenerator] SlotID 중복 {_slots[i].SlotID}: '{_slots[i - 1].name}' vs '{_slots[i].name}' — 고유 ID 부여 필요(결정성 깨짐).");

        foreach (var s in _slots) if (s != null) s.ResetRuntime();
    }

    // 역할 후보 중 1곳씩 배정 (퀘스트 → 보스 → 스폰). 정렬 슬롯 + 단일 RNG → 결정적.
    private void AssignSlotRoles()
    {
        var taken = new HashSet<ZoneSlot>();
        AssignRole(s => s.IsQuestCandidate, ZoneRole.Quest, taken);
        AssignRole(s => s.IsBossCandidate, ZoneRole.BossRoom, taken);
        AssignRole(s => s.IsSpawnCandidate, ZoneRole.PlayerSpawn, taken);
    }

    private void AssignRole(System.Func<ZoneSlot, bool> predicate, ZoneRole role, HashSet<ZoneSlot> taken)
    {
        var pool = new List<ZoneSlot>();
        foreach (var s in _slots)
            if (s != null && predicate(s) && !taken.Contains(s)) pool.Add(s);
        if (pool.Count == 0) return;

        var chosen = pool[_rng.Next(pool.Count)];
        chosen.AssignedRole = role;
        taken.Add(chosen);
    }

    // 역할로 뽑힌 첫 슬롯 (오버뷰/스포너용)
    public ZoneSlot GetRoleSlot(ZoneRole role)
    {
        foreach (var s in _slots) if (s != null && s.AssignedRole == role) return s;
        return null;
    }

#if UNITY_EDITOR
    [ContextMenu("▶ Test Generate (random seed)")]
    private void EditorTestGenerate() => Generate(System.Environment.TickCount, 0);
#endif
}
