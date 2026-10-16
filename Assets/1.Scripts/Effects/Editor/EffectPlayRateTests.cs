using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 이펙트 재생 배율(할 일 52.1 — 팔라딘 R 1.2배).
/// ① 기본값(배율 인자 생략)은 예전 동작 그대로 ② 배율은 저작 simulationSpeed 에 곱한다
/// ③ 풀에서 다시 빌려도 배율이 남지 않는다.
///
/// EditMode 에서는 <see cref="EffectManager"/> 의 Awake 가 돌지 않는다(DontDestroyOnLoad 도 못 쓴다).
/// 그래서 풀만 리플렉션으로 꽂고 공개 API(Play/PlayLooping/ReleaseImmediate)로 실제 경로를 태운다.
/// </summary>
public sealed class EffectPlayRateTests
{
    private const float AuthoredFast = 2f;   // FX_Sword_Electric 처럼 저작값이 1이 아닌 시스템
    private const float AuthoredNormal = 1f;

    private readonly List<Object> _spawned = new List<Object>();
    private readonly List<GameObject> _buffer = new List<GameObject>();
    private EffectManager _manager;
    private EffectPool _pool;
    private Transform _poolRoot;
    private GameObject _prefab;
    private EffectEntry _entry;

    [SetUp]
    public void SetUp()
    {
        var root = new GameObject("[EffectPlayRateTests Pool]");
        _poolRoot = root.transform;
        _spawned.Add(root);

        var managerObject = new GameObject("[EffectPlayRateTests Manager]");
        _spawned.Add(managerObject);
        _manager = managerObject.AddComponent<EffectManager>();

        var driver = new ShurikenEffectSystem();
        _pool = new EffectPool(_poolRoot, _ => driver, 8);
        typeof(EffectManager).GetField("_pool", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(_manager, _pool);

        // 템플릿: 루트(저작 2) + 자식(저작 1). 풀이 Instantiate 하므로 씬 오브젝트로 충분하다.
        _prefab = new GameObject("PlayRateProbe");
        _spawned.Add(_prefab);
        ParticleSystem rootSystem = _prefab.AddComponent<ParticleSystem>();
        SetAuthoredSpeed(rootSystem, AuthoredFast);
        var child = new GameObject("Child");
        child.transform.SetParent(_prefab.transform, false);
        SetAuthoredSpeed(child.AddComponent<ParticleSystem>(), AuthoredNormal);
        _prefab.SetActive(false);

        _entry = ScriptableObject.CreateInstance<EffectEntry>();
        _spawned.Add(_entry);
        _entry.duration = 1f;
        _entry.parts = new[] { new EffectPart { prefab = _prefab } };
    }

    [TearDown]
    public void TearDown()
    {
        // _pool.Dispose 는 Object.Destroy 를 써서 EditMode 에서 에러가 난다 — 인스턴스는 풀 루트 자식이라 같이 지운다.
        for (int i = _spawned.Count - 1; i >= 0; i--)
        {
            if (_spawned[i] != null) Object.DestroyImmediate(_spawned[i]);
        }
        _spawned.Clear();
    }

    [Test]
    public void Looping_DefaultRate_KeepsAuthoredSpeeds()
    {
        EffectHandle handle = _manager.PlayLooping(_entry, Vector3.zero, Quaternion.identity);

        AssertSpeeds(SingleInstance(handle), AuthoredFast, AuthoredNormal);
        _manager.ReleaseImmediate(handle);
    }

    [Test]
    public void Looping_PlayRate_MultipliesAuthoredSpeeds()
    {
        EffectHandle handle = _manager.PlayLooping(_entry, Vector3.zero, Quaternion.identity, playRate: 1.2f);

        AssertSpeeds(SingleInstance(handle), AuthoredFast * 1.2f, AuthoredNormal * 1.2f);
        _manager.ReleaseImmediate(handle);
    }

    [Test]
    public void PooledReuse_DoesNotCarryPlayRate()
    {
        EffectHandle fast = _manager.PlayLooping(_entry, Vector3.zero, Quaternion.identity, playRate: 1.2f);
        GameObject first = SingleInstance(fast);
        _manager.ReleaseImmediate(fast);

        EffectHandle normal = _manager.PlayLooping(_entry, Vector3.zero, Quaternion.identity);
        GameObject second = SingleInstance(normal);

        Assert.That(second, Is.SameAs(first), "같은 풀 인스턴스를 다시 빌려야 이 테스트가 의미 있다");
        Assert.That(_pool.CountAll(_prefab), Is.EqualTo(1));
        AssertSpeeds(second, AuthoredFast, AuthoredNormal);
        _manager.ReleaseImmediate(normal);
    }

    [Test]
    public void ReturnToPool_RestoresAuthoredSpeed_NotOne()
    {
        // 예전 ResetForPool 은 1로 덮어 저작값 2인 시스템이 반납 뒤 느려졌다.
        EffectHandle handle = _manager.PlayLooping(_entry, Vector3.zero, Quaternion.identity, playRate: 1.2f);
        GameObject instance = SingleInstance(handle);
        _manager.ReleaseImmediate(handle);

        AssertSpeeds(instance, AuthoredFast, AuthoredNormal);
    }

    [Test]
    public void OneShot_PlayRate_MultipliesAuthoredSpeeds()
    {
        _manager.Play(_entry, Vector3.zero, Quaternion.identity, 1f, 0f, 1.2f);

        AssertSpeeds(PooledInstance(), AuthoredFast * 1.2f, AuthoredNormal * 1.2f);
    }

    [Test]
    public void OneShot_DefaultRate_KeepsAuthoredSpeeds()
    {
        _manager.Play(_entry, Vector3.zero, Quaternion.identity);

        AssertSpeeds(PooledInstance(), AuthoredFast, AuthoredNormal);
    }

    [Test]
    public void NonPositivePlayRate_FallsBackToOne()
    {
        EffectHandle handle = _manager.PlayLooping(_entry, Vector3.zero, Quaternion.identity, playRate: 0f);

        AssertSpeeds(SingleInstance(handle), AuthoredFast, AuthoredNormal);
        _manager.ReleaseImmediate(handle);
    }

    [Test]
    public void HitStopRate_IsAlsoRelativeToAuthored()
    {
        // 히트스톱(SetPlayRateForTarget)도 같은 드라이버 경로다 — 풀린 뒤(1) 저작값으로 돌아와야 한다.
        var target = new GameObject("FollowTarget");
        _spawned.Add(target);
        EffectHandle handle = _manager.PlayLooping(_entry, target.transform);
        GameObject instance = SingleInstance(handle);

        _manager.SetPlayRateForTarget(target.transform, 0f);
        AssertSpeeds(instance, 0f, 0f);

        _manager.SetPlayRateForTarget(target.transform, 1f);
        AssertSpeeds(instance, AuthoredFast, AuthoredNormal);
        _manager.ReleaseImmediate(handle);
    }

    private GameObject SingleInstance(EffectHandle handle)
    {
        Assert.That(handle.IsSet, Is.True);
        Assert.That(_manager.GetInstances(handle, _buffer), Is.EqualTo(1));
        return _buffer[0];
    }

    // 원샷은 핸들이 없다 — 풀 루트 아래에서 켜져 있는 인스턴스를 찾는다.
    private GameObject PooledInstance()
    {
        Assert.That(_pool.CountActive(_prefab), Is.EqualTo(1));
        foreach (Transform child in _poolRoot)
        {
            if (child.gameObject.activeSelf) return child.gameObject;
        }

        Assert.Fail("풀 루트 아래에 켜진 인스턴스가 없다");
        return null;
    }

    private static void AssertSpeeds(GameObject instance, float rootSpeed, float childSpeed)
    {
        ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(true);
        Assert.That(systems.Length, Is.EqualTo(2));
        Assert.That(systems[0].main.simulationSpeed, Is.EqualTo(rootSpeed).Within(1e-5f), "루트");
        Assert.That(systems[1].main.simulationSpeed, Is.EqualTo(childSpeed).Within(1e-5f), "자식");
    }

    private static void SetAuthoredSpeed(ParticleSystem system, float speed)
    {
        ParticleSystem.MainModule main = system.main;
        main.playOnAwake = false;
        main.simulationSpeed = speed;
    }
}
