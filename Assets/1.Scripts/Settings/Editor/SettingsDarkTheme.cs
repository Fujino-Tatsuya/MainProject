using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 설정 패널 컨트롤을 어두운 색으로 칠한다(팀장 10-13). <b>멱등</b> — 색만 덮어쓴다.
/// 🔴 원인: 드롭다운·버튼·슬라이더가 Unity 기본 <b>흰색</b> 스프라이트였다. 타이틀 CRT 셰이더(밝기 ×1.25 + 글로우)에서
///    흰 면이 번져 그 위 회색 글자가 묻혔다. 흰 면을 없애면 CRT 효과가 남아 있어도 번지지 않는다.
/// 기준색 = 탭 버튼(0.16, 0.19, 0.24). 타이틀·ESC 공통(같은 프리팹).
/// <see cref="SettingsPanelAuthoring"/> 이 탭 내용마다 마지막에 부른다 — 기존 볼륨 슬라이더·Hold/대시 토글도 포함.
/// </summary>
internal static class SettingsDarkTheme
{
    static readonly Color Bg = new Color(0.16f, 0.19f, 0.24f, 0.95f);
    static readonly Color BgHover = new Color(0.25f, 0.30f, 0.38f, 1f);
    static readonly Color BgPressed = new Color(0.11f, 0.13f, 0.17f, 1f);
    static readonly Color BgDisabled = new Color(0.12f, 0.13f, 0.15f, 0.6f);
    static readonly Color ListBg = new Color(0.09f, 0.11f, 0.14f, 0.98f);
    static readonly Color Text = new Color(0.86f, 0.89f, 0.93f, 1f);
    static readonly Color Track = new Color(0.12f, 0.14f, 0.18f, 1f);
    static readonly Color Fill = new Color(0.36f, 0.48f, 0.64f, 1f);
    static readonly Color Handle = new Color(0.72f, 0.76f, 0.82f, 1f);

    internal static void Apply(Transform content)
    {
        foreach (Selectable s in content.GetComponentsInChildren<Selectable>(true))
        {
            switch (s)
            {
                case Slider slider: Slider(slider); break;
                case Scrollbar bar: Tint(bar, Handle, Text); break;
                case Toggle toggle: Toggle(toggle); break;
                default: Tint(s, Bg, BgHover); break;   // 버튼·드롭다운 본체
            }
            foreach (TMP_Text t in s.GetComponentsInChildren<TMP_Text>(true)) t.color = Text;
        }

        foreach (TMP_Dropdown dd in content.GetComponentsInChildren<TMP_Dropdown>(true))
        {
            Image arrow = Child<Image>(dd.transform, "Arrow");
            if (arrow != null) arrow.color = Text;
            if (dd.template != null)
            {
                Image list = dd.template.GetComponent<Image>();
                if (list != null) list.color = ListBg;
            }
        }
    }

    // 버튼류 — 그림은 흰색으로 두고 색은 ColorBlock 이 정한다(상태별 색이 그대로 보이게).
    static void Tint(Selectable s, Color normal, Color hover)
    {
        if (s.targetGraphic != null) s.targetGraphic.color = Color.white;
        ColorBlock c = s.colors;
        c.normalColor = normal;
        c.highlightedColor = hover;
        c.selectedColor = hover;
        c.pressedColor = BgPressed;
        c.disabledColor = BgDisabled;
        c.colorMultiplier = 1f;
        s.colors = c;
    }

    static void Toggle(Toggle t)
    {
        Tint(t, Bg, BgHover);
        if (t.graphic != null) t.graphic.color = Text;   // 체크 표시
        // 드롭다운 목록 항목(Item) — 배경은 목록과 같게, 마우스를 올리면 밝게.
        if (t.name.StartsWith("Item")) Tint(t, ListBg, BgHover);
    }

    static void Slider(Slider s)
    {
        Tint(s, Handle, Color.white);   // 손잡이(targetGraphic)
        Image track = Child<Image>(s.transform, "Background");
        if (track != null) track.color = Track;
        if (s.fillRect != null)
        {
            Image fill = s.fillRect.GetComponent<Image>();
            if (fill != null) fill.color = Fill;
        }
    }

    static T Child<T>(Transform root, string name) where T : Component
    {
        Transform t = SettingsPanelAuthoring.FindDeep(root, name);
        return t != null ? t.GetComponent<T>() : null;
    }
}
