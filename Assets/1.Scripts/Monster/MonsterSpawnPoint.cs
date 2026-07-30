using UnityEngine;

// 씬 배치용 몬스터 스폰 지점 마커. 스포너가 자식으로 수집하거나 직접 참조한다.
// 프리팹 오버라이드가 있으면 스포너 기본 프리팹 대신 이 프리팹을 스폰한다.
[DisallowMultipleComponent]
public class MonsterSpawnPoint : MonoBehaviour
{
    [Tooltip("비우면 스포너의 기본 몬스터 프리팹을 사용")]
    [SerializeField] private GameObject monsterPrefabOverride;

    [Tooltip("이 지점에서 스폰할 마리 수")]
    [SerializeField] private int count = 1;

    [Tooltip("여러 마리일 때 원형 분산 반경")]
    [SerializeField] private float scatterRadius = 1.5f;

    public GameObject MonsterPrefabOverride => monsterPrefabOverride;
    public int Count => Mathf.Max(1, count);
    public float ScatterRadius => Mathf.Max(0f, scatterRadius);

    // i번째 몬스터의 스폰 위치(간단 원형 분산).
    public Vector3 GetSpawnPosition(int index)
    {
        int c = Count;
        if (c <= 1 || ScatterRadius <= 0f)
            return transform.position;

        float angle = (360f / c) * index * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ScatterRadius;
        return transform.position + offset;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.5f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1.5f);
        if (Count > 1 && ScatterRadius > 0f)
        {
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, ScatterRadius);
        }
    }
#endif
}
