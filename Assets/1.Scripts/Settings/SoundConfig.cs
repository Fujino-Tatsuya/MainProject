using Ami.BroAudio;
using UnityEngine;

/// <summary>
/// 볼륨(마스터·카테고리)·백그라운드 음소거 — 로컬 오디오 설정의 단일 창구(PLAN-settings.md 오디오·S11).
/// 저장은 PlayerPrefs. 실제 출력은 <see cref="AudioManager"/>(BroAudio) 가 한다 — 여기는 값과 저장만.
///
/// 🔴 예전엔 볼륨이 실행마다 1 로 초기화됐다(<c>VolumeSlider</c> 저장 코드가 주석 처리 · AudioManager.InitVolumes 가 1 로 채움).
///    이제 AudioManager 가 깨어날 때 <see cref="ApplyTo"/> 로 저장값을 건다.
/// </summary>
public static class SoundConfig
{
    const string VolumeKeyPrefix = "Settings.Audio.Volume.";   // + (int)BroAudioType
    const string MuteInBackgroundKey = "Settings.Audio.MuteInBackground";

    // 설정창에 노출하는 카테고리. 저장값이 있는 것만 AudioManager 에 다시 건다.
    static readonly BroAudioType[] Persisted = { BroAudioType.All, BroAudioType.Music, BroAudioType.SFX };

    /// <summary>저장된 볼륨(0~1). 없으면 1(원음).</summary>
    public static float GetVolume(BroAudioType type) =>
        Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKeyPrefix + (int)type, 1f));

    /// <summary>볼륨을 저장하고 즉시 건다. <c>All</c> = 마스터.</summary>
    public static void SetVolume(BroAudioType type, float volume)
    {
        volume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(VolumeKeyPrefix + (int)type, volume);
        PlayerPrefs.Save();
        Push(AudioManager.Instance, type, volume);
    }

    /// <summary>포커스를 잃으면 전체 출력을 0 으로(기본 켬, S11). 슬라이더·저장 볼륨과 독립이다.</summary>
    public static bool MuteInBackground
    {
        get => PlayerPrefs.GetInt(MuteInBackgroundKey, 1) != 0;
        set
        {
            PlayerPrefs.SetInt(MuteInBackgroundKey, value ? 1 : 0);
            PlayerPrefs.Save();
            BackgroundAudioMute.Refresh();
        }
    }

    /// <summary>AudioManager 가 깨어날 때 부른다 — 저장된 볼륨을 전부 건다.</summary>
    public static void ApplyTo(AudioManager manager)
    {
        if (manager == null) return;
        foreach (BroAudioType type in Persisted)
            if (PlayerPrefs.HasKey(VolumeKeyPrefix + (int)type))
                Push(manager, type, GetVolume(type));
    }

    static void Push(AudioManager manager, BroAudioType type, float volume)
    {
        if (manager == null) return;   // 부트스트랩 없이 단독 Play — 저장만 되고 다음 기동 때 걸린다
        if (type == BroAudioType.All) manager.SetMasterVolume(volume);
        else manager.SetVolume(type, volume);
    }
}

/// <summary>
/// 창 포커스를 잃으면 <see cref="AudioListener.volume"/> 을 0 으로, 돌아오면 1 로(S11).
/// 리스너 전체 출력만 만지므로 카테고리 볼륨·저장값은 그대로다 — "이전 값을 기억했다가 복구"가 필요 없다.
/// 첫 씬 로드 전에 DDOL 오브젝트로 하나 만든다(씬 배치 없음).
/// </summary>
public sealed class BackgroundAudioMute : MonoBehaviour
{
    static BackgroundAudioMute _instance;
    bool _focused = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Create()
    {
        if (_instance != null) return;
        var go = new GameObject(nameof(BackgroundAudioMute)) { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<BackgroundAudioMute>();
    }

    /// <summary>옵션을 바꿨을 때 지금 포커스 상태로 다시 맞춘다.</summary>
    public static void Refresh()
    {
        if (_instance != null) _instance.ApplyFocus(_instance._focused);
    }

    void OnApplicationFocus(bool hasFocus) => ApplyFocus(hasFocus);

    void ApplyFocus(bool hasFocus)
    {
        _focused = hasFocus;
        // 에디터에서는 Game 뷰 밖을 누를 때마다 꺼져 작업이 불편하다 — 빌드에서만.
        if (Application.isEditor) { AudioListener.volume = 1f; return; }
        AudioListener.volume = !hasFocus && SoundConfig.MuteInBackground ? 0f : 1f;
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }
}
