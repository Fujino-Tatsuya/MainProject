using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 존 하이어라키 정리 저작 도구 (할 일 32 세팅 보조 16 — 사용자 승인 표준).
//
// 존마다 제각각 정리된 하이어라키를 아래 표준으로 맞춘다(bossroom 제외). 다시 돌리면 같은 결과(멱등) — 바뀐 게 없으면 저장하지 않는다.
//
//   Zone_XXX
//   ├ Environment / Floor(Floor_1F·Floor_2F) · Wall · Pillar · Stair · Fence · Machinery(Elevator)
//   ├ Colliders      바닥 Ground 박스 Floor_00… · 벽 Wall 박스
//   ├ Props / Boxes · Barrels · Pallets · Containers · Chains · Pipes · Etc
//   ├ Water          ZoneWater 오브젝트 자체(그룹으로 감싸지 않는다 — FlatKitWaterPatchAuthoring 이 root.Find("Water") 로 찾고 재생성한다)
//   ├ Lighting       Light 달린 오브젝트(LGT_*)
//   ├ MonsterSpawns  MonsterSpawnPoint 달린 오브젝트
//   └ Unsorted       규칙으로 못 정한 것(있을 때만)
//
//  · 프리팹 인스턴스 분류 = 가장 가까운 원본(GetCorrespondingObjectFromSource) 에셋 경로·이름. 이름 규칙(fence·pillar/column)이 폴더보다 먼저.
//    Lighting/Fixtures(Light 없는 등 기구)는 Props/Etc.
//  · 이름이 표준과 같은(또는 변형: Floors·Walls·Pillars·Fences·Pallet·Barrel·Container·Chain·Pipe·Lights·SpawnPoints…) 사용자 그룹은
//    내용을 다시 분류하지 않고 그대로 표준 그룹으로 옮긴다(L_B 의 Pillar 안 벽 조각도 Pillar 에 남는다). 그 안의 모르는 하위 그룹(Molding)은 통째로.
//    Environment·Props 와 모르는 그룹(Zone Layout·PF_Prop_*·InteractiveObject·Teleport 등)은 풀어서 규칙으로 분류한다.
//  · 어디에 있든 Light·MonsterSpawnPoint·ZoneWater·바닥/벽 콜라이더는 표준 자리로 뺀다. 바닥 콜라이더(ZoneFloor·FloorCollider*)는 Floor_NN 으로 이름만 바꾼다.
//  · 표준 그룹 = 항등 Transform · Default 레이어 · Untagged. 같은 이름이 있어도 항등이 아니면 새로 만들고 옛 것은 비면 지운다.
//  · 이동은 월드 유지(SetParent worldPositionStays). 꺼진 그룹 때문에 꺼져 있던 것만 받은 새 그룹은 꺼 둔다(Quest01) — 화면 상태 유지.
//  · 빈 오브젝트(자식·컴포넌트 없음)와 정리 후 빈 그룹은 지우고 보고한다. 형제 순서 = 그룹 안 이름순(Ordinal).
// 🔴 콜라이더 추가·삭제 없음, 컴포넌트 값 변경 없음(그룹 레이어·태그 정리 제외).
public static class ZoneHierarchyAuthoring
{
    const string ZonesFolder = "Assets/2.Prefabs/Environment/Layouts/Zones";
    const string BossRoomPath = ZonesFolder + "/bossroom.prefab";
    const string Tag = "[ZoneHierarchy]";
    const string EnvRoot = "Environment";
    const string PropsRoot = "Props";
    const string CollidersGroup = "Colliders";
    const string LightingGroup = "Lighting";
    const string SpawnsGroup = "MonsterSpawns";
    const string UnsortedGroup = "Unsorted";
    const string WaterName = "Water";
    const string FloorColliderPrefix = "Floor_";
    const string FloorGroup = EnvRoot + "/Floor";
    const string WallGroup = EnvRoot + "/Wall";
    const string PillarGroup = EnvRoot + "/Pillar";
    const string StairGroup = EnvRoot + "/Stair";
    const string FenceGroup = EnvRoot + "/Fence";
    const string MachineryGroup = EnvRoot + "/Machinery";
    const float Epsilon = 0.0001f;

