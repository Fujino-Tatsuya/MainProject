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
/// EditMode 에서는 OnDisable 이 돌지 않아 직접 부른다.
/// </summary>
public sealed class CharacterSelectChevronHoverOutlineTests
{
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
    public void TearDown() => EditorSceneManager.ClosePreviewScene(_scene);

    static PointerEventData Pointer() => new PointerEventData(null);

    [Test]
    public void Prefab_OutlineWired_OffByDefault_NotRaycastTarget()
    {
        var outline = _hover.Outline as UiTriangleOutline;
        Assert.IsNotNull(outline, "아웃라인 = 셰브론 자식 UiTriangleOutline");
        Assert.AreSame(_button.transform, outline.transform.parent);
        Assert.AreSame(_button.GetComponent<UiTriangleRaycastFilter>(), outline.Shape, "선 모양 = 클릭 삼각형");
        Assert.IsFalse(outline.raycastTarget, "아웃라인이 hover·클릭 영역을 넓히면 안 된다");
        Assert.IsFalse(outline.enabled, "기본 꺼짐");
        Assert.AreEqual(Color.white, outline.color);
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
    public void Outline_DrawsOutsideTriangleOnly()
    {
        var outline = (UiTriangleOutline)_hover.Outline;
        outline.enabled = true;
        var vh = new VertexHelper();
        typeof(Graphic).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(VertexHelper) }, null)
            .Invoke(outline, new object[] { vh });
        Assert.AreEqual(12, vh.currentVertCount, "변 3개 × 사각형");

        var shape = outline.Shape;
        var rect = (RectTransform)shape.transform;
        // 아웃라인 = 셰브론 직계 자식 — 저장된 Canvas 루트는 scale 0 이라 월드 좌표 대신 로컬 TRS 로 되돌린다
        Transform child = outline.transform;
        Matrix4x4 toShape = Matrix4x4.TRS(child.localPosition, child.localRotation, child.localScale);
        var vertex = new UIVertex();
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref vertex, i);
            Vector2 local = toShape.MultiplyPoint3x4(vertex.position);
            Assert.IsFalse(UiTriangleRaycastFilter.Contains(local, rect.rect, shape.VertexA, shape.VertexB, shape.VertexC),
                $"정점 {i} 가 셰브론 삼각형 안 — 셰브론을 가린다");
        }
        vh.Dispose();
    }
}
