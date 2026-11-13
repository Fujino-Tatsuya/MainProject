using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 패드 모드 UI 첫 선택(할 일 278 2단계, Docs/tech/gamepad-support.md §7).
/// 모달 패널(<see cref="UiModalBlocker"/> 가 붙은 루트)이 켜질 때 등록되고, 패드 모드인 동안 가장 위(마지막에 켜진) 패널 아래에
/// 선택이 없으면 첫 선택을 건다 — 지정한 것(<see cref="Prefer"/>, 최근 것 우선) → 없으면 내비게이션이 켜진 첫 상호작용 가능 Selectable.
/// 마우스 모드에선 아무것도 하지 않고, 마우스 모드로 돌아오는 순간 패널 안 선택을 지운다(각 화면의 예전 '선택 해제' 규칙 그대로).
/// 내비게이션이 None 인 버튼(캐릭터 선택 카드처럼 자기 키 입력을 쓰는 패널)은 고르지 않는다.
/// </summary>
public static class GamepadUiFocus
{
    private struct Root
    {
        public Object Owner;
        public GameObject GameObject;
    }

    private static readonly List<Root> s_roots = new List<Root>();
    private static readonly List<Selectable> s_preferred = new List<Selectable>();
    private static readonly List<Selectable> s_scratch = new List<Selectable>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_roots.Clear();
        s_preferred.Clear();
        s_scratch.Clear();
    }

    /// <summary>패널 루트 등록(켜질 때). 같은 소유자는 한 번만 — 다시 부르면 맨 위로 올린다.</summary>
    public static void Push(Object owner, GameObject root)
    {
        if (owner == null || root == null)
            return;

        Pop(owner);
        s_roots.Add(new Root { Owner = owner, GameObject = root });
    }

    /// <summary>패널 루트 해제(꺼질 때).</summary>
    public static void Pop(Object owner)
    {
        for (int i = s_roots.Count - 1; i >= 0; i--)
        {
            if (s_roots[i].Owner == owner || s_roots[i].Owner == null)
                s_roots.RemoveAt(i);
        }
    }

    /// <summary>
    /// 이 Selectable 을 첫 선택 후보로 맨 앞에 둔다(패널이 화면을 바꿀 때 부른다 — 메뉴·확인창·카드 등).
    /// 패드 모드이고 지금 선택이 이 패널 밖이거나 꺼진 것이면 다음 갱신 때 이것을 고른다.
    /// </summary>
    public static void Prefer(Selectable selectable)
    {
        if (selectable == null)
            return;

        s_preferred.Remove(selectable);
        s_preferred.Insert(0, selectable);
        if (s_preferred.Count > 16)
            s_preferred.RemoveAt(s_preferred.Count - 1);
    }

    /// <summary>
    /// 지정 버튼을 지금 바로 선택한다(패드 모드일 때만). 같은 패널 안에서 화면이 바뀌어 지금 선택이 여전히 유효해도 옮겨야 할 때.
    /// </summary>
    public static void SelectNowIfGamepad(Selectable selectable)
    {
        Prefer(selectable);
        if (!InputDeviceMode.IsGamepad || !IsUsable(selectable))
            return;

        EventSystem es = EventSystem.current;
        if (es != null && !es.alreadySelecting)
            es.SetSelectedGameObject(selectable.gameObject);
    }

    /// <summary>드라이버가 매 프레임 LateUpdate 에서 부른다.</summary>
    internal static void Tick()
    {
        if (!InputDeviceMode.IsGamepad)
            return;

        EventSystem es = EventSystem.current;
        if (es == null || es.alreadySelecting)
            return;

        GameObject top = TopRoot();
        if (top == null)
            return;

        GameObject current = es.currentSelectedGameObject;
        if (current != null && current.activeInHierarchy && current.transform.IsChildOf(top.transform) &&
            current.TryGetComponent(out Selectable currentSelectable) && IsUsable(currentSelectable))
            return;

        Selectable pick = Pick(top);
        if (pick != null)
            es.SetSelectedGameObject(pick.gameObject);
    }

    /// <summary>마우스 모드로 돌아왔다 — 등록된 패널 안 선택을 지운다(호버·클릭 규칙이 다시 맡는다).</summary>
    internal static void HandleModeChanged(InputDeviceKind kind)
    {
        if (kind != InputDeviceKind.MouseKeyboard)
            return;

        EventSystem es = EventSystem.current;
        GameObject current = es != null ? es.currentSelectedGameObject : null;
        if (current == null || es.alreadySelecting)
            return;

        for (int i = 0; i < s_roots.Count; i++)
        {
            GameObject root = s_roots[i].GameObject;
            if (root != null && current.transform.IsChildOf(root.transform))
            {
                es.SetSelectedGameObject(null);
                return;
            }
        }
    }

    /// <summary>순수 규칙 — 패드 첫 선택 후보가 될 수 있는가(켜짐·상호작용 가능·내비게이션 None 아님).</summary>
    public static bool IsUsable(Selectable selectable) =>
        selectable != null && selectable.isActiveAndEnabled && selectable.IsInteractable() &&
        selectable.navigation.mode != Navigation.Mode.None;

    private static GameObject TopRoot()
    {
        for (int i = s_roots.Count - 1; i >= 0; i--)
        {
            GameObject root = s_roots[i].GameObject;
            if (root == null || s_roots[i].Owner == null)
            {
                s_roots.RemoveAt(i);
                continue;
            }

            if (root.activeInHierarchy)
                return root;
        }

        return null;
    }

    private static Selectable Pick(GameObject root)
    {
        for (int i = s_preferred.Count - 1; i >= 0; i--)
        {
            if (s_preferred[i] == null)
                s_preferred.RemoveAt(i);
        }

        for (int i = 0; i < s_preferred.Count; i++)
        {
            Selectable s = s_preferred[i];
            if (IsUsable(s) && s.transform.IsChildOf(root.transform))
                return s;
        }

        root.GetComponentsInChildren(false, s_scratch);
        Selectable first = null;
        for (int i = 0; i < s_scratch.Count; i++)
        {
            if (IsUsable(s_scratch[i]))
            {
                first = s_scratch[i];
                break;
            }
        }

        s_scratch.Clear();
        return first;
    }

    /// <summary>
    /// 가로 한 줄 버튼에 명시 내비게이션(왼·오른쪽, 끝에서 돌아감)을 건다 — 특성 카드·확인창 예/아니오.
    /// 위·아래는 비운다(카드 줄 밖으로 새지 않게).
    /// </summary>
    public static void LinkHorizontal(IReadOnlyList<Selectable> row)
    {
        if (row == null)
            return;

        int count = row.Count;
        for (int i = 0; i < count; i++)
        {
            Selectable s = row[i];
            if (s == null)
                continue;

            Navigation nav = s.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnLeft = count > 1 ? row[(i - 1 + count) % count] : null;
            nav.selectOnRight = count > 1 ? row[(i + 1) % count] : null;
            nav.selectOnUp = null;
            nav.selectOnDown = null;
            s.navigation = nav;
        }
    }
}
