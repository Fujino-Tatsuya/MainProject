using NUnit.Framework;
using UnityEditor;

/// <summary>
/// 할 일 157 커밋 ⑩ — L40/L41 출격(확정 1회·래치 뒤 트리거 무시·로딩 요청 1회·카운트다운 표시 없음), 시연 즉시 출격 플래그,
/// 회차 정책(새 회차 = 새 모델·runId 거름), 연결 종료 재평가·N=0, 재접속 rebind(명단·라커·T10 마스크)·매핑 없음 = 늦은 참가자, 늦은 참가자 L40 포함.
/// </summary>
public sealed class LobbySortieRunTests
{
    const int Run = 11;
    const string StepsPath = "Assets/9.ScriptableObject/Lobby/LobbyTutorialSteps.asset";
    const string RulesPath = "Assets/9.ScriptableObject/Lobby/LobbyRules.asset";

    static readonly LobbyTutorialStepKind[] Kinds =
    {
        LobbyTutorialStepKind.LockerVisit,  // 0 L00
        LobbyTutorialStepKind.Gather,       // 1 L10
        LobbyTutorialStepKind.Dialogue,     // 2
        LobbyTutorialStepKind.Activity,     // 3
        LobbyTutorialStepKind.Gather,       // 4 L40
        LobbyTutorialStepKind.Loading,      // 5 L41
    };

    const int SortieIndex = 4;
    const int LoadingIndex = 5;

    static LobbyTutorialFlow Create(int runId, params ulong[] clientIds)
    {
        var flow = new LobbyTutorialFlow(Kinds, runId);
        foreach (var clientId in clientIds)
            flow.AddClient(clientId);
        return flow;
    }

    static LobbyTutorialStepTable LoadSteps()
    {
        var table = AssetDatabase.LoadAssetAtPath<LobbyTutorialStepTable>(StepsPath);
        Assert.That(table, Is.Not.Null, StepsPath);
        return table;
    }

    static int Index(LobbyTutorialStepTable table, string stateId)
    {
        var index = table.IndexOf(stateId);
        Assert.That(index, Is.GreaterThanOrEqualTo(0), stateId);
        return index;
    }

    // ── L40/L41 출격 확정 ──

    [Test]
    public void 출격_확정은_한번_래치_뒤엔_로딩_행으로만_전이한다()
    {
        var flow = Create(Run, 0, 1);
        flow.LockRoster();
        flow.TryAdvanceTo(Run, 1, SortieIndex, 10, 0);

        Assert.That(flow.TryCommitLaunch(), Is.True);
        Assert.That(flow.TryCommitLaunch(), Is.False, "중복 확정 — 로딩 요청은 첫 확정에서만");
        Assert.That(flow.TryAdvanceTo(Run, flow.Revision, LoadingIndex, 11, 1, ignoreParticipants: true), Is.EqualTo(LobbyTutorialAdvanceResult.Advanced));
        Assert.That(flow.Snapshot.LaunchCommitted, Is.True);
        Assert.That(flow.StepIndex, Is.EqualTo(LoadingIndex));
    }

    [Test]
    public void 출격_확정_뒤_집결_대사_완료_보고는_무시된다()
    {
        var flow = Create(Run, 0);
        flow.LockRoster();
        flow.TryAdvanceTo(Run, 1, 2, 10, 0);   // 대사 행에서 (시연 즉시 출격처럼) 확정이 먼저 났다고 본다

        Assert.That(flow.TryCommitLaunch(), Is.True);
        Assert.That(flow.TryAdvance(Run, flow.Revision, 11, 1), Is.EqualTo(LobbyTutorialAdvanceResult.LaunchLocked), "대사 끝 → 다음 행");
        Assert.That(flow.TryAdvanceTo(Run, flow.Revision, SortieIndex, 11, 1), Is.EqualTo(LobbyTutorialAdvanceResult.LaunchLocked), "집결 행");
        Assert.That(flow.StepIndex, Is.EqualTo(2));
        Assert.That(flow.TryAdvanceTo(Run, flow.Revision, LoadingIndex, 11, 1, ignoreParticipants: true), Is.EqualTo(LobbyTutorialAdvanceResult.Advanced));
    }

