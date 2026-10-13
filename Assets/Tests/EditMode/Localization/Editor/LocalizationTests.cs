using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// 로컬라이징 L1 — Strings 시트 검증·시트 읽기/Export·Loc 조회·언어 설정. 러너 = 데이터 테이블 EditMode 테스트.

public sealed class StringTableValidatorTests
{
    private static StringRow Row(string key, string ko, string en, int line = 2) =>
        new StringRow(key, string.Empty, ko, en, $"T › Strings!A{line}", $"T › Strings!C{line}", $"T › Strings!D{line}");

    [Test]
    public void ValidRow_PassesThrough()
    {
        var issues = new DataTableIssues();
        List<LocalizedString> result = StringTableValidator.Validate(new[] { Row("UI.Lobby.Ready", "준비", "Ready") }, issues);

        Assert.That(issues.Items, Is.Empty);
        Assert.That(result.Single().Ko, Is.EqualTo("준비"));
        Assert.That(result.Single().En, Is.EqualTo("Ready"));
    }

    [Test]
    public void DuplicateKey_IsError_FirstKept()
    {
        var issues = new DataTableIssues();
        List<LocalizedString> result = StringTableValidator.Validate(
            new[] { Row("A", "가", "a", 2), Row("A", "나", "b", 3) }, issues);

        Assert.That(issues.HasErrors, Is.True);
        Assert.That(issues.Items.Single().Location, Is.EqualTo("T › Strings!A3"));
        Assert.That(result.Single().Ko, Is.EqualTo("가"));
    }

