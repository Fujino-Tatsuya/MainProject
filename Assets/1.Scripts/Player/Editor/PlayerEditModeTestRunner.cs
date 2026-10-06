using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using NUnit.Framework;

/// <summary>
/// 플레이어·코어(상태이상·보호막·스킬 장부·어쌔신) EditMode 테스트를 메뉴 한 번으로 돌리고 결과를 콘솔에 남긴다.
/// 테스트는 asmdef 없는 <c>Assembly-CSharp-Editor</c> 에 있어 픽스처 이름 정규식으로 고른다.
/// 패턴은 <see cref="DataTableEditModeTestRunner"/> 를 따랐다. 픽스처가 늘면 <see cref="Fixtures"/> 에만 추가한다.
/// </summary>
internal static class PlayerEditModeTestRunner
{
    static readonly string[] Fixtures =
    {
        "^StatusEffectImmunityTests$", // PLAN-assassin A1 — SuperArmor 의미 통일
        "^PlayerSkillCooldownLedgerTests$", // PLAN-assassin A2 — 스킬 단위 쿨 장부·슬롯 교체
        "^HealthShieldTests$",
        "^GunnerHeatModelTests$",
    };

    const string Tag = "[PlayerTests]";

    static TestRunnerApi _runner;

    [MenuItem("Tools/Tests/플레이어 EditMode 테스트 실행")]
    static void Run()
    {
        if (_runner != null)
        {
            Debug.LogWarning($"{Tag} 이미 실행 중이다 — 끝날 때까지 기다릴 것.");
            return;
        }

        _runner = ScriptableObject.CreateInstance<TestRunnerApi>();
        _runner.RegisterCallbacks(new Callbacks());
        _runner.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, groupNames = Fixtures }));
    }

    sealed class Callbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }

        public void RunFinished(ITestResultAdaptor result)
        {
            string summary =
                $"{Tag} {result.TestStatus} — 통과 {result.PassCount} / 실패 {result.FailCount} / " +
                $"건너뜀 {result.SkipCount} ({result.Duration:F3}s)";

            // 통과 0건은 "실패 0건"과 다르다 — 필터가 아무것도 못 잡아도 TestStatus 는 Passed 가 된다.
            if (result.FailCount > 0 || result.PassCount == 0)
                Debug.LogError(summary + (result.PassCount == 0 ? "  ⚠️ 통과 0건 — 필터가 안 걸렸는지 확인" : ""));
            else
                Debug.Log(summary);

            Object.DestroyImmediate(_runner);
            _runner = null;
        }

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (result.TestStatus != TestStatus.Failed) return;

            Debug.LogError($"{Tag} FAIL {result.FullName}\n{result.Message}\n{result.StackTrace}");
        }
    }
}

public sealed class PlayerSkillCooldownLedgerTests
{
    [Test]
    public void SkillsHaveIndependentCooldowns_AndInactiveCooldownKeepsDecreasing()
    {
        var ledger = new PlayerSkillCooldownLedger(2);
        ledger.Start(0, 10f, 5f);
        ledger.Start(1, 10f, 12f);

        Assert.That(ledger.GetRemaining(0, 12f), Is.EqualTo(3f));
        Assert.That(ledger.GetRemaining(1, 12f), Is.EqualTo(10f));
        Assert.That(ledger.GetRemaining(0, 16f), Is.Zero);
        Assert.That(ledger.GetRemaining(1, 16f), Is.EqualTo(6f));
    }

    [Test]
    public void SlotSwap_DoesNotReplaceInactiveSkillCooldown()
    {
        var ledger = new PlayerSkillCooldownLedger(2);
        var bindings = new PlayerSkillSlotBindingModel(1 << (int)PlayerSkillSlot.Main);
        ledger.Start(0, 0f, 5f);
        ledger.Start(1, 0f, 9f);

        Assert.That(bindings.Request(PlayerSkillSlot.Main, true, defer: false), Is.True);
        Assert.That(ledger.GetRemaining(0, 3f), Is.EqualTo(2f), "비활성 기본 Q 장부 유지");
        Assert.That(ledger.GetRemaining(1, 3f), Is.EqualTo(6f), "활성 대체 Q 장부 유지");
    }

    [Test]
    public void Reduce_ClampsRemainingAtZero()
    {
        var ledger = new PlayerSkillCooldownLedger(1);
        ledger.Start(0, 2f, 3f);

        ledger.Reduce(0, 3f, 10f);

        Assert.That(ledger.GetRemaining(0, 3f), Is.Zero);
        Assert.That(ledger.IsReady(0, 3f), Is.True);
    }

    [Test]
    public void OverrideDuringSkill_DefersUntilSkillEnds()
    {
        var bindings = new PlayerSkillSlotBindingModel(1 << (int)PlayerSkillSlot.Sub);

        Assert.That(bindings.Request(PlayerSkillSlot.Sub, true, defer: true), Is.False);
        Assert.That(bindings.ActiveMask, Is.Zero);
        Assert.That(bindings.HasPending, Is.True);
        Assert.That(bindings.ApplyPending(), Is.True);
        Assert.That(bindings.ActiveMask, Is.EqualTo(1 << (int)PlayerSkillSlot.Sub));
    }

    [Test]
    public void MissingAlternate_PreservesBaseBinding()
    {
        var bindings = new PlayerSkillSlotBindingModel(0);

        Assert.That(bindings.Request(PlayerSkillSlot.Main, true, defer: false), Is.False);
        Assert.That(bindings.ActiveMask, Is.Zero);
        Assert.That(bindings.HasPending, Is.False);
    }
}