    [Test]
    public void 확정_전에_미뤄_둔_전이는_확정_뒤_적용되지_않는다()
    {
        var flow = Create(Run, 0);
        flow.LockRoster();
        flow.TryAdvanceTo(Run, 1, 2, 10, 0);
        Assert.That(flow.TryAdvance(Run, 2, 10, 0), Is.EqualTo(LobbyTutorialAdvanceResult.Deferred));

        flow.TryCommitLaunch();
        Assert.That(flow.Tick(11), Is.False);
        Assert.That(flow.StepIndex, Is.EqualTo(2));
    }

    [Test]
    public void 출격_확정_뒤엔_모든_집결_구역을_끈다()
    {
        Assert.That(LobbyTutorialRules.IsZoneActive(LobbyGatherPoint.Sortie, LobbyGatherPoint.Sortie, false, false), Is.True);
        Assert.That(LobbyTutorialRules.IsZoneActive(LobbyGatherPoint.Sortie, LobbyGatherPoint.Sortie, false, true), Is.False);
        Assert.That(LobbyTutorialRules.IsZoneActive(LobbyGatherPoint.Brief, LobbyGatherPoint.Sortie, false, false), Is.False);
        Assert.That(LobbyTutorialRules.IsZoneActive(LobbyGatherPoint.None, LobbyGatherPoint.None, true, false), Is.False);
    }

    [Test]
    public void 단계표_L40_은_출격_집결_유지_0_3초_카운트다운_표시_없음_L41_은_로딩()
    {
        var table = LoadSteps();
        var l40 = table[Index(table, "L40_SortieGather")];
        var l41 = table[Index(table, "L41_Loading")];

        Assert.That(l40.kind, Is.EqualTo(LobbyTutorialStepKind.Gather));
        Assert.That(l40.gatherPoint, Is.EqualTo(LobbyGatherPoint.Sortie));
        Assert.That(l40.holdSeconds, Is.EqualTo(0.3f).Within(1e-4f));
        Assert.That(l40.countdownStateId, Is.Empty, "Q8 A — 출격 카운트다운 표시 없음");
        Assert.That(l40.progressMode, Is.EqualTo(LobbyTutorialProgressMode.Gather));
        Assert.That(l41.kind, Is.EqualTo(LobbyTutorialStepKind.Loading));
        Assert.That(Index(table, "L41_Loading"), Is.EqualTo(Index(table, "L40_SortieGather") + 1));
    }

    [Test]
    public void 옛_집결_표시는_튜토리얼_중_카운트다운_표시_행의_활성_구역만_남은_초를_보인다()
    {
        // L40·유지 0.3초 집결: 파사드 카운트다운 0 → 표시 없음(구역 카운트다운이 돌아도)
        Assert.That(LobbyTutorialCountdownDisplay.ZoneStatusCountdownSeconds(true, true, 0d, true, 0.3d), Is.EqualTo(0d));
        // 시연 즉시 출격 구역(활성 행 구역 아님)
        Assert.That(LobbyTutorialCountdownDisplay.ZoneStatusCountdownSeconds(true, false, 0d, true, 0.2d), Is.EqualTo(0d));
        // T20 카운트다운 표시 행
        Assert.That(LobbyTutorialCountdownDisplay.ZoneStatusCountdownSeconds(true, true, 2.4d, true, 2.4d), Is.EqualTo(2.4d));
        // 진행기 없음 = 옛 동작
        Assert.That(LobbyTutorialCountdownDisplay.ZoneStatusCountdownSeconds(false, false, 0d, true, 1.5d), Is.EqualTo(1.5d));
        Assert.That(LobbyTutorialCountdownDisplay.ZoneStatusCountdownSeconds(false, false, 0d, false, 1.5d), Is.EqualTo(0d));
    }

