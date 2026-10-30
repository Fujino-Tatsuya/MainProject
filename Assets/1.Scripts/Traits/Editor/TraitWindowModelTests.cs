using NUnit.Framework;

/// <summary>
/// 튜토리얼 특성 선택 창 판정 고정 (할 일 15 T2) — 열림·닫힘·닫힌 제시 재표시 금지·획득 토스트·문구.
/// 첫 인자는 로컬 플레이어의 제시 번호(OfferSeq — 할 일 30 4단계부터 전역 라운드 대신).
/// </summary>
public sealed class TraitWindowModelTests
{
    private const ulong Host = 0;
    private const ulong Client1 = 1;

    private static int First(int maxExclusive) => 0;

    // 클라 뷰가 하는 일과 같다 — 내 제시 번호·제시 유무·획득 수만 넘긴다.
    private static TraitWindowUpdate EvaluateFor(TraitWindowModel window, TraitSelectionModel selection, ulong clientId)
    {
        return window.Evaluate(selection.GetOfferSeq(clientId), selection.HasPendingChoice(clientId), selection.GetAcquired(clientId).Count);
    }

    [Test]
    public void OthersPersonalRounds_KeepMyWindowOpen()
    {
        // 둘이 동시에 재선택(할 일 30) — Client1 이 고르고 다음 라운드를 받아도 Host 창은 닫혔다 다시 열리지 않는다.
        var selection = new TraitSelectionModel(12);
        var hostWindow = new TraitWindowModel();
        var clientWindow = new TraitWindowModel();
        selection.StartPersonalRound(Host, 3, First);
        selection.StartPersonalRound(Client1, 3, First);
        Assert.That(EvaluateFor(hostWindow, selection, Host).Opened, Is.True);
        Assert.That(EvaluateFor(clientWindow, selection, Client1).Opened, Is.True);
        int hostSeq = hostWindow.OpenOfferSeq;

        for (int r = 0; r < 3; r++)
        {
            selection.TryChoose(Client1, selection.GetOffered(Client1)[0]);
            selection.StartPersonalRound(Client1, 3, First);

            TraitWindowUpdate host = EvaluateFor(hostWindow, selection, Host);
            Assert.That(host.Opened || host.Closed || host.ShowPickedToast, Is.False, $"Client1 재선택 {r + 1}번째 — Host 창 그대로");
            Assert.That(hostWindow.OpenOfferSeq, Is.EqualTo(hostSeq));

            TraitWindowUpdate client = EvaluateFor(clientWindow, selection, Client1);
            Assert.That(client.Closed && client.Opened, Is.True, "Client1 은 자기 다음 제시로 새 창");
            Assert.That(client.ShowPickedToast, Is.True);
        }
    }

    [Test]
    public void OpensOnlyWhenMyOfferSeqChanges()
    {
        // 복도 라운드에서 Host 가 고른 뒤 늦게 온 Client1 추가·Client1 재선택 — Host 번호는 그대로라 창이 안 열린다.
        var selection = new TraitSelectionModel(12);
        var hostWindow = new TraitWindowModel();
        selection.StartRound(new[] { Host }, 3, First);
        EvaluateFor(hostWindow, selection, Host);
        selection.TryChoose(Host, selection.GetOffered(Host)[0]);
        EvaluateFor(hostWindow, selection, Host);
        int hostSeq = selection.GetOfferSeq(Host);

        selection.AddParticipant(Client1, First);
        selection.StartPersonalRound(Client1, 3, First);
        Assert.That(selection.GetOfferSeq(Host), Is.EqualTo(hostSeq), "다른 사람 제시는 내 번호를 안 바꾼다");
        TraitWindowUpdate unchanged = EvaluateFor(hostWindow, selection, Host);
        Assert.That(unchanged.Opened || unchanged.Closed, Is.False);

        selection.StartPersonalRound(Host, 3, First);
        Assert.That(selection.GetOfferSeq(Host), Is.GreaterThan(hostSeq));
        Assert.That(EvaluateFor(hostWindow, selection, Host).Opened, Is.True, "내 번호가 올라가면 새 창");
    }

