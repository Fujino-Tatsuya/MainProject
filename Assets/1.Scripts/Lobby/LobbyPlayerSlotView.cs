using UnityEngine;
using UnityEngine.UI;

public class LobbyPlayerSlotView : MonoBehaviour
{
    [SerializeField] private Image connectedImage;
    [SerializeField] private Image readyImage;
    [SerializeField] private Image portraitImage;
    [SerializeField] private Sprite connectedSprite;
    [SerializeField] private Sprite disconnectedSprite;
    [SerializeField] private Sprite readySprite;
    [SerializeField] private Sprite notReadySprite;
    [SerializeField] private Color connectedColor = new Color(0.25f, 0.85f, 0.45f, 1f);
    [SerializeField] private Color disconnectedColor = new Color(0.25f, 0.25f, 0.25f, 1f);
    [SerializeField] private Color readyColor = new Color(0.25f, 0.65f, 1f, 1f);
    [SerializeField] private Color notReadyColor = new Color(0.85f, 0.25f, 0.25f, 1f);

    public void SetState(bool connected, bool ready, Sprite portrait)
    {
        SetConnected(connected);
        SetReady(connected && ready);
        SetPortrait(connected, connected ? portrait : null);
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
    private void SetPortrait(bool connected, Sprite portrait)
    {
        if (portraitImage == null)
        {
            return;
        }

        portraitImage.sprite = portrait;
        portraitImage.color = connected ? Color.white : disconnectedColor;
        portraitImage.enabled = true;
    }
}
