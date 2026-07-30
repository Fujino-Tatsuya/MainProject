#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// NetworkClock의 MainGameElapsed를 기준으로 동작하는 주기적 바닥 해저드.
/// 상태는 공유 시간의 순수함수로 계산하며, 데미지 처리는 자식의 ColliderBasicAttack이 담당한다.
/// </summary>
public class Vent : MonoBehaviour
{
    public enum VentState
    {
        Idle,
        Warning,
        Active
    }

    [Header("상태 시간")]
    [Min(0f)]
    [SerializeField] private float idleDuration = 2f;
    [Min(0f)]
    [SerializeField] private float warningDuration = 1f;
    [Min(0f)]
    [SerializeField] private float activeDuration = 1f;
    [Tooltip("Vent 인스턴스별 주기 시작 오프셋(초). 음수도 사용할 수 있습니다.")]
    [SerializeField] private float startOffset;

    [Header("데미지 판정")]
    [Tooltip("Active 상태에서만 활성화할 자식 데미지 콜라이더 오브젝트.")]
    [SerializeField] private GameObject damageCollider;

    [Header("상태 연출")]
    [SerializeField] private UnityEvent OnIdle = new UnityEvent();
    [SerializeField] private UnityEvent OnWarning = new UnityEvent();
    [SerializeField] private UnityEvent OnActive = new UnityEvent();

    private VentState _currentState = VentState.Idle;
    private bool _hasCurrentState;

    private void Awake()
    {
        // 프리팹의 초기 활성 상태와 관계없이 첫 물리 프레임 전에는 데미지 판정을 끈다.
        SetDamageColliderActive(false);
    }

    private void Update()
    {
        var clock = NetworkClock.Instance;
        VentState state = clock != null && clock.HasMainGameStarted
            ? EvaluateState(clock.MainGameElapsed)
            : VentState.Idle;

        ApplyRuntimeState(state);
    }

    /// <summary>공유 경과 시간(초)을 현재 Vent 상태로 변환한다. dt 누적 없이 항상 같은 결과를 낸다.</summary>
    private VentState EvaluateState(double elapsed)
    {
        double idle = System.Math.Max(0.0, idleDuration);
        double warning = System.Math.Max(0.0, warningDuration);
        double active = System.Math.Max(0.0, activeDuration);
        double cyclePeriod = idle + warning + active;

        if (cyclePeriod <= 0.0)
        {
            return VentState.Idle;
        }

        double shifted = elapsed + startOffset;
        double cycleTime = ((shifted % cyclePeriod) + cyclePeriod) % cyclePeriod;

        if (cycleTime < idle)
        {
            return VentState.Idle;
        }

        if (cycleTime < idle + warning)
        {
            return VentState.Warning;
        }

        return VentState.Active;
    }

    private void ApplyRuntimeState(VentState state)
    {
        if (_hasCurrentState && _currentState == state)
        {
            return;
        }

        _currentState = state;
        _hasCurrentState = true;
        SetDamageColliderActive(state == VentState.Active);

        switch (state)
        {
            case VentState.Idle:
                OnIdle?.Invoke();
                break;
            case VentState.Warning:
                OnWarning?.Invoke();
                break;
            case VentState.Active:
                OnActive?.Invoke();
                break;
        }
    }

    private void SetDamageColliderActive(bool active)
    {
        if (damageCollider != null && damageCollider.activeSelf != active)
        {
            damageCollider.SetActive(active);
        }
    }

#if UNITY_EDITOR
    private bool _editorPreviewActive;
    private bool _editorOriginalDamageColliderActive;
    private GameObject _editorOriginalDamageCollider;
    private VentState _editorPreviewState = VentState.Idle;

    /// <summary>씬 뷰에 표시할 런타임 또는 프리뷰 상태.</summary>
    public VentState EditorDisplayState =>
        _editorPreviewActive ? _editorPreviewState : (_hasCurrentState ? _currentState : VentState.Idle);

    /// <summary>편집 모드 상태 프리뷰를 시작하고 복원할 collider 상태를 보관한다.</summary>
    public void EditorPreviewBegin()
    {
        if (_editorPreviewActive)
        {
            return;
        }

        _editorOriginalDamageCollider = damageCollider;
        _editorOriginalDamageColliderActive =
            _editorOriginalDamageCollider != null && _editorOriginalDamageCollider.activeSelf;
        _editorPreviewState = EvaluateState(0.0);
        _editorPreviewActive = true;
        SetDamageColliderActive(_editorPreviewState == VentState.Active);
    }

    /// <summary>편집 모드 경과 시간을 상태로 변환한다. 연출 UnityEvent는 호출하지 않는다.</summary>
    public void EditorPreviewTick(double elapsed)
    {
        if (!_editorPreviewActive)
        {
            return;
        }

        _editorPreviewState = EvaluateState(elapsed);
        SetDamageColliderActive(_editorPreviewState == VentState.Active);
    }

    /// <summary>프리뷰 시작 전 collider 활성 상태를 복원한다.</summary>
    public void EditorPreviewEnd()
    {
        if (!_editorPreviewActive)
        {
            return;
        }

        if (_editorOriginalDamageCollider != null)
        {
            _editorOriginalDamageCollider.SetActive(_editorOriginalDamageColliderActive);
        }

        _editorOriginalDamageCollider = null;
        _editorPreviewActive = false;
    }
#endif

    private void OnDrawGizmosSelected()
    {
        VentState state = VentState.Idle;
#if UNITY_EDITOR
        state = EditorDisplayState;
#else
        if (_hasCurrentState)
        {
            state = _currentState;
        }
#endif

        switch (state)
        {
            case VentState.Idle:
                Gizmos.color = Color.green;
                break;
            case VentState.Warning:
                Gizmos.color = Color.yellow;
                break;
            case VentState.Active:
                Gizmos.color = Color.red;
                break;
        }

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        Gizmos.matrix = previousMatrix;

#if UNITY_EDITOR
        Handles.Label(transform.position + Vector3.up * 0.6f, $"Vent: {state}");
#endif
    }
}
