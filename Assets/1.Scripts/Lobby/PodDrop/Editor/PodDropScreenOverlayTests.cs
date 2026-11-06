using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 포드 레터박스 오버레이(할 일 230) — 프리팹을 복제하면 띠 높이 anchor·검은 화면 알파·정렬만 바꾸고 나머지(색 등)는 프리팹 값을 지키는지,
/// 프리팹이 없거나 구성이 안 맞으면 코드 기본 구성으로 대체하는지, 커밋된 프리팹이 계약(경로·자식 이름)을 지키는지.
/// </summary>
public sealed class PodDropScreenOverlayTests
{
    GameObject _host;
    GameObject _template;

    [TearDown]
    public void TearDown()
    {
        if (_host != null) Object.DestroyImmediate(_host);
        if (_template != null) Object.DestroyImmediate(_template);
    }

    PodDropScreenOverlay NewOverlay(GameObject prefab)
    {
        _host = new GameObject("OverlayHost");
        var overlay = _host.AddComponent<PodDropScreenOverlay>();
        overlay.Prefab = prefab;
        return overlay;
    }

    [Test]
    public void Prefab_OnlyLetterboxAnchorFadeAlphaAndSortingChange()
    {
        _template = PodDropScreenOverlay.CreateDefault(null, 0.04f);
        _template.GetComponent<Canvas>().sortingOrder = 7;
        var topTemplate = (RectTransform)_template.transform.Find(PodDropScreenOverlay.TopBarName);
        topTemplate.GetComponent<Image>().color = Color.red;
        topTemplate.anchorMin = new Vector2(0.1f, topTemplate.anchorMin.y);
        topTemplate.offsetMin = new Vector2(5f, 0f);
        _template.transform.Find(PodDropScreenOverlay.FadeName).GetComponent<Image>().color = new Color(0.2f, 0f, 0f, 0f);

        PodDropScreenOverlay overlay = NewOverlay(_template);
        overlay.Apply(0.1f, 0.5f, false);

        Transform root = _host.transform.Find(_template.name);
        Assert.IsNotNull(root, "프리팹 복제본이 이름 그대로 붙어야 한다");
        Assert.AreEqual(PodDropScreenOverlay.CanvasSortingOrder, root.GetComponent<Canvas>().sortingOrder);

        var top = (RectTransform)root.Find(PodDropScreenOverlay.TopBarName);
        var bottom = (RectTransform)root.Find(PodDropScreenOverlay.BottomBarName);
        Assert.AreEqual(0.9f, top.anchorMin.y, 1e-5f);
        Assert.AreEqual(0.1f, bottom.anchorMax.y, 1e-5f);
        Assert.AreEqual(0.1f, top.anchorMin.x, 1e-5f, "anchor x 는 프리팹 값");
        Assert.AreEqual(5f, top.offsetMin.x, 1e-4f, "offset 은 프리팹 값");
        Assert.AreEqual(Color.red, top.GetComponent<Image>().color, "띠 색은 프리팹 값");

        Image fade = root.Find(PodDropScreenOverlay.FadeName).GetComponent<Image>();
        Assert.AreEqual(0.5f, fade.color.a, 1e-5f);
        Assert.AreEqual(0.2f, fade.color.r, 1e-5f, "검은 화면 RGB 는 프리팹 값");
        Assert.IsTrue(fade.enabled);
    }

    [Test]
    public void NoPrefab_BuildsDefault()
    {
        PodDropScreenOverlay overlay = NewOverlay(null);
        overlay.Apply(0.04f, 0f, false);

        Transform root = _host.transform.Find(PodDropScreenOverlay.RootName);
        Assert.IsNotNull(root);
        Assert.AreEqual(PodDropScreenOverlay.CanvasSortingOrder, root.GetComponent<Canvas>().sortingOrder);
        Assert.AreEqual(0.96f, ((RectTransform)root.Find(PodDropScreenOverlay.TopBarName)).anchorMin.y, 1e-5f);
        Assert.IsFalse(root.Find(PodDropScreenOverlay.FadeName).GetComponent<Image>().enabled, "알파 0 이면 검은 화면은 꺼진다");
    }

    [Test]
    public void BrokenPrefab_FallsBackToDefault()
    {
        _template = PodDropScreenOverlay.CreateDefault(null, 0.04f);
        _template.name = "Broken";
        Object.DestroyImmediate(_template.transform.Find(PodDropScreenOverlay.FadeName).gameObject);

        PodDropScreenOverlay overlay = NewOverlay(_template);
        overlay.Apply(0.04f, 1f, false);   // 경고 1회(구성 안 맞음)

        Assert.IsNull(_host.transform.Find("Broken"), "구성이 안 맞는 복제본은 지운다");
        Assert.IsNotNull(_host.transform.Find(PodDropScreenOverlay.RootName));
    }

    [Test]
    public void CommittedPrefab_MatchesContract()
    {
        var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<PodDropSettings>("Assets/9.ScriptableObject/Lobby/PodDropSettings.asset");
        Assert.IsNotNull(settings);
        GameObject prefab = settings.OverlayPrefab;
        Assert.IsNotNull(prefab, $"PodDropSettings.overlayPrefab 이 비었다 — Tools/Lobby/포드 레터박스 프리팹 만들기");
        Assert.AreEqual(PodDropOverlayPrefabAuthoring.PrefabPath, UnityEditor.AssetDatabase.GetAssetPath(prefab));

        Assert.IsNotNull(prefab.GetComponent<Canvas>());
        var top = prefab.transform.Find(PodDropScreenOverlay.TopBarName) as RectTransform;
        var bottom = prefab.transform.Find(PodDropScreenOverlay.BottomBarName) as RectTransform;
        Transform fade = prefab.transform.Find(PodDropScreenOverlay.FadeName);
        Assert.IsNotNull(top);
        Assert.IsNotNull(bottom);
        Assert.IsNotNull(fade != null ? fade.GetComponent<Image>() : null);
    }
}