    static readonly string[] RootOrder = { EnvRoot, CollidersGroup, PropsRoot, WaterName, LightingGroup, SpawnsGroup, UnsortedGroup };
    static readonly string[] EnvOrder = { "Floor", "Wall", "Pillar", "Stair", "Fence", "Machinery" };
    static readonly string[] PropsOrder = { "Boxes", "Barrels", "Pallets", "Containers", "Chains", "Pipes", "Etc" };

    // 이름(소문자) → 표준 경로. 내용은 다시 분류하지 않고 그대로 옮긴다.
    static readonly Dictionary<string, string> KeptGroups = new Dictionary<string, string>
    {
        { "floor", FloorGroup }, { "floors", FloorGroup },
        { "floor_1f", FloorGroup + "/Floor_1F" }, { "floor1f", FloorGroup + "/Floor_1F" },
        { "floor_2f", FloorGroup + "/Floor_2F" }, { "floor2f", FloorGroup + "/Floor_2F" },
        { "wall", WallGroup }, { "walls", WallGroup },
        { "pillar", PillarGroup }, { "pillars", PillarGroup },
        { "stair", StairGroup }, { "stairs", StairGroup },
        { "fence", FenceGroup }, { "fences", FenceGroup },
        { "machinery", MachineryGroup },
        { "elevator", MachineryGroup + "/Elevator" },
        { "boxes", PropsRoot + "/Boxes" }, { "box", PropsRoot + "/Boxes" },
        { "barrel", PropsRoot + "/Barrels" }, { "barrels", PropsRoot + "/Barrels" },
        { "pallet", PropsRoot + "/Pallets" }, { "pallets", PropsRoot + "/Pallets" },
        { "container", PropsRoot + "/Containers" }, { "containers", PropsRoot + "/Containers" },
        { "chain", PropsRoot + "/Chains" }, { "chains", PropsRoot + "/Chains" },
        { "pipe", PropsRoot + "/Pipes" }, { "pipes", PropsRoot + "/Pipes" },
        { "etc", PropsRoot + "/Etc" },
        { "lighting", LightingGroup }, { "lights", LightingGroup }, { "light", LightingGroup }, { "lighting_target_combatlevel", LightingGroup },
        { "monsterspawns", SpawnsGroup }, { "spawnpoints", SpawnsGroup },
        { "colliders", CollidersGroup },
        { "unsorted", UnsortedGroup },
    };

    // 풀어서 규칙으로 분류하는 컨테이너(하위 그룹은 이름으로 다시 매칭).
    static readonly HashSet<string> Containers = new HashSet<string> { "environment", "props" };

    class Ctx
    {
        public GameObject Root;
        public Dictionary<GameObject, bool> WasActive = new Dictionary<GameObject, bool>();
        public Dictionary<Transform, List<GameObject>> Received = new Dictionary<Transform, List<GameObject>>();
        public HashSet<Transform> Created = new HashSet<Transform>();
        public List<Transform> FloorColliders = new List<Transform>();
        public List<string> Unsorted = new List<string>();
        public List<string> DeletedEmpty = new List<string>();
        public List<string> DeletedGroups = new List<string>();
        public List<string> Notes = new List<string>();
        public int Changes;
    }

