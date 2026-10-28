using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 128 (5b) — 그림자 회귀(ASN_2_RETURN) 수용 조건. 칸 교체·창·쿨 시작 시점은 공용 후속 시전 장치의 순수 상태
/// (<see cref="PlayerSkillFollowUpRules"/>·<see cref="PlayerSkillFollowUpModel"/>)에 어쌔신 규칙(<see cref="AssassinShadowReturnRules"/>)을 얹어 본다.
/// 수치는 프로젝트 정의 에셋, 연결은 Player_Assassin 프리팹(일반·변신 Q → 복귀 스킬).
/// </summary>
public sealed class AssassinTraitShadowReturnTests
{
    const float Eps = 1e-4f;

    const string Return = "ASN_2_RETURN";
    const string Restraint = "ASN_2_RESTRAINT";
    const string Breakthrough = "ASN_1_Q_COOLDOWN";
    const string Trail = "ASN_4_Q_TRAIL";
    const string AssassinPrefab = "Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab";

    const int Q = (int)PlayerSkillSlot.Main;
    const int SourceIndex = 0;
    const int ReturnIndex = 1;
    const int TransformedSourceIndex = 2;
    const int TransformedReturnIndex = 3;
    const float NormalQCooldown = 5f;

    static readonly TraitAttackKey NormalQ = new TraitAttackKey(AttackType.SkillQ, TraitAttackForm.Normal);
    static readonly TraitAttackKey TransformedQ = new TraitAttackKey(AttackType.SkillQ, TraitAttackForm.Alternate);

    // ── 도우미 ──────────────────────────────────────────────────────────

    static TraitDefinition Load(string id)
    {
        TraitCharacterPool pool = TraitContentTestKit.CharacterPool(TutorialCharacter.Assassin);
        Assert.IsNotNull(pool, "어쌔신 캐릭터 풀");
        TraitDefinition def = pool.Traits.FirstOrDefault(t => t != null && t.Id == id);
        Assert.IsNotNull(def, $"TraitPool_Assassin 에 {id} 정의가 있어야 한다");
        Assert.IsInstanceOf<AssassinTraitDefinition>(def);
        return def;
    }

    static TraitModifierSet SetOf(params string[] ids)
    {
        var table = new Dictionary<int, ITraitModifierContributor>();
        foreach (string id in ids.Distinct())
            table[TraitIdHash.Of(id)] = Load(id);

        var set = new TraitModifierSet();
        set.Rebuild(ids.Select(TraitIdHash.Of).ToArray(), hash => table.TryGetValue(hash, out ITraitModifierContributor c) ? c : null);
        Assert.IsEmpty(set.Errors, string.Join("\n", set.Errors));
        return set;
    }

    static string Card(string id)
    {
        TraitDefinition def = Load(id);
        Assert.IsTrue(TraitTooltip.Format(def, def.InlineDescription, null, false, TraitContent.Build(), out string text, out string error), error);
        return text;
    }

    // 원래 Q 승인 — 장치 분류(복귀 연결 + 특성 보유 여부)와 승인 때 쿨 시작 여부.
    static PlayerSkillFollowUpKind ApproveSource(bool hasTrait) =>
        PlayerSkillFollowUpRules.Classify(false, true, AssassinShadowReturnRules.OpensReturn(true, hasTrait));

    // 장치가 복귀(닫힘) 때 하는 일 — 준비된 원래 스킬이면 쿨 시작(StartCooldownNow 의 IsReady 가드와 같다).
    static void StartIfReady(PlayerSkillCooldownLedger ledger, int sourceIndex, float now, float cooldown)
    {
        if (sourceIndex >= 0 && ledger.IsReady(sourceIndex, now))
            ledger.Start(sourceIndex, now, cooldown);
    }

    // ── 정의 데이터·카드·풀 ─────────────────────────────────────────────

