using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 139-6 단계 1 — 간파 창 길이 해석기(<see cref="InterruptWindowLengthResolver"/>)와 예측 경과 게임초 통일.
/// 허수아비: 본게임 = 무기한, 랩 무기한 = 무기한, 랩 주기 = 열림 길이(서버만 — 원격 클라는 모름 → 관측 중앙값 폴백).
/// 단계 2 — 중간보스 3종·로비 스파링 봇 = <see cref="MonsterCounterWindow.WindowDuration"/>(서버·클라 같음), 23호는 아직 모름.
/// 서버·오너의 실제 시계 연결(InterruptLagMonitor·InterruptWindowReplicaTracker 의 Time.time)은 NetworkManager 가 필요해 Play 확인.
/// </summary>
public sealed class InterruptWindowLengthResolverTests
{
    sealed class FixedGate : ITrainingDummyWindowGate
    {
        readonly DummyInterruptWindowSchedule _schedule;
        public FixedGate(DummyInterruptWindowSchedule schedule) => _schedule = schedule;
        public bool TickWindowOpen(float deltaTime, bool recovering) => _schedule.Tick(deltaTime, recovering);
        public float OpenWindowLength => _schedule.OpenWindowLength;
    }

    static FixedGate Periodic(float open, float closed) =>
        new FixedGate(new DummyInterruptWindowSchedule(DummyInterruptWindowMode.Periodic, open, closed));

    // ─── 허수아비 ───

    [Test]
    public void 허수아비_게이트_없음_본게임은_모든_피어에서_무기한()
    {
        Assert.IsTrue(float.IsPositiveInfinity(InterruptWindowLengthResolver.ForTrainingDummy(true, null)));
        Assert.IsTrue(float.IsPositiveInfinity(InterruptWindowLengthResolver.ForTrainingDummy(false, null)));
    }

    [Test]
    public void 허수아비_랩_무기한은_서버에서_무기한()
    {
        var gate = new FixedGate(new DummyInterruptWindowSchedule(DummyInterruptWindowMode.Unbounded));
        Assert.IsTrue(float.IsPositiveInfinity(InterruptWindowLengthResolver.ForTrainingDummy(true, gate)));
    }

    [Test]
    public void 허수아비_랩_주기는_서버에서_열림_길이()
    {
        Assert.AreEqual(0.6f, InterruptWindowLengthResolver.ForTrainingDummy(true, Periodic(0.6f, 1.5f)), 1e-6f);
        Assert.IsTrue(float.IsPositiveInfinity(InterruptWindowLengthResolver.ForTrainingDummy(true, Periodic(0.6f, 0f))),
            "닫힘 0 = 계속 열림");
        Assert.AreEqual(0f, InterruptWindowLengthResolver.ForTrainingDummy(true, Periodic(0f, 1.5f)), "열림 0 = 열리지 않음");
    }

    [Test]
    public void 허수아비_랩_원격_클라는_모름_폴백()
    {
        // 호스트 패널의 Play 중 모드·길이 변경이 복제되지 않는다 — 클라 게이트 값은 틀릴 수 있다.
        Assert.AreEqual(InterruptWindowLengthResolver.Unknown,
            InterruptWindowLengthResolver.ForTrainingDummy(false, Periodic(0.6f, 1.5f)));
        Assert.AreEqual(InterruptWindowLengthResolver.Unknown,
            InterruptWindowLengthResolver.ForTrainingDummy(false,
                new FixedGate(new DummyInterruptWindowSchedule(DummyInterruptWindowMode.Unbounded))));
    }

    [Test]
    public void 스케줄_길이_변경과_모드_전환이_바로_반영된다()
    {
        var schedule = new DummyInterruptWindowSchedule(DummyInterruptWindowMode.Periodic, 0.6f, 1.5f);
        schedule.SetDurations(0.9f, 1.5f);
        Assert.AreEqual(0.9f, schedule.OpenWindowLength, 1e-6f);
        schedule.SetMode(DummyInterruptWindowMode.Unbounded);
        Assert.IsTrue(float.IsPositiveInfinity(schedule.OpenWindowLength));
    }

