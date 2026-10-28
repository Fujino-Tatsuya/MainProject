using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 오너 마스크 암전(할 일 132 단계 3) — 모드 선택·강도 0 패스 생략·색/불투명도 칸·렌더링 레이어 비트 태깅/원복.
/// </summary>
public sealed class InterruptOwnerDimTests
{
    private sealed class Look : IInterruptOwnerDimLook
    {
        public Color OwnerDimColor { get; set; } = Color.black;
        public float OwnerDimOpacity { get; set; } = 1f;
    }

    private GameObject _owner;
    private GameObject _target;
    private GameObject _other;

    [SetUp]
    public void SetUp()
    {
        DarkAdaptationState.Clear();
        DarkAdaptationState.ClearMask();
        _owner = CreateWithRenderers("Owner", 2);
        _target = CreateWithRenderers("Target", 1);
        _other = CreateWithRenderers("Other", 1);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_owner);
        Object.DestroyImmediate(_target);
        Object.DestroyImmediate(_other);
        DarkAdaptationState.Clear();
        DarkAdaptationState.ClearMask();
    }

    // 루트 + 자식(비활성 하나 포함) 렌더러. 렌더링 레이어 기본 = bit 0(조명).
    private static GameObject CreateWithRenderers(string name, int children)
    {
        var root = new GameObject(name);
        root.AddComponent<MeshRenderer>().renderingLayerMask = 1u;
        for (int i = 0; i < children; i++)
        {
            var child = new GameObject($"{name}_{i}");
            child.transform.SetParent(root.transform, false);
            child.AddComponent<MeshRenderer>().renderingLayerMask = 1u;
            child.SetActive(i % 2 == 1);
        }

        return root;
    }

    private static void AssertAllMasks(GameObject root, uint expected)
    {
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            Assert.AreEqual(expected, r.renderingLayerMask, r.name);
    }

    // ---- 비트 배치 ----

    [Test]
    public void Bit_IsNotZero_AndDoesNotOverlapExistingBits()
    {
        Assert.AreNotEqual(0, InterruptOwnerMaskLayers.LayerIndex, "비트 0 = 조명");
        Assert.AreEqual(0u, InterruptOwnerMaskLayers.Mask & DecalReceivers.Mask);
        Assert.AreEqual(0u, InterruptOwnerMaskLayers.Mask & PlayerSilhouetteLayers.LocalMask);
        Assert.AreEqual(0u, InterruptOwnerMaskLayers.Mask & PlayerSilhouetteLayers.RemoteMask);
        Assert.AreEqual(0u, InterruptOwnerMaskLayers.Mask & (1u << 8), "TagManager Ground Layer(바닥 257)");
        Assert.AreEqual(1u << 4, InterruptOwnerMaskLayers.Mask, "SVN DashAfterimage.cs 의 리터럴과 같아야 한다");
    }

    [Test]
    public void AddRestore_OnlyTouchesOwnBit()
    {
        uint tagged = InterruptOwnerMaskLayers.Add(1u | PlayerSilhouetteLayers.LocalMask);
        Assert.AreEqual(1u | PlayerSilhouetteLayers.LocalMask | InterruptOwnerMaskLayers.Mask, tagged);
        Assert.AreEqual(1u | PlayerSilhouetteLayers.LocalMask, InterruptOwnerMaskLayers.Restore(tagged, false));
        Assert.AreEqual(tagged, InterruptOwnerMaskLayers.Restore(tagged, true), "원래 있던 비트(Everything 등)는 지우지 않는다");
    }

    // ---- 모드 선택·강도 0 ----

    [Test]
    public void Select_BothZero_None()
    {
        Assert.AreEqual(DarkAdaptationMode.None, DarkAdaptationModes.Select(0f, 0f, true));
    }

    [Test]
    public void Select_LuminanceOnly()
    {
        Assert.AreEqual(DarkAdaptationMode.Luminance, DarkAdaptationModes.Select(0.35f, 0f, true));
    }

    [Test]
    public void Select_MaskOnly_NeedsTarget()
    {
        Assert.AreEqual(DarkAdaptationMode.Mask, DarkAdaptationModes.Select(0f, 1f, true));
        Assert.AreEqual(DarkAdaptationMode.None, DarkAdaptationModes.Select(0f, 1f, false));
    }

    [Test]
    public void Select_Both()
    {
        Assert.AreEqual(DarkAdaptationMode.Luminance | DarkAdaptationMode.Mask, DarkAdaptationModes.Select(1f, 0.5f, true));
    }

    // ---- 색·불투명도 칸 ----

    [Test]
    public void CoverColor_DefaultBlack100()
    {
        Color c = DarkAdaptationModes.CoverColor(Color.black, 1f, 1f);
        Assert.AreEqual(Color.black, c);
    }

    [Test]
    public void CoverColor_Navy90_ScaledByIntensity_IgnoresColorAlpha()
    {
        var navy = new Color(0.05f, 0.07f, 0.2f, 0.3f);
        Color c = DarkAdaptationModes.CoverColor(navy, 0.9f, 0.5f);
        Assert.AreEqual(navy.r, c.r, 1e-6f);
        Assert.AreEqual(navy.g, c.g, 1e-6f);
        Assert.AreEqual(navy.b, c.b, 1e-6f);
        Assert.AreEqual(0.45f, c.a, 1e-6f);
    }

    [Test]
    public void CoverAlpha_Clamps()
    {
        Assert.AreEqual(1f, DarkAdaptationModes.CoverAlpha(2f, 3f));
        Assert.AreEqual(0f, DarkAdaptationModes.CoverAlpha(-1f, 1f));
    }

    // ---- 태거 ----

    [Test]
    public void Tagger_TagsInactiveChildren_RestoresOriginal()
    {
        var tagger = new InterruptOwnerMaskTagger();
        Assert.AreEqual(3, tagger.Tag(_owner));
        AssertAllMasks(_owner, 1u | InterruptOwnerMaskLayers.Mask);
        Assert.AreEqual(0, tagger.Tag(_owner), "이미 붙인 렌더러는 다시 세지 않는다");

        tagger.RestoreAll();
        AssertAllMasks(_owner, 1u);
        Assert.AreEqual(0, tagger.Count);
    }

    [Test]
    public void Tagger_KeepsBitThatWasAlreadyThere()
    {
        Renderer everything = _owner.GetComponent<Renderer>();
        everything.renderingLayerMask = uint.MaxValue;

        var tagger = new InterruptOwnerMaskTagger();
        tagger.Tag(_owner);
        tagger.RestoreAll();
        Assert.AreEqual(uint.MaxValue, everything.renderingLayerMask);
    }

    [Test]
    public void Tagger_RestoreKeepsOtherBitsChangedMeanwhile()
    {
        var tagger = new InterruptOwnerMaskTagger();
        tagger.Tag(_owner);
        Renderer r = _owner.GetComponent<Renderer>();
        r.renderingLayerMask |= PlayerSilhouetteLayers.LocalMask; // 암전 중 실루엣 재태깅

        tagger.RestoreAll();
        Assert.AreEqual(1u | PlayerSilhouetteLayers.LocalMask, r.renderingLayerMask);
    }

    [Test]
    public void Tagger_DestroyedRenderer_Skipped()
    {
        var tagger = new InterruptOwnerMaskTagger();
        tagger.Tag(_target);
        Object.DestroyImmediate(_target);
        _target = null;
        Assert.DoesNotThrow(tagger.RestoreAll);
    }

    // ---- 출력(상태 기계 → 렌더) ----

    [Test]
    public void Output_ZeroToPositive_TagsSubjectsOnly_BackToZeroRestores()
    {
        var output = new InterruptOwnerDimOutput(new Look(), useHudCover: false);
        output.SetSubjects(_owner, _target);

        output.SetOwnerDim(0.5f);
        Assert.IsTrue(output.IsActive);
        Assert.AreEqual(5, output.TaggedCount);
        AssertAllMasks(_owner, 1u | InterruptOwnerMaskLayers.Mask);
        AssertAllMasks(_target, 1u | InterruptOwnerMaskLayers.Mask);
        AssertAllMasks(_other, 1u);
        Assert.AreEqual(0.5f, DarkAdaptationState.MaskIntensity, 1e-6f);

        output.SetOwnerDim(1f); // 램프 중 — 다시 모으지 않는다
        Assert.AreEqual(5, output.TaggedCount);

        output.SetOwnerDim(0f);
        Assert.IsFalse(output.IsActive);
        AssertAllMasks(_owner, 1u);
        AssertAllMasks(_target, 1u);
        Assert.AreEqual(0f, DarkAdaptationState.MaskIntensity, "강도 0 = 마스크 패스 없음");
    }

    [Test]
    public void Output_ZeroWithoutActivation_NoTagging()
    {
        var output = new InterruptOwnerDimOutput(new Look(), useHudCover: false);
        output.SetSubjects(_owner, _target);
        output.SetOwnerDim(0f);
        Assert.AreEqual(0, output.TaggedCount);
        AssertAllMasks(_owner, 1u);
        Assert.AreEqual(DarkAdaptationMode.None,
            DarkAdaptationModes.Select(DarkAdaptationState.Intensity, DarkAdaptationState.MaskIntensity, true));
    }

    [Test]
    public void Output_OpacityZero_NoMaskPass()
    {
        var output = new InterruptOwnerDimOutput(new Look { OwnerDimOpacity = 0f }, useHudCover: false);
        output.SetSubjects(_owner, null);
        output.SetOwnerDim(1f);
        Assert.AreEqual(0f, DarkAdaptationState.MaskIntensity);
        output.SetOwnerDim(0f);
    }

    [Test]
    public void Output_ReadsLookEachChange()
    {
        var look = new Look { OwnerDimColor = new Color(0.1f, 0.1f, 0.3f), OwnerDimOpacity = 0.9f };
        var output = new InterruptOwnerDimOutput(look, useHudCover: false);
        output.SetOwnerDim(1f);
        Assert.AreEqual(0.9f, output.CoverColor.a, 1e-6f);
        Assert.AreEqual(0.3f, output.CoverColor.b, 1e-6f);

        look.OwnerDimOpacity = 0.5f;
        output.SetOwnerDim(0.8f);
        Assert.AreEqual(0.4f, output.CoverColor.a, 1e-6f);
        output.SetOwnerDim(0f);
    }

    [Test]
    public void Output_KeepWhileDimmed_TagsNowOrAtStart_ClearedAfter()
    {
        var output = new InterruptOwnerDimOutput(new Look(), useHudCover: false);
        output.SetSubjects(_owner, null);
        output.KeepWhileDimmed(_other); // 시작 전 → 시작 때
        output.SetOwnerDim(1f);
        AssertAllMasks(_other, 1u | InterruptOwnerMaskLayers.Mask);

        output.SetOwnerDim(0f);
        AssertAllMasks(_other, 1u);

        output.SetOwnerDim(1f); // 다음 암전엔 다시 부르지 않으면 빠진다
        AssertAllMasks(_other, 1u);
        output.KeepWhileDimmed(_other); // 켜진 동안 → 바로
        AssertAllMasks(_other, 1u | InterruptOwnerMaskLayers.Mask);
        output.SetOwnerDim(0f);
        AssertAllMasks(_other, 1u);
    }

    [Test]
    public void Output_SubjectsChangedWhileActive_AppliedNextStart()
    {
        var output = new InterruptOwnerDimOutput(new Look(), useHudCover: false);
        output.SetSubjects(_owner, null);
        output.SetOwnerDim(1f);
        output.SetSubjects(_owner, _target);
        AssertAllMasks(_target, 1u);

        output.SetOwnerDim(0f);
        output.SetOwnerDim(0.2f);
        AssertAllMasks(_target, 1u | InterruptOwnerMaskLayers.Mask);
        output.SetOwnerDim(0f);
    }

    [Test]
    public void Output_DrivenByPresentation_StrongPredictThenFail()
    {
        var output = new InterruptOwnerDimOutput(new Look(), useHudCover: false);
        var presentation = new InterruptOwnerPresentation(new Settings(), output);

        Assert.IsTrue(presentation.Predict(BeaverLobby.Player.Dash.InterruptAttemptForecast.Strong, null, 0f));
        output.SetSubjects(_owner, _target);
        presentation.Tick(0.05f);
        Assert.IsTrue(output.IsActive);
        AssertAllMasks(_owner, 1u | InterruptOwnerMaskLayers.Mask);

        presentation.LocalEnded(0.1f);
        presentation.Tick(0.5f);
        Assert.IsFalse(output.IsActive);
        AssertAllMasks(_owner, 1u);
        AssertAllMasks(_target, 1u);
    }

    private sealed class Settings : IInterruptOwnerPresentationSettings
    {
        public float OwnerDimInDuration => 0.1f;
        public float OwnerDimOutDuration => 0.2f;
    }
}
