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
    public void 캐릭터_변경_사유_우선순위_출격_카운트다운_명단()
    {
        Assert.That(LobbyTutorialRules.CharacterChangeBlock(false, false, false), Is.EqualTo(LobbyCharacterChangeReject.None));
        Assert.That(LobbyTutorialRules.CharacterChangeBlock(false, true, false), Is.EqualTo(LobbyCharacterChangeReject.TrainingLocked));
        Assert.That(LobbyTutorialRules.CharacterChangeBlock(false, true, true), Is.EqualTo(LobbyCharacterChangeReject.CountdownActive));
        Assert.That(LobbyTutorialRules.CharacterChangeBlock(true, false, true), Is.EqualTo(LobbyCharacterChangeReject.GameStarting), "명단 밖도");
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