    // ── 늦은 참가자 L40 포함 ──

    [Test]
    public void 늦은_참가자도_출격_집결_인원에_들고_명단이_비어도_접속자_기준으로_로딩에_간다()
    {
        Assert.That(LobbyTutorialRules.UsesConnectedClients(LobbyGatherPoint.Sortie), Is.True);
        Assert.That(LobbyTutorialRules.UsesConnectedClients(LobbyGatherPoint.Brief), Is.False);

        var flow = Create(Run, 1);
        flow.LockRoster();
        flow.AddClient(2);                       // 늦은 참가자
        flow.RemoveClient(1);                    // 명단 전원 퇴장
        Assert.That(flow.IsWaitingLateJoiner(2), Is.True);
        Assert.That(flow.TryAdvanceTo(Run, 1, SortieIndex, 10, 0), Is.EqualTo(LobbyTutorialAdvanceResult.NoParticipants));
        Assert.That(flow.TryAdvanceTo(Run, 1, SortieIndex, 10, 0, ignoreParticipants: true), Is.EqualTo(LobbyTutorialAdvanceResult.Advanced));
        Assert.That(flow.TryCommitLaunch(), Is.True);
        Assert.That(flow.TryAdvanceTo(Run, 2, LoadingIndex, 11, 0, ignoreParticipants: true), Is.EqualTo(LobbyTutorialAdvanceResult.Advanced));
    }

    // ── 시연 즉시 출격 플래그(Q2 B) ──

    [Test]
    public void 즉시_출격은_플래그를_켰고_명단_고정_전_확정_전일_때만_열린다()
    {
        Assert.That(LobbyTutorialRules.InstantSortieOpen(false, false, false), Is.False, "기본 꺼짐 — L00~ 훈련 없이 출격 불가");
        Assert.That(LobbyTutorialRules.InstantSortieOpen(true, false, false), Is.True);
        Assert.That(LobbyTutorialRules.InstantSortieOpen(true, true, false), Is.False, "명단 고정(L10 확정) 뒤엔 정식 흐름");
        Assert.That(LobbyTutorialRules.InstantSortieOpen(true, false, true), Is.False);
    }

    [Test]
    public void 즉시_출격이_열리면_L00_에서도_출격_구역이_켜지고_꺼지면_꺼진다()
    {
        // L00 = 집결 행 아님(activePoint None)
        Assert.That(LobbyTutorialRules.IsZoneActive(LobbyGatherPoint.Sortie, LobbyGatherPoint.None, instantSortieOpen: true, launchCommitted: false), Is.True);
        Assert.That(LobbyTutorialRules.IsZoneActive(LobbyGatherPoint.Sortie, LobbyGatherPoint.None, instantSortieOpen: false, launchCommitted: false), Is.False);
        // L10 중앙 집결 중 — 중앙 + 즉시 출격 둘 다
        Assert.That(LobbyTutorialRules.IsZoneActive(LobbyGatherPoint.Brief, LobbyGatherPoint.Brief, true, false), Is.True);
        Assert.That(LobbyTutorialRules.IsZoneActive(LobbyGatherPoint.Sortie, LobbyGatherPoint.Brief, true, false), Is.True);
        Assert.That(LobbyTutorialRules.IsZoneActive(LobbyGatherPoint.Entrance, LobbyGatherPoint.Brief, true, false), Is.False);
    }

