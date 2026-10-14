using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

// 로컬라이징 L4b — 코드에 박혀 있던 UI 문구(로딩·스킬 툴팁·상태이상·결과·허수아비)·초 단위 키. 러너 = 데이터 테이블 EditMode 테스트.

public sealed class UiStringsL4bTests
{
    [Test]
    public void StatusEffect_EveryEnumValueExceptNone_HasKeyNamedAfterEnum()
    {
        foreach (StatusEffectType type in (StatusEffectType[])Enum.GetValues(typeof(StatusEffectType)))
        {
            bool found = UiStrings.TryGetStatusEffect(type, out UiString name);
            if (type == StatusEffectType.None)
            {
                Assert.That(found, Is.False);
                continue;
            }

            Assert.That(found, Is.True, type.ToString());
            Assert.That(name.Key, Is.EqualTo("UI.StatusEffect." + type), type.ToString());
            Assert.That(name.Ko, Is.EqualTo(type.ToString().Replace("Modifier", string.Empty)), type.ToString());
            Assert.That(UiStrings.All.Count(s => s.Key == name.Key), Is.EqualTo(1), name.Key);
        }
    }

    [Test]
    public void StatusEffect_CombinedFlags_HaveNoKey()
    {
        Assert.That(UiStrings.TryGetStatusEffect(StatusEffectType.Stunned | StatusEffectType.Slowed, out _), Is.False);
    }

    [Test]
    public void LoadingLists_MatchSceneSlotOrder()
    {
        Assert.That(UiStrings.LoadingTips.Select(s => s.Key),
            Is.EqualTo(new[] { "UI.Loading.Tip1", "UI.Loading.Tip2", "UI.Loading.Tip3" }));
        Assert.That(UiStrings.LoadingReadyMessages.Select(s => s.Key),
            Is.EqualTo(new[] { "UI.Loading.Ready1", "UI.Loading.Ready2", "UI.Loading.Ready3" }));
        foreach (UiString s in UiStrings.LoadingTips.Concat(UiStrings.LoadingReadyMessages))
            Assert.That(UiStrings.All.Count(a => a.Key == s.Key), Is.EqualTo(1), s.Key);
    }

    [Test]
    public void FormattedKeys_DeclareTheirPlaceholders()
    {
        Assert.That(UiStrings.UnitSeconds.Ko, Is.EqualTo("{sec}초"));
        Assert.That(UiStrings.SkillTooltipError.Ko, Does.Contain("{error}"));
        Assert.That(UiStrings.ResultSurvival.Ko, Does.Contain("{time}"));
        Assert.That(UiStrings.ResultPlayerSlot.Ko, Does.Contain("{n}"));
        Assert.That(UiStrings.TrainingDummyHp.Ko, Does.Contain("{hp}").And.Contain("{max}"));
    }

    [Test]
    public void SecondsUnit_IsTheOnlyKoWithASecondsSuffix()
    {
        // 초 단위는 UI.Unit.Seconds 한 곳 — 다른 문구가 "{x}초"·"{x}s" 를 따로 들고 있지 않다.
        foreach (UiString s in UiStrings.All.Where(s => s.Key != UiStrings.UnitSeconds.Key))
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch(s.Ko, @"\}(초|s\b)"), Is.False, s.Key);
    }
}

