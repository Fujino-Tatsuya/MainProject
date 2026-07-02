using UnityEngine;

// 어비스 물 아래 리스폰 트리거 — 낙하한 플레이어를 마지막 안전 지점 근처로 복귀.
// 물 Plane(콜라이더 없음) 아래에 BoxCollider(isTrigger) 볼륨으로 배치.
// SafeGroundTracker 기록이 없으면(스폰 직후 낙하 등) 가장 가까운 SpawnPoint로 폴백.
[RequireComponent(typeof(BoxCollider))]
public class WaterRespawnTrigger : MonoBehaviour
{
    [Tooltip("복귀 시 지면에서 띄우는 높이(m) — 바닥 파고듦 방지.")]
    public float RespawnHeightOffset = 0.5f;

    private void Reset()
    {
        GetComponent<BoxCollider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        var player = other.GetComponentInParent<Player>();
        if (player == null) return;

        var tracker = player.GetComponent<SafeGroundTracker>();
        if (tracker == null)
            tracker = player.gameObject.AddComponent<SafeGroundTracker>();

        Vector3 target;
        if (tracker.HasSafePosition)
        {
            target = tracker.LastSafePosition;
        }
        else if (!TryGetNearestSpawnPoint(player.transform.position, out target))
        {
            target = Vector3.zero; // 최후 폴백: 맵 원점
        }
        target += Vector3.up * RespawnHeightOffset;

        var rb = player.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.position = target;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        else
        {
            player.transform.position = target;
        }
    }

    private static bool TryGetNearestSpawnPoint(Vector3 from, out Vector3 result)
    {
        result = default;
        var points = FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None);
        float best = float.MaxValue;
        foreach (var p in points)
        {
            float d = (p.transform.position - from).sqrMagnitude;
            if (d < best)
            {
                best = d;
                result = p.transform.position;
            }
        }
        return best < float.MaxValue;
    }
}
