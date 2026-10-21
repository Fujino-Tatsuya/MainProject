using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 설정창 틀(PLAN-settings-art — SVN r466 UI_Panel 아트) 쪽 동작. 선택 탭 강조와 닫기 버튼만 맡는다.
/// - 탭 4칸 자리는 패널 그림(<c>panel_settings</c>)에 그려져 있고, <b>선택된 탭만</b> 알약 그림을 얹는다.
///   비선택 탭의 그림은 투명으로 두되 클릭은 받는다(이미지를 끄면 레이캐스트도 꺼진다).
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
    [SerializeField] private Color selectedLabelColor = Color.white;
    [SerializeField] private Color normalLabelColor = new Color(0.78f, 0.77f, 0.76f, 1f);

    public Button CloseButton => closeButton;

    private void OnEnable() => Refresh();

    /// <summary>지금 켜진 내용에 맞춰 탭 강조를 옮긴다. <see cref="TitleOptionsPanel"/> 이 탭을 바꿀 때 부른다.</summary>
    public void Refresh()
    {
        for (int i = 0; i < contents.Length; i++)
        {
            bool selected = contents[i] != null && contents[i].activeSelf;
            if (i < tabBackgrounds.Length && tabBackgrounds[i] != null)
                tabBackgrounds[i].color = selected ? Color.white : Color.clear;
            if (i < tabLabels.Length && tabLabels[i] != null)
                tabLabels[i].color = selected ? selectedLabelColor : normalLabelColor;
        }
    }
}
