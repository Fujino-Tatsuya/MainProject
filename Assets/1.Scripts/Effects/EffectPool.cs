using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// 파트 <b>프리팹 단위</b>로 인스턴스를 재사용하는 풀. 엔트리 단위가 아니라 프리팹 단위인 이유는
/// 같은 파트 프리팹을 여러 엔트리가 공유할 수 있기 때문이다.
///
/// 자체 구현 대신 엔진 내장 <see cref="ObjectPool{T}"/>를 쓴다 — 프로젝트에 풀링 선례가 0건이라
/// 유니티 공식 문서가 곧 팀 문서가 된다.
///
/// 인스턴스는 <b>매니저와 무관한 전용 루트</b> 아래에 둔다. 매니저 오브젝트에 붙이면
/// 매니저의 scale이 곱해져 씬에서 맞춘 이펙트 크기가 조용히 바뀐다.
/// </summary>
public class EffectPool
{
    private readonly Dictionary<GameObject, ObjectPool<GameObject>> _pools =
        new Dictionary<GameObject, ObjectPool<GameObject>>();

    private readonly Transform _root;
    private readonly Func<GameObject, IEffectSystem> _resolveDriver;
    private readonly int _maxSizePerPrefab;

    public EffectPool(Transform root, Func<GameObject, IEffectSystem> resolveDriver, int maxSizePerPrefab)
    {
        _root = root;
        _resolveDriver = resolveDriver;
        _maxSizePerPrefab = Mathf.Max(1, maxSizePerPrefab);
    }

    /// <summary>비활성 상태의 인스턴스를 빌린다. 호출자가 위치를 잡은 뒤 직접 활성화한다.</summary>
    public GameObject Rent(GameObject prefab)
    {
        return PoolFor(prefab).Get();
    }

    /// <summary>인스턴스를 되돌린다. 이미 파괴됐거나 풀 소속이 아니면 조용히 무시한다.</summary>
    public void Return(GameObject instance)
    {
        if (instance == null) return;

        var id = instance.GetComponent<EffectInstance>();
        if (id == null || id.sourcePrefab == null)
        {
            UnityEngine.Object.Destroy(instance);
            return;
        }

        if (!_pools.TryGetValue(id.sourcePrefab, out ObjectPool<GameObject> pool))
        {
            UnityEngine.Object.Destroy(instance);
            return;
        }

        pool.Release(instance);
    }

    /// <summary>씬 로드 시 미리 만들어 둔다. 전투 최고조에 GC 스파이크가 몰리는 것을 막는 값.</summary>
    public void Prewarm(GameObject prefab, int count)
    {
        if (prefab == null || count <= 0) return;

        ObjectPool<GameObject> pool = PoolFor(prefab);
        var rented = new List<GameObject>(count);

        for (int i = 0; i < count; i++) rented.Add(pool.Get());
        for (int i = 0; i < rented.Count; i++) pool.Release(rented[i]);
    }

    /// <summary>이 프리팹으로 지금까지 만들어진 인스턴스 총수. 케이스 6(풀 재사용) 검증용.</summary>
    public int CountAll(GameObject prefab)
    {
        return prefab != null && _pools.TryGetValue(prefab, out ObjectPool<GameObject> pool) ? pool.CountAll : 0;
    }

    /// <summary>지금 대출 중인 인스턴스 수.</summary>
    public int CountActive(GameObject prefab)
    {
        return prefab != null && _pools.TryGetValue(prefab, out ObjectPool<GameObject> pool) ? pool.CountActive : 0;
    }

    /// <summary>풀을 만든 프리팹 목록. 디버그 표시용.</summary>
    public IEnumerable<GameObject> Prefabs => _pools.Keys;

    public void Dispose()
    {
        foreach (ObjectPool<GameObject> pool in _pools.Values) pool.Clear();
        _pools.Clear();
    }

    private ObjectPool<GameObject> PoolFor(GameObject prefab)
    {
        if (_pools.TryGetValue(prefab, out ObjectPool<GameObject> pool)) return pool;

        pool = new ObjectPool<GameObject>(
            createFunc: () => Create(prefab),
            actionOnGet: null,                                   // 활성화는 위치를 잡은 뒤 호출자가 한다
            actionOnRelease: instance => instance.SetActive(false),
            actionOnDestroy: instance => UnityEngine.Object.Destroy(instance),
            collectionCheck: Application.isEditor,               // 이중 반납을 에디터에서 잡는다
            defaultCapacity: 8,
            maxSize: _maxSizePerPrefab);

        _pools.Add(prefab, pool);
        return pool;
    }

    private GameObject Create(GameObject prefab)
    {
        GameObject instance = UnityEngine.Object.Instantiate(prefab, _root);
        instance.SetActive(false);

        EffectPrefabRules.ValidateAndFix(instance, prefab);

        var id = instance.AddComponent<EffectInstance>();
        id.sourcePrefab = prefab;
        id.driver = _resolveDriver(instance);

        return instance;
    }
}
