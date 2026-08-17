using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 저작 도구 — "밟고 지나가는 면"의 레이어를 Default → Ground 로 통일 (2026-07-30).
//
// 배경: 맵의 바닥·경사로·계단이 전부 Default(0)에 있었다. Ground(3)에 있는 콜라이더는 하나도 없다.
// GroundProbe 는 Default|Ground 를 강제로 OR 하기 때문에 폭탄/장판은 지금도 바닥을 찾지만,
// Ground 만 보는 마스크(PlayerAimIndicator.groundMask = Ground 전용)는 생성맵에서 절대 맞지 않는다.
// 레이어를 의미대로 정리해 두면 "바닥을 찾고 싶다"는 의도를 마스크로 표현할 수 있게 된다.
//
// ⚠️ 이 도구만으로는 dash 간헐 실패가 고쳐지지 않는다.
// PlayerGroundingSensor 의 aliveGroundMask/soulGroundMask 가 Everything 이라 이미 Default 를 보고 있고,
// Unit 콜라이더를 제외하지 않아서 보스 히트박스·다른 플레이어 캡슐이 계속 "바닥" 후보로 남는다.
// 마스크를 좁히고 Unit 제외를 넣는 것은 Player 도메인 작업으로 별도다.
//
// 판정 규칙 — 이름만 믿지 않는다. 네 조건을 모두 만족해야 바꾼다:
//   1) Collider 가 있고 isTrigger 가 아니다      (트리거는 바닥이 아니다 — Vent/AttackArea, HazardArea 배제)
//   2) 현재 레이어가 정확히 Default(0) 이다      (Wall(7)/HazardArea(9) 등 이미 분류된 것은 건드리지 않음
//                                                → bossroom 의 Boundary_*/InvisibleBoundaries 자동 보호)
//   3) 이름이 보행면 키워드에 걸린다
//   4) 이름이 제외 키워드에 걸리지 않는다
// 어느 쪽 키워드에도 안 걸리는 콜라이더(예: "Cube")는 바꾸지 않고 "미분류"로 보고만 한다.
//
// 중첩 프리팹 인스턴스 내부는 건너뛴다 — 원본 프리팹이 이 스캔에 포함되므로 원본에서 1회만 바꾼다.
// 인스턴스에서 바꾸면 오버라이드가 남아 원본과 어긋난다(구 MapColliderAuthoring 도 같은 규칙이었다.
// 그 도구는 일회성이라 2026-08-18 에 삭제했다).
// 프리팹만 고치면 씬 인스턴스에 레이어 오버라이드가 걸려 있을 때 반영되지 않으므로 열린 씬도 함께 처리한다.
public static class GroundLayerAuthoring
{
    const string TargetFolder = "Assets/2.Prefabs/Map";
    const string GroundLayerName = "Ground";

    // 밟고 지나가는 면. Env_floor_* / Env_slope_* / Env_stairs_* 계열과, 이름이 다른 개별 케이스.
    static readonly string[] WalkableKeywords =
    {
        "floor", "slope", "stair", "ramp", "ground", "walkway", "platform",
    };

    // 보행면처럼 보이지만 아닌 것. 벽·경계·판정용 트리거·소품.
    static readonly string[] ExcludeKeywords =
    {
        "wall", "boundary", "invisible", "attackarea", "hazard", "trigger",
        "blocker", "laser", "gate", "door", "ceiling", "roof",
    };

    [MenuItem("Tools/Map/Authoring/Ground Layer - 검사 (Dry Run)")]
    public static void DryRun() => Execute(false);

    [MenuItem("Tools/Map/Authoring/Ground Layer - 적용 (프리팹 + 열린 씬)")]
    public static void Apply() => Execute(true);

    // ── 플레이어 접지 마스크 ────────────────────────────────────────────────────
    //
    // 보행면이 Ground 로 정리된 뒤에야 의미가 생기는 후속 작업이다.
    // PlayerGroundingSensor.aliveGroundMask 가 Everything(~0) 이면 다음이 모두 "바닥"이 된다:
    //   · 보스 공격 히트박스 (No.23 은 Default 레이어 콜라이더가 7개)
    //   · 다른 플레이어의 캡슐 (Player 레이어)
    //   · 시체·적·소품
    // PlayerGroundingSensor 는 GroundProbe 와 달리 Unit 콜라이더를 제외하지 않는다
    // (IsOwnCollider 가 자기 자신/자식만 제외). 경사각 필터만 통과하면 바닥으로 인정되므로,
    // 주변에 무엇이 있느냐에 따라 접지가 달라진다 — dash 의 "되다 말다"가 이 형태다.
    // 누락은 항상 실패를, 과다 포함은 간헐적 실패를 만든다.
    //
    // Ground 전용으로 좁히면 위 후보가 전부 빠지고, 움직이는 발판(PlatformBody)도 Ground 라 유지된다.
    // 검증: 모든 테스트 씬의 실제 바닥이 이미 Ground 다 (PlayerDashTest=Plane, BossScene/
    // PlayerBossTest=Ground, PlayerScene=Ground, MapScene=맵 프리팹 상속).
    // Default 로 남는 것은 dash 장애물용 Cube 와 BossArea 마커뿐이다.
    //
    // soulGroundMask 는 건드리지 않는다 — 유령 상태는 통과 규칙이 다를 수 있어 별도 판단이다.
    const string PlayerPrefabFolder = "Assets/2.Prefabs/Player";

