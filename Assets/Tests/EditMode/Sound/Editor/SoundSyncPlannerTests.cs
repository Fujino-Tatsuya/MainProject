using System.Collections.Generic;
using System.Linq;
using Ami.BroAudio;
using Ami.BroAudio.Data;
using NUnit.Framework;

public sealed class SoundSyncPlannerTests
{
    const string Step1 = "Assets/51.Audio/SFX/Step_01.wav";
    const string Step2 = "Assets/51.Audio/SFX/Step_02.wav";
    const string Click = "Assets/51.Audio/UI/Click.mp3";

    static readonly HashSet<string> Clips = new HashSet<string>(new[] { Step1, Step2, Click }, System.StringComparer.OrdinalIgnoreCase);

    readonly List<DataTableEntry> _rows = new List<DataTableEntry>();

    [SetUp]
    public void SetUp() => _rows.Clear();

    void Row(int index, string field, string value) =>
        _rows.Add(new DataTableEntry("Sound", "SoundCatalog", $"entries[{index}].{field}", value, $"GameData.xlsx › Sound!{field}{index}"));

    void Entry(int index, string key, string library, string entity, string audioType = "SFX",
        string clipPaths = null, string playMode = null, string loop = null)
    {
        Row(index, "key", key);
        Row(index, "library", library);
        Row(index, "entity", entity);
        Row(index, "audioType", audioType);
        if (clipPaths != null) Row(index, "clipPaths", clipPaths);
        if (playMode != null) Row(index, "playMode", playMode);
        if (loop != null) Row(index, "loop", loop);
        Row(index, "volumeMultiplier", "1"); // entries 밖 수치 칸은 무시돼야 한다
    }

    static SoundSyncState State(
        IEnumerable<SoundSyncEntityState> entities = null,
        IEnumerable<string> libraries = null,
        IEnumerable<(string, string)> current = null) => new SoundSyncState
    {
        Entities = (entities ?? Enumerable.Empty<SoundSyncEntityState>()).ToList(),
        Libraries = (libraries ?? Enumerable.Empty<string>()).ToList(),
        CurrentEntries = (current ?? Enumerable.Empty<(string, string)>()).ToList(),
        ClipExists = Clips.Contains,
    };

    static SoundSyncEntityState Entity(string name, string library, params string[] clips) => new SoundSyncEntityState
    {
        Name = name,
        Library = library,
        AudioType = BroAudioType.SFX,
        PlayMode = MulticlipsPlayMode.Single,
        Loop = SoundLoopMode.None,
        ClipPaths = clips,
    };

    SoundSyncPlan Plan(SoundSyncState state) => SoundSyncPlanner.Plan(_rows, state);

    static string[] Errors(SoundSyncPlan plan) =>
        plan.Issues.Items.Where(i => i.Kind == DataTableIssueKind.Error).Select(i => i.Message).ToArray();

    static string[] Warnings(SoundSyncPlan plan) =>
        plan.Issues.Items.Where(i => i.Kind == DataTableIssueKind.Warning).Select(i => i.Message).ToArray();

    [Test]
    public void NewLibraryAndEntity_AreCreatedWithClipsAndMode()
    {
        Entry(0, "Footstep", "SFXLibrary", "Footstep", clipPaths: Step1 + "\n" + Step2, playMode: "Random", loop: "0");

        SoundSyncPlan plan = Plan(State());

        Assert.That(Errors(plan), Is.Empty);
        Assert.That(plan.NewLibraries, Is.EqualTo(new[] { "SFXLibrary" }));
        SoundSyncEntryPlan entry = plan.Entries.Single();
        Assert.That(entry.CreatesEntity, Is.True);
        Assert.That(entry.ClipPaths, Is.EqualTo(new[] { Step1, Step2 }));
        Assert.That(entry.PlayMode, Is.EqualTo(MulticlipsPlayMode.Random));
        Assert.That(entry.Loop, Is.EqualTo(SoundLoopMode.None));
        Assert.That(plan.HasChanges, Is.True);
    }

    [Test]
    public void ExistingEntity_InOtherLibrary_IsMoved()
    {
        Entry(0, "Footstep", "SFXLibrary", "footstep");

        SoundSyncPlan plan = Plan(State(new[] { Entity("Footstep", "LobbyLibrary", Step1) }, new[] { "LobbyLibrary", "SFXLibrary" }));

        Assert.That(Errors(plan), Is.Empty);
        SoundSyncEntryPlan entry = plan.Entries.Single();
        Assert.That(entry.CreatesEntity, Is.False);
        Assert.That(entry.Entity, Is.EqualTo("Footstep"), "대소문자는 실제 에셋 이름을 따른다");
        Assert.That(entry.MoveFromLibrary, Is.EqualTo("LobbyLibrary"));
        Assert.That(plan.NewLibraries, Is.Empty);
    }

