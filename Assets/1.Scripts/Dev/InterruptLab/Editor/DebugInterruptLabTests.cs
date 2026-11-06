using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 간파 제작용 디버그 씬(할 일 115 C5)의 순수 규칙 — 배속 합성(<see cref="GlobalTimeScale"/>)과
/// 허수아비 회복 시간(<see cref="DebugDummyInterruptRecovery.SplitRecovery"/> → <see cref="TrainingDummyInterruptCycle"/>).
/// 길이는 2 진수로 딱 떨어지는 값을 쓴다.
/// </summary>
public sealed class DebugInterruptLabTests
{
    const float Eps = 1e-5f;

    // ─── 배속 합성 ─────────────────────────────────────────────────────

    [Test]
    public void 기본_배속_1이면_슬로우_배율_그대로()
    {
        Assert.AreEqual(1f, GlobalTimeScale.ComposeScale(1f, 1f), Eps);
        Assert.AreEqual(0.25f, GlobalTimeScale.ComposeScale(0.25f, 1f), Eps);
    }

    [Test]
    public void 최종_배율은_슬로우_곱하기_기본_배속()
    {
        Assert.AreEqual(0.125f, GlobalTimeScale.ComposeScale(0.25f, 0.5f), Eps);
        Assert.AreEqual(0.5f, GlobalTimeScale.ComposeScale(1f, 0.5f), Eps); // 슬로우 없을 때도 적용
        Assert.AreEqual(0.5f, GlobalTimeScale.ComposeScale(0.25f, 2f), Eps);
    }

    [Test]
    public void 기본_배속은_0이_되지_않는다()
    {
        Assert.Greater(GlobalTimeScale.ComposeScale(1f, 0f), 0f);
    }

    [Test]
    public void 슬로우도_기본_배속도_없으면_레이어는_쉰다()
    {
        Assert.IsFalse(GlobalTimeScale.IsLayerActive(false, 1f)); // 지금 동작과 같다 — timeScale 에 쓰지 않음
        Assert.IsTrue(GlobalTimeScale.IsLayerActive(true, 1f));
        Assert.IsTrue(GlobalTimeScale.IsLayerActive(false, 0.5f));
        Assert.IsTrue(GlobalTimeScale.IsLayerActive(true, 0.5f));
    }

    [Test]
    public void 디버그_배속은_범위로_자르고_프리셋은_범위_안()
    {
        Assert.AreEqual(DebugTimeScale.MinScale, DebugTimeScale.ClampScale(0f), Eps);
        Assert.AreEqual(DebugTimeScale.MaxScale, DebugTimeScale.ClampScale(10f), Eps);
        foreach (float preset in DebugTimeScale.Presets)
            Assert.AreEqual(preset, DebugTimeScale.ClampScale(preset), Eps);
    }

    // ─── 허수아비 회복 ─────────────────────────────────────────────────

    static TrainingDummyInterruptCycle CycleFor(float recovery)
    {
        (float vulnerable, float idle) = DebugDummyInterruptRecovery.SplitRecovery(recovery);
        return new TrainingDummyInterruptCycle(vulnerable, idle);
    }

    [Test]
    public void 회복_시간_합은_입력과_같다()
    {
        (float vulnerable, float idle) = DebugDummyInterruptRecovery.SplitRecovery(2f);
        Assert.AreEqual(2f, vulnerable + idle, Eps);
    }

    [Test]
    public void 성공_후_회복_시간_전에는_인터럽트_불가()
    {
        var c = CycleFor(2f);
        Assert.IsTrue(c.TryInterrupt());
        c.Tick(1.5f);
        Assert.IsFalse(c.IsInterruptible);
        Assert.IsFalse(c.TryInterrupt());
    }

    [Test]
    public void 회복_시간이_지나면_인터럽트_가능으로_복귀()
    {
        var c = CycleFor(2f);
        c.TryInterrupt();
        c.Tick(1.5f);
        c.Tick(0.5f);
        Assert.IsTrue(c.IsInterruptible);
    }