    [Test]
    public void 즉시_출격_경로는_L00_에서_명단_고정_확정_로딩으로_훈련을_건너뛴다()
    {
        var flow = Create(Run, 0, 1);

        flow.LockRoster();
        Assert.That(flow.TryCommitLaunch(), Is.True);
        Assert.That(flow.TryAdvanceTo(Run, 1, LoadingIndex, 10, 0, ignoreParticipants: true), Is.EqualTo(LobbyTutorialAdvanceResult.Advanced));
        Assert.That(flow.StepIndex, Is.EqualTo(LoadingIndex));
        Assert.That(LobbyTutorialRules.InstantSortieOpen(true, flow.IsRosterLocked, flow.LaunchCommitted), Is.False, "확정 뒤 다시 열리지 않는다");
    }

    [Test]
    public void LobbyRules_에셋의_즉시_출격_플래그는_기본_꺼짐()
    {
        var rules = AssetDatabase.LoadAssetAtPath<LobbyRules>(RulesPath);
        Assert.That(rules, Is.Not.Null, RulesPath);
        Assert.That(rules.AllowInstantSortie, Is.False);
    }

    // ── 회차 정책(Q3 A) ──

    [Test]
    public void 새_회차는_새_모델이라_L00_부터_라커_명단_확정이_비어_있다()
    {
        var previous = Create(Run, 0, 1);
        previous.MarkLockerVisited(0);
        previous.MarkLockerVisited(1);
        previous.LockRoster();
        previous.TryAdvanceTo(Run, 1, SortieIndex, 10, 0);
        previous.TryCommitLaunch();

        var next = Create(Run + 1, 0, 1);    // 판 끝 로비 복귀 = 진행기 재스폰 = runId +1
        Assert.That(next.RunId, Is.EqualTo(Run + 1));
        Assert.That(next.StepIndex, Is.EqualTo(0));
        Assert.That(next.CurrentKind, Is.EqualTo(LobbyTutorialStepKind.LockerVisit));
        Assert.That(next.LockerVisited, Is.Empty);
        Assert.That(next.IsRosterLocked, Is.False);
        Assert.That(next.Roster, Is.Empty);
        Assert.That(next.LaunchCommitted, Is.False);
        Assert.That(next.TrainingCleared, Is.Empty, "자동 클리어 부여 없음 — 매 회차 L00 부터");
    }

    [Test]
    public void 이전_회차의_늦은_콜백은_runId_로_걸러진다()
    {
        var next = Create(Run + 1, 0);

        Assert.That(next.AcceptsEvent(Run, 1, 99), Is.False);
        Assert.That(next.TryAdvance(Run, 1, 10, 0), Is.EqualTo(LobbyTutorialAdvanceResult.StaleRun));
        Assert.That(next.StepIndex, Is.EqualTo(0));
        Assert.That(next.AcceptsEvent(Run + 1, 1, 99), Is.True);
    }

    [Test]
    public void 새_회차_T10_체크_마스크는_처음부터()
    {
        var model = new LobbyWeaponCheckModel();
        model.Begin(new ulong[] { 0 });
        model.Report(0, LobbyWeaponCheckItem.Dash);
        model.Stop();

        model.Begin(new ulong[] { 0 });
        Assert.That(model.MaskOf(0), Is.EqualTo(0));
    }

    // ── 연결 종료 재평가 ──

    [Test]
    public void L00_전원_퇴장이면_N_0_이라_라커_완료도_전이도_없다()
    {
        var flow = Create(Run, 1, 2);
        flow.MarkLockerVisited(1);
        flow.RemoveClient(1);
        flow.RemoveClient(2);

        Assert.That(flow.AllSpawnedParticipantsVisitedLocker(_ => true), Is.False);
        Assert.That(flow.TryAdvance(Run, 1, 10, 0), Is.EqualTo(LobbyTutorialAdvanceResult.NoParticipants));
    }

    [Test]
    public void 미방문자_퇴장이면_감소한_N_으로_라커_완료()
    {
        var flow = Create(Run, 1, 2);
        flow.MarkLockerVisited(1);
        Assert.That(flow.AllSpawnedParticipantsVisitedLocker(_ => true), Is.False);

        flow.RemoveClient(2, "booth-2");
        Assert.That(flow.AllSpawnedParticipantsVisitedLocker(_ => true), Is.True);
    }

