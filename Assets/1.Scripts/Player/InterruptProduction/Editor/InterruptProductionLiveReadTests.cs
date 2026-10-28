using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 간파 연출 디렉터 "캐싱 금지" 회귀(할 일 118 C4) — 디렉터를 만든 뒤 인스펙터 필드를 바꾸면 바로 다음 사건에 그 값이 쓰인다.
/// Play 중 인스펙터로 바로 바꿔 보는 사용 방식의 보장.
/// </summary>
public sealed class InterruptProductionLiveReadTests
{
    private sealed class FakeDarkAdaptation : IInterruptDarkAdaptationOutput
    {
        public int Plays;
        public void Play(bool viewerIsCaster, ulong casterClientId, Vector3 targetPosition) => Plays++;
        public void Stop() { }
    }

    private GameObject _go;
    private InterruptProductionDirector _director;
    private InterruptProductionStageTests.FakeTimeScale _port;
    private InterruptAttemptStage _attempt;
    private InterruptSuccessStage _success;

    [SetUp]
    public void SetUp()
    {
        _go = new GameObject("Director");
        _director = _go.AddComponent<InterruptProductionDirector>();
        _port = new InterruptProductionStageTests.FakeTimeScale();
        // 단계는 디렉터가 만드는 것과 같게 설정 = 디렉터, 시간 = 포트로 만든다(만든 뒤 필드를 바꾼다).
        _attempt = new InterruptAttemptStage(_director, _port);
        _success = new InterruptSuccessStage(_director, _port);
    }

    [TearDown]
    public void TearDown()
    {
        if (_go != null)
            Object.DestroyImmediate(_go);
    }

    private void Set(string path, bool value)
    {
        var so = new SerializedObject(_director);
        so.FindProperty(path).boolValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private void Set(string path, float value)
    {
        var so = new SerializedObject(_director);
        so.FindProperty(path).floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static InterruptProductionEvent Success =>
        InterruptProductionEvent.Success(1, null, null, true, Vector3.one, Vector3.zero);

    [Test]
    public void 히트스톱_칸을_바꾸면_다음_성공에_바로_쓰인다()
    {
        _success.Server(Success, null);
        Set("hitStop", true);
        _success.Server(Success, null);
        Set("hitStop", false);
        _success.Server(Success, null);

        Assert.IsFalse(_port.Starts[0].hitStopOn);
        Assert.IsTrue(_port.Starts[1].hitStopOn);
        Assert.IsFalse(_port.Starts[2].hitStopOn);
    }

    [Test]
    public void 성공_프로필_값을_바꾸면_다음_성공에_바로_쓰인다()
    {
        Set("interruptSuccessProfile.holdDuration", 2.5f);
        _success.Server(Success, null);
        Assert.AreEqual(2.5f, _port.Starts[0].profile.holdDuration);

        Set("interruptSuccessProfile.holdDuration", 0.7f);
        _success.Server(Success, null);
        Assert.AreEqual(0.7f, _port.Starts[1].profile.holdDuration);
    }

    [Test]
    public void 시도_프로필_값을_바꾸면_다음_시도에_바로_쓰인다()
    {
        Set("interruptProfile.scale", 0.5f);
        _attempt.Server(InterruptProductionEvent.Attempt(1, null, null, BeaverLobby.Player.Dash.InterruptAttemptForecast.Strong));
        Assert.AreEqual(0.5f, _port.Starts[0].profile.scale);
    }

    [Test]
    public void 피어_칸을_바꾸면_다음_성공_연출에_바로_쓰인다()
    {
        var dark = new FakeDarkAdaptation();
        var e = InterruptProductionEvent.LocalSuccess(1, 1, Vector3.zero);

        _success.LocalStarted(SlowMotionPurpose.Success, e, true, dark, null);
        Set("darkAdaptation", true);
        _success.LocalStarted(SlowMotionPurpose.Success, e, true, dark, null);

        Assert.AreEqual(1, dark.Plays);
    }

    [Test]
    public void 오너_줌_칸을_바꾸면_다음_프레임_질의에_바로_쓰인다()
    {
        Assert.AreEqual(1f, _success.OwnerZoomFactor(0.85f));
        Set("ownerZoom", true);
        Assert.AreEqual(0.85f, _success.OwnerZoomFactor(0.85f));
    }
}
