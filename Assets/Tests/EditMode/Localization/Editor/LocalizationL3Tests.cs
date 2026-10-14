using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEngine;

// 로컬라이징 L3 — UI 문구 키 목록·Export 등록·런타임 포맷(과열 단계·변신·머리 위 이름)·저작 메뉴 멱등. 러너 = 데이터 테이블 EditMode 테스트.

public sealed class UiStringsTests
{
    [Test]
    public void Keys_AreUnique_AndFollowAreaScreenNameRule()
    {
        var keys = UiStrings.All.Select(s => s.Key).ToList();

        Assert.That(keys, Is.Unique);
        foreach (string key in keys)
            Assert.That(Regex.IsMatch(key, @"^[A-Z][A-Za-z0-9]*(\.[A-Z][A-Za-z0-9]*){2,}$"), Is.True, key);
    }

    [Test]
    public void Ko_PassesSheetSyntaxRules()
    {
        foreach (UiString s in UiStrings.All)
        {
            Assert.That(s.Ko, Is.Not.Empty, s.Key);
            Assert.That(LocText.BraceError(s.Ko), Is.Null, s.Key);
            Assert.That(LocText.StyleTagError(s.Ko), Is.Null, s.Key);
        }
    }

    [Test]
    public void Registry_ExportsEveryKey_WithKoAndWhere()
    {
        List<StringKeyInfo> collected = StringKeyRegistry.Collect();

        foreach (UiString s in UiStrings.All)
        {
            StringKeyInfo info = collected.Single(k => k.Key == s.Key);
            Assert.That(info.Ko, Is.EqualTo(s.Ko));
            Assert.That(info.Description, Is.EqualTo(s.Where));
        }
    }

    [Test]
    public void FormattedKeys_DeclareTheirPlaceholders()
    {
        Assert.That(UiStrings.GunnerHeatStage.Ko, Does.Contain("{stage}"));
        Assert.That(UiStrings.AssassinTransformRemaining.Ko, Does.Contain("{time}"));
        Assert.That(UiStrings.AssassinTransformReleasable.Ko, Does.Contain("{time}"));
        Assert.That(UiStrings.OverheadPlayerLabel.Ko, Does.Contain("{n}"));
    }
}

public sealed class UiStringsFormatTests
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
        PlayerPrefs.Save();
        UserLanguageConfig.ResetCache();
    }

    private void UseRows(params StringTable.Entry[] entries) => _table.SetEntries(entries);

    [Test]
    public void GunnerHeat_NoRow_FallsBackToInlineKo()
    {
        Assert.That(GunnerHeatGauge.LabelText(0, false), Is.EqualTo("과열 0단계"));
        Assert.That(GunnerHeatGauge.LabelText(3, false), Is.EqualTo("과열 3단계"));
        Assert.That(GunnerHeatGauge.LabelText(3, true), Is.EqualTo("과열!"));
    }

    [Test]
    public void GunnerHeat_Row_FormatsStageInCurrentLanguage()
    {
        UseRows(new StringTable.Entry("UI.GunnerHeat.Stage", "과열 {stage}단계", "Heat Lv.{stage}"),
            new StringTable.Entry("UI.GunnerHeat.Overheated", "과열!", "Overheat!"));
        Loc.Current = GameLanguage.En;

        Assert.That(GunnerHeatGauge.LabelText(2, false), Is.EqualTo("Heat Lv.2"));
        Assert.That(GunnerHeatGauge.LabelText(2, true), Is.EqualTo("Overheat!"));
    }

    [Test]
    public void AssassinTransform_NoRow_MatchesOldText()
    {
        Assert.That(AssassinHUD.TransformLabelText(2.5f, false, false), Is.EqualTo("변신 2.5초"));
        Assert.That(AssassinHUD.TransformLabelText(4f, false, true), Is.EqualTo("변신 4.0초 · R 해제 가능"));
        Assert.That(AssassinHUD.TransformLabelText(4f, true, true), Is.EqualTo("변신 종료 대기"));
    }

    [Test]
    public void AssassinTransform_Row_FormatsSecondsInCurrentLanguage()
    {
        UseRows(new StringTable.Entry("UI.AssassinHUD.TransformRemaining", "변신 {time}", "Form {time}"),
            new StringTable.Entry("UI.Unit.Seconds", "{sec}초", "{sec}s"));
        Loc.Current = GameLanguage.En;

        Assert.That(AssassinHUD.TransformLabelText(1.5f, false, false), Is.EqualTo("Form 1.5s"));
    }

    [Test]
    public void OverheadLabel_NoRow_UsesSerializedFormat()
    {
        Assert.That(PlayerOverheadHealthBarRule.LabelText(1, "P{0}"), Is.EqualTo("P1"));
        Assert.That(PlayerOverheadHealthBarRule.LabelText(2, "Player {0}"), Is.EqualTo("Player 2"));
        Assert.That(PlayerOverheadHealthBarRule.LabelText(3, ""), Is.EqualTo("P3"));
    }

    [Test]
    public void OverheadLabel_Row_WinsOverSerializedFormat()
    {
        UseRows(new StringTable.Entry("UI.Overhead.PlayerLabel", "{n}P", "P{n}"));

        Assert.That(PlayerOverheadHealthBarRule.LabelText(2, "P{0}"), Is.EqualTo("2P"));
        Loc.Current = GameLanguage.En;
        Assert.That(PlayerOverheadHealthBarRule.LabelText(2, "P{0}"), Is.EqualTo("P2"));
    }

    [Test]
    public void LocalizedText_NoRow_KeepsAuthoredText_EmptyAuthoredShowsHashKey()
    {
        Assert.That(LocalizedText.Text("UI.Result.Kills", "처치"), Is.EqualTo("처치"));
        Assert.That(LocalizedText.Text("UI.Result.Kills", ""), Is.EqualTo("#UI.Result.Kills"));

        UseRows(new StringTable.Entry("UI.Result.Kills", "처치", "Kills"));
        Loc.Current = GameLanguage.En;
        Assert.That(LocalizedText.Text("UI.Result.Kills", "처치"), Is.EqualTo("Kills"));
    }

    [Test]
    public void SettingsConfirm_NoRow_KoWithSecondsUnit_RowFormatsInCurrentLanguage()
    {
        Assert.That(GraphicsSettingsController.ConfirmText(10), Is.EqualTo("이 화면 설정을 유지할까요?\n10초 뒤 되돌립니다"));

        UseRows(new StringTable.Entry("UI.Settings.Graphics.Confirm", "이 화면 설정을 유지할까요?\n{time} 뒤 되돌립니다", "Keep these display settings?\nReverting in {time}"),
            new StringTable.Entry("UI.Unit.Seconds", "{sec}초", "{sec}s"));
        Loc.Current = GameLanguage.En;
        Assert.That(GraphicsSettingsController.ConfirmText(3), Is.EqualTo("Keep these display settings?\nReverting in 3s"));
    }

    [Test]
    public void SettingsActionLabel_FollowsLanguage()
    {
        Assert.That(KeyRebindRow.ActionLabel("Player/SkillMain"), Is.EqualTo("스킬 1 (메인)"));

        UseRows(new StringTable.Entry("UI.Settings.Controls.SkillMain", "스킬 1 (메인)", "Skill 1 (Main)"));
        Loc.Current = GameLanguage.En;
        Assert.That(KeyRebindRow.ActionLabel("Player/SkillMain"), Is.EqualTo("Skill 1 (Main)"));
    }
}

