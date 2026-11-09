using Ami.BroAudio;
using UnityEngine;

/// <summary>
/// 저체력 경고음(11-09 경석 — 자세한 명세는 추후). <b>내 캐릭터만</b>, 체력 <see cref="threshold"/> 이하에서
/// 심장 소리 루프(<c>S1_Vignetting</c>)를 또렷하게 틀고, 나머지 효과음·BGM·환경음에는 먹먹한 효과(로우패스)를 건다.
/// <para>
/// 심장 소리는 엔티티 타입을 <b>VoiceOver</b> 로 둬서(Sound 시트) 로우패스 대상(Music·SFX·Ambience)에서 빠진다.
/// 체력이 낮을수록 심장 소리가 커진다. 회복해서 기준을 넘으면 서서히, 사망하면 즉시 끈다. 끄기 옵션은 두지 않는다(경석 11-09).
/// </para>
/// <para><c>Player.Awake</c> 가 런타임에 붙인다(프리팹 무수정). 원격 플레이어 인스턴스는 아무것도 안 한다.</para>
/// </summary>
public class PlayerLowHealthAudio : MonoBehaviour
{
    private const string HeartbeatKey = "S1_Vignetting";
    private const BroAudioType MuffledTypes = BroAudioType.Music | BroAudioType.SFX | BroAudioType.Ambience;

    [Tooltip("이 체력 비율 이하에서 켠다(경석 11-09 = 30%)")]
    [SerializeField, Range(0f, 1f)] private float threshold = 0.3f;
    [Tooltip("먹먹한 효과 차단 주파수(Hz) — 낮을수록 더 먹먹하다. 명세 전 임시값")]
    [SerializeField, Min(10f)] private float lowPassFrequency = 1000f;
    [Tooltip("켜고 끌 때 페이드(초)")]
    [SerializeField, Min(0f)] private float fadeTime = 0.5f;
    [Tooltip("심장 소리 볼륨 — 기준 체력에서 / 체력 0 근처에서(시트 볼륨에 곱하지 않는 절대 배율). 명세 전 임시값")]
    [SerializeField] private Vector2 heartbeatVolume = new Vector2(0.5f, 1f);

    private Player _player;
    private PlayerLifeCycleController _lifeCycle;
    private IAudioPlayer _heartbeat;
    private bool _active;

    private void Awake()
    {
        _player = GetComponent<Player>();
        _lifeCycle = GetComponent<PlayerLifeCycleController>();
    }

    private void OnDisable() => Deactivate(0f);

    private void Update()
    {
        // 내 캐릭터만 — 스폰된 남의 캐릭터는 무시(오프라인 테스트는 내 것으로 본다).
        if (_player == null || (_player.IsSpawned && !_player.IsOwner))
        {
            if (_active) Deactivate(0f);
            return;
        }

        bool alive = _lifeCycle == null || _lifeCycle.State == PlayerLifeState.Alive;
        if (!alive)
        {
            if (_active) Deactivate(0f);   // 사망 = 즉시
            return;
        }

        int max = Mathf.Max(1, _player.FinalMaxHp);
        float ratio = Mathf.Clamp01((float)_player.CurrentHealth / max);
        bool low = ratio <= threshold && _player.CurrentHealth > 0;

        if (low && !_active) Activate();
        else if (!low && _active) Deactivate(fadeTime);

        if (_active && _heartbeat != null && _heartbeat.IsActive)
        {
            // 기준 체력 → 0 으로 갈수록 0 → 1
            float severity = threshold > 0f ? 1f - ratio / threshold : 1f;
            _heartbeat.SetVolume(Mathf.Lerp(heartbeatVolume.x, heartbeatVolume.y, severity));
        }
    }

    private void Activate()
    {
        _active = true;
        if (AudioManager.Instance != null)
        {
            _heartbeat = AudioManager.Instance.Play(HeartbeatKey);
        }
        BroAudio.SetEffect(Effect.LowPass(lowPassFrequency, fadeTime), MuffledTypes);
    }

    private void Deactivate(float fade)
    {
        if (!_active) return;
        _active = false;
        if (_heartbeat != null && _heartbeat.IsActive) _heartbeat.Stop(fade);
        _heartbeat = null;
        // OnDisable 은 씬 정리 중에도 불린다 — BroAudio 가 이미 내려갔으면 조용히 넘어간다.
        try { BroAudio.SetEffect(Effect.ResetLowPass(fade), MuffledTypes); }
        catch (System.Exception) { }
    }
}
