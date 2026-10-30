using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 특성 창 배경 블러 채도 패스(할 일 165 후속) — 0 = 흑백, 1 = 원색, 채도 패스가 RT 를 더 남기지 않음(반납).
/// 실제 셰이더를 GPU 로 돌린다(설정 에셋의 직렬화 참조 그대로).
/// </summary>
public sealed class TraitBackdropBlurTests
{
    private const string SettingsPath = "Assets/9.ScriptableObject/Traits/TraitBackdropBlurSettings.asset";

    private TraitBackdropBlurSettings settings;
    private Texture2D source;

    [SetUp]
    public void SetUp()
    {
        var asset = AssetDatabase.LoadAssetAtPath<TraitBackdropBlurSettings>(SettingsPath);
        Assert.That(asset, Is.Not.Null, SettingsPath);
        Assert.That(asset.saturationShader, Is.Not.Null, "채도 셰이더가 직렬화 참조로 물려 있어야 빌드에서 스트립되지 않는다");
        Assume.That(asset.CanBlur && asset.saturationShader.isSupported, "이 GPU 에서 셰이더 미지원");

        settings = Object.Instantiate(asset);
        settings.downsampleShift = 0;
        settings.iterations = 1;

        // 왼쪽 절반 빨강, 오른쪽 절반 파랑 — 가운데만 섞이고 양 끝은 원색이 남는다.
        source = new Texture2D(32, 8, TextureFormat.RGBA32, false);
        var pixels = new Color32[32 * 8];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = i % 32 < 16 ? new Color32(255, 0, 0, 255) : new Color32(0, 0, 255, 255);
        source.SetPixels32(pixels);
        source.Apply();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(source);
        Object.DestroyImmediate(settings);
    }

    [Test]
    public void SaturationZero_ResultIsGray()
    {
        settings.saturation = 0f;
        Color edge = SampleLeftEdge();
        Assert.That(Mathf.Abs(edge.r - edge.g), Is.LessThan(0.02f), $"흑백이어야 함: {edge}");
        Assert.That(Mathf.Abs(edge.r - edge.b), Is.LessThan(0.02f), $"흑백이어야 함: {edge}");
        Assert.That(edge.r, Is.GreaterThan(0.01f), "루마가 0 이 아님(빨강 Rec.709 0.2126)");
    }

    [Test]
    public void SaturationOne_KeepsColor()
    {
        settings.saturation = 1f;
        Assert.That(settings.ShouldSaturate, Is.False, "원색이면 채도 패스를 건너뛴다");
        Color edge = SampleLeftEdge();
        Assert.That(edge.r - edge.g, Is.GreaterThan(0.5f), $"빨강이 남아야 함: {edge}");
    }

    [Test]
    public void SaturationPass_LeavesNoExtraRenderTextures()
    {
        settings.saturation = 0f;
        TraitBackdropBlur.Release(TraitBackdropBlur.Blur(source, false, settings)); // 임시 RT 풀 채우기
        int before = Resources.FindObjectsOfTypeAll<RenderTexture>().Length;

        for (int i = 0; i < 10; i++)
            TraitBackdropBlur.Release(TraitBackdropBlur.Blur(source, false, settings));

        int after = Resources.FindObjectsOfTypeAll<RenderTexture>().Length;
        Assert.That(after, Is.LessThanOrEqualTo(before), "반납 안 된 임시 RT 가 늘면 풀이 새로 만든다(누수)");
    }

    private Color SampleLeftEdge()
    {
        RenderTexture result = TraitBackdropBlur.Blur(source, false, settings);
        Assert.That(result, Is.Not.Null);
        var read = new Texture2D(result.width, result.height, TextureFormat.RGBA32, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            RenderTexture.active = result;
            read.ReadPixels(new Rect(0, 0, result.width, result.height), 0, 0);
            read.Apply();
            return read.GetPixel(1, result.height / 2);
        }
        finally
        {
            RenderTexture.active = previous;
            TraitBackdropBlur.Release(result);
            Object.DestroyImmediate(read);
        }
    }
}
