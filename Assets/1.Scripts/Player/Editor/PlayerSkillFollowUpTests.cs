using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 공용 후속 시전(할 일 128 5a) — 칸 교체·후속 사용 복귀·쿨 시작 시점·창 만료·강제 복귀·오너 예측/서버 거부 원복·재접속 복원·미연결 스킬 기존 동작.
/// 배선(PlayerSkillController)이 쓰는 순수 규칙과 상태만 본다. 쿨 장부는 실제 <see cref="PlayerSkillCooldownLedger"/> 로 시작 시점을 확인한다.
/// </summary>
public sealed class PlayerSkillFollowUpTests
{
    private const int Q = (int)PlayerSkillSlot.Main;
    private const int E = (int)PlayerSkillSlot.Sub;
    private const int SourceIndex = 0;
    private const int FollowUpIndex = 1;
    private const int OtherIndex = 2;
    private const float SourceCooldown = 8f;

    private readonly List<GameObject> created = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in created)
            Object.DestroyImmediate(go);
        created.Clear();
    }

    // ── 승인 분류·쿨 시작 시점 ──

    [Test]
    public void Classify_LinkedAndOpens_OpensFollowUp_AndDoesNotStartCooldown()
    {
        PlayerSkillFollowUpKind kind = PlayerSkillFollowUpRules.Classify(false, true, true);

        Assert.That(kind, Is.EqualTo(PlayerSkillFollowUpKind.OpenFollowUp));
        Assert.That(PlayerSkillFollowUpRules.StartsCooldownOnApproval(kind, false), Is.False, "원래 스킬 사용 시점엔 쿨 미시작");
    }

    [Test]
    public void Classify_LinkedButConditionFails_IsNormal_AndStartsCooldownAsBefore()
    {
        PlayerSkillFollowUpKind kind = PlayerSkillFollowUpRules.Classify(false, true, false);

        Assert.That(kind, Is.EqualTo(PlayerSkillFollowUpKind.Normal));
        Assert.That(PlayerSkillFollowUpRules.StartsCooldownOnApproval(kind, false), Is.True, "후속이 안 열리면 기존대로 사용 시 쿨 시작");
    }

    [Test]
    public void Classify_Unlinked_KeepsExistingBehaviour()
    {
        PlayerSkillFollowUpKind kind = PlayerSkillFollowUpRules.Classify(false, false, true);

        Assert.That(kind, Is.EqualTo(PlayerSkillFollowUpKind.Normal));
        Assert.That(PlayerSkillFollowUpRules.StartsCooldownOnApproval(kind, false), Is.True, "자동 커밋 = 승인 때 쿨");
        Assert.That(PlayerSkillFollowUpRules.StartsCooldownOnApproval(kind, true), Is.False, "수동 커밋 = 스킬이 시작(기존)");
    }

    [Test]
    public void Classify_OpenFollowUpInSlot_IsUse_AndHasNoOwnCooldown()
    {
        PlayerSkillFollowUpKind kind = PlayerSkillFollowUpRules.Classify(true, false, false);

        Assert.That(kind, Is.EqualTo(PlayerSkillFollowUpKind.UseFollowUp));
        Assert.That(PlayerSkillFollowUpRules.StartsCooldownOnApproval(kind, false), Is.False);
    }

    // ── 칸 교체·후속 사용 ──

    [Test]
    public void Open_SwapsSlotToFollowUp_OnlyForLinkedSetSkill()
    {
        FakeSkill followUp = CreateSkill(null);
        FakeSkill source = CreateSkill(followUp);
        FakeSkill unlinked = CreateSkill(null);
        var model = new PlayerSkillFollowUpModel();

        Assert.That(PlayerSkillFollowUpRules.Resolve(source, model.OpenMask, Q), Is.SameAs(source), "열리기 전 = 원래 스킬");

        Assert.That(model.Open(Q, SourceIndex, FollowUpIndex, 42), Is.True);

        Assert.That(model.OpenMask, Is.EqualTo((byte)(1 << Q)));
        Assert.That(PlayerSkillFollowUpRules.Resolve(source, model.OpenMask, Q), Is.SameAs(followUp), "칸 = 후속 스킬");
        Assert.That(PlayerSkillFollowUpRules.Resolve(unlinked, model.OpenMask, Q), Is.SameAs(unlinked), "후속 미연결 스킬은 비트가 있어도 그대로");
        Assert.That(PlayerSkillFollowUpRules.Resolve(source, model.OpenMask, E), Is.SameAs(source), "다른 칸은 그대로");
        Assert.That(model.CastIdOf(Q), Is.EqualTo(42), "후속이 읽을 원래 시전 ID");
        Assert.That(model.HoldsSource(SourceIndex), Is.True, "창 동안 원래 스킬 쿨 보류");
    }

    [Test]
    public void UseFollowUp_RevertsToSource_AndStartsSourceCooldownThen()
    {
        FakeSkill followUp = CreateSkill(null);
        FakeSkill source = CreateSkill(followUp);
        var model = new PlayerSkillFollowUpModel();
        var ledger = new PlayerSkillCooldownLedger(3);

        // 원래 스킬 사용(t=0) — 쿨 미시작
        StartIfApproval(ledger, SourceIndex, PlayerSkillFollowUpRules.Classify(false, true, true), 0f);
        model.Open(Q, SourceIndex, FollowUpIndex, 7);
        Assert.That(ledger.IsReady(SourceIndex, 1f), Is.True, "원래 사용 시점엔 쿨 없음");

        // 후속 사용(t=2) — 복귀 + 원래 쿨 시작
        PlayerSkillFollowUpKind useKind = PlayerSkillFollowUpRules.Classify(model.FollowUpOf(Q) == FollowUpIndex, false, false);
        StartIfApproval(ledger, FollowUpIndex, useKind, 2f);
        int closed = model.Consume(Q, FollowUpIndex);
        ledger.Start(closed, 2f, SourceCooldown);

        Assert.That(closed, Is.EqualTo(SourceIndex));
        Assert.That(model.OpenMask, Is.Zero);
        Assert.That(PlayerSkillFollowUpRules.Resolve(source, model.OpenMask, Q), Is.SameAs(source), "원래 스킬로 복귀");
        Assert.That(ledger.GetRemaining(SourceIndex, 2f), Is.EqualTo(SourceCooldown), "쿨은 후속 사용 시점부터");
        Assert.That(ledger.IsReady(FollowUpIndex, 2f), Is.True, "후속 자체 쿨 없음");
        Assert.That(model.HoldsSource(SourceIndex), Is.False);
    }

    [Test]
    public void Consume_WrongSkill_DoesNotClose()
    {
        var model = new PlayerSkillFollowUpModel();
        model.Open(Q, SourceIndex, FollowUpIndex, 1);

        Assert.That(model.Consume(Q, OtherIndex), Is.EqualTo(-1));
        Assert.That(model.IsOpen(Q), Is.True);
        Assert.That(model.Consume(E, FollowUpIndex), Is.EqualTo(-1), "안 열린 칸");
    }

    // ── 창 ──

    [Test]
    public void Window_NotStarted_NeverExpires()
    {
        var model = new PlayerSkillFollowUpModel();
        model.Open(Q, SourceIndex, FollowUpIndex, 1);

        Assert.That(model.CollectExpired(1000f, new List<int>()), Is.Zero, "창 시작 전(원래 스킬 실행 중)은 만료 없음");
        Assert.That(model.IsOpen(Q), Is.True);
    }

    [Test]
    public void Window_Expires_RevertsAndReturnsSourceForCooldownStart()
    {
        var model = new PlayerSkillFollowUpModel();
        var ledger = new PlayerSkillCooldownLedger(2);
        var closed = new List<int>();
        model.Open(Q, SourceIndex, FollowUpIndex, 1);

        Assert.That(model.BeginWindow(Q, SourceIndex, 10f, 3f), Is.True);
        Assert.That(model.CollectExpired(12.9f, closed), Is.Zero);
        Assert.That(model.IsOpen(Q), Is.True);

        Assert.That(model.CollectExpired(13f, closed), Is.EqualTo(1));
        foreach (int source in closed)
            ledger.Start(source, 13f, SourceCooldown);

        Assert.That(closed, Is.EqualTo(new[] { SourceIndex }));
        Assert.That(model.OpenMask, Is.Zero);
        Assert.That(ledger.GetRemaining(SourceIndex, 13f), Is.EqualTo(SourceCooldown), "쿨은 창이 끝난 순간부터");
    }

    [Test]
    public void Window_StartsOnce_EarlyStartIsNotExtendedBySkillEnd()
    {
        var model = new PlayerSkillFollowUpModel();
        model.Open(Q, SourceIndex, FollowUpIndex, 1);

        Assert.That(model.BeginWindow(Q, SourceIndex, 5f, 2f), Is.True, "스킬 지정 시작(첫 돌진 종료)");
        Assert.That(model.BeginWindow(Q, SourceIndex, 6f, 2f), Is.False, "스킬 종료 시 기본 시작은 무시");
        Assert.That(model.WindowEndOf(Q), Is.EqualTo(7f));
        Assert.That(model.BeginWindow(Q, OtherIndex, 5f, 2f), Is.False, "다른 원래 스킬");
    }

    [Test]
    public void Window_ZeroSeconds_ExpiresOnNextCheck()
    {
        var model = new PlayerSkillFollowUpModel();
        model.Open(Q, SourceIndex, FollowUpIndex, 1);
        model.BeginWindow(Q, SourceIndex, 4f, 0f);

        Assert.That(model.CollectExpired(4f, null), Is.EqualTo(1));
    }

    // ── 강제 복귀(사망·세트 전환·씬 이동·막힘) ──

    [Test]
    public void CloseAll_DeathSceneOrBlocked_RevertsEverySlotAndReturnsSources()
    {
        var model = new PlayerSkillFollowUpModel();
        var closed = new List<int>();
        model.Open(Q, SourceIndex, FollowUpIndex, 1);
        model.Open(E, OtherIndex, 3, 2);

        Assert.That(model.CloseAll(closed), Is.EqualTo(2));

        Assert.That(closed, Is.EquivalentTo(new[] { SourceIndex, OtherIndex }), "닫힌 원래 스킬마다 쿨 시작");
        Assert.That(model.OpenMask, Is.Zero);
        Assert.That(model.CloseAll(closed), Is.Zero, "두 번째 호출은 아무것도 안 함");
    }

    [Test]
    public void ForceClose_SetChange_RevertsOnlyThatSlot()
    {
        var model = new PlayerSkillFollowUpModel();
        model.Open(Q, SourceIndex, FollowUpIndex, 1);
        model.Open(E, OtherIndex, 3, 2);

        Assert.That(model.ForceClose(Q), Is.EqualTo(SourceIndex));

        Assert.That(model.IsOpen(Q), Is.False);
        Assert.That(model.IsOpen(E), Is.True, "세트가 안 바뀐 칸은 유지");
        Assert.That(model.ForceClose(Q), Is.EqualTo(-1));
    }

    // ── 원격 오너 예측 ──

    [Test]
    public void Prediction_OpenThenServerConfirms_ClearsPrediction()
    {
        var prediction = new PlayerSkillFollowUpPrediction();

        prediction.Predict(Q, true);
        Assert.That(prediction.Effective(0), Is.EqualTo((byte)(1 << Q)), "서버 확정 전 즉시 칸 교체");

        prediction.ResolveServerMask((byte)(1 << Q));
        Assert.That(prediction.HasPrediction, Is.False);
        Assert.That(prediction.Effective((byte)(1 << Q)), Is.EqualTo((byte)(1 << Q)));
    }

    [Test]
    public void Prediction_ServerRejects_RestoresServerValue()
    {
        var prediction = new PlayerSkillFollowUpPrediction();

        prediction.Predict(Q, true);
        prediction.Reject();
        Assert.That(prediction.Effective(0), Is.Zero, "거부된 열기 예측 원복");

        prediction.Predict(Q, false);
        Assert.That(prediction.Effective((byte)(1 << Q)), Is.Zero, "후속 사용 예측 = 즉시 원래 칸");
        prediction.Reject();
        Assert.That(prediction.Effective((byte)(1 << Q)), Is.EqualTo((byte)(1 << Q)), "거부된 사용 예측 원복(서버는 여전히 후속)");
    }

    [Test]
    public void Prediction_UnrelatedServerChange_KeepsPrediction()
    {
        var prediction = new PlayerSkillFollowUpPrediction();
        prediction.Predict(Q, true);

        prediction.ResolveServerMask((byte)(1 << E));

        Assert.That(prediction.HasPrediction, Is.True, "예측한 칸과 다른 서버 변화로는 안 내린다");
        Assert.That(prediction.Effective((byte)(1 << E)), Is.EqualTo((byte)((1 << Q) | (1 << E))));
    }

    [Test]
    public void Prediction_ApprovedButServerDisagrees_FollowsServerAfterGrace()
    {
        var prediction = new PlayerSkillFollowUpPrediction();
        prediction.Predict(Q, true);

        Assert.That(prediction.Tick(10f, 0.5f), Is.False, "승인 전에는 유예가 돌지 않는다");
        prediction.MarkApproved(10f);
        Assert.That(prediction.Tick(10.4f, 0.5f), Is.False);
        Assert.That(prediction.Tick(10.5f, 0.5f), Is.True);
        Assert.That(prediction.Effective(0), Is.Zero, "서버가 최종");
    }

    [Test]
    public void Rejoin_ReplicatedMaskAlone_RestoresFollowUpSlot()
    {
        FakeSkill followUp = CreateSkill(null);
        FakeSkill source = CreateSkill(followUp);
        var server = new PlayerSkillFollowUpModel();
        server.Open(Q, SourceIndex, FollowUpIndex, 1);

        var rejoinedOwner = new PlayerSkillFollowUpPrediction(); // 재접속 = 예측 없음, 복제값만
        byte effective = rejoinedOwner.Effective(server.OpenMask);

        Assert.That(effective, Is.EqualTo((byte)(1 << Q)));
        Assert.That(PlayerSkillFollowUpRules.Resolve(source, effective, Q), Is.SameAs(followUp));
    }

    // ── 도우미 ──

    private static void StartIfApproval(PlayerSkillCooldownLedger ledger, int index, PlayerSkillFollowUpKind kind, float now)
    {
        if (PlayerSkillFollowUpRules.StartsCooldownOnApproval(kind, false))
            ledger.Start(index, now, SourceCooldown);
    }

    private FakeSkill CreateSkill(PlayerSkillBase followUp)
    {
        var go = new GameObject("FollowUpTestSkill");
        created.Add(go);
        FakeSkill skill = go.AddComponent<FakeSkill>();
        skill.Link = followUp;
        return skill;
    }

    private sealed class FakeSkill : PlayerSkillBase
    {
        public PlayerSkillBase Link;
        public override PlayerSkillSlot Slot => PlayerSkillSlot.Main;
        public override PlayerSkillBase FollowUpSkill => Link;
        public override void OnServerStart(Vector3 direction, Unit target) { }
        public override void OnClientPlay(Vector3 direction) { }
    }
}