    [Test]
    public void ClosedOfferNeverReopens_EvenAfterOthersOffers()
    {
        var selection = new TraitSelectionModel(12);
        var hostWindow = new TraitWindowModel();
        selection.StartRound(new[] { Host, Client1 }, 3, First);
        EvaluateFor(hostWindow, selection, Host);
        int closedSeq = hostWindow.OpenOfferSeq;
        Assert.That(hostWindow.ForceClose(), Is.True, "디렉터를 잠깐 놓쳤다 — 이 제시는 닫힌 것으로 친다");

        selection.TryChoose(Client1, selection.GetOffered(Client1)[0]);
        selection.StartPersonalRound(Client1, 3, First);
        TraitWindowUpdate update = EvaluateFor(hostWindow, selection, Host);

        Assert.That(selection.HasPendingChoice(Host), Is.True, "Host 제시 행은 아직 있다");
        Assert.That(update.Opened, Is.False, "닫힌 제시는 다른 사람 변화로 다시 열리지 않는다");
        Assert.That(hostWindow.WasClosed(closedSeq), Is.True);
    }

    [Test]
    public void OpensWhenOffered()
    {
        var model = new TraitWindowModel();
        TraitWindowUpdate update = model.Evaluate(1, hasOffer: true, acquiredCount: 0);

        Assert.That(update.Opened, Is.True);
        Assert.That(update.Closed, Is.False);
        Assert.That(model.OpenOfferSeq, Is.EqualTo(1));
    }

    [Test]
    public void StaysClosedWithoutOfferOrRound()
    {
        var model = new TraitWindowModel();

        Assert.That(model.Evaluate(1, false, 0).Opened, Is.False, "제시 없음(0장·참가자 아님)");
        Assert.That(model.Evaluate(0, true, 0).Opened, Is.False, "라운드 0 = 아직 없음");
        Assert.That(model.IsOpen, Is.False);
    }

    [Test]
    public void RepeatedEvaluateWhileOpen_NoEdges()
    {
        var model = new TraitWindowModel();
        model.Evaluate(1, true, 0);
        TraitWindowUpdate update = model.Evaluate(1, true, 0);

        Assert.That(update.Opened || update.Closed || update.ShowPickedToast, Is.False);
        Assert.That(model.IsOpen, Is.True);
    }

    [Test]
    public void ClosesWithToast_WhenChoiceConfirmed()
    {
        var model = new TraitWindowModel();
        model.Evaluate(2, true, 1);
        TraitWindowUpdate update = model.Evaluate(2, false, 2);

        Assert.That(update.Closed, Is.True);
        Assert.That(update.ShowPickedToast, Is.True, "수동·자동 획득 모두 획득 수가 는다");
        Assert.That(model.IsOpen, Is.False);
    }

    [Test]
    public void ClosesWithoutToast_WhenOfferVanishesWithoutAcquiring()
    {
        var model = new TraitWindowModel();
        model.Evaluate(1, true, 0);
        TraitWindowUpdate update = model.Evaluate(1, false, 0);

        Assert.That(update.Closed, Is.True);
        Assert.That(update.ShowPickedToast, Is.False);
    }

    [Test]
    public void ClosedRoundNeverReopens()
    {
        var model = new TraitWindowModel();
        model.Evaluate(1, true, 0);
        model.Evaluate(1, false, 1);
        TraitWindowUpdate update = model.Evaluate(1, true, 1);

        Assert.That(update.Opened, Is.False, "같은 라운드 제시 행이 다시 보여도(복도 왕복) 다시 띄우지 않는다");
        Assert.That(model.WasClosed(1), Is.True);
    }

    [Test]
    public void NewRoundOpensAfterPreviousClosed()
    {
        var model = new TraitWindowModel();
        model.Evaluate(1, true, 0);
        model.Evaluate(1, false, 1);
        TraitWindowUpdate update = model.Evaluate(2, true, 1);

        Assert.That(update.Opened, Is.True);
        Assert.That(update.OfferSeq, Is.EqualTo(2));
    }

    [Test]
    public void RoundChangeWhileOpen_ClosesOldAndOpensNew()
    {
        var model = new TraitWindowModel();
        model.Evaluate(1, true, 0);
        TraitWindowUpdate update = model.Evaluate(2, true, 0);

        Assert.That(update.Closed, Is.True);
        Assert.That(update.Opened, Is.True);
        Assert.That(update.ShowPickedToast, Is.False);
        Assert.That(model.WasClosed(1), Is.True);
        Assert.That(model.OpenOfferSeq, Is.EqualTo(2));
    }

