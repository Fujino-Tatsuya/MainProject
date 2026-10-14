using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Ami.BroAudio;
using Ami.BroAudio.Data;
using Ami.BroAudio.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// <c>Tools/Data/사운드 동기화</c> — xlsx <c>Sound</c> 시트를 디스크의 BroAudio 라이브러리·엔티티와 <c>SoundCatalog.asset</c> 에 반영한다
/// (PLAN-sound-sync.md). 판정은 <see cref="SoundSyncPlanner"/>, 여기는 읽기·쓰기만 한다.
/// <para>
/// 오류가 하나라도 있으면 아무것도 쓰지 않는다. 기존 에셋 수정은 Undo 로 되돌릴 수 있지만
/// 새로 만든 라이브러리·엔티티와 이동은 git 으로 되돌린다.
/// </para>
/// </summary>
public static class SoundSyncMenu
{
    private const string MenuPath = "Tools/Data/사운드 동기화 (xlsx → 라이브러리·엔티티·SoundCatalog)";
    private const string LogPrefix = "[SoundSync] ";
    private const int DialogChangeLines = 25;

    // BroAudio 직렬화 필드 이름(패키지 코드는 고치지 않고 SerializedProperty 로 쓴다).
    private const string PropAudioAsset = "<AudioAsset>k__BackingField";
    private const string PropAudioType = "<AudioType>k__BackingField";
    private const string PropLoop = "<Loop>k__BackingField";
    private const string PropSeamlessLoop = "<SeamlessLoop>k__BackingField";
    private const string PropPlayMode = "MulticlipsPlayMode";
    private const string PropClips = "Clips";
    private const string PropClip = "AudioClip";

    private sealed class Context
    {
        public SoundCatalog Catalog;
        public readonly List<DataTableEntry> Rows = new List<DataTableEntry>();
        public readonly DataTableIssues ReadIssues = new DataTableIssues();
        public readonly Dictionary<string, AudioAsset> Libraries = new Dictionary<string, AudioAsset>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, AudioEntity> Entities = new Dictionary<string, AudioEntity>(StringComparer.OrdinalIgnoreCase);
        public SoundSyncState State;
        public int FileCount;
    }

    [MenuItem(MenuPath, priority = 10)]
    private static void RunFromMenu() => Sync(confirm: true);

    /// <summary>동기화를 실행한다. <paramref name="confirm"/> = false 면 확인 대화상자 없이 쓴다(자동화용).</summary>
    public static bool Sync(bool confirm) => Sync(confirm, null);

    /// <summary><paramref name="rowsOverride"/> 가 있으면 xlsx 대신 그 행을 쓴다(검증용 — xlsx 는 SVN 원본이라 건드리지 않는다).</summary>
    internal static bool Sync(bool confirm, IReadOnlyList<DataTableEntry> rowsOverride)
    {
        if (!TryLoad(out Context context, out string loadError, rowsOverride))
        {
            Debug.LogError(LogPrefix + loadError);
            if (confirm) EditorUtility.DisplayDialog("사운드 동기화", loadError, "확인");
            return false;
        }

        SoundSyncPlan plan = SoundSyncPlanner.Plan(context.Rows, context.State);
        List<DataTableIssue> issues = context.ReadIssues.Items.Concat(plan.Issues.Items).ToList();
        foreach (DataTableIssue issue in issues)
        {
            if (issue.Kind == DataTableIssueKind.Error) Debug.LogError(LogPrefix + issue);
            else Debug.LogWarning(LogPrefix + issue);
        }

        int errorCount = issues.Count(i => i.Kind == DataTableIssueKind.Error);
        if (errorCount == 0)
        {
            errorCount += CheckTargetPaths(plan, context);
        }

        if (errorCount > 0)
        {
            string message = $"오류 {errorCount}개 — 아무것도 쓰지 않았다. 목록은 Console.";
            Debug.LogError(LogPrefix + message);
            if (confirm) EditorUtility.DisplayDialog("사운드 동기화", message, "확인");
            return false;
        }

        int warningCount = issues.Count(i => i.Kind == DataTableIssueKind.Warning);
        if (plan.HasChanges && confirm && !EditorUtility.DisplayDialog("사운드 동기화", Summary(plan, warningCount), "동기화", "취소"))
        {
            return false;
        }

        Apply(plan, context);
        AssetDatabase.SaveAssets();

        string done = plan.HasChanges
            ? $"동기화 완료 — 변경 {plan.Changes.Count}개, 엔트리 {plan.EntryCount}개" + (warningCount > 0 ? $", 경고 {warningCount}개(Console)" : string.Empty)
            : "바꿀 구조 없음 — SoundCatalog 텍스트 칸만 맞췄다.";
        foreach (string change in plan.Changes)
        {
            Debug.Log(LogPrefix + change);
        }

        Debug.Log(LogPrefix + done, context.Catalog);
        if (confirm) EditorWindow.focusedWindow?.ShowNotification(new GUIContent(done));
        return true;
    }