    [Test]
    public void Definition_SpecNameCategory_CooldownAndSkillValues_Valid_AfterRestraintInPool()
    {
        TraitDefinition def = Load(Return);
        Assert.AreEqual(Return, def.name, "파일 이름 = 특성 ID(시트 열 이름)");
        Assert.AreEqual("그림자 회귀", def.InlineDisplayName);
        Assert.AreEqual("Q 변경 · 왕복 공격", def.InlineCategory);
        Assert.AreEqual(2, def.Effects.Count, "effects[0] Q 쿨 · [1] 스킬 수치(창·복귀 비율)");
        Assert.IsInstanceOf<TraitCooldownEffect>(def.Effects[0]);
        Assert.IsInstanceOf<TraitSkillValueEffect>(def.Effects[1]);
        var errors = new List<string>();
        def.Validate(errors);
        Assert.IsEmpty(errors, string.Join("\n", errors));

        string[] poolIds = TraitContentTestKit.CharacterPool(TutorialCharacter.Assassin).Traits.Select(t => t.Id).ToArray();
        Assert.AreEqual(Array.IndexOf(poolIds, Restraint) + 1, Array.IndexOf(poolIds, Return), "풀에서 절제된 분노 바로 뒤");
    }

    [Test]
    public void Card_ResolvesToSpecSelectionText()
    {
        Assert.AreEqual(
            "Q 사용 후 3초 안에 다시 누르면 처음 위치를 향해 돌진하며 원래 Q 피해의 60%를 줍니다. 대신 Q 재사용 대기시간이 20% 늘어납니다.",
            Card(Return));
    }

    [Test]
    public void ModeId_IsRegisteredSwitch()
    {
        Assert.AreEqual(Return, TraitModeIds.AssassinShadowReturn);
        CollectionAssert.Contains(TraitModeIds.All, Return);
    }

    // ── 칸 교체 · 미보유 = 기존 Q ────────────────────────────────────────

    [Test]
    public void WithoutTrait_IsExistingQ_CooldownStartsOnUse()
    {
        PlayerSkillFollowUpKind kind = ApproveSource(false);
        Assert.AreEqual(PlayerSkillFollowUpKind.Normal, kind, "미보유 = 후속 안 열림");
        Assert.IsTrue(PlayerSkillFollowUpRules.StartsCooldownOnApproval(kind, false), "기존대로 사용 시 쿨 시작");
        Assert.IsFalse(AssassinShadowReturnRules.OpensReturn(false, true), "복귀 스킬이 연결 안 됐으면 보유해도 기존 Q");
    }

    [Test]
    public void WithTrait_SlotBecomesReturn_NoCooldownOnFirstDash()
    {
        PlayerSkillFollowUpKind kind = ApproveSource(true);
        Assert.AreEqual(PlayerSkillFollowUpKind.OpenFollowUp, kind);
        Assert.IsFalse(PlayerSkillFollowUpRules.StartsCooldownOnApproval(kind, false), "첫 돌진 때 쿨 미시작(복귀·만료 때 시작)");

        var model = new PlayerSkillFollowUpModel();
        Assert.IsTrue(model.Open(Q, SourceIndex, ReturnIndex, 42));
        Assert.IsTrue(PlayerSkillFollowUpRules.IsSet(model.OpenMask, Q), "Q 칸이 복귀로 바뀐다");
        Assert.AreEqual(ReturnIndex, model.FollowUpOf(Q));
        Assert.AreEqual(42, model.CastIdOf(Q), "복귀가 이어받을 원래 시전 ID");
        Assert.AreEqual(PlayerSkillFollowUpKind.UseFollowUp, PlayerSkillFollowUpRules.Classify(true, false, false), "창 중 Q = 복귀(새 돌진 아님)");
    }

    // ── 창 3초(첫 돌진 종료부터, 실제 시간) · 만료 복귀 + 쿨 시작 ────────

    [Test]
    public void Window_3Seconds_FromFirstDashEnd_ExpiryRestoresQAndStartsCooldown()
    {
        TraitModifierSet set = SetOf(Return);
        float window = set.ResolveSkillValue(AssassinShadowReturnRules.WindowKey, 99f);
        Assert.AreEqual(3f, window, Eps, "창 = 정의 에셋 스킬 수치");
        float cooldown = NormalQCooldown * set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal);

        var model = new PlayerSkillFollowUpModel();
        var ledger = new PlayerSkillCooldownLedger(4);
        var closed = new List<int>();
        model.Open(Q, SourceIndex, ReturnIndex, 1);

