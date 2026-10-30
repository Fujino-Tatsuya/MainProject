using NUnit.Framework;
using UnityEditor;

/// <summary>
/// 로비 튜토리얼 진행기 규칙 — 할 일 157 커밋 ②(옛 LobbyTrainingFlowTests 의 정적 규칙: 캐릭터 잠금·접속 차단·확정 텔레포트·멈춤 탈출·체력 하한을 단계표 기준으로 옮김).
/// 실제 단계표 에셋으로 L20 = 입구 집결(§10.3 안 A)·훈련 보정 범위도 본다.
/// </summary>
public sealed class LobbyTutorialRulesTests
{
    const string AssetPath = "Assets/9.ScriptableObject/Lobby/LobbyTutorialSteps.asset";

    static LobbyTutorialStepTable LoadAsset()
    {
        var table = AssetDatabase.LoadAssetAtPath<LobbyTutorialStepTable>(AssetPath);
        Assert.That(table, Is.Not.Null, AssetPath);
        return table;
    }

    static int Index(LobbyTutorialStepTable table, string stateId)
    {
        var index = table.IndexOf(stateId);
        Assert.That(index, Is.GreaterThanOrEqualTo(0), stateId);
        return index;
    }

    // ── 캐릭터 잠금·접속 차단 ──

    [Test]
    public void 캐릭터_변경_사유_우선순위_출격_카운트다운()
    {
        // 훈련 명단 잠금 없음(157 Q1, 커밋 ③)
        Assert.That(LobbyTutorialRules.CharacterChangeBlock(false, false), Is.EqualTo(LobbyCharacterChangeReject.None));
        Assert.That(LobbyTutorialRules.CharacterChangeBlock(false, true), Is.EqualTo(LobbyCharacterChangeReject.CountdownActive));
        Assert.That(LobbyTutorialRules.CharacterChangeBlock(true, true), Is.EqualTo(LobbyCharacterChangeReject.GameStarting));
    }

    [Test]
    public void 접속_차단은_출격_카운트다운_중과_출격_확정_뒤만()
    {
        Assert.That(LobbyTutorialRules.BlocksNewJoins(false, false), Is.False, "훈련·브리핑 중 = 로비 대기로 받는다");
        Assert.That(LobbyTutorialRules.BlocksNewJoins(false, true), Is.True);
        Assert.That(LobbyTutorialRules.BlocksNewJoins(true, false), Is.True);
    }

    [TestCase(false, false, DemoJoinRejectReason.None)]
    [TestCase(false, true, DemoJoinRejectReason.GameInProgress)]
    [TestCase(true, false, DemoJoinRejectReason.GameInProgress)]
    public void 접속_승인_데모_규칙과_합치면(bool launching, bool sortieCountdown, DemoJoinRejectReason expected)
    {
        var request = new DemoJoinRequest(
            isHostSelf: false, isDemoFlow: true, isInLobby: true,
            isCountdownActive: LobbyTutorialRules.BlocksNewJoins(launching, sortieCountdown),
            connectedCount: 1, maxPlayers: DemoJoinRule.MaxPlayers, isPlayerIdValid: true, isPlayerIdConnected: false);

        Assert.That(DemoJoinRule.Evaluate(request), Is.EqualTo(expected));
    }

    // ── 구역·텔레포트 ──

    [TestCase(LobbyGatherPoint.Entrance, LobbyGatherPoint.Train01Start)]
    [TestCase(LobbyGatherPoint.TrainEnd, LobbyGatherPoint.Brief)]
    [TestCase(LobbyGatherPoint.Brief, LobbyGatherPoint.None)]
    [TestCase(LobbyGatherPoint.Train02Start, LobbyGatherPoint.None)]
    [TestCase(LobbyGatherPoint.Sortie, LobbyGatherPoint.None)]
    public void 확정_텔레포트_대상_구역(LobbyGatherPoint confirmed, LobbyGatherPoint expected)
    {
        Assert.That(LobbyTutorialRules.TeleportTargetOnConfirm(confirmed), Is.EqualTo(expected));
    }

