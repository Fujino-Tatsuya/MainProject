using UnityEngine;

/// <summary>
/// 1단계(루프 정정) 검증용 임시 계측기. 로컬 플레이어의 <b>수평</b> 이동거리를 구간별로 재서
/// 프레임레이트가 달라져도 같은 시간에 같은 거리를 가는지 확인한다.
///
/// 왜 필요한가: 이동이 <c>Update</c>에서 돌면 <c>MovePosition</c>이 물리 스텝까지 예약되는
/// 특성 때문에 물리 틱 하나 사이의 여러 프레임이 서로를 덮어쓴다 → 고프레임일수록 느려진다.
/// 1단계가 이걸 고쳤는지는 <b>fps를 바꿔가며 같은 거리를 재보는 것</b>으로만 확인된다.
///
/// 쓰는 법:
/// 1. 씬의 아무 GameObject에 붙인다(플레이어에 붙일 필요 없다 — 로컬 플레이어를 스스로 찾는다).
/// 2. Play 후 <see cref="targetFrameRate"/>를 30 / 60 / 144로 바꿔가며 각각
///    한 방향으로 쭉 걷는다(벽·경사 없는 평지에서, 방향키를 계속 누른 채).
/// 3. 콘솔의 <c>[MoveProbe]</c> 줄에서 <b>평균 속도(m/s)</b>를 비교한다. 세 값의 편차가
///    5% 이내여야 통과. 1단계 이전이라면 고프레임일수록 느리게 나온다.
///
/// 🔴 검증용이며 빌드에 남기지 않는다. 확인이 끝나면 이 파일을 삭제할 것.
/// </summary>
public sealed class DevMoveSpeedProbe : MonoBehaviour
{
    [Header("측정 설정")]
    [Tooltip("Play 중 이 값을 바꾸면 즉시 적용된다. 0이면 건드리지 않는다(VSync 설정을 따른다).")]
    [SerializeField] private int targetFrameRate = 0;

    [Tooltip("이 시간마다 한 구간의 평균 속도를 로그로 남긴다(초).")]
    [SerializeField, Min(0.5f)] private float reportInterval = 2f;

    [Tooltip("이 속도 미만인 구간은 '멈춰 있었다'로 보고 로그하지 않는다(m/s).")]
    [SerializeField, Min(0f)] private float idleSpeedThreshold = 0.2f;

    private Transform tracked;
    private Vector3 segmentStart;
    private float segmentStartTime;
    private int appliedFrameRate = int.MinValue;

    private void Update()
    {
        ApplyFrameRateIfChanged();

        if (!TryResolveTarget())
            return;

        float elapsed = Time.time - segmentStartTime;
        if (elapsed < reportInterval)
            return;

        Vector3 delta = tracked.position - segmentStart;
        delta.y = 0f; // 수평만 — 낙하·경사 성분은 이동속도 검증과 무관하다
        float distance = delta.magnitude;
        float speed = elapsed > 0f ? distance / elapsed : 0f;

        if (speed >= idleSpeedThreshold)
        {
            Debug.Log(
                $"[MoveProbe] 평균 {speed:F3} m/s | 거리 {distance:F3}m / {elapsed:F2}s " +
                $"| 설정fps={(targetFrameRate > 0 ? targetFrameRate.ToString() : "제한없음")} " +
                $"실측fps={1f / Mathf.Max(Time.smoothDeltaTime, 1e-5f):F0} " +
                $"물리틱={1f / Time.fixedDeltaTime:F0}Hz");
        }

        segmentStart = tracked.position;
        segmentStartTime = Time.time;
    }

    private void ApplyFrameRateIfChanged()
    {
        if (targetFrameRate == appliedFrameRate)
            return;

        appliedFrameRate = targetFrameRate;
        if (targetFrameRate > 0)
        {
            QualitySettings.vSyncCount = 0; // vSync가 켜져 있으면 targetFrameRate가 무시된다
            Application.targetFrameRate = targetFrameRate;
        }
        else
        {
            Application.targetFrameRate = -1;
        }
    }

    /// <summary>로컬 플레이어를 찾고, 대상이 바뀌면 구간을 리셋한다.</summary>
    private bool TryResolveTarget()
    {
        Player local = Player.LocalPlayer;
        Transform next = local != null ? local.transform : null;

        if (next == null)
        {
            tracked = null;
            return false;
        }

        if (next != tracked)
        {
            tracked = next;
            segmentStart = tracked.position;
            segmentStartTime = Time.time;
            return false;
        }

        return true;
    }
}
