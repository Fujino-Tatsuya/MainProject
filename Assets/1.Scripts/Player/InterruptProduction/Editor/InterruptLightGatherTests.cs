using System.Collections.Generic;
using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 빛 모임(할 일 132 단계 4) — 재생 규칙(강만·오너 중복 방지·정지 경로), 소켓 폴백(없으면 캐릭터 중심),
/// 덮어쓸 텍스처 칸 → 절차 생성 키워드 선택, 시간 기준 칸, 그리고 프리팹 배선(base 빈 칸·Variant 소켓·VFX 에셋).
/// </summary>
public sealed class InterruptLightGatherTests
{
    private const string PlayerPrefabPath = "Assets/2.Prefabs/Player/Player.prefab";
    private const string VfxPrefabPath = "Assets/5.VFX/Interrupt/LightGather/FX_InterruptLightGather.prefab";
    private const string ShaderName = "BeaverLobby/VFX/InterruptLightGather";

    private sealed class FakeGather : IInterruptLightGatherPlayer
    {
        public readonly GameObject Root;
        public int Plays;
        public int Stops;

        public FakeGather(GameObject root = null) => Root = root;

        public GameObject Play()
        {
            Plays++;
            return Root;
        }

        public void Stop() => Stops++;
    }

    private sealed class FakeTimings : IInterruptOwnerPresentationSettings
    {
        public float OwnerDimInDuration => 0.125f;
        public float OwnerDimOutDuration => 0.25f;
    }