    [Test]
    public void 출격만_접속자_전원()
    {
        Assert.That(LobbyTutorialRules.UsesConnectedClients(LobbyGatherPoint.Sortie), Is.True);
        Assert.That(LobbyTutorialRules.UsesConnectedClients(LobbyGatherPoint.Entrance), Is.False);
        Assert.That(LobbyTutorialRules.UsesConnectedClients(LobbyGatherPoint.Brief), Is.False);
    }

    [Test]
    public void L20_은_입구_집결이고_확정_텔레포트로_바로_L21_훈련장_안()
    {
        var table = LoadAsset();
        var l20 = table[Index(table, "L20_TrainingGather")];

        Assert.That(l20.kind, Is.EqualTo(LobbyTutorialStepKind.Gather));
        Assert.That(l20.gatherPoint, Is.EqualTo(LobbyGatherPoint.Entrance), "157 §10.3 안 A");
        Assert.That(LobbyTutorialRules.TeleportTargetOnConfirm(l20.gatherPoint), Is.EqualTo(LobbyGatherPoint.Train01Start));
        Assert.That(table[Index(table, "L21_TrainingIntro")].kind, Is.EqualTo(LobbyTutorialStepKind.Dialogue), "재집결 없이 바로 훈련 도입 대사");
    }

    [Test]
    public void 훈련장_안_시작_위치는_텔레포트_도착지일_뿐_집결_행이_아니다()
    {
        // 157 커밋 ④(§10.3 A): Train01Start 재집결 단계·구역 활성 없음 — L20 확정 텔레포트가 '문 통과'를 대신한다.
        var table = LoadAsset();
        Assert.That(LobbyTutorialRules.IndexOfGather(table.Steps, LobbyGatherPoint.Train01Start), Is.EqualTo(-1));
        Assert.That(LobbyTutorialRules.IndexOfGather(table.Steps, LobbyGatherPoint.Entrance), Is.EqualTo(Index(table, "L20_TrainingGather")),
            "입구 집결은 L20 한 번");
    }

    [Test]
    public void L21_대사_동안은_표적_미스폰_훈련_판정_시작_안_함()
    {
        var table = LoadAsset();
        var l21 = table[Index(table, "L21_TrainingIntro")];

        Assert.That(l21.activity, Is.EqualTo(LobbyTutorialActivity.None));
        Assert.That(LobbyTrainingSupportRules.Train01TargetsActive(l21.activity), Is.False, "표적·T10 체크는 T10(BasicCheck) 진입 때 시작");
        Assert.That(l21.checklistKeys.Length, Is.Zero);
        Assert.That(Index(table, "T10_Basic"), Is.EqualTo(Index(table, "L21_TrainingIntro") + 1), "대사 끝 = T10");
    }

    [Test]
    public void L20_확정_뒤_같은_프레임에_L21_을_건너뛰지_않는다()
    {
        var table = LoadAsset();
        var l20 = Index(table, "L20_TrainingGather");
        var flow = new LobbyTutorialFlow(table.Kinds(), 3);
        flow.AddClient(0);
        Assert.That(flow.TryAdvanceTo(3, flow.Revision, l20, 1, 0), Is.EqualTo(LobbyTutorialAdvanceResult.Advanced));

        // 입구 집결 확정 → L21 (텔레포트는 이 전이와 같은 호출에서 한 번)
        Assert.That(flow.TryAdvance(3, flow.Revision, 10, 1), Is.EqualTo(LobbyTutorialAdvanceResult.Advanced));
        Assert.That(flow.StepIndex, Is.EqualTo(l20 + 1));

        // 같은 프레임 연쇄(L21 → T10)는 다음 프레임으로 미뤄진다
        Assert.That(flow.TryAdvance(3, flow.Revision, 10, 1), Is.EqualTo(LobbyTutorialAdvanceResult.Deferred));
        Assert.That(flow.StepIndex, Is.EqualTo(l20 + 1));
    }

