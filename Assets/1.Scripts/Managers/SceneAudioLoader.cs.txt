using System.Collections.Generic;
using FMODUnity;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 씬 단위로 FMOD 뱅크를 로드/언로드하는 전담 컴포넌트(싱글톤).
/// "재생/믹싱"을 담당하는 SoundManager와 관심사를 분리해, 여기서는 오직 뱅크 생명주기만 관리한다.
///
/// FMOD Settings의 Load Banks는 Master/Strings 등 "항상 필요한 뱅크"만 자동 로드하도록 두고,
/// 씬 전용 뱅크는 이 로더가 씬 전환에 맞춰 로드/언로드한다. (메모리 최적화)
///
/// 지금은 씬 이름 → 뱅크 목록 매핑으로 충분하다.
/// 향후 크로스페이드/로딩중/로드실패 복구 등 전이 규칙이 복잡해지면 이 클래스 안에 StateMachine을 도입한다.
/// </summary>
public class SceneAudioLoader : MonoBehaviour
{
    public static SceneAudioLoader Instance { get; private set; }

    [System.Serializable]
    public struct SceneBanks
    {
        public string sceneName;
        public List<string> banks;        // 예: "Boss", "Town" (확장자 없이)
        public bool preloadSampleData;    // true면 뱅크 로드 시 샘플까지 미리 로드
    }

    [Header("씬 → 뱅크 매핑")]
    [SerializeField] private List<SceneBanks> sceneBankTable = new List<SceneBanks>();

    // 현재 이 로더가 로드한 뱅크들(씬 전환 시 언로드 대상). Master 등 자동 로드 뱅크는 건드리지 않는다.
    private readonly HashSet<string> _loadedByThis = new HashSet<string>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Edit.LogWarning("[SceneAudioLoader] 중복 생성이 감지되어 파괴합니다.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private void OnDestroy()
    {
        if (Instance != this) return;

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (TryGetBanks(scene.name, out SceneBanks entry))
            LoadBanks(entry);
    }

    private void OnSceneUnloaded(Scene scene)
    {
        if (TryGetBanks(scene.name, out SceneBanks entry))
            UnloadBanks(entry);
    }

    private bool TryGetBanks(string sceneName, out SceneBanks entry)
    {
        foreach (SceneBanks e in sceneBankTable)
        {
            if (e.sceneName == sceneName)
            {
                entry = e;
                return true;
            }
        }

        entry = default;
        return false;
    }

    /// <summary>지정 씬의 뱅크들을 로드한다. (외부에서 프리로드 목적으로 직접 호출도 가능)</summary>
    public void LoadBanks(SceneBanks entry)
    {
        if (entry.banks == null) return;

        foreach (string bank in entry.banks)
        {
            if (string.IsNullOrEmpty(bank) || _loadedByThis.Contains(bank))
                continue;

            try
            {
                RuntimeManager.LoadBank(bank, entry.preloadSampleData);
                _loadedByThis.Add(bank);
            }
            catch (BankLoadException ex)
            {
                Edit.LogWarning($"[SceneAudioLoader] 뱅크 로드 실패: {bank} — {ex.Message}");
            }
        }
    }

    private void UnloadBanks(SceneBanks entry)
    {
        if (entry.banks == null) return;

        foreach (string bank in entry.banks)
        {
            if (string.IsNullOrEmpty(bank) || !_loadedByThis.Contains(bank))
                continue;

            RuntimeManager.UnloadBank(bank);
            _loadedByThis.Remove(bank);
        }
    }
}
