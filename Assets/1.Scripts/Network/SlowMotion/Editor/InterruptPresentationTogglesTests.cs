using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 간파 연출 스위치(할 일 104) — 기본 꺼짐, 인스턴스 없음 = 꺼짐, 히트스톱 실림, 클립 이벤트 id 게이트, C1 꺼짐 + C2 켜짐.
/// </summary>
public sealed class InterruptPresentationTogglesTests
{
    private const string NetworkManagerPrefabPath = "Assets/2.Prefabs/Network/NetworkManager.prefab";

    private static readonly InterruptPresentationKind[] AllKinds =
        (InterruptPresentationKind[])Enum.GetValues(typeof(InterruptPresentationKind));

    private GameObject _go;

    [TearDown]
    public void TearDown()
    {
        if (_go != null)
            Object.DestroyImmediate(_go);
    }

    private InterruptPresentationToggles CreateToggles(params string[] onFields)
    {
        _go = new GameObject("Toggles");
        var toggles = _go.AddComponent<InterruptPresentationToggles>();
        var so = new SerializedObject(toggles);
        foreach (string field in onFields)
            so.FindProperty(field).boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        return toggles;
    }

    // ─── 기본값·인스턴스 ───

    [Test]
    public void 새_컴포넌트는_스위치_10개_전부_꺼짐()
    {
        var toggles = CreateToggles();
        Assert.AreEqual(10, AllKinds.Length);
        foreach (var kind in AllKinds)
            Assert.IsFalse(toggles.Get(kind), kind.ToString());
    }

    [Test]
    public void NetworkManager_프리팹에_붙어_있고_전부_꺼짐()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPrefabPath);
        Assert.IsNotNull(prefab, NetworkManagerPrefabPath);
        var toggles = prefab.GetComponent<InterruptPresentationToggles>();
        Assert.IsNotNull(toggles, "InterruptPresentationToggles 없음");
        Assert.IsNotNull(prefab.GetComponent<InterruptDarkAdaptation>(), "암순응과 같은 오브젝트여야 한다");
        foreach (var kind in AllKinds)
            Assert.IsFalse(toggles.Get(kind), kind.ToString());
    }

    [Test]
    public void 인스턴스가_없으면_전부_꺼짐()
    {
        Assume.That(InterruptPresentationToggles.Instance == null, "EditMode 에 인스턴스가 살아 있다");
        foreach (var kind in AllKinds)
        {
            Assert.IsFalse(InterruptPresentationToggles.IsOn(kind), kind.ToString());
            Assert.IsFalse(InterruptPresentationToggles.IsOn(null, kind), kind.ToString());
        }
    }

    [Test]
    public void 켠_칸만_켜짐()
    {
        var toggles = CreateToggles("darkAdaptation", "targetFlash");
        foreach (var kind in AllKinds)
        {
            bool expected = kind == InterruptPresentationKind.DarkAdaptation || kind == InterruptPresentationKind.TargetFlash;
            Assert.AreEqual(expected, InterruptPresentationToggles.IsOn(toggles, kind), kind.ToString());
        }
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

    [TestCase(InterruptPresentationToggles.PaladinShieldChargeId, InterruptPresentationKind.PaladinShieldCharge)]
    [TestCase(InterruptPresentationToggles.AssassinInsightWindupId, InterruptPresentationKind.AssassinInsightAttempt)]
    [TestCase(InterruptPresentationToggles.AssassinInsightPopId, InterruptPresentationKind.AssassinInsightAttempt)]
    [TestCase(InterruptPresentationToggles.AssassinInsightSlashId, InterruptPresentationKind.AssassinInsightAttempt)]
    public void 간파_id_는_꺼지면_막고_켜지면_통과(string id, InterruptPresentationKind kind)
    {
        Assert.IsTrue(InterruptPresentationToggles.TryGetAnimEffectKind(id, out var found));
        Assert.AreEqual(kind, found);
        Assert.IsFalse(InterruptPresentationToggles.AllowsAnimEffect(null, id));
        Assert.IsFalse(InterruptPresentationToggles.AllowsAnimEffect(CreateToggles(), id));
        string field = kind == InterruptPresentationKind.PaladinShieldCharge ? "paladinShieldCharge" : "assassinInsightAttempt";
        Object.DestroyImmediate(_go);
        Assert.IsTrue(InterruptPresentationToggles.AllowsAnimEffect(CreateToggles(field), id));
    }

    [TestCase("Assassin_NormalSlash_1")]
    [TestCase("Assassin_EnhancedSlash")]
    [TestCase("Slash")]
    [TestCase("")]
    [TestCase((string)null)]
    public void 다른_id_는_스위치와_무관하게_통과(string id)
    {
        Assert.IsFalse(InterruptPresentationToggles.TryGetAnimEffectKind(id, out _));
        Assert.IsTrue(InterruptPresentationToggles.AllowsAnimEffect(null, id));
    }

    [Test]
    public void 프리팹_소켓_id_가_상수와_같다()
    {
        AssertPrefabHasId("Assets/2.Prefabs/Player/Paladin/Player_Paladin.prefab", InterruptPresentationToggles.PaladinShieldChargeId);
        const string assassin = "Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab";
        AssertPrefabHasId(assassin, InterruptPresentationToggles.AssassinInsightWindupId);
        AssertPrefabHasId(assassin, InterruptPresentationToggles.AssassinInsightPopId);
        AssertPrefabHasId(assassin, InterruptPresentationToggles.AssassinInsightSlashId);
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
        insight.id = InterruptPresentationToggles.AssassinInsightSlashId;
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
        Assume.That(InterruptPresentationToggles.Instance == null);
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
        Assume.That(InterruptPresentationToggles.Instance == null);
        var events = CreateAnimEvents(out var insight, out _);
        events.StopEffect(insight.id);
        Assert.AreEqual(1, insight.stop);
    }

    [Test]
    public void C1_꺼짐이어도_올려치기_재생_알림은_난다_C2_성공_대기_기준()
    {
        // AssassinSkillView 는 Played(올려치기 id) 를 성공 이펙트(C2) 기준 프레임으로 쓴다 —
        // C1 을 꺼 올려치기 이펙트가 안 나와도 알림은 나야 C2 가 같은 프레임에 나온다.
        Assume.That(InterruptPresentationToggles.Instance == null);
        var events = CreateAnimEvents(out var insight, out _);
        string played = null;
        events.Played += id => played = id;

        events.PlayEffect(insight.id);

        Assert.AreEqual(0, insight.playOnce, "C1 꺼짐 — 올려치기 이펙트는 안 나온다");
        Assert.AreEqual(InterruptPresentationToggles.AssassinInsightSlashId, played);

        var toggles = CreateTogglesOn("assassinInsightSuccess");
        Assert.IsFalse(InterruptPresentationToggles.IsOn(toggles, InterruptPresentationKind.AssassinInsightAttempt));
        Assert.IsTrue(InterruptPresentationToggles.IsOn(toggles, InterruptPresentationKind.AssassinInsightSuccess));
    }

    // _go 를 덮지 않는 별도 스위치(테스트 끝에 같이 지운다)
    private InterruptPresentationToggles CreateTogglesOn(string field)
    {
        var go = new GameObject("Toggles");
        go.transform.SetParent(_go.transform);
        var toggles = go.AddComponent<InterruptPresentationToggles>();
        var so = new SerializedObject(toggles);
        so.FindProperty(field).boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        return toggles;
    }
}