        // 돌진 중(원래 Q 실행 중) = 창 미시작 — 끝없이 기다려도 닫히지 않는다.
        Assert.AreEqual(0, model.CollectExpired(100f, closed));
        // 첫 돌진 종료 = 원래 Q 가 실행 중에서 빠지는 시각(t=1.2) — 장치 기본 시작과 같다. 공격 가속 인자는 없다(실제 시간).
        Assert.IsTrue(model.BeginWindow(Q, SourceIndex, 1.2f, window));
        Assert.AreEqual(0, model.CollectExpired(4.19f, closed), "3초 안 = 아직 복귀 칸");
        Assert.IsTrue(ledger.IsReady(SourceIndex, 4.19f), "창 동안 원래 Q 쿨 미시작");

        Assert.AreEqual(1, model.CollectExpired(4.2f, closed), "창 끝 = 원래 Q 로 복귀");
        CollectionAssert.AreEqual(new[] { SourceIndex }, closed);
        StartIfReady(ledger, closed[0], 4.2f, cooldown);
        Assert.IsFalse(PlayerSkillFollowUpRules.IsSet(model.OpenMask, Q));
        Assert.AreEqual(6f, ledger.GetRemaining(SourceIndex, 4.2f), Eps, "만료 때 Q 쿨 5 × 1.2 시작");
    }

    [Test]
    public void UseReturn_StartsCooldownOnce_NoRestart()
    {
        var model = new PlayerSkillFollowUpModel();
        var ledger = new PlayerSkillCooldownLedger(4);
        model.Open(Q, SourceIndex, ReturnIndex, 7);
        model.BeginWindow(Q, SourceIndex, 1f, 3f);

        Assert.IsFalse(PlayerSkillFollowUpRules.StartsCooldownOnApproval(PlayerSkillFollowUpKind.UseFollowUp, false), "복귀 자체 쿨 없음");
        int source = model.Consume(Q, ReturnIndex);
        Assert.AreEqual(SourceIndex, source, "복귀 사용 = 원래 Q 로 복귀");
        StartIfReady(ledger, source, 2f, 6f);
        Assert.AreEqual(6f, ledger.GetRemaining(SourceIndex, 2f), Eps, "복귀 사용 때 쿨 시작");

        Assert.AreEqual(-1, model.Consume(Q, ReturnIndex), "두 번째 복귀 없음");
        Assert.AreEqual(0, model.CollectExpired(10f, new List<int>()), "닫힌 뒤 만료로 다시 시작 안 함");
        StartIfReady(ledger, SourceIndex, 3f, 6f);
        Assert.AreEqual(5f, ledger.GetRemaining(SourceIndex, 3f), Eps, "재시작·2회 지불 없음");
    }

    // ── 쿨 ×1.20 · 명세 §10 검산 ───────────────────────────────────────

    [Test]
    public void Cooldown_NormalAndTransformedQ_Times1_2_OtherSlotsUnchanged()
    {
        TraitModifierSet set = SetOf(Return);
        Assert.AreEqual(1.2f, set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal), Eps, "일반 Q");
        Assert.AreEqual(1.2f, set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Alternate), Eps, "변신 Q");
        foreach (TraitAttackForm form in Enum.GetValues(typeof(TraitAttackForm)))
        {
            Assert.AreEqual(1f, set.CooldownMultiplier(PlayerSkillSlot.Sub, form), Eps, $"E {form}");
            Assert.AreEqual(1f, set.CooldownMultiplier(PlayerSkillSlot.Interrupt, form), Eps, $"우클릭 {form}");
            Assert.AreEqual(1f, set.CooldownMultiplier(PlayerSkillSlot.Ultimate, form), Eps, $"R {form}");
        }

        Assert.AreEqual(1f, set.DamageMultiplier(NormalQ), Eps, "원래 Q 피해는 그대로");
        Assert.AreEqual(1f, set.DamageMultiplier(TransformedQ), Eps);
    }

    [Test]
    public void Spec10_BreakthroughPlusReturn_NormalQ_5_1Seconds()
    {
        TraitModifierSet set = SetOf(Breakthrough, Return);
        Assert.AreEqual(5.1f, NormalQCooldown * set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal), Eps,
            "명세 §10: 5 × 0.85 × 1.20 = 5.1초");
        Assert.AreEqual(1.2f, set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Alternate), Eps, "돌파 가속은 변신 Q 제외");
    }

    [Test]
    public void Spec10_ReturnPlusTrail_200PercentOfOutbound()
    {
        TraitModifierSet set = SetOf(Return);
        float ratio = set.ResolveSkillValue(AssassinShadowReturnRules.DamageRatioKey, 0f);
        float trailRatio = AssassinDerivedTooltipRules.RatioOf(Load(Trail));
        Assert.AreEqual(0.6f, ratio, Eps);
        Assert.AreEqual(0.25f, trailRatio, Eps);

        const int dref = 100;
        int outbound = dref;
        int outboundTrail = TraitDerivedAttackRules.Damage(outbound, trailRatio);
        int back = AssassinShadowReturnRules.StageDamage(dref, ratio);
        int backTrail = TraitDerivedAttackRules.Damage(back, trailRatio);
        Assert.AreEqual(60, back, "복귀 = Dref × 0.6");
        Assert.AreEqual(15, backTrail, "복귀 궤적 = 복귀 Dref(60) × 0.25");
        Assert.AreEqual(200, outbound + outboundTrail + back + backTrail, "명세 §10: (1 + 0.6) × (1 + 0.25) = 출발 기준 200%");
    }

    // ── 복귀 방향·거리 ─────────────────────────────────────────────────

    [Test]
    public void ReturnDirection_TowardSavedOrigin_DistanceCappedByQMax()
    {
        AssassinShadowReturnRules.Resolve(new Vector3(0f, 0f, 4f), new Vector3(0f, 1f, 1f), 4f, Vector3.forward,
            out Vector3 direction, out float distance);
        Assert.AreEqual(0f, Vector3.Distance(Vector3.back, direction), Eps, "현재 위치 → 저장점(수평)");
        Assert.AreEqual(3f, distance, Eps, "남은 거리가 짧으면 저장점까지");

        AssassinShadowReturnRules.Resolve(new Vector3(10f, 0f, 0f), Vector3.zero, 4f, Vector3.forward, out direction, out distance);
        Assert.AreEqual(0f, Vector3.Distance(Vector3.left, direction), Eps);
        Assert.AreEqual(4f, distance, Eps, "멀어졌으면 현재 Q 최대 거리까지만 — 저장점 도착 보장 없음");

        AssassinShadowReturnRules.Resolve(Vector3.one, Vector3.one, 4f, Vector3.right, out direction, out distance);
        Assert.AreEqual(0f, distance, Eps, "이미 저장점 위 = 이동 0");
        Assert.AreEqual(0f, Vector3.Distance(Vector3.right, direction), Eps);
    }

    // ── 피해·보상 — 출발·복귀 각각 피해, 보상은 왕복 최초 1회 ───────────

    [Test]
    public void ReturnDamage_DrefTimes0_6_FromDefinition()
    {
        TraitModifierSet set = SetOf(Return);
        float ratio = set.ResolveSkillValue(AssassinShadowReturnRules.DamageRatioKey, AssassinShadowReturnRules.DefaultDamageRatio);
        Assert.AreEqual(90, AssassinShadowReturnRules.StageDamage(150, ratio), "Q 계수 1.5 × 공격력 100 = 150 → 90");
        Assert.AreEqual(81, AssassinShadowReturnRules.StageDamage(135, ratio), "단계 실행 때 Dref 기준(절제된 분노 등 배율 포함 값을 받는다)");
    }

    [Test]
    public void OutboundMiss_ReturnHit_RewardOnce_SameEnemyDamagedEachStage()
    {
        var ledger = new AssassinDashHitLedger();
        object enemy = new object();

        ledger.Begin();
        Assert.IsFalse(ledger.TryClaimRageReward(false), "출발 빗나감 — 보상 소모 없음");
        Assert.IsFalse(ledger.TryClaimCooldownReward(false));

        ledger.BeginNextStage();
        Assert.IsTrue(ledger.TryRegisterTarget(enemy));
        Assert.IsTrue(ledger.TryClaimRageReward(true), "복귀 적중에서 인정");
        Assert.IsTrue(ledger.TryClaimCooldownReward(true));

        // 출발 적중 + 복귀 적중 = 보상 1회, 같은 적 피해는 단계마다.
        ledger.Begin();
        Assert.IsTrue(ledger.TryRegisterTarget(enemy));
        Assert.IsFalse(ledger.TryRegisterTarget(enemy), "한 단계 안에서는 대상별 1회");
        Assert.IsTrue(ledger.TryClaimRageReward(true));
        ledger.BeginNextStage();
        Assert.IsTrue(ledger.TryRegisterTarget(enemy), "복귀 피해는 같은 적에게 별도 적용");
        Assert.IsFalse(ledger.TryClaimRageReward(true), "왕복 전체 최초 1회");
        Assert.IsTrue(ledger.TryClaimCooldownReward(true), "쿨 차감 보상은 분노와 따로 센다");
        Assert.IsFalse(ledger.TryClaimCooldownReward(true));
    }

    // 장치가 쿨 시작 때 하는 일 — StartCooldownNow 와 같은 순서(시작 → 보류 차감 한 번).
    static void StartWithHeld(PlayerSkillFollowUpModel model, PlayerSkillCooldownLedger ledger, int sourceIndex, float now, float cooldown)
    {
        if (sourceIndex < 0 || !ledger.IsReady(sourceIndex, now))
            return;
        ledger.Start(sourceIndex, now, cooldown);
        float held = model.TakeHeldReduction(sourceIndex);
        if (held > 0f)
            ledger.Reduce(sourceIndex, now, held);
    }

    // 변신 Q 적중 차감 경로(ReduceCooldownOrHoldServer) — 묶여 있으면 보류, 아니면 바로.
    static void ReduceOrHold(PlayerSkillFollowUpModel model, PlayerSkillCooldownLedger ledger, int sourceIndex, float now, float seconds)
    {
        if (!model.HoldReduction(sourceIndex, seconds))
            ledger.Reduce(sourceIndex, now, seconds);
    }

    [Test]
    public void TransformedQ_OutboundHitBeforeCooldown_HeldThenAppliedOnceWhenCooldownStarts()
    {
        const float cooldown = 3.6f, reduction = 1.5f; // 변신 Q 3 × 1.2, 적중 차감 1.5
        var model = new PlayerSkillFollowUpModel();
        var ledger = new PlayerSkillCooldownLedger(4);
        var hits = new AssassinDashHitLedger();

        // 출발 단계 적중 — 쿨 시작 전이라 보류(버리지 않는다).
        model.Open(Q, TransformedSourceIndex, TransformedReturnIndex, 5);
        hits.Begin();
        Assert.IsTrue(hits.TryClaimCooldownReward(true));
        ReduceOrHold(model, ledger, TransformedSourceIndex, 1f, reduction);
        Assert.IsTrue(ledger.IsReady(TransformedSourceIndex, 1f), "창 동안 쿨 미시작 — 준비 상태 그대로");

        // 복귀 사용 = 쿨 시작 + 보류 차감 적용.
        int source = model.Consume(Q, TransformedReturnIndex);
        StartWithHeld(model, ledger, source, 2f, cooldown);
        Assert.AreEqual(cooldown - reduction, ledger.GetRemaining(TransformedSourceIndex, 2f), Eps, "쿨 시작 때 1.5초 차감");

        // 복귀 단계 적중 — 왕복 전체 1회라 다시 차감하지 않는다.
        hits.BeginNextStage();
        Assert.IsFalse(hits.TryClaimCooldownReward(true), "왕복 전체 1회");
        Assert.AreEqual(0f, model.TakeHeldReduction(TransformedSourceIndex), Eps, "보류분은 한 번만 가져간다");
        Assert.AreEqual(cooldown - reduction, ledger.GetRemaining(TransformedSourceIndex, 2f), Eps);
    }

    [Test]
    public void TransformedQ_HeldReduction_AppliedOnExpiry_AndNotCarriedToNextUse()
    {
        var model = new PlayerSkillFollowUpModel();
        var ledger = new PlayerSkillCooldownLedger(4);
        var closed = new List<int>();

        // 출발 적중 후 창 만료 = 쿨 시작 + 보류 차감.
        model.Open(Q, TransformedSourceIndex, TransformedReturnIndex, 1);
        Assert.IsTrue(model.HoldReduction(TransformedSourceIndex, 1.5f));
        model.BeginWindow(Q, TransformedSourceIndex, 1f, 3f);
        Assert.AreEqual(1, model.CollectExpired(4f, closed));
        StartWithHeld(model, ledger, closed[0], 4f, 3.6f);
        Assert.AreEqual(2.1f, ledger.GetRemaining(TransformedSourceIndex, 4f), Eps, "만료 때 시작한 쿨에도 적용");

        // 묶여 있지 않으면 보류하지 않는다(평소처럼 바로 줄인다).
        Assert.IsFalse(model.HoldReduction(TransformedSourceIndex, 1.5f), "후속 닫힘 = 보류 없음");

        // 시작 못 한 보류분은 다음 사용이 비운다.
        model.Open(Q, TransformedSourceIndex, TransformedReturnIndex, 2);
        model.HoldReduction(TransformedSourceIndex, 1.5f);
        model.Open(Q, TransformedSourceIndex, TransformedReturnIndex, 3);
        Assert.AreEqual(0f, model.TakeHeldReduction(TransformedSourceIndex), Eps, "새 사용 = 이전 보류분 없음");
    }

    // ── 복귀 창 = 실제 시간(슬로우·히트스톱에 늘지 않음, 할 일 133 i) ──────

    [Test]
    public void Window_RealTime_SlowMotionDoesNotExtend()
    {
        Assert.AreEqual(12.5, PlayerSkillFollowUpRules.WindowNow(true, 12.5, 99.0), 1e-9, "세션 중 = 일시정지만 뺀 서버 시간(슬로우 보정 전)");
        Assert.AreEqual(99.0, PlayerSkillFollowUpRules.WindowNow(false, 0.0, 99.0), 1e-9, "세션 밖 = unscaled");

        // 창 시작 t=10(실제), 0.2배 슬로우로 게임 시간은 0.6초만 흘러도 실제 3초가 지나면 닫힌다.
        var model = new PlayerSkillFollowUpModel();
        model.Open(Q, SourceIndex, ReturnIndex, 1);
        model.BeginWindow(Q, SourceIndex, (float)PlayerSkillFollowUpRules.WindowNow(true, 10.0, 0.0), 3f);
        Assert.AreEqual(0, model.CollectExpired((float)PlayerSkillFollowUpRules.WindowNow(true, 12.99, 0.0), null));
        Assert.AreEqual(1, model.CollectExpired((float)PlayerSkillFollowUpRules.WindowNow(true, 13.0, 0.0), null), "실제 3초 = 만료");
    }

    // ── 같은 시전 ID · 강제 복귀(R 전환·사망) · 저장점 폐기 ─────────────

    [Test]
    public void ForcedReturn_RSwitchOrDeath_StartsCooldown_AndDiscardsOrigin()
    {
        var model = new PlayerSkillFollowUpModel();
        var ledger = new PlayerSkillCooldownLedger(4);
        var normalOrigin = new AssassinShadowReturnOrigin();
        var transformedOrigin = new AssassinShadowReturnOrigin();

        // 일반 Q 사용 → 저장. R 전환(세트 교체) = 강제 복귀 + 쿨 시작 + 저장점 폐기.
        normalOrigin.Save(new Vector3(1f, 0f, 2f));
        model.Open(Q, SourceIndex, ReturnIndex, 3);
        int source = model.ForceClose(Q);
        Assert.AreEqual(SourceIndex, source);
        StartIfReady(ledger, source, 1f, 6f);
        Assert.IsFalse(ledger.IsReady(SourceIndex, 1f), "R 전환 때 일반 Q 쿨 시작");
        normalOrigin.Discard();
        Assert.IsFalse(normalOrigin.TryTake(out _), "저장점 폐기");
        Assert.IsFalse(transformedOrigin.TryTake(out _), "변신 Q 는 일반 Q 의 과거 저장점을 쓰지 않는다(각자 저장)");

        // 변신 Q 사용 → 사망 = 전 칸 강제 복귀 + 쿨 시작.
        transformedOrigin.Save(Vector3.zero);
        model.Open(Q, TransformedSourceIndex, TransformedReturnIndex, 4);
        var closed = new List<int>();
        Assert.AreEqual(1, model.CloseAll(closed));
        CollectionAssert.AreEqual(new[] { TransformedSourceIndex }, closed);
        StartIfReady(ledger, closed[0], 2f, 3.6f);
        Assert.IsFalse(ledger.IsReady(TransformedSourceIndex, 2f), "사망 때 변신 Q 쿨 시작");
    }

    [Test]
    public void Origin_TakenOnce_NewCastOverwrites()
    {
        var origin = new AssassinShadowReturnOrigin();
        origin.Save(new Vector3(1f, 0f, 1f));
        origin.Save(new Vector3(5f, 0f, 5f));
        Assert.IsTrue(origin.TryTake(out Vector3 point));
        Assert.AreEqual(new Vector3(5f, 0f, 5f), point, "마지막 출발 돌진의 시작 위치");
        Assert.IsFalse(origin.TryTake(out _), "복귀 1회만");
    }

    // ── 추격의 궤적 — 왕복 각 1회, 복귀 강제 중단이면 없음 ─────────────

    [Test]
    public void Trail_EachStageOnce_ReturnForcedStopHasNone()
    {
        Assert.IsTrue(AssassinTrailRules.Schedules(SkillEndReason.Completed, 4f), "출발 정상 종료");
        Assert.IsTrue(AssassinTrailRules.Schedules(SkillEndReason.Completed, 2.5f), "복귀 정상 종료(벽 앞 정지 포함)");
        Assert.IsFalse(AssassinTrailRules.Schedules(SkillEndReason.CasterDied, 2.5f), "복귀 강제 중단 = 없음");
        Assert.IsFalse(AssassinTrailRules.Schedules(SkillEndReason.Cancelled, 2.5f));
        Assert.IsFalse(AssassinTrailRules.Schedules(SkillEndReason.Completed, 0f), "저장점 위(이동 0) = 지나온 길 없음");
    }

    // ── 프리팹 연결 ───────────────────────────────────────────────────

    [Test]
    public void Prefab_NormalAndTransformedQ_LinkedToOwnReturnSkill()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssassinPrefab);
        Assert.IsNotNull(prefab, AssassinPrefab);

        AssassinDashStrikeSkill[] sources = prefab.GetComponentsInChildren<AssassinDashStrikeSkill>(true)
            .Where(s => !(s is AssassinShadowReturnSkill)).ToArray();
        Assert.AreEqual(2, sources.Length, "일반 Q · 변신 Q");
        Assert.AreEqual(1, sources.Count(s => s.Data is AssassinTransformedDashStrikeSkillData), "변신 Q 하나");

        var returns = new HashSet<PlayerSkillBase>();
        foreach (AssassinDashStrikeSkill source in sources)
        {
            var back = source.FollowUpSkill as AssassinShadowReturnSkill;
            Assert.IsNotNull(back, $"{source.Data.name} → 복귀 스킬");
            Assert.IsTrue(returns.Add(back), "일반·변신 각자 다른 복귀 컴포넌트");
            Assert.IsTrue(source.FollowUpInheritsCastId, "왕복 = 같은 시전 ID");
            Assert.IsNull(back.FollowUpSkill, "후속의 후속 없음");

            PlayerSkillData data = back.Data;
            Assert.IsNotNull(data);
            Assert.IsNotInstanceOf<AssassinDashStrikeSkillData>(data, "별도 데이터(데이터 테이블 시트 밖)");
            Assert.AreEqual(0f, data.CooldownTime, "복귀 자체 쿨 0");
            Assert.IsFalse(data.SnapRotationOnStart, "조준이 아니라 출발점 쪽으로 돈다");
            Assert.AreEqual(source.Data.AnimatorStateName, data.AnimatorStateName, "기존 Q 연출 재사용");
            Assert.AreEqual(new Color(0.6f, 0.4f, 1f, 1f), data.FollowUpIconTint, "HUD 보라 tint");
        }
    }
}
