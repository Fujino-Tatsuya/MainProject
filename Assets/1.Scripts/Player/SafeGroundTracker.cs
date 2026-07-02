using UnityEngine;

// 마지막 "안전 지점"(바닥 위에 서 있던 위치) 기록 — 어비스 낙하 시 근처 복귀용.
// 일정 주기로 발밑 레이캐스트, 바닥이 있고 충분히 높은 위치면 저장.
// 비네트워크: 각 클라가 자기 소유 플레이어 위치만 기록/복귀해도 NetworkTransform이 동기화.
public class SafeGroundTracker : MonoBehaviour
{
    [Tooltip("안전 지점 기록 주기(초).")]
    public float SampleInterval = 0.25f;
    [Tooltip("이 Y 미만은 안전 지점으로 기록하지 않음(구멍 낙하 중 오기록 방지).")]
    public float MinSafeY = -0.5f;
    [Tooltip("발밑 바닥 판정 레이 길이(m).")]
    public float GroundRayLength = 1.5f;
    [Tooltip("바닥 판정 레이어. 0(Nothing)이면 모든 레이어.")]
    public LayerMask GroundMask = ~0;

    public bool HasSafePosition { get; private set; }
    public Vector3 LastSafePosition { get; private set; }

    private float _nextSampleTime;

    private void Update()
    {
        if (Time.time < _nextSampleTime) return;
        _nextSampleTime = Time.time + SampleInterval;

        if (transform.position.y < MinSafeY) return;

        // 발밑에 바닥이 있어야 안전 지점 — 구멍 위 공중은 제외.
        Vector3 origin = transform.position + Vector3.up * 0.3f;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, GroundRayLength, GroundMask, QueryTriggerInteraction.Ignore))
            return;
        if (hit.point.y < MinSafeY) return;

        LastSafePosition = hit.point;
        HasSafePosition = true;
    }
}