    /// <summary>
    /// 쓰지 않고 xlsx 와 에셋이 어긋났는지만 본다 — 테이블 Play·Verify 에서 "동기화를 실행하라" 경고용.
    /// 어떤 예외도 호출 측(Play 진입)을 막지 않는다.
    /// </summary>
    public static void WarnIfPending(string context)
    {
        try
        {
            if (!TryLoad(out Context loaded, out _) || loaded.FileCount == 0)
            {
                return;
            }

            SoundSyncPlan plan = SoundSyncPlanner.Plan(loaded.Rows, loaded.State);
            int errors = loaded.ReadIssues.Items.Concat(plan.Issues.Items).Count(i => i.Kind == DataTableIssueKind.Error);
            if (errors > 0)
            {
                Debug.LogWarning($"{LogPrefix}{context}: Sound 시트에 동기화 오류 {errors}개 — `Tools/Data/사운드 동기화` 로 확인할 것.");
            }
            else if (plan.HasChanges)
            {
                Debug.LogWarning($"{LogPrefix}{context}: xlsx Sound 시트와 사운드 에셋이 다르다(변경 {plan.Changes.Count}개) — " +
                                 "`Tools/Data/사운드 동기화` 를 먼저 실행할 것. entries 가 늘었다면 그 전까지 테이블이 배열 범위 오류를 낸다.\n" +
                                 string.Join("\n", plan.Changes));
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{LogPrefix}{context}: 동기화 상태 확인 실패 — {e.Message}");
        }
    }

    private static bool TryLoad(out Context context, out string error, IReadOnlyList<DataTableEntry> rowsOverride = null)
    {
        context = new Context();
        error = null;

        string[] catalogGuids = AssetDatabase.FindAssets("t:" + nameof(SoundCatalog));
        if (catalogGuids.Length != 1)
        {
            error = $"SoundCatalog 에셋이 {catalogGuids.Length}개다 — 정확히 하나여야 한다.";
            return false;
        }

        context.Catalog = AssetDatabase.LoadAssetAtPath<SoundCatalog>(AssetDatabase.GUIDToAssetPath(catalogGuids[0]));
        string sheetName = typeof(SoundCatalog).GetCustomAttribute<DataTableSheetAttribute>()?.Sheet ?? nameof(SoundCatalog);
        if (rowsOverride != null)
        {
            context.Rows.AddRange(rowsOverride);
            context.FileCount = 1;
        }
        else
        {
            ReadRows(context, sheetName);
        }

        foreach (AudioAsset library in LoadAll<AudioAsset>())
        {
            if (!context.Libraries.ContainsKey(library.name))
            {
                context.Libraries.Add(library.name, library);
            }
        }

        var entityStates = new List<SoundSyncEntityState>();
        foreach (AudioEntity entity in LoadAll<AudioEntity>())
        {
            context.Entities[entity.name] = entity; // 이름 중복은 planner 가 오류로 낸다(그땐 쓰지 않는다)
            entityStates.Add(new SoundSyncEntityState
            {
                Name = entity.name,
                Library = entity.AudioAsset != null ? entity.AudioAsset.name : null,
                AudioType = entity.AudioType,
                PlayMode = entity.PlayMode,
                Loop = entity.SeamlessLoop ? SoundLoopMode.Seamless : entity.Loop ? SoundLoopMode.Loop : SoundLoopMode.None,
                ClipPaths = ClipPathsOf(entity),
            });
        }

        // SoundID 의 엔티티 참조는 internal 이라 직렬화 필드로 읽는다.
        var current = new List<(string, string)>();
        SerializedProperty entries = new SerializedObject(context.Catalog).FindProperty("entries");
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty element = entries.GetArrayElementAtIndex(i);
            Object linked = element.FindPropertyRelative("sound._entity").objectReferenceValue;
            current.Add((element.FindPropertyRelative(SoundSyncPlanner.FieldKey).stringValue, linked != null ? linked.name : null));
        }

        context.State = new SoundSyncState
        {
            Libraries = context.Libraries.Keys.ToList(),
            Entities = entityStates,
            CurrentEntries = current,
            ClipExists = path => AssetDatabase.LoadAssetAtPath<AudioClip>(path) != null,
        };
        return true;
    }

