using System.Collections.Generic;
using UnityEngine;
using Ami.BroAudio;

/// <summary>
/// 사운드 재생·BGM·볼륨의 중앙 창구(싱글톤). 기존 FMOD의 SoundManager를 대체한다.
/// BroAudio 파사드(<see cref="BroAudio"/>) 위에 게임 의미의 메서드를 얹기만 하며,
/// BGM 전환(씬·보스 단계)도 여기서 판단한다(11-10 PLAN-sound-spec-apply 4 — <see cref="SceneBgmSwitcher"/> 는 배선이 없어 쓰지 않는다).
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

    /// <summary>3D 엔트리를 <paramref name="follow"/> 를 따라가며 재생한다(움직이는 발원). 2D 엔트리면 위치를 무시한다.</summary>
    public IAudioPlayer Play(string key, Transform follow)
    {
        if (!TryGetEntry(key, out SoundEntry entry))
        {
            return null;
        }

        return entry.Is3D && follow != null ? PlayEntry(entry, follow.position, 1f, follow) : PlayEntry(entry, null);
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

    private IAudioPlayer PlayEntry(SoundEntry entry, Vector3? worldPos, float volumeScale = 1f, Transform follow = null)
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

        IAudioPlayer player = follow != null
            ? BroAudio.Play(entry.Sound, follow)
            : worldPos.HasValue
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

    // ── BGM 공통 규칙(사운드 명세 v0.1 원칙 10 · PLAN-sound-spec-apply 4단계, 11-10) ──
    // · 같은 곡 반복 = 엔티티 Seamless 2초(끝 2초 전 같은 파일을 겹쳐 시작 — BroAudio 엔티티 TransitionTime 2).
    // · 곡이 바뀔 때 = 지금 곡을 페이드아웃 → **끝난 뒤** 새 곡 2초 페이드인. 서로 다른 곡은 겹치지 않는다(로딩 중 공백 허용).
    // · 씬 전환 시작 = 1초 아웃(BeginSceneTransition). 씬 로딩은 음악을 기다리지 않는다(여기는 소리만).
    // · 11-12 경석: 로딩 씬 = 로비 곡(EnterLoadingBgm) · 연출(로비 포드 발사·맵 포드 착륙·존4 소개) 동안 BGM 없음 = 0.1초 아웃,
    //   끝나면 그 씬 곡 2초 인(Begin/EndCinematicBgm) · 타이틀은 모니터 꺼짐 시작에 0.1초 아웃.
    // · 보스: 연출 시작(Preparing) = 스테이지 곡 1초 아웃 → 연출 중 무음 → 전투 시작 = 보스 곡 2초 인 →
    //   보스 사망 = 2초 아웃이 이긴다(그 뒤 오는 일반 아웃은 진행 중 아웃을 줄이지 못한다).
    // 판단은 이 한 곳 — "씬이 원하는 곡"과 "보스 상태"를 보고 지금 틀 곡을 고른다(EvaluateBgm). 요청은 멱등.
    // 🔴 개별 재생기 핸들 대신 SoundID 로 끈다 — Seamless 반복은 회차마다 재생기가 바뀌어 옛 핸들은 이미 끝나 있을 수 있다.
    //    BroAudio Stop(id) 은 완료 콜백이 없어 페이드 시간을 직접(실시간) 센다.
    public enum BossBgmState { None, Intro, Combat, Defeated }

    public const string BossBgmKey = "BGM_Boss";
    private const float BgmSceneFadeOut = 1f;
    private const float BgmBossDeathFadeOut = 2f;
    private const float BgmFadeIn = 2f;
    private const float BgmCinematicFadeOut = 0.1f;   // "바로 꺼짐" — 뚝 끊으면 틱 잡음이 나서 0.1초(경석 11-12)
    public const string LoadingBgmKey = "BGM_Lobby";  // 로딩 씬 = 로비 곡(경석 11-12)
    private readonly HashSet<Object> _cinematicOwners = new HashSet<Object>();   // 지금 BGM 을 꺼 둔 연출들

    private string _sceneBgmKey;            // 이 씬이 원하는 곡(없으면 null)
    private BossBgmState _bossBgm;          // 같은 씬 안 보스 단계
    private string _bgmKey;                 // 지금 울리는(또는 끄는 중인) 곡
    private SoundID _bgmId = SoundID.Invalid;
    private bool _bgmStopping;
    private int _bgmGeneration;
    private UnityEngine.SceneManagement.Scene _bossBgmScene;   // 보스 상태를 낸 씬(재합류 때 PlayBGM 이 지우지 않게)

    // DontDestroyOnLoad 는 늘 떠 있으므로 "그 씬이 아직 있다"의 근거가 못 된다 — 예전처럼 지운다.
    private bool IsBossBgmSceneLoaded =>
        _bossBgmScene.IsValid() && _bossBgmScene.isLoaded && _bossBgmScene.name != "DontDestroyOnLoad";

    /// <summary>
    /// [이 피어] 이 씬의 BGM 을 정한다(Title·Lobby <c>Start</c>, Map <c>MainGameReady</c>). 지금 곡과 다르면 아웃 → 끝난 뒤 2초 인.
    /// 보스 단계는 건드리지 않는다(보스전 중 늦게 와도 보스 상태가 이긴다).
    /// </summary>
    public void PlayBGM(string key)
    {
        _sceneBgmKey = key;
        // 씬 진입(Title·Lobby Start, Map MainGameReady)이다 — 이전 씬의 보스 상태는 남기지 않는다
        // (보스전 중 ESC → 타이틀처럼 BeginSceneTransition 을 안 거친 이탈도 여기서 풀린다).
        // 🔴 단, 보스 상태를 낸 씬이 아직 떠 있으면 그대로 둔다 — 판 도중 재합류(C14)는 동기화 때 보스 단계·23호 사망을 먼저
        //    적용하고 그 뒤에 MainGameReady(맵 곡)가 와서, 예전엔 여기서 지워져 보스전 중에 스테이지 곡이 났다(Codex 교차검증 11-12).
        if (!IsBossBgmSceneLoaded) _bossBgm = BossBgmState.None;
        EvaluateBgm(BgmSceneFadeOut);
    }

    /// <summary>
    /// [이 피어] 씬 전환이 시작됐다 — 지금 곡 아웃(기본 1초, 보스 사망 아웃 중이면 그대로 둔다). 새 씬이 PlayBGM 으로 다시 정한다.
    /// 타이틀은 모니터가 꺼지기 시작하는 순간 0.1초로 부른다(경석 11-12).
    /// </summary>
    public void BeginSceneTransition(float fadeOut = BgmSceneFadeOut)
    {
        _sceneBgmKey = null;
        _bossBgm = BossBgmState.None;
        _bossBgmScene = default;
        _cinematicOwners.Clear();
        EvaluateBgm(fadeOut);
    }

    /// <summary>
    /// [이 피어] 로딩 씬이 떴다 — 로딩 동안은 로비 곡(경석 11-12: "로딩씬은 로비 BGM 을 이어서 쓴다").
    /// 로비 끝 포드 발사 연출로 꺼 둔 로비 곡이 여기서 다시 2초 인으로 나오고, 맵 곡(재시작)이 울리고 있었으면 1초 아웃 뒤 바뀐다.
    /// 로딩 흐름의 여러 지점(서버 시작·클라 상태 메시지·맵 로드 시작)에서 불려도 같은 곡이면 아무 일도 없다.
    /// </summary>
    public void EnterLoadingBgm()
    {
        _sceneBgmKey = LoadingBgmKey;
        _bossBgm = BossBgmState.None;
        _bossBgmScene = default;
        _cinematicOwners.Clear();
        EvaluateBgm(BgmSceneFadeOut);
    }

    /// <summary>
    /// [이 피어] 연출이 시작됐다 — 연출 동안 BGM 없음(연출 사운드만). 지금 곡은 0.1초 아웃(경석 11-12 "바로 꺼짐").
    /// 대상: 로비 끝 포드 발사 · 맵 포드 착륙 · 존4 소개 연출. 보스 등장·사망은 보스 단계(<see cref="SetBossBgmState"/>)가 따로 맡는다.
    /// <paramref name="owner"/> 마다 한 번 — 같은 owner 로 여러 번 불러도 같다. 끝나면 <see cref="EndCinematicBgm"/>.
    /// </summary>
    public void BeginCinematicBgm(Object owner)
    {
        if (owner == null || !_cinematicOwners.Add(owner)) return;
        EvaluateBgm(BgmCinematicFadeOut);
    }

    /// <summary>[이 피어] 연출이 끝났다 — 남은 연출이 없으면 이 씬 곡(또는 보스 단계 곡)을 2초 인. 멱등.</summary>
    public void EndCinematicBgm(Object owner)
    {
        if (owner == null || !_cinematicOwners.Remove(owner)) return;
        EvaluateBgm(BgmSceneFadeOut);
    }

    // 파괴된 owner(EndCinematicBgm 없이 씬과 함께 사라진 연출)는 무음을 붙잡지 않는다.
    private bool HasCinematic
    {
        get
        {
            if (_cinematicOwners.Count == 0) return false;
            _cinematicOwners.RemoveWhere(o => o == null);
            return _cinematicOwners.Count > 0;
        }
    }

    /// <summary>
    /// [이 피어] 보스 단계(복제값을 각 피어가 본다). 사망 = 2초 아웃, 나머지 전환 = 1초 아웃.
    /// <paramref name="owner"/> = 상태를 낸 오브젝트 — 그 씬이 떠 있는 동안은 <see cref="PlayBGM(string)"/> 가 보스 상태를 지우지 않는다(재합류).
    /// </summary>
    public void SetBossBgmState(BossBgmState state, Component owner = null)
    {
        if (owner != null) _bossBgmScene = owner.gameObject.scene;
        if (_bossBgm == state) return;
        // 사망 뒤엔 보스 단계(Combat·Idle 등)가 늦게 와도 곡을 되살리지 않는다 — 씬 전환(BeginSceneTransition)만 푼다.
        // (늦은 합류는 23호 Dead 보정과 보스 단계 현재값 적용의 순서가 정해져 있지 않다.)
        if (_bossBgm == BossBgmState.Defeated) return;
        _bossBgm = state;
        EvaluateBgm(state == BossBgmState.Defeated ? BgmBossDeathFadeOut : BgmSceneFadeOut);
    }

    private string TargetBgmKey => HasCinematic ? null : _bossBgm switch
    {
        BossBgmState.Intro => null,
        BossBgmState.Combat => BossBgmKey,
        BossBgmState.Defeated => null,
        _ => _sceneBgmKey,
    };

    private void EvaluateBgm(float fadeOut)
    {
        string target = TargetBgmKey;
        if (_bgmStopping) return;   // 끄는 중 — 끝나면 FinishBgmStop 이 다시 고른다(진행 중 아웃은 늘이거나 줄이지 않는다)
        if (_bgmKey == target) return;

        // 🔴 "울리는 중" 판정에 HasAnyPlayingInstances 를 쓰지 않는다 — BroAudio.Play 는 LateUpdate 큐에 넣으므로
        //    같은 프레임에 곡이 바뀌면(무음 → Combat → Defeated) 아직 큐에 있는 곡을 못 끄고 사망 뒤에 보스 곡이 시작된다.
        //    우리가 튼 곡(_bgmKey)이 있으면 무조건 SoundID 로 끄고 페이드 시간을 기다린다(큐에 있던 것도 같은 ID 라 같이 멈춘다).
        if (_bgmKey != null && _bgmId.IsValid())
        {
            _bgmStopping = true;
            int generation = ++_bgmGeneration;
            BroAudio.Stop(_bgmId, fadeOut);
            StartCoroutine(FinishBgmStopAfter(fadeOut, generation));
            return;
        }

        StartBgm(target);
    }

    private System.Collections.IEnumerator FinishBgmStopAfter(float seconds, int generation)
    {
        yield return new WaitForSecondsRealtime(seconds);
        if (generation != _bgmGeneration) yield break;
        _bgmStopping = false;
        _bgmKey = null;
        _bgmId = SoundID.Invalid;
        StartBgm(TargetBgmKey);
    }

    private void StartBgm(string key)
    {
        _bgmKey = null;
        _bgmId = SoundID.Invalid;
        if (string.IsNullOrEmpty(key) || !TryGetEntry(key, out SoundEntry entry)) return;

        if (!entry.Sound.IsValid())
        {
            Edit.LogWarning($"[AudioManager] BGM 키 '{key}'의 SoundID가 유효하지 않습니다.", this);
            return;
        }

        BroAudio.Play(entry.Sound, BgmFadeIn).SetVolume(entry.VolumeMultiplier);
        _bgmKey = key;
        _bgmId = entry.Sound;
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

    /// <summary>이 씬의 BGM 을 끈다(곡 없음). 보스 단계가 곡을 원하면 그쪽이 남는다.</summary>
    public void StopBGM(float fadeOut = 1f)
    {
        _sceneBgmKey = null;
        EvaluateBgm(fadeOut);
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