    [Test]
    public void UnchangedEntry_HasNoChanges()
    {
        Entry(0, "UI_Click", "LobbyLibrary", "UI_Click_01", "SFX", Click, "Single", "0");

        SoundSyncPlan plan = Plan(State(new[] { Entity("UI_Click_01", "LobbyLibrary", Click) }, new[] { "LobbyLibrary" },
            new[] { ("UI_Click", "UI_Click_01") }));

        Assert.That(Errors(plan), Is.Empty);
        Assert.That(plan.Changes, Is.Empty);
    }

    [Test]
    public void DashCells_KeepEntityClipsModeAndLoop()
    {
        Entry(0, "UI_Click", "LobbyLibrary", "UI_Click_01");

        SoundSyncPlan plan = Plan(State(new[] { Entity("UI_Click_01", "LobbyLibrary", Click) }, new[] { "LobbyLibrary" },
            new[] { ("UI_Click", "UI_Click_01") }));

        SoundSyncEntryPlan entry = plan.Entries.Single();
        Assert.That(entry.ClipPaths, Is.Null);
        Assert.That(entry.PlayMode, Is.Null);
        Assert.That(entry.Loop, Is.Null);
        Assert.That(plan.Changes, Is.Empty);
    }

    [Test]
    public void ClipPaths_SplitOnNewlines_TrimAndAcceptBackslash()
    {
        Assert.That(SoundSyncPlanner.SplitClipPaths("  Assets\\51.Audio\\SFX\\Step_01.wav \r\n\r\n" + Step2 + "\n"),
            Is.EqualTo(new[] { Step1, Step2 }));
    }

    [Test]
    public void ClipPaths_OutsideAudioRootOrMissing_AreErrors()
    {
        Entry(0, "A", "Lib", "A", clipPaths: "Assets/Other/x.wav");
        Entry(1, "B", "Lib", "B", clipPaths: "Assets/51.Audio/../Other/x.wav");
        Entry(2, "C", "Lib", "C", clipPaths: "Assets/51.Audio/SFX/Missing.wav");
        Entry(3, "D", "Lib", "D", clipPaths: "");

        SoundSyncPlan plan = Plan(State());

        Assert.That(Errors(plan).Length, Is.EqualTo(4));
    }

    [Test]
    public void SharedEntity_IsError()
    {
        Entry(0, "A", "Lib", "Same", clipPaths: Step1);
        Entry(1, "B", "Lib", "Same", clipPaths: Step1);

        SoundSyncPlan plan = Plan(State());

        Assert.That(Errors(plan), Has.Some.Contains("한 엔티티는 한 키만"));
    }

    [Test]
    public void DuplicateEntityNameInProject_IsError()
    {
        Entry(0, "A", "Lib", "Dup");

        SoundSyncPlan plan = Plan(State(new[] { Entity("Dup", "Lib1"), Entity("Dup", "Lib2") }));

        Assert.That(Errors(plan), Has.Some.Contains("2개"));
    }

    [Test]
    public void UnknownNames_AndLoopOutOfRange_AreErrors()
    {
        Entry(0, "A", "Lib", "A", audioType: "Sfxx", clipPaths: Step1, playMode: "Randum", loop: "3");

        SoundSyncPlan plan = Plan(State());

        Assert.That(Errors(plan).Length, Is.EqualTo(3));
    }

    [Test]
    public void InvalidFileNameCharacters_AreErrors()
    {
        Entry(0, "A", "Lib/Sub", "A:B", clipPaths: Step1);

        SoundSyncPlan plan = Plan(State());

        Assert.That(Errors(plan).Length, Is.EqualTo(2));
    }

    [Test]
    public void IndexGap_IsError()
    {
        Entry(0, "A", "Lib", "A", clipPaths: Step1);
        Entry(2, "C", "Lib", "C", clipPaths: Step1);

        SoundSyncPlan plan = Plan(State());

        Assert.That(Errors(plan), Has.Some.Contains("entries[1]"));
    }

    [Test]
    public void PartialConfig_IsError()
    {
        Row(0, "key", "A");
        Row(0, "entity", "A");

        SoundSyncPlan plan = Plan(State());

        Assert.That(Errors(plan), Has.Some.Contains("library"));
    }