    [MenuItem("Tools/Map/Authoring/Ground Layer - 플레이어 접지 마스크를 Ground 전용으로")]
    public static void NarrowPlayerGroundMask()
    {
        int groundLayer = LayerMask.NameToLayer(GroundLayerName);
        if (groundLayer < 0)
        {
            Debug.LogError($"[GroundLayer] '{GroundLayerName}' 레이어가 없다.");
            return;
        }

        int target = 1 << groundLayer;
        var sb = new StringBuilder("[GroundLayer] 플레이어 접지 마스크\n");
        int changed = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PlayerPrefabFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                var sensor = root.GetComponentInChildren<PlayerGroundingSensor>(true);
                if (sensor == null)
                    continue;

                var so = new SerializedObject(sensor);
                SerializedProperty alive = so.FindProperty("aliveGroundMask");
                SerializedProperty soul = so.FindProperty("soulGroundMask");

                if (alive == null)
                {
                    sb.AppendLine($"  ⚠️ aliveGroundMask 필드 없음: {path}");
                    continue;
                }

                int before = alive.intValue;
                string soulNote = soul != null ? $" / soulGroundMask={soul.intValue}(유지)" : "";

                if (before == target)
                {
                    sb.AppendLine($"  정상  {path}  alive={before}{soulNote}");
                    continue;
                }

                alive.intValue = target;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                changed++;

                sb.AppendLine($"  수정  {path}  alive={before} → {target}{soulNote}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        sb.AppendLine($"  → 프리팹 {changed}개 수정 (Ground 전용 = {target})");
        Debug.Log(sb.ToString());
    }

    struct Stat
    {
        public int Changed;
        public int AlreadyGround;
        public int Unclassified;
        public int SkippedTrigger;
        public int SkippedOtherLayer;
        public int ClassifiedByMaterial;
    }

    /// <summary>
    /// 머티리얼 이름으로 보행면 여부를 판정한다.
    /// 판정 근거를 찾았을 때만 true 를 반환하고, 결과는 <paramref name="walkable"/> 로 준다.
    /// 렌더러/머티리얼이 없거나 어느 키워드에도 안 걸리면 false — 호출부가 "미분류"로 남긴다.
    /// </summary>
    static bool TryMaterialVerdict(GameObject go, out bool walkable)
    {
        walkable = false;

        var renderer = go.GetComponent<Renderer>();
        if (renderer == null)
            return false;

        Material[] materials = renderer.sharedMaterials;
        if (materials == null)
            return false;

        for (int i = 0; i < materials.Length; i++)
        {
            Material mat = materials[i];
            if (mat == null)
                continue;

            if (MatchesAny(mat.name, ExcludeKeywords))
            {
                walkable = false;
                return true;
            }

            if (MatchesAny(mat.name, WalkableKeywords))
            {
                walkable = true;
                return true;
            }
        }

        return false;
    }

    static void Execute(bool apply)
    {
        int groundLayer = LayerMask.NameToLayer(GroundLayerName);
        if (groundLayer < 0)
        {
            Debug.LogError($"[GroundLayer] '{GroundLayerName}' 레이어가 프로젝트에 없다. TagManager 확인 필요.");
            return;
        }

        var total = new Stat();
        var changedByName = new SortedDictionary<string, int>();
        var unclassifiedByName = new SortedDictionary<string, int>();
        var changedPrefabs = new List<string>();

        // ── 프리팹 ────────────────────────────────────────────────────────────
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { TargetFolder });
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                EditorUtility.DisplayProgressBar("Ground Layer", path, (float)i / Mathf.Max(1, guids.Length));

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                int changedHere = 0;
                try
                {
                    changedHere = Scan(root, groundLayer, apply, recordUndo: false,
                                       ref total, changedByName, unclassifiedByName);

                    if (apply && changedHere > 0)
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }

                if (changedHere > 0)
                    changedPrefabs.Add($"{changedHere,5}  {path}");
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        // ── 열린 씬 ───────────────────────────────────────────────────────────
        var changedScenes = new List<string>();
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded)
                continue;

