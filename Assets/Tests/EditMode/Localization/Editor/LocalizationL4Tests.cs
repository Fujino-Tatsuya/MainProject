using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

// 로컬라이징 L4 — 로비·네트워크 오류 코드 목록·ErrorCodes Export·화면 표기·인자 치환·SessionError. 러너 = 데이터 테이블 EditMode 테스트.

public sealed class ErrorCatalogTests
{
    private static readonly string[] Header = { "code", "#분류", "#원인(개발자용)", "#조치(QA·CS용)", "ko", "en" };

    [Test]
    public void Codes_AreUnique_AndFollowAreaNameRule()
    {
        var codes = ErrorCatalog.All.Select(e => e.Code).ToList();

        Assert.That(codes, Is.Unique);
        foreach (string code in codes)
            Assert.That(Regex.IsMatch(code, @"^[A-Z][A-Z0-9]*(_[A-Z0-9]+)+$"), Is.True, code);
    }

    [Test]
    public void All_ContainsEveryPublicField()
    {
        var fields = typeof(ErrorCatalog).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(ErrorCode))
            .Select(f => ((ErrorCode)f.GetValue(null)).Code)
            .ToList();

        Assert.That(ErrorCatalog.All.Select(e => e.Code), Is.EquivalentTo(fields));
    }

    [Test]
    public void Ko_AndMemos_AreFilled_AndPassSheetSyntaxRules()
    {
        foreach (ErrorCode e in ErrorCatalog.All)
        {
            Assert.That(e.Ko, Is.Not.Empty, e.Code);
            Assert.That(e.Category, Is.Not.Empty, e.Code);
            Assert.That(e.Cause, Is.Not.Empty, e.Code);
            Assert.That(LocText.BraceError(e.Ko), Is.Null, e.Code);
            Assert.That(LocText.StyleTagError(e.Ko), Is.Null, e.Code);
        }
    }

    [Test]
    public void Registry_ExportsEveryCode_WithKoAndMemoColumns()
    {
        List<StringKeyInfo> collected = StringKeyRegistry.CollectErrorCodes();

        Assert.That(collected.Select(k => k.Key), Is.EquivalentTo(ErrorCatalog.All.Select(e => e.Code)));
        foreach (ErrorCode e in ErrorCatalog.All)
        {
            StringKeyInfo info = collected.Single(k => k.Key == e.Code);
            Assert.That(info.Ko, Is.EqualTo(e.Ko));
            Assert.That(info.Memos, Is.EqualTo(new[] { e.Category, e.Cause, e.Action }));
        }
    }

    [Test]
    public void ErrorCodes_DoNotLeakIntoStringsRegistry()
    {
        var strings = new HashSet<string>(StringKeyRegistry.Collect().Select(k => k.Key));

        foreach (ErrorCode e in ErrorCatalog.All)
            Assert.That(strings.Contains(e.Code), Is.False, e.Code);
    }

    [Test]
    public void ExportedSheet_FromRegistry_PassesValidation_WithOnlyEnWarnings()
    {
        XlsxWriteSheet written = StringsSheet.ErrorCodes.BuildSheet(null, StringKeyRegistry.CollectErrorCodes(), new List<string>());
        var sheet = new XlsxSheet("ErrorCodes", written.Rows.Select(r => r.Select(c => c?.ToString() ?? string.Empty).ToArray()).ToArray());

        var issues = new DataTableIssues();
        List<LocalizedString> result = StringTableValidator.Validate(StringsSheet.ErrorCodes.Parse("GameData.xlsx", new[] { sheet }, issues), issues);

        Assert.That(issues.HasErrors, Is.False, string.Join("\n", issues.Items.Select(i => i.Message)));
        Assert.That(result.Count, Is.EqualTo(ErrorCatalog.All.Count));
    }

    [Test]
    public void BuildSheet_AppendsMissingCodes_WithMemoColumns_KeepsExisting()
    {
        var existing = new XlsxSheet("ErrorCodes", new[]
        {
            Header,
            new[] { "NET_IP_EMPTY", "기획 분류", "기획 원인", "기획 조치", "기획 문구", "Enter IP" },
            new[] { "#OLD_CODE", "", "", "", "메모", "" },
        });
        var registered = new[]
        {
            new StringKeyInfo("NET_IP_EMPTY", new[] { "연결", "원인", "조치" }, "IP를 입력하세요."),
            new StringKeyInfo("NET_PORT_INVALID", new[] { "연결", "포트 원인", "" }, "Port {port}"),
        };

        XlsxWriteSheet sheet = StringsSheet.ErrorCodes.BuildSheet(existing, registered, new List<string>());

        Assert.That(sheet.Rows.Count, Is.EqualTo(4));
        Assert.That(sheet.Rows[1], Is.EqualTo(new object[] { "NET_IP_EMPTY", "기획 분류", "기획 원인", "기획 조치", "기획 문구", "Enter IP" }));
        Assert.That(sheet.Rows[2][0], Is.EqualTo("#OLD_CODE"));
        Assert.That(sheet.Rows[3], Is.EqualTo(new object[] { "NET_PORT_INVALID", "연결", "포트 원인", null, "Port {port}", null }));
    }

    [Test]
    public void UnregisteredKeys_ListsSheetOnlyRows_SkipsMemoAndEmpty()
    {
        var existing = new XlsxSheet("ErrorCodes", new[]
        {
            Header,
            new[] { "NET_IP_EMPTY", "", "", "", "가", "" },
            new[] { "LOBBY_GONE", "", "", "", "나", "" },
            new[] { "#OLD_CODE", "", "", "", "다", "" },
            new[] { "", "", "", "", "", "" },
        });

        List<string> unregistered = StringsSheet.ErrorCodes.UnregisteredKeys(existing,
            new[] { new StringKeyInfo("NET_IP_EMPTY", new string[0], "가") });

        Assert.That(unregistered, Is.EqualTo(new[] { "LOBBY_GONE" }));
        Assert.That(StringsSheet.ErrorCodes.UnregisteredKeys(null, new StringKeyInfo[0]), Is.Empty);
    }
}