    [Test]
    public void MissingConfig_OnExistingEntry_KeepsLinkWithWarning()
    {
        Row(0, "key", "UI_Click");
        Entry(1, "A", "Lib", "A", clipPaths: Step1);

        SoundSyncPlan plan = Plan(State(new[] { Entity("UI_Click_01", "Lib", Click), Entity("A", "Lib", Step1) }, new[] { "Lib" },
            new[] { ("UI_Click", "UI_Click_01"), ("A", "A") }));

        Assert.That(Errors(plan), Is.Empty);
        Assert.That(plan.Entries[0].HasConfig, Is.False);
        Assert.That(Warnings(plan), Has.Some.Contains("그대로 둔다"));
        Assert.That(Warnings(plan), Has.None.Contains("어느 키도"), "기존 연결은 사용 중으로 친다");
    }

    [Test]
    public void SheetWithoutAnyConfig_IsError_SoKeysAreNotDeleted()
    {
        Row(0, "key", "UI_Click");
        Row(0, "volumeMultiplier", "1");

        SoundSyncPlan plan = Plan(State(new[] { Entity("UI_Click_01", "Lib", Click) }, new[] { "Lib" },
            new[] { ("UI_Click", "UI_Click_01"), ("BGM_Title", "TitleBGM_01") }));

        Assert.That(Errors(plan), Has.Some.Contains("Export Template"));
        Assert.That(plan.Changes, Is.Empty);
    }

    [Test]
    public void MissingConfig_OnNewEntry_IsError()
    {
        Row(0, "key", "New");

        SoundSyncPlan plan = Plan(State());

        Assert.That(Errors(plan), Has.Some.Contains("새 엔트리"));
    }

    [Test]
    public void EmptyKey_IsError()
    {
        Entry(0, "", "Lib", "A", clipPaths: Step1);

        SoundSyncPlan plan = Plan(State());

        Assert.That(Errors(plan), Has.Some.Contains("key"));
    }

    [Test]
    public void Shrink_WarnsRemovedKeys()
    {
        Entry(0, "A", "Lib", "A", clipPaths: Step1);

        SoundSyncPlan plan = Plan(State(new[] { Entity("A", "Lib", Step1), Entity("B", "Lib", Step2) }, new[] { "Lib" },
            new[] { ("A", "A"), ("B", "B") }));

        Assert.That(Errors(plan), Is.Empty);
        Assert.That(plan.EntryCount, Is.EqualTo(1));
        Assert.That(Warnings(plan), Has.Some.Contains("'B'"));
        Assert.That(plan.Changes, Has.Some.Contains("2 → 1"));
    }

    [Test]
    public void UnreferencedEntity_IsOrphanWarning()
    {
        Entry(0, "A", "Lib", "A", clipPaths: Step1);

        SoundSyncPlan plan = Plan(State(new[] { Entity("A", "Lib", Step1), Entity("Slash_01", "Lib") }, new[] { "Lib" },
            new[] { ("A", "A") }));

        Assert.That(Warnings(plan), Has.Some.Contains("Lib/Slash_01"));
    }

    [Test]
    public void SingleModeWithManyClips_Warns()
    {
        Entry(0, "A", "Lib", "A", clipPaths: Step1 + "\n" + Step2, playMode: "Single");

        SoundSyncPlan plan = Plan(State());

        Assert.That(Errors(plan), Is.Empty);
        Assert.That(Warnings(plan), Has.Some.Contains("첫 클립만"));
    }

    [Test]
    public void NewEntityWithoutClips_Warns()
    {
        Entry(0, "A", "Lib", "A");

        SoundSyncPlan plan = Plan(State());

        Assert.That(Errors(plan), Is.Empty);
        Assert.That(Warnings(plan), Has.Some.Contains("클립 없는 엔티티"));
    }

    [Test]
    public void ConfigChanges_AreDescribed()
    {
        Entry(0, "A", "Lib", "A", "Music", Step2, "Random", "2");

        SoundSyncPlan plan = Plan(State(new[] { Entity("A", "Lib", Step1) }, new[] { "Lib" }, new[] { ("A", "A") }));

        Assert.That(plan.Changes, Has.Some.Contains("audioType"));
        Assert.That(plan.Changes, Has.Some.Contains("playMode"));
        Assert.That(plan.Changes, Has.Some.Contains("loop 0 → 2"));
        Assert.That(plan.Changes, Has.Some.Contains("클립"));
    }
}
