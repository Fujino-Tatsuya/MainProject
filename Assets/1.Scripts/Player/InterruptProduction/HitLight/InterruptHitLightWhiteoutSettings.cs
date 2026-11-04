using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// V2 화이트아웃(할 일 194) 전용 오버레이 칸 — 기존 적중 섬광 오버레이(<see cref="InterruptHitFlashOverlay"/>)를 하나 더 만들어 ③ 화이트아웃 그림만 쓴다.
/// 재질·블러·Volume 칸은 디렉터 값을 그대로 읽고, 효과 칸은 ③ 만 켠다(V0 의 ③ 칸이 꺼져 있어도 V2 화이트아웃은 그린다).
/// </summary>
public sealed class InterruptHitLightWhiteoutSettings : IInterruptHitFlashScreenSettings
{
    private readonly IInterruptHitFlashScreenSettings _inner;

    public InterruptHitLightWhiteoutSettings(IInterruptHitFlashScreenSettings inner)
    {
        _inner = inner;
    }

    public bool IsEffectOn(InterruptPresentationEffect effect) => effect == InterruptPresentationEffect.Whiteout;

    public Material HitFlashOverlayMaterial => _inner?.HitFlashOverlayMaterial;
    public LensFlareDataSRP HitFlashLensFlare => _inner?.HitFlashLensFlare;
    public float HitFlashFlareIntensity => _inner != null ? _inner.HitFlashFlareIntensity : 0f;
    public float HitFlashFlareScale => _inner != null ? _inner.HitFlashFlareScale : 0f;
    public float HitFlashBlurStrength => _inner != null ? _inner.HitFlashBlurStrength : 0f;
    public VolumeProfile HitFlashAberrationProfile => _inner?.HitFlashAberrationProfile;
    public VolumeProfile HitFlashBloomProfile => _inner?.HitFlashBloomProfile;
}