    // ─── 중간보스·로비 스파링 봇(단계 2) — 프리팹 실제 값 ───

    static GameObject LoadPrefab(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.IsNotNull(prefab, path);
        return prefab;
    }

    [TestCase("Assets/2.Prefabs/Monster/SpinnerBot.prefab", 1.5f)]
    [TestCase("Assets/2.Prefabs/Monster/GauntletBot.prefab", 2.0f)]
    [TestCase("Assets/2.Prefabs/Monster/WallBot.prefab", 1.5f)]
    [TestCase("Assets/2.Prefabs/Lobby/TrainingSparringBot.prefab", 1.5f)]   // SpinnerBot 프리팹 변형 — 값 상속
    public void 중간보스_스파링봇은_서버_클라_모두_창_길이_저작값_나누기_공격속도(string path, float authored)
    {
        GameObject prefab = LoadPrefab(path);
        var counter = prefab.GetComponent<MonsterCounterWindow>();
        var monster = prefab.GetComponent<MonsterBase>();
        Assert.IsNotNull(counter, "MonsterCounterWindow");
        Assert.IsNotNull(monster, "MonsterBase");

        float expected = authored / monster.AttackAnimSpeed;
        Assert.AreEqual(expected, counter.WindowDuration, 1e-5f, "프리팹 저작값");
        Assert.AreEqual(expected, InterruptWindowLengthResolver.Resolve(prefab, true), 1e-5f, "서버");
        Assert.AreEqual(expected, InterruptWindowLengthResolver.Resolve(prefab, false), 1e-5f, "원격 클라 — 같은 에셋이라 같은 값");
    }

    [Test]
    public void 어댑터_순서_허수아비와_중간보스가_겹치지_않고_23호는_맡지_않는다()
    {
        // 허수아비 = 허수아비 어댑터만(MonsterBase 없음) → 무기한.
        GameObject dummy = LoadPrefab("Assets/2.Prefabs/TrainingDummy.prefab");
        Assert.IsNull(dummy.GetComponent<MonsterBase>());
        Assert.IsTrue(float.IsPositiveInfinity(InterruptWindowLengthResolver.Resolve(dummy, false)));

        // 봇엔 허수아비 컴포넌트가 없다 — 중간보스 어댑터만 맡는다.
        foreach (string path in new[]
                 {
                     "Assets/2.Prefabs/Monster/SpinnerBot.prefab", "Assets/2.Prefabs/Monster/GauntletBot.prefab",
                     "Assets/2.Prefabs/Monster/WallBot.prefab", "Assets/2.Prefabs/Lobby/TrainingSparringBot.prefab",
                 })
            Assert.IsNull(LoadPrefab(path).GetComponent<TrainingDummyInterrupt>(), path);

        // 23호 프리팹에도 MonsterCounterWindow 가 있지만 23호 창은 자기 계통 — 단계 3 어댑터 전까지 모름(관측 중앙값).
        GameObject boss = LoadPrefab("Assets/2.Prefabs/Monster/Boss/TwentyThree.prefab");
        Assert.AreEqual(InterruptWindowLengthResolver.Unknown, InterruptWindowLengthResolver.Resolve(boss, true));
        Assert.AreEqual(InterruptWindowLengthResolver.Unknown, InterruptWindowLengthResolver.Resolve(boss, false));
    }

    // ─── 어댑터 체인 ───

