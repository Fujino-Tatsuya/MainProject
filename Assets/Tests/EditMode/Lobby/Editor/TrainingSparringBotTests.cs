using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>TRAIN_03 훈련 상대 — 할 일 86 T5b. 순수 판정(<see cref="TrainingSparringRules"/>) + 프리팹 구성 검사.</summary>
public sealed class TrainingSparringBotTests
{
    const string PrefabPath = "Assets/2.Prefabs/Lobby/TrainingSparringBot.prefab";
    const string TwentyThreePrefabPath = "Assets/2.Prefabs/Monster/Boss/TwentyThree.prefab";
    const string NetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";

    // 23호 기본 비(100 / 20 = 5칸)와 같은 입력 — 테스트 입력일 뿐 규칙엔 수치가 없다.
    const float Step = 20f;

    // ── 처치 방지·마무리 체력 ──

    [Test]
    public void HealthFloor_IsOneWhilePreventingDeath()
    {
        Assert.That(TrainingSparringRules.HealthFloor(true), Is.EqualTo(1));
        Assert.That(TrainingSparringRules.HealthFloor(false), Is.Zero);
        Assert.That(HealthFloorRules.Clamp(10, -50, TrainingSparringRules.HealthFloor(true)), Is.EqualTo(1), "치명 피해도 1 에서 멈춘다");
        Assert.That(HealthFloorRules.Clamp(10, -50, TrainingSparringRules.HealthFloor(false)), Is.EqualTo(-50), "해제 뒤엔 하한 없음");
    }

    [Test]
    public void FinishHealth_LowersButNeverHeals()
    {
        Assert.That(TrainingSparringRules.FinishHealth(200, 260, 60), Is.EqualTo(60));
        Assert.That(TrainingSparringRules.FinishHealth(30, 260, 60), Is.EqualTo(30), "이미 낮으면 그대로 — 회복하지 않는다");
        Assert.That(TrainingSparringRules.FinishHealth(1, 260, 60), Is.EqualTo(1));
        Assert.That(TrainingSparringRules.FinishHealth(200, 40, 60), Is.EqualTo(40), "최대 체력으로 자른다");
        Assert.That(TrainingSparringRules.FinishHealth(200, 260, 0), Is.EqualTo(1), "0 이하 저작 = 1");
    }

    // ── 간파 성공 ──

    [Test]
    public void Counter_InAB_GroggyOnly_NoGaugeChange()
    {
        var o = TrainingSparringRules.ResolveCounter(100f, Step, 1.5f, 5f, allowsGaugeTransition: false);
        Assert.That(o.NextGauge, Is.EqualTo(100f));
        Assert.That(o.Reduced, Is.False);
        Assert.That(o.IsSuppress, Is.False);
        Assert.That(o.Duration, Is.EqualTo(1.5f));

        var last = TrainingSparringRules.ResolveCounter(Step, Step, 1.5f, 5f, allowsGaugeTransition: false);
        Assert.That(last.IsSuppress, Is.False, "A/B 는 마지막 칸에서도 제압 전이 없음");
        Assert.That(last.NextGauge, Is.EqualTo(Step));
    }

    [Test]
    public void Counter_InCD_FollowsTwentyThreeRule()
    {
        var o = TrainingSparringRules.ResolveCounter(100f, Step, 1.5f, 5f, allowsGaugeTransition: true);
        Assert.That(o.NextGauge, Is.EqualTo(80f));
        Assert.That(o.Reduced, Is.True);
        Assert.That(o.IsSuppress, Is.False);
        Assert.That(o.Duration, Is.EqualTo(1.5f), "남으면 그로기");

        var last = TrainingSparringRules.ResolveCounter(Step, Step, 1.5f, 5f, allowsGaugeTransition: true);
        Assert.That(last.IsSuppress, Is.True);
        Assert.That(last.NextGauge, Is.Zero);
        Assert.That(last.Duration, Is.EqualTo(5f), "0 이면 제압(breakDuration)");
    }

    // ── 벽 ──

