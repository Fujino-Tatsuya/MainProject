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

    [Header("BGM")]
    [SerializeField] private Transition bgmTransition = Transition.CrossFade;
    [Tooltip("BGM 페이드 인 / 크로스페이드 시간(초)")]
    [SerializeField, Min(0f)] private float bgmFadeTime = 1.5f;

    /// <summary>중앙 사운드 카탈로그. 예: AudioManager.Instance.Catalog.UIClick</summary>
    public SoundCatalog Catalog => catalog;

    // BroAudioType별 현재 볼륨(0~10, 1=원음). 게임 시작 시 전부 1f로 초기화. PlayerPrefs 미사용.
    private readonly Dictionary<BroAudioType, float> _volumes = new Dictionary<BroAudioType, float>();

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

        if (catalog == null)
        {
            Edit.LogWarning("[AudioManager] SoundCatalog가 연결되지 않았습니다. 인스펙터에서 연결하세요.", this);
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

    /// <summary>위치 무관(2D/전역) 사운드를 한 번 재생한다.</summary>
    public IAudioPlayer PlayOneShot(SoundID id)
    {
        if (!id.IsValid())
        {
            Edit.LogWarning($"[AudioManager] 유효하지 않은 SoundID 재생 시도. ({id})");
            return null;
        }
        return BroAudio.Play(id);
    }

    /// <summary>월드 좌표에서 3D 사운드를 한 번 재생한다.</summary>
    public IAudioPlayer PlayOneShot(SoundID id, Vector3 worldPos)
    {
        if (!id.IsValid())
        {
            Edit.LogWarning($"[AudioManager] 유효하지 않은 SoundID 재생 시도. ({id})");
            return null;
        }
        return BroAudio.Play(id, worldPos);
    }

    #endregion

    #region BGM

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
