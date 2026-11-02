using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 제압 간파 섬광(할 일 179) — 사양 표 기본값, 실제 시간 → 프레임(24) → 컷·2프레임 홀드 계단, 컷 전환·종료, 섬광 줄이기(반전 끔·대비),
/// SO 값 Play 중 반영, 디렉터 프로필 선택(A6 칸 켬/끔·섬광 줄이기와 독립),서버 표식(같은 프레임만), 슬로우 Start 이벤트 플래그 직렬화, 실루엣 비트 7 태깅·원복, 에셋·프리팹 연결.
/// </summary>
public sealed class SuppressFlashTests
{
    private const float Frame = 1f / 24f;
    private const string SequencePath = "Assets/5.VFX/Interrupt/SuppressFlash/SuppressFlashSequence.asset";
    private const string MaterialPath = "Assets/5.VFX/Interrupt/SuppressFlash/M_SuppressFlash.mat";
    private const string ShaderName = "Hidden/Rendering/SuppressFlash";
    private const string NetworkManagerPath = "Assets/2.Prefabs/Network/NetworkManager.prefab";

    private sealed class FakeOutput : ISuppressFlashOutput
    {
        public readonly List<SuppressFlashFrame> Shown = new List<SuppressFlashFrame>();
        public readonly List<GameObject> Subjects = new List<GameObject>();
        public int Hides;

        public void SetSubjects(IReadOnlyList<GameObject> subjects)
        {
            Subjects.Clear();
            if (subjects != null)
                Subjects.AddRange(subjects);
        }

        public void Show(in SuppressFlashFrame frame, Vector3 worldCenter, SuppressFlashSequence sequence) => Shown.Add(frame);
        public void Hide() => Hides++;
    }

    private readonly List<Object> _objects = new List<Object>();
    private SuppressFlashSequence _sequence;