    [Test]
    public void Wall_InCD_ReducesAndSuppressesOnLastSegment()
    {
        var o = TrainingSparringRules.ResolveWall(60f, Step, allowsGaugeTransition: true);
        Assert.That(o.Reduced, Is.True);
        Assert.That(o.NextGauge, Is.EqualTo(40f));
        Assert.That(o.IsSuppress, Is.False);

        var last = TrainingSparringRules.ResolveWall(Step, Step, allowsGaugeTransition: true);
        Assert.That(last.Reduced, Is.True, "같은 넉백의 벽 + 마지막 칸 = 감소 동반(벽 목표 인정) 뒤 제압");
        Assert.That(last.IsSuppress, Is.True);
    }

    [Test]
    public void Wall_AtZeroGauge_NotReduced()
    {
        var o = TrainingSparringRules.ResolveWall(0f, Step, allowsGaugeTransition: true);
        Assert.That(o.Reduced, Is.False, "이미 0칸 — 감소 거부된 접촉은 벽 목표로 인정하지 않는다");
        Assert.That(o.IsSuppress, Is.False);
    }

    [Test]
    public void Wall_InAB_NoReduction()
    {
        var o = TrainingSparringRules.ResolveWall(100f, Step, allowsGaugeTransition: false);
        Assert.That(o.Reduced, Is.False);
        Assert.That(o.NextGauge, Is.EqualTo(100f));
        Assert.That(o.IsSuppress, Is.False);
    }

    [Test]
    public void DesignatedWall_OnlyListedColliderCounts()
    {
        var a = new object();
        var b = new object();
        var list = new List<object> { a, null };
        Assert.That(TrainingSparringRules.IsDesignatedWall(a, list), Is.True);
        Assert.That(TrainingSparringRules.IsDesignatedWall(b, list), Is.False, "장식 벽(목록 밖) 불인정");
        Assert.That(TrainingSparringRules.IsDesignatedWall<object>(null, list), Is.False);
        Assert.That(TrainingSparringRules.IsDesignatedWall(a, new List<object>()), Is.False, "빈 목록 = 어떤 벽도 무효");
        Assert.That(TrainingSparringRules.IsDesignatedWall(a, null), Is.False);
    }

    // ── 칸 UI·정면·Q 경직 ──

    [Test]
    public void SegmentsRemaining_CeilsAndToleratesFloatError()
    {
        Assert.That(TrainingSparringRules.SegmentsRemaining(100f, Step), Is.EqualTo(5));
        Assert.That(TrainingSparringRules.SegmentsRemaining(80f, Step), Is.EqualTo(4));
        Assert.That(TrainingSparringRules.SegmentsRemaining(80.00001f, Step), Is.EqualTo(4));
        Assert.That(TrainingSparringRules.SegmentsRemaining(10f, Step), Is.EqualTo(1));
        Assert.That(TrainingSparringRules.SegmentsRemaining(0f, Step), Is.Zero);
        Assert.That(TrainingSparringRules.SegmentsRemaining(50f, 0f), Is.Zero);
    }

    [Test]
    public void FrontCheck_UsesAngleUnlessDisabled()
    {
        Vector3 fwd = Vector3.forward;
        Assert.That(TrainingSparringRules.IsFromFront(fwd, new Vector3(0f, 0f, 3f), 60f, true), Is.True);
        Assert.That(TrainingSparringRules.IsFromFront(fwd, new Vector3(3f, 0f, 3f), 60f, true), Is.True, "45°");
        Assert.That(TrainingSparringRules.IsFromFront(fwd, new Vector3(0f, 0f, -3f), 60f, true), Is.False, "뒤");
        Assert.That(TrainingSparringRules.IsFromFront(fwd, new Vector3(0f, 0f, -3f), 60f, false), Is.True, "정면 판정 끔");
        Assert.That(TrainingSparringRules.IsFromFront(fwd, Vector3.zero, 60f, true), Is.True, "겹침 = 정면");
    }

    [Test]
    public void PushStagger_CapsAtVulnerableRemainingUnlessHeld()
    {
        Assert.That(TrainingSparringRules.PushStaggerSeconds(1.2f, 0.5f, false), Is.EqualTo(0.5f));
        Assert.That(TrainingSparringRules.PushStaggerSeconds(1.2f, 3f, false), Is.EqualTo(1.2f));
        Assert.That(TrainingSparringRules.PushStaggerSeconds(1.2f, 0.01f, false), Is.Zero, "0.05 이하 = 경직 없음");
        Assert.That(TrainingSparringRules.PushStaggerSeconds(1.2f, -1f, true), Is.EqualTo(1.2f), "B 학습용 취약 유지 = Q 경직 그대로");
    }

