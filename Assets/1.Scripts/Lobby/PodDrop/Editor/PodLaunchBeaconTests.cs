using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 할 일 284 — 로비 포드 출격 경광등: 켜고 끄는 구간·가속 단계·회전(순수 함수) + 프리팹 배선.
public sealed class PodLaunchBeaconTests
{
    const string PrefabPath = "Assets/2.Prefabs/Lobby/PodLaunchBeacon/PF_PodLaunchBeacon.prefab";
    const double NotYet = double.PositiveInfinity;

    [Test]
    public void IsOn_FromPodCinematicStartUntilEndBlack()
    {
        Assert.That(PodLaunchBeaconRules.IsOn(-0.01, 12.0), Is.False, "연출 시작 전");
        Assert.That(PodLaunchBeaconRules.IsOn(0.0, 12.0), Is.True, "연출 시작 순간(출격 확정) 켬");
        Assert.That(PodLaunchBeaconRules.IsOn(11.99, 12.0), Is.True);
        Assert.That(PodLaunchBeaconRules.IsOn(12.0, 12.0), Is.False, "다 검어진 순간 끔");
        Assert.That(PodLaunchBeaconRules.IsOn(30.0, 12.0), Is.False);
    }

    [Test]
    public void IsOn_StaysOnWhileEndBlackUnknown()
    {
        Assert.That(PodLaunchBeaconRules.IsOn(5.0, NotYet), Is.True, "하강 시작 전(다 검어질 시각 모름) = 켜진 채");
        Assert.That(PodLaunchBeaconRules.IsOn(double.NaN, 12.0), Is.False);
        Assert.That(PodLaunchBeaconRules.IsOn(1.0, double.NaN), Is.False);
    }

    [Test]
    public void IsOn_MatchesTimelineEndBlackAt()
    {
        var timings = PodDropLobbyTimings.Default; timings.launchEndWaitSeconds = 0.5f; timings.launchEndFadeSeconds = 1f;
        var timeline = new PodDropLobbyTimeline(timings, new[] { 2f });
        double descent = 8.0;
        double endBlack = timeline.EndBlackAt(descent);
        Assert.That(endBlack, Is.EqualTo(9.5).Within(1e-9));
        Assert.That(PodLaunchBeaconRules.IsOn(9.49, endBlack), Is.True);
        Assert.That(PodLaunchBeaconRules.IsOn(9.5, endBlack), Is.False);
    }

    [Test]
    public void Stage_FollowsReplicatedSpeedUpsAndLocalStage()
    {
        Assert.That(PodLaunchBeaconRules.Stage(3.0, NotYet, NotYet), Is.EqualTo(0));
        Assert.That(PodLaunchBeaconRules.Stage(3.0, 2.5, NotYet), Is.EqualTo(1));
        Assert.That(PodLaunchBeaconRules.Stage(2.4, 2.5, NotYet), Is.EqualTo(0));
        Assert.That(PodLaunchBeaconRules.Stage(6.0, 2.5, 5.0), Is.EqualTo(2));
        Assert.That(PodLaunchBeaconRules.Stage(3.0, NotYet, NotYet, 1), Is.EqualTo(1), "자기 가속 이벤트가 복제보다 먼저");
        Assert.That(PodLaunchBeaconRules.Stage(3.0, NotYet, NotYet, 9), Is.EqualTo(2), "범위 밖 단계는 2로");
        Assert.That(PodLaunchBeaconRules.Stage(6.0, 2.5, 5.0, 1), Is.EqualTo(2));
    }

    [Test]
    public void PeriodFor_PicksStageAndClamps()
    {
        float[] p = { 1.2f, 0.8f, 0.5f };
        Assert.That(PodLaunchBeaconRules.PeriodFor(0, p), Is.EqualTo(1.2f));
        Assert.That(PodLaunchBeaconRules.PeriodFor(1, p), Is.EqualTo(0.8f));
        Assert.That(PodLaunchBeaconRules.PeriodFor(2, p), Is.EqualTo(0.5f));
        Assert.That(PodLaunchBeaconRules.PeriodFor(5, p), Is.EqualTo(0.5f), "칸이 모자라면 마지막 칸");
        Assert.That(PodLaunchBeaconRules.PeriodFor(-1, p), Is.EqualTo(1.2f));
        Assert.That(PodLaunchBeaconRules.PeriodFor(0, new[] { 0f }), Is.EqualTo(PodLaunchBeaconRules.MinPeriod));
        Assert.That(PodLaunchBeaconRules.PeriodFor(0, null), Is.EqualTo(1f));
    }

    [Test]
    public void AdvanceYaw_OneTurnPerPeriod()
    {
        Assert.That(PodLaunchBeaconRules.AdvanceYaw(0f, 0.3f, 1.2f), Is.EqualTo(90f).Within(1e-3f));
        Assert.That(PodLaunchBeaconRules.AdvanceYaw(350f, 0.25f, 0.5f), Is.EqualTo(170f).Within(1e-3f));
        Assert.That(PodLaunchBeaconRules.AdvanceYaw(45f, 0f, 1f), Is.EqualTo(45f));
    }