    private readonly List<Object> _created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object o in _created)
        {
            if (o != null)
                Object.DestroyImmediate(o);
        }

        _created.Clear();
    }

    private T Track<T>(T o) where T : Object
    {
        _created.Add(o);
        return o;
    }

    // ─── 누가 트나(팀원 화면) ───

    [Test]
    public void 남의_강_시도면_팀원_화면이_튼다()
    {
        Assert.IsTrue(InterruptLightGatherRules.ShouldPlayForPeer(SlowMotionPurpose.Attempt, false));
    }

    [Test]
    public void 본인_강_시도는_서버_이벤트로_다시_틀지_않는다()
    {
        // 본인 화면은 입력 순간 오너 예측이 이미 켰다 — 서버 강 이벤트로 중복 재생 금지.
        Assert.IsFalse(InterruptLightGatherRules.ShouldPlayForPeer(SlowMotionPurpose.Attempt, true));
    }

    [Test]
    public void 약_시도와_성공은_틀지_않는다()
    {
        Assert.IsFalse(InterruptLightGatherRules.ShouldPlayForPeer(SlowMotionPurpose.AttemptWeak, false));
        Assert.IsFalse(InterruptLightGatherRules.ShouldPlayForPeer(SlowMotionPurpose.Success, false));
    }

    // ─── 팀원 화면 빛 모임 목록 ───

    [Test]
    public void 같은_발동자는_한_번만_튼다()
    {
        var peers = new InterruptLightGatherPeers();
        var gather = new FakeGather();
        Assert.IsTrue(peers.Begin(2, 10, gather));
        Assert.IsFalse(peers.Begin(2, 11, gather), "돌고 있으면 다시 틀지 않는다");
        Assert.AreEqual(1, gather.Plays);
        Assert.AreEqual(1, peers.Count);
    }

    [Test]
    public void 발동자_Hit_이면_그_발동자만_끈다()
    {
        var peers = new InterruptLightGatherPeers();
        var a = new FakeGather();
        var b = new FakeGather();
        peers.Begin(1, 10, a);
        peers.Begin(2, 11, b);

        Assert.IsTrue(peers.EndCaster(1));
        Assert.AreEqual(1, a.Stops);
        Assert.AreEqual(0, b.Stops);
        Assert.IsFalse(peers.IsPlaying(1));
        Assert.IsTrue(peers.IsPlaying(2));
        Assert.IsFalse(peers.EndCaster(1), "두 번째 Hit 은 아무것도 안 한다");
    }

    [Test]
    public void 실패_복귀는_그_발동_번호만_끈다()
    {
        var peers = new InterruptLightGatherPeers();
        var a = new FakeGather();
        peers.Begin(1, 10, a);

        Assert.IsFalse(peers.EndTrigger(0), "발동 번호 0 은 무시");
        Assert.IsFalse(peers.EndTrigger(99));
        Assert.AreEqual(0, a.Stops);
        Assert.IsTrue(peers.EndTrigger(10));
        Assert.AreEqual(1, a.Stops);
    }

    [Test]
    public void 다시_받은_강_시도는_발동_번호를_새로_잡는다()
    {
        var peers = new InterruptLightGatherPeers();
        var a = new FakeGather();
        peers.Begin(1, 10, a);
        peers.Begin(1, 12, a);

        Assert.IsFalse(peers.EndTrigger(10), "옛 번호의 실패 복귀로 끄지 않는다");
        Assert.IsTrue(peers.EndTrigger(12));
    }

    [Test]
    public void 강제_리셋은_전부_끈다()
    {
        var peers = new InterruptLightGatherPeers();
        var a = new FakeGather();
        var b = new FakeGather();
        peers.Begin(1, 10, a);
        peers.Begin(2, 11, b);

        peers.EndAll();
        Assert.AreEqual(0, peers.Count);
        Assert.AreEqual(1, a.Stops);
        Assert.AreEqual(1, b.Stops);
    }

    [Test]
    public void 발동자_소켓을_못_찾으면_아무것도_안_한다()
    {
        var peers = new InterruptLightGatherPeers();
        Assert.IsFalse(peers.Begin(1, 10, null));
        Assert.AreEqual(0, peers.Count);
    }

    // ─── 오너 출력 ───

    [Test]
    public void 오너_시작은_소켓을_틀고_VFX_루트를_암전에_남긴다()
    {
        var root = Track(new GameObject("Root"));
        var gather = new FakeGather(root);
        var kept = new List<GameObject>();
        var output = new InterruptLightGatherOwnerOutput(_ => gather, kept.Add);

        output.StartLightGather(null);
        Assert.AreEqual(1, gather.Plays);
        CollectionAssert.AreEqual(new[] { root }, kept);

        output.StopLightGather();
        Assert.AreEqual(1, gather.Stops);
        Assert.IsNull(output.Playing);
    }

    [Test]
    public void 오너_소켓이_없으면_아무것도_안_한다()
    {
        var kept = new List<GameObject>();
        var output = new InterruptLightGatherOwnerOutput(_ => null, kept.Add);

        output.StartLightGather(null);
        output.StopLightGather();
        Assert.IsEmpty(kept);
    }

    [Test]
    public void 오너_다시_시작하면_앞_것을_먼저_끈다()
    {
        var first = new FakeGather();
        var second = new FakeGather();
        var queue = new Queue<IInterruptLightGatherPlayer>(new[] { first, second });
        var output = new InterruptLightGatherOwnerOutput(_ => queue.Dequeue(), null);

        output.StartLightGather(null);
        output.StartLightGather(null);
        Assert.AreEqual(1, first.Stops);
        Assert.AreEqual(1, second.Plays);
        Assert.AreSame(second, output.Playing);
    }

    [Test]
    public void 오너_강_예측만_틀고_Hit_에_멈춘다()
    {
        var gather = new FakeGather();
        var output = new InterruptLightGatherOwnerOutput(_ => gather, null);
        var sm = new InterruptOwnerPresentation(new FakeTimings(), null, output);

        Assert.IsFalse(sm.Predict(InterruptAttemptForecast.NoTarget, null, 0f));
        Assert.AreEqual(0, gather.Plays, "대상 없음 = 아무것도 안 켬");

        Assert.IsTrue(sm.Predict(InterruptAttemptForecast.Weak, null, 0f));
        Assert.AreEqual(0, gather.Plays, "약 = 빛 모임 없음");
        sm.LocalEnded(0.1f);
        sm.Tick(1f);

        Assert.IsTrue(sm.Predict(InterruptAttemptForecast.Strong, null, 2f));
        Assert.AreEqual(1, gather.Plays, "강 = 입력 순간 시작");

        sm.ServerStarted(5, SlowMotionPurpose.Attempt, true, 2.05f);
        Assert.AreEqual(1, gather.Plays, "서버 강 이벤트로 다시 틀지 않는다");

        sm.LocalHit();
        Assert.AreEqual(1, gather.Stops, "Hit 이벤트에 정지");
    }

    [Test]
    public void 오너_예측_빗나감이면_멈춘다()
    {
        var gather = new FakeGather();
        var output = new InterruptLightGatherOwnerOutput(_ => gather, null);
        var sm = new InterruptOwnerPresentation(new FakeTimings(), null, output);

        sm.Predict(InterruptAttemptForecast.Strong, null, 0f);
        sm.ServerStarted(5, SlowMotionPurpose.AttemptWeak, true, 0.05f);
        Assert.AreEqual(1, gather.Stops);
    }

    [Test]
    public void 받아들임_조건은_예측과_같다()
    {
        var sm = new InterruptOwnerPresentation(new FakeTimings());
        Assert.IsFalse(sm.Accepts(InterruptAttemptForecast.NoTarget));
        Assert.IsTrue(sm.Accepts(InterruptAttemptForecast.Strong));
        Assert.IsTrue(sm.Accepts(InterruptAttemptForecast.Weak));

        sm.Predict(InterruptAttemptForecast.Strong, null, 0f);
        Assert.IsFalse(sm.Accepts(InterruptAttemptForecast.Strong), "결론 안 난 시도 중이면 중복 입력");
    }

    [Test]
    public void 대상을_먼저_넘기면_빛_모임_루트가_암전에_남는다()
    {
        // 디렉터 LocalOwnerAttempt 순서: SetSubjects → Predict(안에서 빛 모임 시작 → KeepWhileDimmed) → 다음 프레임 암전 시작에 태깅.
        var dim = new InterruptOwnerDimOutput(null, false);
        var gatherRoot = Track(new GameObject("Gather"));
        gatherRoot.AddComponent<MeshRenderer>();
        var owner = Track(new GameObject("Owner"));

        dim.SetSubjects(owner, null);
        dim.KeepWhileDimmed(gatherRoot);
        dim.SetOwnerDim(1f);
        try
        {
            Assert.AreEqual(1, dim.TaggedCount);
            Assert.IsTrue(InterruptOwnerMaskLayers.Has(gatherRoot.GetComponent<Renderer>().renderingLayerMask));
        }
        finally
        {
            dim.SetOwnerDim(0f);
        }
    }

    // ─── 소켓 폴백 ───

    [Test]
    public void 소켓이_비면_캐릭터_중심()
    {
        var center = Track(new GameObject("Center")).transform;
        Assert.AreSame(center, InterruptLightGatherRules.ResolveAnchor(null, center));
    }

    [Test]
    public void 소켓이_있으면_소켓()
    {
        var center = Track(new GameObject("Center")).transform;
        var socket = Track(new GameObject("Socket")).transform;
        Assert.AreSame(socket, InterruptLightGatherRules.ResolveAnchor(socket, center));
    }

    [Test]
    public void 소켓이_파괴됐으면_캐릭터_중심()
    {
        var center = Track(new GameObject("Center")).transform;
        var socket = new GameObject("Socket").transform;
        Object.DestroyImmediate(socket.gameObject);
        Assert.AreSame(center, InterruptLightGatherRules.ResolveAnchor(socket, center));
    }

    [Test]
    public void 소켓_컴포넌트_칸이_비면_자기_자신에_붙는다()
    {
        var go = Track(new GameObject("Player"));
        var socket = go.AddComponent<InterruptLightGatherSocket>();
        Assert.AreSame(go.transform, socket.Anchor);
        Assert.IsNull(socket.Play(), "VFX 프리팹이 없으면 아무것도 안 튼다");
    }

    [Test]
    public void 소켓_VFX_는_한_번만_만들어_재사용하고_소켓_위치로_간다()
    {
        var go = Track(new GameObject("Player"));
        var anchor = new GameObject("Anchor").transform;
        anchor.SetParent(go.transform, false);
        anchor.localPosition = new Vector3(1f, 2f, 3f);
        anchor.localRotation = Quaternion.Euler(30f, 40f, 50f);

        var socket = go.AddComponent<InterruptLightGatherSocket>();
        var so = new SerializedObject(socket);
        so.FindProperty("socket").objectReferenceValue = anchor;
        so.FindProperty("vfxPrefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<InterruptLightGatherVfx>(VfxPrefabPath);
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject first = socket.Play();
        Assert.IsNotNull(first);
        Assert.AreSame(go.transform, first.transform.parent, "플레이어 아래 둔다(플레이어와 함께 사라진다)");
        Assert.AreEqual(anchor.position, first.transform.position);
        Assert.AreEqual(Quaternion.identity, first.transform.rotation, "회전은 월드 기준(기둥이 위로 선다)");

        socket.Stop();
        GameObject second = socket.Play();
        Assert.AreSame(first, second, "매 재생 생성 금지 — 재사용");

        foreach (ParticleSystem ps in first.GetComponentsInChildren<ParticleSystem>(true))
            Assert.IsFalse(ps.main.useUnscaledTime, $"{ps.name} — 기본 스케일 시간(슬로우를 따라 느려진다)");
    }

    // ─── 덮어쓸 텍스처 → 절차 생성 키워드 ───

    [Test]
    public void 텍스처_칸이_비면_절차_생성()
    {
        Assert.IsFalse(InterruptLightGatherRules.UsesOverrideTexture(null));
        var tex = Track(new Texture2D(2, 2));
        Assert.IsTrue(InterruptLightGatherRules.UsesOverrideTexture(tex));
    }

    [Test]
    public void 텍스처_칸에_맞춰_키워드를_켜고_끈다()
    {
        var shader = Shader.Find(ShaderName);
        Assert.IsNotNull(shader, ShaderName);
        var mat = Track(new Material(shader));
        Assert.IsFalse(InterruptLightGatherRules.ApplyOverrideKeyword(mat), "빈 칸 + 꺼짐 = 바꿀 것 없음");
        Assert.IsFalse(mat.IsKeywordEnabled(InterruptLightGatherRules.OverrideTextureKeyword));

        mat.SetTexture(InterruptLightGatherRules.OverrideTextureProperty, Track(new Texture2D(2, 2)));
        Assert.IsTrue(InterruptLightGatherRules.ApplyOverrideKeyword(mat));
        Assert.IsTrue(mat.IsKeywordEnabled(InterruptLightGatherRules.OverrideTextureKeyword));
        Assert.IsFalse(InterruptLightGatherRules.ApplyOverrideKeyword(mat), "같으면 바꾸지 않는다");

        mat.SetTexture(InterruptLightGatherRules.OverrideTextureProperty, null);
        Assert.IsTrue(InterruptLightGatherRules.ApplyOverrideKeyword(mat));
        Assert.IsFalse(mat.IsKeywordEnabled(InterruptLightGatherRules.OverrideTextureKeyword));
    }

    [Test]
    public void 칸_없는_재질은_건드리지_않는다()
    {
        var mat = Track(new Material(Shader.Find("Hidden/InternalErrorShader")));
        Assert.IsFalse(InterruptLightGatherRules.ApplyOverrideKeyword(mat));
        Assert.IsFalse(InterruptLightGatherRules.ApplyOverrideKeyword(null));
    }

    // ─── 소켓 라이트(§16) ───

    private InterruptLightGatherVfx NewGatherVfx()
    {
        var go = Track(new GameObject("Gather"));
        go.AddComponent<ParticleSystem>();
        return go.AddComponent<InterruptLightGatherVfx>();
    }

    [Test]
    public void 상승_곡선은_차지_동안_오르고_그_뒤_유지()
    {
        AnimationCurve linear = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        Assert.AreEqual(0f, InterruptEffectLight.Rise(linear, 4f, -0.1f, 0.5f), "시작 전");
        Assert.AreEqual(0f, InterruptEffectLight.Rise(linear, 4f, 0f, 0.5f));
        Assert.AreEqual(2f, InterruptEffectLight.Rise(linear, 4f, 0.25f, 0.5f), 1e-5f);
        Assert.AreEqual(4f, InterruptEffectLight.Rise(linear, 4f, 0.5f, 0.5f), 1e-5f);
        Assert.AreEqual(4f, InterruptEffectLight.Rise(linear, 4f, 3f, 0.5f), 1e-5f, "차지가 길어도 꺼지지 않는다(끄는 쪽은 Hit)");
        Assert.AreEqual(4f, InterruptEffectLight.Rise(linear, 4f, 0f, 0f), "길이 0 = 바로 최대");
    }

    [Test]
    public void 빛_모임_라이트는_차지_동안_세지고_정지에서_꺼진다()
    {
        InterruptLightGatherVfx vfx = NewGatherVfx();
        vfx.Play(null);
        Light light = vfx.GatherLight;
        Assert.IsNotNull(light, "첫 재생 때 만든다");
        Assert.AreEqual(LightType.Point, light.type);
        Assert.AreEqual(LightShadows.None, light.shadows, "그림자 기본 끔");
        Assert.AreEqual(0f, light.intensity, "차지 시작 = 0");

        vfx.TickLight(vfx.LightRiseDuration * 0.5f);
        float half = light.intensity;
        Assert.IsTrue(light.enabled);
        Assert.Greater(half, 0f);
        Assert.Less(half, vfx.LightIntensityPeak);

        vfx.TickLight(vfx.LightRiseDuration);
        Assert.AreEqual(vfx.LightIntensityPeak, light.intensity, 1e-4f, "최대에서 유지");

        vfx.Stop();
        Assert.IsFalse(light.enabled, "Hit(정지) = 바로 끔");
        Assert.AreEqual(0f, light.intensity);
        vfx.TickLight(1f);
        Assert.IsFalse(light.enabled, "정지 뒤엔 다시 켜지지 않는다");
    }

    [Test]
    public void 빛_모임_라이트는_재사용한다()
    {
        InterruptLightGatherVfx vfx = NewGatherVfx();
        vfx.Play(null);
        Light first = vfx.GatherLight;
        int children = vfx.transform.childCount;
        vfx.Stop();

        vfx.Play(null);
        Assert.AreSame(first, vfx.GatherLight, "두 번째 재생에 새 라이트 없음");
        Assert.AreEqual(children, vfx.transform.childCount, "새 오브젝트 없음");
        Assert.AreEqual(0f, vfx.GatherLight.intensity, "다시 0 에서 오른다");
    }

    // ─── 프리팹 ───

    [Test]
    public void Player_base_루트에_있고_소켓_칸은_비어_있다()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.IsNotNull(prefab, PlayerPrefabPath);
        var socket = prefab.GetComponent<InterruptLightGatherSocket>();
        Assert.IsNotNull(socket, "Player.prefab 루트에 InterruptLightGatherSocket 이 없다");

        var so = new SerializedObject(socket);
        Assert.IsNull(so.FindProperty("socket").objectReferenceValue, "base 소켓 칸은 비워 둔다 = 캐릭터 중심");
        Assert.AreEqual(AssetDatabase.LoadAssetAtPath<InterruptLightGatherVfx>(VfxPrefabPath),
            so.FindProperty("vfxPrefab").objectReferenceValue);
    }

    [TestCase("Assets/2.Prefabs/Player/Paladin/Player_Paladin.prefab", "SM_Wep_Shield_01")]
    [TestCase("Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab", "Dagger_Weapon_R")]
    [TestCase("Assets/2.Prefabs/Player/Gunner/Player_Gunner.prefab", "Muzzle")]
    public void 캐릭터_Variant_가_소켓을_연결한다(string path, string socketName)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.IsNotNull(prefab, path);
        var socket = prefab.GetComponent<InterruptLightGatherSocket>();
        Assert.IsNotNull(socket, path);

        Transform anchor = socket.Anchor;
        Assert.AreEqual(socketName, anchor.name, path);
        Assert.IsTrue(anchor.IsChildOf(prefab.transform) && anchor != prefab.transform, "자기 계층 안 소켓");
    }

    [Test]
    public void VFX_프리팹_기본값()
    {
        var vfx = AssetDatabase.LoadAssetAtPath<InterruptLightGatherVfx>(VfxPrefabPath);
        Assert.IsNotNull(vfx, VfxPrefabPath);
        Assert.AreEqual(InterruptEffectTimeBase.Scaled, vfx.TimeBase, "기본 스케일 — 강 슬로우 0.15 를 따라 느려진다(할 일 132 §14)");

        var so = new SerializedObject(vfx);
        Assert.AreSame(vfx.GetComponent<ParticleSystem>(), so.FindProperty("root").objectReferenceValue);
        Assert.Greater(vfx.LightIntensityPeak, 0f, "소켓 라이트 칸(§16)");
        Assert.AreEqual(LightShadows.None, (LightShadows)so.FindProperty("lightShadows").enumValueIndex, "그림자 기본 끔");
        Assert.IsNull(vfx.GetComponentInChildren<Light>(true), "라이트는 프리팹에 두지 않고 첫 재생 때 한 번 만든다");

        var renderers = vfx.GetComponentsInChildren<ParticleSystemRenderer>(true);
        Assert.GreaterOrEqual(renderers.Length, 2, "기둥 + 모여드는 입자");
        var streams = new List<ParticleSystemVertexStream>();
        foreach (ParticleSystemRenderer r in renderers)
        {
            Material mat = r.sharedMaterial;
            Assert.IsNotNull(mat, r.name);
            Assert.AreEqual(ShaderName, mat.shader.name, r.name);
            Assert.Greater(mat.GetColor("_Color").maxColorComponent, 1f, $"{mat.name} — HDR(블룸)");
            Assert.IsNull(mat.GetTexture(InterruptLightGatherRules.OverrideTextureProperty), $"{mat.name} — 처음엔 절차 생성");
            Assert.IsFalse(mat.IsKeywordEnabled(InterruptLightGatherRules.OverrideTextureKeyword), mat.name);

            r.GetActiveVertexStreams(streams);
            CollectionAssert.Contains(streams, ParticleSystemVertexStream.AgePercent, $"{r.name} — 셰이더가 나이로 움직인다");
            CollectionAssert.Contains(streams, ParticleSystemVertexStream.StableRandomX, r.name);

            ParticleSystem ps = r.GetComponent<ParticleSystem>();
            Assert.IsFalse(ps.main.playOnAwake, r.name);
            Assert.AreEqual(ParticleSystemSimulationSpace.Local, ps.main.simulationSpace, $"{r.name} — 움직이는 소켓 쪽으로 모인다");
        }
    }
}
