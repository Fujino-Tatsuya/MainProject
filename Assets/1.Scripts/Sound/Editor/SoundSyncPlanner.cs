using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Ami.BroAudio;
using Ami.BroAudio.Data;

/// <summary><c>SoundEntry.loop</c> 칸 값. 엔티티의 <c>Loop</c>·<c>SeamlessLoop</c> 두 플래그를 한 칸으로 고른다.</summary>
public enum SoundLoopMode
{
    None = 0,
    Loop = 1,
    Seamless = 2,
}

/// <summary>동기화 판정에 쓰는 엔티티 현재 상태(Unity 객체 없이).</summary>
public sealed class SoundSyncEntityState
{
    public string Name;
    public string Library; // 소속 라이브러리 이름. 없으면 null
    public BroAudioType AudioType;
    public MulticlipsPlayMode PlayMode;
    public SoundLoopMode Loop;
    public IReadOnlyList<string> ClipPaths = Array.Empty<string>();
}

/// <summary>동기화 판정 입력 — 프로젝트의 라이브러리·엔티티와 <c>SoundCatalog</c> 현재 엔트리.</summary>
public sealed class SoundSyncState
{
    public IReadOnlyCollection<string> Libraries = Array.Empty<string>();
    public IReadOnlyList<SoundSyncEntityState> Entities = Array.Empty<SoundSyncEntityState>();

    /// <summary>현재 <c>entries</c> 순서대로 (키, 연결된 엔티티 이름 — 없으면 null).</summary>
    public IReadOnlyList<(string Key, string Entity)> CurrentEntries = Array.Empty<(string, string)>();

    /// <summary>경로(<c>Assets/51.Audio/...</c>)에 AudioClip 이 있는가.</summary>
    public Func<string, bool> ClipExists = _ => false;
}

/// <summary>엔트리 하나에 대해 동기화가 할 일.</summary>
public sealed class SoundSyncEntryPlan
{
    public int Index;
    public string Key;

    /// <summary>xlsx 텍스트 칸 원문 — <c>SoundCatalog.asset</c> 에 그대로 남긴다.</summary>
    public readonly Dictionary<string, string> RawText = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>false = 구성 칸(library·entity·audioType)이 없어 기존 연결을 그대로 둔다.</summary>
    public bool HasConfig;

    public string Library;
    public string Entity;
    public BroAudioType AudioType;

    /// <summary>null = 클립 유지(<c>-</c>).</summary>
    public IReadOnlyList<string> ClipPaths;

    /// <summary>null = 유지(<c>-</c>).</summary>
    public MulticlipsPlayMode? PlayMode;

    /// <summary>null = 유지(<c>-</c>).</summary>
    public SoundLoopMode? Loop;

    public bool CreatesEntity;

    /// <summary>엔티티를 다른 라이브러리에서 옮겨 올 때 원래 라이브러리. 이동이 없으면 null.</summary>
    public string MoveFromLibrary;
}

public sealed class SoundSyncPlan
{
    public readonly DataTableIssues Issues = new DataTableIssues();
    public readonly List<SoundSyncEntryPlan> Entries = new List<SoundSyncEntryPlan>();
    public readonly List<string> NewLibraries = new List<string>();

    /// <summary>사람이 읽는 변경 목록(확인 대화상자·"동기화 필요" 경고).</summary>
    public readonly List<string> Changes = new List<string>();

    public int EntryCount => Entries.Count;
    public bool HasChanges => Changes.Count > 0;
}

/// <summary>
/// xlsx <c>Sound</c> 시트의 <c>SoundCatalog</c> 행 → 동기화 계획(PLAN-sound-sync.md).
/// 디스크·Unity API 를 쓰지 않는 순수 판정이다 — 쓰기는 <see cref="SoundSyncMenu"/> 가 한다.
/// 오류가 하나라도 있으면 실행 측은 아무것도 쓰지 않는다.
/// </summary>
public static class SoundSyncPlanner
{
    public const string AudioRoot = "Assets/51.Audio/";

    public const string FieldKey = "key";
    public const string FieldLibrary = "library";
    public const string FieldEntity = "entity";
    public const string FieldAudioType = "audioType";
    public const string FieldClipPaths = "clipPaths";
    public const string FieldPlayMode = "playMode";
    public const string FieldLoop = "loop";