    [Test]
    public void Prefab_IsWiredRealtimeNoShadowNotStatic()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null, PrefabPath);
        var beacon = prefab.GetComponent<PodLaunchBeacon>();
        Assert.That(beacon, Is.Not.Null);

        var so = new SerializedObject(beacon);
        Assert.That(so.FindProperty("rotor").objectReferenceValue, Is.Not.Null, "회전 피벗");
        Assert.That(so.FindProperty("domeRenderers").arraySize, Is.GreaterThan(0), "돔 렌더러");
        Assert.That(so.FindProperty("spots").arraySize, Is.GreaterThan(0), "Spot");

        foreach (Light l in prefab.GetComponentsInChildren<Light>(true))
        {
            Assert.That(l.type, Is.EqualTo(LightType.Spot), l.name);
            Assert.That(l.lightmapBakeType, Is.EqualTo(LightmapBakeType.Realtime), l.name);
            Assert.That(l.shadows, Is.EqualTo(LightShadows.None), l.name);
            Assert.That(l.enabled, Is.False, l.name + " — 꺼진 채 저장(켜고 끔은 컴포넌트)");
            Assert.That(l.cookie, Is.Not.Null, l.name + " — 바닥 4등분 무늬 쿠키(284 후속)");
        }

        foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
            Assert.That(GameObjectUtility.GetStaticEditorFlags(t.gameObject), Is.EqualTo((StaticEditorFlags)0), t.name + " — 회전하므로 Static 끔");

        foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
            Assert.That(r.GetComponent<Collider>(), Is.Null, r.name + " — 충돌 없음");

        var dome = (Renderer)so.FindProperty("domeRenderers").GetArrayElementAtIndex(0).objectReferenceValue;
        Assert.That(dome.sharedMaterial.IsKeywordEnabled("_EMISSION"), Is.True, "돔 머티리얼 _EMISSION 켬(MPB 로 색만 바꾼다)");
    }

    [Test]
    public void Component_ApplyAndTurnOff()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            var beacon = go.GetComponent<PodLaunchBeacon>();
            var lights = go.GetComponentsInChildren<Light>(true);
            var dome = (Renderer)new SerializedObject(beacon).FindProperty("domeRenderers").GetArrayElementAtIndex(0).objectReferenceValue;
            var mpb = new MaterialPropertyBlock();

            beacon.TurnOff();
            Assert.That(beacon.IsOn, Is.False);
            foreach (Light l in lights) Assert.That(l.enabled, Is.False);
            dome.GetPropertyBlock(mpb);
            Assert.That(mpb.GetColor("_EmissionColor").maxColorComponent, Is.EqualTo(0f).Within(1e-5f));

            beacon.Apply(true, 2);
            Assert.That(beacon.IsOn, Is.True);
            foreach (Light l in lights) Assert.That(l.enabled, Is.True);
            dome.GetPropertyBlock(mpb);
            Assert.That(mpb.GetColor("_EmissionColor").maxColorComponent, Is.GreaterThan(1f), "HDR 이미시브");

            beacon.Apply(false, 2);
            Assert.That(beacon.IsOn, Is.False);
            foreach (Light l in lights) Assert.That(l.enabled, Is.False);

            beacon.TurnOff();
            Assert.That(beacon.Yaw, Is.EqualTo(0f), "끄면 회전 각 처음으로");
        }
        finally { Object.DestroyImmediate(go); }
    }
}

// 할 일 284 후속 — 경광등 켜진 동안 씬 라이트 끄기: 켜져 있던 것만 끄고 그것만 되돌린다, 경광등 Spot·태양은 건너뜀.
public sealed class PodLaunchLightBlackoutTests
{
    static Light MakeLight(string name, bool enabled, LightType type = LightType.Point)
    {
        var go = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
        var l = go.AddComponent<Light>();
        l.type = type;
        l.enabled = enabled;
        return l;
    }

    [Test]
    public void BeginEnd_TurnsOffOnlyLitOthersAndRestoresThem()
    {
        Light lit = MakeLight("lit", true);
        Light alreadyOff = MakeLight("off", false);
        Light beaconSpot = MakeLight("beacon", false, LightType.Spot);
        Light otherDirectional = MakeLight("dir2", true, LightType.Directional);
        Light sun = MakeLight("sun", true, LightType.Directional);
        var all = new[] { lit, alreadyOff, beaconSpot, otherDirectional, sun };
        try
        {
            var blackout = new PodLaunchLightBlackout();
            beaconSpot.enabled = true; // 켬 전이 순간 경광등이 먼저 켜져 있어도 남긴다
            blackout.Begin(all, new HashSet<Light> { beaconSpot }, sun);

            Assert.That(blackout.Active, Is.True);
            Assert.That(blackout.TurnedOffCount, Is.EqualTo(2), "lit + 두 번째 Directional");
            Assert.That(lit.enabled, Is.False);
            Assert.That(otherDirectional.enabled, Is.False);
            Assert.That(alreadyOff.enabled, Is.False);
            Assert.That(beaconSpot.enabled, Is.True, "경광등 Spot 은 남김");
            Assert.That(sun.enabled, Is.True, "태양은 SceneLightDim 몫");

            blackout.Begin(all, null, null);
            Assert.That(blackout.TurnedOffCount, Is.EqualTo(2), "켜짐 중 다시 Begin = 무시");

            blackout.End();
            Assert.That(blackout.Active, Is.False);
            Assert.That(lit.enabled, Is.True);
            Assert.That(otherDirectional.enabled, Is.True);
            Assert.That(alreadyOff.enabled, Is.False, "원래 꺼져 있던 건 그대로");
            Assert.That(beaconSpot.enabled, Is.True);

            lit.enabled = false;
            blackout.End();
            Assert.That(lit.enabled, Is.False, "End 두 번 = 무시");
        }
        finally
        {
            foreach (Light l in all) Object.DestroyImmediate(l.gameObject);
        }
    }

    [Test]
    public void End_SkipsDestroyedLights()
    {
        Light a = MakeLight("a", true);
        Light b = MakeLight("b", true);
        var blackout = new PodLaunchLightBlackout();
        blackout.Begin(new[] { a, b }, null, null);
        Object.DestroyImmediate(a.gameObject);
        try
        {
            Assert.DoesNotThrow(blackout.End);
            Assert.That(b.enabled, Is.True);
        }
        finally { Object.DestroyImmediate(b.gameObject); }
    }
}
