using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// FBX Variant → 래퍼 프리팹 변환 (할 일 40 — 사용자 승인 W1~W3).
//
// FBX 를 바로 쓰는 프리팹(루트가 FBX 프리팹 인스턴스인 Variant)을 아래 래퍼 구조로 그 자리에서(경로·guid 유지) 바꾼다.
//
//   object_xxx        빈 루트 — 같은 이름·항등 Transform(옛 루트 스케일이 1 이 아니면 그 스케일)·옛 루트 레이어/태그/Static
//   │                 옛 Variant 가 루트에 추가한 컴포넌트(콜라이더 등)는 여기에 그대로(원래 월드 위치 유지)
//   └ Model           원 FBX 인스턴스(항등) — 옛 Variant 의 FBX 오버라이드(머티리얼·그림자 등)를 그대로 옮긴다
//
//  · 루트 GameObject·Transform·루트 추가 컴포넌트에는 옛 Variant 의 로컬 fileID 를 그대로 준다(임시 경로에 저장한 뒤 내용 교체).
//    그래서 인스턴스의 위치·이름·활성·레이어 오버라이드, 루트 컴포넌트 제거(m_RemovedComponents)·추가는 끊기지 않는다.
//    렌더러·MeshFilter 는 Model 안으로 들어가 fileID 가 바뀌므로, 그쪽 오버라이드와 외부 참조는 스냅샷으로 복구한다.
//  · 영향 파일 = 이 에셋을 (중첩 포함) 쓰는 모든 .prefab/.unity. 씬은 Additive 로 열고 처리 후 저장·닫는다.
//    열린 씬이 dirty 거나 프리팹 스테이지가 열려 있거나 Play 중이면 멈춘다.
//  · 산출물: <레포 상위>/_lane-artifacts/<프로젝트 폴더>/fbx-wrapper/<에셋명>/ — backup/(바이트 백업)·before.json·after.json·compare.md.
//  · 지원 안 함(멈추고 보고): Variant 의 추가 GameObject·제거 컴포넌트/GameObject·FBX 자식에 추가된 컴포넌트.
// 🔴 .meta 를 건드리지 않는다. 콜라이더를 새로 추가하지 않는다.
public static class FbxWrapperConversion
{
    const string Tag = "[FbxWrapper]";
    const string ModelName = "Model";
    const float PosEpsilon = 0.0001f;
    const float BoundsEpsilon = 0.001f;
    const float AngleEpsilon = 0.01f;

    [MenuItem("Tools/Map/Authoring/Prefabs/FBX Variant → 래퍼 (선택 에셋)")]
    static void ConvertSelectedMenu() => RunSelected(dryRun: false);

    [MenuItem("Tools/Map/Authoring/Prefabs/FBX Variant → 래퍼 (선택 에셋, 조사·스냅샷만)")]
    static void SurveySelectedMenu() => RunSelected(dryRun: true);

