using UnityEngine;

/// <summary>
/// 활성 Gameplay Scene마다 정확히 하나 배치하는 추락 경계 설정. (PLAN §13)
/// 서버가 이 threshold로 Alive Player의 추락을 감지한다. NetworkObject 아님.
/// </summary>
public sealed class FallBoundarySettings : MonoBehaviour
{
    public static FallBoundarySettings Instance { get; private set; }

    [Tooltip("이 월드 Y 아래로 내려가면 추락으로 판정한다.")]
    [SerializeField] private float fallThresholdY = -30f;

    public float FallThresholdY => fallThresholdY;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[FallAlert] FallBoundarySettings가 씬에 둘 이상 존재합니다. 최초 인스턴스만 사용합니다.", this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
