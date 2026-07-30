using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class GameManagerMainGameReadyTests
{
    private static readonly MethodInfo SetStateMethod =
        typeof(GameManager).GetMethod("SetState", BindingFlags.Instance | BindingFlags.NonPublic);

    private GameObject _gameObject;
    private GameManager _gameManager;

    [SetUp]
    public void SetUp()
    {
        _gameObject = new GameObject("GameManagerTest");
        _gameManager = _gameObject.AddComponent<GameManager>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_gameObject);
    }

    [Test]
    public void NotifyMainGameReady_IsIdempotent()
    {
        var invocationCount = 0;
        _gameManager.OnMainGameReady += () => invocationCount++;

        _gameManager.NotifyMainGameReady();
        _gameManager.NotifyMainGameReady();

        Assert.That(_gameManager.IsMainGameReady, Is.True);
        Assert.That(invocationCount, Is.EqualTo(1));
    }

    [Test]
    public void LeavingMainGame_ResetsReadyState()
    {
        SetState(GameManager.GameState.MainGame);
        _gameManager.NotifyMainGameReady();

        SetState(GameManager.GameState.Result);

        Assert.That(_gameManager.IsMainGameReady, Is.False);
    }

    [Test]
    public void LateSubscriber_CanHandleReadyFromCurrentState()
    {
        _gameManager.NotifyMainGameReady();

        var handled = false;
        if (_gameManager.IsMainGameReady)
        {
            handled = true;
        }
        else
        {
            _gameManager.OnMainGameReady += () => handled = true;
        }

        Assert.That(handled, Is.True);
    }

    private void SetState(GameManager.GameState state)
    {
        Assert.That(SetStateMethod, Is.Not.Null);
        SetStateMethod.Invoke(_gameManager, new object[] { state });
    }
}
