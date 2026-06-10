using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class NetworkObjectPoolLoadingTask : NetworkLoadingTask
{
#pragma warning disable CS0649
    [System.Serializable]
    private struct PoolEntry
    {
        public NetworkObject prefab;
        [Min(0)] public int preloadCount;
    }
#pragma warning restore CS0649

    [SerializeField] private PoolEntry[] entries;
    [SerializeField] private Transform poolRoot;
    [SerializeField] private int objectsPerFrame = 4;

    protected override IEnumerator Execute()
    {
        var totalCount = GetTotalCount();
        if (totalCount == 0)
        {
            SetProgress(1f);
            yield break;
        }

        if (poolRoot == null)
        {
            var rootObject = new GameObject("NetworkObjectPool");
            DontDestroyOnLoad(rootObject);
            poolRoot = rootObject.transform;
        }

        var created = 0;
        var frameBudget = Mathf.Max(1, objectsPerFrame);

        foreach (var entry in entries)
        {
            if (entry.prefab == null || entry.preloadCount <= 0)
            {
                continue;
            }

            for (var i = 0; i < entry.preloadCount; i++)
            {
                var instance = Instantiate(entry.prefab, poolRoot);
                instance.gameObject.SetActive(false);

                created++;
                SetProgress((float)created / totalCount);

                if (created % frameBudget == 0)
                {
                    yield return null;
                }
            }
        }
    }

    private int GetTotalCount()
    {
        var total = 0;
        if (entries == null)
        {
            return total;
        }

        foreach (var entry in entries)
        {
            if (entry.prefab != null)
            {
                total += Mathf.Max(0, entry.preloadCount);
            }
        }

        return total;
    }
}