    private static readonly string[] ConfigFields = { FieldLibrary, FieldEntity, FieldAudioType };
    private static readonly string[] TextFields = { FieldKey, FieldLibrary, FieldEntity, FieldAudioType, FieldClipPaths, FieldPlayMode };
    private static readonly Regex EntryField = new Regex(@"^entries\[(\d+)\]\.(\w+)$", RegexOptions.Compiled);

    private static readonly BroAudioType[] AllowedAudioTypes =
        { BroAudioType.Music, BroAudioType.UI, BroAudioType.Ambience, BroAudioType.SFX, BroAudioType.VoiceOver };

    private static readonly MulticlipsPlayMode[] AllowedPlayModes =
    {
        MulticlipsPlayMode.Single, MulticlipsPlayMode.Sequence, MulticlipsPlayMode.Random,
        MulticlipsPlayMode.Shuffle, MulticlipsPlayMode.Velocity, MulticlipsPlayMode.Chained,
    };

    private sealed class Row
    {
        public string Value;
        public string Location;
    }

    public static SoundSyncPlan Plan(IEnumerable<DataTableEntry> rows, SoundSyncState state)
    {
        var plan = new SoundSyncPlan();
        DataTableIssues issues = plan.Issues;
        SortedDictionary<int, Dictionary<string, Row>> byIndex = Collect(rows, issues);

        if (byIndex.Count == 0)
        {
            issues.Error("Sound", "SoundCatalog 의 entries 행이 없다 — 병합 Export 로 Sound 시트를 먼저 만든다.");
            return plan;
        }

        int count = byIndex.Keys.Last() + 1;
        for (int i = 0; i < count; i++)
        {
            if (!byIndex.ContainsKey(i))
            {
                issues.Error("Sound", $"entries[{i}] 행이 없다 — 인덱스는 0 부터 빈틈없이 이어져야 한다.");
            }
        }

        if (issues.HasErrors)
        {
            return plan;
        }

        var entitiesByName = state.Entities
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var libraries = new HashSet<string>(state.Libraries, StringComparer.OrdinalIgnoreCase);
        var claimedBy = new Dictionary<string, SoundSyncEntryPlan>(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<int, Dictionary<string, Row>> pair in byIndex)
        {
            SoundSyncEntryPlan entry = PlanEntry(pair.Key, pair.Value, state, entitiesByName, issues);
            plan.Entries.Add(entry);

            if (!entry.HasConfig || entry.Entity == null || entry.Library == null)
            {
                continue; // 이름 오류는 PlanEntry 가 이미 보고했다
            }

            if (claimedBy.TryGetValue(entry.Entity, out SoundSyncEntryPlan other))
            {
                issues.Error(pair.Value[FieldEntity].Location,
                    $"엔티티 '{entry.Entity}' 를 키 '{other.Key}'(entries[{other.Index}]) 도 쓴다 — 한 엔티티는 한 키만 쓴다.");
            }
            else
            {
                claimedBy[entry.Entity] = entry;
            }

            if (!libraries.Contains(entry.Library) && !plan.NewLibraries.Contains(entry.Library, StringComparer.OrdinalIgnoreCase))
            {
                plan.NewLibraries.Add(entry.Library);
            }
        }

        // 병합 Export 전의 옛 시트(구성 칸 없음)로 돌리면 배열이 줄어 기존 키가 지워진다 — 아예 막는다.
        if (plan.Entries.All(e => !e.HasConfig))
        {
            issues.Error("Sound", "Sound 시트에 library·entity·audioType 칸이 하나도 없다 — `Export Template` 병합으로 새 칸부터 만든다.");
            return plan;
        }

        DescribeChanges(plan, state, entitiesByName);
        WarnOrphans(plan, state);
        return plan;
    }

    /// <summary>엔티티 클립 목록 칸 → 정규화한 경로 목록. 줄바꿈 구분, 빈 줄·앞뒤 공백 무시, <c>\</c> → <c>/</c>.</summary>
    public static List<string> SplitClipPaths(string value)
    {
        return (value ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim().Replace('\\', '/'))
            .Where(p => p.Length > 0)
            .ToList();
    }