    static void RunSelected(bool dryRun)
    {
        var paths = Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath)
                                        .Where(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)).ToList();
        if (paths.Count == 0) { Debug.LogWarning($"{Tag} 프로젝트 창에서 FBX Variant 프리팹을 선택하세요."); return; }
        foreach (string p in paths)
        {
            bool ok = Run(p, dryRun, out string report);
            if (ok) Debug.Log(report); else Debug.LogError(report);
        }
    }

    public static string ArtifactRoot
    {
        get
        {
            string project = Path.GetDirectoryName(Path.GetFullPath(Application.dataPath));
            return Path.Combine(Path.GetDirectoryName(project), "_lane-artifacts", Path.GetFileName(project), "fbx-wrapper");
        }
    }

    // ───────────────────────────── 실행 ─────────────────────────────

    public static bool Run(string assetPath, bool dryRun, out string report)
    {
        var log = new StringBuilder();
        log.AppendLine($"{Tag} {assetPath} — {(dryRun ? "조사·스냅샷만" : "변환")}");
        report = null;

        string blocked = CheckEditorState();
        if (blocked != null) { report = log.AppendLine("멈춤: " + blocked).ToString(); return false; }

        var oldRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        string unsupported = CheckConvertible(assetPath, oldRoot);
        if (unsupported != null) { report = log.AppendLine("멈춤: " + unsupported).ToString(); return false; }

        string assetName = Path.GetFileNameWithoutExtension(assetPath);
        string outDir = Path.Combine(ArtifactRoot, assetName);
        Directory.CreateDirectory(outDir);

        List<AffectedFile> files = FindAffectedFiles(assetPath);
        log.AppendLine($"영향 파일 {files.Count} (직접 {files.Count(f => f.Direct)})");
        foreach (var f in files) log.AppendLine($"  {(f.Direct ? "직접" : "중첩")} {f.Path}");

        // 스냅샷(전)
        var before = new Snapshot { asset = assetPath, phase = "before", time = DateTime.Now.ToString("s") };
        before.files.Add(SnapshotAsset(assetPath, wrapped: false));
        foreach (var f in files) before.files.Add(SnapshotFile(f, assetPath, wrapped: false));
        File.WriteAllText(Path.Combine(outDir, dryRun ? "survey-before.json" : "before.json"), JsonUtility.ToJson(before, true));
        log.AppendLine($"인스턴스 {before.files.Skip(1).Sum(f => f.instances.Count)} · 외부 참조 {before.files.Sum(f => f.refs.Count)}");
        if (dryRun) { report = log.AppendLine("산출물: " + outDir).ToString(); return true; }

        // 바이트 백업
        string backupDir = Path.Combine(outDir, "backup");
        if (Directory.Exists(backupDir) && Directory.EnumerateFileSystemEntries(backupDir).Any())
            backupDir = Path.Combine(outDir, "backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Backup(backupDir, new[] { assetPath, assetPath + ".meta" }.Concat(files.Select(f => f.Path)));
        log.AppendLine("백업: " + backupDir);

        string guidBefore = AssetDatabase.AssetPathToGUID(assetPath);
        string convertNote = ConvertAsset(assetPath);
        if (convertNote.StartsWith("실패")) { report = log.AppendLine(convertNote).ToString(); return false; }
        log.AppendLine(convertNote);
        if (AssetDatabase.AssetPathToGUID(assetPath) != guidBefore)
        { report = log.AppendLine($"멈춤: guid 가 바뀜 {guidBefore} → {AssetDatabase.AssetPathToGUID(assetPath)}").ToString(); return false; }

        // 인스턴스 복구 — 프리팹(의존 순) 먼저, 씬 나중
        foreach (var f in files)
        {
            FileSnap snap = before.files.First(s => s.path == f.Path);
            log.AppendLine(RestoreFile(f, snap, assetPath));
        }

        // 스냅샷(후) + 비교
        var after = new Snapshot { asset = assetPath, phase = "after", time = DateTime.Now.ToString("s") };
        after.files.Add(SnapshotAsset(assetPath, wrapped: true));
        foreach (var f in files) after.files.Add(SnapshotFile(f, assetPath, wrapped: true));
        File.WriteAllText(Path.Combine(outDir, "after.json"), JsonUtility.ToJson(after, true));

        bool ok = Compare(before, after, out string compareMd);
        File.WriteAllText(Path.Combine(outDir, "compare.md"), compareMd);
        log.AppendLine($"비교: {(ok ? "전부 일치" : "불일치 있음")} — {Path.Combine(outDir, "compare.md")}");
        report = log.ToString();
        return ok;
    }

    static string CheckEditorState()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "Play 모드";
        if (PrefabStageUtility.GetCurrentPrefabStage() != null) return "프리팹 스테이지가 열려 있음";
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (s.isDirty) return $"열린 씬이 dirty: {s.path}";
        }
        return null;
    }

    static string CheckConvertible(string assetPath, GameObject oldRoot)
    {
        if (oldRoot == null) return "에셋을 못 읽음";
        if (PrefabUtility.GetPrefabAssetType(oldRoot) != PrefabAssetType.Variant) return "Variant 가 아님(이미 래퍼이거나 일반 프리팹)";
        GameObject src = PrefabUtility.GetCorrespondingObjectFromSource(oldRoot);
        if (src == null || PrefabUtility.GetPrefabAssetType(src) != PrefabAssetType.Model) return "루트가 FBX(Model) 프리팹 인스턴스가 아님";

        GameObject contents = PrefabUtility.LoadPrefabContents(assetPath);
        try
        {
            var problems = new List<string>();
            if (PrefabUtility.GetAddedGameObjects(contents).Count > 0) problems.Add("추가 GameObject");
            if (PrefabUtility.GetRemovedComponents(contents).Count > 0) problems.Add("제거 컴포넌트");
            if (PrefabUtility.GetRemovedGameObjects(contents).Count > 0) problems.Add("제거 GameObject");
            if (PrefabUtility.GetAddedComponents(contents).Any(a => a.instanceComponent.gameObject != contents)) problems.Add("FBX 자식에 추가 컴포넌트");
            return problems.Count > 0 ? "지원 안 하는 Variant 오버라이드: " + string.Join(", ", problems) : null;
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
    }

    // ───────────────────────────── 영향 파일 ─────────────────────────────

    class AffectedFile
    {
        public string Path;
        public bool Direct;
        public bool IsScene => Path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);
    }

    static List<AffectedFile> FindAffectedFiles(string assetPath)
    {
        string[] all = AssetDatabase.FindAssets("t:Prefab t:Scene", new[] { "Assets" })
                                    .Select(AssetDatabase.GUIDToAssetPath).Distinct().ToArray();
        var users = new Dictionary<string, List<string>>(); // 의존 대상 → 그것을 직접 쓰는 파일
        foreach (string p in all)
            foreach (string d in AssetDatabase.GetDependencies(p, false))
            {
                if (d == p) continue;
                if (!users.TryGetValue(d, out var list)) users[d] = list = new List<string>();
                list.Add(p);
            }

        // 너비 우선 = 의존 순서(안쪽 프리팹 먼저)
        var result = new List<AffectedFile>();
        var seen = new HashSet<string> { assetPath };
        var queue = new Queue<string>();
        queue.Enqueue(assetPath);
        while (queue.Count > 0)
        {
            string cur = queue.Dequeue();
            if (!users.TryGetValue(cur, out var list)) continue;
            foreach (string u in list.OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!seen.Add(u)) continue;
                result.Add(new AffectedFile { Path = u, Direct = cur == assetPath });
                queue.Enqueue(u);
            }
        }
        // 프리팹이 씬보다 먼저, 같은 종류 안에서는 위 순서 유지
        return result.Where(f => !f.IsScene).Concat(result.Where(f => f.IsScene)).ToList();
    }

    static void Backup(string dir, IEnumerable<string> assetPaths)
    {
        string project = Path.GetDirectoryName(Path.GetFullPath(Application.dataPath));
        foreach (string p in assetPaths.Distinct())
        {
            string dst = Path.Combine(dir, p.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(Path.Combine(project, p), dst, true);
        }
    }

    // ───────────────────────────── 에셋 변환 ─────────────────────────────

    static readonly string[] RootTransformProps = { "m_LocalPosition", "m_LocalRotation", "m_LocalEulerAnglesHint", "m_LocalScale" };

    static string ConvertAsset(string assetPath)
    {
        var oldRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        GameObject fbxRoot = PrefabUtility.GetCorrespondingObjectFromSource(oldRoot);
        var note = new StringBuilder();

        // 옛 로컬 fileID — 루트 GO·Transform·루트 추가 컴포넌트
        var idMap = new List<(Func<GameObject, UnityEngine.Object> pick, long oldId)>();
        idMap.Add((g => g, LocalId(oldRoot)));
        idMap.Add((g => g.transform, LocalId(oldRoot.transform)));
        var addedOnRoot = oldRoot.GetComponents<Component>()
                                 .Where(c => c != null && !(c is Transform) && PrefabUtility.GetCorrespondingObjectFromSource(c) == null)
                                 .ToList();

        // 옛 Variant 의 FBX 오버라이드 — 루트 위치·회전·스케일·이름·활성은 래퍼 루트 몫이라 뺀다.
        var oldMods = (PrefabUtility.GetPropertyModifications(oldRoot) ?? new PropertyModification[0])
            .Where(m => !(m.target == fbxRoot.transform && RootTransformProps.Any(p => m.propertyPath.StartsWith(p, StringComparison.Ordinal))))
            .Where(m => !(m.target == fbxRoot && (m.propertyPath == "m_Name" || m.propertyPath == "m_IsActive")))
            .ToList();

        // 임시 파일도 같은 파일 이름이어야 루트 m_Name 이 그대로다(SaveAsPrefabAsset 은 루트 이름을 파일 이름으로 바꾼다).
        Scene preview = EditorSceneManager.NewPreviewScene();
        string tempFolder = AssetDatabase.GenerateUniqueAssetPath("Assets/__FbxWrapperTmp");
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(tempFolder));
        string tempPath = tempFolder + "/" + Path.GetFileName(assetPath);
        try
        {
            var root = new GameObject(oldRoot.name);
            SceneManager.MoveGameObjectToScene(root, preview);
            root.layer = oldRoot.layer;
            root.tag = oldRoot.tag;
            GameObjectUtility.SetStaticEditorFlags(root, GameObjectUtility.GetStaticEditorFlags(oldRoot));
            root.transform.localScale = oldRoot.transform.localScale;
            if ((oldRoot.transform.localScale - Vector3.one).sqrMagnitude > 1e-10f)
                note.AppendLine($"  루트 스케일 {oldRoot.transform.localScale} 을 래퍼 루트에 유지(인스턴스가 스케일을 오버라이드하지 않으면 그 값이 쓰였음)");

            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbxRoot, preview);
            model.transform.SetParent(root.transform, false);

            var mods = (PrefabUtility.GetPropertyModifications(model) ?? new PropertyModification[0]).ToList();
            foreach (var m in oldMods)
            {
                mods.RemoveAll(x => x.target == m.target && x.propertyPath == m.propertyPath);
                mods.Add(m);
                note.AppendLine($"  Model 오버라이드: {m.target.name}.{m.propertyPath} = {(m.objectReference != null ? m.objectReference.name : m.value)}");
            }
            PrefabUtility.SetPropertyModifications(model, mods.ToArray());

            // 이름·Transform 은 오버라이드 목록을 넣은 뒤에 정한다(SetPropertyModifications 가 앞선 변경을 덮는다).
            model.name = ModelName;
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            PrefabUtility.RecordPrefabInstancePropertyModifications(model);
            PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);

            var newAdded = new List<Component>();
            foreach (Component c in addedOnRoot)
            {
                ComponentUtility.CopyComponent(c);
                ComponentUtility.PasteComponentAsNew(root);
                Component pasted = root.GetComponents(c.GetType()).Last();
                newAdded.Add(pasted);
                note.AppendLine($"  루트 유지: {c.GetType().Name} (원래 FBX 루트 = 지금 래퍼 루트·Model 항등 → 월드 위치 같음)");
            }
            for (int i = 0; i < addedOnRoot.Count; i++)
            {
                int k = i;
                idMap.Add((g => g.GetComponents(addedOnRoot[k].GetType())[addedOnRoot.Take(k).Count(x => x.GetType() == addedOnRoot[k].GetType())], LocalId(addedOnRoot[k])));
            }

            PrefabUtility.SaveAsPrefabAsset(root, tempPath, out bool saved);
            if (!saved) return "실패: 임시 프리팹 저장 실패";

            // 임시 파일의 새 fileID → 옛 fileID 로 바꾸고 원래 파일 내용을 교체(.meta·guid 유지)
            var tempRoot = AssetDatabase.LoadAssetAtPath<GameObject>(tempPath);
            string project = Path.GetDirectoryName(Path.GetFullPath(Application.dataPath));
            string text = File.ReadAllText(Path.Combine(project, tempPath));
            foreach (var (pick, oldId) in idMap)
            {
                long newId = LocalId(pick(tempRoot));
                if (Regex.IsMatch(text, $@"(?<=&|fileID: ){oldId}\b")) return $"실패: 옛 fileID {oldId} 가 새 파일에 이미 있음";
                text = Regex.Replace(text, $@"(?<=&|fileID: ){newId}\b", oldId.ToString());
            }
            File.WriteAllText(Path.Combine(project, assetPath), text);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
            AssetDatabase.DeleteAsset(tempFolder);
        }
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

        var converted = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (converted == null || PrefabUtility.GetPrefabAssetType(converted) != PrefabAssetType.Regular || converted.transform.Find(ModelName) == null)
            return "실패: 변환 결과를 다시 읽지 못함";
        return "에셋 변환 완료\n" + note.ToString().TrimEnd();
    }

    static long LocalId(UnityEngine.Object o)
    {
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string _, out long id);
        return id;
    }

    // ───────────────────────────── 스냅샷 ─────────────────────────────

    [Serializable] class Snapshot { public string asset; public string phase; public string time; public List<FileSnap> files = new List<FileSnap>(); }

    [Serializable] class FileSnap
    {
        public string path; public bool direct;
        public int missingRefs; public int missingScripts;
        public List<InstSnap> instances = new List<InstSnap>();
        public List<RefSnap> refs = new List<RefSnap>();
    }

    [Serializable] class InstSnap
    {
        public string path; public string name; public bool outermost;
        public Vector3 worldPosition; public Quaternion worldRotation; public Vector3 lossyScale; public Vector3 localScale;
        public bool activeSelf; public bool activeInHierarchy; public int layer; public string tag;
        public List<RendSnap> renderers = new List<RendSnap>();
        public List<ColSnap> colliders = new List<ColSnap>();
        public List<string> components = new List<string>(); // "rel|Type" — rel 은 옛 구조 기준
        public List<string> children = new List<string>();
    }

    [Serializable] class RendSnap
    {
        public string rel; public string type; public List<string> materials = new List<string>();
        public int layer; public string castShadows; public bool receiveShadows; public bool enabled;
        public bool activeSelf; public bool activeInHierarchy; public Vector3 center; public Vector3 size;
    }

    [Serializable] class ColSnap { public string rel; public string type; public bool enabled; public bool isTrigger; public Vector3 center; public Vector3 size; }

    [Serializable] class RefSnap
    {
        public string referrer; public string componentType; public int componentIndex; public string propertyPath;
        public string instancePath; public string targetRel; public string targetType; public int targetIndex;
    }

    static FileSnap SnapshotAsset(string assetPath, bool wrapped)
    {
        var fs = new FileSnap { path = assetPath + " (에셋 자체)", direct = true };
        GameObject contents = PrefabUtility.LoadPrefabContents(assetPath);
        try
        {
            // 에셋 루트 위치는 인스턴스가 늘 덮어쓴다 — 루트 기준으로 비교한다.
            contents.transform.localPosition = Vector3.zero;
            contents.transform.localRotation = Quaternion.identity;
            fs.instances.Add(SnapInstance(contents.transform, "<asset>", wrapped));
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        return fs;
    }

    static FileSnap SnapshotFile(AffectedFile f, string assetPath, bool wrapped)
    {
        var fs = new FileSnap { path = f.Path, direct = f.Direct };
        WithFile(f, roots =>
        {
            var instances = FindInstances(roots, assetPath);
            var owner = new Dictionary<Transform, (Transform root, string path)>();
            foreach (Transform r in instances)
            {
                string p = OuterPath(r);
                fs.instances.Add(SnapInstance(r, p, wrapped));
                foreach (Transform t in r.GetComponentsInChildren<Transform>(true)) owner[t] = (r, p);
            }
            ScanReferences(roots, owner, wrapped, fs);
            return false;
        });
        return fs;
    }

    static InstSnap SnapInstance(Transform r, string path, bool wrapped)
    {
        var s = new InstSnap
        {
            path = path, name = r.name, outermost = PrefabUtility.IsOutermostPrefabInstanceRoot(r.gameObject),
            worldPosition = r.position, worldRotation = r.rotation, lossyScale = r.lossyScale, localScale = r.localScale,
            activeSelf = r.gameObject.activeSelf, activeInHierarchy = r.gameObject.activeInHierarchy,
            layer = r.gameObject.layer, tag = r.tag,
        };
        foreach (Transform t in r.GetComponentsInChildren<Transform>(true))
        {
            string rel = RelOld(r, t, wrapped);
            if (wrapped && t == ModelOf(r)) { /* Model 자체는 옛 루트와 합친다 */ }
            else if (t != r) s.children.Add(rel);

            foreach (Component c in t.GetComponents<Component>())
            {
                if (c is Transform) continue;
                s.components.Add(rel + "|" + (c == null ? "<Missing>" : c.GetType().Name));
                if (c is Renderer rd)
                {
                    Bounds b = WorldBounds(rd);
                    var rs = new RendSnap
                    {
                        rel = rel, type = rd.GetType().Name, layer = t.gameObject.layer, castShadows = rd.shadowCastingMode.ToString(),
                        receiveShadows = rd.receiveShadows, enabled = rd.enabled, activeInHierarchy = t.gameObject.activeInHierarchy,
                        activeSelf = rel.Length == 0 ? r.gameObject.activeSelf : t.gameObject.activeSelf,
                        center = b.center, size = b.size,
                    };
                    foreach (Material m in rd.sharedMaterials) rs.materials.Add(m == null ? "null" : AssetDatabase.GetAssetPath(m) + ":" + m.name);
                    s.renderers.Add(rs);
                }
                else if (c is Collider col)
                {
                    Bounds b = WorldBounds(col);
                    s.colliders.Add(new ColSnap { rel = rel, type = col.GetType().Name, enabled = col.enabled, isTrigger = col.isTrigger, center = b.center, size = b.size });
                }
            }
        }
        s.components.Sort(StringComparer.Ordinal);
        s.children.Sort(StringComparer.Ordinal);
        s.renderers = s.renderers.OrderBy(x => x.rel, StringComparer.Ordinal).ThenBy(x => x.type).ToList();
        s.colliders = s.colliders.OrderBy(x => x.rel, StringComparer.Ordinal).ThenBy(x => x.type).ToList();
        return s;
    }

    static void ScanReferences(List<GameObject> roots, Dictionary<Transform, (Transform root, string path)> owner, bool wrapped, FileSnap fs)
    {
        foreach (GameObject go in roots)
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                fs.missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                bool inside = owner.ContainsKey(t);
                var comps = t.GetComponents<Component>();
                foreach (Component c in comps)
                {
                    if (c == null || c is Transform) continue;
                    var so = new SerializedObject(c);
                    SerializedProperty it = so.GetIterator();
                    while (it.Next(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                        UnityEngine.Object v = it.objectReferenceValue;
                        if (v == null)
                        {
                            if (it.objectReferenceInstanceIDValue != 0) fs.missingRefs++;
                            continue;
                        }
                        if (inside) continue; // 인스턴스 내부 참조는 에셋 몫
                        Transform vt = v is Component vc ? vc.transform : v is GameObject vg ? vg.transform : null;
                        if (vt == null || !owner.TryGetValue(vt, out var o)) continue;
                        fs.refs.Add(new RefSnap
                        {
                            referrer = OuterPath(t), componentType = c.GetType().Name, componentIndex = IndexOfType(comps, c),
                            propertyPath = it.propertyPath, instancePath = o.path, targetRel = TargetRelOld(o.root, v, wrapped),
                            targetType = v.GetType().Name, targetIndex = v is Component tc ? IndexOfType(tc.GetComponents<Component>(), tc) : 0,
                        });
                    }
                }
            }
    }

    // 옛 구조 기준 위치. 래퍼 루트의 컴포넌트와 Model 의 컴포넌트는 둘 다 옛 루트("")다.
    static string TargetRelOld(Transform root, UnityEngine.Object v, bool wrapped)
    {
        Transform t = v is Component c ? c.transform : ((GameObject)v).transform;
        return RelOld(root, t, wrapped);
    }

    // ───────────────────────────── 복구 ─────────────────────────────

    static string RestoreFile(AffectedFile f, FileSnap snap, string assetPath)
    {
        var log = new StringBuilder();
        int changes = 0;
        WithFile(f, roots =>
        {
            foreach (InstSnap s in snap.instances)
            {
                Transform r = FindOuter(roots, s.path);
                if (r == null) { log.AppendLine($"    ✗ 인스턴스 못 찾음 {s.path}"); continue; }
                changes += RestoreInstance(r, s, log);
            }
            foreach (RefSnap rs in snap.refs) changes += RestoreReference(roots, rs, log);

            // 직접(바깥) 인스턴스의 옛 렌더러 오버라이드는 이제 대상이 없다 — 정리
            foreach (InstSnap s in snap.instances.Where(x => x.outermost))
            {
                Transform r = FindOuter(roots, s.path);
                int unused = r == null ? 0 : (PrefabUtility.GetPropertyModifications(r.gameObject) ?? new PropertyModification[0]).Count(m => m.target == null);
                if (unused == 0) continue;
                PrefabUtility.RemoveUnusedOverrides(new[] { r.gameObject }, InteractionMode.AutomatedAction);
                log.AppendLine($"    쓰이지 않는 오버라이드 {unused} 정리 {s.path}");
                changes++;
            }
            return changes > 0;
        });
        return $"  복구 {f.Path}: 변경 {changes}\n{log}".TrimEnd();
    }

    static int RestoreInstance(Transform r, InstSnap s, StringBuilder log)
    {
        int n = 0;
        if ((r.position - s.worldPosition).magnitude > PosEpsilon) { r.position = s.worldPosition; n++; log.AppendLine($"    {s.path} 월드 위치"); }
        if (Quaternion.Angle(r.rotation, s.worldRotation) > AngleEpsilon) { r.rotation = s.worldRotation; n++; log.AppendLine($"    {s.path} 월드 회전"); }
        if ((r.localScale - s.localScale).magnitude > PosEpsilon) { r.localScale = s.localScale; n++; log.AppendLine($"    {s.path} 스케일"); }
        if (r.gameObject.activeSelf != s.activeSelf) { r.gameObject.SetActive(s.activeSelf); n++; log.AppendLine($"    {s.path} 활성"); }
        if (r.gameObject.layer != s.layer) { r.gameObject.layer = s.layer; n++; log.AppendLine($"    {s.path} 레이어"); }
        if (r.tag != s.tag) { r.tag = s.tag; n++; log.AppendLine($"    {s.path} 태그"); }
        if (r.name != s.name) { r.name = s.name; n++; log.AppendLine($"    {s.path} 이름"); }

        foreach (RendSnap rs in s.renderers)
        {
            Transform t = FindRelNew(r, rs.rel);
            Renderer rd = t != null ? t.GetComponents<Renderer>().FirstOrDefault(x => x.GetType().Name == rs.type) : null;
            if (rd == null) { log.AppendLine($"    ✗ {s.path} 렌더러 못 찾음 '{rs.rel}' {rs.type}"); continue; }
            var mats = rs.materials.Select(LoadMaterial).ToArray();
            if (!rd.sharedMaterials.SequenceEqual(mats)) { rd.sharedMaterials = mats; n++; log.AppendLine($"    {s.path} '{rs.rel}' 머티리얼"); }
            if (rd.shadowCastingMode.ToString() != rs.castShadows) { rd.shadowCastingMode = (ShadowCastingMode)Enum.Parse(typeof(ShadowCastingMode), rs.castShadows); n++; log.AppendLine($"    {s.path} '{rs.rel}' CastShadows"); }
            if (rd.receiveShadows != rs.receiveShadows) { rd.receiveShadows = rs.receiveShadows; n++; log.AppendLine($"    {s.path} '{rs.rel}' ReceiveShadows"); }
            if (rd.enabled != rs.enabled) { rd.enabled = rs.enabled; n++; log.AppendLine($"    {s.path} '{rs.rel}' 렌더러 enabled"); }
            if (t.gameObject.layer != rs.layer) { t.gameObject.layer = rs.layer; n++; log.AppendLine($"    {s.path} '{rs.rel}' 레이어"); }
            bool wantActive = rs.rel.Length == 0 || rs.activeSelf; // 옛 루트 렌더러의 활성은 래퍼 루트가 갖는다
            if (t.gameObject.activeSelf != wantActive) { t.gameObject.SetActive(wantActive); n++; log.AppendLine($"    {s.path} '{rs.rel}' 활성"); }
        }

        // 인스턴스에서 지웠던 FBX 컴포넌트(옛 루트 쪽)가 Model 에 되살아났으면 다시 지운다.
        var want = s.components.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        foreach (Transform t in r.GetComponentsInChildren<Transform>(true))
        {
            string rel = RelOld(r, t, wrapped: true);
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                string key = rel + "|" + c.GetType().Name;
                if (want.TryGetValue(key, out int left) && left > 0) { want[key] = left - 1; continue; }
                Component origin = PrefabUtility.GetCorrespondingObjectFromOriginalSource(c);
                if (origin == null || PrefabUtility.GetPrefabAssetType(origin.gameObject) != PrefabAssetType.Model)
                { log.AppendLine($"    ✗ {s.path} 예상 밖 컴포넌트 {key}(FBX 원본 아님 — 손대지 않음)"); continue; }
                try { UnityEngine.Object.DestroyImmediate(c); n++; log.AppendLine($"    {s.path} 제거 오버라이드 복구 {key}"); }
                catch (Exception e) { log.AppendLine($"    ✗ {s.path} 제거 오버라이드 못 옮김 {key}: {e.Message}"); }
            }
        }
        foreach (var kv in want.Where(kv => kv.Value > 0)) log.AppendLine($"    ✗ {s.path} 컴포넌트 없음 {kv.Key} ×{kv.Value}");
        return n;
    }

    static int RestoreReference(List<GameObject> roots, RefSnap rs, StringBuilder log)
    {
        Component c = FindComponent(roots, rs.referrer, rs.componentType, rs.componentIndex);
        Transform inst = FindOuter(roots, rs.instancePath);
        if (c == null || inst == null) { log.AppendLine($"    ✗ 참조 복구 실패(대상 없음) {rs.referrer}.{rs.componentType}.{rs.propertyPath}"); return 0; }
        UnityEngine.Object want = ResolveTarget(inst, rs);
        if (want == null) { log.AppendLine($"    ✗ 참조 대상 못 찾음 {rs.referrer}.{rs.propertyPath} → '{rs.targetRel}' {rs.targetType}"); return 0; }
        var so = new SerializedObject(c);
        SerializedProperty p = so.FindProperty(rs.propertyPath);
        if (p == null) { log.AppendLine($"    ✗ 프로퍼티 없음 {rs.referrer}.{rs.componentType}.{rs.propertyPath}"); return 0; }
        if (p.objectReferenceValue == want) return 0;
        p.objectReferenceValue = want;
        so.ApplyModifiedPropertiesWithoutUndo();
        log.AppendLine($"    참조 재연결 {rs.referrer}.{rs.componentType}.{rs.propertyPath} → '{rs.targetRel}' {rs.targetType}");
        return 1;
    }

    static UnityEngine.Object ResolveTarget(Transform inst, RefSnap rs)
    {
        // 옛 루트("")의 GameObject/Transform = 래퍼 루트. 옛 루트의 컴포넌트는 래퍼 루트에 남은 것 먼저, 없으면 Model.
        var candidates = new List<Transform>();
        if (rs.targetRel.Length == 0)
        {
            if (rs.targetType == nameof(GameObject)) return inst.gameObject;
            if (rs.targetType == nameof(Transform)) return inst;
            candidates.Add(inst);
            if (ModelOf(inst) != null) candidates.Add(ModelOf(inst));
        }
        else
        {
            Transform t = FindRelNew(inst, rs.targetRel);
            if (t == null) return null;
            if (rs.targetType == nameof(GameObject)) return t.gameObject;
            candidates.Add(t);
        }
        foreach (Transform t in candidates)
        {
            var same = t.GetComponents<Component>().Where(x => x != null && x.GetType().Name == rs.targetType).ToList();
            if (same.Count > 0) return same[Mathf.Min(rs.targetIndex, same.Count - 1)];
        }
        return null;
    }

    static Material LoadMaterial(string key)
    {
        if (key == "null") return null;
        int colon = key.LastIndexOf(':');
        string path = key.Substring(0, colon), name = key.Substring(colon + 1);
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().FirstOrDefault(m => m.name == name);
    }

    // ───────────────────────────── 비교 ─────────────────────────────

    static bool Compare(Snapshot before, Snapshot after, out string md)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# FBX 래퍼 변환 비교 — {before.asset}");
        sb.AppendLine();
        sb.AppendLine($"- 전: {before.time} / 후: {after.time}");
        sb.AppendLine($"- 기준: 렌더러·콜라이더 월드 bounds {BoundsEpsilon * 1000:F0}mm, 위치 {PosEpsilon}m, 회전 {AngleEpsilon}°. 경로(rel)는 옛 구조 기준(Model 은 옛 루트와 같은 자리).");
        sb.AppendLine();
        sb.AppendLine("| 파일 | 인스턴스 | 루트 Transform | 활성 | 렌더러 수 전→후 | bounds Δmax(mm) | 머티리얼 | 레이어 | 그림자 | 콜라이더 | 컴포넌트 | 결과 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        bool allOk = true;
        var notes = new List<string>();

        foreach (FileSnap bf in before.files)
        {
            FileSnap af = after.files.FirstOrDefault(x => x.path == bf.path);
            if (af == null) { allOk = false; notes.Add($"{bf.path}: 후 스냅샷 없음"); continue; }
            foreach (InstSnap bi in bf.instances)
            {
                InstSnap ai = af.instances.FirstOrDefault(x => x.path == bi.path);
                if (ai == null) { allOk = false; sb.AppendLine($"| {Short(bf.path)} | {bi.name} | ✗ 없음 | | | | | | | | | ✗ |"); continue; }
                bool tr = (bi.worldPosition - ai.worldPosition).magnitude <= PosEpsilon && Quaternion.Angle(bi.worldRotation, ai.worldRotation) <= AngleEpsilon
                          && (bi.lossyScale - ai.lossyScale).magnitude <= PosEpsilon;
                bool act = bi.activeSelf == ai.activeSelf && bi.activeInHierarchy == ai.activeInHierarchy
                           && bi.renderers.Count == ai.renderers.Count && bi.renderers.Zip(ai.renderers, (x, y) => x.activeInHierarchy == y.activeInHierarchy && x.enabled == y.enabled).All(b => b);
                bool sameRend = bi.renderers.Select(x => x.rel + "|" + x.type).SequenceEqual(ai.renderers.Select(x => x.rel + "|" + x.type));
                float dmax = 0f;
                bool mat = sameRend, lay = sameRend && bi.layer == ai.layer, sh = sameRend;
                if (sameRend)
                    for (int i = 0; i < bi.renderers.Count; i++)
                    {
                        RendSnap x = bi.renderers[i], y = ai.renderers[i];
                        dmax = Mathf.Max(dmax, MaxAbs(x.center - y.center), MaxAbs(x.size - y.size));
                        mat &= x.materials.SequenceEqual(y.materials);
                        lay &= x.layer == y.layer;
                        sh &= x.castShadows == y.castShadows && x.receiveShadows == y.receiveShadows;
                    }
                bool col = bi.colliders.Count == ai.colliders.Count && bi.colliders.Zip(ai.colliders, (x, y) =>
                    x.rel == y.rel && x.type == y.type && x.enabled == y.enabled && x.isTrigger == y.isTrigger
                    && MaxAbs(x.center - y.center) <= BoundsEpsilon && MaxAbs(x.size - y.size) <= BoundsEpsilon).All(b => b);
                bool comp = bi.components.SequenceEqual(ai.components) && bi.children.SequenceEqual(ai.children);
                bool ok = tr && act && sameRend && dmax <= BoundsEpsilon && mat && lay && sh && col && comp;
                allOk &= ok;
                sb.AppendLine($"| {Short(bf.path)} | {bi.name} | {Mark(tr)} | {Mark(act)} | {bi.renderers.Count}→{ai.renderers.Count} | {dmax * 1000f:F2} | {Mark(mat)} | {Mark(lay)} | {Mark(sh)} | {bi.colliders.Count}→{ai.colliders.Count} {Mark(col)} | {Mark(comp)} | {Mark(ok)} |");
                if (!comp)
                    notes.Add($"{bf.path} {bi.path}: 컴포넌트/자식 차이 — 전 [{string.Join(", ", bi.components.Concat(bi.children))}] / 후 [{string.Join(", ", ai.components.Concat(ai.children))}]");
            }
            if (af.instances.Count != bf.instances.Count) { allOk = false; notes.Add($"{bf.path}: 인스턴스 수 {bf.instances.Count}→{af.instances.Count}"); }

            // 외부 참조: 같은 참조자·프로퍼티가 같은 옛 위치를 가리키는지
            foreach (RefSnap br in bf.refs)
            {
                bool found = af.refs.Any(ar => ar.referrer == br.referrer && ar.componentType == br.componentType && ar.componentIndex == br.componentIndex
                                               && ar.propertyPath == br.propertyPath && ar.instancePath == br.instancePath
                                               && ar.targetRel == br.targetRel && ar.targetType == br.targetType);
                allOk &= found;
                notes.Add($"{bf.path}: 참조 {br.referrer}.{br.componentType}.{br.propertyPath} → {br.instancePath} '{br.targetRel}' {br.targetType} {Mark(found)}");
            }
            bool missingOk = af.missingRefs <= bf.missingRefs && af.missingScripts <= bf.missingScripts;
            allOk &= missingOk;
            notes.Add($"{bf.path}: Missing 참조 {bf.missingRefs}→{af.missingRefs}, Missing 스크립트 {bf.missingScripts}→{af.missingScripts} {Mark(missingOk)}");
        }

        sb.AppendLine();
        sb.AppendLine("## 파일별 참조·Missing");
        sb.AppendLine();
        foreach (string n in notes) sb.AppendLine("- " + n);
        sb.AppendLine();
        sb.AppendLine($"**결과: {(allOk ? "전부 일치" : "불일치 있음")}**");
        md = sb.ToString();
        return allOk;
    }

    static string Mark(bool ok) => ok ? "✓" : "✗";
    static string Short(string path) => Path.GetFileName(path);
    static float MaxAbs(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    // ───────────────────────────── 파일·경로 도우미 ─────────────────────────────

    // 파일을 열어 roots 를 넘기고, body 가 true 를 돌려주면 저장한다.
    static void WithFile(AffectedFile f, Func<List<GameObject>, bool> body)
    {
        if (!f.IsScene)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(f.Path);
            try
            {
                if (body(new List<GameObject> { contents }))
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, f.Path, out bool ok);
                    if (!ok) Debug.LogError($"{Tag} 저장 실패 {f.Path}");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            return;
        }

        Scene scene = SceneManager.GetSceneByPath(f.Path);
        bool wasOpen = scene.IsValid() && scene.isLoaded;
        if (!wasOpen) scene = EditorSceneManager.OpenScene(f.Path, OpenSceneMode.Additive);
        try
        {
            if (body(scene.GetRootGameObjects().ToList())) EditorSceneManager.SaveScene(scene);
        }
        finally { if (!wasOpen) EditorSceneManager.CloseScene(scene, true); }
    }

    static List<Transform> FindInstances(List<GameObject> roots, string assetPath)
    {
        var result = new List<Transform>();
        foreach (GameObject go in roots)
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) continue;
                for (GameObject s = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject); s != null; s = PrefabUtility.GetCorrespondingObjectFromSource(s))
                    if (s.transform.parent == null && AssetDatabase.GetAssetPath(s) == assetPath) { result.Add(t); break; }
            }
        return result;
    }

    static Transform ModelOf(Transform root)
    {
        Transform m = root.Find(ModelName);
        return m != null && PrefabUtility.IsAnyPrefabInstanceRoot(m.gameObject) ? m : null;
    }

    // 파일 루트부터 "이름#형제순서/…" — 인스턴스 바깥 경로라 변환 전후로 같다.
    static string OuterPath(Transform t)
    {
        var segs = new List<string>();
        for (Transform c = t; c != null; c = c.parent) segs.Add(c.name + "#" + c.GetSiblingIndex());
        segs.Reverse();
        return string.Join("/", segs);
    }

    static Transform FindOuter(List<GameObject> roots, string path)
    {
        string[] segs = path.Split('/');
        Transform cur = null;
        foreach (string seg in segs)
        {
            int hash = seg.LastIndexOf('#');
            string name = seg.Substring(0, hash);
            int idx = int.Parse(seg.Substring(hash + 1));
            if (cur == null)
                cur = roots.Select(g => g.transform).FirstOrDefault(t => t.name == name && t.GetSiblingIndex() == idx)
                      ?? roots.Select(g => g.transform).FirstOrDefault(t => t.name == name);
            else
            {
                Transform next = idx < cur.childCount && cur.GetChild(idx).name == name ? cur.GetChild(idx) : null;
                if (next == null)
                    foreach (Transform ch in cur) if (ch.name == name) { next = ch; break; }
                cur = next;
            }
            if (cur == null) return null;
        }
        return cur;
    }

    static Component FindComponent(List<GameObject> roots, string path, string type, int index)
    {
        Transform t = FindOuter(roots, path);
        if (t == null) return null;
        var same = t.GetComponents<Component>().Where(c => c != null && c.GetType().Name == type).ToList();
        return index < same.Count ? same[index] : null;
    }

    static int IndexOfType(Component[] comps, Component c)
    {
        int i = 0;
        foreach (Component x in comps)
        {
            if (x == c) return i;
            if (x != null && x.GetType() == c.GetType()) i++;
        }
        return 0;
    }

    // 인스턴스 루트 기준 상대 경로(이름, 같은 이름 형제는 ~k). wrapped 면 Model 단계를 빼서 옛 구조 기준으로 맞춘다.
    static string RelOld(Transform root, Transform t, bool wrapped)
    {
        var segs = new List<string>();
        Transform model = wrapped ? ModelOf(root) : null;
        for (Transform c = t; c != null && c != root; c = c.parent)
        {
            if (c == model) continue;
            segs.Add(Seg(c));
        }
        segs.Reverse();
        return string.Join("/", segs);
    }

    static string Seg(Transform c)
    {
        int k = 0;
        if (c.parent != null)
            for (int i = 0; i < c.GetSiblingIndex(); i++) if (c.parent.GetChild(i).name == c.name) k++;
        return k == 0 ? c.name : c.name + "~" + k;
    }

    // 옛 구조 기준 rel → 새 구조의 Transform. "" = Model(옛 루트 렌더러 자리). 그 밖은 Model 아래 먼저, 없으면 래퍼 루트 아래.
    static Transform FindRelNew(Transform root, string rel)
    {
        Transform model = ModelOf(root);
        if (rel.Length == 0) return model != null ? model : root;
        return (model != null ? WalkRel(model, rel) : null) ?? WalkRel(root, rel);
    }

    static Transform WalkRel(Transform from, string rel)
    {
        Transform cur = from, model = ModelOf(from);
        foreach (string seg in rel.Split('/'))
        {
            Transform next = null;
            foreach (Transform ch in cur)
                if (ch != model && Seg(ch) == seg) { next = ch; break; }
            if (next == null) return null;
            cur = next;
        }
        return cur;
    }

    static Bounds WorldBounds(Renderer r)
    {
        var mf = r.GetComponent<MeshFilter>();
        if (r is MeshRenderer && mf != null && mf.sharedMesh != null) return Transformed(mf.sharedMesh.bounds, r.transform.localToWorldMatrix);
        return r.bounds;
    }

    static Bounds WorldBounds(Collider c)
    {
        switch (c)
        {
            case BoxCollider b: return Transformed(new Bounds(b.center, b.size), c.transform.localToWorldMatrix);
            case MeshCollider m when m.sharedMesh != null: return Transformed(m.sharedMesh.bounds, c.transform.localToWorldMatrix);
            case SphereCollider s: return Transformed(new Bounds(s.center, Vector3.one * s.radius * 2f), c.transform.localToWorldMatrix);
            default: return c.bounds;
        }
    }

    static Bounds Transformed(Bounds local, Matrix4x4 m)
    {
        Vector3 ext = local.extents;
        var b = new Bounds(m.MultiplyPoint3x4(local.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            var corner = local.center + Vector3.Scale(ext, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            b.Encapsulate(m.MultiplyPoint3x4(corner));
        }
        return b;
    }
}