    [Test]
    public void 복귀한_인터럽트_가능_상태는_시간_제한_없이_유지()
    {
        var c = CycleFor(2f);
        c.TryInterrupt();
        c.Tick(2f);
        for (int i = 0; i < 100; i++)
            c.Tick(64f);
        Assert.IsTrue(c.IsInterruptible);
        Assert.IsTrue(c.TryInterrupt()); // 다시 성공한다
    }

    [Test]
    public void 회복_시간_0이면_다음_틱에_바로_복귀()
    {
        var c = CycleFor(0f);
        c.TryInterrupt();
        c.Tick(0f);
        Assert.IsTrue(c.IsInterruptible);
    }

    [Test]
    public void 음수_회복_시간은_0으로_본다()
    {
        (float vulnerable, float idle) = DebugDummyInterruptRecovery.SplitRecovery(-1f);
        Assert.AreEqual(0f, vulnerable, Eps);
        Assert.AreEqual(0f, idle, Eps);
    }

    // ─── C6 캐릭터 순환 ────────────────────────────────────────────────

    static readonly int[] PaladinAssassinGunner = { 0, 2, 1 };

    [Test]
    public void 순환은_팔라딘_어쌔신_거너_다시_팔라딘()
    {
        Assert.AreEqual(2, DebugCharacterCycle.NextInCycle(PaladinAssassinGunner, 0));
        Assert.AreEqual(1, DebugCharacterCycle.NextInCycle(PaladinAssassinGunner, 2));
        Assert.AreEqual(0, DebugCharacterCycle.NextInCycle(PaladinAssassinGunner, 1));
    }

    [Test]
    public void 지금_캐릭터를_모르면_순서의_처음()
    {
        Assert.AreEqual(0, DebugCharacterCycle.NextInCycle(PaladinAssassinGunner, -1));
        Assert.AreEqual(0, DebugCharacterCycle.NextInCycle(PaladinAssassinGunner, 7));
    }

    [Test]
    public void 순서가_비면_교체_없음()
    {
        Assert.AreEqual(-1, DebugCharacterCycle.NextInCycle(new int[0], 0));
        Assert.AreEqual(-1, DebugCharacterCycle.NextInCycle(null, 0));
    }

    // ─── C6 성공 후 1배속 ──────────────────────────────────────────────

    [Test]
    public void 성공_후_1배속_중엔_기본_배속_1_끝나면_슬라이더_값()
    {
        Assert.AreEqual(1f, DebugTimeScale.ResolveBaseScale(0.25f, true), Eps);
        Assert.AreEqual(0.25f, DebugTimeScale.ResolveBaseScale(0.25f, false), Eps);
        Assert.AreEqual(0.5f, DebugTimeScale.ResolveBaseScale(0.5f, false), Eps); // 그 사이 바꾼 슬라이더 값
    }

    [Test]
    public void 성공_후_1배속은_정한_시간_동안만()
    {
        double until = DebugTimeScale.HoldUntil(10.0, double.NegativeInfinity, 3f);
        Assert.IsTrue(DebugTimeScale.IsHolding(10.0, until));
        Assert.IsTrue(DebugTimeScale.IsHolding(12.875, until));
        Assert.IsFalse(DebugTimeScale.IsHolding(13.0, until));
    }

    [Test]
    public void 그_사이_또_성공하면_그때부터_다시_잰다()
    {
        double until = DebugTimeScale.HoldUntil(10.0, double.NegativeInfinity, 3f);
        until = DebugTimeScale.HoldUntil(12.0, until, 3f);
        Assert.IsTrue(DebugTimeScale.IsHolding(14.5, until));
        Assert.IsFalse(DebugTimeScale.IsHolding(15.0, until));
    }

    [Test]
    public void 성공_후_1배속_0초면_걸지_않는다()
    {
        double until = DebugTimeScale.HoldUntil(10.0, double.NegativeInfinity, 0f);
        Assert.IsFalse(DebugTimeScale.IsHolding(10.0, until));
    }

