using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 282 — 스피너봇 간파 성공 뒤 회전 이펙트 잔상.
/// ① <see cref="EffectSocketPlayer.Stop"/> 은 "서서히"(새 입자만 멈춤 — 인스턴스가 종료 시간 동안 남는다)
/// ② <see cref="EffectSocketPlayer.StopImmediate"/> 는 같은 프레임에 인스턴스를 풀로 돌려보낸다(남는 입자 없음)
/// ③ 스피너봇은 그로기(= 간파로 회전이 끊김)에서만 바로 걷는다.
///
/// EditMode 에서는 <see cref="EffectManager"/> 의 Awake 가 돌지 않는다 — 풀과 Instance 를 리플렉션으로 꽂는다
/// (<see cref="EffectPlayRateTests"/> 와 같은 방식).
/// </summary>
public sealed class EffectSocketStopImmediateTests
{
    private readonly List<Object> _spawned = new List<Object>();
    private EffectManager _manager;
    private EffectManager _previousInstance;
    private EffectPool _pool;
    private GameObject _prefab;
    private EffectSocketPlayer _socket;

    private static readonly MethodInfo InstanceSetter =
        typeof(EffectManager).GetProperty(nameof(EffectManager.Instance)).GetSetMethod(true);

    [SetUp]
    public void SetUp()
    {
        var root = new GameObject("[EffectSocketStopImmediateTests Pool]");
        _spawned.Add(root);

        var managerObject = new GameObject("[EffectSocketStopImmediateTests Manager]");
        _spawned.Add(managerObject);
        _manager = managerObject.AddComponent<EffectManager>();

        var driver = new ShurikenEffectSystem();
        _pool = new EffectPool(root.transform, _ => driver, 8);
        typeof(EffectManager).GetField("_pool", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(_manager, _pool);

        _previousInstance = EffectManager.Instance;
        InstanceSetter.Invoke(null, new object[] { _manager });

        // 루프 파티클 하나(FX_Mob_Spin_Loop 의 glow 처럼 loop = true).
        _prefab = new GameObject("SpinProbe");
        _spawned.Add(_prefab);
        ParticleSystem system = _prefab.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = system.main;
        main.playOnAwake = false;
        main.loop = true;
        _prefab.SetActive(false);

        var entry = ScriptableObject.CreateInstance<EffectEntry>();
        _spawned.Add(entry);
        entry.outroDuration = 1f;   // 서서히 끄면 이 시간 동안 인스턴스가 남는다
        entry.parts = new[] { new EffectPart { prefab = _prefab } };

        var owner = new GameObject("SpinnerSocketOwner");
        _spawned.Add(owner);
        _socket = owner.AddComponent<EffectSocketPlayer>();
        typeof(EffectSocketPlayer).GetField("effect", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(_socket, entry);
    }

    [TearDown]
    public void TearDown()
    {
        InstanceSetter.Invoke(null, new object[] { _previousInstance });

        // _pool.Dispose 는 Object.Destroy 를 써서 EditMode 에서 에러가 난다 — 인스턴스는 풀 루트 자식이라 같이 지운다.
        for (int i = _spawned.Count - 1; i >= 0; i--)
        {
            if (_spawned[i] != null) Object.DestroyImmediate(_spawned[i]);
        }
        _spawned.Clear();
    }

    [Test]
    public void Stop_IsGraceful_InstanceStaysForOutro()
    {
        _socket.Play();
        Assert.That(_pool.CountActive(_prefab), Is.EqualTo(1));

        _socket.Stop();

        Assert.That(_socket.IsPlaying, Is.False);
        Assert.That(_pool.CountActive(_prefab), Is.EqualTo(1),
            "서서히 끄기는 새 입자만 멈춘다 — 이미 나온 입자를 든 인스턴스는 종료 시간 동안 남는다(간파 뒤 잔상의 정체)");
    }

    [Test]
    public void StopImmediate_ReturnsInstanceToPoolSameFrame()
    {
        _socket.Play();
        Assert.That(_pool.CountActive(_prefab), Is.EqualTo(1));

        _socket.StopImmediate();

        Assert.That(_socket.IsPlaying, Is.False);
        Assert.That(_pool.CountActive(_prefab), Is.EqualTo(0), "바로 걷기는 같은 프레임에 풀로 돌려보낸다 — 남는 입자 없음");
    }

    [Test]
    public void StopImmediate_WhenNotPlaying_IsNoOp()
    {
        Assert.DoesNotThrow(() => _socket.StopImmediate());
        Assert.That(_pool.CountActive(_prefab), Is.EqualTo(0));
    }

    [Test]
    public void StopImmediate_ThenPlayAgain_Works()
    {
        _socket.Play();
        _socket.StopImmediate();
        _socket.Play();

        Assert.That(_socket.IsPlaying, Is.True);
        Assert.That(_pool.CountActive(_prefab), Is.EqualTo(1));
        _socket.StopImmediate();
    }

    [TestCase(MonsterState.Groggy, true)]
    [TestCase(MonsterState.Idle, false)]
    [TestCase(MonsterState.Chase, false)]
    [TestCase(MonsterState.Dead, false)]
    [TestCase(MonsterState.Hit, false)]
    public void Spinner_OnlyGroggyCutClearsImmediately(MonsterState next, bool expected)
    {
        Assert.That(SpinnerBot.SpinCutClearsImmediately(next), Is.EqualTo(expected));
    }
}