    [SetUp]
    public void SetUp()
    {
        _sequence = ScriptableObject.CreateInstance<SuppressFlashSequence>();
        _objects.Add(_sequence);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object o in _objects)
            if (o != null)
                Object.DestroyImmediate(o);
        _objects.Clear();
    }

    // 프레임 가운데 시각 — 경계 오차와 무관하게 그 프레임.
    private static float At(int frame) => (frame + 0.5f) * Frame;

    // ---- 사양 표 ----

    [Test]
    public void 기본값은_사양_표_7컷_15프레임_24fps_2홀드()
    {
        Assert.AreEqual(24f, _sequence.frameRate);
        Assert.AreEqual(2, _sequence.holdFrames);
        Assert.AreEqual(7, _sequence.cuts.Count);
        Assert.AreEqual(15, _sequence.TotalFrames);

        SuppressFlashTone[] tones =
        {
            SuppressFlashTone.Negative, SuppressFlashTone.Positive, SuppressFlashTone.Negative, SuppressFlashTone.Positive,
            SuppressFlashTone.Negative, SuppressFlashTone.Positive, SuppressFlashTone.Return,
        };
        SuppressFlashCross[] crosses =
        {
            SuppressFlashCross.Single, SuppressFlashCross.Grid, SuppressFlashCross.Single, SuppressFlashCross.Single,
            SuppressFlashCross.Single, SuppressFlashCross.None, SuppressFlashCross.None,
        };
        float[] stars = { 1f, 0.45f, 1.25f, 0.7f, 1.9f, 0f, 0f };
        float[] lines = { 0.5f, 0.2f, 1f, 0.6f, 1f, 0.15f, 0f };
        float[] aberrations = { 0.2f, 0.5f, 0.3f, 1f, 0.4f, 0.6f, 0.8f };
        bool[] hatching = { false, true, false, true, false, true, false };

        for (int i = 0; i < 7; i++)
        {
            SuppressFlashCut cut = _sequence.cuts[i];
            Assert.AreEqual(i == 6 ? 3 : 2, cut.frames, $"컷 {i + 1} 길이");
            Assert.AreEqual(tones[i], cut.tone, $"컷 {i + 1} 명암");
            Assert.AreEqual(crosses[i], cut.cross, $"컷 {i + 1} 십자");
            Assert.AreEqual(stars[i], cut.starSize, 1e-5f, $"컷 {i + 1} 별");
            Assert.AreEqual(lines[i], cut.speedLines, 1e-5f, $"컷 {i + 1} 집중선");
            Assert.AreEqual(aberrations[i], cut.aberration, 1e-5f, $"컷 {i + 1} 색수차");
            Assert.AreEqual(hatching[i], cut.hatching, $"컷 {i + 1} 빗금");
            Assert.AreEqual((Color)new Color32(0x1C, 0x19, 0x30, 0xFF), cut.dark);
            Assert.AreEqual((Color)new Color32(0x7D, 0x77, 0xA0, 0xFF), cut.mid);
            Assert.AreEqual((Color)new Color32(0xEC, 0xE9, 0xF6, 0xFF), cut.light);
        }

        Assert.AreEqual(0f, _sequence.cuts[6].aberrationEnd, "7컷 색수차 0.8→0");
        Assert.AreEqual(0f, _sequence.cuts[6].letterboxEnd, "7컷 레터박스 빠짐");
    }

    // ---- 시간표 ----

    [Test]
    public void 경과_시간은_24fps_프레임으로_내림()
    {
        Assert.AreEqual(0, SuppressFlashRules.ElapsedFrames(-1f, 24f));
        Assert.AreEqual(0, SuppressFlashRules.ElapsedFrames(0f, 24f));
        Assert.AreEqual(0, SuppressFlashRules.ElapsedFrames(Frame * 0.99f, 24f));
        Assert.AreEqual(1, SuppressFlashRules.ElapsedFrames(Frame, 24f), "1/24 초 경계는 다음 프레임");
        Assert.AreEqual(12, SuppressFlashRules.ElapsedFrames(0.5f, 24f));
    }

    [Test]
    public void 홀드_계단은_2프레임마다()
    {
        Assert.AreEqual(0, SuppressFlashRules.SteppedFrame(0, 2));
        Assert.AreEqual(0, SuppressFlashRules.SteppedFrame(1, 2));
        Assert.AreEqual(2, SuppressFlashRules.SteppedFrame(2, 2));
        Assert.AreEqual(2, SuppressFlashRules.SteppedFrame(3, 2));
        Assert.AreEqual(3, SuppressFlashRules.SteppedFrame(3, 0), "홀드 0 이하는 1");
    }

    [Test]
    public void 컷_전환_2프레임마다_7컷은_3프레임_뒤_끝()
    {
        int[] expected = { 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 6 };
        for (int f = 0; f < expected.Length; f++)
        {
            SuppressFlashFrame frame = SuppressFlashRules.Evaluate(_sequence, At(f), false);
            Assert.IsTrue(frame.Active, $"프레임 {f} 재생 중");
            Assert.AreEqual(expected[f], frame.CutIndex, $"프레임 {f} 컷");
        }

        Assert.IsFalse(SuppressFlashRules.Evaluate(_sequence, At(15), false).Active, "15 프레임(0.625초)에 끝");
        Assert.IsFalse(SuppressFlashRules.Evaluate(null, 0f, false).Active, "컷 데이터 없음");
    }

    [Test]
    public void 포지티브_컷만_뒤집고_대비는_그대로()
    {
        Assert.IsFalse(SuppressFlashRules.Evaluate(_sequence, At(0), false).Invert);
        Assert.IsTrue(SuppressFlashRules.Evaluate(_sequence, At(2), false).Invert);
        Assert.IsFalse(SuppressFlashRules.Evaluate(_sequence, At(12), false).Invert, "복귀 컷은 뒤집지 않는다");
        Assert.AreEqual(1f, SuppressFlashRules.Evaluate(_sequence, At(2), false).Contrast);
        Assert.AreEqual(SuppressFlashCross.Grid, SuppressFlashRules.Evaluate(_sequence, At(2), false).Cross);
        Assert.IsTrue(SuppressFlashRules.Evaluate(_sequence, At(2), false).Hatching);
    }

    [Test]
    public void 섬광_줄이기는_반전_끔_대비_낮춤()
    {
        _sequence.reducedContrast = 0.4f;
        for (int f = 0; f < 15; f++)
        {
            SuppressFlashFrame frame = SuppressFlashRules.Evaluate(_sequence, At(f), true);
            Assert.IsFalse(frame.Invert, $"프레임 {f} 반전 없음");
            Assert.AreEqual(0.4f, frame.Contrast, 1e-5f);
        }

        Assert.AreEqual(SuppressFlashTone.Positive, SuppressFlashRules.Evaluate(_sequence, At(2), true).Tone, "컷 데이터 명암은 그대로 보고");
    }

    [Test]
    public void 복귀_컷은_홀드_계단으로_색수차_감소_원래_색_섞기_레터박스_빠짐()
    {
        SuppressFlashFrame first = SuppressFlashRules.Evaluate(_sequence, At(12), false);
        SuppressFlashFrame second = SuppressFlashRules.Evaluate(_sequence, At(13), false);
        SuppressFlashFrame third = SuppressFlashRules.Evaluate(_sequence, At(14), false);

        Assert.AreEqual(0.8f, first.Aberration, 1e-5f);
        Assert.AreEqual(0f, first.ReturnBlend, 1e-5f);
        Assert.AreEqual(1f, first.Letterbox, 1e-5f);
        Assert.AreEqual(first.Aberration, second.Aberration, 1e-5f, "2프레임 홀드 — 같은 그림");
        Assert.AreEqual(0.8f / 3f, third.Aberration, 1e-4f, "3번째 프레임 = 진행 2/3");
        Assert.AreEqual(2f / 3f, third.ReturnBlend, 1e-4f);
        Assert.AreEqual(1f / 3f, third.Letterbox, 1e-4f);
        Assert.AreEqual(0f, SuppressFlashRules.Evaluate(_sequence, At(4), false).ReturnBlend, "복귀 외 컷은 0");
    }

    // ---- 진행기 ----

    [Test]
    public void 진행기는_실제_시간으로_컷을_넘기고_끝나면_걷는다()
    {
        var output = new FakeOutput();
        var player = new SuppressFlashPlayer(() => _sequence, () => false, output);
        var owner = new GameObject("owner");
        var target = new GameObject("target");
        _objects.Add(owner);
        _objects.Add(target);

        Assert.IsTrue(player.Begin(100f, Vector3.zero, new[] { owner, target }));
        CollectionAssert.AreEqual(new[] { owner, target }, output.Subjects, "실루엣 대상 목록");
        Assert.AreEqual(0, player.Current.CutIndex);

        player.Tick(100f + At(4));
        Assert.AreEqual(2, player.Current.CutIndex);

        player.Tick(100f + At(15));
        Assert.IsFalse(player.IsPlaying);
        Assert.IsFalse(player.Current.Active);
        Assert.AreEqual(1, output.Hides);
    }

    [Test]
    public void 컷_데이터를_재생_중에_바꾸면_다음_프레임부터_반영()
    {
        var output = new FakeOutput();
        var player = new SuppressFlashPlayer(() => _sequence, () => false, output);
        player.Begin(0f, Vector3.zero, null);

        _sequence.cuts[1].starSize = 0.9f;
        player.Tick(At(2));
        Assert.AreEqual(0.9f, player.Current.StarSize, 1e-5f);
    }

    [Test]
    public void 섬광_줄이기는_재생_중에도_프레임마다_읽는다()
    {
        bool reduce = false;
        var player = new SuppressFlashPlayer(() => _sequence, () => reduce, new FakeOutput());
        player.Begin(0f, Vector3.zero, null);
        player.Tick(At(2));
        Assert.IsTrue(player.Current.Invert);

        reduce = true;
        player.Tick(At(3));
        Assert.IsFalse(player.Current.Invert);
    }

    [Test]
    public void 컷_데이터가_없으면_시작하지_않고_멈춤은_한_번만_걷는다()
    {
        var output = new FakeOutput();
        var empty = new SuppressFlashPlayer(() => null, () => false, output);
        Assert.IsFalse(empty.Begin(0f, Vector3.zero, null));
        empty.Stop();
        Assert.AreEqual(0, output.Hides, "돌지 않았으면 걷을 것 없음");

        var player = new SuppressFlashPlayer(() => _sequence, () => false, output);
        player.Begin(0f, Vector3.zero, null);
        player.Stop();
        player.Stop();
        Assert.AreEqual(1, output.Hides);
    }

    [Test]
    public void 새_성공은_처음부터_다시()
    {
        var player = new SuppressFlashPlayer(() => _sequence, () => false, new FakeOutput());
        player.Begin(0f, Vector3.zero, null);
        player.Tick(At(9));
        Assert.AreEqual(4, player.Current.CutIndex);

        player.Begin(At(9), Vector3.zero, null);
        Assert.AreEqual(0, player.Current.CutIndex);
    }

    // ---- 디렉터 프로필·서버 표식 ----

    [TestCase(SlowMotionPurpose.Success, true, true, true, ExpectedResult = true)]
    [TestCase(SlowMotionPurpose.Success, false, true, true, ExpectedResult = false)]
    [TestCase(SlowMotionPurpose.Success, true, false, true, ExpectedResult = false)]
    [TestCase(SlowMotionPurpose.Success, true, true, false, ExpectedResult = false)]
    [TestCase(SlowMotionPurpose.Attempt, true, true, true, ExpectedResult = false)]
    public bool 제압_간파_프로필_선택(SlowMotionPurpose purpose, bool suppress, bool enabled, bool hasSequence) =>
        SuppressFlashRules.ShouldPlay(purpose, suppress, enabled, hasSequence);

    // ---- A6 제압 간파 섬광 시퀀스 칸 ----

    private InterruptProductionDirector CreateDirectorWithSequence()
    {
        var go = new GameObject("Director");
        _objects.Add(go);
        var director = go.AddComponent<InterruptProductionDirector>();
        var so = new SerializedObject(director);
        so.FindProperty("suppressFlashSequence").objectReferenceValue = _sequence;
        so.ApplyModifiedPropertiesWithoutUndo();
        return director;
    }

    [Test]
    public void A6_켬이면_제압_간파는_시퀀스_끔이면_일반_섬광()
    {
        InterruptProductionDirector director = CreateDirectorWithSequence();
        Assert.IsTrue(director.SuppressFlashEnabled, "코드 기본값 켬");
        Assert.IsTrue(director.PlaysSuppressFlash(SlowMotionPurpose.Success, true), "켬 = 시퀀스");
        Assert.IsFalse(director.PlaysSuppressFlash(SlowMotionPurpose.Success, false), "일반 간파는 늘 ②~⑥");

        director.SuppressFlashEnabled = false;
        Assert.IsFalse(director.PlaysSuppressFlash(SlowMotionPurpose.Success, true), "끔 = ②~⑥ 경로");
        // ②~⑥ 경로의 오너 화면 섬광 조건은 제압 표식을 보지 않는다 — 일반 간파와 같은 흐름(강 시도 암전 켜짐이면 오버레이).
        Assert.IsTrue(InterruptHitFlashRules.ShouldPlayOwnerOverlay(SlowMotionPurpose.Success, true, true));
        Assert.IsFalse(InterruptHitFlashRules.ShouldPlayOwnerOverlay(SlowMotionPurpose.Success, false, true), "팀원 화면은 오버레이 없음");

        director.SuppressFlashEnabled = true;
        Assert.IsTrue(director.PlaysSuppressFlash(SlowMotionPurpose.Success, true), "Play 중 다시 켜면 다음 성공부터 시퀀스");
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void A6_칸과_섬광_줄이기는_서로_독립(bool suppressFlash, bool reduceFlash)
    {
        InterruptProductionDirector director = CreateDirectorWithSequence();
        var so = new SerializedObject(director);
        so.FindProperty("reduceFlash").boolValue = reduceFlash;
        so.ApplyModifiedPropertiesWithoutUndo();

        director.SuppressFlashEnabled = suppressFlash;
        Assert.AreEqual(suppressFlash, director.PlaysSuppressFlash(SlowMotionPurpose.Success, true), "섬광 줄이기는 시퀀스 선택을 바꾸지 않는다");

        so.Update();
        Assert.AreEqual(reduceFlash, so.FindProperty("reduceFlash").boolValue, "A6 칸은 섬광 줄이기 칸을 바꾸지 않는다");
        Assert.AreEqual(suppressFlash, so.FindProperty("suppressFlash").boolValue);
    }

    [Test]
    public void A6_칸_켬이어도_컷_데이터가_없으면_일반_섬광()
    {
        var go = new GameObject("Director");
        _objects.Add(go);
        var director = go.AddComponent<InterruptProductionDirector>();
        Assert.IsTrue(director.SuppressFlashEnabled);
        Assert.IsFalse(director.PlaysSuppressFlash(SlowMotionPurpose.Success, true));
    }

    [Test]
    public void 서버_표식은_같은_프레임에_한_번만()
    {
        var mark = new SuppressInterruptMark();
        Assert.IsFalse(mark.Consume(10));

        mark.Mark(10);
        Assert.IsTrue(mark.Consume(10));
        Assert.IsFalse(mark.Consume(10), "읽으면 비운다");

        mark.Mark(10);
        Assert.IsFalse(mark.Consume(11), "다른 프레임에 남은 표식은 버린다");
        Assert.IsFalse(mark.Consume(10));
    }

    [Test]
    public void 성공_사건_사본은_제압_간파_칸만_바꾼다()
    {
        var e = InterruptProductionEvent.Success(3, null, null, true, new Vector3(1, 2, 3), new Vector3(4, 5, 6));
        InterruptProductionEvent marked = e.WithSuppressInterrupt(true);

        Assert.IsFalse(e.SuppressInterrupt);
        Assert.IsTrue(marked.SuppressInterrupt);
        Assert.AreEqual(InterruptProductionPhase.Success, marked.Phase);
        Assert.AreEqual(3ul, marked.CasterClientId);
        Assert.IsTrue(marked.HasTarget);
        Assert.AreEqual(new Vector3(1, 2, 3), marked.TargetPosition);
        Assert.AreEqual(new Vector3(4, 5, 6), marked.CasterPosition);
        Assert.IsTrue(InterruptProductionEvent.LocalSuccess(1, 2, Vector3.zero, true).SuppressInterrupt);
    }

    // ---- 전송(새 RPC 없음 — 슬로우 Start 이벤트 1바이트) ----

    [TestCase(false)]
    [TestCase(true)]
    public void 슬로우_Start_이벤트는_제압_간파_칸을_싣고_세션이_기억한다(bool suppress)
    {
        var profile = new SlowMotionProfile { scale = 0.25f, holdDuration = 1f, enterDuration = 0.05f, exitDuration = 0.4f };
        SlowMotionEvent e = GlobalTimeScale.CreateStartEvent(5, 10.0, 2, profile, SlowMotionPurpose.Success, Vector3.one, true, suppress);

        SlowMotionEvent read;
        using (var writer = new FastBufferWriter(SlowMotionEvent.SerializedSize, Allocator.Temp))
        {
            e.Write(writer);
            Assert.AreEqual(SlowMotionEvent.SerializedSize, writer.Length, "직렬화 크기 상수 = 실제 쓴 바이트");
            using (var reader = new FastBufferReader(writer, Allocator.Temp))
                read = SlowMotionEvent.Read(reader);
        }

        Assert.AreEqual(suppress, read.SuppressInterrupt);
        Assert.AreEqual(SlowMotionPurpose.Success, read.Purpose);
        Assert.AreEqual(Vector3.one, read.TargetPosition);

        var session = new SlowMotionSession();
        Assert.AreEqual(SlowMotionApplyResult.Started, session.Apply(read));
        Assert.AreEqual(suppress, session.CurrentSuppressInterrupt);
        session.Clear();
        Assert.IsFalse(session.CurrentSuppressInterrupt);
    }

    // ---- 렌더 출력 ----

    [Test]
    public void 셰이더_파라미터는_컷과_시퀀스를_묶는다()
    {
        SuppressFlashFrame frame = SuppressFlashRules.Evaluate(_sequence, At(2), false);
        SuppressFlashParams p = SuppressFlashScreenOutput.Build(frame, _sequence, new Vector2(0.3f, 0.6f), 1.5f, 9);

        Assert.AreEqual(new Vector4(0.3f, 0.6f, 1.5f, 9f), p.Center);
        Assert.AreEqual(1f, p.Tone.x, "포지티브 = 뒤집기");
        Assert.AreEqual(1f, p.Tone.w, "빗금");
        Assert.AreEqual((float)SuppressFlashCross.Grid, p.Cross.x);
        Assert.AreEqual(_sequence.starRadius * 0.45f, p.Fx.x, 1e-5f);
        Assert.AreEqual(_sequence.aberrationScale * 0.5f, p.Fx.z, 1e-5f);
        Assert.AreEqual(_sequence.letterboxHeight, p.Fx.w, 1e-5f);
        Assert.AreEqual(frame.Dark.linear, p.Dark, "선형 색");
    }

    [Test]
    public void 실루엣_태거는_비트_7만_붙이고_원복한다()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _objects.Add(go);
        var renderer = go.GetComponent<Renderer>();
        renderer.renderingLayerMask = 1u | InterruptOwnerMaskLayers.Mask;

        var tagger = new InterruptOwnerMaskTagger(SuppressFlashMaskLayers.Mask);
        Assert.AreEqual(1, tagger.Tag(go));
        Assert.AreEqual(1u | InterruptOwnerMaskLayers.Mask | SuppressFlashMaskLayers.Mask, renderer.renderingLayerMask);

        // 암전 쪽(비트 4)이 먼저 원복해도 비트 7 은 남는다.
        renderer.renderingLayerMask &= ~InterruptOwnerMaskLayers.Mask;
        Assert.IsTrue((renderer.renderingLayerMask & SuppressFlashMaskLayers.Mask) != 0);

        tagger.RestoreAll();
        Assert.AreEqual(1u, renderer.renderingLayerMask);
        Assert.AreEqual(0, tagger.Count);
    }

    [Test]
    public void 비트_7은_다른_렌더링_레이어와_겹치지_않는다()
    {
        Assert.AreNotEqual(InterruptOwnerMaskLayers.Mask, SuppressFlashMaskLayers.Mask);
        Assert.AreNotEqual(DecalReceivers.Mask, SuppressFlashMaskLayers.Mask);
        Assert.AreNotEqual(PlayerSilhouetteLayers.LocalMask, SuppressFlashMaskLayers.Mask);
        Assert.AreNotEqual(PlayerSilhouetteLayers.RemoteMask, SuppressFlashMaskLayers.Mask);
        Assert.AreNotEqual(1u << 8, SuppressFlashMaskLayers.Mask, "Ground Layer");
        Assert.AreEqual(0u, BossVictoryRender.AllMask & SuppressFlashMaskLayers.Mask, "보스 처치 연출 비트 4·5·6");
    }

    // ---- 레퍼런스 B(179 후속 3) ----

    private const float FrameB = 1f / 60f;
    private const string SequenceBPath = "Assets/5.VFX/Interrupt/SuppressFlash/SuppressFlashSequence_B.asset";

    private static float AtB(int frame) => (frame + 0.5f) * FrameB;

    private SuppressFlashSequence CreateReference()
    {
        var b = ScriptableObject.CreateInstance<SuppressFlashSequence>();
        _objects.Add(b);
        b.ResetToReference();
        return b;
    }

    [Test]
    public void B는_60fps_홀드1_5컷_22프레임_컷마다_길이가_다르다()
    {
        SuppressFlashSequence b = CreateReference();
        Assert.AreEqual(60f, b.frameRate);
        Assert.AreEqual(1, b.holdFrames);
        Assert.AreEqual(22, b.TotalFrames);

        int[] lengths = { 1, 5, 6, 2, 8 };
        SuppressFlashTone[] tones =
        {
            SuppressFlashTone.Return, SuppressFlashTone.Positive, SuppressFlashTone.Bloom, SuppressFlashTone.Negative, SuppressFlashTone.Return,
        };
        Assert.AreEqual(5, b.cuts.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.AreEqual(lengths[i], b.cuts[i].frames, $"B 컷 {i + 1} 길이");
            Assert.AreEqual(tones[i], b.cuts[i].tone, $"B 컷 {i + 1} 명암");
            Assert.AreEqual(0f, b.cuts[i].letterbox, "B 는 레터박스 없음");
        }

        Assert.AreEqual(Color.black, b.cuts[1].dark, "③ 흑백 두 색");
        Assert.AreEqual(Color.white, b.cuts[1].mid);
        Assert.AreEqual(Color.white, b.cuts[1].light);
    }

    [Test]
    public void B_컷_전환은_60fps_프레임마다()
    {
        SuppressFlashSequence b = CreateReference();
        int[] expected = { 0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4 };
        for (int f = 0; f < expected.Length; f++)
        {
            SuppressFlashFrame frame = SuppressFlashRules.Evaluate(b, AtB(f), false);
            Assert.IsTrue(frame.Active, $"B 프레임 {f}");
            Assert.AreEqual(expected[f], frame.CutIndex, $"B 프레임 {f} 컷");
        }

        Assert.IsFalse(SuppressFlashRules.Evaluate(b, AtB(22), false).Active, "22 프레임(0.367초)에 끝");
    }

    [Test]
    public void B_컷별_모양_궤적_실루엣_블룸_반전_복귀()
    {
        SuppressFlashSequence b = CreateReference();

        SuppressFlashFrame trail = SuppressFlashRules.Evaluate(b, AtB(0), false);
        Assert.AreEqual(1f, trail.ReturnBlend, 1e-5f, "② 원래 색에서 시작");
        Assert.AreEqual(1f, trail.AberrationUniform, "② 고른 색수차 = 빨강·파랑 궤적");
        Assert.Greater(trail.DirectionalBlur, 0f);
        Assert.Greater(trail.Shake, 0f, "② 흔들림");

        SuppressFlashFrame silhouette = SuppressFlashRules.Evaluate(b, AtB(3), false);
        Assert.IsTrue(silhouette.Invert, "③ 흰 바탕·검은 실루엣 = 뒤집기");
        Assert.AreEqual(1f, silhouette.DirectionalBlur, "③ 강한 방향 블러");
        Assert.AreEqual(0f, silhouette.Bloom);

        SuppressFlashFrame bloom = SuppressFlashRules.Evaluate(b, AtB(8), false);
        Assert.AreEqual(SuppressFlashTone.Bloom, bloom.Tone);
        Assert.IsFalse(bloom.Invert);
        Assert.Greater(bloom.Bloom, 0f, "④ 컬러 블룸");
        Assert.Greater(bloom.StarSize, 0f, "④ 스파이크 별");

        SuppressFlashFrame negative = SuppressFlashRules.Evaluate(b, AtB(12), false);
        Assert.IsFalse(negative.Invert, "⑤ 검은 바탕");
        Assert.AreEqual(0.25f, negative.SilhouetteDim, 1e-5f, "⑤ 밝은 회색 실루엣");

        SuppressFlashFrame first = SuppressFlashRules.Evaluate(b, AtB(14), false);
        SuppressFlashFrame last = SuppressFlashRules.Evaluate(b, AtB(21), false);
        Assert.AreEqual(1f, first.ReturnBlend, 1e-5f, "⑥ 첫 프레임부터 원래 색");
        Assert.AreEqual(1f, first.Aberration, 1e-5f, "⑥ 첫 프레임 강한 색수차");
        Assert.AreEqual(1f, first.AberrationUniform);
        Assert.Less(last.Aberration, 0.2f, "⑥ 가라앉음");
    }

    [Test]
    public void B_섬광_줄이기는_반전_끔_블룸_흔들림_줄임()
    {
        SuppressFlashSequence b = CreateReference();
        b.reducedContrast = 0.4f;
        Assert.IsFalse(SuppressFlashRules.Evaluate(b, AtB(3), true).Invert);
        Assert.AreEqual(b.cuts[2].bloom * 0.4f, SuppressFlashRules.Evaluate(b, AtB(8), true).Bloom, 1e-5f);
        Assert.AreEqual(b.cuts[0].shake * 0.4f, SuppressFlashRules.Evaluate(b, AtB(0), true).Shake, 1e-5f);
    }

    [Test]
    public void A_컷은_새_칸이_0이라_셰이더_B_경로를_건너뛴다()
    {
        foreach (SuppressFlashCut cut in _sequence.cuts)
        {
            Assert.AreEqual(0f, cut.directionalBlur);
            Assert.AreEqual(0f, cut.shake);
            Assert.AreEqual(0f, cut.silhouetteDim);
            Assert.AreEqual(0f, cut.returnStart);
            Assert.AreEqual(0f, cut.aberrationUniform);
            Assert.AreEqual(0f, cut.bloom);
        }

        for (int f = 0; f < 15; f++)
        {
            SuppressFlashFrame frame = SuppressFlashRules.Evaluate(_sequence, At(f), false);
            SuppressFlashParams p = SuppressFlashScreenOutput.Build(frame, _sequence, new Vector2(0.5f, 0.5f), 1.7f, f, new Vector2(0f, 1f));
            Assert.AreEqual(Vector4.zero, p.Extra, $"A 프레임 {f} 블룸·실루엣 밝기·고른 색수차 0");
            Assert.AreEqual(0f, p.Direction.z, "방향 블러 길이 0");
            Assert.AreEqual(0f, p.Shake.x);
            Assert.AreEqual(0f, p.Shake.y);
            Assert.AreEqual(0f, p.Shake.z, "블룸 0");
        }
    }

    [Test]
    public void B_셰이더_파라미터_블룸_방향_흔들림()
    {
        SuppressFlashSequence b = CreateReference();
        SuppressFlashParams bloom = SuppressFlashScreenOutput.Build(SuppressFlashRules.Evaluate(b, AtB(8), false), b, new Vector2(0.5f, 0.5f), 2f, 3);
        Assert.AreEqual(1f, bloom.Extra.x, "블룸 명암");
        Assert.AreEqual(b.cuts[2].bloom, bloom.Shake.z, 1e-5f);
        Assert.AreEqual(b.bloomRadius, bloom.Shake.w, 1e-5f);
        Assert.AreEqual(b.bloomColor.linear, bloom.Bloom);

        SuppressFlashParams silhouette = SuppressFlashScreenOutput.Build(SuppressFlashRules.Evaluate(b, AtB(3), false), b,
            new Vector2(0.5f, 0.5f), 2f, 3, new Vector2(0f, -1f));
        Assert.AreEqual(new Vector4(0f, -1f, b.directionalBlurLength, 0f), silhouette.Direction);
        Assert.AreEqual(1f, SuppressFlashScreenOutput.Build(SuppressFlashRules.Evaluate(b, AtB(3), false), b,
            new Vector2(0.5f, 0.5f), 2f, 3).Direction.x, "방향이 없으면 오른쪽");

        SuppressFlashParams trail = SuppressFlashScreenOutput.Build(SuppressFlashRules.Evaluate(b, AtB(0), false), b, new Vector2(0.5f, 0.5f), 2f, 3);
        float shakeHeight = new Vector2(trail.Shake.x * 2f, trail.Shake.y).magnitude;
        Assert.AreEqual(b.shakeScale * b.cuts[0].shake, shakeHeight, 1e-5f, "흔들림 폭(가로는 화면비로 나눔)");
    }

    [Test]
    public void 방향_블러_방향은_시전자에서_적중점_화면비_보정()
    {
        Vector2 d = SuppressFlashScreenOutput.AttackDirection(new Vector2(0.4f, 0.5f), new Vector2(0.5f, 0.5f), 2f);
        Assert.AreEqual(1f, d.x, 1e-5f);
        Vector2 diagonal = SuppressFlashScreenOutput.AttackDirection(new Vector2(0.5f, 0.5f), new Vector2(0.6f, 0.7f), 2f);
        Assert.AreEqual(new Vector2(0.2f, 0.2f).normalized.x, diagonal.x, 1e-5f, "가로 × 화면비");
        Assert.AreEqual(Vector2.right, SuppressFlashScreenOutput.AttackDirection(Vector2.one * 0.5f, Vector2.one * 0.5f, 1.7f), "겹치면 오른쪽");
    }

    [Test]
    public void 진행기는_시작_순간_고른_시퀀스를_끝까지_쓴다()
    {
        SuppressFlashSequence b = CreateReference();
        SuppressFlashSequence selected = _sequence;
        var player = new SuppressFlashPlayer(() => selected, () => false, new FakeOutput());

        player.Begin(0f, Vector3.zero, null);
        Assert.AreSame(_sequence, player.Playing);
        selected = b; // 재생 중 A → B 전환
        player.Tick(At(4));
        Assert.AreSame(_sequence, player.Playing, "도는 시퀀스는 끝까지 A");
        Assert.AreEqual(2, player.Current.CutIndex, "A 시간표(24fps·2프레임 컷)");

        player.Begin(1f, Vector3.zero, null);
        Assert.AreSame(b, player.Playing, "다음 성공부터 B");
        player.Tick(1f + AtB(3));
        Assert.AreEqual(1, player.Current.CutIndex, "B 시간표(60fps)");
    }

    [Test]
    public void 디렉터_선택_A_B_고른_쪽이_비면_일반_섬광()
    {
        InterruptProductionDirector director = CreateDirectorWithSequence();
        Assert.AreEqual(SuppressFlashVariant.A, director.SuppressFlashVariant, "기본 = A");
        Assert.AreSame(_sequence, director.SelectedSuppressFlashSequence);

        director.SuppressFlashVariant = SuppressFlashVariant.B;
        Assert.IsNull(director.SelectedSuppressFlashSequence, "B 가 비면 A 로 대신하지 않는다");
        Assert.IsFalse(director.PlaysSuppressFlash(SlowMotionPurpose.Success, true), "고른 쪽이 비면 일반 섬광");

        SuppressFlashSequence b = CreateReference();
        var so = new SerializedObject(director);
        so.FindProperty("suppressFlashSequenceB").objectReferenceValue = b;
        so.ApplyModifiedPropertiesWithoutUndo();
        Assert.AreSame(b, director.SelectedSuppressFlashSequence);
        Assert.IsTrue(director.PlaysSuppressFlash(SlowMotionPurpose.Success, true));
        Assert.AreSame(b, director.SuppressFlash.Begin(0f, Vector3.zero, null) ? director.SuppressFlash.Playing : null,
            "진행기는 디렉터 선택을 읽는다");
        director.SuppressFlash.Stop();

        Assert.AreSame(_sequence, SuppressFlashRules.Select(SuppressFlashVariant.A, _sequence, b));
        Assert.AreSame(b, SuppressFlashRules.Select(SuppressFlashVariant.B, _sequence, b));
    }

    [Test]
    public void B_에셋은_레퍼런스_값()
    {
        var asset = AssetDatabase.LoadAssetAtPath<SuppressFlashSequence>(SequenceBPath);
        Assert.IsNotNull(asset, SequenceBPath);
        Assert.AreEqual(60f, asset.frameRate);
        Assert.AreEqual(1, asset.holdFrames);
        Assert.AreEqual(5, asset.cuts.Count);
        Assert.AreEqual(22, asset.TotalFrames);
        Assert.AreEqual(SuppressFlashTone.Bloom, asset.cuts[2].tone);
    }

    // ---- 에셋·프리팹 연결 ----

    [Test]
    public void 기본_에셋은_사양_값_재질은_섬광_셰이더()
    {
        var asset = AssetDatabase.LoadAssetAtPath<SuppressFlashSequence>(SequencePath);
        Assert.IsNotNull(asset, SequencePath);
        Assert.AreEqual(7, asset.cuts.Count);
        Assert.AreEqual(15, asset.TotalFrames);

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Assert.IsNotNull(material, MaterialPath);
        Assert.AreEqual(ShaderName, material.shader.name);
        Assert.AreEqual(2, material.shader.passCount, "합성 + 실루엣 마스크");
    }

    [Test]
    public void 디렉터_프리팹은_제압_간파_칸_켬_섬광_줄이기_끔_에셋_연결()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPath);
        var director = prefab.GetComponent<InterruptProductionDirector>();
        Assert.IsNotNull(director);
        Assert.IsTrue(director.SuppressFlashEnabled);
        Assert.AreEqual(AssetDatabase.LoadAssetAtPath<SuppressFlashSequence>(SequencePath), director.SuppressFlashSequence);
        Assert.AreEqual(AssetDatabase.LoadAssetAtPath<Material>(MaterialPath), director.SuppressFlashMaterial);
        Assert.AreEqual(AssetDatabase.LoadAssetAtPath<SuppressFlashSequence>(SequenceBPath), director.SuppressFlashSequenceB, "B 연결(179 후속 3)");
        Assert.AreEqual(SuppressFlashVariant.A, director.SuppressFlashVariant, "선택 기본 = A");

        var serialized = new SerializedObject(director);
        Assert.IsFalse(serialized.FindProperty("reduceFlash").boolValue, "섬광 줄이기 기본 끔");
    }
}
