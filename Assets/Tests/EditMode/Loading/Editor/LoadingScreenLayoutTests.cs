using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 할 일 283 — 로딩창이 해상도·화면비가 달라도 같은 자리·같은 상대 크기로 보이도록 고정한 씬 배선.
public sealed class LoadingScreenLayoutTests
{
    private const string ScenePath = "Assets/0.Scenes/MainFlow/2.LoadingScene.unity";

    private Scene _scene;
    private bool _openedHere;
    private NetworkLoadingScreenView _view;

    [OneTimeSetUp]
    public void Open()
    {
        _scene = SceneManager.GetSceneByPath(ScenePath);
        if (!_scene.isLoaded)
        {
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            _openedHere = true;
        }

        foreach (var root in _scene.GetRootGameObjects())
        {
            _view = root.GetComponentInChildren<NetworkLoadingScreenView>(true);
            if (_view != null)
            {
                break;
            }
        }

        Assert.That(_view, Is.Not.Null, "로딩씬에 NetworkLoadingScreenView 가 있어야 한다");
    }

    [OneTimeTearDown]
    public void Close()
    {
        if (_openedHere && _scene.IsValid())
        {
            EditorSceneManager.CloseScene(_scene, true);
        }
    }

    [Test]
    public void CanvasScaler_ScalesWithScreen_Expand1920x1080()
    {
        var scaler = _view.GetComponent<CanvasScaler>();
        Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
        Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(1920f, 1080f)));
        Assert.That(scaler.screenMatchMode, Is.EqualTo(CanvasScaler.ScreenMatchMode.Expand),
            "Expand = 1920×1080 기준 사각형이 어떤 화면비에서도 잘리지 않고 다 들어온다");
    }

    [Test]
    public void Background_EnvelopesParent_WithSpriteAspect()
    {
        var background = FindChild("Background");
        var fitter = background.GetComponent<AspectRatioFitter>();
        Assert.That(fitter, Is.Not.Null);
        Assert.That(fitter.aspectMode, Is.EqualTo(AspectRatioFitter.AspectMode.EnvelopeParent), "꽉 채우되 비율 유지(잘림 허용)");

        var sprite = background.GetComponent<Image>().sprite;
        if (sprite != null)
        {
            Assert.That(fitter.aspectRatio, Is.EqualTo(sprite.rect.width / sprite.rect.height).Within(0.01f),
                "배경 비율 = 원본 그림 비율(찌그러짐 금지)");
        }
    }

    [TestCase("Text_Tooltip", 1f)]
    [TestCase("progress fill", 0f)]
    [TestCase("Text_Status", 0f)]
    [TestCase("Text_Percentage", 0f)]
    public void Elements_AnchoredToNearestEdge(string name, float edgeY)
    {
        var rect = FindChild(name);
        Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(0.5f, edgeY)));
        Assert.That(rect.anchorMax, Is.EqualTo(new Vector2(0.5f, edgeY)));
    }

    [Test]
    public void Elements_FitInsideReferenceRect()
    {
        var reference = new Rect(0f, 0f, 1920f, 1080f);
        foreach (var name in new[] { "Text_Tooltip", "progress fill", "Text_Status", "Text_Percentage" })
        {
            var rect = FindChild(name);
            Vector2 anchor = new Vector2(rect.anchorMin.x * reference.width, rect.anchorMin.y * reference.height);
            Vector2 center = anchor + rect.anchoredPosition + (new Vector2(0.5f, 0.5f) - rect.pivot) * rect.sizeDelta;
            var bounds = new Rect(center - rect.sizeDelta * 0.5f, rect.sizeDelta);
            Assert.That(reference.Contains(bounds.min) && reference.Contains(bounds.max), Is.True,
                $"{name} 가 1920×1080 기준 사각형 안에 있어야 한다 — {bounds}");
        }
    }

    private RectTransform FindChild(string name)
    {
        foreach (RectTransform child in _view.transform)
        {
            if (child.name == name)
            {
                return child;
            }
        }

        Assert.Fail($"로딩 캔버스에 {name} 가 없다");
        return null;
    }
}