    private static void ReadRows(Context context, string sheetName)
    {
        string[] files = Directory.Exists(DataTableSource.Folder)
            ? Directory.GetFiles(DataTableSource.Folder, "*.xlsx", SearchOption.TopDirectoryOnly)
                .Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();
        context.FileCount = files.Length;
        if (files.Length == 0)
        {
            context.ReadIssues.Error(DataTableSource.Folder, "xlsx 가 없다 — SVN 업데이트 확인.");
            return;
        }

        var entries = new List<DataTableEntry>();
        foreach (string file in files)
        {
            string fileName = Path.GetFileName(file);
            try
            {
                foreach (XlsxSheet sheet in XlsxReader.Read(file).Where(s => string.Equals(s.Name?.Trim(), sheetName, StringComparison.Ordinal)))
                {
                    DataTableSchema.Parse(fileName, sheet, entries, context.ReadIssues);
                }
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is System.Xml.XmlException)
            {
                context.ReadIssues.Error(fileName, $"xlsx 를 읽지 못했다: {e.Message}");
            }
        }

        context.Rows.AddRange(entries.Where(e => string.Equals(e.AssetId, context.Catalog.name, StringComparison.Ordinal)));
    }

    /// <summary>새로 만들거나 옮길 자리에 이미 파일이 있으면 오류 — 쓰기 전에 전부 확인한다.</summary>
    private static int CheckTargetPaths(SoundSyncPlan plan, Context context)
    {
        int errors = 0;
        string root = OutputRoot();
        foreach (string library in plan.NewLibraries)
        {
            string path = $"{root}/{library}.asset";
            if (AssetExists(path))
            {
                Debug.LogError($"{LogPrefix}라이브러리 '{library}' 를 만들 자리({path})에 다른 에셋이 있다.");
                errors++;
            }
        }

        foreach (SoundSyncEntryPlan entry in plan.Entries.Where(e => e.HasConfig && (e.CreatesEntity || e.MoveFromLibrary != null)))
        {
            string path = EntityPath(root, entry.Library, entry.Entity);
            if (AssetExists(path))
            {
                Debug.LogError($"{LogPrefix}엔티티 '{entry.Entity}' 를 둘 자리({path})에 다른 에셋이 있다.");
                errors++;
            }
        }

        return errors;
    }

    private static void Apply(SoundSyncPlan plan, Context context)
    {
        string root = OutputRoot();
        EnsureFolder(root);

        foreach (string name in plan.NewLibraries)
        {
            var library = ScriptableObject.CreateInstance<AudioAsset>();
            string path = $"{root}/{name}.asset";
            AssetDatabase.CreateAsset(library, path);
            library.AssetName = name;
            library.AssetGUID = AssetDatabase.AssetPathToGUID(path);
            EditorUtility.SetDirty(library);
            context.Libraries[name] = library;
        }

        var linked = new Dictionary<int, AudioEntity>();
        foreach (SoundSyncEntryPlan entry in plan.Entries.Where(e => e.HasConfig))
        {
            AudioAsset library = context.Libraries[entry.Library];
            string folder = $"{root}/{library.name}";
            EnsureFolder(folder);
            string path = EntityPath(root, library.name, entry.Entity);

            AudioEntity entity;
            if (entry.CreatesEntity)
            {
                entity = AudioEntity.CreateNewInstance(library, entry.Entity, entry.AudioType);
                AssetDatabase.CreateAsset(entity, path);
            }
            else
            {
                entity = context.Entities[entry.Entity];
                if (entry.MoveFromLibrary != null)
                {
                    string moveError = AssetDatabase.MoveAsset(AssetDatabase.GetAssetPath(entity), path);
                    if (!string.IsNullOrEmpty(moveError))
                    {
                        Debug.LogError($"{LogPrefix}엔티티 '{entry.Entity}' 이동 실패: {moveError}", entity);
                    }
                }
            }

            Configure(entity, library, entry);
            linked[entry.Index] = entity;
        }

        WriteCatalog(context.Catalog, plan, linked);
    }

