using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

/// <summary>로비 브리핑 — 할 일 86 T6c(대사 순서·서버 시간·완료 1회·중복 시작 무시·중단 = 해제 신호·잠금 장부·목표 표시).</summary>
public sealed class LobbyBriefingTests
{
    static readonly List<string> Due = new List<string>();

    static List<LobbyBriefingLine> Lines(params float[] seconds)
    {
        var lines = new List<LobbyBriefingLine>();
        for (var i = 0; i < seconds.Length; i++)
            lines.Add(new LobbyBriefingLine(LobbyBriefingRules.LineIds[i], seconds[i]));
        return lines;
    }

    // 큰 dt 한 번이 아니라 작은 걸음으로 돌려 실제 프레임처럼 — 내보낸 줄과 완료 횟수를 모은다.
    static int Run(LobbyBriefingModel model, float seconds, List<string> sent, float step = 0.1f)
    {
        var completions = 0;
        for (var t = 0f; t < seconds - 0.0001f; t += step)
        {
            if (model.Tick(step, Due))
                completions++;
            sent.AddRange(Due);
        }

        return completions;
    }

    [Test]
    public void 대사는_순서대로_앞_줄_시간이_지나면_하나씩_나간다()
    {
        var model = new LobbyBriefingModel();
        Assert.That(model.Start(Lines(2f, 1f, 3f, 1f), 0.5f, 1f), Is.True);
        var sent = new List<string>();

        Run(model, 0.4f, sent);
        Assert.That(sent, Is.Empty, "시작 지연 전엔 없음");

        Run(model, 0.2f, sent);   // 0.6초
        Assert.That(sent, Is.EqualTo(new[] { LobbyRadioIds.Brief01 }));

        Run(model, 2f, sent);     // 2.6초 — 01(0.5~2.5) 끝 → 02
        Assert.That(sent, Is.EqualTo(new[] { LobbyRadioIds.Brief01, LobbyRadioIds.Brief02 }));

        Run(model, 1f, sent);     // 3.6초 — 03
        Run(model, 3f, sent);     // 6.6초 — 04
        Assert.That(sent, Is.EqualTo(LobbyBriefingRules.LineIds));
    }

    [Test]
    public void 전체_시간은_지연_더하기_대사_합_더하기_여유이고_완료는_정확히_한_번()
    {
        var model = new LobbyBriefingModel();
        model.Start(Lines(7f, 5f, 6f, 4f), 0.5f, 1f);
        Assert.That(model.TotalSeconds, Is.EqualTo(23.5f).Within(0.0001f));

        var sent = new List<string>();
        Assert.That(Run(model, 23.3f, sent), Is.EqualTo(0), "여유가 끝나기 전엔 완료 아님");
        Assert.That(model.HoldsLock, Is.True);
        Assert.That(Run(model, 5f, sent), Is.EqualTo(1));
        Assert.That(model.State, Is.EqualTo(LobbyBriefingState.Completed));
        Assert.That(model.HoldsLock, Is.False);
        Assert.That(Run(model, 5f, sent), Is.EqualTo(0), "완료 뒤 추가 신호 없음");
        Assert.That(sent.Count, Is.EqualTo(4));
    }

    [Test]
    public void 큰_dt_한_번이면_남은_줄을_순서대로_한꺼번에_내보내고_완료한다()
    {
        var model = new LobbyBriefingModel();
        model.Start(Lines(1f, 1f, 1f, 1f), 0f, 0f);
        Assert.That(model.Tick(100f, Due), Is.True);
        Assert.That(Due, Is.EqualTo(LobbyBriefingRules.LineIds));
    }

    [Test]
    public void 표시_시간이_비면_기본값()
    {
        var line = new LobbyBriefingLine(LobbyRadioIds.Brief01, 0f);
        Assert.That(line.Seconds, Is.EqualTo(LobbyBriefingRules.DefaultLineSeconds));
        Assert.That(LobbyBriefingRules.DefaultLineSeconds, Is.EqualTo(TutorialSubtitleQueue.DefaultDurationSeconds));
    }

    [Test]
    public void 중복_시작은_무시한다()
    {
        var model = new LobbyBriefingModel();
        Assert.That(model.Start(Lines(1f, 1f, 1f, 1f), 0f, 0f), Is.True);
        model.Tick(1.5f, Due);
        Assert.That(model.Start(Lines(9f, 9f, 9f, 9f), 0f, 0f), Is.False, "진행 중 재시작 무시");
        Assert.That(model.SentCount, Is.EqualTo(2), "진행이 처음부터 다시 시작되지 않는다");

        model.Tick(100f, Due);
        Assert.That(model.Start(Lines(1f, 1f, 1f, 1f), 0f, 0f), Is.False, "완료 뒤 재시작 무시(회차당 1회)");
    }

    [Test]
    public void 중단은_진행_중일_때만_해제_신호이고_뒤엔_대사도_완료도_없다()
    {
        var model = new LobbyBriefingModel();
        Assert.That(model.Abort(), Is.False, "시작 전 중단 = 풀 것 없음");

        model.Start(Lines(1f, 1f, 1f, 1f), 0f, 0f);
        model.Tick(0.1f, Due);
        Assert.That(model.Abort(), Is.True);
        Assert.That(model.HoldsLock, Is.False);
        Assert.That(model.Abort(), Is.False, "두 번째 중단은 신호 없음");

        Assert.That(model.Tick(100f, Due), Is.False);
        Assert.That(Due, Is.Empty);
        Assert.That(model.Start(Lines(1f, 1f, 1f, 1f), 0f, 0f), Is.False, "중단 뒤 같은 진행기에서 재시작 없음");
    }

