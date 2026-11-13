using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyPlayerSlotView : MonoBehaviour
{
    [SerializeField] private Image connectedImage;
    [SerializeField] private Image readyImage;
    [SerializeField] private Image portraitImage;
    [Tooltip("이름·캐릭터 표기(예: P1 · 팔라딘). 비우면 표시하지 않는다.")]
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private Sprite connectedSprite;
    [SerializeField] private Sprite disconnectedSprite;
    [SerializeField] private Sprite readySprite;
    [SerializeField] private Sprite notReadySprite;
    [SerializeField] private Color connectedColor = new Color(0.25f, 0.85f, 0.45f, 1f);
    [SerializeField] private Color disconnectedColor = new Color(0.25f, 0.25f, 0.25f, 1f);
    [SerializeField] private Color readyColor = new Color(0.25f, 0.65f, 1f, 1f);
    [SerializeField] private Color notReadyColor = new Color(0.85f, 0.25f, 0.25f, 1f);

    /// <param name="character">초상화·얼굴 크롭 출처. null 이면 빈 초상화.</param>
    public void SetState(bool connected, bool ready, CharacterRoster.Entry character, string label = null)
    {
        SetConnected(connected);
        SetReady(connected && ready);
        SetPortrait(connected, connected ? character : null);
        SetLabel(connected ? label : null);
    }

    private void SetLabel(string label)
    {
        if (labelText == null)
        {
            return;
        }

        labelText.text = label ?? string.Empty;
    }

    private void SetConnected(bool connected)
    {
        if (connectedImage == null)
        {
            return;
        }

        connectedImage.sprite = connected ? connectedSprite : disconnectedSprite;
        connectedImage.color = connected ? connectedColor : disconnectedColor;
        connectedImage.enabled = connectedImage.sprite != null || connectedImage.color.a > 0f;
    }

    private void SetReady(bool ready)
    {
        if (readyImage == null)
        {
            return;
        }

        readyImage.sprite = ready ? readySprite : notReadySprite;
        readyImage.color = ready ? readyColor : notReadyColor;
        readyImage.enabled = readyImage.sprite != null || readyImage.color.a > 0f;
    }

    // 미접속 슬롯은 숨기지 않고 회색 칸으로 둔다 — IsConnect 아이콘 대신 접속 여부를 보여준다.
    // 초상화 부모(PortraitMask)의 RectMask2D 안에서 캐릭터별 크롭으로 얼굴만 보인다(할 일 276 후속).
    private void SetPortrait(bool connected, CharacterRoster.Entry character)
    {
        if (portraitImage == null)
        {
            return;
        }

        portraitImage.sprite = character != null ? character.Portrait : null;
        portraitImage.color = connected ? Color.white : disconnectedColor;
        portraitImage.enabled = true;
        PortraitFaceCrop.Apply(portraitImage, character != null ? character.FaceCrop : PortraitFaceCrop.Full);
    }
}
