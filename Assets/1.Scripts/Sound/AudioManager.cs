using System.Collections.Generic;
using UnityEngine;
using Ami.BroAudio;

/// <summary>
/// 사운드 재생·BGM·볼륨의 중앙 창구(싱글톤). 기존 FMOD의 SoundManager를 대체한다.
/// BroAudio 파사드(<see cref="BroAudio"/>) 위에 게임 의미의 메서드를 얹기만 하며,
/// 씬↔BGM 전환은 <see cref="SceneBgmSwitcher"/>가 담당한다.
///
/// 이름은 BroAudio 내부의 Ami.BroAudio.Runtime.SoundManager와 혼동을 피하려 AudioManager로 둔다.
/// (BroAudio 런타임은 최초 사용 시 자동 초기화되므로 별도 초기화 호출은 불필요하다.)
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Catalog")]
    [Tooltip("모든 SoundID를 모아둔 SoundCatalog 에셋")]
    [SerializeField] private SoundCatalog catalog;

    [Header("플레이어 소리 (PLAN-sound-mix Q6)")]
    [Tooltip("다른 플레이어의 스킬·공격·발소리·대시 볼륨 배율. 내 소리는 1. 팀 합의 11-09 = 0.3")]
    [SerializeField, Range(0f, 1f)] private float remotePlayerVolume = 0.3f;
    [Tooltip("이 접두사로 시작하는 키는 '플레이어 소리'로 본다 — 주인을 모르는 VFX 원샷은 재생 위치에서 가장 가까운 플레이어로 주인을 판정한다")]
    [SerializeField] private string[] playerSoundKeyPrefixes = { "S1_Paladin", "S1_Gunner", "S1_Assassin" };
    [Tooltip("위치로 주인을 찾을 때 플레이어까지 허용 거리(m). 이 안에 내 캐릭터가 있으면 내 소리로 본다")]
    [SerializeField, Min(0f)] private float ownerSearchRadius = 2f;

    [Header("BGM")]
    [SerializeField] private Transition bgmTransition = Transition.CrossFade;
    [Tooltip("BGM 페이드 인 / 크로스페이드 시간(초)")]
    [SerializeField, Min(0f)] private float bgmFadeTime = 1.5f;

    /// <summary>중앙 사운드 카탈로그. 재생은 키로 한다 — 예: AudioManager.Instance.Play("UI_Click")</summary>
    public SoundCatalog Catalog => catalog;

    // BroAudioType별 현재 볼륨(0~10, 1=원음). 게임 시작 시 전부 1f로 초기화. PlayerPrefs 미사용.
    private readonly Dictionary<BroAudioType, float> _volumes = new Dictionary<BroAudioType, float>();
    private readonly SoundPlaybackLimiter _playbackLimiter = new SoundPlaybackLimiter();
    private SoundCatalogLookup _catalogLookup;
    private SoundCatalog _lookupSource;
    // VFX 소리(SoundID 직접 재생)를 시트 키로 되찾는 역조회 — SoundID 동등성 = 엔티티 참조(PLAN-sound-mix 1단계)
    private readonly Dictionary<SoundID, SoundEntry> _entryById = new Dictionary<SoundID, SoundEntry>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Edit.LogWarning("[AudioManager] 중복 생성이 감지되어 파괴합니다.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        InitVolumes();
        SoundConfig.ApplyTo(this);   // 저장된 설정 볼륨(없으면 원음 그대로) — PLAN-settings

        if (catalog == null)
        {
            Edit.LogWarning("[AudioManager] SoundCatalog가 연결되지 않았습니다. 인스펙터에서 연결하세요.", this);
        }
        else
        {
            RebuildCatalogLookup();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    #region 재생 (One-Shot)

    /// <summary>
    /// 카탈로그 키로 2D 사운드를 재생한다. 3D 엔트리는 위치가 필요하므로 재생하지 않는다.
    /// </summary>
    public IAudioPlayer Play(string key)
    {
        if (!TryGetEntry(key, out SoundEntry entry))
        {
            return null;
        }

        if (entry.Is3D)
        {
            Edit.LogWarning($"[AudioManager] 3D 사운드 키 '{key}'는 위치를 지정해 재생해야 합니다.", this);
            return null;
        }

        return PlayEntry(entry, null);
    }

    /// <summary>
    /// 카탈로그 키로 사운드를 재생한다. 3D 엔트리는 지정 위치를 사용하고, 2D 엔트리는 위치를 무시한다.
    /// </summary>
    public IAudioPlayer Play(string key, Vector3 worldPos)
    {
        if (!TryGetEntry(key, out SoundEntry entry))
        {
            return null;
        }

        return PlayEntry(entry, entry.Is3D ? worldPos : (Vector3?)null);
    }

    /// <summary>
    /// 주인이 있는 소리(발소리·대시·스킬). <paramref name="source"/> 가 다른 플레이어 소유면
    /// <see cref="remotePlayerVolume"/> 을 곱한다(PLAN-sound-mix Q6).
    /// </summary>
    public IAudioPlayer Play(string key, Vector3 worldPos, Component source)
    {
        if (!TryGetEntry(key, out SoundEntry entry))
        {
            return null;
        }

        return PlayEntry(entry, entry.Is3D ? worldPos : (Vector3?)null, VolumeForSource(source));
    }

    /// <summary>위치 무관(2D/전역) 사운드를 한 번 재생한다. 카탈로그에 있으면 시트 설정을 적용한다.</summary>
    public IAudioPlayer PlayOneShot(SoundID id)
    {
        if (!id.IsValid())
        {
            Edit.LogWarning($"[AudioManager] 유효하지 않은 SoundID 재생 시도. ({id})");
            return null;
        }

        if (TryGetEntry(id, out SoundEntry entry))
        {
            if (entry.Is3D)
            {
                Edit.LogWarning($"[AudioManager] 3D 사운드 '{entry.Key}'를 위치 없이 재생 — 2D 로 낸다.", this);
            }
            return PlayEntry(entry, null);
        }

        return BroAudio.Play(id);
    }

    /// <summary>월드 좌표에서 3D 사운드를 한 번 재생한다. 카탈로그에 있으면 시트 설정을 적용한다.</summary>
    public IAudioPlayer PlayOneShot(SoundID id, Vector3 worldPos) => PlayOneShot(id, worldPos, null);

    /// <summary>
    /// VFX 소리 재생 창구(<c>EffectManager.Fire</c>). 카탈로그에 있으면 시트의 볼륨·동시 수·3D 거리·피치를 적용하고,
    /// 플레이어 소리면 주인 판정으로 다른 플레이어 배율을 곱한다. <paramref name="sourceHint"/> = 따라가는 대상(소켓 루프) — 없으면 위치로 찾는다.
    /// 카탈로그에 없는 SoundID 는 예전처럼 BroAudio 로 바로 낸다.
    /// </summary>
    public IAudioPlayer PlayOneShot(SoundID id, Vector3 worldPos, Transform sourceHint)
    {
        if (!id.IsValid())
        {
            Edit.LogWarning($"[AudioManager] 유효하지 않은 SoundID 재생 시도. ({id})");
            return null;
        }

        if (!TryGetEntry(id, out SoundEntry entry))
        {
            return BroAudio.Play(id, worldPos);
        }

        float scale = 1f;
        if (IsPlayerSoundKey(entry.Key))
        {
            Player owner = sourceHint != null ? sourceHint.GetComponentInParent<Player>() : null;
            if (owner == null)
            {
                // 위치만 아는 원샷 — 가장 가까운 플레이어가 주인. 내 캐릭터가 반경 안이면 내 소리로 본다(겹쳐 서 있을 때 100%).
                Player nearest = PlayerSoundRegistry.Nearest(worldPos, ownerSearchRadius, out bool localIsNear);
                if (!localIsNear) owner = nearest;
            }
            scale = VolumeForPlayer(owner);
        }

        return PlayEntry(entry, entry.Is3D ? worldPos : (Vector3?)null, scale);
    }

    /// <summary>
    /// 이 키로 재생 중인 소리를 전부 끈다(이 피어). 패턴이 끊길 때(카운터·그로기·사망) 남는 공격 소리 정리용.
    /// 동시 수 제한은 Stop → OnEnd 가 반납한다.
    /// </summary>
    public void StopKey(string key, float fadeOut = 0.1f)
    {
        if (TryGetSound(key, out SoundID id)) BroAudio.Stop(id, fadeOut);
    }

    /// <summary>키의 SoundID(BGM 전환 캐시 등). 없으면 false — 경고는 내지 않는다.</summary>
    public bool TryGetSound(string key, out SoundID id)
    {
        id = SoundID.Invalid;
        if (catalog == null || string.IsNullOrEmpty(key)) return false;
        if (_catalogLookup == null || _lookupSource != catalog) RebuildCatalogLookup();
        if (!_catalogLookup.TryGet(key, out SoundEntry entry) || !entry.Sound.IsValid()) return false;
        id = entry.Sound;
        return true;
    }

    /// <summary><paramref name="source"/> 의 부모 플레이어가 원격이면 <see cref="remotePlayerVolume"/>, 아니면 1.</summary>
    public float VolumeForSource(Component source) =>
        VolumeForPlayer(source != null ? source.GetComponentInParent<Player>() : null);

    private float VolumeForPlayer(Player player)
    {
        // 오프라인(스폰 전·비네트워크 테스트)이나 내 캐릭터는 1. 스폰된 남의 캐릭터만 줄인다.
        if (player == null || !player.IsSpawned || player.IsOwner) return 1f;
        return remotePlayerVolume;
    }

    private bool IsPlayerSoundKey(string key)
    {
        if (string.IsNullOrEmpty(key) || playerSoundKeyPrefixes == null) return false;
        for (int i = 0; i < playerSoundKeyPrefixes.Length; i++)
        {
            string prefix = playerSoundKeyPrefixes[i];
            if (!string.IsNullOrEmpty(prefix) && key.StartsWith(prefix, System.StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private bool TryGetEntry(SoundID id, out SoundEntry entry)
    {
        entry = null;
        if (catalog == null) return false;
        if (_catalogLookup == null || _lookupSource != catalog)
        {
            RebuildCatalogLookup();
        }
        return _entryById.TryGetValue(id, out entry);
    }

    private IAudioPlayer PlayEntry(SoundEntry entry, Vector3? worldPos, float volumeScale = 1f)
    {
        string key = entry.Key;
        if (!entry.Sound.IsValid())
        {
            Edit.LogWarning($"[AudioManager] 사운드 키 '{key}'의 SoundID가 유효하지 않습니다.", this);
            return null;
        }

        if (!_playbackLimiter.TryAcquire(
                key,
                Time.unscaledTimeAsDouble,
                entry.MaxSimultaneous,
                entry.RetriggerCooldown))
        {
            return null;
        }

        IAudioPlayer player = worldPos.HasValue
            ? BroAudio.Play(entry.Sound, worldPos.Value)
            : BroAudio.Play(entry.Sound);

        if (player == null || !player.IsActive)
        {
            _playbackLimiter.Release(key);
            return null;
        }

        // 반납 예약은 재생기를 얻은 즉시 — 아래 설정이 실패해도 동시 수가 새지 않는다.
        player.OnEnd(_ => _playbackLimiter.Release(key));

        // SetVolume은 BroAudio의 track 배율이라 엔티티/클립 볼륨을 보존한 채 곱해진다. 1 초과도 믹서에서 증폭된다(+20dB까지).
        player.SetVolume(entry.VolumeMultiplier * RandomMultiplier(entry.VolumeRandomMin, entry.VolumeRandomMax) * volumeScale);

        // 🔴 피치·거리는 OnStart 에서 — BroAudio 재생은 큐에 들어갔다 LateUpdate 에 시작하며, 그때 엔티티 피치(SetInitialPitch)와
        //    공간 설정(SetSpatial)을 덮어쓴다. 여기서 바로 넣으면 지워진다(Codex 교차검증 11-09, AudioPlayer.Playback.cs 114·115 → 169).
        float pitchMultiplier = entry.PitchMultiplier * RandomMultiplier(entry.PitchRandomMin, entry.PitchRandomMax);
        bool is3D = entry.Is3D && worldPos.HasValue;
        if (!Mathf.Approximately(pitchMultiplier, 1f) || is3D)
        {
            float minDistance = entry.MinDistance;
            float maxDistance = Mathf.Max(entry.MinDistance, entry.MaxDistance);
            player.OnStart(p =>
            {
                if (p == null || !p.IsActive) return;
                if (!Mathf.Approximately(pitchMultiplier, 1f)) p.SetPitch(p.AudioSource.pitch * pitchMultiplier);
                if (is3D)
                {
                    p.AudioSource.minDistance = minDistance;
                    p.AudioSource.maxDistance = maxDistance;
                }
            });
        }

        return player;
    }

    /// <summary>min~max 사이 랜덤 배율. 두 값이 뒤집혀 있어도 범위로 본다.</summary>
    private static float RandomMultiplier(float min, float max)
    {
        return Random.Range(Mathf.Min(min, max), Mathf.Max(min, max));
    }

    private bool TryGetEntry(string key, out SoundEntry entry)
    {
        if (catalog == null)
        {
            Edit.LogWarning("[AudioManager] SoundCatalog가 없어 키 사운드를 재생할 수 없습니다.", this);
            entry = null;
            return false;
        }

        if (_catalogLookup == null || _lookupSource != catalog)
        {
            RebuildCatalogLookup();
        }

        if (!_catalogLookup.TryGet(key, out entry))
        {
            Edit.LogWarning($"[AudioManager] 사운드 키를 찾을 수 없습니다: '{key ?? "<null>"}'", this);
            return false;
        }

        return true;
    }

    private void RebuildCatalogLookup()
    {
        _lookupSource = catalog;
        _catalogLookup = new SoundCatalogLookup(catalog.Entries);

        foreach (int index in _catalogLookup.EmptyKeyIndices)
        {
            Edit.LogWarning($"[AudioManager] SoundCatalog entries[{index}]의 키가 비어 있어 제외했습니다.", catalog);
        }

        foreach (string duplicate in _catalogLookup.DuplicateKeys)
        {
            Edit.LogWarning($"[AudioManager] SoundCatalog 키 '{duplicate}'가 중복되어 첫 엔트리만 사용합니다.", catalog);
        }

        _entryById.Clear();
        foreach (SoundEntry entry in catalog.Entries)
        {
            if (entry == null || !entry.Sound.IsValid() || string.IsNullOrEmpty(entry.Key)) continue;
            if (_entryById.TryGetValue(entry.Sound, out SoundEntry existing))
            {
                Edit.LogWarning($"[AudioManager] 엔티티 하나를 키 '{existing.Key}'·'{entry.Key}'가 같이 쓴다 — VFX 소리는 '{existing.Key}' 설정을 쓴다.", catalog);
                continue;
            }
            _entryById.Add(entry.Sound, entry);
        }
    }

    #endregion

    #region BGM

    /// <summary>
    /// 카탈로그 키로 BGM을 재생한다. 엔트리의 볼륨 배율을 적용하고, 재생 제한(동시 수·쿨다운)은 쓰지 않는다.
    /// </summary>
    public void PlayBGM(string key)
    {
        if (!TryGetEntry(key, out SoundEntry entry))
        {
            return;
        }

        if (!entry.Sound.IsValid())
        {
            Edit.LogWarning($"[AudioManager] BGM 키 '{key}'의 SoundID가 유효하지 않습니다.", this);
            return;
        }

        BroAudio.Play(entry.Sound)
            .AsBGM()
            .SetTransition(bgmTransition, bgmFadeTime)
            .SetVolume(entry.VolumeMultiplier);
    }

    /// <summary>
    /// BGM을 재생한다. BroAudio가 Music 타입을 자동으로 BGM으로 취급해
    /// 이전 BGM에서 크로스페이드로 전환한다.
    /// </summary>
    public void PlayBGM(SoundID id)
    {
        if (!id.IsValid())
        {
            Edit.LogWarning($"[AudioManager] 유효하지 않은 BGM SoundID 재생 시도. ({id})");
            return;
        }

        BroAudio.Play(id)
            .AsBGM()
            .SetTransition(bgmTransition, bgmFadeTime);
    }

    /// <summary>현재 재생 중인 BGM(Music 카테고리)을 정지한다.</summary>
    public void StopBGM(float fadeOut = 1f)
    {
        BroAudio.Stop(BroAudioType.Music, fadeOut);
    }

    #endregion

    #region 볼륨 / 카테고리 제어 (기존 VCA/Bus 대체)

    /// <summary>BroAudioType별 볼륨 딕셔너리를 전부 1f(원음)로 초기화한다.</summary>
    private void InitVolumes()
    {
        foreach (BroAudioType type in System.Enum.GetValues(typeof(BroAudioType)))
        {
            _volumes[type] = 1f;
        }
    }

    /// <summary>카테고리별 볼륨을 설정한다. (기존 VCA 대체) vol 0~10, 1 = 원음</summary>
    public void SetVolume(BroAudioType audioType, float volume, float fadeTime = 0f)
    {
        _volumes[audioType] = volume;
        BroAudio.SetVolume(audioType, volume, fadeTime);
    }

    /// <summary>마스터 볼륨을 설정한다. (BroAudioType.All) vol 0~10, 1 = 원음</summary>
    public void SetMasterVolume(float volume, float fadeTime = 0f)
    {
        _volumes[BroAudioType.All] = volume;
        BroAudio.SetVolume(volume, fadeTime);
    }

    /// <summary>지정한 BroAudioType의 현재 볼륨을 반환한다. (딕셔너리에 없으면 1f)</summary>
    public float GetVolume(BroAudioType audioType)
    {
        return _volumes.TryGetValue(audioType, out float volume) ? volume : 1f;
    }

    /// <summary>카테고리 전체를 일시정지한다. (기존 Bus pause 대체)</summary>
    public void Pause(BroAudioType audioType)
    {
        BroAudio.Pause(audioType);
    }

    /// <summary>일시정지한 카테고리를 재개한다.</summary>
    public void UnPause(BroAudioType audioType)
    {
        BroAudio.UnPause(audioType);
    }

    #endregion
}
