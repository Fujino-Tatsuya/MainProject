using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 간파 연출 디렉터 스위치(할 일 104 → 118 이관) — 기본 꺼짐, 인스턴스 없음 = 꺼짐, 히트스톱 실림, 클립 이벤트 id 게이트, C1 꺼짐 + C2 켜짐.
/// </summary>
public sealed class InterruptProductionDirectorTests
{
    private const string NetworkManagerPrefabPath = "Assets/2.Prefabs/Network/NetworkManager.prefab";

    private static readonly InterruptProductionCue[] AllCues =
        (InterruptProductionCue[])Enum.GetValues(typeof(InterruptProductionCue));

    private GameObject _go;

    [TearDown]
    public void TearDown()
    {
        if (_go != null)
            Object.DestroyImmediate(_go);
    }

    private InterruptProductionDirector CreateDirector(params string[] onFields)
    {
        _go = new GameObject("Director");
        var director = _go.AddComponent<InterruptProductionDirector>();
        var so = new SerializedObject(director);
        foreach (string field in onFields)
            so.FindProperty(field).boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        return director;
    }

    // ─── 기본값·인스턴스 ───

    [Test]
    public void 새_컴포넌트는_스위치_10개_전부_꺼짐()
    {
        var director = CreateDirector();
        Assert.AreEqual(10, AllCues.Length);
        foreach (var cue in AllCues)
            Assert.IsFalse(director.Get(cue), cue.ToString());
    }

    // 할 일 132 단계 6 — 새 체계에 들어오는 공용 칸(A1~A4)만 켜고, 캐릭터별 연출(B·C·E)은 꺼 둔다(새 적중 섬광이 대신).
    private static readonly InterruptProductionCue[] PrefabOnCues =
    {
        InterruptProductionCue.HitStop,
        InterruptProductionCue.DarkAdaptation,
        InterruptProductionCue.OwnerZoom,
        InterruptProductionCue.SuccessFxSlot,
    };