public sealed class UiStringsL4bFormatTests
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

    private void UseEnSeconds() => UseRows(new StringTable.Entry("UI.Unit.Seconds", "{sec}초", "{sec}s"));

    [Test]
    public void Seconds_NoRow_KoUnit_InvariantDecimalPoint()
    {
        Assert.That(UiStrings.Seconds(2.5f, "0.#"), Is.EqualTo("2.5초"));
        Assert.That(UiStrings.Seconds(3f, "0.#"), Is.EqualTo("3초"));
        Assert.That(UiStrings.Seconds(0.04f, "F1"), Is.EqualTo("0.0초"));
    }

    [Test]
    public void Seconds_Row_FollowsCurrentLanguage()
    {
        UseEnSeconds();
        Loc.Current = GameLanguage.En;

        Assert.That(UiStrings.Seconds(4f, "0.0"), Is.EqualTo("4.0s"));
        Assert.That(AssassinHUD.TransformLabelText(4f, false, false), Is.EqualTo("변신 4.0s"));
        Assert.That(SkillTooltipView.CooldownText(6f), Does.EndWith("> 6s"));
    }

    [Test]
    public void SkillTooltip_NoRow_MatchesOldText()
    {
        Assert.That(SkillTooltipView.CooldownText(1.5f), Is.EqualTo("<sprite name=\"cooldown\" color=#B8C4D6> 1.5초"));
        Assert.That(SkillTooltipView.ErrorText("bad"), Is.EqualTo("[오류: bad]"));
        Assert.That(UiStrings.SkillTooltipShiftHint.Text, Is.EqualTo("자세한 정보를 보려면 [Shift] 키를 누르세요"));
        Assert.That(SkillTooltipView.KeyBadgeText("Q"), Is.EqualTo("Q"));
        Assert.That(SkillTooltipView.KeyBadgeText(PassiveHUD.DisplayKey), Is.EqualTo("Passive"));
    }

    [Test]
    public void SkillTooltip_PassiveBadgeRow_TranslatesOnlyPassive()
    {
        UseRows(new StringTable.Entry("UI.SkillTooltip.PassiveBadge", "패시브", "Passive"));

        Assert.That(SkillTooltipView.KeyBadgeText(PassiveHUD.DisplayKey), Is.EqualTo("패시브"));
        Assert.That(SkillTooltipView.KeyBadgeText("RMB"), Is.EqualTo("RMB"));
    }

    [Test]
    public void StatusEffect_NoRow_ShowsEnumNameWithoutModifier()
    {
        Assert.That(StatusEffectHUD.DisplayName(StatusEffectType.Stunned), Is.EqualTo("Stunned"));
        Assert.That(StatusEffectHUD.DisplayName(StatusEffectType.MoveSpeedModifier), Is.EqualTo("MoveSpeed"));
        Assert.That(StatusEffectHUD.DisplayName(StatusEffectType.Stunned | StatusEffectType.Slowed), Is.EqualTo("Stunned, Slowed"));
    }

    [Test]
    public void StatusEffect_Row_UsesSheetName()
    {
        UseRows(new StringTable.Entry("UI.StatusEffect.Stunned", "기절", "Stun"));

        Assert.That(StatusEffectHUD.DisplayName(StatusEffectType.Stunned), Is.EqualTo("기절"));
        Loc.Current = GameLanguage.En;
        Assert.That(StatusEffectHUD.DisplayName(StatusEffectType.Stunned), Is.EqualTo("Stun"));
    }

    [Test]
    public void Result_NoRow_UsesSerializedLabels()
    {
        Assert.That(ResultStatsView.OutcomeText(SessionOutcome.Cleared, "CLEAR", "FAILED", "ABORTED"), Is.EqualTo("CLEAR"));
        Assert.That(ResultStatsView.OutcomeText(SessionOutcome.Aborted, "CLEAR", "FAILED", "ABORTED"), Is.EqualTo("ABORTED"));
        Assert.That(ResultStatsView.OutcomeText(SessionOutcome.Wiped, "CLEAR", "FAILED", "ABORTED"), Is.EqualTo("FAILED"));
        Assert.That(ResultStatsView.SurvivalText("생존 시간  ", "03:07"), Is.EqualTo("생존 시간  03:07"));
        Assert.That(ResultStatsView.SurvivalText(null, "--:--"), Is.EqualTo("--:--"));
        Assert.That(ResultPlayerRowView.SlotText(2, "P{0}"), Is.EqualTo("P2"));
        Assert.That(ResultPlayerRowView.SlotText(3, ""), Is.EqualTo("P3"));
    }

    [Test]
    public void Result_Row_WinsOverSerializedLabels()
    {
        UseRows(new StringTable.Entry("UI.Result.Cleared", "클리어", "CLEAR"),
            new StringTable.Entry("UI.Result.Survival", "생존 시간 {time}", "Survived {time}"),
            new StringTable.Entry("UI.Result.PlayerSlot", "{n}P", "P{n}"));

        Assert.That(ResultStatsView.OutcomeText(SessionOutcome.Cleared, "CLEAR", "FAILED", "ABORTED"), Is.EqualTo("클리어"));
        Assert.That(ResultPlayerRowView.SlotText(2, "P{0}"), Is.EqualTo("2P"));
        Loc.Current = GameLanguage.En;
        Assert.That(ResultStatsView.SurvivalText("생존 시간  ", "03:07"), Is.EqualTo("Survived 03:07"));
    }

    [Test]
    public void TrainingDummy_NoRow_UsesSerializedFormat_RowWins()
    {
        Assert.That(TrainingDummyHealthBar.HpText(40, 100, "{0} / {1}"), Is.EqualTo("40 / 100"));
        Assert.That(TrainingDummyHealthBar.HpText(40, 100, ""), Is.EqualTo("40 / 100"));

        UseRows(new StringTable.Entry("UI.TrainingDummy.Hp", "{hp}/{max}", "{hp}/{max}"));
        Assert.That(TrainingDummyHealthBar.HpText(40, 100, "{0} / {1}"), Is.EqualTo("40/100"));
    }

    [Test]
    public void Loading_NoRow_ShowsSceneValues_RowWins_ExtraSlotsStayAuthored()
    {
        string[] scene = { "으아ㅏㅏㅏㅏ", "커피ㅣㅣㅣㅣ", "밥 뭐 먹지....", "네 번째 팁" };

        Assert.That(NetworkLoadingScreenView.TableText(UiStrings.LoadingTips, scene, 0), Is.EqualTo("으아ㅏㅏㅏㅏ"));
        Assert.That(NetworkLoadingScreenView.TableText(UiStrings.LoadingTips, scene, 3), Is.EqualTo("네 번째 팁"));
        Assert.That(NetworkLoadingScreenView.PhaseText(NetworkLoadingPhase.WaitingForPlayers), Is.EqualTo("Waiting for players..."));
        Assert.That(NetworkLoadingScreenView.PhaseText(NetworkLoadingPhase.Idle), Is.EqualTo("Stand by..."));
        Assert.That(NetworkLoadingScreenView.PhaseText(NetworkLoadingPhase.Completed), Is.Empty);

        UseRows(new StringTable.Entry("UI.Loading.Tip2", "커피 한 잔 하세요", "Grab a coffee"),
            new StringTable.Entry("UI.Loading.Loading", "불러오는 중...", "Loading..."));
        Assert.That(NetworkLoadingScreenView.TableText(UiStrings.LoadingTips, scene, 1), Is.EqualTo("커피 한 잔 하세요"));
        Assert.That(NetworkLoadingScreenView.PhaseText(NetworkLoadingPhase.LoadingGame), Is.EqualTo("불러오는 중..."));
    }
}