public sealed class UiStringsAuthoringTests
{
    private GameObject _root;

    [SetUp]
    public void SetUp() => _root = new GameObject("Root");

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(_root);

    private GameObject Child(Transform parent, string name, bool withText)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        if (withText)
            go.AddComponent<TextMeshProUGUI>().text = "처치";
        return go;
    }

    [Test]
    public void Apply_IsIdempotent_AndKeepsText()
    {
        GameObject label = Child(_root.transform, "Header_Kills", true);

        Assert.That(UiStringsAuthoring.Apply(label, "UI.Result.Kills"), Is.True);
        Assert.That(UiStringsAuthoring.Apply(label, "UI.Result.Kills"), Is.False);
        Assert.That(label.GetComponents<LocalizedText>().Length, Is.EqualTo(1));
        Assert.That(label.GetComponent<LocalizedText>().Key, Is.EqualTo("UI.Result.Kills"));
        Assert.That(label.GetComponent<TMP_Text>().text, Is.EqualTo("처치"));
    }

    [Test]
    public void Apply_FixesWrongKey_SkipsWithoutText()
    {
        GameObject label = Child(_root.transform, "A", true);
        UiStringsAuthoring.Apply(label, "UI.Old");

        Assert.That(UiStringsAuthoring.Apply(label, "UI.Result.Kills"), Is.True);
        Assert.That(label.GetComponent<LocalizedText>().Key, Is.EqualTo("UI.Result.Kills"));
        Assert.That(UiStringsAuthoring.Apply(Child(_root.transform, "B", false), "UI.X.Y"), Is.False);
    }

    [Test]
    public void FindByPath_MatchesNameSuffix_IncludingInactive()
    {
        GameObject row = Child(_root.transform, "PlayerRowsHeader", false);
        GameObject label = Child(row.transform, "Header_Kills", true);
        Child(_root.transform, "Header_Kills", true); // 다른 부모의 같은 이름
        row.SetActive(false);

        Assert.That(UiStringsAuthoring.FindByPath(_root.transform, "PlayerRowsHeader/Header_Kills"), Is.SameAs(label.transform));
        Assert.That(UiStringsAuthoring.FindByPath(_root.transform, "Nope"), Is.Null);
    }

    // 설정창 라벨 연결(SettingsLocalizationLinker) — 영어 저작 원문으로 키를 찾고 글자를 ko 폴백으로 맞춘다. 두 번째는 무변경.
    [Test]
    public void SettingsLinker_EnglishLabel_GetsKeyAndKoFallback_Idempotent()
    {
        var label = new GameObject("Label");
        label.transform.SetParent(_root.transform, false);
        label.AddComponent<TextMeshProUGUI>().text = "Resolution";
        var value = new GameObject("Value");
        value.transform.SetParent(_root.transform, false);
        value.AddComponent<TextMeshProUGUI>().text = "100%";

        Assert.That(SettingsLocalizationLinker.Link(_root.transform), Is.EqualTo(1));
        Assert.That(label.GetComponent<LocalizedText>().Key, Is.EqualTo(SettingsUiStrings.GraphicsResolution.Key));
        Assert.That(label.GetComponent<TMP_Text>().text, Is.EqualTo("해상도"));
        Assert.That(value.GetComponent<LocalizedText>(), Is.Null);

        Assert.That(SettingsLocalizationLinker.Link(_root.transform), Is.EqualTo(0));
        Assert.That(label.GetComponents<LocalizedText>().Length, Is.EqualTo(1));
    }
}