    // ─── C6 대시 충전 즉시 회복 ────────────────────────────────────────

    [Test]
    public void 오너_예측_충전은_덜_찼을_때만_채운다()
    {
        Assert.IsTrue(DebugZeroCooldown.NeedsOwnerRefill(0, 1));
        Assert.IsTrue(DebugZeroCooldown.NeedsOwnerRefill(1, 2));
        Assert.IsFalse(DebugZeroCooldown.NeedsOwnerRefill(2, 2));
        Assert.IsFalse(DebugZeroCooldown.NeedsOwnerRefill(0, 0)); // 장부 없음(준비 전)
    }

    // ─── 132 §17 튜닝 패널 (11-06 할 일 238 정리 — V3 런타임에서 보이는 칸만) ───

    [Test]
    public void 튜닝_패널_효과_줄은_V3_런타임에서_보이는_빛_모임만()
    {
        Assert.AreEqual(1, DebugInterruptTuningPanel.Rows.Length);
        DebugInterruptTuningPanel.Row row = DebugInterruptTuningPanel.Rows[0];
        Assert.IsFalse(row.IsCue);
        Assert.AreEqual(InterruptPresentationEffect.LightGather, row.Effect);
        Assert.IsFalse(string.IsNullOrEmpty(row.Label));
    }