    private static void Configure(AudioEntity entity, AudioAsset library, SoundSyncEntryPlan entry)
    {
        var so = new SerializedObject(entity);
        so.FindProperty(PropAudioAsset).objectReferenceValue = library;
        so.FindProperty(PropAudioType).intValue = (int)entry.AudioType;

        if (entry.PlayMode.HasValue)
        {
            so.FindProperty(PropPlayMode).intValue = (int)entry.PlayMode.Value;
        }

        if (entry.Loop.HasValue)
        {
            so.FindProperty(PropLoop).boolValue = entry.Loop.Value == SoundLoopMode.Loop;
            so.FindProperty(PropSeamlessLoop).boolValue = entry.Loop.Value == SoundLoopMode.Seamless;
        }

        if (entry.ClipPaths != null)
        {
            ReplaceClips(so.FindProperty(PropClips), entry.ClipPaths);
        }

        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// 클립 목록을 xlsx 순서로 바꾼다. 이미 있던 클립은 원소째 옮겨 Volume·Weight·Fade 등 클립별 설정을 보존하고,
    /// 새 클립만 기본값으로 넣는다.
    /// </summary>
    private static void ReplaceClips(SerializedProperty clips, IReadOnlyList<string> paths)
    {
        for (int i = 0; i < paths.Count; i++)
        {
            int found = -1;
            for (int j = i; j < clips.arraySize; j++)
            {
                if (SoundSyncPlanner.SamePath(ClipPath(clips.GetArrayElementAtIndex(j)), paths[i]))
                {
                    found = j;
                    break;
                }
            }

            if (found >= 0)
            {
                if (found != i)
                {
                    clips.MoveArrayElement(found, i);
                }

                continue;
            }

            clips.InsertArrayElementAtIndex(i); // 이웃 원소를 복제하므로 아래에서 전부 초기화한다
            SerializedProperty clip = clips.GetArrayElementAtIndex(i);
            clip.FindPropertyRelative(PropClip).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(paths[i]);
            SetFloat(clip, "Volume", 1f);
            foreach (string zero in new[] { "Delay", "StartPosition", "EndPosition", "FadeIn", "FadeOut" })
            {
                SetFloat(clip, zero, 0f);
            }

            SerializedProperty weight = clip.FindPropertyRelative("Weight");
            if (weight != null) weight.intValue = 0;
            SerializedProperty addressable = clip.FindPropertyRelative("AudioClipAssetReference.m_AssetGUID");
            if (addressable != null) addressable.stringValue = string.Empty;
        }

        while (clips.arraySize > paths.Count)
        {
            clips.DeleteArrayElementAtIndex(clips.arraySize - 1);
        }
    }

    private static void WriteCatalog(SoundCatalog catalog, SoundSyncPlan plan, Dictionary<int, AudioEntity> linked)
    {
        var so = new SerializedObject(catalog);
        SerializedProperty entries = so.FindProperty("entries");
        int before = entries.arraySize;
        entries.arraySize = plan.EntryCount;

        for (int i = before; i < plan.EntryCount; i++)
        {
            ResetEntry(entries.GetArrayElementAtIndex(i)); // 늘어난 원소는 마지막 원소의 복제라 기본값으로 되돌린다
        }

        foreach (SoundSyncEntryPlan entry in plan.Entries)
        {
            SerializedProperty element = entries.GetArrayElementAtIndex(entry.Index);
            if (entry.Key != null)
            {
                element.FindPropertyRelative(SoundSyncPlanner.FieldKey).stringValue = entry.Key;
            }

            if (!entry.HasConfig)
            {
                continue;
            }

            foreach (string field in new[]
                     {
                         SoundSyncPlanner.FieldLibrary, SoundSyncPlanner.FieldEntity, SoundSyncPlanner.FieldAudioType,
                         SoundSyncPlanner.FieldClipPaths, SoundSyncPlanner.FieldPlayMode,
                     })
            {
                // xlsx 에서 `-` 인 칸은 행이 없다 → SO 에도 `-` 로 남겨 병합 Export 가 그대로 돌려주게 한다.
                element.FindPropertyRelative(field).stringValue = entry.RawText.TryGetValue(field, out string value) ? value : DataTableSchema.NotApplicable;
            }

            if (entry.Loop.HasValue)
            {
                element.FindPropertyRelative(SoundSyncPlanner.FieldLoop).intValue = (int)entry.Loop.Value;
            }

            element.FindPropertyRelative("sound._entity").objectReferenceValue = linked[entry.Index];
        }

        so.ApplyModifiedProperties();
    }

    private static void ResetEntry(SerializedProperty element)
    {
        foreach (string text in new[] { "key", "library", "entity", "audioType", "description" })
        {
            element.FindPropertyRelative(text).stringValue = string.Empty;
        }

        element.FindPropertyRelative("clipPaths").stringValue = DataTableSchema.NotApplicable;
        element.FindPropertyRelative("playMode").stringValue = DataTableSchema.NotApplicable;
        element.FindPropertyRelative("sound._entity").objectReferenceValue = null;
        foreach (string one in new[] { "volumeMultiplier", "volumeRandomMin", "volumeRandomMax", "pitchMultiplier", "pitchRandomMin", "pitchRandomMax", "minDistance" })
        {
            SetFloat(element, one, 1f);
        }

        SetFloat(element, "maxDistance", 500f);
        SetFloat(element, "retriggerCooldown", 0f);
        foreach (string zero in new[] { "loop", "is3D", "maxSimultaneous" })
        {
            element.FindPropertyRelative(zero).intValue = 0;
        }
    }

    private static string Summary(SoundSyncPlan plan, int warningCount)
    {
        var text = new StringBuilder();
        text.AppendLine($"변경 {plan.Changes.Count}개를 디스크에 쓴다.");
        foreach (string change in plan.Changes.Take(DialogChangeLines))
        {
            text.AppendLine("• " + change);
        }

        if (plan.Changes.Count > DialogChangeLines)
        {
            text.AppendLine($"… 외 {plan.Changes.Count - DialogChangeLines}개(Console)");
        }

        if (warningCount > 0)
        {
            text.AppendLine().AppendLine($"⚠️ 경고 {warningCount}개 — Console 확인.");
        }

        text.AppendLine().Append("기존 에셋 수정은 Undo 가능, 새로 만든 라이브러리·엔티티와 이동은 git 으로 되돌린다.");
        return text.ToString();
    }

    private static IEnumerable<T> LoadAll<T>() where T : Object =>
        AssetDatabase.FindAssets("t:" + typeof(T).Name)
            .Select(guid => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(asset => asset != null);

    private static IReadOnlyList<string> ClipPathsOf(AudioEntity entity)
    {
        SerializedProperty clips = new SerializedObject(entity).FindProperty(PropClips);
        var paths = new List<string>();
        for (int i = 0; clips != null && i < clips.arraySize; i++)
        {
            paths.Add(ClipPath(clips.GetArrayElementAtIndex(i)));
        }

        return paths;
    }

    private static string ClipPath(SerializedProperty clip)
    {
        Object asset = clip.FindPropertyRelative(PropClip)?.objectReferenceValue;
        return asset != null ? AssetDatabase.GetAssetPath(asset) : string.Empty;
    }

    private static string OutputRoot() => BroEditorUtility.AssetOutputPath.TrimEnd('/', '\\').Replace('\\', '/');

    private static string EntityPath(string root, string library, string entity) => $"{root}/{library}/{entity}.asset";

    private static bool AssetExists(string path) => !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets));

    private static void SetFloat(SerializedProperty parent, string name, float value)
    {
        SerializedProperty property = parent.FindPropertyRelative(name);
        if (property != null) property.floatValue = value;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
