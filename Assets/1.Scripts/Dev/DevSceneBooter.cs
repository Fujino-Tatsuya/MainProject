using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 개발용 단독 부팅기. <c>scene</c> 필드에 적은 씬을 정식 흐름과 동일한 상태로 띄운다.
/// 호스트 기동 → NGO 씬 로드 → 액티브 씬 지정 → 플레이어 스폰 → MainGame 시작 통보 → 부팅 씬 언로드.
///
/// 씬 로드를 <see cref="NetworkSceneManager"/>로 하는 것이 핵심이다. 그래야 씬에 배치된
/// NetworkObject(몹·보스·기믹)가 자동 스폰되고, 그게 전투가 도는 전제 조건이다.
///
/// 스폰과 MainGameReady 발행은 <see cref="NetworkLoadingFlowController"/>가 이미 해준다 —
/// 그 컨트롤러는 로딩 씬이 아니라 <c>targetSceneName</c> 기준으로 씬 이벤트를 판정하므로,
/// 로딩 씬을 생략해도 완료 체인이 그대로 돈다. 이 부팅기는 컨트롤러가 해주지 않는 것만 메운다:
/// 타겟 지정 · 액티브 씬 전환 · 부팅 씬 언로드 · 순서 비의존 안전망.
///
/// ⚠️ Assets/0.Scenes/Dev/Dev_Boot.unity 에만 둔다. 그 씬은 빌드 목록에 넣지 않는다.
/// 정식 흐름(BootStrap→Title→Lobby→Loading→Map)은 이 파일과 무관하게 그대로 동작한다.
/// </summary>
[DisallowMultipleComponent]
public class DevSceneBooter : MonoBehaviour
{
    private const string LoadingSceneName = "2.LoadingScene";

    [Header("부팅할 씬 — 이 이름만 바꾸면 된다")]
    [Tooltip("빌드 씬 목록에 등록되고 enabled 상태여야 한다. 런타임 LoadScene은 비활성 씬을 로드하지 못한다.")]
    [SerializeField] private string scene = "4.MapScene";

    [Header("옵션")]
    [SerializeField] private bool autoBootOnPlay = true;
    [Tooltip("타겟 씬을 Single 모드로 실어 부팅 씬을 대체한다(부팅 씬이 하이어라키에서 사라진다). " +
             "끄면 Additive 로 얹고 부팅 씬을 남긴다. " +
             "⚠️ 부팅 씬을 UnloadSceneAsync 로 명시 언로드하면 에디터가 Play 모드를 종료해버린다 — 그래서 교체 방식을 쓴다.")]
    [SerializeField] private bool replaceBootScene = true;
    [Tooltip("비우면 NetworkManager 프리팹에 설정된 기본 플레이어를 쓴다.")]
    [SerializeField] private GameObject playerPrefabOverride;

    [Header("대기 상한(초) — 무한 대기 방지")]
    [SerializeField, Min(1f)] private float timeoutSeconds = 30f;

    /// <summary>부팅 대상 씬 이름.</summary>
    public string TargetScene => scene;

    private bool _booting;

    private void Awake()
    {
        // GameManager.Start()는 조건 없이 타이틀 씬을 로드한다. 그대로 두면 무슨 씬을 지정해도 타이틀로 튕긴다.
        // Instance 가 아니라 Find 를 쓰는 이유: 두 Awake 의 실행 순서는 보장되지 않지만(Instance 가 아직 null 일 수 있다)
        // 오브젝트 존재는 씬 로드 시점에 보장된다. Start 는 모든 Awake 뒤라 억제가 반드시 선행된다.
        GameManager gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager == null)
        {
            Debug.LogError("[DevBoot] 씬에 GameManager 가 없다. Dev_Boot 씬에 GameManager.prefab 을 넣을 것.", this);
            return;
        }

