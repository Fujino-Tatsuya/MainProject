using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Ami.BroAudio;

/// <summary>
/// 씬 단위로 BGM을 자동 전환하는 전담 컴포넌트(싱글톤). 기존 FMOD의 SceneAudioLoader를 대체한다.
///
/// BroAudio는 FMOD처럼 뱅크로 로드/언로드하지 않으므로 뱅크 관리 책임은 사라졌다.
/// (오디오 메모리는 클립의 임포트 설정 — BGM은 Streaming 등 — 으로 관리한다.)
/// 따라서 이 로더는 "씬 이름 → 재생할 BGM SoundID" 매핑만 담고,
/// 씬 진입 시 <see cref="AudioManager.PlayBGM"/>를 호출한다.
///
/// 향후 대용량 클립의 메모리 압박이 커지면, 여기에 Addressables 기반
/// 로드/언로드(LoadAllAssetsAsync/ReleaseAllAssets)를 얹어 FMOD 뱅크 등가물로 확장할 수 있다.
/// </summary>
public class SceneBgmSwitcher : MonoBehaviour
{
    public static SceneBgmSwitcher Instance { get; private set; }

    [Serializable]
    public struct SceneBgm
    {
        public string sceneName;
        public SoundID bgm;
    }

    [Header("씬 → BGM 매핑")]
    [SerializeField] private List<SceneBgm> sceneBgmTable = new List<SceneBgm>();

    // 같은 BGM을 연속 요청할 때 재시작을 막기 위한 현재 BGM 추적
    private SoundID _currentBgm = SoundID.Invalid;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Edit.LogWarning("[SceneBgmSwitcher] 중복 생성이 감지되어 파괴합니다.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // 최초(부트스트랩) 씬은 이 컴포넌트가 살아나기 전에 이미 로드돼 sceneLoaded가 안 오므로 여기서 처리한다.
    private void Start()
    {
        PlayForScene(SceneManager.GetActiveScene().name);
    }

    private void OnDestroy()
    {
        if (Instance != this) return;

        SceneManager.sceneLoaded -= OnSceneLoaded;
        Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        PlayForScene(scene.name);
    }

    private void PlayForScene(string sceneName)
    {
        if (!TryGetBgm(sceneName, out SoundID bgm))
        {
            return;
        }

        if (!bgm.IsValid())
        {
            Edit.LogWarning($"[SceneBgmSwitcher] '{sceneName}'에 매핑된 BGM SoundID가 비어 있습니다.");
            return;
        }

        // 이미 같은 BGM이 지정돼 있으면 재시작하지 않는다.
        if (bgm.Equals(_currentBgm))
        {
            return;
        }

        if (AudioManager.Instance == null)
        {
            Edit.LogWarning("[SceneBgmSwitcher] AudioManager.Instance가 없습니다. 부트스트랩 씬에 AudioManager가 있는지 확인하세요.");
            return;
        }

        AudioManager.Instance.PlayBGM(bgm);
        _currentBgm = bgm;
    }

    private bool TryGetBgm(string sceneName, out SoundID bgm)
    {
        foreach (SceneBgm e in sceneBgmTable)
        {
            if (e.sceneName == sceneName)
            {
                bgm = e.bgm;
                return true;
            }
        }

        bgm = SoundID.Invalid;
        return false;
    }
}