    [Test]
    public void EmptyKo_IsError()
    {
        var issues = new DataTableIssues();
        List<LocalizedString> result = StringTableValidator.Validate(new[] { Row("A", "", "a") }, issues);

        Assert.That(issues.HasErrors, Is.True);
        Assert.That(issues.Items.First(i => i.Kind == DataTableIssueKind.Error).Location, Is.EqualTo("T › Strings!C2"));
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void EmptyEn_FallsBackToKo_WithWarning()
    {
        var issues = new DataTableIssues();
        List<LocalizedString> result = StringTableValidator.Validate(new[] { Row("A", "<style=stun>기절</style> {sec}초", "") }, issues);

        Assert.That(issues.HasErrors, Is.False);
        Assert.That(issues.Items.Single().Kind, Is.EqualTo(DataTableIssueKind.Warning));
        Assert.That(result.Single().En, Is.EqualTo("<style=stun>기절</style> {sec}초"));
    }

    [Test]
    public void PlaceholderSetMismatch_IsError()
    {
        var issues = new DataTableIssues();
        List<LocalizedString> result = StringTableValidator.Validate(new[] { Row("A", "{n}명 {time}", "{n} players") }, issues);

        Assert.That(issues.HasErrors, Is.True);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void PlaceholderOrderAndRepeat_Ignored()
    {
        var issues = new DataTableIssues();
        StringTableValidator.Validate(new[] { Row("A", "{a} {b} {a}", "{b} then {a}") }, issues);

        Assert.That(issues.Items, Is.Empty);
    }

    [TestCase("<style=stun>기절")]
    [TestCase("기절</style>")]
    [TestCase("</style><style=stun>기절")]
    public void StyleTagUnpaired_IsError(string ko)
    {
        var issues = new DataTableIssues();
        StringTableValidator.Validate(new[] { Row("A", ko, "x") }, issues);

        Assert.That(issues.HasErrors, Is.True);
    }

    [Test]
    public void StyleTagUnpairedInEnOnly_IsError()
    {
        var issues = new DataTableIssues();
        StringTableValidator.Validate(new[] { Row("A", "<style=stun>기절</style>", "<style=stun>Stun") }, issues);

        Assert.That(issues.Items.Single(i => i.Kind == DataTableIssueKind.Error).Location, Is.EqualTo("T › Strings!D2"));
    }

    [Test]
    public void StyleTagNestedPairs_Ok()
    {
        Assert.That(LocText.StyleTagError("<style=a>x<style=b>y</style></style>"), Is.Null);
    }
}

public sealed class StringsSheetTests
{
    [Test]
    public void Parse_ReadsRows_SkipsCommentAndEmpty()
    {
        var sheet = new XlsxSheet("Strings", new[]
        {
            new[] { "key", "#설명", "ko", "en" },
            new[] { "UI.Lobby.Ready", "로비 버튼", "준비", "Ready" },
            new[] { "#메모", "", "무시", "" },
            new string[0],
            new[] { "UI.Lobby.Start", "", "시작" },
        });
        var issues = new DataTableIssues();
        List<StringRow> rows = StringsSheet.Strings.Parse("GameData.xlsx", new[] { sheet }, issues);

        Assert.That(issues.Items, Is.Empty);
        Assert.That(rows.Select(r => r.Key), Is.EqualTo(new[] { "UI.Lobby.Ready", "UI.Lobby.Start" }));
        Assert.That(rows[1].En, Is.EqualTo(string.Empty));
        Assert.That(rows[1].EnLocation, Is.EqualTo("GameData.xlsx › Strings!D5"));
    }

    [Test]
    public void Parse_ValueWithoutKey_IsError()
    {
        var sheet = new XlsxSheet("Strings", new[] { new[] { "key", "#설명", "ko", "en" }, new[] { "", "", "고아", "" } });
        var issues = new DataTableIssues();
        StringsSheet.Strings.Parse("T", new[] { sheet }, issues);

        Assert.That(issues.HasErrors, Is.True);
    }

    [Test]
    public void Parse_BadHeader_IsError()
    {
        var sheet = new XlsxSheet("Strings", new[] { new[] { "id", "ko" }, new[] { "A", "가" } });
        var issues = new DataTableIssues();
        StringsSheet.Strings.Parse("T", new[] { sheet }, issues);

        Assert.That(issues.HasErrors, Is.True);
    }

    [Test]
    public void SchemaParse_IgnoresStringsSheet()
    {
        var sheet = new XlsxSheet("Strings", new[] { new[] { "key", "#설명", "ko", "en" }, new[] { "A", "", "가", "a" } });
        var issues = new DataTableIssues();
        List<DataTableEntry> entries = DataTableSchema.Parse("T", new[] { sheet }, issues);

        Assert.That(entries, Is.Empty);
        Assert.That(issues.Items, Is.Empty);
    }

    [Test]
    public void BuildSheet_NoExisting_NoKeys_HeaderOnly()
    {
        XlsxWriteSheet sheet = StringsSheet.Strings.BuildSheet(null, new StringKeyInfo[0], new List<string>());

        Assert.That(sheet.Name, Is.EqualTo("Strings"));
        Assert.That(sheet.Rows.Count, Is.EqualTo(1));
        Assert.That(sheet.Rows[0], Is.EqualTo(new object[] { "key", "#설명", "ko", "en" }));
    }

    [Test]
    public void BuildSheet_KeepsExistingRows_AppendsOnlyNewKeys()
    {
        var existing = new XlsxSheet("Strings", new[]
        {
            new[] { "key", "#설명", "ko", "en" },
            new[] { "A", "설명", "가", "a" },
            new[] { "#메모 행" },
        });
        XlsxWriteSheet sheet = StringsSheet.Strings.BuildSheet(existing,
            new[] { new StringKeyInfo("A", "새 설명", "덮으면 안 됨"), new StringKeyInfo("B", "", "나") }, new List<string>());

        Assert.That(sheet.Rows.Count, Is.EqualTo(4));
        Assert.That(sheet.Rows[1], Is.EqualTo(new object[] { "A", "설명", "가", "a" }));
        Assert.That(sheet.Rows[2][0], Is.EqualTo("#메모 행"));
        Assert.That(sheet.Rows[3], Is.EqualTo(new object[] { "B", null, "나", null }));
    }

    [Test]
    public void BuildSheet_RoundTripsThroughWriterAndReader()
    {
        var existing = new XlsxSheet("Strings", new[] { new[] { "key", "#설명", "ko", "en" }, new[] { "A", "", "첫 줄\n둘째 줄", "line" } });
        XlsxWriteSheet written = StringsSheet.Strings.BuildSheet(existing, new StringKeyInfo[0], new List<string>());

        using var stream = new System.IO.MemoryStream();
        XlsxWriter.Write(stream, new[] { written });
        stream.Position = 0;
        var issues = new DataTableIssues();
        List<StringRow> rows = StringsSheet.Strings.Parse("T", XlsxReader.Read(stream), issues);

        Assert.That(issues.Items, Is.Empty);
        Assert.That(rows.Single().Ko, Is.EqualTo("첫 줄\n둘째 줄"));
    }
}

public sealed class ErrorCodesSheetTests
{
    private static readonly string[] Header = { "code", "#분류", "#원인(개발자용)", "#조치(QA·CS용)", "ko", "en" };

    private static List<LocalizedString> ParseAndValidate(XlsxSheet sheet, DataTableIssues issues) =>
        StringTableValidator.Validate(StringsSheet.ErrorCodes.Parse("GameData.xlsx", new[] { sheet }, issues), issues);

    [Test]
    public void EmptySheet_HeaderOnly_Passes()
    {
        var issues = new DataTableIssues();
        List<LocalizedString> result = ParseAndValidate(new XlsxSheet("ErrorCodes", new[] { Header }), issues);

        Assert.That(issues.Items, Is.Empty);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Parse_ReadsCodeAndText_IgnoresMemoColumns()
    {
        var issues = new DataTableIssues();
        List<LocalizedString> result = ParseAndValidate(new XlsxSheet("ErrorCodes", new[]
        {
            Header,
            new[] { "LOBBY_FULL", "로비", "인원 초과", "재시도 안내", "방이 가득 찼다", "Lobby is full" },
            new[] { "#메모", "", "", "", "무시", "" },
        }), issues);

        Assert.That(issues.Items, Is.Empty);
        Assert.That(result.Single().Key, Is.EqualTo("LOBBY_FULL"));
        Assert.That(result.Single().En, Is.EqualTo("Lobby is full"));
    }

    [Test]
    public void DuplicateCode_IsError()
    {
        var issues = new DataTableIssues();
        List<LocalizedString> result = ParseAndValidate(new XlsxSheet("ErrorCodes", new[]
        {
            Header,
            new[] { "NET_TIMEOUT", "", "", "", "시간 초과", "Timed out" },
            new[] { "NET_TIMEOUT", "", "", "", "또", "Again" },
        }), issues);

        Assert.That(issues.HasErrors, Is.True);
        Assert.That(issues.Items.Single().Location, Is.EqualTo("GameData.xlsx › ErrorCodes!A3"));
        Assert.That(result.Single().Ko, Is.EqualTo("시간 초과"));
    }

    [Test]
    public void EmptyEn_FallsBackToKo_WithWarning()
    {
        var issues = new DataTableIssues();
        List<LocalizedString> result = ParseAndValidate(new XlsxSheet("ErrorCodes", new[]
        {
            Header,
            new[] { "NET_TIMEOUT", "", "", "", "시간 초과", "" },
        }), issues);

        Assert.That(issues.HasErrors, Is.False);
        Assert.That(issues.Items.Single().Location, Is.EqualTo("GameData.xlsx › ErrorCodes!F2"));
        Assert.That(result.Single().En, Is.EqualTo("시간 초과"));
    }

    [Test]
    public void SameRulesAsStrings_PlaceholderMismatchAndEmptyKo()
    {
        var issues = new DataTableIssues();
        ParseAndValidate(new XlsxSheet("ErrorCodes", new[]
        {
            Header,
            new[] { "A", "", "", "", "{n}초 뒤", "in {sec}s" },
            new[] { "B", "", "", "", "", "only en" },
        }), issues);

        Assert.That(issues.Items.Count(i => i.Kind == DataTableIssueKind.Error), Is.EqualTo(2));
    }

    [Test]
    public void SheetsAreSeparate()
    {
        var strings = new XlsxSheet("Strings", new[] { new[] { "key", "#설명", "ko", "en" }, new[] { "A", "", "가", "a" } });
        var errors = new XlsxSheet("ErrorCodes", new[] { Header, new[] { "A", "", "", "", "오류", "error" } });
        var issues = new DataTableIssues();

        Assert.That(StringsSheet.ErrorCodes.Parse("T", new[] { strings, errors }, issues).Single().Ko, Is.EqualTo("오류"));
        Assert.That(StringsSheet.Strings.Parse("T", new[] { strings, errors }, issues).Single().Ko, Is.EqualTo("가"));
        Assert.That(DataTableSchema.Parse("T", new[] { strings, errors }, issues), Is.Empty);
        Assert.That(issues.Items, Is.Empty);
    }

    [Test]
    public void BuildSheet_NoExisting_HeaderOnly()
    {
        XlsxWriteSheet sheet = StringsSheet.ErrorCodes.BuildSheet(null, new StringKeyInfo[0], new List<string>());

        Assert.That(sheet.Name, Is.EqualTo("ErrorCodes"));
        Assert.That(sheet.Rows.Count, Is.EqualTo(1));
        Assert.That(sheet.Rows[0], Is.EqualTo(Header));
    }

    [Test]
    public void BuildSheet_KeepsMemoColumns_RoundTrips()
    {
        var existing = new XlsxSheet("ErrorCodes", new[]
        {
            new[] { "code", "ko", "en", "#조치(QA·CS용)" }, // 열 순서가 달라도 이름으로 찾는다
            new[] { "LOBBY_FULL", "가득 참", "", "다른 방 안내" },
        });
        XlsxWriteSheet written = StringsSheet.ErrorCodes.BuildSheet(existing, new StringKeyInfo[0], new List<string>());

        Assert.That(written.Rows[1], Is.EqualTo(new object[] { "LOBBY_FULL", null, null, "다른 방 안내", "가득 참", null }));

        using var stream = new System.IO.MemoryStream();
        XlsxWriter.Write(stream, new[] { written });
        stream.Position = 0;
        var issues = new DataTableIssues();
        List<StringRow> rows = StringsSheet.ErrorCodes.Parse("T", XlsxReader.Read(stream), issues);

        Assert.That(issues.Items, Is.Empty);
        Assert.That(rows.Single().Ko, Is.EqualTo("가득 참"));
    }

    [Test]
    public void ExportedEmptySheet_ReadsBackClean()
    {
        XlsxWriteSheet written = StringsSheet.ErrorCodes.BuildSheet(null, new StringKeyInfo[0], new List<string>());

        using var stream = new System.IO.MemoryStream();
        XlsxWriter.Write(stream, new[] { written });
        stream.Position = 0;
        var issues = new DataTableIssues();

        Assert.That(ParseAndValidate(XlsxReader.Read(stream).Single(), issues), Is.Empty);
        Assert.That(issues.Items, Is.Empty);
    }
}

public sealed class LocTests
{
    private bool _hadPref;
    private string _savedPref;
    private StringTable _table;

    [SetUp]
    public void SetUp()
    {
        _hadPref = PlayerPrefs.HasKey(UserLanguageConfig.PrefsKey);
        _savedPref = PlayerPrefs.GetString(UserLanguageConfig.PrefsKey, string.Empty);
        _table = ScriptableObject.CreateInstance<StringTable>();
        _table.SetEntries(new[]
        {
            new StringTable.Entry("UI.Lobby.Ready", "준비", "Ready"),
            new StringTable.Entry("UI.Wait", "{n}명 대기 — {time}", "{n} waiting — {time}"),
            new StringTable.Entry("UI.KoOnly", "한국어만", ""),
        });
        _table.SetErrors(new[]
        {
            new StringTable.Entry("NET_TIMEOUT", "{sec}초 동안 응답이 없다", "No response for {sec}s"),
            new StringTable.Entry("UI.Lobby.Ready", "오류 칸", "error slot"),
        });
        Loc.SetTable(_table);
    }

    [TearDown]
    public void TearDown()
    {
        Loc.SetTable(null);
        Object.DestroyImmediate(_table);
        if (_hadPref)
            PlayerPrefs.SetString(UserLanguageConfig.PrefsKey, _savedPref);
        else
            PlayerPrefs.DeleteKey(UserLanguageConfig.PrefsKey);
        PlayerPrefs.Save();
        UserLanguageConfig.ResetCache();
    }

    [Test]
    public void Get_ReturnsCurrentLanguage()
    {
        Loc.Current = GameLanguage.Ko;
        Assert.That(Loc.Get("UI.Lobby.Ready"), Is.EqualTo("준비"));

        Loc.Current = GameLanguage.En;
        Assert.That(Loc.Get("UI.Lobby.Ready"), Is.EqualTo("Ready"));
    }

    [Test]
    public void Get_MissingKey_ReturnsHashKey()
    {
        Assert.That(Loc.Get("UI.Nope"), Is.EqualTo("#UI.Nope"));
    }

    [Test]
    public void Get_EmptyEn_FallsBackToKo()
    {
        Loc.Current = GameLanguage.En;
        Assert.That(Loc.Get("UI.KoOnly"), Is.EqualTo("한국어만"));
    }

    [Test]
    public void GetWithFallback_PresentKey_ReturnsCurrentLanguage()
    {
        Loc.Current = GameLanguage.En;
        Assert.That(Loc.Get("UI.Lobby.Ready", "인라인"), Is.EqualTo("Ready"));
        Assert.That(Loc.Get("UI.KoOnly", "인라인"), Is.EqualTo("한국어만"), "en 빈 칸은 ko — 폴백이 아니다");
    }

    [Test]
    public void GetWithFallback_MissingOrEmptyKey_ReturnsFallback_NotHashKey()
    {
        Assert.That(Loc.Get("UI.Nope", "인라인"), Is.EqualTo("인라인"));
        Assert.That(Loc.Get(null, "인라인"), Is.EqualTo("인라인"));
        Assert.That(Loc.Get("", "인라인"), Is.EqualTo("인라인"));
    }

    [Test]
    public void SkillTooltipText_Keyed_UsesTable_ThenInline()
    {
        var data = ScriptableObject.CreateInstance<LocalizedTextTestData>();
        data.name = "SkillA";
        try
        {
            var so = new UnityEditor.SerializedObject(data);
            so.FindProperty("tooltip.displayName").stringValue = "검격";
            so.FindProperty("tooltip.description").stringValue = "인라인 설명";
            so.ApplyModifiedPropertiesWithoutUndo();
            _table.SetEntries(new[] { new StringTable.Entry("SkillA/tooltip.displayName", "검격(표)", "Slash") });

            SkillTooltipText keyed = data.tooltip.Keyed(data, nameof(LocalizedTextTestData.tooltip));
            Loc.Current = GameLanguage.En;
            Assert.That(keyed.DisplayName, Is.EqualTo("Slash"));
            Assert.That(keyed.Description, Is.EqualTo("인라인 설명"), "표에 없으면 '#키' 가 아니라 인라인 ko");
            Assert.That(keyed.Subtitle, Is.EqualTo(string.Empty));

            Loc.Current = GameLanguage.Ko;
            Assert.That(keyed.DisplayName, Is.EqualTo("검격(표)"));
            Assert.That(data.tooltip.DisplayName, Is.EqualTo("검격"), "키를 안 붙인 값은 인라인 그대로");
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }

    [Test]
    public void CharacterRosterEntry_DisplayName_UsesNameKey_ThenInline()
    {
        var roster = ScriptableObject.CreateInstance<CharacterRoster>();
        var prefab = new GameObject("Player_Test");
        try
        {
            var so = new UnityEditor.SerializedObject(roster);
            SetRosterEntries(so, prefab);
            _table.SetEntries(new[] { new StringTable.Entry("Character.Player_Test.Name", "테스터", "Tester") });

            Loc.Current = GameLanguage.En;
            Assert.That(roster.Entries[0].DisplayName, Is.EqualTo("Tester"));
            Assert.That(roster.Entries[1].DisplayName, Is.EqualTo("프리팹 없음"), "키 없음 → 인라인");

            _table.SetEntries(new StringTable.Entry[0]);
            Assert.That(roster.Entries[0].DisplayName, Is.EqualTo("테스트"), "표에 행 없음 → 인라인");
        }
        finally
        {
            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(roster);
        }
    }

    // [0] = 프리팹 있음(키 있음), [1] = 프리팹 없음(키 없음).
    private static void SetRosterEntries(UnityEditor.SerializedObject so, GameObject prefab)
    {
        UnityEditor.SerializedProperty entries = so.FindProperty("entries");
        entries.arraySize = 2;
        entries.GetArrayElementAtIndex(0).FindPropertyRelative("displayName").stringValue = "테스트";
        entries.GetArrayElementAtIndex(0).FindPropertyRelative("playerPrefab").objectReferenceValue = prefab;
        entries.GetArrayElementAtIndex(1).FindPropertyRelative("displayName").stringValue = "프리팹 없음";
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    [Test]
    public void Format_FillsPlaceholders_LeavesUnknown()
    {
        Loc.Current = GameLanguage.En;
        string text = Loc.Format("UI.Wait", new Dictionary<string, string> { ["n"] = "2" });

        Assert.That(text, Is.EqualTo("2 waiting — {time}"));
    }

    [Test]
    public void Format_MissingKey_ReturnsHashKey()
    {
        Assert.That(Loc.Format("UI.Nope", new Dictionary<string, string> { ["n"] = "2" }), Is.EqualTo("#UI.Nope"));
    }

    [Test]
    public void GetError_FillsPlaceholders_InCurrentLanguage()
    {
        var args = new Dictionary<string, string> { ["sec"] = "10" };
        Loc.Current = GameLanguage.Ko;
        Assert.That(Loc.GetError("NET_TIMEOUT", args), Is.EqualTo("10초 동안 응답이 없다"));

        Loc.Current = GameLanguage.En;
        Assert.That(Loc.GetError("NET_TIMEOUT", args), Is.EqualTo("No response for 10s"));
        Assert.That(Loc.GetError("NET_TIMEOUT"), Is.EqualTo("No response for {sec}s"));
    }

    [Test]
    public void GetError_MissingCode_ReturnsHashCode()
    {
        Assert.That(Loc.GetError("NET_NOPE"), Is.EqualTo("#NET_NOPE"));
        Assert.That(Loc.GetError("NET_NOPE", new Dictionary<string, string> { ["sec"] = "1" }), Is.EqualTo("#NET_NOPE"));
        Assert.That(Loc.GetError(""), Is.EqualTo("#"));
    }

    [Test]
    public void GetError_AndGet_UseSeparateSheets()
    {
        Loc.Current = GameLanguage.Ko;
        Assert.That(Loc.Get("UI.Lobby.Ready"), Is.EqualTo("준비"));
        Assert.That(Loc.GetError("UI.Lobby.Ready"), Is.EqualTo("오류 칸"));
        Assert.That(Loc.Get("NET_TIMEOUT"), Is.EqualTo("#NET_TIMEOUT"));
    }

    [Test]
    public void Current_Change_RaisesChangedOnce()
    {
        Loc.Current = GameLanguage.Ko;
        int count = 0;
        void OnChanged() => count++;
        Loc.Changed += OnChanged;
        try
        {
            Loc.Current = GameLanguage.En;
            Loc.Current = GameLanguage.En; // 같은 값 — 안 나감
        }
        finally
        {
            Loc.Changed -= OnChanged;
        }

        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void LocalizedImage_Select_FallsBackToKo()
    {
        Sprite ko = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
        try
        {
            Assert.That(LocalizedImage.Select(ko, null, GameLanguage.En, out bool fellBack), Is.SameAs(ko));
            Assert.That(fellBack, Is.True);
            Assert.That(LocalizedImage.Select(ko, null, GameLanguage.Ko, out fellBack), Is.SameAs(ko));
            Assert.That(fellBack, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(ko);
        }
    }
}

public sealed class UserLanguageConfigTests
{
    private bool _hadPref;
    private string _savedPref;

    [SetUp]
    public void SetUp()
    {
        _hadPref = PlayerPrefs.HasKey(UserLanguageConfig.PrefsKey);
        _savedPref = PlayerPrefs.GetString(UserLanguageConfig.PrefsKey, string.Empty);
    }

    [TearDown]
    public void TearDown()
    {
        if (_hadPref)
            PlayerPrefs.SetString(UserLanguageConfig.PrefsKey, _savedPref);
        else
            PlayerPrefs.DeleteKey(UserLanguageConfig.PrefsKey);
        PlayerPrefs.Save();
        UserLanguageConfig.ResetCache();
    }

    [TestCase(SystemLanguage.Korean, GameLanguage.Ko)]
    [TestCase(SystemLanguage.English, GameLanguage.En)]
    [TestCase(SystemLanguage.Japanese, GameLanguage.En)]
    public void DefaultFor_KoreanOsIsKo_OthersEn(SystemLanguage system, GameLanguage expected)
    {
        Assert.That(UserLanguageConfig.DefaultFor(system), Is.EqualTo(expected));
    }

    [Test]
    public void NoSavedValue_UsesSystemDefault()
    {
        PlayerPrefs.DeleteKey(UserLanguageConfig.PrefsKey);
        UserLanguageConfig.ResetCache();

        Assert.That(UserLanguageConfig.Language, Is.EqualTo(UserLanguageConfig.DefaultFor(Application.systemLanguage)));
    }

    [TestCase(GameLanguage.Ko, "ko")]
    [TestCase(GameLanguage.En, "en")]
    public void Set_SavesCode_AndSurvivesCacheReset(GameLanguage language, string code)
    {
        UserLanguageConfig.ResetCache();
        UserLanguageConfig.Language = language == GameLanguage.Ko ? GameLanguage.En : GameLanguage.Ko;
        UserLanguageConfig.Language = language;

        Assert.That(PlayerPrefs.GetString(UserLanguageConfig.PrefsKey), Is.EqualTo(code));
        UserLanguageConfig.ResetCache();
        Assert.That(UserLanguageConfig.Language, Is.EqualTo(language));
    }

    [Test]
    public void GarbageSavedValue_UsesSystemDefault()
    {
        PlayerPrefs.SetString(UserLanguageConfig.PrefsKey, "fr");
        UserLanguageConfig.ResetCache();

        Assert.That(UserLanguageConfig.Language, Is.EqualTo(UserLanguageConfig.DefaultFor(Application.systemLanguage)));
    }

    // 옛 설정창 언어 시스템(경석 LanguageConfig)은 같은 키에 정수(0 = English, 1 = 한국어)로 저장했다.
    [TestCase(0, GameLanguage.En, "en")]
    [TestCase(1, GameLanguage.Ko, "ko")]
    public void LegacyIntSavedValue_MigratesOnceToCode(int legacy, GameLanguage expected, string code)
    {
        PlayerPrefs.DeleteKey(UserLanguageConfig.PrefsKey);
        PlayerPrefs.SetInt(UserLanguageConfig.PrefsKey, legacy);
        UserLanguageConfig.ResetCache();

        Assert.That(UserLanguageConfig.Language, Is.EqualTo(expected));
        Assert.That(PlayerPrefs.GetString(UserLanguageConfig.PrefsKey, string.Empty), Is.EqualTo(code));

        UserLanguageConfig.ResetCache();
        Assert.That(UserLanguageConfig.Language, Is.EqualTo(expected));
    }

    [Test]
    public void LegacyIntOutOfRange_UsesSystemDefault()
    {
        PlayerPrefs.DeleteKey(UserLanguageConfig.PrefsKey);
        PlayerPrefs.SetInt(UserLanguageConfig.PrefsKey, 7);
        UserLanguageConfig.ResetCache();

        Assert.That(UserLanguageConfig.Language, Is.EqualTo(UserLanguageConfig.DefaultFor(Application.systemLanguage)));
        Assert.That(UserLanguageConfig.LegacyIndexToLanguage(7, out _), Is.False);
        Assert.That(UserLanguageConfig.LegacyIndexToLanguage(-1, out _), Is.False);
    }
}