public sealed class ErrorDisplayTests
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
        _table.SetEntries(new StringTable.Entry[0]);
        _table.SetErrors(new StringTable.Entry[0]);
        Loc.SetTable(_table);
        Loc.Current = GameLanguage.Ko;
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
    }

    [Test]
    public void Compose_PutsSmallCodeOnNextLine()
    {
        Assert.That(ErrorDisplay.Compose("방이 가득 찼습니다", "LOBBY_FULL"),
            Is.EqualTo("방이 가득 찼습니다\n<size=70%>[LOBBY_FULL]</size>"));
    }

    [Test]
    public void Compose_WithoutCode_IsMessageOnly_WithoutMessage_IsCodeOnly()
    {
        Assert.That(ErrorDisplay.Compose("문구", null), Is.EqualTo("문구"));
        Assert.That(ErrorDisplay.Compose("문구", ""), Is.EqualTo("문구"));
        Assert.That(ErrorDisplay.Compose(null, "A_B"), Is.EqualTo("<size=70%>[A_B]</size>"));
    }

    [Test]
    public void Message_NoSheetRow_FallsBackToKo_WithArgs()
    {
        Assert.That(ErrorCatalog.NetIpInvalid.Message(ErrorDisplay.Arg("ip", "1.2.3")),
            Is.EqualTo("IP 형식이 올바르지 않습니다: 1.2.3"));
        Assert.That(ErrorCatalog.NetIpEmpty.Message(), Is.EqualTo("IP를 입력하세요."));
    }

    [Test]
    public void Message_SheetRowWins_InCurrentLanguage_WithArgs()
    {
        _table.SetErrors(new[] { new StringTable.Entry("NET_PORT_INVALID", "포트 {port} 안 됨", "Bad port {port}") });

        Assert.That(ErrorCatalog.NetPortInvalid.Message(ErrorDisplay.Arg("port", "0")), Is.EqualTo("포트 0 안 됨"));
        Loc.Current = GameLanguage.En;
        Assert.That(ErrorCatalog.NetPortInvalid.Display(ErrorDisplay.Arg("port", "0")),
            Is.EqualTo("Bad port 0\n<size=70%>[NET_PORT_INVALID]</size>"));
    }

    [Test]
    public void Message_UnknownArg_StaysAsPlaceholder()
    {
        Assert.That(ErrorCatalog.NetIpInvalid.Message(ErrorDisplay.Arg("other", "x")),
            Is.EqualTo("IP 형식이 올바르지 않습니다: {ip}"));
    }

    [Test]
    public void LocGetError_WithFallback_EmptyCode_UsesFallback()
    {
        Assert.That(Loc.GetError(null, "폴백 {n}", ErrorDisplay.Arg("n", "3")), Is.EqualTo("폴백 3"));
        Assert.That(Loc.GetError("NOPE"), Is.EqualTo("#NOPE"));
    }

    [Test]
    public void SessionError_Display_HasCodeButNoDetail_Log_HasCodeArgsAndDetail()
    {
        var error = new SessionError(ErrorCatalog.RelayJoinFailed, ErrorDisplay.Arg("n", "1"), "reason=Unknown boom");

        Assert.That(error.DisplayText, Is.EqualTo("Relay 방 참가에 실패했습니다.\n<size=70%>[RELAY_JOIN_FAILED]</size>"));
        Assert.That(error.DisplayText, Does.Not.Contain("boom"));
        Assert.That(error.LogText, Is.EqualTo("RELAY_JOIN_FAILED n=1 detail='reason=Unknown boom'"));
    }

    [Test]
    public void SessionStartResult_Failed_WithoutCode_IsNetStartFailed_Succeeded_HasNoError()
    {
        SessionStartResult failed = SessionStartResult.Failed(default(SessionError));
        SessionStartResult ok = SessionStartResult.Succeeded("CODE");

        Assert.That(failed.Success, Is.False);
        Assert.That(failed.Error.Code.Code, Is.EqualTo("NET_START_FAILED"));
        Assert.That(ok.Success, Is.True);
        Assert.That(ok.Error.IsNone, Is.True);
        Assert.That(ok.Error.LogText, Is.Empty);
        Assert.That(ok.ShareCode, Is.EqualTo("CODE"));
    }
}
