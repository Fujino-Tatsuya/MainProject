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
}