    // 지운 칸(오너 암전·②~⑥·월드 섬광·적중 지점 라이트·A2)이 정말 안 보이는 전제 — NetworkManager 디렉터의 세 단계가 V3·암전 끔.
    // 단계 설정이 바뀌면(V0~V2 로 되돌리거나 ownerDim 켬) 이 테스트가 깨진다 → 랩 줄을 다시 넣을 것.
    [Test]
    public void 지운_효과_줄의_전제_세_단계가_V3_이고_오너_암전_끔()
    {
        string text = System.IO.File.ReadAllText("Assets/2.Prefabs/Network/NetworkManager.prefab").Replace("\r\n", "\n");
        foreach (string tier in new[] { "normalTier", "dashTier", "suppressTier" })
        {
            int at = text.IndexOf("  " + tier + ":\n", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(at, 0, tier);
            string block = text.Substring(at, System.Math.Min(200, text.Length - at));
            StringAssert.Contains("ownerDim: 0\n", block, tier);
            StringAssert.Contains("flash: " + (int)InterruptTierFlash.HitLightReference + "\n", block, tier);
        }
    }

    [Test]
    public void 튜닝_패널_배속_버튼은_범위_끝을_포함()
    {
        CollectionAssert.Contains(DebugInterruptTuningPanel.SpeedButtons, DebugTimeScale.MinScale);
        CollectionAssert.Contains(DebugInterruptTuningPanel.SpeedButtons, DebugTimeScale.MaxScale);
        foreach (float speed in DebugInterruptTuningPanel.SpeedButtons)
            Assert.AreEqual(speed, DebugTimeScale.ClampScale(speed), Eps);
    }

    [Test]
    public void 튜닝_패널_토글은_디렉터_런타임_값만_바꾼다()
    {
        var go = new UnityEngine.GameObject("Director");
        try
        {
            var director = go.AddComponent<InterruptProductionDirector>();
            DebugInterruptTuningPanel.Row row = DebugInterruptTuningPanel.Rows[0];
            row.Set(director, true);
            Assert.IsTrue(row.Get(director));
            row.Set(director, false);
            Assert.IsFalse(row.Get(director));
            Assert.IsFalse(row.Get(null), "디렉터가 없으면 꺼짐");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    // ---- 카메라 대상 중심 토글(196) ----

    [Test]
    public void 카메라_대상_중심_토글은_기본_켬()
    {
        var go = new UnityEngine.GameObject("Director");
        try
        {
            var director = go.AddComponent<InterruptProductionDirector>();
            Assert.IsTrue(director.CameraCenterEnabled, "기본 = 켬");
            Assert.IsFalse(string.IsNullOrEmpty(DebugInterruptTuningPanel.CameraCenterLabel));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    // ---- 238 캐릭터 변경 버튼 ----

    [Test]
    public void 캐릭터_변경_버튼은_팔라딘_거너_어쌔신()
    {
        CollectionAssert.AreEqual(new[] { TutorialCharacter.Paladin, TutorialCharacter.Gunner, TutorialCharacter.Assassin },
            DebugInterruptTuningPanel.CharacterButtons);
        CollectionAssert.AreEqual(new[] { "팔라딘", "거너", "어쌔신" },
            System.Array.ConvertAll(DebugInterruptTuningPanel.CharacterButtons, DebugInterruptTuningPanel.CharacterButtonLabel));
    }

    // ---- 허수아비 간파 = 제압 간파 재현(179 후속 3). A6·A7 토글은 11-04(할 일 211) 시퀀스와 함께 뺐다 ----

    [Test]
    public void 제압_간파_재현은_꺼져_있으면_허수아비가_없어도_무해()
    {
        var repro = new DebugSuppressInterruptRepro();
        Assert.IsFalse(repro.Enabled, "기본 끔 — 랩의 일반 간파 튜닝을 바꾸지 않는다");
        repro.Refresh();
        repro.Tick(0f);
        Assert.AreEqual(0, repro.MarkedCount);
        repro.Dispose();
        Assert.AreEqual(0, repro.DummyCount);
    }

    // ---- 194 — 단계별 적중 라이트(버전 버튼은 11-06 할 일 238 삭제) ----

    [Test]
    public void 단계별_버튼은_일반_대쉬_제압에_암순응_가_나()
    {
        CollectionAssert.AreEqual(new[] { InterruptPresentationTier.Normal, InterruptPresentationTier.Dash, InterruptPresentationTier.Suppress },
            DebugInterruptTierPreview.VersionTiers, "제압도 같은 표(11-04 할 일 211)");
        Assert.AreEqual(2, DebugInterruptTierPreview.DarkAdaptModes.Length);
        StringAssert.StartsWith("가", DebugInterruptTierPreview.DarkAdaptLabel(InterruptDarkAdaptMode.ScreenPass));
        StringAssert.StartsWith("나", DebugInterruptTierPreview.DarkAdaptLabel(InterruptDarkAdaptMode.SceneLight));
        Assert.AreEqual("[가]", DebugInterruptTierPreview.Selected("가", true));
        Assert.AreEqual("가", DebugInterruptTierPreview.Selected("가", false));
        Assert.AreEqual("V3", DebugInterruptTierPreview.LookLabel(InterruptSuccessLook.HitLightReference));
    }

    [Test]
    public void 단계_섬광_값이_다음_성공의_섬광을_고른다()
    {
        var go = new GameObject("Director");
        try
        {
            var director = go.AddComponent<InterruptProductionDirector>();
            var so = new SerializedObject(director);
            so.FindProperty("hitLightLook").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<InterruptHitLightLook>("Assets/5.VFX/Interrupt/HitLight/InterruptHitLightLook.asset");
            so.FindProperty("hitLightVfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<InterruptHitLightFx>("Assets/5.VFX/Interrupt/HitLight/FX_InterruptHitLight.prefab");
            so.ApplyModifiedPropertiesWithoutUndo();

            director.TierProfile(InterruptPresentationTier.Normal).flash = InterruptTierFlash.HitLightWhiteout;
            director.TierProfile(InterruptPresentationTier.Dash).flash = InterruptTierFlash.HitFlash;

            Assert.AreEqual(InterruptSuccessLook.HitLightWhiteout, director.SuccessLook(InterruptPresentationTier.Normal));
            Assert.AreEqual(InterruptSuccessLook.HitFlash, director.SuccessLook(InterruptPresentationTier.Dash));
            Assert.AreEqual("V2", DebugInterruptTierPreview.LookLabel(director.SuccessLook(InterruptPresentationTier.Normal)));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
