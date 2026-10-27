using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 설정창 틀(PLAN-settings-art — SVN r466 UI_Panel 아트) 쪽 동작. 선택 탭 강조와 닫기 버튼만 맡는다.
/// - 탭 4개 모두 테두리 있는 알약 그림을 보인다. <b>선택된 탭만 색을 곱해 어둡게 + 글자를 흐리게</b>,
///   나머지(누를 수 있는 탭)는 원래 알약 + 흰 글자로 밝게(경석 10-26 — PLAN-title-start-settings-tabs B안).
/// - 마우스 올림·키보드 선택 = ESC 메뉴 버튼과 같다(경석 10-26 "메뉴 hover 로 통일"): 알약 0.55 + 회색 글자.
///   ⚠️ 탭 Button 의 ColorTint 는 Image.color 와 따로 곱해진다 — 선택된 탭은 이미 0.55 라 그 탭의 hover·선택 색은 흰색(=그대로)으로
///   둬서 두 번 어두워지지 않게 한다. 글자는 <see cref="ButtonLabelStateColor"/>(Pinned = 선택된 탭).
/// - 닫기는 이 프리팹이 직접 닫지 않는다 — 타이틀(<see cref="TitleFlowDirector"/>)·ESC(<see cref="MapSceneManager"/>)가
///   <see cref="CloseButton"/> 에 각자 닫기를 연결한다(열고 닫는 주체는 각 씬).
/// 배치·참조는 저작 메뉴(<c>SettingsArtSkin</c>)가 채운다.
/// </summary>
public sealed class SettingsBodyView : MonoBehaviour
{
    [SerializeField] private Button closeButton;
    [Tooltip("탭 순서: 게임플레이·그래픽·조작·오디오. contents 와 같은 순서.")]
    [SerializeField] private Image[] tabBackgrounds = new Image[4];
    [SerializeField] private TMP_Text[] tabLabels = new TMP_Text[4];
    [SerializeField] private GameObject[] contents = new GameObject[4];
    [Tooltip("선택된 탭 알약에 곱하는 색 — 어둡게.")]
    [SerializeField] private Color selectedTabTint = new Color(0.55f, 0.55f, 0.55f, 1f);
    [Tooltip("선택된 탭 글자 — 흐리게. ESC 메뉴 버튼도 이 규칙을 따른다(경석 10-26: 순백 = 누를 수 있음, 회색 = 선택됨).")]
    [SerializeField] private Color selectedTabLabelColor = new Color(0.51f, 0.5f, 0.49f, 1f);
    [Tooltip("나머지(누를 수 있는) 탭 글자 — 순백이 기준(ESC 메뉴 버튼 글자도 순백).")]
    [SerializeField] private Color tabLabelColor = Color.white;

    public Button CloseButton => closeButton;

    private void OnEnable() => Refresh();

    /// <summary>탭 버튼 오브젝트들 — <see cref="UiFeedbackStyle"/> 가 이 탭들의 색을 덮지 않게(열린 탭 두 번 곱하기 방지).</summary>
    public void CollectTabs(System.Collections.Generic.ISet<GameObject> into)
    {
        foreach (Image bg in tabBackgrounds)
            if (bg != null) into.Add(bg.gameObject);
    }

    /// <summary>지금 켜진 내용에 맞춰 탭 강조를 옮긴다. <see cref="TitleOptionsPanel"/> 이 탭을 바꿀 때 부른다.</summary>
    public void Refresh()
    {
        for (int i = 0; i < contents.Length; i++)
        {
            bool selected = contents[i] != null && contents[i].activeSelf;
            if (i < tabBackgrounds.Length && tabBackgrounds[i] != null)
            {
                Image bg = tabBackgrounds[i];
                bg.color = selected ? selectedTabTint : Color.white;
                if (bg.TryGetComponent(out Button button))
                {
                    ColorBlock c = button.colors;
                    c.highlightedColor = selected ? Color.white : selectedTabTint;
                    c.selectedColor = selected ? Color.white : selectedTabTint;
                    c.pressedColor = selected ? Color.white : PressedTint;
                    button.colors = c;
                }
            }

            if (i < tabLabels.Length && tabLabels[i] != null)
            {
                TMP_Text label = tabLabels[i];
                ButtonLabelStateColor state = TabLabelState(i, label);
                if (state != null) state.Pinned = selected;
                else label.color = selected ? selectedTabLabelColor : tabLabelColor;
            }
        }
    }

    // ESC 메뉴 버튼(PauseMenuAuthoring)과 같은 누름 색.
    private static readonly Color PressedTint = new Color(0.45f, 0.45f, 0.45f, 1f);

    // 탭 버튼(알약 오브젝트)에 글자 상태 컴포넌트를 붙인다 — 프리팹을 다시 굽지 않게 런타임에.
    private ButtonLabelStateColor TabLabelState(int i, TMP_Text label)
    {
        if (i >= tabBackgrounds.Length || tabBackgrounds[i] == null) return null;
        GameObject tab = tabBackgrounds[i].gameObject;
        if (!tab.TryGetComponent(out ButtonLabelStateColor state))
        {
            state = tab.AddComponent<ButtonLabelStateColor>();
            state.Configure(label, tabLabelColor, selectedTabLabelColor);
        }
        return state;
    }
}