    public static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static SortedDictionary<int, Dictionary<string, Row>> Collect(IEnumerable<DataTableEntry> rows, DataTableIssues issues)
    {
        var byIndex = new SortedDictionary<int, Dictionary<string, Row>>();
        foreach (DataTableEntry row in rows)
        {
            Match match = EntryField.Match(row.Field.Trim());
            if (!match.Success)
            {
                continue; // entries 밖 필드는 데이터 테이블이 다룬다
            }

            int index = int.Parse(match.Groups[1].Value);
            string field = match.Groups[2].Value;
            if (!byIndex.TryGetValue(index, out Dictionary<string, Row> fields))
            {
                fields = new Dictionary<string, Row>(StringComparer.Ordinal);
                byIndex.Add(index, fields);
            }

            if (fields.ContainsKey(field))
            {
                issues.Error(row.Location, $"entries[{index}].{field} 가 두 번 나온다.");
                continue;
            }

            fields[field] = new Row { Value = row.Value?.Trim() ?? string.Empty, Location = row.Location };
        }

        return byIndex;
    }

    private static SoundSyncEntryPlan PlanEntry(
        int index,
        Dictionary<string, Row> fields,
        SoundSyncState state,
        Dictionary<string, List<SoundSyncEntityState>> entitiesByName,
        DataTableIssues issues)
    {
        var entry = new SoundSyncEntryPlan { Index = index };
        string where = fields.Values.First().Location;

        foreach (string text in TextFields)
        {
            if (fields.TryGetValue(text, out Row textRow))
            {
                entry.RawText[text] = textRow.Value;
            }
        }

        if (!fields.TryGetValue(FieldKey, out Row keyRow) || keyRow.Value.Length == 0)
        {
            issues.Error(keyRow?.Location ?? where, $"entries[{index}].key 가 비어 있다 — 코드에서 부를 키가 필요하다.");
        }
        else
        {
            entry.Key = keyRow.Value;
        }

        int present = ConfigFields.Count(fields.ContainsKey);
        if (present == 0)
        {
            bool hasExtra = fields.ContainsKey(FieldClipPaths) || fields.ContainsKey(FieldPlayMode) || fields.ContainsKey(FieldLoop);
            if (hasExtra)
            {
                issues.Error(where, $"entries[{index}] 에 library·entity·audioType 칸이 없는데 클립·재생 모드 칸이 있다.");
            }
            else if (index >= state.CurrentEntries.Count)
            {
                issues.Error(where, $"entries[{index}] 는 새 엔트리인데 library·entity·audioType 칸이 없다 — 연결할 엔티티를 적는다.");
            }
            else
            {
                issues.Warning(where, $"entries[{index}] 에 library·entity·audioType 칸이 없다 — 병합 Export 전이면 정상. 지금 연결을 그대로 둔다.");
            }

            return entry;
        }

        if (present < ConfigFields.Length)
        {
            string missing = string.Join("·", ConfigFields.Where(f => !fields.ContainsKey(f)));
            issues.Error(where, $"entries[{index}] 에 {missing} 칸이 없다(`-` 불가) — library·entity·audioType 은 함께 적는다.");
            return entry;
        }

        entry.HasConfig = true;
        entry.Library = ValidName(fields[FieldLibrary], "library", issues);
        entry.Entity = ValidName(fields[FieldEntity], "entity", issues);
        entry.AudioType = ParseAudioType(fields[FieldAudioType], issues);

        if (fields.TryGetValue(FieldClipPaths, out Row clipRow))
        {
            entry.ClipPaths = ParseClipPaths(clipRow, state, issues);
        }

        if (fields.TryGetValue(FieldPlayMode, out Row modeRow))
        {
            entry.PlayMode = ParsePlayMode(modeRow, issues);
        }

        if (fields.TryGetValue(FieldLoop, out Row loopRow))
        {
            entry.Loop = ParseLoop(loopRow, issues);
        }

        if (entry.Entity == null)
        {
            return entry;
        }

        entitiesByName.TryGetValue(entry.Entity, out List<SoundSyncEntityState> matches);
        if (matches == null || matches.Count == 0)
        {
            entry.CreatesEntity = true;
            if (entry.ClipPaths == null)
            {
                issues.Warning(fields[FieldEntity].Location, $"새 엔티티 '{entry.Entity}' 의 clipPaths 가 `-` 다 — 클립 없는 엔티티가 생겨 재생되지 않는다.");
            }
        }
        else if (matches.Count > 1)
        {
            issues.Error(fields[FieldEntity].Location,
                $"이름이 '{entry.Entity}' 인 엔티티가 {matches.Count}개다({string.Join(", ", matches.Select(m => m.Library ?? "<라이브러리 없음>"))}) — 이름을 겹치지 않게 바꾼다.");
        }
        else
        {
            SoundSyncEntityState current = matches[0];
            entry.Entity = current.Name; // 대소문자는 실제 에셋 이름을 따른다
            if (entry.Library != null && !string.Equals(current.Library, entry.Library, StringComparison.OrdinalIgnoreCase))
            {
                entry.MoveFromLibrary = current.Library ?? string.Empty;
            }
        }

        MulticlipsPlayMode effectiveMode = entry.PlayMode ?? (entry.CreatesEntity || matches == null || matches.Count != 1
            ? MulticlipsPlayMode.Single
            : matches[0].PlayMode);
        int effectiveClips = entry.ClipPaths?.Count ?? (matches != null && matches.Count == 1 ? matches[0].ClipPaths.Count : 0);
        if (effectiveMode == MulticlipsPlayMode.Single && effectiveClips > 1)
        {
            issues.Warning(fields[FieldEntity].Location, $"'{entry.Entity}' 는 Single 인데 클립이 {effectiveClips}개다 — 첫 클립만 재생된다.");
        }

        return entry;
    }

