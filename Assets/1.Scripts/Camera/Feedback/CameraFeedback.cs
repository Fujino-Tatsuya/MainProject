using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public sealed class CameraFeedback : MonoBehaviour
{
    const int ImpulseChannel = 1;

    public static CameraFeedback Instance { get; private set; }
    public static bool RequiresAttributedDamageRpc => Instance != null;

    [Header("피격 쉐이크")]
    [SerializeField, Min(0f)] float receivedHitAmplitude = 0.35f;
    [SerializeField, Min(0.01f)] float receivedHitDuration = 0.2f;
    [SerializeField, Min(0f)] float receivedHitMinInterval = 0.08f;

    [Header("타격 쉐이크")]
    [SerializeField, Min(0f)] float dealtDamageAmplitude = 0.12f;
    [SerializeField, Min(0.01f)] float dealtDamageDuration = 0.08f;
    [SerializeField, Min(0f)] float dealtDamageMinInterval = 0.05f;

    [Header("HP 비네트")]
    [SerializeField, Range(0f, 1f)] float intensityAtFullHp;
    [SerializeField, Range(0f, 1f)] float intensityAtZeroHp = 0.32f;
    [SerializeField, Min(0f)] float vignetteSmoothingPerSecond = 0.8f;
    [SerializeField] float volumePriority = 100f;

    CinemachineImpulseSource _receivedHitImpulse;
    CinemachineImpulseSource _dealtDamageImpulse;
    VolumeProfile _runtimeVolumeProfile;
    Vignette _vignette;
    float _currentVignetteIntensity;
    float _lastReceivedHitTime = float.NegativeInfinity;
    float _lastDealtDamageTime = float.NegativeInfinity;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }

        Instance = this;
        InitializeImpulse();
        InitializeVignette();
    }

    void Update()
    {
        if (_vignette == null)
            return;

        float targetIntensity = intensityAtFullHp;
        Player localPlayer = Player.LocalPlayer;
        if (localPlayer != null && localPlayer.FinalMaxHp > 0)
        {
            float hpRatio = Mathf.Clamp01(
                localPlayer.CurrentHealth / (float)localPlayer.FinalMaxHp);
            targetIntensity = Mathf.Lerp(
                intensityAtFullHp,
                intensityAtZeroHp,
                1f - hpRatio);
        }

        _currentVignetteIntensity = Mathf.MoveTowards(
            _currentVignetteIntensity,
            targetIntensity,
            vignetteSmoothingPerSecond * Time.deltaTime);
        _vignette.intensity.value = _currentVignetteIntensity;
    }

    public void ReportLocalPlayerHit()
    {
        TryGenerateImpulse(
            _receivedHitImpulse,
            receivedHitAmplitude,
            receivedHitMinInterval,
            ref _lastReceivedHitTime);
    }

    public void ReportLocalPlayerDealtDamage()
    {
        TryGenerateImpulse(
            _dealtDamageImpulse,
            dealtDamageAmplitude,
            dealtDamageMinInterval,
            ref _lastDealtDamageTime);
    }

    void InitializeImpulse()
    {
        // ⚠️ 조용히 빠지면 안 된다 — 붙일 GameObject를 잘못 고르면 쉐이크가 영구히 안 나는데
        //    로그가 0줄이라 원인을 못 찾는다. 리스너는 vcam이 아니라 Brain 쪽에 있어야 한다
        //    (vcam에 붙이면 추락·관전 카메라 전환에서 끊긴다).
        if (!TryGetComponent(out CinemachineBrain _))
        {
            Debug.LogWarning(
                "[CameraFeedback] 같은 GameObject에 CinemachineBrain이 없어 카메라 쉐이크를 비활성화합니다. " +
                "이 컴포넌트는 Brain을 든 렌더 카메라(MainCamera 프리팹)에 붙여야 합니다.",
                this);
            return;
        }

        CinemachineExternalImpulseListener listener =
            gameObject.AddComponent<CinemachineExternalImpulseListener>();
        listener.ChannelMask = ImpulseChannel;
        listener.Gain = 1f;
        listener.Use2DDistance = false;
        listener.UseLocalSpace = true;

        _receivedHitImpulse = gameObject.AddComponent<CinemachineImpulseSource>();
        ConfigureImpulseSource(_receivedHitImpulse, receivedHitDuration);

        _dealtDamageImpulse = gameObject.AddComponent<CinemachineImpulseSource>();
        ConfigureImpulseSource(_dealtDamageImpulse, dealtDamageDuration);
    }

    void InitializeVignette()
    {
        if (TryGetComponent(out UniversalAdditionalCameraData cameraData) &&
            !cameraData.renderPostProcessing)
        {
            Debug.LogWarning(
                "[CameraFeedback] HP 비네트를 표시하려면 MainCamera 프리팹에서 " +
                "Post Processing을 켜야 합니다.",
                this);
        }

        Volume volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = volumePriority;
        volume.weight = 1f;

        _runtimeVolumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        _runtimeVolumeProfile.name = "CameraFeedback Runtime Volume";
        _runtimeVolumeProfile.hideFlags = HideFlags.DontSave;
        volume.profile = _runtimeVolumeProfile;

        _vignette = _runtimeVolumeProfile.Add<Vignette>();
        _vignette.intensity.overrideState = true;
        _currentVignetteIntensity = intensityAtFullHp;
        _vignette.intensity.value = _currentVignetteIntensity;
    }

    static void ConfigureImpulseSource(CinemachineImpulseSource source, float duration)
    {
        source.ImpulseDefinition.ImpulseChannel = ImpulseChannel;
        source.ImpulseDefinition.ImpulseShape =
            CinemachineImpulseDefinition.ImpulseShapes.Bump;
        source.ImpulseDefinition.ImpulseDuration = duration;
        source.ImpulseDefinition.ImpulseType =
            CinemachineImpulseDefinition.ImpulseTypes.Uniform;
    }

    void TryGenerateImpulse(
        CinemachineImpulseSource source,
        float amplitude,
        float minInterval,
        ref float lastTriggeredTime)
    {
        if (!isActiveAndEnabled || source == null || amplitude <= 0f)
            return;

        float now = Time.unscaledTime;
        if (now - lastTriggeredTime < minInterval)
            return;

        lastTriggeredTime = now;
        Vector2 randomDirection = Random.insideUnitCircle;
        if (randomDirection.sqrMagnitude <= Mathf.Epsilon)
            randomDirection = Vector2.up;

        randomDirection.Normalize();
        source.GenerateImpulseWithVelocity(
            new Vector3(randomDirection.x, randomDirection.y, 0f) * amplitude);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (_runtimeVolumeProfile != null)
            Destroy(_runtimeVolumeProfile);
    }
}
