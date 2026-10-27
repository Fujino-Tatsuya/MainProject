using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// SELECT 셰브론 hover 흰 아웃라인(할 일 109 후속) — 실제 패널 프리팹을 프리뷰 씬에 올려 본다.
/// Enter/Exit 는 EventSystem 이 레이캐스트(삼각형 필터 포함)로 고른 대상에만 보내므로 여기서는 핸들러를 직접 부른다.
/// EditMode 에서는 OnDisable 이 돌지 않아 직접 부른다. 링 PNG 는 원본 chevron.png 와 파일 바이트로 비교한다.
/// </summary>
public sealed class CharacterSelectChevronHoverOutlineTests
{
    const string ChevronPng = "Assets/50.Art/UI/char_select/chevron.png";

    Scene _scene;
    CharacterSelectPanel _panel;
    Button _button;
    UiHoverOutline _hover;

    [SetUp]
    public void SetUp()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSelectPanelAuthoring.PanelPath);
        Assert.IsNotNull(prefab, CharacterSelectPanelAuthoring.PanelPath);

        _scene = EditorSceneManager.NewPreviewScene();
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _scene);
        _panel = instance.GetComponent<CharacterSelectPanel>();
        _button = new SerializedObject(_panel).FindProperty("confirmButton").objectReferenceValue as Button;
        Assert.IsNotNull(_button, "프리팹 confirmButton 이 연결돼 있어야 한다");
        _hover = _button.GetComponent<UiHoverOutline>();
        Assert.IsNotNull(_hover, "셰브론에 UiHoverOutline 이 붙어 있어야 한다");
    }

    [TearDown]
    public void TearDown()
    {
        if (_panel != null)
            _panel.Hide(); // 정적 열림 수 되돌리기(IsEscapeClaimed)
        EditorSceneManager.ClosePreviewScene(_scene);
    }

    static PointerEventData Pointer() => new PointerEventData(null);

    [Test]
    public void Prefab_RingWired_OffByDefault_NotRaycastTarget()
    {
        var outline = _hover.Outline as Image;
        Assert.IsNotNull(outline, "아웃라인 = 셰브론 자식 Image(구운 실루엣 링)");
        Assert.AreSame(_button.transform, outline.transform.parent);
        Assert.AreEqual(CharacterSelectPanelAuthoring.ChevronRingPath, AssetDatabase.GetAssetPath(outline.sprite), "링 스프라이트 연결");
        Assert.IsFalse(outline.raycastTarget, "아웃라인이 hover·클릭 영역을 넓히면 안 된다");
        Assert.IsFalse(outline.enabled, "기본 꺼짐");
        Assert.AreEqual(Color.white, outline.color);
    }

    [Test]
    public void Prefab_RingRect_CenteredOnChevron_NativeSize()
    {
        RectTransform rect = ((Image)_hover.Outline).rectTransform;
        Sprite sprite = ((Image)_hover.Outline).sprite;
        Vector2 native = sprite.rect.size * (100f / sprite.pixelsPerUnit);
        Assert.AreEqual(Vector2.zero, rect.anchorMin, "셰브론 rect 에 붙어 늘어난다");
        Assert.AreEqual(Vector2.one, rect.anchorMax);
        Assert.AreEqual(-rect.offsetMax, rect.offsetMin, "사방 같은 여백 = 셰브론 가운데 정렬");
        Assert.AreEqual(native.x, rect.rect.width, 0.01f, "링 rect = 스프라이트 표시 크기(찌그러지지 않는다)");
        Assert.AreEqual(native.y, rect.rect.height, 0.01f);
    }

    [Test]
    public void Enter_ShowsOutline_Exit_HidesOutline()
    {
        _hover.OnPointerEnter(Pointer());
        Assert.IsTrue(_hover.IsShown);
        _hover.OnPointerExit(Pointer());
        Assert.IsFalse(_hover.IsShown);
    }

    [Test]
    public void Enter_NotInteractable_StaysHidden()
    {
        _button.interactable = false;
        _hover.OnPointerEnter(Pointer());
        Assert.IsFalse(_hover.IsShown);
    }

    [Test]
    public void Disable_WhileHovered_HidesOutline()
    {
        _hover.OnPointerEnter(Pointer());
        Assert.IsTrue(_hover.IsShown);

        typeof(UiHoverOutline).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_hover, null);

        Assert.IsFalse(_hover.IsShown, "패널이 닫혀도 켜진 채 남지 않는다");
    }

    [Test]
    public void Locked_Enter_StaysHidden_ButtonStillInteractable()
    {
        _panel.Show(0, true);
        _hover.OnPointerEnter(Pointer());
        Assert.IsFalse(_hover.IsShown, "잠금 중엔 hover 아웃라인을 켜지 않는다");
        Assert.IsTrue(_button.IsInteractable(), "잠금은 interactable 을 끄지 않는다 — 클릭 = Confirm → Locked → 잠금 문구 강조");
    }

    [Test]
    public void Unlocked_WhileHovered_ShowsImmediately()
    {
        _panel.Show(0, true);
        _hover.OnPointerEnter(Pointer());
        Assert.IsFalse(_hover.IsShown);

        _panel.SetLocked(false);
        Assert.IsTrue(_hover.IsShown, "hover 중 잠금이 풀리면 바로 켜진다");
    }

    [Test]
    public void Locked_WhileHovered_HidesImmediately()
    {
        _panel.Show(0, false);
        _hover.OnPointerEnter(Pointer());
        Assert.IsTrue(_hover.IsShown);

        _panel.SetLocked(true);
        Assert.IsFalse(_hover.IsShown, "켜진 채 잠기면 바로 꺼진다");

        _panel.Show(0, false); // 다시 열 때 잠금 해제 = Show 경로도 따른다
        Assert.IsTrue(_hover.IsShown);
    }

    [Test]
    public void RingPng_NeverOverlapsChevronPixels_AndSurroundsRoundLeftTip()
    {
        Texture2D chevron = LoadPng(ChevronPng);
        Texture2D ring = LoadPng(CharacterSelectPanelAuthoring.ChevronRingPath);
        try
        {
            int margin = (ring.width - chevron.width) / 2;
            Assert.AreEqual(chevron.width + margin * 2, ring.width, "링 = 원본 + 사방 같은 여백");
            Assert.AreEqual(chevron.height + margin * 2, ring.height);

            Color32[] src = chevron.GetPixels32();
            Color32[] dst = ring.GetPixels32();
            int overlaps = 0;
            for (int y = 0; y < chevron.height; y++)
            for (int x = 0; x < chevron.width; x++)
                if (src[y * chevron.width + x].a > 0 && dst[(y + margin) * ring.width + x + margin].a > 0)
                    overlaps++;
            Assert.AreEqual(0, overlaps, "링이 셰브론 불투명 픽셀을 가린다");

            // 왼쪽 둥근 끝 — 가운데 줄에서 링 픽셀이 원본 왼쪽 끝보다 왼쪽(여백 안)에 있다
            int row = (chevron.height / 2 + margin) * ring.width;
            bool left = false;
            for (int x = 0; x < margin; x++)
                left |= dst[row + x].a == 255;
            Assert.IsTrue(left, "왼쪽 둥근 끝을 두르는 링이 없다");
        }
        finally
        {
            Object.DestroyImmediate(chevron);
            Object.DestroyImmediate(ring);
        }
    }

    [Test]
    public void RingPng_MatchesRebake_WithDocumentedParameters()
    {
        Texture2D chevron = LoadPng(ChevronPng);
        Texture2D ring = LoadPng(CharacterSelectPanelAuthoring.ChevronRingPath);
        try
        {
            Vector2 display = ((RectTransform)_button.transform).rect.size;
            float scale = chevron.width / display.x;
            float thickness = CharacterSelectPanelAuthoring.ChevronRingThickness * scale;
            float gap = CharacterSelectPanelAuthoring.ChevronRingGap * scale;
            int margin = UiSilhouetteRingBaker.MarginPixels(thickness, gap);
            Color32[] expected = UiSilhouetteRingBaker.BakePixels(chevron.GetPixels32(), chevron.width, chevron.height, thickness, gap, margin);
            Color32[] actual = ring.GetPixels32();
            Assert.AreEqual(expected.Length, actual.Length, "링 PNG 크기가 문서 값으로 다시 구운 것과 다르다 — 굽기 메뉴를 다시 돌릴 것");
            int diff = 0;
            for (int i = 0; i < expected.Length; i++)
                if (expected[i].a != actual[i].a)
                    diff++;
            Assert.AreEqual(0, diff, "링 PNG 가 문서 값(두께·간격)으로 다시 구운 것과 다르다");
        }
        finally
        {
            Object.DestroyImmediate(chevron);
            Object.DestroyImmediate(ring);
        }
    }

    static Texture2D LoadPng(string assetPath)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(assetPath)), assetPath);
        return texture;
    }
}