    [Test]
    public void 확정_텔레포트_재시도는_다음_집결_행까지()
    {
        var table = LoadAsset();
        Assert.That(LobbyTutorialRules.TeleportRetryLastIndex(table.Steps, Index(table, "L20_TrainingGather")), Is.EqualTo(Index(table, "T20_CombatGather")),
            "L21·T10·T20 동안 다시 옮긴다(문이 막혀 걸어 못 들어간다)");
        Assert.That(LobbyTutorialRules.TeleportRetryLastIndex(table.Steps, Index(table, "T50_FreePractice")), Is.EqualTo(Index(table, "L30_BriefGather")));
        Assert.That(LobbyTutorialRules.TeleportRetryLastIndex(table.Steps, table.Count - 1), Is.EqualTo(table.Count - 1), "뒤에 집결 없음 = 마지막 행");
        Assert.That(LobbyTutorialRules.TeleportRetryLastIndex(null, 0), Is.EqualTo(-1));
    }

    [Test]
    public void T50_종료_확정_텔레포트_도착지는_L30_중앙_집결()
    {
        var table = LoadAsset();
        var t50 = table[Index(table, "T50_FreePractice")];
        var l30 = table[Index(table, "L30_BriefGather")];

        Assert.That(LobbyTutorialRules.TeleportTargetOnConfirm(t50.gatherPoint), Is.EqualTo(l30.gatherPoint));
    }

    // ── 행 찾기 ──

    [Test]
    public void 명단_고정_행은_첫_집결_L10()
    {
        var table = LoadAsset();
        Assert.That(LobbyTutorialRules.RosterLockIndex(table.Steps), Is.EqualTo(Index(table, "L10_CentralGather")));
    }

    [Test]
    public void 브리핑_잠금_묶음_뒤는_L40_출격_집결()
    {
        var table = LoadAsset();
        var after = LobbyTutorialRules.IndexAfterLockedBlock(table.Steps, Index(table, "L31_BriefIntro"));

        Assert.That(after, Is.EqualTo(Index(table, "L40_SortieGather")));
        Assert.That(LobbyTutorialRules.IndexOfGather(table.Steps, LobbyGatherPoint.Sortie), Is.EqualTo(after));
        Assert.That(LobbyTutorialRules.IndexOfKind(table.Steps, LobbyTutorialStepKind.Loading), Is.EqualTo(Index(table, "L41_Loading")));
    }

    [Test]
    public void 잠금_묶음_밖이면_다음_행()
    {
        var table = LoadAsset();
        var t10 = Index(table, "T10_Basic");
        Assert.That(LobbyTutorialRules.IndexAfterLockedBlock(table.Steps, t10), Is.EqualTo(t10 + 1));
    }

    [Test]
    public void 훈련_상대_단계는_T31_부터_T40()
    {
        Assert.That(LobbyTutorialRules.IsSparringActivity(LobbyTutorialActivity.Counter), Is.True);
        Assert.That(LobbyTutorialRules.IsSparringActivity(LobbyTutorialActivity.ResetPosition), Is.True);
        Assert.That(LobbyTutorialRules.IsSparringActivity(LobbyTutorialActivity.SuppressKill), Is.True);
        Assert.That(LobbyTutorialRules.IsSparringActivity(LobbyTutorialActivity.Combat), Is.False);
        Assert.That(LobbyTutorialRules.IsSparringActivity(LobbyTutorialActivity.FreePractice), Is.False);
        Assert.That(LobbyTutorialRules.IsSparringActivity(LobbyTutorialActivity.None), Is.False);
    }

    // ── 훈련 보정(체력 하한) ──

