using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 할 일 277 후속 — 프랍 이미시브 애니 곡선(순수 함수)과 프리팹 배선.
public sealed class PropEmissiveAnimatorTests
{
    static PropEmissiveAnimator.Settings S(PropEmissiveAnimator.Wave w, float period, float min, float max) => new PropEmissiveAnimator.Settings
    {
        wave = w, period = period, min = min, max = max, pulseAttack = 0.15f,
        noiseAmount = 0f, noiseFrequency = 10f, dipInterval = 0f, dipDuration = 0.1f, dipDepth = 0.4f,
    };

    [Test]
    public void Breathe_HitsMinAtStartAndMaxAtHalfPeriod()
    {
        var s = S(PropEmissiveAnimator.Wave.Breathe, 2.5f, 0.6f, 1f);
        Assert.That(PropEmissiveAnimator.Evaluate(s, 0f, 1), Is.EqualTo(0.6f).Within(1e-4f));
        Assert.That(PropEmissiveAnimator.Evaluate(s, 1.25f, 1), Is.EqualTo(1f).Within(1e-4f));
        Assert.That(PropEmissiveAnimator.Evaluate(s, 2.5f, 1), Is.EqualTo(0.6f).Within(1e-4f));
    }

    [Test]
    public void Pulse_FastAttackSlowDecay()
    {
        var s = S(PropEmissiveAnimator.Wave.Pulse, 1.2f, 0.15f, 1f);
        float peakT = 1.2f * 0.15f;
        Assert.That(PropEmissiveAnimator.Evaluate(s, 0f, 1), Is.EqualTo(0.15f).Within(1e-4f));
        Assert.That(PropEmissiveAnimator.Evaluate(s, peakT, 1), Is.EqualTo(1f).Within(1e-3f));
        // 꺼지는 구간은 단조 감소
        float prev = 2f;
        for (float t = peakT + 0.01f; t < 1.19f; t += 0.05f)
        {
            float v = PropEmissiveAnimator.Evaluate(s, t, 1);
            Assert.That(v, Is.LessThanOrEqualTo(prev + 1e-5f));
            Assert.That(v, Is.InRange(0.15f - 1e-4f, 1f + 1e-4f));
            prev = v;
        }
    }

    [Test]
    public void Noise_StaysWithinAmountAndIsDeterministic()
    {
        var s = S(PropEmissiveAnimator.Wave.Constant, 1f, 1f, 1f);
        s.noiseAmount = 0.06f; s.noiseFrequency = 14f;
        bool varied = false;
        for (float t = 0f; t < 10f; t += 0.013f)
        {
            float v = PropEmissiveAnimator.Evaluate(s, t, 77);
            Assert.That(v, Is.InRange(0.94f - 1e-4f, 1.06f + 1e-4f));
            Assert.That(PropEmissiveAnimator.Evaluate(s, t, 77), Is.EqualTo(v));
            if (Mathf.Abs(v - 1f) > 0.01f) varied = true;
        }
        Assert.That(varied, Is.True);
    }

    [Test]
    public void Dip_OccursAboutOncePerIntervalAndReachesDepth()
    {
        var s = S(PropEmissiveAnimator.Wave.Constant, 1f, 1f, 1f);
        s.dipInterval = 8f; s.dipDuration = 0.12f; s.dipDepth = 0.4f;
        int dips = 0; bool inDip = false; float minV = 1f;
        const float dt = 0.002f;
        for (float t = 0f; t < 800f; t += dt)
        {
            float d = PropEmissiveAnimator.DipFactor(s, t, 5);
            minV = Mathf.Min(minV, d);
            bool now = d < 1f;
            if (now && !inDip) dips++;
            inDip = now;
        }
        Assert.That(minV, Is.EqualTo(0.4f).Within(1e-5f));
        // 한 딥 안에서 끊김(지직)이 있어 '켜짐→꺼짐' 진입이 여러 번 셀 수 있다 — 칸 수(100) 이상, 칸당 4번 이하
        Assert.That(dips, Is.InRange(100, 400));
    }