    [Test]
    public void NetworkManager_프리팹에_붙어_있고_A1_A4만_켜짐()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPrefabPath);
        Assert.IsNotNull(prefab, NetworkManagerPrefabPath);
        var director = prefab.GetComponent<InterruptProductionDirector>();
        Assert.IsNotNull(director, "InterruptProductionDirector 없음");
        Assert.IsNotNull(prefab.GetComponent<GlobalTimeScale>(), "GlobalTimeScale 과 같은 오브젝트여야 한다");
        Assert.IsNotNull(prefab.GetComponent<InterruptDarkAdaptation>(), "암순응과 같은 오브젝트여야 한다");
        foreach (var cue in AllCues)
            Assert.AreEqual(Array.IndexOf(PrefabOnCues, cue) >= 0, director.Get(cue), cue.ToString());
    }

    [Test]
    public void 인스턴스가_없으면_전부_꺼짐_줌_배율_1()
    {
        Assume.That(InterruptProductionDirector.Instance == null, "EditMode 에 인스턴스가 살아 있다");
        foreach (var cue in AllCues)
        {
            Assert.IsFalse(InterruptProductionDirector.IsOn(cue), cue.ToString());
            Assert.IsFalse(InterruptProductionRules.IsOn(null, cue), cue.ToString());
        }

        Assert.AreEqual(1f, InterruptProductionDirector.OwnerZoomFactor(SlowMotionPurpose.Attempt, 0.7f));
        Assert.AreEqual(1f, InterruptProductionDirector.OwnerZoomFactor(SlowMotionPurpose.AttemptWeak, 0.7f));
        Assert.AreEqual(1f, InterruptProductionDirector.OwnerZoomFactor(SlowMotionPurpose.Success, 0.7f));
    }

    [Test]
    public void 켠_칸만_켜짐()
    {
        var director = CreateDirector("darkAdaptation", "targetFlash");
        foreach (var cue in AllCues)
        {
            bool expected = cue == InterruptProductionCue.DarkAdaptation || cue == InterruptProductionCue.TargetFlash;
            Assert.AreEqual(expected, InterruptProductionRules.IsOn(director, cue), cue.ToString());
        }
    }

    [Test]
    public void 오너_줌_칸이_켜지면_배율_그대로_꺼지면_1()
    {
        Assert.AreEqual(1f, InterruptProductionRules.OwnerZoomFactor(CreateDirector(), 0.7f));
        Object.DestroyImmediate(_go);
        Assert.AreEqual(0.7f, InterruptProductionRules.OwnerZoomFactor(CreateDirector("ownerZoom"), 0.7f));
    }

    // ─── 시도 강·약(할 일 132) ───

    [Test]
    public void 성공_예측은_강_실패_예측은_약_대상_없음은_목적_없음()
    {
        Assert.IsTrue(InterruptProductionRules.TrySelectAttemptPurpose(BeaverLobby.Player.Dash.InterruptAttemptForecast.Strong, out SlowMotionPurpose strong));
        Assert.AreEqual(SlowMotionPurpose.Attempt, strong);
        Assert.IsTrue(InterruptProductionRules.TrySelectAttemptPurpose(BeaverLobby.Player.Dash.InterruptAttemptForecast.Weak, out SlowMotionPurpose weak));
        Assert.AreEqual(SlowMotionPurpose.AttemptWeak, weak);
        Assert.IsFalse(InterruptProductionRules.TrySelectAttemptPurpose(BeaverLobby.Player.Dash.InterruptAttemptForecast.NoTarget, out _));
    }

    [Test]
    public void 시도_목적별_프로필_선택()
    {
        var settings = new InterruptProductionStageTests.FakeSettings();
        Assert.AreSame(settings.Attempt, InterruptProductionRules.SelectAttemptProfile(SlowMotionPurpose.Attempt, settings));
        Assert.AreSame(settings.Weak, InterruptProductionRules.SelectAttemptProfile(SlowMotionPurpose.AttemptWeak, settings));
    }

    [TestCase(SlowMotionPurpose.Attempt, 0.7f)]
    [TestCase(SlowMotionPurpose.AttemptWeak, 0.93f)]
    [TestCase(SlowMotionPurpose.Success, 0.85f)]
    public void 슬로우_목적별_줌_배율_선택(SlowMotionPurpose purpose, float expected)
    {
        Assert.AreEqual(expected, InterruptProductionRules.SelectOwnerZoomFactor(purpose, 0.7f, 0.93f, 0.85f));
    }

    // 시도 예측 창 추정(InterruptLagMonitor.EstimateWindow 의 재료) — 열린 뒤 경과·추정 길이
    [Test]
    public void 창_기록_지금_열린_시각과_닫혀_있으면_없음()
    {
        var history = new InterruptWindowHistory();
        history.Record(1.0, true);
        history.Record(1.5, false);
        history.Record(3.0, true);
        Assert.IsTrue(history.TryGetOpenSince(3.2, out double since));
        Assert.AreEqual(3.0, since, 1e-9);
        Assert.IsFalse(history.TryGetOpenSince(2.0, out _), "닫힌 구간");
        Assert.IsFalse(new InterruptWindowHistory().TryGetOpenSince(1.0, out _), "기록 없음");
    }

    [Test]
    public void 창_추정_길이는_완결된_열림의_중앙값()
    {
        var history = new InterruptWindowHistory();
        Assert.IsFalse(history.TryGetTypicalOpenDuration(out _));
        history.Record(0.0, true);
        history.Record(0.4, false);
        history.Record(1.0, true);
        history.Record(1.6, false);
        history.Record(2.0, true);
        history.Record(2.5, false);
        history.Record(3.0, true); // 아직 열림 — 길이에 안 들어간다
        Assert.IsTrue(history.TryGetTypicalOpenDuration(out double duration));
        Assert.AreEqual(0.5, duration, 1e-9);
    }

    [Test]
    public void 새_디렉터_줌_배율_기본값_강_0_7_약_0_93()
    {
        var so = new SerializedObject(CreateDirector());
        Assert.AreEqual(0.7f, so.FindProperty("interruptZoomFactor").floatValue, 1e-6f);
        Assert.AreEqual(0.93f, so.FindProperty("interruptWeakZoomFactor").floatValue, 1e-6f);
    }

    // ─── A1 히트스톱 ───

    private static SlowMotionProfile SuccessProfile() => new SlowMotionProfile
    {
        scale = 0.25f, hitStopDuration = 0.05f, hitStopScale = 0.02f,
        enterDuration = 0.05f, holdDuration = 1.2f, exitDuration = 0.4f, failExitDuration = 0.1f,
    };

    [Test]
    public void 히트스톱_꺼짐이면_0_으로_실리고_나머지는_그대로()
    {
        var p = SuccessProfile();
        var e = GlobalTimeScale.CreateStartEvent(3, 10.0, 1, p, SlowMotionPurpose.Success, Vector3.one, false);
        Assert.AreEqual(0f, e.HitStopDuration);
        Assert.AreEqual(p.scale, e.Scale);
        Assert.AreEqual(p.hitStopScale, e.HitStopScale);
        Assert.AreEqual(p.enterDuration, e.EnterDuration);
        Assert.AreEqual(p.holdDuration, e.HoldDuration);
        Assert.AreEqual(p.exitDuration, e.ExitDuration);
        Assert.AreEqual(p.failExitDuration, e.FailExitDuration);
        Assert.AreEqual(SlowMotionPurpose.Success, e.Purpose);
        Assert.AreEqual(Vector3.one, e.TargetPosition);
    }

    [Test]
    public void 히트스톱_켜짐이면_원값()
    {
        var e = GlobalTimeScale.CreateStartEvent(3, 10.0, 1, SuccessProfile(), SlowMotionPurpose.Success, Vector3.zero, true);
        Assert.AreEqual(0.05f, e.HitStopDuration);
    }

    // ─── B2·C1 클립 이벤트 id ───

    [TestCase(InterruptProductionRules.PaladinShieldChargeId, InterruptProductionCue.PaladinShieldCharge)]
    [TestCase(InterruptProductionRules.AssassinInsightWindupId, InterruptProductionCue.AssassinInsightAttempt)]
    [TestCase(InterruptProductionRules.AssassinInsightPopId, InterruptProductionCue.AssassinInsightAttempt)]
    [TestCase(InterruptProductionRules.AssassinInsightSlashId, InterruptProductionCue.AssassinInsightAttempt)]
    public void 간파_id_는_꺼지면_막고_켜지면_통과(string id, InterruptProductionCue cue)
    {
        Assert.IsTrue(InterruptProductionRules.TryGetAnimEffectCue(id, out var found));
        Assert.AreEqual(cue, found);
        Assert.IsFalse(InterruptProductionRules.AllowsAnimEffect(null, id));
        Assert.IsFalse(InterruptProductionRules.AllowsAnimEffect(CreateDirector(), id));
        string field = cue == InterruptProductionCue.PaladinShieldCharge ? "paladinShieldCharge" : "assassinInsightAttempt";
        Object.DestroyImmediate(_go);
        Assert.IsTrue(InterruptProductionRules.AllowsAnimEffect(CreateDirector(field), id));
    }

    [TestCase("Assassin_NormalSlash_1")]
    [TestCase("Assassin_EnhancedSlash")]
    [TestCase("Slash")]
    [TestCase("")]
    [TestCase((string)null)]
    public void 다른_id_는_스위치와_무관하게_통과(string id)
    {
        Assert.IsFalse(InterruptProductionRules.TryGetAnimEffectCue(id, out _));
        Assert.IsTrue(InterruptProductionRules.AllowsAnimEffect(null, id));
    }

    [Test]
    public void 프리팹_소켓_id_가_상수와_같다()
    {
        AssertPrefabHasId("Assets/2.Prefabs/Player/Paladin/Player_Paladin.prefab", InterruptProductionRules.PaladinShieldChargeId);
        const string assassin = "Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab";
        AssertPrefabHasId(assassin, InterruptProductionRules.AssassinInsightWindupId);
        AssertPrefabHasId(assassin, InterruptProductionRules.AssassinInsightPopId);
        AssertPrefabHasId(assassin, InterruptProductionRules.AssassinInsightSlashId);
    }

    private static void AssertPrefabHasId(string path, string id)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.IsNotNull(prefab, path);
        foreach (var effect in prefab.GetComponentsInChildren<IAnimEventEffect>(true))
        {
            if (effect.Id == id)
                return;
        }
        Assert.Fail($"{path} 에 id '{id}' 소켓이 없다");
    }

    // ─── EffectAnimEvents 게이트 (인스턴스 없음 = 전부 꺼짐) ───

    private sealed class FakeEffect : MonoBehaviour, IAnimEventEffect
    {
        public string id;
        public int playOnce, play, stop;
        public string Id => id;
        public void PlayOnce() => playOnce++;
        public void Play() => play++;
        public void Stop() => stop++;
    }

    private EffectAnimEvents CreateAnimEvents(out FakeEffect insight, out FakeEffect other)
    {
        _go = new GameObject("Unit");
        insight = new GameObject("Insight").AddComponent<FakeEffect>();
        insight.id = InterruptProductionRules.AssassinInsightSlashId;
        insight.transform.SetParent(_go.transform);
        other = new GameObject("Other").AddComponent<FakeEffect>();
        other.id = "Assassin_NormalSlash_1";
        other.transform.SetParent(_go.transform);
        var events = _go.AddComponent<EffectAnimEvents>();
        // EditMode 에서는 Awake 가 안 돈다 — 색인만 만든다(릴레이 부착은 Animator 가 없어 생략).
        typeof(EffectAnimEvents).GetMethod("BuildIndex", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(events, null);
        return events;
    }

    [Test]
    public void 꺼짐이면_간파_id_재생만_막고_다른_id_는_재생()
    {
        Assume.That(InterruptProductionDirector.Instance == null);
        var events = CreateAnimEvents(out var insight, out var other);

        events.PlayEffect(insight.id);
        events.StartEffect(insight.id);
        events.PlayEffect(other.id);
        events.StartEffect(other.id);

        Assert.AreEqual(0, insight.playOnce);
        Assert.AreEqual(0, insight.play);
        Assert.AreEqual(1, other.playOnce);
        Assert.AreEqual(1, other.play);
    }

    [Test]
    public void 꺼짐이어도_Stop_은_통과()
    {
        Assume.That(InterruptProductionDirector.Instance == null);
        var events = CreateAnimEvents(out var insight, out _);
        events.StopEffect(insight.id);
        Assert.AreEqual(1, insight.stop);
    }

    [Test]
    public void C1_꺼짐이어도_올려치기_재생_알림은_난다_C2_성공_대기_기준()
    {
        // AssassinSkillView 는 Played(올려치기 id) 를 성공 이펙트(C2) 기준 프레임으로 쓴다 —
        // C1 을 꺼 올려치기 이펙트가 안 나와도 알림은 나야 C2 가 같은 프레임에 나온다.
        Assume.That(InterruptProductionDirector.Instance == null);
        var events = CreateAnimEvents(out var insight, out _);
        string played = null;
        events.Played += id => played = id;

        events.PlayEffect(insight.id);

        Assert.AreEqual(0, insight.playOnce, "C1 꺼짐 — 올려치기 이펙트는 안 나온다");
        Assert.AreEqual(InterruptProductionRules.AssassinInsightSlashId, played);

        var director = CreateDirectorOn("assassinInsightSuccess");
        Assert.IsFalse(InterruptProductionRules.IsOn(director, InterruptProductionCue.AssassinInsightAttempt));
        Assert.IsTrue(InterruptProductionRules.IsOn(director, InterruptProductionCue.AssassinInsightSuccess));
    }

    // _go 를 덮지 않는 별도 디렉터(테스트 끝에 같이 지운다)
    private InterruptProductionDirector CreateDirectorOn(string field)
    {
        var go = new GameObject("Director");
        go.transform.SetParent(_go.transform);
        var director = go.AddComponent<InterruptProductionDirector>();
        var so = new SerializedObject(director);
        so.FindProperty(field).boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        return director;
    }
}
