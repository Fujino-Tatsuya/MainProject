using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 할 일 139-6 단계 3 — 23호 창 길이 어댑터(<see cref="TwentyThreeInterruptWindowLength"/>).
/// 종류별 길이 = 실제 SO 에셋(No23·No23_A·No23_B) 값, 종류 구별 = 가짜 공개 상태(애니메이터 상태·경과·서버 기믹 플래그).
/// 실제 애니메이터 구동·복제 bool 도착 시각은 Play 확인.
/// </summary>
public sealed class TwentyThreeInterruptWindowLengthTests
{
    const string DataDir = "Assets/2.Prefabs/Monster/Data/";
    const string PrefabPath = "Assets/2.Prefabs/Monster/Boss/TwentyThree.prefab";

    readonly List<Object> _created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object o in _created)
            Object.DestroyImmediate(o);
        _created.Clear();
    }

    static BossDataSO Load(string name)
    {
        var data = AssetDatabase.LoadAssetAtPath<BossDataSO>(DataDir + name + ".asset");
        Assert.IsNotNull(data, name);
        return data;
    }

    // 에셋을 건드리지 않는 사본 — 페이즈 구성만 바꿔 본다.
    BossDataSO CopyWithSequences(string name, params BossPhaseSequence[] sequences)
    {
        BossDataSO copy = Object.Instantiate(Load(name));
        _created.Add(copy);
        copy.phases = new BossPhaseEntry[sequences.Length];
        for (int i = 0; i < sequences.Length; i++)
            copy.phases[i] = new BossPhaseEntry { hpThreshold = 0.5f, sequence = sequences[i] };
        return copy;
    }

    static TwentyThreeWindowObservation Open(string state, float stateElapsed = -1f, float openElapsed = -1f, int serverRaid = -1) =>
        new TwentyThreeWindowObservation
        {
            WindowOpen = true,
            StateHash = Animator.StringToHash(state),
            StateElapsed = stateElapsed,
            OpenElapsed = openElapsed,
            ServerRaidGimmick = serverRaid,
        };

    static float LengthFor(TwentyThreeWindowObservation obs, BossDataSO data) =>
        TwentyThreeInterruptWindowLength.LengthOf(TwentyThreeInterruptWindowLength.Classify(obs, data), data);

    // ─── 종류별 길이 = 실제 SO 에셋 ───

    [TestCase("No23")]
    [TestCase("No23_A")]
    [TestCase("No23_B")]
    public void 종류별_길이는_SO_에셋_값(string name)
    {
        BossDataSO data = Load(name);
        Assert.AreEqual(1.5f, TwentyThreeInterruptWindowLength.LengthOf(TwentyThreeWindowKind.Dash, data), 1e-6f, "A 돌진 행 counterWindowDuration");
        Assert.AreEqual(0.7f, TwentyThreeInterruptWindowLength.LengthOf(TwentyThreeWindowKind.ChainedDash, data), 1e-6f, "A' comboDashWindup(공칭)");
        Assert.AreEqual(0.9f, TwentyThreeInterruptWindowLength.LengthOf(TwentyThreeWindowKind.RaidRelay, data), 1e-6f, "D relayCounterWindow");
        Assert.AreEqual(1.5f, TwentyThreeInterruptWindowLength.LengthOf(TwentyThreeWindowKind.RaidChain, data), 1e-6f, "E chainCounterWindow");
        Assert.AreEqual(3f, TwentyThreeInterruptWindowLength.LengthOf(TwentyThreeWindowKind.PushNorth, data), 1e-6f, "F pushNorthStunTime");

        // 단계 4 전 — 잡기·착지는 모름(관측 중앙값).
        Assert.AreEqual(-1f, TwentyThreeInterruptWindowLength.LengthOf(TwentyThreeWindowKind.Grab, data));
        Assert.AreEqual(-1f, TwentyThreeInterruptWindowLength.LengthOf(TwentyThreeWindowKind.JumpLanding, data));
        Assert.AreEqual(-1f, TwentyThreeInterruptWindowLength.LengthOf(TwentyThreeWindowKind.Unknown, data));
    }

    [Test]
    public void 릴레이_준비가_창보다_짧으면_준비_길이()
    {
        BossDataSO data = CopyWithSequences("No23_A", BossPhaseSequence.RaidInterruptRelay);
        data.relayPrepare = 0.6f;
        data.relayCounterWindow = 0.9f;
        Assert.AreEqual(0.6f, TwentyThreeInterruptWindowLength.LengthOf(TwentyThreeWindowKind.RaidRelay, data), 1e-6f);
    }

    // ─── 종류 구별(가짜 공개 상태) ───

    [Test]
    public void 창이_닫혀_있거나_상태를_모르면_모름()
    {
        BossDataSO data = Load("No23_A");
        TwentyThreeWindowObservation obs = Open("DashPrep", 0.1f, 0.1f, 0);
        obs.WindowOpen = false;
        Assert.AreEqual(TwentyThreeWindowKind.Unknown, TwentyThreeInterruptWindowLength.Classify(obs, data));

        obs = Open("DashPrep", 0.1f, 0.1f, 0);
        obs.StateHash = 0;
        Assert.AreEqual(TwentyThreeWindowKind.Unknown, TwentyThreeInterruptWindowLength.Classify(obs, data));
        Assert.AreEqual(-1f, LengthFor(Open("LeftHook"), data), "창을 안 여는 클립 = 모름");
    }

    [Test]
    public void 후속_돌진은_돌진_클립에서_공칭_길이()
    {
        BossDataSO data = Load("No23");
        Assert.AreEqual(TwentyThreeWindowKind.ChainedDash, TwentyThreeInterruptWindowLength.Classify(Open("DashAttack"), data));
        Assert.AreEqual(0.7f, LengthFor(Open("DashAttack"), data), 1e-6f);
    }

    [Test]
    public void 준비_클립_레이드_기믹_없는_데이터는_항상_일반_돌진()
    {
        // No23(송전기만)·No23_B(드론·북진) — 준비 클립을 쓰는 창은 일반 돌진뿐이라 경과를 몰라도 정확.
        foreach (string name in new[] { "No23", "No23_B" })
        {
            BossDataSO data = Load(name);
            Assert.AreEqual(TwentyThreeWindowKind.Dash, TwentyThreeInterruptWindowLength.Classify(Open("DashPrep"), data), name);
            Assert.AreEqual(1.5f, LengthFor(Open("DashPrep"), data), 1e-6f, name);
        }
    }

    [Test]
    public void 릴레이_데이터_서버는_공개_기믹_플래그로_가른다()
    {
        BossDataSO data = Load("No23_A");   // 송전기 + 간파 릴레이 — TwentyThree.prefab 기본 데이터
        Assert.AreEqual(TwentyThreeWindowKind.Dash, TwentyThreeInterruptWindowLength.Classify(Open("DashPrep", serverRaid: 0), data));
        Assert.AreEqual(TwentyThreeWindowKind.RaidRelay, TwentyThreeInterruptWindowLength.Classify(Open("DashPrep", serverRaid: 1), data));
        Assert.AreEqual(0.9f, LengthFor(Open("DashPrep", serverRaid: 1), data), 1e-6f);
    }

    [Test]
    public void 릴레이_데이터_클라는_열릴_때_준비_경과로_가른다()
    {
        BossDataSO data = Load("No23_A");
        Assert.AreEqual(0.9f, TwentyThreeInterruptWindowLength.RelayOpenOffset(data), 1e-6f, "relayPrepare 1.8 − relayCounterWindow 0.9");

        // 일반 돌진 — 준비 클립과 창 bool 이 같은 틱(도착 차 한두 프레임).
        Assert.AreEqual(TwentyThreeWindowKind.Dash,
            TwentyThreeInterruptWindowLength.Classify(Open("DashPrep", stateElapsed: 0.53f, openElapsed: 0.5f), data));
        // 릴레이 — 준비 0.9초 뒤 창이 열렸다.
        Assert.AreEqual(TwentyThreeWindowKind.RaidRelay,
            TwentyThreeInterruptWindowLength.Classify(Open("DashPrep", stateElapsed: 1.22f, openElapsed: 0.3f), data));
        Assert.AreEqual(0.9f, LengthFor(Open("DashPrep", 1.22f, 0.3f), data), 1e-6f);

        // 경과를 모르면(창 기록 없음·애니메이터 정지) 모름.
        Assert.AreEqual(TwentyThreeWindowKind.Unknown, TwentyThreeInterruptWindowLength.Classify(Open("DashPrep", -1f, 0.3f), data));
        Assert.AreEqual(TwentyThreeWindowKind.Unknown, TwentyThreeInterruptWindowLength.Classify(Open("DashPrep", 1.2f, -1f), data));
    }

    [Test]
    public void 릴레이_간격이_너무_짧으면_클라는_모름_서버는_그대로()
    {
        BossDataSO data = CopyWithSequences("No23_A", BossPhaseSequence.RaidInterruptRelay);
        data.relayPrepare = 1.0f;   // 간격 0.1초 < 0.3 — 일반 돌진(간격 0)과 못 가른다
        data.relayCounterWindow = 0.9f;
        Assert.AreEqual(TwentyThreeWindowKind.Unknown,
            TwentyThreeInterruptWindowLength.Classify(Open("DashPrep", 0.5f, 0.4f), data));
        Assert.AreEqual(TwentyThreeWindowKind.RaidRelay,
            TwentyThreeInterruptWindowLength.Classify(Open("DashPrep", 0.5f, 0.4f, serverRaid: 1), data));
    }

    [Test]
    public void 연쇄_데이터는_지목_시간_뒤_열린_창이_연쇄()
    {
        // 지금 에셋엔 연쇄(6)를 쓰는 데이터가 없다 — 사본으로 확인.
        BossDataSO data = CopyWithSequences("No23_A", BossPhaseSequence.ChargeSequence, BossPhaseSequence.RaidChainCounter);
        Assert.AreEqual(TwentyThreeWindowKind.RaidChain,
            TwentyThreeInterruptWindowLength.Classify(Open("DashPrep", stateElapsed: 1.4f, openElapsed: 0.4f), data));   // chainMarkTime 1
        Assert.AreEqual(1.5f, LengthFor(Open("DashPrep", 1.4f, 0.4f), data), 1e-6f);
        Assert.AreEqual(TwentyThreeWindowKind.Dash,
            TwentyThreeInterruptWindowLength.Classify(Open("DashPrep", stateElapsed: 0.4f, openElapsed: 0.4f), data));
    }

    [Test]
    public void 릴레이와_연쇄를_둘_다_쓰는_데이터는_레이드_쪽이면_모름()
    {
        BossDataSO data = CopyWithSequences("No23_A", BossPhaseSequence.RaidInterruptRelay, BossPhaseSequence.RaidChainCounter);
        Assert.AreEqual(TwentyThreeWindowKind.Unknown, TwentyThreeInterruptWindowLength.Classify(Open("DashPrep", serverRaid: 1), data));
        Assert.AreEqual(TwentyThreeWindowKind.Unknown, TwentyThreeInterruptWindowLength.Classify(Open("DashPrep", 1.3f, 0.3f), data));
        Assert.AreEqual(TwentyThreeWindowKind.Dash, TwentyThreeInterruptWindowLength.Classify(Open("DashPrep", serverRaid: 0), data));
        Assert.AreEqual(TwentyThreeWindowKind.Dash, TwentyThreeInterruptWindowLength.Classify(Open("DashPrep", 0.31f, 0.3f), data));
    }

    [Test]
    public void 북진은_그로기_진입_클립에서_무력화_길이_북진_없는_데이터는_모름()
    {
        BossDataSO pushNorth = Load("No23_B");   // 드론 + 북진
        Assert.AreEqual(TwentyThreeWindowKind.PushNorth, TwentyThreeInterruptWindowLength.Classify(Open("GroggyStart"), pushNorth));
        Assert.AreEqual(3f, LengthFor(Open("GroggyStart"), pushNorth), 1e-6f);

        Assert.AreEqual(TwentyThreeWindowKind.Unknown, TwentyThreeInterruptWindowLength.Classify(Open("GroggyStart"), Load("No23_A")));
    }

    [Test]
    public void 잡기_착지는_종류만_가르고_길이는_모름()
    {
        BossDataSO data = Load("No23");
        foreach (string s in new[] { "Grab", "Holding", "Throw" })
        {
            Assert.AreEqual(TwentyThreeWindowKind.Grab, TwentyThreeInterruptWindowLength.Classify(Open(s), data), s);
            Assert.AreEqual(-1f, LengthFor(Open(s), data), s);
        }
        foreach (string s in new[] { "JumpLanding", "Locomotion" })
        {
            Assert.AreEqual(TwentyThreeWindowKind.JumpLanding, TwentyThreeInterruptWindowLength.Classify(Open(s), data), s);
            Assert.AreEqual(-1f, LengthFor(Open(s), data), s);
        }
    }

    // ─── 프리팹·컨트롤러 배선 — 구별에 쓰는 상태 이름이 실제 0층 상태인가 ───

    [Test]
    public void 프리팹_애니메이터와_0층_상태가_구별_재료와_맞다()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab);
        var boss = prefab.GetComponent<TwentyThreeBoss>();
        Assert.IsNotNull(boss);

        // 어댑터는 GetComponentInChildren<Animator> 로 찾는다 — 보스가 쓰는 animator(protected)와 같아야 한다.
        var so = new SerializedObject(boss);
        Animator wired = so.FindProperty("animator").objectReferenceValue as Animator;
        Assert.IsNotNull(wired);
        Assert.AreSame(wired, prefab.GetComponentInChildren<Animator>(), "첫 Animator = 보스 animator");
        Assert.AreSame(Load("No23_A"), so.FindProperty("data").objectReferenceValue, "프리팹 기본 데이터");

        var controller = wired.runtimeAnimatorController as AnimatorController;
        Assert.IsNotNull(controller, "AnimatorController");
        var names = new HashSet<string>();
        Collect(controller.layers[0].stateMachine, names);
        foreach (string s in new[] { "DashPrep", "DashAttack", "GroggyStart", "Grab", "Holding", "Throw", "JumpLanding", "Locomotion" })
            Assert.IsTrue(names.Contains(s), $"0층 상태 '{s}' 없음");
    }

    static void Collect(AnimatorStateMachine machine, HashSet<string> names)
    {
        foreach (ChildAnimatorState s in machine.states) names.Add(s.state.name);
        foreach (ChildAnimatorStateMachine sub in machine.stateMachines) Collect(sub.stateMachine, names);
    }
}