    private static string ValidName(Row row, string label, DataTableIssues issues)
    {
        if (row.Value.Length == 0)
        {
            issues.Error(row.Location, $"{label} 가 비어 있다.");
            return null;
        }

        if (row.Value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || row.Value.Contains('/'))
        {
            issues.Error(row.Location, $"{label} '{row.Value}' 에 파일 이름으로 못 쓰는 문자가 있다.");
            return null;
        }

        return row.Value;
    }

    private static BroAudioType ParseAudioType(Row row, DataTableIssues issues)
    {
        foreach (BroAudioType type in AllowedAudioTypes)
        {
            if (string.Equals(type.ToString(), row.Value, StringComparison.OrdinalIgnoreCase))
            {
                return type;
            }
        }

        issues.Error(row.Location, $"audioType '{row.Value}' 를 모른다 — {string.Join("·", AllowedAudioTypes)} 중 하나.");
        return BroAudioType.None;
    }

    private static MulticlipsPlayMode? ParsePlayMode(Row row, DataTableIssues issues)
    {
        foreach (MulticlipsPlayMode mode in AllowedPlayModes)
        {
            if (string.Equals(mode.ToString(), row.Value, StringComparison.OrdinalIgnoreCase))
            {
                return mode;
            }
        }

        issues.Error(row.Location, $"playMode '{row.Value}' 를 모른다 — {string.Join("·", AllowedPlayModes)} 중 하나(유지하려면 `-`).");
        return null;
    }

    private static SoundLoopMode? ParseLoop(Row row, DataTableIssues issues)
    {
        if (int.TryParse(row.Value, out int value) && value >= 0 && value <= 2)
        {
            return (SoundLoopMode)value;
        }

        issues.Error(row.Location, $"loop '{row.Value}' — 0(반복 없음)·1(Loop)·2(SeamlessLoop) 중 하나(유지하려면 `-`).");
        return null;
    }

    private static IReadOnlyList<string> ParseClipPaths(Row row, SoundSyncState state, DataTableIssues issues)
    {
        List<string> paths = SplitClipPaths(row.Value);
        if (paths.Count == 0)
        {
            issues.Error(row.Location, "clipPaths 가 비어 있다 — 클립을 건드리지 않으려면 `-`.");
            return null;
        }

        foreach (string path in paths)
        {
            if (!path.StartsWith(AudioRoot, StringComparison.OrdinalIgnoreCase) || path.Split('/').Contains(".."))
            {
                issues.Error(row.Location, $"클립 경로 '{path}' 는 {AudioRoot} 아래여야 한다.");
            }
            else if (!state.ClipExists(path))
            {
                issues.Error(row.Location, $"클립 경로 '{path}' 에 AudioClip 이 없다 — 경로·확장자·SVN 업데이트 확인.");
            }
        }

        return paths;
    }

