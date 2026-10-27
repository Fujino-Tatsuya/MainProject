/// <summary>
/// 간파 연출 개인 채널(할 일 118) — "이 화면이 간파한 본인일 때만" 트는 연출: 강한 암순응(A2 self), 오너 줌(A3).
/// 값을 들지 않고 부를 때마다 설정을 읽는다.
/// </summary>
public sealed class InterruptPersonalChannel
{
    private readonly IInterruptProductionSettings _settings;

    public InterruptPersonalChannel(IInterruptProductionSettings settings)
    {
        _settings = settings;
    }

    /// <summary>A2 — 본인 화면 암순응(self 강도). 칸이 꺼져 있으면 안 튼다.</summary>
    public void PlayDarkAdaptation(in InterruptProductionEvent e, IInterruptDarkAdaptationOutput darkAdaptation)
    {
        if (darkAdaptation == null || !_settings.Get(InterruptProductionCue.DarkAdaptation))
            return;

        darkAdaptation.Play(true, e.CasterClientId, e.TargetPosition);
    }

    /// <summary>A3 — 오너 줌 배율(카메라가 매 프레임 묻는다). 칸이 꺼져 있으면 1. 내 슬로우인지는 카메라가 따로 본다.</summary>
    public float OwnerZoomFactor(float zoomFactor) => _settings.Get(InterruptProductionCue.OwnerZoom) ? zoomFactor : 1f;
}
