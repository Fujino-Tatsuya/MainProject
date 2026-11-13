using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

/// <summary>할 일 278 2단계 — 패드 UI 첫 선택 후보 규칙·가로 명시 내비게이션(특성 카드·확인창).</summary>
public sealed class GamepadUiFocusTests
{
    readonly List<GameObject> created = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in created)
            if (go != null) Object.DestroyImmediate(go);
        created.Clear();
    }

    Button NewButton(string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        created.Add(go);
        return go.GetComponent<Button>();
    }

    [Test]
    public void IsUsable_RejectsNullNavigationNoneAndNonInteractable()
    {
        Assert.IsFalse(GamepadUiFocus.IsUsable(null));

        Button none = NewButton("None");
        none.navigation = new Navigation { mode = Navigation.Mode.None }; // 캐릭터 선택 카드처럼 자기 키 입력을 쓰는 버튼
        Assert.IsFalse(GamepadUiFocus.IsUsable(none));

        Button disabled = NewButton("Disabled");
        disabled.interactable = false;
        Assert.IsFalse(GamepadUiFocus.IsUsable(disabled));

        Button hidden = NewButton("Hidden");
        hidden.gameObject.SetActive(false);
        Assert.IsFalse(GamepadUiFocus.IsUsable(hidden));
    }

    [Test]
    public void LinkHorizontal_WrapsLeftRight_ClearsUpDown()
    {
        Button a = NewButton("A"), b = NewButton("B"), c = NewButton("C");
        GamepadUiFocus.LinkHorizontal(new Selectable[] { a, b, c });

        Assert.AreEqual(Navigation.Mode.Explicit, a.navigation.mode);
        Assert.AreSame(c, a.navigation.selectOnLeft);
        Assert.AreSame(b, a.navigation.selectOnRight);
        Assert.AreSame(a, b.navigation.selectOnLeft);
        Assert.AreSame(c, b.navigation.selectOnRight);
        Assert.AreSame(b, c.navigation.selectOnLeft);
        Assert.AreSame(a, c.navigation.selectOnRight);
        Assert.IsNull(b.navigation.selectOnUp);
        Assert.IsNull(b.navigation.selectOnDown);
    }

    [Test]
    public void LinkHorizontal_SingleButton_HasNoNeighbours()
    {
        Button only = NewButton("Only");
        GamepadUiFocus.LinkHorizontal(new Selectable[] { only });
        Assert.AreEqual(Navigation.Mode.Explicit, only.navigation.mode);
        Assert.IsNull(only.navigation.selectOnLeft);
        Assert.IsNull(only.navigation.selectOnRight);
    }
}
