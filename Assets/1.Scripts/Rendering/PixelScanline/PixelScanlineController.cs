using UnityEngine;

// 씬 상주 게이트. PC Renderer는 여러 씬에서 공유되므로 이 컴포넌트가 살아 있는 씬에서만
// 픽셀레이트·스캔라인 패스를 큐잉한다.
[DisallowMultipleComponent]
public sealed class PixelScanlineController : MonoBehaviour
{
    [SerializeField] private PixelScanlineSettings settings;

    private static PixelScanlineController s_active;

    public static PixelScanlineSettings ActiveSettings
    {
        get
        {
            if (s_active == null || !s_active.isActiveAndEnabled)
                return null;

            PixelScanlineSettings active = s_active.settings;
            return active != null && active.HasActiveEffect ? active : null;
        }
    }

    public PixelScanlineSettings Settings => settings;

    public void SetSettings(PixelScanlineSettings newSettings) => settings = newSettings;

    private void OnEnable()
    {
        if (s_active != null && s_active != this)
        {
            Debug.LogWarning(
                $"[PixelScanline] 컨트롤러가 둘 이상이다 — '{s_active.name}'를 '{name}'로 교체한다. " +
                "씬에 하나만 두는 구조다.",
                this);
        }

        s_active = this;

        if (settings == null)
        {
            Debug.LogWarning(
                "[PixelScanline] Settings가 비어 있어 효과가 돌지 않는다. " +
                "PixelScanlineSettings 애셋을 연결할 것.",
                this);
        }
    }

    private void OnDisable()
    {
        if (s_active == this)
            s_active = null;
    }
}