    [Test]
    public void ForceClose_BlocksReopenAndNoToast()
    {
        var model = new TraitWindowModel();
        model.Evaluate(1, true, 0);

        Assert.That(model.ForceClose(), Is.True);
        Assert.That(model.ForceClose(), Is.False, "이미 닫혔으면 false");
        Assert.That(model.Evaluate(1, true, 0).Opened, Is.False);
    }

    [Test]
    public void AutoChosenWithoutWindow_ShowsToastOnce()
    {
        // 복도 미도착자 — 같은 틱에 참가자 추가 → 자동 획득이라 제시 행을 못 보고 획득 수만 는다(S12).
        var model = new TraitWindowModel();
        model.Evaluate(1, false, 1);
        TraitWindowUpdate update = model.Evaluate(2, false, 2);

        Assert.That(update.Opened || update.Closed, Is.False);
        Assert.That(update.ShowPickedToast, Is.True);
        Assert.That(model.Evaluate(2, false, 2).ShowPickedToast, Is.False, "한 번만");
    }

    [Test]
    public void FirstEvaluate_OnlySetsBaseline()
    {
        var model = new TraitWindowModel();
        Assert.That(model.Evaluate(2, false, 2).ShowPickedToast, Is.False, "늦게 붙은 UI 는 기존 획득에 토스트를 띄우지 않는다");
    }

    [Test]
    public void ToastNotDoubled_WhenWindowClosesByPick()
    {
        var model = new TraitWindowModel();
        model.Evaluate(1, true, 0);
        Assert.That(model.Evaluate(1, false, 1).ShowPickedToast, Is.True);
        Assert.That(model.Evaluate(1, false, 1).ShowPickedToast, Is.False);
    }

    [Test]
    public void LateJoinAfterChoosing_DoesNothing()
    {
        var model = new TraitWindowModel();
        TraitWindowUpdate update = model.Evaluate(3, false, 3);

        Assert.That(update.Opened || update.Closed || update.ShowPickedToast, Is.False);
    }

    [Test]
    public void Texts_LookupByIdAndFormatPicked()
    {
        Assert.That(TraitTexts.Get(TraitTexts.TitleId), Is.EqualTo("특성 선택"));
        Assert.That(TraitTexts.Get("UNKNOWN"), Is.Empty);
        Assert.That(TraitTexts.Get(TraitTexts.IntroId), Does.Contain("이번 작전 동안만"));

        string picked = TraitTexts.FormatPicked(TraitTexts.Get(TraitTexts.PickedId), "화면 왼쪽 위");
        Assert.That(picked, Is.EqualTo("특성을 얻었습니다. 획득한 특성은 화면 왼쪽 위에서 다시 볼 수 있습니다."));
    }

    [Test]
    public void TextTable_DefaultsMatchConstants_AndFallsBackWithoutAssetOrRow()
    {
        var table = UnityEngine.ScriptableObject.CreateInstance<TraitTextTable>();
        try
        {
            Assert.That(table.Entries.Count, Is.EqualTo(TraitTexts.AllIds.Length), "새 에셋 = 모든 ID 행");
            foreach (string id in TraitTexts.AllIds)
                Assert.That(TraitTextTable.Resolve(table, id), Is.EqualTo(TraitTexts.Get(id)), id);

            Assert.That(TraitTextTable.Resolve(null, TraitTexts.PickedLocationId), Is.EqualTo("화면 왼쪽 위"),
                "에셋 없음 → 상수");

            var so = new UnityEditor.SerializedObject(table);
            UnityEditor.SerializedProperty entries = so.FindProperty("entries");
            entries.GetArrayElementAtIndex(0).FindPropertyRelative("text").stringValue = "바뀐 제목";
            entries.DeleteArrayElementAtIndex(entries.arraySize - 1); // PICKED_LOCATION 행 삭제
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(TraitTextTable.Resolve(table, TraitTexts.TitleId), Is.EqualTo("바뀐 제목"), "에셋 값 우선");
            Assert.That(TraitTextTable.Resolve(table, TraitTexts.PickedLocationId), Is.EqualTo("화면 왼쪽 위"),
                "행 없음 → 상수");
            Assert.That(TraitTextTable.Resolve(table, "UNKNOWN"), Is.Empty);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(table);
        }
    }
}