    [Test]
    public void 쓰러짐_사망은_집결_불인정()
    {
        Assert.That(new PartyGatherMemberState(true, true, false).Gathered, Is.False);
        Assert.That(new PartyGatherMemberState(true, false, false).Gathered, Is.True);
    }

    // ── 재접속 rebind(Q-T2 A) ──

    [Test]
    public void 재접속하면_명단_소속과_라커_방문을_새_clientId_로_옮긴다()
    {
        var flow = Create(Run, 0, 1, 2);
        flow.MarkLockerVisited(1);
        flow.MarkLockerVisited(2);
        flow.LockRoster();
        flow.TryAdvanceTo(Run, 1, 3, 10, 0);

        flow.RemoveClient(1, "booth-2");
        Assert.That(flow.Roster, Is.EqualTo(new ulong[] { 0, 2 }), "연결 종료 확정 = 명단에서 뺀다");
        Assert.That(flow.HasDepartedRecord("booth-2"), Is.True);

        flow.AddClient(7);
        Assert.That(flow.TryRebind(7, "booth-2", out var previous), Is.True);
        Assert.That(previous, Is.EqualTo(1UL));
        Assert.That(flow.Roster, Is.EqualTo(new ulong[] { 0, 2, 7 }));
        Assert.That(flow.HasVisitedLocker(7), Is.True);
        Assert.That(flow.IsWaitingLateJoiner(7), Is.False);
        Assert.That(flow.StepIndex, Is.EqualTo(3), "훈련 처음부터 재실행 없음");
        Assert.That(flow.HasVisitedLocker(2), Is.True, "다른 사람 기록 그대로");
        Assert.That(flow.HasDepartedRecord("booth-2"), Is.False, "기록은 한 번만 쓴다");
        Assert.That(flow.TryRebind(7, "booth-2", out _), Is.False);
    }

    [Test]
    public void 매핑을_못_찾으면_늦은_참가자()
    {
        var flow = Create(Run, 0, 1);
        flow.LockRoster();
        flow.RemoveClient(1);                // 키 없음(playerId 없이 접속)
        flow.AddClient(8);

        Assert.That(flow.TryRebind(8, "booth-2", out _), Is.False);
        Assert.That(flow.TryRebind(8, null, out _), Is.False);
        Assert.That(flow.IsWaitingLateJoiner(8), Is.True);
        Assert.That(flow.Roster, Is.EqualTo(new ulong[] { 0 }));
    }

    [Test]
    public void 명단_고정_전에_나갔다가_고정_뒤_돌아오면_늦은_참가자_라커_방문은_이어_감()
    {
        var flow = Create(Run, 0, 1);
        flow.MarkLockerVisited(1);
        flow.RemoveClient(1, "booth-2");
        flow.LockRoster();
        flow.AddClient(9);

        Assert.That(flow.TryRebind(9, "booth-2", out _), Is.True);
        Assert.That(flow.IsWaitingLateJoiner(9), Is.True, "명단 고정 때 없었다");
        Assert.That(flow.HasVisitedLocker(9), Is.True);
    }

    [Test]
    public void L00_중_재접속은_라커_방문을_이어_가_다시_안_가도_된다()
    {
        var flow = Create(Run, 0, 1);
        flow.MarkLockerVisited(0);
        flow.MarkLockerVisited(1);
        flow.RemoveClient(1, "booth-2");
        flow.AddClient(5);
        Assert.That(flow.AllSpawnedParticipantsVisitedLocker(_ => true), Is.False, "rebind 전엔 새 clientId 미방문");

        flow.TryRebind(5, "booth-2", out _);
        Assert.That(flow.AllSpawnedParticipantsVisitedLocker(_ => true), Is.True);
    }