    [Test]
    public void 완료_뒤_중단은_신호가_없다()
    {
        var model = new LobbyBriefingModel();
        model.Start(Lines(1f, 1f, 1f, 1f), 0f, 0f);
        model.Tick(100f, Due);
        Assert.That(model.Abort(), Is.False);
        Assert.That(model.State, Is.EqualTo(LobbyBriefingState.Completed));
    }

    [Test]
    public void 대사가_없어도_지연_여유가_지나면_완료한다()
    {
        var model = new LobbyBriefingModel();
        model.Start(new List<LobbyBriefingLine>(), 0.5f, 1f);
        Assert.That(model.Tick(1f, Due), Is.False);
        Assert.That(model.Tick(0.6f, Due), Is.True);
    }

    [Test]
    public void 잠금_장부는_내가_건_것만_푼다()
    {
        var ledger = new LobbyBriefingLockLedger();
        Assert.That(ledger.ShouldLock(1, lockedByOther: false), Is.True);
        ledger.Record(1);
        Assert.That(ledger.ShouldLock(1, lockedByOther: true), Is.False, "이미 내가 잠근 사람은 다시 잠그지 않는다");
        Assert.That(ledger.ShouldLock(2, lockedByOther: true), Is.False, "다른 계통(보스 등장 연출)이 잠근 사람은 건드리지 않는다");
        Assert.That(ledger.ShouldLock(3, lockedByOther: false), Is.True);
        ledger.Record(3);

        var release = new List<ulong>();
        ledger.TakeAll(release);
        Assert.That(release, Is.EqualTo(new ulong[] { 1, 3 }));
        Assert.That(ledger.Count, Is.EqualTo(0));
    }

    [Test]
    public void 잠금_장부_퇴장한_사람은_풀_대상에서_빠진다()
    {
        var ledger = new LobbyBriefingLockLedger();
        ledger.Record(1);
        ledger.Record(2);
        Assert.That(ledger.Forget(2), Is.True);
        Assert.That(ledger.Forget(2), Is.False);

        var release = new List<ulong>();
        ledger.TakeAll(release);
        Assert.That(release, Is.EqualTo(new ulong[] { 1 }));
    }

    static LobbyTutorialStepView View(LobbyTutorialStepTable table, string stateId)
    {
        var index = table.IndexOf(stateId);
        Assert.That(index, Is.GreaterThanOrEqualTo(0), stateId);
        return new LobbyTutorialStepView(index, table[index]);
    }

    static LobbyTutorialStepTable LoadSteps()
    {
        var table = AssetDatabase.LoadAssetAtPath<LobbyTutorialStepTable>("Assets/9.ScriptableObject/Lobby/LobbyTutorialSteps.asset");
        Assert.That(table, Is.Not.Null);
        return table;
    }

    [Test]
    public void 목표_표시는_브리핑_중과_출격_집결만()
    {
        var table = LoadSteps();

        Assert.That(LobbyBriefingRules.ObjectiveFor(View(table, "L30_BriefGather")), Is.Null, "브리핑 위치 이동 중");
        Assert.That(LobbyBriefingRules.ObjectiveFor(View(table, "L31_BriefIntro")), Is.EqualTo(LobbyObjectiveIds.Briefing));
        Assert.That(LobbyBriefingRules.ObjectiveFor(View(table, "L34_SortieIntro")), Is.EqualTo(LobbyObjectiveIds.Briefing));
        Assert.That(LobbyBriefingRules.ObjectiveFor(View(table, "L40_SortieGather")), Is.EqualTo(LobbyObjectiveIds.Sortie));
        Assert.That(LobbyBriefingRules.ObjectiveFor(View(table, "L41_Loading")), Is.Null);
        Assert.That(LobbyBriefingRules.ObjectiveFor(View(table, "L00_LockerVisit")), Is.Null);
        Assert.That(LobbyBriefingRules.ObjectiveFor(View(table, "T50_FreePractice")), Is.Null);
        Assert.That(LobbyBriefingRules.ObjectiveFor(default), Is.Null, "진행기 없음");
    }

    [Test]
    public void 브리핑은_입력_잠금_대사_행만_감독관_대사_행은_아님()
    {
        var table = LoadSteps();

        Assert.That(LobbyBriefingRules.IsBriefing(View(table, "L11_CentralIntro")), Is.False, "잠금 없는 대사");
        Assert.That(LobbyBriefingRules.IsBriefing(View(table, "L21_TrainingIntro")), Is.False);
        Assert.That(LobbyBriefingRules.IsBriefing(View(table, "L30_BriefGather")), Is.False, "집결 = 아직 이동 중");
        Assert.That(LobbyBriefingRules.IsBriefing(View(table, "L31_BriefIntro")), Is.True);
        Assert.That(LobbyBriefingRules.IsBriefing(View(table, "L40_SortieGather")), Is.False, "브리핑 완료 = 출격 집결");
        Assert.That(LobbyBriefingRules.IsBriefing(default), Is.False);
    }

    [Test]
    public void 문구_에셋에_브리핑_대사와_로비_목표_행이_있다()
    {
        var table = AssetDatabase.LoadAssetAtPath<TutorialGuideTextTable>(TutorialGuideTextTable.AssetPath);
        Assume.That(table, Is.Not.Null, "TutorialGuideTexts 에셋 없음");
        foreach (var id in LobbyBriefingRules.LineIds)
        {
            Assert.That(table.TryGetRow(id, out var row), Is.True, id);
            Assert.That(row.DurationSeconds, Is.GreaterThan(0f), id);
        }

        foreach (var id in LobbyObjectiveIds.All)
        {
            Assert.That(table.TryGetRow(id, out var row), Is.True, id);
            Assert.That(row.Text, Is.Not.Empty, id);
        }
    }
}