        gameManager.SuppressStartupSceneLoad();
    }

    private void Start()
    {
        if (autoBootOnPlay)
        {
            Boot();
        }
    }

    /// <summary>수동 부팅(autoBootOnPlay 를 끄고 버튼/컨텍스트 메뉴로 호출할 때).</summary>
    public void Boot()
    {
        if (_booting)
        {
            Debug.LogWarning("[DevBoot] 이미 부팅이 진행 중이다.", this);
            return;
        }

        _booting = true;
        StartCoroutine(BootRoutine());
    }

    private IEnumerator BootRoutine()
    {
        string targetScene = string.IsNullOrWhiteSpace(scene) ? string.Empty : scene.Trim();
        if (targetScene.Length == 0)
        {
            Debug.LogError("[DevBoot] Scene 필드가 비어 있다. 부팅할 씬 이름을 적을 것.", this);
            yield break;
        }

        if (!IsEnabledBuildScene(targetScene))
        {
            Debug.LogError(
                $"[DevBoot] '{targetScene}' 은 빌드 씬 목록에 없거나 비활성(enabled=0)이다. " +
                "런타임 LoadScene 은 비활성 씬을 로드하지 못한다. " +
                $"빌드 설정의 Scene List 에서 체크할 것. 현재 로드 가능한 씬: {string.Join(", ", EnabledBuildSceneNames())}",
                this);
            yield break;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            Debug.LogError("[DevBoot] NetworkManager.Singleton 이 없다. Dev_Boot 씬에 NetworkManager.prefab 을 넣을 것.", this);
            yield break;
        }

        NetworkSessionLauncher launcher = networkManager.GetComponent<NetworkSessionLauncher>();
        NetworkLoadingFlowController flow = networkManager.GetComponent<NetworkLoadingFlowController>();
        if (launcher == null || flow == null)
        {
            Debug.LogError(
                $"[DevBoot] NetworkManager 에 컴포넌트가 없다. launcher={launcher != null} loadingFlow={flow != null}. " +
                "NetworkManager.prefab 인스턴스를 쓰고 있는지 확인할 것.",
                this);
            yield break;
        }

        if (playerPrefabOverride != null)
        {
            flow.SetDefaultPlayerPrefab(playerPrefabOverride);
        }

        // 기존 편집기 설정 API로 타겟과 표시 시간을 맞춘다. Dev 경로는 StartGameLoading을 호출하지 않으므로
        // 로비 준비 게이트는 사용되지 않는다.
        flow.SetEditorDefaults(LoadingSceneName, targetScene, 0f, 0f);

        Scene bootScene = gameObject.scene;
        Debug.Log($"[DevBoot] 부팅 시작 target={targetScene} bootScene={bootScene.name} replaceBootScene={replaceBootScene}");

        // Single 모드로 실으면 부팅 씬이 파괴되면서 이 오브젝트도 사라진다 → 코루틴이 중간에 끊긴다.
        // 부팅을 끝까지 진행하려면 이 오브젝트만 씬 밖으로 빼둔다.
        if (replaceBootScene)
        {
            DontDestroyOnLoad(gameObject);
        }

        if (networkManager.IsListening)
        {
            Debug.LogWarning("[DevBoot] 네트워크가 이미 시작된 상태다. StartHost 를 건너뛴다.", this);
        }
        else if (!launcher.StartHost())
        {
            Debug.LogError("[DevBoot] StartHost 실패. 7777 포트가 이미 사용 중인지 확인할 것.", this);
            yield break;
        }

        // 세션이 실제로 올라오고 씬 매니저가 준비될 때까지.
        bool sessionReady = false;
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (Time.realtimeSinceStartup <= deadline)
        {
            if (networkManager.IsListening && networkManager.IsServer && networkManager.SceneManager != null)
            {
                sessionReady = true;
                break;
            }

            yield return null;
        }

        if (!sessionReady)
        {
            Debug.LogError(
                $"[DevBoot] 세션 기동 대기 시간 초과({timeoutSeconds}초). " +
                $"listening={networkManager.IsListening} server={networkManager.IsServer}",
                this);
            yield break;
        }

        // 씬 배치 NetworkObject 가 스폰되도록 반드시 NGO 경로로 로드한다(로컬 SceneManager.LoadScene 이면 스폰되지 않는다).
        LoadSceneMode loadMode = replaceBootScene ? LoadSceneMode.Single : LoadSceneMode.Additive;
        SceneEventProgressStatus status = networkManager.SceneManager.LoadScene(targetScene, loadMode);
        int retries = 0;
        while (status == SceneEventProgressStatus.SceneEventInProgress && retries < 30)
        {
            retries++;
            yield return null;
            status = networkManager.SceneManager.LoadScene(targetScene, loadMode);
        }

        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError($"[DevBoot] 씬 로드 요청 실패 scene={targetScene} status={status} retries={retries}", this);
            yield break;
        }

        Scene loadedScene = default;
        deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (Time.realtimeSinceStartup <= deadline)
        {
            loadedScene = SceneManager.GetSceneByName(targetScene);
            if (loadedScene.IsValid() && loadedScene.isLoaded)
            {
                break;
            }

            yield return null;
        }

        if (!loadedScene.IsValid() || !loadedScene.isLoaded)
        {
            Debug.LogError($"[DevBoot] 씬 로드 완료 대기 시간 초과({timeoutSeconds}초) scene={targetScene}", this);
            yield break;
        }

        // Additive 경로에서는 액티브 씬이 부팅 씬에 남는다(컨트롤러는 소스 씬 언로드 중에만 액티브 씬을 옮기고,
        // 로딩 씬을 생략하면 그 코드가 돌지 않는다). 액티브 씬은 인스턴스화 대상 씬과 환경광을 결정하므로 직접 옮긴다.
        // Single 경로에서는 이미 타겟이 액티브다 — 그때는 호출이 무해한 no-op 이 된다.
        if (SceneManager.GetActiveScene() != loadedScene)
        {
            SceneManager.SetActiveScene(loadedScene);
        }

        Debug.Log($"[DevBoot] 액티브 씬 = {SceneManager.GetActiveScene().name}");

        // 씬 배치 NetworkObject 스폰과 컨트롤러의 씬 이벤트 처리가 한 프레임 돌게 한다.
        yield return null;

        // 아래 셋은 전부 중복 호출 안전이 확인된 API다. 컨트롤러의 이벤트 순서에 의존하지 않기 위한 안전망.
        EnsurePlayerSpawned(networkManager, flow);

        // MarkMainGameStart 는 멱등이 아니다(부를 때마다 재스탬프). GameManager 가 4.MapScene 일 때
        // 이미 찍어주므로, 안 찍힌 경우에만 찍는다. 재스탬프하면 락스텝 기준 시각이 밀린다.
        if (NetworkClock.Instance != null && !NetworkClock.Instance.HasMainGameStarted)
        {
            NetworkClock.Instance.MarkMainGameStart();
        }

        // 플레이어 AudioListener 활성화와 InGame BGM 이 이 통보에 걸려 있다. 멱등.
        GameManager.Instance?.NotifyMainGameReady();

        Debug.Log(
            $"[DevBoot] 부팅 완료 target={targetScene} bootSceneLoaded={bootScene.IsValid() && bootScene.isLoaded} " +
            $"activeScene={SceneManager.GetActiveScene().name}");

        if (replaceBootScene)
        {
            // 부팅 씬은 Single 로드로 이미 대체됐다. 남은 것은 씬 밖으로 빼둔 이 오브젝트뿐이므로 스스로 정리한다.
            Destroy(gameObject);
        }
    }

    private static void EnsurePlayerSpawned(NetworkManager networkManager, NetworkLoadingFlowController flow)
    {
        bool hostHasPlayer =
            networkManager.ConnectedClients.TryGetValue(networkManager.LocalClientId, out NetworkClient host) &&
            host.PlayerObject != null;

        if (hostHasPlayer)
        {
            return;
        }

        Debug.Log("[DevBoot] 호스트 플레이어가 아직 없다 — 직접 스폰한다.");
        flow.SpawnAllPlayers();
    }

    private static bool IsEnabledBuildScene(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (string.Equals(SceneNameFromPath(path), sceneName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] EnabledBuildSceneNames()
    {
        int count = SceneManager.sceneCountInBuildSettings;
        string[] names = new string[count];
        for (int i = 0; i < count; i++)
        {
            names[i] = SceneNameFromPath(SceneUtility.GetScenePathByBuildIndex(i));
        }

        return names;
    }

    private static string SceneNameFromPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        int start = path.LastIndexOf('/') + 1;
        int end = path.LastIndexOf('.');
        return end > start ? path.Substring(start, end - start) : path.Substring(start);
    }
}