    [Test]
    public void SuppressText_ShowsRemainingSeconds()
    {
        Assert.That(LobbyUiStrings.FormatTrain03Segments(3, 5), Does.Contain("3").And.Contain("5"));
        string text = LobbyUiStrings.FormatTrain03SuppressAttack(4.2);
        Assert.That(text, Does.StartWith("제압된 상대를 공격하세요"));
        Assert.That(text, Does.Contain("5"), "남은 초는 올림");
        Assert.That(LobbyUiStrings.FormatTrain03SuppressAttack(0), Is.EqualTo("제압된 상대를 공격하세요"));
    }

    // ── 프리팹 구성 ──

    [Test]
    public void Prefab_HasRequiredComponents_AndNoSpinnerBot()
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(go, Is.Not.Null, PrefabPath);
        Assert.That(go.GetComponent<TrainingSparringBot>(), Is.Not.Null);
        Assert.That(go.GetComponent<SpinnerBot>(), Is.Null, "SpinnerBot 동작은 쓰지 않는다(외형·애니만)");
        Assert.That(go.GetComponents<MonsterBase>().Length, Is.EqualTo(1));
        Assert.That(go.GetComponent<NetworkObject>(), Is.Not.Null);
        Assert.That(go.GetComponent<MonsterCounterWindow>(), Is.Not.Null, "간파 창 = SpinnerBot 저작값");
        Assert.That(go.GetComponent<IInterruptSuccessCooldownTarget>(), Is.Not.Null);
        Assert.That(go.GetComponent<IInterruptSlowMotionTarget>(), Is.Not.Null);
        Assert.That(go.GetComponent<IOverheadHealthBarRule>(), Is.Not.Null, "칸 UI 규칙(머리 위 중간보스 바)");
        Assert.That(go.GetComponentInChildren<MonsterMeleeAttack>(true), Is.Not.Null);
    }

    [Test]
    public void Prefab_ReadsTwentyThreeData_AndHasValidSegments()
    {
        var bot = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<TrainingSparringBot>();
        var so = new SerializedObject(bot);
        var rules = so.FindProperty("counterRules").objectReferenceValue as BossDataSO;
        Assert.That(rules, Is.Not.Null, "23호 데이터 참조");

        var boss = AssetDatabase.LoadAssetAtPath<GameObject>(TwentyThreePrefabPath).GetComponent<TwentyThreeBoss>();
        var bossData = new SerializedObject(boss).FindProperty("data").objectReferenceValue;
        Assert.That(rules, Is.SameAs(bossData), "TwentyThree.prefab 이 쓰는 데이터와 같은 애셋");

        Assert.That(so.FindProperty("data").objectReferenceValue, Is.Not.Null, "MonsterDataSO(SpinnerBot 데이터)");
        Assert.That(TrainingSparringSegmentCheck.Evaluate(BossCounterProgress.GaugeMax, rules.counterGaugeStep).IsValid, Is.True, "총 칸 ≥ 2");
        Assert.That(so.FindProperty("finishHealth").intValue, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void Prefab_WhipStateExistsInController()
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        string state = new SerializedObject(go.GetComponent<TrainingSparringBot>()).FindProperty("whipState").stringValue;
        var controller = go.GetComponentInChildren<Animator>(true).runtimeAnimatorController as AnimatorController;
        Assert.That(controller, Is.Not.Null);
        Assert.That(HasState(controller.layers[0].stateMachine, state), Is.True, $"컨트롤러에 '{state}' 상태가 없다");
    }

    [Test]
    public void Prefab_IsRegisteredAsNetworkPrefab()
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
        Assert.That(list, Is.Not.Null);
        bool found = false;
        foreach (NetworkPrefab p in list.PrefabList)
            found |= p.Prefab == go;
        Assert.That(found, Is.True, "DefaultNetworkPrefabs 에 등록");
    }

    static bool HasState(AnimatorStateMachine sm, string name)
    {
        foreach (ChildAnimatorState s in sm.states)
            if (s.state.name == name) return true;
        foreach (ChildAnimatorStateMachine child in sm.stateMachines)
            if (HasState(child.stateMachine, name)) return true;
        return false;
    }
}
