using NUnit.Framework;

/// <summary>튜토리얼 특성 선택 창 판정 고정 (할 일 15 T2) — 열림·닫힘·닫힌 라운드 재표시 금지·획득 토스트·문구.</summary>
public sealed class TutorialTraitWindowModelTests
{
    [Test]
    public void OpensWhenOffered()
    {
        var model = new TutorialTraitWindowModel();
        TutorialTraitWindowUpdate update = model.Evaluate(1, hasOffer: true, acquiredCount: 0);

        Assert.That(update.Opened, Is.True);
        Assert.That(update.Closed, Is.False);
        Assert.That(model.OpenRound, Is.EqualTo(1));
    }

    [Test]
    public void StaysClosedWithoutOfferOrRound()
    {
        var model = new TutorialTraitWindowModel();

        Assert.That(model.Evaluate(1, false, 0).Opened, Is.False, "제시 없음(0장·참가자 아님)");
        Assert.That(model.Evaluate(0, true, 0).Opened, Is.False, "라운드 0 = 아직 없음");
        Assert.That(model.IsOpen, Is.False);
    }

    [Test]
    public void RepeatedEvaluateWhileOpen_NoEdges()
    {
        var model = new TutorialTraitWindowModel();
        model.Evaluate(1, true, 0);
        TutorialTraitWindowUpdate update = model.Evaluate(1, true, 0);

        Assert.That(update.Opened || update.Closed || update.ShowPickedToast, Is.False);
        Assert.That(model.IsOpen, Is.True);
    }

    [Test]
    public void ClosesWithToast_WhenChoiceConfirmed()
    {
        var model = new TutorialTraitWindowModel();
        model.Evaluate(2, true, 1);
        TutorialTraitWindowUpdate update = model.Evaluate(2, false, 2);

        Assert.That(update.Closed, Is.True);
        Assert.That(update.ShowPickedToast, Is.True, "수동·자동 획득 모두 획득 수가 는다");
        Assert.That(model.IsOpen, Is.False);
    }

    [Test]
    public void ClosesWithoutToast_WhenOfferVanishesWithoutAcquiring()
    {
        var model = new TutorialTraitWindowModel();
        model.Evaluate(1, true, 0);
        TutorialTraitWindowUpdate update = model.Evaluate(1, false, 0);

        Assert.That(update.Closed, Is.True);
        Assert.That(update.ShowPickedToast, Is.False);
    }

    [Test]
    public void ClosedRoundNeverReopens()
    {
        var model = new TutorialTraitWindowModel();
        model.Evaluate(1, true, 0);
        model.Evaluate(1, false, 1);
        TutorialTraitWindowUpdate update = model.Evaluate(1, true, 1);

        Assert.That(update.Opened, Is.False, "같은 라운드 제시 행이 다시 보여도(복도 왕복) 다시 띄우지 않는다");
        Assert.That(model.WasClosed(1), Is.True);
    }

    [Test]
    public void NewRoundOpensAfterPreviousClosed()
    {
        var model = new TutorialTraitWindowModel();
        model.Evaluate(1, true, 0);
        model.Evaluate(1, false, 1);
        TutorialTraitWindowUpdate update = model.Evaluate(2, true, 1);

        Assert.That(update.Opened, Is.True);
        Assert.That(update.Round, Is.EqualTo(2));
    }

    [Test]
    public void RoundChangeWhileOpen_ClosesOldAndOpensNew()
    {
        var model = new TutorialTraitWindowModel();
        model.Evaluate(1, true, 0);
        TutorialTraitWindowUpdate update = model.Evaluate(2, true, 0);

        Assert.That(update.Closed, Is.True);
        Assert.That(update.Opened, Is.True);
        Assert.That(update.ShowPickedToast, Is.False);
        Assert.That(model.WasClosed(1), Is.True);
        Assert.That(model.OpenRound, Is.EqualTo(2));
    }

    [Test]
    public void ForceClose_BlocksReopenAndNoToast()
    {
        var model = new TutorialTraitWindowModel();
        model.Evaluate(1, true, 0);

        Assert.That(model.ForceClose(), Is.True);
        Assert.That(model.ForceClose(), Is.False, "이미 닫혔으면 false");
        Assert.That(model.Evaluate(1, true, 0).Opened, Is.False);
    }

    [Test]
    public void LateJoinAfterChoosing_DoesNothing()
    {
        var model = new TutorialTraitWindowModel();
        TutorialTraitWindowUpdate update = model.Evaluate(3, false, 3);

        Assert.That(update.Opened || update.Closed || update.ShowPickedToast, Is.False);
    }

    [Test]
    public void Texts_LookupByIdAndFormatPicked()
    {
        Assert.That(TutorialTraitTexts.Get(TutorialTraitTexts.TitleId), Is.EqualTo("특성 선택"));
        Assert.That(TutorialTraitTexts.Get("UNKNOWN"), Is.Empty);
        Assert.That(TutorialTraitTexts.Get(TutorialTraitTexts.IntroId), Does.Contain("이번 작전 동안만"));

        string picked = TutorialTraitTexts.FormatPicked(TutorialTraitTexts.Get(TutorialTraitTexts.PickedId), "화면 왼쪽 위");
        Assert.That(picked, Is.EqualTo("특성을 얻었습니다. 획득한 특성은 화면 왼쪽 위에서 다시 볼 수 있습니다."));
    }

    [Test]
    public void TextTable_DefaultsMatchConstants_AndFallsBackWithoutAssetOrRow()
    {
        var table = UnityEngine.ScriptableObject.CreateInstance<TutorialTraitTextTable>();
        try
        {
            Assert.That(table.Entries.Count, Is.EqualTo(TutorialTraitTexts.AllIds.Length), "새 에셋 = 모든 ID 행");
            foreach (string id in TutorialTraitTexts.AllIds)
                Assert.That(TutorialTraitTextTable.Resolve(table, id), Is.EqualTo(TutorialTraitTexts.Get(id)), id);

            Assert.That(TutorialTraitTextTable.Resolve(null, TutorialTraitTexts.PickedLocationId), Is.EqualTo("화면 왼쪽 위"),
                "에셋 없음 → 상수");

            var so = new UnityEditor.SerializedObject(table);
            UnityEditor.SerializedProperty entries = so.FindProperty("entries");
            entries.GetArrayElementAtIndex(0).FindPropertyRelative("text").stringValue = "바뀐 제목";
            entries.DeleteArrayElementAtIndex(entries.arraySize - 1); // PICKED_LOCATION 행 삭제
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(TutorialTraitTextTable.Resolve(table, TutorialTraitTexts.TitleId), Is.EqualTo("바뀐 제목"), "에셋 값 우선");
            Assert.That(TutorialTraitTextTable.Resolve(table, TutorialTraitTexts.PickedLocationId), Is.EqualTo("화면 왼쪽 위"),
                "행 없음 → 상수");
            Assert.That(TutorialTraitTextTable.Resolve(table, "UNKNOWN"), Is.Empty);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(table);
        }
    }
}
