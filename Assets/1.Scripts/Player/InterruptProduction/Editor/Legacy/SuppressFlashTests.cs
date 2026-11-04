using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// [Legacy — 11-04 할 일 211] 제압 간파 섬광 시퀀스(할 일 179) — 런타임 연결은 끊겼고 코드·에셋은 Legacy 폴더에 보관한다. 슬로우 모션 테스트 메뉴에서 뺐다(Test Runner 에서 직접 돌릴 수 있다).
/// 사양 표 기본값, 실제 시간 → 프레임(24) → 컷·2프레임 홀드 계단, 컷 전환·종료, 섬광 줄이기(반전 끔·대비), SO 값 Play 중 반영, 프로필 선택 규칙,
/// 실루엣 비트 7 태깅·원복, A7 범위 규칙, 제압 암순응 컷(192 Q7), 에셋. 디렉터 연결(A6·A7·프리팹) 테스트는 지웠다 — 디렉터에 칸이 없다.
/// </summary>
public sealed class SuppressFlashTests
{
    private const float Frame = 1f / 24f;
    private const string SequencePath = "Assets/5.VFX/Interrupt/Legacy/SuppressFlash/SuppressFlashSequence.asset";
    private const string MaterialPath = "Assets/5.VFX/Interrupt/Legacy/SuppressFlash/M_SuppressFlash.mat";
    private const string ShaderName = "Hidden/Rendering/SuppressFlash";

    private sealed class FakeOutput : ISuppressFlashOutput
    {
        public readonly List<SuppressFlashFrame> Shown = new List<SuppressFlashFrame>();
        public readonly List<GameObject> Subjects = new List<GameObject>();
        public SuppressFlashMaskMode Mode;
        public int Hides;

