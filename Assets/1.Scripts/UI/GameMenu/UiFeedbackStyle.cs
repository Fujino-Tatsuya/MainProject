using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ESC 메뉴·설정창 안의 모든 누를 수 있는 것에 같은 반응(<see cref="ButtonLabelStateColor"/>)과 클릭 소리(<see cref="UiClickSound"/>)를 붙인다.
/// 경석 10-26 6차(PLAN-title-start-settings-tabs) — Codex 회의 반영. <see cref="GameMenuController"/> 가 시작할 때 한 번 부른다.
/// </summary>
/// <remarks>
/// 런타임에 붙이는 이유: 설정 패널 전체 재저작(<c>SettingsPanelAuthoring</c>)은 줄을 지웠다 다시 만든다 — 프리팹에만 붙이면 그때 사라진다.
/// 드롭다운 목록 항목은 템플릿에 붙이면 열 때마다 복제본에 따라간다.
/// 🔴 설정 탭은 본 그림 색을 <see cref="SettingsBodyView"/> 가 맡는다(열린 탭은 이미 ×0.55 — 두 번 곱하지 않게) → 소리만 붙인다.
/// 멱등 — 이미 붙어 있으면 설정만 다시 넣는다.
/// </remarks>
public static class UiFeedbackStyle
{
    // 드롭다운 목록 항목 — 검은 목록이라 띠(흰색 알파)로 고름을 보인다. highlighted·selected 를 같게(올린 항목이 곧 선택되므로 — Codex 10-26).
    private static readonly Color ItemBand = new Color(1f, 1f, 1f, 0.14f);
    private static readonly Color ItemPressed = new Color(1f, 1f, 1f, 0.24f);

    public static void Apply(Transform root)
    {
        if (root == null) return;
        var tabs = new HashSet<GameObject>();
        foreach (SettingsBodyView body in root.GetComponentsInChildren<SettingsBodyView>(true))
            body.CollectTabs(tabs);

        foreach (Selectable s in root.GetComponentsInChildren<Selectable>(true))
        {
            if (tabs.Contains(s.gameObject))
            {
                Sound(s, UiClickSound.Trigger.ButtonClick);
                continue;
            }

            switch (s)
            {
                case Toggle t when IsDropdownItem(t, out _):
                    ItemColors(t);   // 띠만 — 글자는 흰색 그대로(경석 10-26 7차: 띠가 있으니 글자색 유지)
                    Sound(t, UiClickSound.Trigger.ClickOrSubmit);
                    break;

                // 클릭 뒤 고름(어둡게)이 다른 곳을 누를 때까지 남지 않게 — 마우스로 누르고 떼면 선택을 푼다. 키보드·패드 선택은 남는다
                // (경석 10-26 7·8차: 토글 → 버튼·키 변경·드롭다운 머리·슬라이더까지).
                case Button b:
                    StandardColors(b);
                    State(b, b.GetComponentInChildren<TMP_Text>(true), Icons(b)).ReleaseSelectionOnMouseUp = true;
                    if (b.GetComponentInParent<KeyRebindRow>(true) == null)   // 키 변경은 입력 대기 시작에서 직접 운다
                        Sound(b, UiClickSound.Trigger.ButtonClick);
                    break;

                case Toggle t:
                    StandardColors(t);
                    State(t, null, Icons(t)).ReleaseSelectionOnMouseUp = true;
                    Sound(t, UiClickSound.Trigger.ToggleChanged);
                    break;

                case TMP_Dropdown d:
                    StandardColors(d);
                    // 소리는 SettingsDropdown 이 목록을 만들 때. 목록을 마우스로 닫으면 TMP 가 머리를 다시 Select() 해서 — 그 해제도 SettingsDropdown.
                    State(d, d.captionText, Icons(d)).ReleaseSelectionOnMouseUp = true;
                    break;

                case Slider sl:
                    StandardColors(sl);
                    State(sl, null, null).ReleaseSelectionOnMouseUp = true;
                    Sound(sl, UiClickSound.Trigger.PointerDown);
                    break;

                case Scrollbar sb:
                    StandardColors(sb);   // 스크롤은 무음
                    State(sb, null, null).ReleaseSelectionOnMouseUp = true;
                    break;
            }
        }
    }

    private static void StandardColors(Selectable s)
    {
        if (s.transition != Selectable.Transition.ColorTint) return;
        ColorBlock c = s.colors;
        c.normalColor = Color.white;
        c.highlightedColor = ButtonLabelStateColor.ChosenTint;
        c.selectedColor = ButtonLabelStateColor.ChosenTint;
        c.pressedColor = ButtonLabelStateColor.PressedTint;
        c.disabledColor = ButtonLabelStateColor.DisabledTint;
        c.colorMultiplier = 1f;
        s.colors = c;
    }

    private static void ItemColors(Selectable s)
    {
        ColorBlock c = s.colors;
        c.highlightedColor = ItemBand;
        c.selectedColor = ItemBand;
        c.pressedColor = ItemPressed;
        s.colors = c;
    }

    private static ButtonLabelStateColor State(Selectable s, TMP_Text label, Graphic[] icons)
    {
        if (!s.TryGetComponent(out ButtonLabelStateColor state))
            state = s.gameObject.AddComponent<ButtonLabelStateColor>();
        state.Configure(label, Color.white, ButtonLabelStateColor.ChosenLabel, icons);
        return state;
    }

    private static void Sound(Selectable s, UiClickSound.Trigger trigger)
    {
        if (!s.TryGetComponent(out UiClickSound sound))
            sound = s.gameObject.AddComponent<UiClickSound>();
        sound.Configure(trigger);
    }

    // 본 그림·글자 말고, 이 컨트롤에 딸린 아이콘(✓/✗·X·화살표). 안쪽 다른 Selectable·드롭다운 템플릿 밑은 뺀다.
    private static Graphic[] Icons(Selectable s)
    {
        var list = new List<Graphic>();
        Transform template = s is TMP_Dropdown d && d.template != null ? d.template : null;
        foreach (Graphic g in s.GetComponentsInChildren<Graphic>(true))
        {
            if (g == s.targetGraphic || g is TMP_Text) continue;
            if (template != null && g.transform.IsChildOf(template)) continue;
            if (g.GetComponentInParent<Selectable>(true) != s) continue;
            list.Add(g);
        }
        return list.ToArray();
    }

    private static bool IsDropdownItem(Toggle t, out TMP_Dropdown owner)
    {
        owner = t.GetComponentInParent<TMP_Dropdown>(true);
        return owner != null && owner.template != null && t.transform.IsChildOf(owner.template);
    }
}
