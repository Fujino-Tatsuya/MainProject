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
    public void Chevron_Ring_ThicknessIs7px()
    {
        Assert.AreEqual(7f, CharacterSelectPanelAuthoring.ChevronRingThickness, "링 두께(화면 px) — PNG 대조는 CharacterSelectChevronHoverOutlineTests");
        Assert.AreEqual(3f, CharacterSelectPanelAuthoring.ChevronRingGap, "간격은 그대로");
    }
}
