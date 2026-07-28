using FMODUnity;
using UnityEngine;

/// <summary>
/// 프로젝트에서 사용하는 모든 FMOD 이벤트(EventReference)를 한 곳에 모아두는 데이터 허브.
/// 인스펙터에서 이벤트를 드래그로 연결해야 하므로 MonoBehaviour 싱글톤으로 유지한다.
/// 씬이 늘어나면 [Header]로 구획을 나눠 정리한다.
/// </summary>
public class FmodEvents : MonoBehaviour
{
    public static FmodEvents Instance { get; private set; }

    [Header("Common")]
    [field: SerializeField] public EventReference UIClick { get; private set; }

    [Header("BGM")]
    [field: SerializeField] public EventReference Lobby { get; private set; }
    [field: SerializeField] public EventReference InGame { get; private set; }

    // [Header("Boss Scene (Wells / No.23)")]
    // [field: SerializeField] public EventReference BossBGM { get; private set; }
    // [field: SerializeField] public EventReference BossRoar { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Edit.LogWarning("[FmodEvents] 중복 생성이 감지되어 파괴합니다.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