        public void SetSubjects(IReadOnlyList<GameObject> subjects, SuppressFlashMaskMode mode)
        {
            Mode = mode;
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
    public void 복귀_컷은_홀드_계단으로_색수차_감소_원래_색_섞기()
    {
        SuppressFlashFrame first = SuppressFlashRules.Evaluate(_sequence, At(12), false);
        SuppressFlashFrame second = SuppressFlashRules.Evaluate(_sequence, At(13), false);
        SuppressFlashFrame third = SuppressFlashRules.Evaluate(_sequence, At(14), false);

        Assert.AreEqual(0.8f, first.Aberration, 1e-5f);
        Assert.AreEqual(0f, first.ReturnBlend, 1e-5f);
        Assert.AreEqual(first.Aberration, second.Aberration, 1e-5f, "2프레임 홀드 — 같은 그림");
        Assert.AreEqual(0.8f / 3f, third.Aberration, 1e-4f, "3번째 프레임 = 진행 2/3");
        Assert.AreEqual(2f / 3f, third.ReturnBlend, 1e-4f);
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
        Assert.AreEqual(0f, p.Fx.w, "레터박스 없음(179 후속 11-03 삭제)");
        Assert.AreEqual(frame.Dark.linear, p.Dark, "선형 색");
        Assert.AreEqual(0f, p.Mask.x, "기본 = 전체 화면");

        SuppressFlashParams interior = SuppressFlashScreenOutput.Build(frame, _sequence, new Vector2(0.3f, 0.6f), 1.5f, 9,
            SuppressFlashMaskMode.TargetInterior);
        Assert.AreEqual(1f, interior.Mask.x, "A7 대상 내부만");
        Assert.AreEqual(p.Fx, interior.Fx, "별·색수차 값은 그대로");
    }

    // ---- A7 섬광 = 대상 내부만(179/192 후속) ----

    [TestCase(false, false, SuppressFlashMaskMode.FullScreen)]
    [TestCase(false, true, SuppressFlashMaskMode.FullScreen)]
    [TestCase(true, false, SuppressFlashMaskMode.FullScreen)]
    [TestCase(true, true, SuppressFlashMaskMode.TargetInterior)]
    public void A7_켬이고_대상을_찾았을_때만_대상_내부(bool targetOnly, bool hasTarget, SuppressFlashMaskMode expected)
    {
        Assert.AreEqual(expected, SuppressFlashRules.SelectMaskMode(targetOnly, hasTarget));
    }

    [Test]
    public void 마스크_루트는_전체_화면이면_실루엣_대상_전부_대상_내부면_간파_대상만()
    {
        var caster = new GameObject("Caster");
        var target = new GameObject("Target");
        _objects.Add(caster);
        _objects.Add(target);
        var subjects = new List<GameObject> { caster, target };
        var targets = new List<GameObject> { target };
        var roots = new List<GameObject> { caster }; // 이전 값은 비운다

        SuppressFlashRules.SelectMaskRoots(SuppressFlashMaskMode.FullScreen, subjects, targets, roots);
        CollectionAssert.AreEqual(new[] { caster, target }, roots);

        SuppressFlashRules.SelectMaskRoots(SuppressFlashMaskMode.TargetInterior, subjects, targets, roots);
        CollectionAssert.AreEqual(new[] { target }, roots, "오너(동시 성공자 포함)는 내부 마스크에 넣지 않는다");

        SuppressFlashRules.SelectMaskRoots(SuppressFlashMaskMode.TargetInterior, subjects, null, roots);
        Assert.AreEqual(0, roots.Count);
    }

    [Test]
    public void 진행기는_시작_순간_그리는_범위를_출력에_넘긴다()
    {
        var output = new FakeOutput();
        var player = new SuppressFlashPlayer(() => _sequence, () => false, output);

        player.Begin(0f, Vector3.zero, null);
        Assert.AreEqual(SuppressFlashMaskMode.FullScreen, output.Mode, "기본 = 전체 화면");
        Assert.AreEqual(SuppressFlashMaskMode.FullScreen, player.MaskMode);

        player.Begin(1f, Vector3.zero, null, SuppressFlashMaskMode.TargetInterior);
        Assert.AreEqual(SuppressFlashMaskMode.TargetInterior, output.Mode);
        Assert.AreEqual(SuppressFlashMaskMode.TargetInterior, player.MaskMode);
    }

    [Test]
    public void 화면_출력은_대상_내부면_넘겨받은_루트만_비트_7()
    {
        var caster = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _objects.Add(caster);
        _objects.Add(target);
        var subjects = new List<GameObject> { caster, target };
        var targets = new List<GameObject> { target };
        var roots = new List<GameObject>();

        var output = new SuppressFlashScreenOutput(() => null);
        SuppressFlashRules.SelectMaskRoots(SuppressFlashMaskMode.TargetInterior, subjects, targets, roots);
        output.SetSubjects(roots, SuppressFlashMaskMode.TargetInterior);
        Assert.AreEqual(SuppressFlashMaskMode.TargetInterior, output.MaskMode);
        Assert.AreEqual(1, output.TaggedCount);
        Assert.AreEqual(0u, caster.GetComponent<Renderer>().renderingLayerMask & SuppressFlashMaskLayers.Mask);
        Assert.AreNotEqual(0u, target.GetComponent<Renderer>().renderingLayerMask & SuppressFlashMaskLayers.Mask);

        output.Hide();
        Assert.AreEqual(0u, target.GetComponent<Renderer>().renderingLayerMask & SuppressFlashMaskLayers.Mask, "끝 = 원복");
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

    [Test]
    public void 진행기는_시작_순간_고른_시퀀스를_끝까지_쓴다()
    {
        var other = ScriptableObject.CreateInstance<SuppressFlashSequence>();
        _objects.Add(other);
        other.ResetToSpec();
        SuppressFlashSequence selected = _sequence;
        var player = new SuppressFlashPlayer(() => selected, () => false, new FakeOutput());

        player.Begin(0f, Vector3.zero, null);
        Assert.AreSame(_sequence, player.Playing);
        selected = other; // 재생 중 칸 교체
        player.Tick(At(4));
        Assert.AreSame(_sequence, player.Playing, "도는 시퀀스는 끝까지");
        Assert.AreEqual(2, player.Current.CutIndex);

        player.Begin(1f, Vector3.zero, null);
        Assert.AreSame(other, player.Playing, "다음 성공부터 새 칸");
    }

    // ---- 3. 제압 암순응 컷(Q7) ----

    private sealed class FakeFlashOutput : ISuppressFlashOutput
    {
        public int Hides;
        public void SetSubjects(IReadOnlyList<GameObject> subjects, SuppressFlashMaskMode mode) { }
        public void Show(in SuppressFlashFrame frame, Vector3 worldCenter, SuppressFlashSequence sequence) { }
        public void Hide() => Hides++;
    }

    private sealed class FakeDarkAdapt : ISuppressDarkAdaptOutput
    {
        public readonly List<float> Cuts = new List<float>();
        public int Clears;
        public void ShowDarkAdaptCut(float darkness) => Cuts.Add(darkness);
        public void ClearDarkAdapt() => Clears++;
    }

    [Test]
    public void 암순응_컷_기본값은_어두움_중간_두_컷()
    {
        var sequence = ScriptableObject.CreateInstance<SuppressFlashSequence>();
        _objects.Add(sequence);
        Assert.AreEqual(2, sequence.darkAdaptCuts.Count);
        Assert.AreEqual(1f, sequence.darkAdaptCuts[0].darkness);
        Assert.AreEqual(0.5f, sequence.darkAdaptCuts[1].darkness);
    }

    [Test]
    public void 암순응_컷_시간표()
    {
        var cuts = new List<SuppressDarkAdaptCut> { new SuppressDarkAdaptCut(0.1f, 1f), new SuppressDarkAdaptCut(0.2f, 0.4f) };

        Assert.IsTrue(SuppressFlashRules.TryEvaluateDarkAdapt(cuts, 0f, out int i, out float d));
        Assert.AreEqual(0, i);
        Assert.AreEqual(1f, d);
        Assert.IsTrue(SuppressFlashRules.TryEvaluateDarkAdapt(cuts, 0.15f, out i, out d));
        Assert.AreEqual(1, i);
        Assert.AreEqual(0.4f, d, 1e-5f);
        Assert.IsFalse(SuppressFlashRules.TryEvaluateDarkAdapt(cuts, 0.31f, out _, out _), "끝 = 원래 색");
        Assert.IsFalse(SuppressFlashRules.TryEvaluateDarkAdapt(null, 0f, out _, out _));
        Assert.IsFalse(SuppressFlashRules.TryEvaluateDarkAdapt(cuts, -0.01f, out _, out _));
    }

    [Test]
    public void 제압_암순응은_시퀀스_끝_뒤_어두움_중간_원래_색으로_끊어_진행()
    {
        var sequence = ScriptableObject.CreateInstance<SuppressFlashSequence>();
        _objects.Add(sequence);
        float end = sequence.TotalFrames / sequence.frameRate;
        var flash = new FakeFlashOutput();
        var dark = new FakeDarkAdapt();
        var player = new SuppressFlashPlayer(() => sequence, () => false, flash, dark);

        player.Begin(0f, Vector3.zero, null);
        player.Tick(end - 0.01f);
        Assert.IsTrue(player.IsPlaying);
        Assert.IsFalse(player.DarkAdaptActive);
        Assert.AreEqual(0, dark.Cuts.Count, "시퀀스 중엔 암순응 없음");

        player.Tick(end + 0.05f);
        Assert.IsFalse(player.IsPlaying, "시퀀스 끝");
        Assert.AreEqual(1, flash.Hides);
        Assert.IsTrue(player.DarkAdaptActive);
        Assert.AreEqual(0, player.DarkAdaptCutIndex);
        CollectionAssert.AreEqual(new[] { 1f }, dark.Cuts, "어두움");

        player.Tick(end + 0.1f);
        Assert.AreEqual(1, dark.Cuts.Count, "같은 컷이면 다시 안 보낸다");

        player.Tick(end + 0.2f);
        Assert.AreEqual(1, player.DarkAdaptCutIndex);
        CollectionAssert.AreEqual(new[] { 1f, 0.5f }, dark.Cuts, "중간");

        player.Tick(end + 0.31f);
        Assert.IsFalse(player.DarkAdaptActive, "원래 색");
        Assert.AreEqual(1, dark.Clears);
    }

    [Test]
    public void 암순응_컷_중_멈춤_새_성공은_즉시_걷는다()
    {
        var sequence = ScriptableObject.CreateInstance<SuppressFlashSequence>();
        _objects.Add(sequence);
        float end = sequence.TotalFrames / sequence.frameRate;
        var dark = new FakeDarkAdapt();
        var player = new SuppressFlashPlayer(() => sequence, () => false, new FakeFlashOutput(), dark);

        player.Begin(0f, Vector3.zero, null);
        player.Tick(end + 0.05f);
        Assert.IsTrue(player.DarkAdaptActive);
        player.Stop();
        Assert.IsFalse(player.DarkAdaptActive);
        Assert.AreEqual(1, dark.Clears);

        player.Begin(10f, Vector3.zero, null);
        player.Tick(10f + end + 0.05f);
        player.Begin(20f, Vector3.zero, null);
        Assert.AreEqual(2, dark.Clears, "새 성공 = 남은 암순응 컷을 걷고 처음부터");
        Assert.IsTrue(player.IsPlaying);
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
}