    [MenuItem("Tools/Map/Authoring/Zones/존 하이어라키 정리 (멱등)")]
    public static void Generate()
    {
        var report = new StringBuilder();
        report.AppendLine($"{Tag} 존 하이어라키 정리 — bossroom 제외");
        foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { ZonesFolder })
                     .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, System.StringComparer.Ordinal))
        {
            if (path == BossRoomPath) continue;
            report.AppendLine(ProcessZone(path));
        }
        Debug.Log(report.ToString());
    }

    static string ProcessZone(string path)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        var sb = new StringBuilder();
        EditPrefab(path, root =>
        {
            var ctx = new Ctx { Root = root };
            Organize(ctx);
            sb.AppendLine($"== {name}: {(ctx.Changes > 0 ? $"갱신({ctx.Changes})" : "변경 없음")}");
            AppendTree(sb, root.transform);
            if (ctx.Unsorted.Count > 0) sb.AppendLine("  Unsorted: " + string.Join(", ", ctx.Unsorted));
            if (ctx.DeletedEmpty.Count > 0) sb.AppendLine("  삭제한 빈 오브젝트: " + string.Join(", ", ctx.DeletedEmpty));
            if (ctx.DeletedGroups.Count > 0) sb.AppendLine("  삭제한 빈 그룹: " + string.Join(", ", ctx.DeletedGroups));
            foreach (string n in ctx.Notes) sb.AppendLine("  · " + n);
            return ctx.Changes > 0;
        }, out string error);
        if (error != null) sb.AppendLine($"== {name}: 건너뜀 — {error}");
        return sb.ToString().TrimEnd();
    }

    // ─── 정리 ──────────────────────────────────────────────────────────────

    static void Organize(Ctx ctx)
    {
        Transform root = ctx.Root.transform;
        var originals = root.GetComponentsInChildren<Transform>(true).Where(t => t != root).ToList();
        foreach (Transform t in originals) ctx.WasActive[t.gameObject] = t.gameObject.activeInHierarchy;
        // 지울 후보 = 원래 있던 순수 오브젝트(인스턴스 내부 제외). 깊은 것부터.
        var groupCandidates = originals.Where(t => !InsideInstance(t, root) && IsPlain(t.gameObject))
            .OrderByDescending(t => Depth(t, root)).ToList();
        var originallyEmpty = new HashSet<Transform>(groupCandidates.Where(t => t.childCount == 0));

        // 표준 그룹 레이어·태그 정리(이미 자리에 있는 것)
        Process(ctx, root, null);

        // 바닥 콜라이더 이름 통일
        if (ctx.FloorColliders.Count > 0)
        {
            Transform colliders = GetGroup(ctx, CollidersGroup);
            var taken = new HashSet<string>(colliders.Cast<Transform>().Where(c => !ctx.FloorColliders.Contains(c)).Select(c => c.name));
            int next = 0;
            foreach (Transform c in ctx.FloorColliders.OrderBy(c => c.name, System.StringComparer.Ordinal).ThenBy(c => c.GetSiblingIndex()))
            {
                string n;
                do n = $"{FloorColliderPrefix}{next++:00}"; while (taken.Contains(n));
                if (c.name != n) { ctx.Notes.Add($"바닥 콜라이더 {c.name} → {CollidersGroup}/{n}"); c.name = n; ctx.Changes++; }
            }
        }

        // 빈 오브젝트·빈 그룹 삭제
        foreach (Transform t in groupCandidates)
        {
            if (t == null || t.childCount > 0) continue;
            string label = PathOf(t, root);
            if (originallyEmpty.Contains(t)) ctx.DeletedEmpty.Add(label);
            else ctx.DeletedGroups.Add(label);
            Object.DestroyImmediate(t.gameObject);
            ctx.Changes++;
        }

        // 꺼진 그룹에서만 받은 새 그룹은 끈다(옮기기 전 화면 상태 유지)
        foreach (var kv in ctx.Received.Where(k => k.Key != null && ctx.Created.Contains(k.Key)))
        {
            // 자기 자신이 꺼진 것(activeSelf false)은 그룹 상태와 무관 — 조상 그룹 때문에 꺼져 있던 것만 센다
            if (kv.Value.Count > 0 && kv.Value.All(g => !ctx.WasActive[g] && g.activeSelf) && kv.Key.childCount == kv.Value.Count && kv.Key.gameObject.activeSelf)
            {
                kv.Key.gameObject.SetActive(false);
                ctx.Notes.Add($"{PathOf(kv.Key, root)} 꺼 둠 — 꺼진 그룹에서 옮긴 것만 들어 있음");
            }
        }
        foreach (var kv in ctx.WasActive)
        {
            if (kv.Key == null || kv.Key.activeInHierarchy == kv.Value) continue;
            if (!kv.Value && kv.Key.activeSelf)
            {
                kv.Key.SetActive(false);
                ctx.Notes.Add($"{PathOf(kv.Key.transform, root)} activeSelf 끔 — 꺼진 그룹에서 켜진 그룹으로 옮겨짐");
            }
            else ctx.Notes.Add($"🔴 {PathOf(kv.Key.transform, root)} 활성 상태 달라짐(전 {kv.Value})");
        }

        // 형제 순서
        SortChildren(ctx, root, RootOrder);
        foreach (Transform g in root.Cast<Transform>().ToList())
        {
            if (!IsStandardGroup(g)) continue;
            if (g.name == EnvRoot) { SortChildren(ctx, g, EnvOrder); foreach (Transform s in g) SortStandard(ctx, s); }
            else if (g.name == PropsRoot) { SortChildren(ctx, g, PropsOrder); foreach (Transform s in g) SortStandard(ctx, s); }
            else SortChildren(ctx, g, null);
        }
    }

    static void SortStandard(Ctx ctx, Transform group)
    {
        if (!IsStandardGroup(group)) return;
        SortChildren(ctx, group, null);
        foreach (Transform s in group)
            if (s.name == "Floor_1F" || s.name == "Floor_2F" || s.name == "Elevator") SortChildren(ctx, s, null);
    }

    // parent 의 자식을 분류한다. kept = 내용을 그대로 받을 표준 그룹(없으면 규칙 분류).
    static void Process(Ctx ctx, Transform parent, Transform kept)
    {
        Transform root = ctx.Root.transform;
        foreach (Transform c in parent.Cast<Transform>().ToList())
        {
            GameObject go = c.gameObject;
            if (PrefabUtility.IsAnyPrefabInstanceRoot(go))
            {
                Transform dest = kept;
                if (dest == null)
                {
                    string target = ClassifyInstance(go);
                    if (target == UnsortedGroup) ctx.Unsorted.Add($"{go.name} <{SourcePath(go)}>");
                    dest = GetGroup(ctx, target);
                }
                MoveTo(ctx, c, dest);
                continue;
            }

            string special = ClassifySpecial(go);
            if (special != null)
            {
                if (special == WaterName) MoveTo(ctx, c, root);
                else
                {
                    if (special == CollidersGroup && IsFloorCollider(go)) ctx.FloorColliders.Add(c);
                    MoveTo(ctx, c, GetGroup(ctx, special));
                }
                continue;
            }

            if (!IsPlain(go))
            {
                // 그룹이 아닌 기타 오브젝트 — 표준 그룹 안이면 그대로, 아니면 Unsorted
                if (kept != null) MoveTo(ctx, c, kept);
                else { ctx.Unsorted.Add($"{go.name} (컴포넌트 {string.Join(",", go.GetComponents<Component>().Where(x => x != null && !(x is Transform)).Select(x => x.GetType().Name))})"); MoveTo(ctx, c, GetGroup(ctx, UnsortedGroup)); }
                continue;
            }

            if (c.childCount == 0) continue;   // 빈 오브젝트 — 나중에 지운다

            string key = go.name.Trim().ToLowerInvariant();
            if (KeptGroups.TryGetValue(key, out string std))
            {
                Transform g = GetGroup(ctx, std);
                if (g != c) Process(ctx, c, g);
                else { FixGroup(ctx, g); Process(ctx, c, g); }
            }
            else if (Containers.Contains(key) && (parent == root || kept == null))
            {
                if (IsStandardGroup(c) && PathOf(c, root) == (key == "environment" ? EnvRoot : PropsRoot)) FixGroup(ctx, c);
                Process(ctx, c, null);
            }
            else if (kept != null) MoveTo(ctx, c, kept);   // 표준 그룹 안의 사용자 하위 그룹(Molding 등)은 통째로
            else Process(ctx, c, null);                    // 모르는 그룹은 풀어서 분류
        }
    }

    static string ClassifySpecial(GameObject go)
    {
        if (go.GetComponent("ZoneWater") != null) return WaterName;
        if (go.GetComponent<Light>() != null) return LightingGroup;
        if (go.GetComponent("MonsterSpawnPoint") != null) return SpawnsGroup;
        if (go.GetComponent<Collider>() != null && (IsFloorCollider(go) || go.CompareTag(GameLayers.Wall) || go.CompareTag(GameLayers.Ground)))
            return CollidersGroup;
        return null;
    }

    static bool IsFloorCollider(GameObject go)
        => go.GetComponent<Collider>() != null && (go.name == "ZoneFloor" || go.name.StartsWith("FloorCollider") || go.CompareTag(GameLayers.Ground));

    static string ClassifyInstance(GameObject go)
    {
        string src = SourcePath(go);
        string file = System.IO.Path.GetFileNameWithoutExtension(src).ToLowerInvariant();
        string rel = src.StartsWith("Assets/2.Prefabs/Environment/") ? src.Substring("Assets/2.Prefabs/Environment/".Length) : src;

        if (file.Contains("fence")) return FenceGroup;
        if (file.Contains("pillar") || file.Contains("column")) return PillarGroup;
        if (rel.StartsWith("Architecture/Floors/")) return FloorGroup;
        if (rel.StartsWith("Architecture/Stairs/")) return StairGroup;
        if (rel.StartsWith("Architecture/Walls/") || rel.StartsWith("Architecture/Doors/")) return WallGroup;
        if (rel.StartsWith("Machinery/") || file.Contains("elevator") || file.Contains("crane") || file.Contains("conveyor") || file.Contains("convayor"))
            return MachineryGroup;
        if (rel.StartsWith("Pipes/") || file.Contains("pipe")) return PropsRoot + "/Pipes";
        if (rel.StartsWith("Props/") || rel.StartsWith("Lighting/Fixtures/"))
        {
            if (file.Contains("container")) return PropsRoot + "/Containers";
            if (file.Contains("barrel")) return PropsRoot + "/Barrels";
            if (file.Contains("pallet")) return PropsRoot + "/Pallets";
            if (file.Contains("chain")) return PropsRoot + "/Chains";
            if (file.Contains("box") || file.Contains("crate") || file.Contains("cardboard")) return PropsRoot + "/Boxes";
            return PropsRoot + "/Etc";
        }
        return UnsortedGroup;
    }

    static string SourcePath(GameObject go)
    {
        GameObject src = PrefabUtility.GetCorrespondingObjectFromSource(go);
        return src != null ? AssetDatabase.GetAssetPath(src) : "?";
    }

    // ─── 그룹 ──────────────────────────────────────────────────────────────

    static Transform GetGroup(Ctx ctx, string path)
    {
        Transform cur = ctx.Root.transform;
        foreach (string seg in path.Split('/'))
        {
            Transform found = cur.Cast<Transform>().FirstOrDefault(c => c.name == seg && IsStandardGroup(c));
            if (found == null)
            {
                found = new GameObject(seg).transform;
                found.SetParent(cur, false);
                ctx.Created.Add(found);
                ctx.Changes++;
            }
            else FixGroup(ctx, found);
            cur = found;
        }
        return cur;
    }

    static void FixGroup(Ctx ctx, Transform g)
    {
        if (g.gameObject.layer != 0) { ctx.Notes.Add($"{PathOf(g, ctx.Root.transform)} 레이어 {g.gameObject.layer} → 0"); g.gameObject.layer = 0; ctx.Changes++; }
        if (!g.CompareTag("Untagged")) { ctx.Notes.Add($"{PathOf(g, ctx.Root.transform)} 태그 {g.tag} → Untagged"); g.tag = "Untagged"; ctx.Changes++; }
    }

    static bool IsStandardGroup(Transform t)
        => IsPlain(t.gameObject)
           && t.localPosition.sqrMagnitude < Epsilon * Epsilon
           && Quaternion.Angle(t.localRotation, Quaternion.identity) < 0.01f
           && (t.localScale - Vector3.one).sqrMagnitude < Epsilon * Epsilon;

    static bool IsPlain(GameObject go)
        => !PrefabUtility.IsPartOfPrefabInstance(go) && go.GetComponents<Component>().All(c => c is Transform);

    static void MoveTo(Ctx ctx, Transform t, Transform dest)
    {
        if (t.parent == dest) return;
        t.SetParent(dest, true);
        if (!ctx.Received.TryGetValue(dest, out var list)) ctx.Received[dest] = list = new List<GameObject>();
        list.Add(t.gameObject);
        ctx.Changes++;
    }

    static void SortChildren(Ctx ctx, Transform parent, string[] order)
    {
        var kids = parent.Cast<Transform>().ToList();
        var sorted = kids
            .OrderBy(k => { int i = order == null ? -1 : System.Array.IndexOf(order, k.name); return i < 0 ? int.MaxValue : i; })
            .ThenBy(k => k.name, System.StringComparer.Ordinal)
            .ToList();   // OrderBy 는 안정 정렬 — 같은 이름은 원래 순서
        for (int i = 0; i < sorted.Count; i++)
        {
            if (sorted[i].GetSiblingIndex() == i) continue;
            sorted[i].SetSiblingIndex(i);
            ctx.Changes++;
        }
    }

    // ─── 보고 ──────────────────────────────────────────────────────────────

    static void AppendTree(StringBuilder sb, Transform root)
    {
        foreach (Transform a in root)
        {
            string off = a.gameObject.activeSelf ? "" : " (꺼짐)";
            if (!IsPlain(a.gameObject) || !a.Cast<Transform>().Any(IsGroupLike))
            {
                sb.AppendLine($"  ├ {a.name}{off} — {a.childCount}개");
                continue;
            }
            var parts = a.Cast<Transform>().GroupBy(IsGroupLike).SelectMany(g => g.Key
                ? g.Select(s => $"{s.name}{(s.gameObject.activeSelf ? "" : "(꺼짐)")}({s.childCount})")
                : new[] { $"기타 {g.Count()}개" });
            sb.AppendLine($"  ├ {a.name}{off} / {string.Join(" · ", parts)}");
        }
    }

    static bool IsGroupLike(Transform t) => IsPlain(t.gameObject) && t.childCount > 0;

    static bool InsideInstance(Transform t, Transform root)
    {
        for (Transform p = t.parent; p != null && p != root; p = p.parent)
            if (PrefabUtility.IsAnyPrefabInstanceRoot(p.gameObject)) return true;
        return false;
    }

    static int Depth(Transform t, Transform root)
    {
        int d = 0;
        for (Transform p = t; p != null && p != root; p = p.parent) d++;
        return d;
    }

    static string PathOf(Transform t, Transform root)
    {
        var names = new List<string>();
        for (Transform p = t; p != null && p != root; p = p.parent) names.Add(p.name);
        names.Reverse();
        return string.Join("/", names);
    }

    static void EditPrefab(string path, System.Func<GameObject, bool> edit, out string error)
    {
        error = null;
        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.assetPath == path)
        {
            if (stage.scene.isDirty)
            {
                error = "프리팹 모드에 저장 안 된 수정이 있다 — 저장하거나 닫고 다시 돌릴 것";
                return;
            }
            if (edit(stage.prefabContentsRoot))
            {
                PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, path);
                stage.ClearDirtiness();
            }
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (edit(root)) PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
