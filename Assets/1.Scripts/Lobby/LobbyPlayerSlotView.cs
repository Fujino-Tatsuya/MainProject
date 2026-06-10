using UnityEngine;
using UnityEngine.UI;

public class LobbyPlayerSlotView : MonoBehaviour
{
    [SerializeField] private Image connectedImage;
    [SerializeField] private Image readyImage;
    [SerializeField] private Sprite connectedSprite;
    [SerializeField] private Sprite disconnectedSprite;
    [SerializeField] private Sprite readySprite;
    [SerializeField] private Sprite notReadySprite;
    [SerializeField] private Color connectedColor = new Color(0.25f, 0.85f, 0.45f, 1f);
    [SerializeField] private Color disconnectedColor = new Color(0.25f, 0.25f, 0.25f, 1f);
    [SerializeField] private Color readyColor = new Color(0.25f, 0.65f, 1f, 1f);
    [SerializeField] private Color notReadyColor = new Color(0.85f, 0.25f, 0.25f, 1f);

    public void SetState(bool connected, bool ready)
    {
        SetConnected(connected);
        SetReady(connected && ready);
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
}