            int changedHere = 0;
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                changedHere += Scan(go, groundLayer, apply, recordUndo: true,
                                    ref total, changedByName, unclassifiedByName);
            }

            if (changedHere > 0)
            {
                changedScenes.Add($"{changedHere,5}  {scene.name}");
                if (apply)
                    EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        if (apply)
            AssetDatabase.SaveAssets();

        Report(apply, total, changedByName, unclassifiedByName, changedPrefabs, changedScenes);
    }

    static int Scan(GameObject root, int groundLayer, bool apply, bool recordUndo,
                    ref Stat total,
                    SortedDictionary<string, int> changedByName,
                    SortedDictionary<string, int> unclassifiedByName)
    {
        int changed = 0;

        foreach (Collider col in root.GetComponentsInChildren<Collider>(true))
        {
            GameObject go = col.gameObject;

            // 프리팹 인스턴스 내부는 건너뛴다 — 프리팹 패스에서 원본을 고치면 인스턴스는 상속받는다.
            // 여기서 같이 바꾸면 레이어 오버라이드가 씬에 쌓여(맵 씬에서 388건) 이후 프리팹 수정이
            // 인스턴스에 전파되지 않는다. 씬 패스는 "씬에 직접 배치된 오브젝트"만 담당한다.
            if (PrefabUtility.IsPartOfPrefabInstance(go))
                continue;

            if (col.isTrigger)
            {
                total.SkippedTrigger++;
                continue;
            }

            if (go.layer == groundLayer)
            {
                total.AlreadyGround++;
                continue;
            }

            // 이미 다른 의미로 분류된 레이어는 건드리지 않는다.
            if (go.layer != 0)
            {
                total.SkippedOtherLayer++;
                continue;
            }

            string name = go.name;
            if (MatchesAny(name, ExcludeKeywords))
                continue;

            bool walkable = MatchesAny(name, WalkableKeywords);

            // 이름으로 판별이 안 되면 머티리얼 이름을 본다.
            // 존 프리팹의 바닥 조각 다수가 모델링 툴 기본 이름("Cube.289")으로 들어와 있고,
            // 그것들은 전부 MA_floor_urethane 을 쓴다. 이름보다 머티리얼이 신뢰도 높은 신호다.
            if (!walkable && TryMaterialVerdict(go, out bool matWalkable))
            {
                if (!matWalkable)
                    continue;

                walkable = true;
                total.ClassifiedByMaterial++;
            }

            if (!walkable)
            {
                total.Unclassified++;
                Bump(unclassifiedByName, Normalize(name));
                continue;
            }

            if (apply)
            {
                if (recordUndo)
                    Undo.RecordObject(go, "Assign Ground Layer");
                go.layer = groundLayer;
            }

            total.Changed++;
            changed++;
            Bump(changedByName, Normalize(name));
        }

        return changed;
    }

    static bool MatchesAny(string name, string[] keywords)
    {
        string lower = name.ToLowerInvariant();
        for (int i = 0; i < keywords.Length; i++)
        {
            if (lower.Contains(keywords[i]))
                return true;
        }
        return false;
    }

    // "Env_floor_basic_typeA (12)" → "Env_floor_basic_typeA" — 보고를 이름 계열로 묶는다.
    static string Normalize(string name)
    {
        int paren = name.IndexOf(" (");
        return paren > 0 ? name.Substring(0, paren) : name;
    }

    static void Bump(SortedDictionary<string, int> map, string key)
    {
        map.TryGetValue(key, out int n);
        map[key] = n + 1;
    }

    static void Report(bool apply, Stat total,
                       SortedDictionary<string, int> changedByName,
                       SortedDictionary<string, int> unclassifiedByName,
                       List<string> changedPrefabs, List<string> changedScenes)
    {
        var sb = new StringBuilder();
        sb.AppendLine(apply
            ? "[GroundLayer] 적용 완료 — Default(0) → Ground"
            : "[GroundLayer] 검사만 수행 (Dry Run) — 변경 없음");
        sb.AppendLine($"  대상          {total.Changed}건 (그중 머티리얼로 판정 {total.ClassifiedByMaterial}건)");
        sb.AppendLine($"  이미 Ground   {total.AlreadyGround}건");
        sb.AppendLine($"  트리거 제외   {total.SkippedTrigger}건");
        sb.AppendLine($"  타 레이어     {total.SkippedOtherLayer}건 (Wall/HazardArea 등 — 손대지 않음)");
        sb.AppendLine($"  ⚠️ 미분류     {total.Unclassified}건 (이름으로 보행면 판별 불가 — 아래 목록 확인)");

        if (changedByName.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  ── 변경 대상 (이름별) ──");
            foreach (var kv in changedByName.OrderByDescending(k => k.Value))
                sb.AppendLine($"    {kv.Value,5}  {kv.Key}");
        }

        if (unclassifiedByName.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  ── 미분류 (판단 필요: 보행면이면 WalkableKeywords 에 추가) ──");
            foreach (var kv in unclassifiedByName.OrderByDescending(k => k.Value).Take(25))
                sb.AppendLine($"    {kv.Value,5}  {kv.Key}");
        }

        if (changedPrefabs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  ── 프리팹별 ──");
            foreach (string line in changedPrefabs)
                sb.AppendLine($"  {line}");
        }

        if (changedScenes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  ── 씬별 (적용 시 저장은 수동) ──");
            foreach (string line in changedScenes)
                sb.AppendLine($"  {line}");
        }

        Debug.Log(sb.ToString());
    }
}