    [Test]
    public void Dip_Off_WhenIntervalZero()
    {
        var s = S(PropEmissiveAnimator.Wave.Constant, 1f, 1f, 1f);
        for (float t = 0f; t < 50f; t += 0.01f) Assert.That(PropEmissiveAnimator.DipFactor(s, t, 3), Is.EqualTo(1f));
    }

    // 원칙: 라이트 달린 프랍 = 스크립트(이미시브·라이트 같은 곡선), 라이트 없는 프랍 = 셰이더 시간 애니(스크립트 없음).
    [TestCase("Assets/2.Prefabs/Environment/Machinery/PowerUnits/object_alarm.prefab", PropEmissiveAnimator.Wave.Pulse)]
    [TestCase("Assets/2.Prefabs/Environment/Props/Office/Lobby/PF_Operations_SlideProjector_01_v001.prefab", PropEmissiveAnimator.Wave.Constant)]
    public void LightProp_HasAnimatorWithPresetAndLight(string path, PropEmissiveAnimator.Wave wave)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(go, Is.Not.Null, path);
        var a = go.GetComponent<PropEmissiveAnimator>();
        Assert.That(a, Is.Not.Null, path);
        Assert.That(a.CurrentSettings.wave, Is.EqualTo(wave));
        Assert.That(go.GetComponentInChildren<Light>(true), Is.Not.Null, path);
    }

    [TestCase("Assets/2.Prefabs/Environment/Props/Office/Lobby/PF_Computer_CRTMonitor_01_v003.prefab")]
    [TestCase("Assets/2.Prefabs/Environment/Props/Office/Lobby/PF_Office_CRTMonitor_Lobby_01.prefab")]
    [TestCase("Assets/2.Prefabs/Environment/Props/Furniture/Lobby/PF_ControlRoom_Desk_01_v003.prefab")]
    public void ShaderProp_UsesPropEmissiveShaderWithoutScript(string path)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(go, Is.Not.Null, path);
        Assert.That(go.GetComponentInChildren<PropEmissiveAnimator>(true), Is.Null, path);
        var mat = go.GetComponentInChildren<MeshRenderer>().sharedMaterial;
        Assert.That(mat.shader.name, Is.EqualTo("VeyTrace/Prop Emissive Lit"), path);
        Assert.That(mat.GetTexture("_ScreenTex"), Is.Not.Null, path);
    }

    // 데스크 버튼 = 12개 공통 숨쉬기(개별 패턴 칸 없음). 수치는 머티리얼에서 튜닝(SVN r680 사용자 값) — 관계만 확인.
    [Test]
    public void DeskButtons_CommonBreatheFields()
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2.Prefabs/Environment/Props/Furniture/Lobby/PF_ControlRoom_Desk_01_v003.prefab");
        var mat = go.GetComponentInChildren<MeshRenderer>().sharedMaterial;
        Assert.That(mat.GetFloat("_ButtonPeriod"), Is.GreaterThan(0f));
        Assert.That(mat.GetFloat("_ButtonMin"), Is.LessThanOrEqualTo(mat.GetFloat("_ButtonMax")));
        foreach (var old in new[] { "_ButtonGrid", "_ButtonOffLevel", "_ButtonSpeed" })
            Assert.That(mat.shader.FindPropertyIndex(old), Is.EqualTo(-1), old);
    }

    [Test]
    public void ProjectorSpot_IsRealtimeSoftShadow()
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2.Prefabs/Environment/Props/Office/Lobby/PF_Operations_SlideProjector_01_v001.prefab");
        var l = go.transform.Find("Light_ProjectorSpot").GetComponent<Light>();
        Assert.That(l.type, Is.EqualTo(LightType.Spot));
        Assert.That(l.shadows, Is.EqualTo(LightShadows.Soft));
        Assert.That(l.lightmapBakeType, Is.EqualTo(LightmapBakeType.Realtime));
    }
}
