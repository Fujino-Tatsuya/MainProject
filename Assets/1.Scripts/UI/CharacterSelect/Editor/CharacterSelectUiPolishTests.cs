using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 캐릭터 선택 UI 수정(할 일 162) — 실제 패널 프리팹을 프리뷰 씬에 올려 본다.
/// 이름판 끔.
/// </summary>
public sealed class CharacterSelectUiPolishTests
{
    Scene _scene;
    GameObject _instance;
    CharacterSelectPanel _panel;

    [SetUp]
    public void SetUp()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSelectPanelAuthoring.PanelPath);
        Assert.IsNotNull(prefab, CharacterSelectPanelAuthoring.PanelPath);

        _scene = EditorSceneManager.NewPreviewScene();
        _instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _scene);
        _panel = _instance.GetComponent<CharacterSelectPanel>();
    }

    [TearDown]
    public void TearDown()
    {
        if (_panel != null)
            _panel.Hide(); // 정적 열림 수 되돌리기(IsEscapeClaimed)
        EditorSceneManager.ClosePreviewScene(_scene);
    }

    [Test]
    public void Nameplate_OffInPrefab_PanelStillFillsIt()
    {
        Transform plate = _instance.transform.Find("Content/Nameplate");
        Assert.IsNotNull(plate, "이름판은 지우지 않고 끈다");
        Assert.IsFalse(plate.gameObject.activeSelf, "이름판 꺼짐");

        var so = new SerializedObject(_panel);
        var name = so.FindProperty("nameplateName").objectReferenceValue as TMP_Text;
        Assert.IsNotNull(name, "칸은 연결된 채 — 다시 켜면 바로 쓴다");

        Assert.DoesNotThrow(() => _panel.Show(0, false));
        _panel.MoveFocus(1);
        Assert.IsFalse(plate.gameObject.activeSelf, "Show·이동이 이름판을 다시 켜지 않는다");
        Assert.IsFalse(string.IsNullOrEmpty(name.text), "꺼진 채로도 포커스 이름을 넣는다(오류 없음)");
    }

    [Test]
    public void Chevron_HoverTint_DimsBodyOnly_OutlineStaysOpaque()
    {
        var button = new SerializedObject(_panel).FindProperty("confirmButton").objectReferenceValue as Button;
        Assert.IsNotNull(button);
        Assert.AreEqual(Selectable.Transition.ColorTint, button.transition);
        Assert.AreEqual(CharacterSelectPanelAuthoring.ChevronHoverAlpha, button.colors.highlightedColor.a, 0.001f, "hover 동안 본체 알파");
        Assert.AreEqual(CharacterSelectPanelAuthoring.ChevronHoverFadeSeconds, button.colors.fadeDuration, 0.001f, "전이 시간");
        Assert.AreEqual(1f, button.colors.normalColor.a, 0.001f, "평소 알파 1");

        var outline = button.GetComponent<UiHoverOutline>().Outline;
        Assert.AreNotSame(button.targetGraphic, outline, "Color Tint 대상은 셰브론 본체 Image 뿐");
        Assert.AreEqual(1f, outline.color.a, 0.001f, "흰 아웃라인 알파 1 유지");
    }

    [Test]
    public void FocusRing_CardPrefabAndTemplate_UseBakedRing_SameRect()
    {
        Sprite baked = AssetDatabase.LoadAssetAtPath<Sprite>(CharacterSelectFocusRingBaker.OutputPath);
        Assert.IsNotNull(baked, "구운 링 PNG(git)");

        var cardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSelectPanelAuthoring.CardPath);
        var cardRing = cardPrefab.transform.Find("Ring_Focus").GetComponent<Image>();
        var template = _instance.transform.Find($"Content/Cards/{CharacterSelectPanelAuthoring.CardTemplateName}/Ring_Focus").GetComponent<Image>();
        foreach (Image ring in new[] { cardRing, template })
        {
            Assert.AreSame(baked, ring.sprite, $"{ring.name} 스프라이트 = 0.7배 링");
            Vector2 size = ((RectTransform)ring.transform).sizeDelta;
            Assert.AreEqual(new Vector2(694f, 551f), size, "Ring_Focus rect 는 그대로");
            Assert.AreEqual(size, baked.rect.size, "PNG = rect 1:1(찌그러지지 않는다)");
        }
    }

    [Test]
    public void FocusRing_Png_MatchesRebake_AndBandIs70Percent()
    {
        Texture2D ring = CharacterSelectFocusRingBaker.LoadPng(CharacterSelectFocusRingBaker.SourceRingPath);
        Texture2D gap = CharacterSelectFocusRingBaker.LoadPng(CharacterSelectFocusRingBaker.CardGapPath);
        Texture2D baked = CharacterSelectFocusRingBaker.LoadPng(CharacterSelectFocusRingBaker.OutputPath);
        try
        {
            var size = new Vector2Int(baked.width, baked.height);
            Color32[] expected = CharacterSelectFocusRingBaker.BakePixels(ring.GetPixels32(), ring.width, ring.height,
                gap.GetPixels32(), gap.width, gap.height, size.x, size.y, CharacterSelectFocusRingBaker.ThicknessScale);
            Color32[] actual = baked.GetPixels32();
            int diff = 0;
            for (int i = 0; i < expected.Length; i++)
                if (expected[i].a != actual[i].a)
                    diff++;
            Assert.AreEqual(0, diff, "링 PNG 가 다시 구운 것과 다르다 — 굽기 메뉴를 다시 돌릴 것");

            Color32[] original = CharacterSelectFocusRingBaker.OriginalPixels(ring, gap, size);
            Color32[] gapPixels = gap.GetPixels32();
            foreach (int row in new[] { size.y / 4, size.y / 2, size.y * 3 / 4 })
            {
                float before = CharacterSelectFocusRingBaker.BandWidth(original, size.x, size.y, gapPixels, gap.width, gap.height, row);
                float after = CharacterSelectFocusRingBaker.BandWidth(actual, size.x, size.y, gapPixels, gap.width, gap.height, row);
                Assert.Greater(before, 10f, "원본 띠가 보여야 비교가 된다");
                Assert.AreEqual(CharacterSelectFocusRingBaker.ThicknessScale, after / before, 0.06f, $"줄 {row} 띠 {before}px → {after}px");
            }
        }
        finally
        {
            Object.DestroyImmediate(ring);
            Object.DestroyImmediate(gap);
            Object.DestroyImmediate(baked);
        }
    }

    [Test]
    public void Chevron_Ring_ThicknessIs7px()
    {
        Assert.AreEqual(7f, CharacterSelectPanelAuthoring.ChevronRingThickness, "링 두께(화면 px) — PNG 대조는 CharacterSelectChevronHoverOutlineTests");
        Assert.AreEqual(3f, CharacterSelectPanelAuthoring.ChevronRingGap, "간격은 그대로");
    }
}