    private static void DescribeChanges(SoundSyncPlan plan, SoundSyncState state, Dictionary<string, List<SoundSyncEntityState>> entitiesByName)
    {
        int before = state.CurrentEntries.Count;
        if (before != plan.EntryCount)
        {
            plan.Changes.Add($"entries 크기 {before} → {plan.EntryCount}");
        }

        for (int i = plan.EntryCount; i < before; i++)
        {
            plan.Issues.Warning("Sound", $"entries[{i}] 키 '{state.CurrentEntries[i].Key}' 가 xlsx 에 없어 SoundCatalog 에서 지워진다.");
        }

        foreach (string library in plan.NewLibraries)
        {
            plan.Changes.Add($"라이브러리 생성: {library}");
        }

        foreach (SoundSyncEntryPlan entry in plan.Entries)
        {
            (string Key, string Entity) current = entry.Index < before ? state.CurrentEntries[entry.Index] : (null, null);
            string label = $"[{entry.Index}] {entry.Key}";

            if (entry.Index < before && entry.Key != null && current.Key != entry.Key)
            {
                plan.Changes.Add($"{label}: 키 '{current.Key}' → '{entry.Key}'");
            }

            if (!entry.HasConfig || entry.Entity == null)
            {
                continue;
            }

            if (entry.CreatesEntity)
            {
                plan.Changes.Add($"{label}: 엔티티 생성 {entry.Library}/{entry.Entity}");
            }
            else if (entry.MoveFromLibrary != null)
            {
                plan.Changes.Add($"{label}: 엔티티 이동 {entry.Entity} ({(entry.MoveFromLibrary.Length == 0 ? "<없음>" : entry.MoveFromLibrary)} → {entry.Library})");
            }

            if (!string.Equals(current.Entity, entry.Entity, StringComparison.OrdinalIgnoreCase))
            {
                plan.Changes.Add($"{label}: SoundID 연결 {current.Entity ?? "<없음>"} → {entry.Entity}");
            }

            SoundSyncEntityState existing = !entry.CreatesEntity && entitiesByName.TryGetValue(entry.Entity, out List<SoundSyncEntityState> list) && list.Count == 1
                ? list[0]
                : null;
            if (existing == null)
            {
                continue;
            }

            if (existing.AudioType != entry.AudioType && entry.AudioType != BroAudioType.None)
            {
                plan.Changes.Add($"{label}: audioType {existing.AudioType} → {entry.AudioType}");
            }

            if (entry.PlayMode.HasValue && existing.PlayMode != entry.PlayMode.Value)
            {
                plan.Changes.Add($"{label}: playMode {existing.PlayMode} → {entry.PlayMode.Value}");
            }

            if (entry.Loop.HasValue && existing.Loop != entry.Loop.Value)
            {
                plan.Changes.Add($"{label}: loop {(int)existing.Loop} → {(int)entry.Loop.Value}");
            }

            if (entry.ClipPaths != null && !entry.ClipPaths.SequenceEqual(existing.ClipPaths, StringComparer.OrdinalIgnoreCase))
            {
                plan.Changes.Add($"{label}: 클립 {existing.ClipPaths.Count}개 → {entry.ClipPaths.Count}개");
            }
        }
    }

    private static void WarnOrphans(SoundSyncPlan plan, SoundSyncState state)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SoundSyncEntryPlan entry in plan.Entries)
        {
            string name = entry.HasConfig
                ? entry.Entity
                : entry.Index < state.CurrentEntries.Count ? state.CurrentEntries[entry.Index].Entity : null;
            if (name != null)
            {
                used.Add(name);
            }
        }

        List<string> orphans = state.Entities
            .Where(e => !used.Contains(e.Name))
            .Select(e => $"{e.Library ?? "<라이브러리 없음>"}/{e.Name}")
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (orphans.Count > 0)
        {
            plan.Issues.Warning("Sound",
                $"어느 키도 쓰지 않는 엔티티 {orphans.Count}개(삭제하지 않음 — EffectPart 등이 SoundID 로 직접 쓸 수 있다): {string.Join(", ", orphans)}");
        }
    }
}
