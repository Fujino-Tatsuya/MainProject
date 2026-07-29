using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// 프로젝트 전역에 EventSystem을 "영구 1개"로 유지하는 싱글톤.
/// 부트스트랩 씬의 EventSystem에 부착한다.
///
/// - 최초 인스턴스를 DontDestroyOnLoad로 살려둔다.
/// - 씬 로드마다 자기 자신을 제외한 다른 EventSystem을 제거해, 로딩 중 Additive로 겹쳐도 항상 1개만 남는다.
///   (하나의 EventSystem이 모든 씬의 Canvas 입력을 처리하므로 다른 씬의 EventSystem은 불필요.)
/// - 씬을 단독 실행해도(부트스트랩 없이) 그 씬의 EventSystem이 인스턴스가 되어 정상 동작한다.
/// </summary>
[RequireComponent(typeof(EventSystem))]
public class PersistentEventSystem : MonoBehaviour
{
    private static PersistentEventSystem _instance;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject); // 중복 인스턴스(다른 씬에서 딸려온 것) 제거
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        RemoveForeignEventSystems();
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _instance = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RemoveForeignEventSystems();
    }

    /// <summary>자기 자신을 제외한 모든 EventSystem GameObject를 제거한다.</summary>
    private void RemoveForeignEventSystems()
    {
        var all = FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var es in all)
        {
            if (es != null && es.gameObject != gameObject)
            {
                es.enabled = false; // 즉시 등록 해제 → 파괴 전 프레임의 다중 EventSystem 경고까지 방지
                Destroy(es.gameObject);
            }
        }
    }
}