    [Test]
    public void 어댑터_없거나_안_맡으면_모름_맡으면_그_값_이상값은_모름()
    {
        var go = new GameObject("resolver-test");
        float value = 0.75f;
        bool Fake(GameObject owner, bool isServer, out float length)
        {
            length = value;
            return owner == go;
        }

        InterruptWindowLengthResolver.Adapter adapter = Fake;
        try
        {
            Assert.AreEqual(InterruptWindowLengthResolver.Unknown, InterruptWindowLengthResolver.Resolve(go, true), "맡는 어댑터 없음");
            Assert.AreEqual(InterruptWindowLengthResolver.Unknown, InterruptWindowLengthResolver.Resolve(null, true));

            InterruptWindowLengthResolver.Register(adapter);
            Assert.AreEqual(0.75f, InterruptWindowLengthResolver.Resolve(go, false), 1e-6f);

            value = float.NaN;
            Assert.AreEqual(InterruptWindowLengthResolver.Unknown, InterruptWindowLengthResolver.Resolve(go, true), "NaN = 모름");
            value = -3f;
            Assert.AreEqual(InterruptWindowLengthResolver.Unknown, InterruptWindowLengthResolver.Resolve(go, true), "음수 = 모름");
            value = float.PositiveInfinity;
            Assert.IsTrue(float.IsPositiveInfinity(InterruptWindowLengthResolver.Resolve(go, true)), "무기한은 그대로");
        }
        finally
        {
            InterruptWindowLengthResolver.Unregister(adapter);
            Object.DestroyImmediate(go);
        }
    }

    // ─── 관측 중앙값 폴백 ───

    [Test]
    public void 로컬_길이_우선_모르면_관측_중앙값()
    {
        var history = new InterruptWindowHistory();
        history.Record(0d, true);
        history.Record(1.2d, false);
        history.Record(3d, true);

        history.Estimate(3.25d, true, InterruptWindowLengthResolver.Unknown, out float elapsed, out float expected);
        Assert.AreEqual(0.25f, elapsed, 1e-5f);
        Assert.AreEqual(1.2f, expected, 1e-5f, "모름 = 관측 중앙값");

        history.Estimate(3.25d, true, 0.6f, out _, out expected);
        Assert.AreEqual(0.6f, expected, 1e-6f, "아는 길이가 관측보다 우선");

        history.Estimate(3.25d, true, InterruptWindowLengthResolver.Unbounded, out _, out expected);
        Assert.IsTrue(float.IsPositiveInfinity(expected));

        // 관측도 로컬도 없으면 모름 그대로.
        var empty = new InterruptWindowHistory();
        empty.Record(0d, true);
        empty.Estimate(1d, true, InterruptWindowLengthResolver.Unknown, out _, out expected);
        Assert.AreEqual(-1f, expected);
    }

    // ─── 게임초 경과 ───

    [Test]
    public void 예측_경과는_게임초라_슬로우_중_실시간만큼_늘지_않는다()
    {
        // 주기 창 0.6초가 열린 직후 슬로우(시간 배율 0.1) 1실시간초. 예측 기록·추정은 게임초(Time.time) 시계 —
        // 창 타이머도 같은 게임초로 흐르므로 남은 창 = 0.6 − 0.1 = 0.5. (139-5 이전 ServerTime 실시간이면 경과 1.0 → 남은 0 → 약.)
        const float scale = 0.1f;
        const float realDt = 1f / 60f;
        var history = new InterruptWindowHistory();
        double gameNow = 10d, realNow = 10d;
        history.Record(gameNow, true);
        for (int i = 0; i < 60; i++)
        {
            gameNow += realDt * scale;
            realNow += realDt;
        }

        history.Estimate(gameNow, true, 0.6f, out float elapsed, out float expected);
        Assert.AreEqual(0.1f, elapsed, 1e-4f, "게임초 경과");
        Assert.Less(elapsed, (float)(realNow - 10d), "실시간 1초보다 짧다");

        DashInterruptPrediction p = DashInterruptPredictor.Predict(new DashInterruptPredictInput
        {
            CastTime = (float)gameNow,
            SkillStartTime = (float)gameNow,
            PrepDelay = 0.345f,
            HasTargetInRange = true,
            TargetWindowOpen = true,
            WindowOpenElapsed = elapsed,
            WindowExpectedDuration = expected,
        });
        Assert.AreEqual(0.5f, p.WindowRemaining, 1e-4f);
        Assert.AreEqual(InterruptAttemptForecast.Strong, p.Forecast);
    }
}
