using UnityEngine;

public class TitleOptionsPanel : MonoBehaviour
{
    [SerializeField] private GameObject gameplayPanel;
    [SerializeField] private GameObject graphicsPanel;
    [SerializeField] private GameObject controlsPanel;
    [SerializeField] private GameObject audioPanel;

    private void OnEnable()
    {
        ShowGameplay();
    }

    public void ShowGameplay()
    {
        ShowOnly(gameplayPanel);
    }

    public void ShowGraphics()
    {
        ShowOnly(graphicsPanel);
    }

    public void ShowControls()
    {
        ShowOnly(controlsPanel);
    }

    public void ShowAudio()
    {
        ShowOnly(audioPanel);
    }

    private void ShowOnly(GameObject activePanel)
    {
        SetActive(gameplayPanel, gameplayPanel == activePanel);
        SetActive(graphicsPanel, graphicsPanel == activePanel);
        SetActive(controlsPanel, controlsPanel == activePanel);
        SetActive(audioPanel, audioPanel == activePanel);
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null)
        {
            target.SetActive(active);
        }
    }
}
