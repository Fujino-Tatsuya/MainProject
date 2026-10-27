using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 훈련 종료 텔레포트 지점(할 일 111) — 종료 집결 확정 때 진행기(<see cref="LobbyTrainingDirector"/>, 서버)가 훈련 명단 플레이어를 여기로 옮긴다.
/// <list type="bullet">
/// <item>자리 = <see cref="points"/>(기본 자식 <c>TrainingEndTeleport_1~3</c>). 명단 순서대로 한 자리씩, 4번째부터 처음부터 다시(순환).</item>
/// <item>연결 = 정적 등록(<see cref="Active"/>) — 진행기는 씬 참조 필드 없이 찾는다. 기본 위치 = <c>LobbyGuideSteps.prefab</c> 의 <c>TrainingEnd</c> 자식.</item>
/// <item>이 컴포넌트는 위치만 준다. 이동은 진행기가 기존 서버 텔레포트(<see cref="PlayerFallRecovery.RelocateServer"/> → 오너 RPC)로 한다.</item>
/// </list>
/// 상세: Docs/tech/lobby-training.md "훈련 종료".
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyTrainingEndTeleport : MonoBehaviour
{
    [Tooltip("플레이어별 도착 자리(바닥 위). 명단 순서대로 한 자리씩, 모자라면 순환.")]
    [SerializeField] private Transform[] points;

    /// <summary>켜져 있는 지점 묶음(없으면 null). 여러 개면 마지막에 켜진 것.</summary>
    public static LobbyTrainingEndTeleport Active { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Active = null;

    private void OnEnable() => Active = this;

    private void OnDisable()
    {
        if (Active == this)
            Active = null;
    }

    /// <summary><paramref name="index"/> 번째 사람(0부터)의 자리 — 비어 있지 않은 지점만 세어 순환. 지점이 하나도 없으면 false.</summary>
    public bool TryGetPoint(int index, out Vector3 position)
    {
        position = default;
        var count = 0;
        if (points != null)
        {
            foreach (var point in points)
            {
                if (point != null)
                    count++;
            }
        }

        if (count == 0)
            return false;

        var target = (index % count + count) % count;
        foreach (var point in points)
        {
            if (point == null)
                continue;
            if (target-- == 0)
            {
                position = point.position;
                return true;
            }
        }

        return false;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (points == null)
            return;

        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.8f);
        for (var i = 0; i < points.Length; i++)
        {
            if (points[i] == null)
                continue;

            Gizmos.DrawWireSphere(points[i].position + Vector3.up * 0.5f, 0.4f);
            Handles.Label(points[i].position + Vector3.up * 1.1f, $"훈련 종료 {i + 1}");
        }
    }
#endif
}