    [Test]
    public void 접속자가_아닌_clientId_로는_rebind_하지_않는다()
    {
        var flow = Create(Run, 0, 1);
        flow.RemoveClient(1, "booth-2");

        Assert.That(flow.TryRebind(5, "booth-2", out _), Is.False);
        Assert.That(flow.HasDepartedRecord("booth-2"), Is.True);
    }

    [Test]
    public void T10_체크_마스크는_재접속_clientId_로_옮겨진다()
    {
        var model = new LobbyWeaponCheckModel();
        model.Begin(new ulong[] { 0, 1 });
        model.Report(1, LobbyWeaponCheckItem.Dash);
        model.Report(1, LobbyWeaponCheckItem.SkillQ);
        model.Report(0, LobbyWeaponCheckItem.BasicAttack);

        model.RemoveClient(1);
        Assert.That(model.Roster, Is.EqualTo(new ulong[] { 0 }));

        Assert.That(model.Rebind(1, 7), Is.True);
        Assert.That(model.Roster, Is.EqualTo(new ulong[] { 0, 7 }));
        Assert.That(model.MaskOf(7), Is.EqualTo(LobbyWeaponCheckRules.BitOf(LobbyWeaponCheckItem.Dash) | LobbyWeaponCheckRules.BitOf(LobbyWeaponCheckItem.SkillQ)));
        Assert.That(model.MaskOf(0), Is.EqualTo(LobbyWeaponCheckRules.BitOf(LobbyWeaponCheckItem.BasicAttack)), "다른 사람 마스크 그대로");
        Assert.That(model.Report(7, LobbyWeaponCheckItem.Dash), Is.False, "이미 한 항목은 다시 체크하지 않는다");
        Assert.That(model.Rebind(1, 8), Is.False, "한 번만");
    }

    [Test]
    public void T10_마스크_rebind_는_점검_중일_때만()
    {
        var model = new LobbyWeaponCheckModel();
        model.Begin(new ulong[] { 1 });
        model.RemoveClient(1);
        model.Stop();
        model.Begin(new ulong[] { 0 });

        Assert.That(model.Rebind(1, 7), Is.False, "지난 점검의 보관 마스크는 버린다");
        Assert.That(model.Rebind(9, 7), Is.False, "보관 마스크 없음");
    }

    [Test]
    public void 훈련장_안_단계에서_재접속한_명단_사람은_훈련장_안으로_옮긴다()
    {
        var table = LoadSteps();
        var steps = table.Steps;
        var trainEnd = LobbyTutorialRules.IndexOfGather(steps, LobbyGatherPoint.TrainEnd);

        Assert.That(LobbyTutorialRules.RebindTeleportTarget(steps, Index(table, "T22_Combat"), out var last), Is.EqualTo(LobbyGatherPoint.Train01Start));
        Assert.That(last, Is.EqualTo(trainEnd));
        Assert.That(LobbyTutorialRules.RebindTeleportTarget(steps, Index(table, "L21_TrainingIntro"), out _), Is.EqualTo(LobbyGatherPoint.Train01Start));
        Assert.That(LobbyTutorialRules.RebindTeleportTarget(steps, trainEnd, out _), Is.EqualTo(LobbyGatherPoint.Train01Start));
        Assert.That(LobbyTutorialRules.RebindTeleportTarget(steps, Index(table, "L20_TrainingGather"), out _), Is.EqualTo(LobbyGatherPoint.None), "입구 집결 = 로비 쪽");
        Assert.That(LobbyTutorialRules.RebindTeleportTarget(steps, Index(table, "L30_BriefGather"), out _), Is.EqualTo(LobbyGatherPoint.None));
        Assert.That(LobbyTutorialRules.RebindTeleportTarget(steps, Index(table, "L40_SortieGather"), out _), Is.EqualTo(LobbyGatherPoint.None));
    }
}