    [TestCase("L00_LockerVisit", 0)]
    [TestCase("L10_CentralGather", 0)]
    [TestCase("L20_TrainingGather", 0)]
    [TestCase("L21_TrainingIntro", 1)]
    [TestCase("T10_Basic", 1)]
    [TestCase("T20_CombatGather", 1)]
    [TestCase("T22_Combat", 1)]
    [TestCase("T31_Counter", 1)]
    [TestCase("T34_Wall", 1)]
    [TestCase("T40_SuppressKill", 1)]
    [TestCase("T50_FreePractice", 0)]
    [TestCase("L31_BriefIntro", 0)]
    [TestCase("L40_SortieGather", 0)]
    public void 체력_하한은_훈련장_안_단계만_옛_Train01_부터_Train03D(string stateId, int expected)
    {
        var table = LoadAsset();
        Assert.That(LobbyTutorialRules.HealthFloorFor(table[Index(table, stateId)], inLockedRoster: true), Is.EqualTo(expected));
    }

    [Test]
    public void 명단_밖은_체력_하한_없음()
    {
        var table = LoadAsset();
        Assert.That(LobbyTutorialRules.HealthFloorFor(table[Index(table, "T10_Basic")], inLockedRoster: false), Is.Zero);
        Assert.That(LobbyTutorialRules.HealthFloorFor(null, inLockedRoster: true), Is.Zero);
    }

    // ── 멈춤 탈출 지점 ──

    [TestCase("L20_TrainingGather", LobbyGatherPoint.Entrance)]
    [TestCase("L21_TrainingIntro", LobbyGatherPoint.Train01Start)]
    [TestCase("T10_Basic", LobbyGatherPoint.Train01Start)]
    [TestCase("T20_CombatGather", LobbyGatherPoint.Train02Start)]
    [TestCase("T22_Combat", LobbyGatherPoint.Train02Start)]
    [TestCase("T30_CounterGather", LobbyGatherPoint.Train03Area)]
    [TestCase("T33_ResetPosition", LobbyGatherPoint.Train03Area)]
    [TestCase("T40_SuppressKill", LobbyGatherPoint.Train03Area)]
    [TestCase("T50_FreePractice", LobbyGatherPoint.TrainEnd)]
    [TestCase("L30_BriefGather", LobbyGatherPoint.Brief)]
    [TestCase("L32_BriefFactory", LobbyGatherPoint.Brief)]
    [TestCase("L40_SortieGather", LobbyGatherPoint.None)]
    [TestCase("L41_Loading", LobbyGatherPoint.None)]
    public void 멈춤_탈출_지점_명단은_단계_구역(string stateId, LobbyGatherPoint expected)
    {
        var table = LoadAsset();
        Assert.That(LobbyTutorialRules.RecoveryPointFor(table.Steps, Index(table, stateId), inLockedRoster: true), Is.EqualTo(expected));
    }

    [TestCase("L00_LockerVisit")]
    [TestCase("T22_Combat")]
    [TestCase("L30_BriefGather")]
    public void 멈춤_탈출_지점_명단_밖은_스폰_지점(string stateId)
    {
        var table = LoadAsset();
        Assert.That(LobbyTutorialRules.RecoveryPointFor(table.Steps, Index(table, stateId), inLockedRoster: false), Is.EqualTo(LobbyGatherPoint.None));
    }

    [Test]
    public void 멈춤_탈출_범위_밖_행은_스폰_지점()
    {
        var table = LoadAsset();
        Assert.That(LobbyTutorialRules.RecoveryPointFor(table.Steps, -1, true), Is.EqualTo(LobbyGatherPoint.None));
        Assert.That(LobbyTutorialRules.RecoveryPointFor(table.Steps, table.Count, true), Is.EqualTo(LobbyGatherPoint.None));
        Assert.That(LobbyTutorialRules.RecoveryPointFor(null, 0, true), Is.EqualTo(LobbyGatherPoint.None));
    }
}
