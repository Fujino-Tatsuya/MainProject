using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 간파 성공 암순응 연출 — <see cref="GlobalTimeScale.Started"/> 가 간파 성공이면 화면의 어두운 픽셀을 잠깐 낮춘다.
/// NetworkManager.prefab 의 <see cref="GlobalTimeScale"/> 옆에 붙는다. 그리기는 <see cref="DarkAdaptationFeature"/>. 할 일 87 C3.
///
/// <list type="bullet">
/// <item>발동자 본인 화면은 <see cref="selfIntensity"/>, 다른 사람 화면은 <see cref="othersIntensity"/>.</item>
/// <item>곡선은 실시간(unscaled) — 슬로우 배율과 무관하게 같은 길이로 보인다.</item>
/// <item>강제 리셋·세션 종료·비활성화면 즉시 0.</item>
/// <item>원형 중심 = <see cref="revealCenter"/>(기본 Target = 신호의 대상 위치, Caster = 발동자 추적, Midpoint = 중간).
///       매 프레임 메인 카메라 뷰포트로 바꾼다. 못 찾거나 카메라 뒤면 원형 끔.</item>
/// </list>
/// </summary>
[RequireComponent(typeof(GlobalTimeScale))]
[DataTableSheet("SlowMotion", Order = 2)]
public sealed class InterruptDarkAdaptation : MonoBehaviour
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
    [Tooltip("살리는 원의 중심 — Target = 카운터 창을 소비한 대상 위치, Caster = 발동자 플레이어 추적, Midpoint = 둘의 중간(시전자 못 찾으면 대상)")]
    [SerializeField] private RevealCenterMode revealCenter = RevealCenterMode.Target;
    [Tooltip("중심 주변을 살리는 원 반경(화면 높이 비율). 0 = 원형 끔")]
    [SerializeField, Min(0f)] private float revealRadius = 0.18f;
    [Tooltip("원 가장자리 부드러움(화면 높이 비율)")]
    [SerializeField, Min(0f)] private float revealSoftness = 0.1f;

    private readonly InterruptDarkAdaptationModel _model = new InterruptDarkAdaptationModel();

    private GlobalTimeScale _timeScale;
    private NetworkManager _networkManager;
    private ulong _triggerClientId;
    private Transform _triggerPlayer;
    private Vector3 _targetPosition;

    private void Awake()
    {
        _timeScale = GetComponent<GlobalTimeScale>();
        _networkManager = GetComponent<NetworkManager>();
    }

    private void OnEnable()
    {
        if (_timeScale == null)
            return;

        _timeScale.Started += HandleStarted;
        _timeScale.ForcedReset += HandleForcedReset;
    }

    private void OnDisable()
    {
        if (_timeScale != null)
        {
            _timeScale.Started -= HandleStarted;
            _timeScale.ForcedReset -= HandleForcedReset;
        }

        StopEffect();
    }

    private void HandleStarted(uint triggerId, ulong triggerClientId, SlowMotionPurpose purpose, Vector3 targetPosition)
    {
        if (purpose != SlowMotionPurpose.Success || _networkManager == null)
            return;
        if (!InterruptPresentationToggles.IsOn(InterruptPresentationKind.DarkAdaptation))
            return;

        bool triggeredByMe = triggerClientId == _networkManager.LocalClientId;
        float peak = InterruptDarkAdaptationModel.SelectIntensity(triggeredByMe, selfIntensity, othersIntensity);
        _model.Play(peak, fadeInDuration, holdDuration, fadeOutDuration);

        if (_triggerClientId != triggerClientId)
            _triggerPlayer = null;
        _triggerClientId = triggerClientId;
        _targetPosition = targetPosition;

        PushState();
    }

    private void HandleForcedReset(uint triggerId) => StopEffect();

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

    private void PushState()
    {
        DarkAdaptationState.Intensity = _model.Value;
        DarkAdaptationState.LuminanceThreshold = InterruptDarkAdaptationModel.SanitizeDuration(luminanceThreshold);
        DarkAdaptationState.ThresholdSoftness = InterruptDarkAdaptationModel.SanitizeDuration(thresholdSoftness);
        DarkAdaptationState.MaxDarken = InterruptDarkAdaptationModel.SanitizeIntensity(maxDarken);
        DarkAdaptationState.RevealSoftness = InterruptDarkAdaptationModel.SanitizeDuration(revealSoftness);

        float radius = InterruptDarkAdaptationModel.SanitizeRadius(revealRadius);
        if (radius > 0f && TryGetRevealViewport(out Vector2 viewport))
        {
            DarkAdaptationState.RevealCenter = viewport;
            DarkAdaptationState.RevealRadius = radius;
        }
        else
        {
            DarkAdaptationState.RevealRadius = 0f;
        }
    }

    private bool TryGetRevealViewport(out Vector2 viewport)
    {
        viewport = default;

        Camera camera = Camera.main;
        if (camera == null)
            return false;

        // Target 모드는 시전자를 찾지 않는다.
        Transform player = revealCenter != RevealCenterMode.Target ? FindTriggerPlayer() : null;
        bool hasCaster = player != null;
        if (!InterruptDarkAdaptationModel.TrySelectRevealCenter(revealCenter, _targetPosition, hasCaster,
                hasCaster ? player.position : default, out Vector3 center))
            return false;

        Vector3 point = camera.WorldToViewportPoint(center);
        if (point.z <= 0f || float.IsNaN(point.x) || float.IsNaN(point.y))
            return false;

        viewport = new Vector2(point.x, point.y);
        return true;
    }

    // SpawnManager.GetPlayerNetworkObject 는 클라에서 남의 것을 찾으면 에러를 찍는다 — 스폰 목록에서 직접 찾는다.
    private Transform FindTriggerPlayer()
    {
        if (_triggerPlayer != null)
            return _triggerPlayer;

        NetworkSpawnManager spawnManager = _networkManager != null ? _networkManager.SpawnManager : null;
        if (spawnManager == null)
            return null;

        foreach (NetworkObject networkObject in spawnManager.SpawnedObjectsList)
        {
            if (networkObject != null && networkObject.IsPlayerObject && networkObject.OwnerClientId == _triggerClientId)
            {
                _triggerPlayer = networkObject.transform;
                break;
            }
        }

        return _triggerPlayer;
    }

    private void StopEffect()
    {
        _model.Stop();
        _triggerPlayer = null;
        DarkAdaptationState.Clear();
    }
}
