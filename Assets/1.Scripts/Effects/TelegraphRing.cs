using UnityEngine;

/// <summary>
/// 대상별 발사 예고(거너 R 위성 레이저 조준 표식). <b>진행도 0 → 1</b> 동안 원이 좁혀지고, 1 = 발사 순간이다.
/// 진행도는 바깥이 매 프레임 넣는다(<see cref="SetProgress"/>) — 박자의 주인은 서버 피해 주기라서
/// 이 컴포넌트는 시간을 스스로 세지 않는다. 켜질 때(풀 대출마다) 꺾쇠가 크게 시작해 조여 드는 "잠금" 동작만 혼자 한다.
///
/// 🔴 <b>원·꺾쇠 파티클은 Scaling Mode = Hierarchy · Simulation = Local · Mesh(Quad) 정렬 Local 이어야 한다.</b>
/// 여기서는 그 부모 트랜스폼의 배율·회전만 바꾼다(빌보드면 회전이 안 먹는다).
///
/// MyProject(제작·미리보기)와 MainProject(실사용)에 <b>같은 guid</b>로 둔다 — 프리팹이 스크립트를 guid 로 가리킨다.
/// </summary>
[DisallowMultipleComponent]
public class TelegraphRing : MonoBehaviour
{
    [Header("좁혀지는 원")]
    [Tooltip("진행도에 따라 배율이 줄어드는 트랜스폼(원 파티클의 부모)")]
    [SerializeField] Transform ring;
    [SerializeField, Min(0f)] float ringFrom = 1f;
    [SerializeField, Min(0f)] float ringTo = 0.12f;

    [Header("잠금(켜질 때 한 번)")]
    [Tooltip("켜질 때 크게·비스듬히 시작해 제자리로 조여 드는 트랜스폼(꺾쇠 파티클의 부모)")]
    [SerializeField] Transform brackets;
    [SerializeField, Min(0.01f)] float lockInSeconds = 0.2f;
    [SerializeField, Min(1f)] float lockInScale = 1.8f;
    [SerializeField] float lockInAngle = 45f;

    float _lockIn = 1f;

    void OnEnable()
    {
        _lockIn = 0f;
        SetProgress(0f);
        ApplyLockIn();
    }

    void Update()
    {
        if (_lockIn >= 1f) return;
        _lockIn = Mathf.Min(1f, _lockIn + Time.deltaTime / lockInSeconds);
        ApplyLockIn();
    }

    /// <summary>0 = 막 발사한 직후(원이 가장 큼) · 1 = 다음 발사 순간(원이 가장 작음).</summary>
    public void SetProgress(float progress)
    {
        if (ring == null) return;
        ring.localScale = Vector3.one * Mathf.Lerp(ringFrom, ringTo, Mathf.Clamp01(progress));
    }

    void ApplyLockIn()
    {
        if (brackets == null) return;
        float t = 1f - (1f - _lockIn) * (1f - _lockIn);   // ease-out
        brackets.localScale = Vector3.one * Mathf.Lerp(lockInScale, 1f, t);
        // 바닥 층은 X 90° 로 눕힌 부모 아래에 있다 — 로컬 Z 축 회전 = 수직축 회전.
        brackets.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(lockInAngle, 0f, t));
    }
}
