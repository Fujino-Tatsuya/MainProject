using UnityEngine;

/// <summary>
/// 서버 보정이 만든 위치 점프를 **화면에서만** 흡수한다.
///
/// 왜 필요한가: 예측이 서버와 어긋나면 Motor 는 시뮬레이션 상태를 서버 값으로 즉시 스냅한다.
/// 그것은 옳다 — 물리·충돌·판정은 항상 권위 위치를 써야 한다. 하지만 그 스냅을 그대로 보여주면
/// 플레이어에게는 순간이동으로 보인다. 2026-09-16 HomeBroadband 실측에서 보정 크기는 평균
/// 6~15cm 였다. 이 정도는 몇 프레임에 걸쳐 흡수하면 눈에 띄지 않는다.
///
/// 방식: 루트(=물리/시뮬레이션 위치)는 건드리지 않고, **메시와 카메라 추종점만** 보정 직전 위치
/// 쪽으로 오프셋했다가 0 으로 감쇠시킨다. 루트를 옮기면 다음 틱 시뮬레이션이 그 오프셋을 물려받아
/// 오차가 수렴하지 않는다(Motor 가 매 틱 transform 에서 위치를 다시 읽기 때문).
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerVisualReconciliationSmoother : MonoBehaviour
{
    // 이 크기를 넘는 보정은 흡수하지 않는다. 텔레포트·부활·낙사 복귀처럼 "실제로 순간이동한" 것을
    // 미끄러뜨리면 더 이상하다. 관측된 일반 보정(수십 cm)보다 넉넉히 위에 둔다.
    [SerializeField, Min(0f)] private float maxSmoothedCorrection = 1.0f;

    // 오프셋이 1/e 로 줄어드는 시간. 짧으면 튀어 보이고 길면 위치가 늘어져 보인다.
    [SerializeField, Min(0.01f)] private float decayTimeConstant = 0.08f;

    private readonly System.Collections.Generic.List<Transform> visualRoots = new();
    private readonly System.Collections.Generic.List<Vector3> baseLocalPositions = new();
    private Vector3 worldOffset;

    private void Awake()
    {
        // 메시(Armature)와 카메라 추종점이 대상이다. 둘 다 루트의 자손이라 루트를 안 옮겨도
        // 화면에 보이는 것과 카메라가 함께 따라온다.
        PlayerMovement movement = GetComponent<PlayerMovement>();
        Transform armature = movement != null ? movement.ArmatureTransform : null;
        if (armature != null)
            Register(armature);

        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child != transform && child.CompareTag("CameraFollowTarget"))
                Register(child);
        }

        if (visualRoots.Count == 0)
        {
            Debug.LogWarning(
                "[VisualSmoother] 흡수할 시각 루트를 찾지 못했습니다(Armature/CameraFollowTarget). " +
                "보정 스냅이 그대로 보입니다.", this);
        }
    }

    private void Register(Transform target)
    {
        visualRoots.Add(target);
        baseLocalPositions.Add(target.localPosition);
    }

    /// <summary>
    /// 보정이 적용된 직후 호출한다. <paramref name="correctionDelta"/> 는 (보정 전 위치 - 보정 후 위치),
    /// 즉 화면이 당분간 머물러야 할 방향이다.
    /// </summary>
    public void AbsorbCorrection(Vector3 correctionDelta)
    {
        if (visualRoots.Count == 0)
            return;

        Vector3 next = worldOffset + correctionDelta;
        if (next.sqrMagnitude > maxSmoothedCorrection * maxSmoothedCorrection)
        {
            // 흡수 한도를 넘으면 미끄러뜨리지 않고 즉시 따라간다.
            worldOffset = Vector3.zero;
            ApplyOffset();
            return;
        }

        worldOffset = next;
        ApplyOffset();
    }

    private void LateUpdate()
    {
        if (worldOffset.sqrMagnitude <= 1e-8f)
            return;

        // 지수 감쇠. 프레임레이트에 무관하도록 deltaTime 을 시간상수로 나눈다.
        float decay = Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, decayTimeConstant));
        worldOffset *= decay;
        if (worldOffset.sqrMagnitude <= 1e-8f)
            worldOffset = Vector3.zero;

        ApplyOffset();
    }

    private void ApplyOffset()
    {
        for (int i = 0; i < visualRoots.Count; i++)
        {
            Transform target = visualRoots[i];
            if (target == null)
                continue;

            Transform parent = target.parent;
            Vector3 localOffset = parent != null
                ? parent.InverseTransformVector(worldOffset)
                : worldOffset;
            target.localPosition = baseLocalPositions[i] + localOffset;
        }
    }
}
