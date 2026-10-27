using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 선택 버튼(할 일 109) — 실제 패널 프리팹을 프리뷰 씬에 올려 버튼 클릭이 Space 와 같은 <see cref="CharacterSelectPanel.Confirm"/> 결과를 내는지 본다.
/// 다른 캐릭터 = Chosen 한 번 · 잠금 = 거부(열린 채 잠금 강조) · 지금 캐릭터 = 그냥 닫힘.
/// EditMode 에서는 Awake 가 돌지 않아 리스너 연결(Awake)을 직접 부른다 — 클릭은 Button.OnPointerClick(실제 마우스 클릭 경로).
/// </summary>
public sealed class CharacterSelectConfirmButtonTests
{
    Scene _scene;
    CharacterSelectPanel _panel;
    Button _button;
    readonly List<int> _chosen = new List<int>();
    readonly List<CharacterSelectCloseReason> _closed = new List<CharacterSelectCloseReason>();

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

        typeof(CharacterSelectPanel).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_panel, null);
        _chosen.Clear();
        _closed.Clear();
        _panel.CharacterChosen += _chosen.Add;
        _panel.Closed += _closed.Add;
    }

    [TearDown]
    public void TearDown()
    {
        if (_panel != null)
            _panel.Hide(); // 정적 열림 수 되돌리기(IsEscapeClaimed)
        EditorSceneManager.ClosePreviewScene(_scene);
    }

    void Click() => _button.OnPointerClick(new PointerEventData(null) { button = PointerEventData.InputButton.Left });

    int OtherAvailableId(int current)
    {
        var roster = (CharacterRoster)new SerializedObject(_panel).FindProperty("roster").objectReferenceValue;
        for (int i = 0; roster != null && i < roster.Count; i++)
            if (i != current && roster.TryGetAvailableCharacter(i, out _))
                return i;
        Assert.Inconclusive("로스터에 지금 캐릭터 말고 고를 수 있는 캐릭터가 없다");
        return -1;
    }

    [Test]
    public void Prefab_ConfirmButton_NavigationNone_WithLabels()
    {
        Assert.AreEqual(Navigation.Mode.None, _button.navigation.mode, "클릭 뒤 EventSystem 선택이 남지 않게");
        var so = new SerializedObject(_panel);
        Assert.IsNotNull(so.FindProperty("confirmLabel").objectReferenceValue);
        Assert.IsNotNull(so.FindProperty("confirmKeyText").objectReferenceValue);
    }

    [Test]
    public void Click_OtherCharacter_ChosenOnce_ThenClosed()
    {
        _panel.Show(0, false);
        int other = OtherAvailableId(0);
        _panel.MoveFocus(other);

        Click();

        CollectionAssert.AreEqual(new[] { other }, _chosen, "Space 와 같이 CharacterChosen 한 번");
        CollectionAssert.AreEqual(new[] { CharacterSelectCloseReason.Chosen }, _closed);
        Assert.IsFalse(_panel.IsOpen);

        Click();
        Assert.AreEqual(1, _chosen.Count, "닫힌 뒤 클릭은 아무것도 안 한다");
        Assert.AreEqual(1, _closed.Count);
    }

    [Test]
    public void Click_Locked_RejectsAndStaysOpen_LikeConfirm()
    {
        _panel.Show(0, true);
        _panel.MoveFocus(OtherAvailableId(0));

        Click();

        Assert.IsEmpty(_chosen);
        Assert.IsEmpty(_closed);
        Assert.IsTrue(_panel.IsOpen);
        float pulseUntil = (float)typeof(CharacterSelectPanel)
            .GetField("_lockedPulseUntil", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_panel);
        Assert.GreaterOrEqual(pulseUntil, 0f, "Space 와 같이 잠금 문구 강조");
        Assert.AreEqual(CharacterSelectConfirmResult.Locked, _panel.Confirm(), "같은 상태의 Confirm 결과도 Locked");
    }

    [Test]
    public void Click_CurrentCharacter_ClosesWithoutChoice()
    {
        _panel.Show(0, false);

        Click();

        Assert.IsEmpty(_chosen);
        CollectionAssert.AreEqual(new[] { CharacterSelectCloseReason.Button }, _closed, "지금 캐릭터 = 뒤로가기와 같은 그냥 닫기");
        Assert.IsFalse(_panel.IsOpen);
    }

    [Test]
    public void Click_WhileClosed_DoesNothing()
    {
        Click();

        Assert.IsEmpty(_chosen);
        Assert.IsEmpty(_closed);
        Assert.IsFalse(_panel.IsOpen);
    }
}
