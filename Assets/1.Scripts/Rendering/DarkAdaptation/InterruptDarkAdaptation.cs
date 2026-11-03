using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 간파 성공 암순응 연출 — 화면의 어두운 픽셀을 잠깐 낮춘다. 할 일 87 C3.
/// NetworkManager.prefab 의 <see cref="GlobalTimeScale"/> 옆에 붙는다. 그리기는 <see cref="DarkAdaptationFeature"/>.
/// 수동 실행자다(할 일 118) — 언제·누구 화면 강도로 틀지는 <see cref="InterruptProductionDirector"/> 가 정해 <see cref="Play"/>·<see cref="Stop"/> 을 부른다.
///
/// <list type="bullet">
/// <item>발동자 본인 화면은 <see cref="selfIntensity"/>, 다른 사람 화면은 <see cref="othersIntensity"/>.</item>
/// <item>곡선은 실시간(unscaled) — 슬로우 배율과 무관하게 같은 길이로 보인다.</item>
/// <item>강제 리셋·세션 종료·비활성화면 즉시 0.</item>
/// <item>화면 전체에 고르게 건다 — 대상 주변을 살리는 원(옛 revealCenter·revealRadius·revealSoftness)은 11-03 삭제.</item>
/// </list>
/// </summary>
[RequireComponent(typeof(GlobalTimeScale))]
[DataTableSheet("SlowMotion", Order = 2)]
public sealed class InterruptDarkAdaptation : MonoBehaviour, IInterruptDarkAdaptationOutput, ISuppressDarkAdaptOutput
{
    [Tooltip("간파 성공 발동자 본인 화면의 암순응 강도 [0, 1]")]
    [SerializeField, Range(0f, 1f)] private float selfIntensity = 1f;
    [Tooltip("다른 사람 화면의 암순응 강도 [0, 1]")]
    [SerializeField, Range(0f, 1f)] private float othersIntensity = 0.35f;
    [Tooltip("최대 강도까지 올라가는 실시간 초")]
    [SerializeField, Min(0f)] private float fadeInDuration = 0.08f;
    [Tooltip("최대 강도 유지 실시간 초")]
    [SerializeField, Min(0f)] private float holdDuration = 0.6f;
    [Tooltip("0 으로 돌아오는 실시간 초")]
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.5f;
    [Tooltip("이 휘도(HDR)보다 어두운 픽셀만 어둡힌다 — 빛 이펙트·발광은 남는다")]
    [SerializeField, Min(0f)] private float luminanceThreshold = 0.8f;
    [Tooltip("임계 아래로 부드럽게 섞는 휘도 폭")]
    [SerializeField, Min(0f)] private float thresholdSoftness = 0.3f;
    [Tooltip("강도 1 에서 어두운 픽셀을 낮추는 최대 비율 [0, 1]")]
    [SerializeField, Range(0f, 1f)] private float maxDarken = 0.85f;

    private readonly InterruptDarkAdaptationModel _model = new InterruptDarkAdaptationModel();

    private NetworkManager _networkManager;

    private void Awake()
    {
        _networkManager = GetComponent<NetworkManager>();
    }

    private void OnDisable()
    {
        StopEffect();
    }

    /// <summary>
    /// 간파 성공 암순응을 튼다(디렉터가 부른다 — 칸·성공 여부는 이미 걸렀다). 본인 화면이면 self 강도, 아니면 others 강도.
    /// 수치는 부르는 순간 필드에서 읽는다.
    /// </summary>
    public void Play(bool viewerIsCaster)
    {
        // 꺼진 컴포넌트는 LateUpdate 가 안 돌아 강도가 남는다 — 옛 구독(OnEnable 동안만)과 같게 무시한다.
        if (!isActiveAndEnabled || _networkManager == null)
            return;

        float peak = InterruptDarkAdaptationModel.SelectIntensity(viewerIsCaster, selfIntensity, othersIntensity);
        _model.Play(peak, fadeInDuration, holdDuration, fadeOutDuration);
        PushState();
    }

    /// <summary>즉시 0(강제 리셋·세션 종료).</summary>
    public void Stop() => StopEffect();

    // ---- 제압 암순응(할 일 192 Q7) — 제압 간파 섬광 진행기가 부른다. 모든 화면 같은 값(Q8 공용) ----

    /// <summary>제압 암순응 컷 — 곡선을 멈추고 이 어둡힘을 그대로 붙잡는다. 임계 칸은 이 컴포넌트 값.</summary>
    public void ShowDarkAdaptCut(float darkness)
    {
        if (!isActiveAndEnabled)
            return;

        _model.Stop();
        PushState(InterruptDarkAdaptationModel.SanitizeIntensity(darkness));
    }

    /// <summary>컷 끝·강제 리셋 — 즉시 0.</summary>
    public void ClearDarkAdapt() => StopEffect();

    private void LateUpdate()
    {
        if (!_model.IsPlaying)
            return;

        if (_networkManager == null || !_networkManager.IsListening)
        {
            StopEffect();
            return;
        }

        _model.Tick(Time.unscaledDeltaTime);
        PushState();
    }

    private void PushState() => PushState(_model.Value);

    private void PushState(float intensity)
    {
        DarkAdaptationState.Intensity = intensity;
        DarkAdaptationState.LuminanceThreshold = InterruptDarkAdaptationModel.SanitizeDuration(luminanceThreshold);
        DarkAdaptationState.ThresholdSoftness = InterruptDarkAdaptationModel.SanitizeDuration(thresholdSoftness);
        DarkAdaptationState.MaxDarken = InterruptDarkAdaptationModel.SanitizeIntensity(maxDarken);
    }

    private void StopEffect()
    {
        _model.Stop();
        DarkAdaptationState.Clear();
    }
}
