using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// A5 피해 숫자 숨김(할 일 154) — 구간 = 오너 강 시도 암전 시작 ~ 성공 섬광 ⑥ 끝(실패면 암전 걷기 끝).
/// 오너 상태 기계 + 섬광 진행기를 디렉터처럼 묶어 규칙(<see cref="InterruptProductionRules.ShouldHideDamageNumbers"/>)을 본다.
/// 약 시도·대상 없음·남의 시도·칸 끔은 안 숨김. 프리팹 기본값 켬.
/// </summary>
public sealed class InterruptDamageNumberHideTests
{
    private const string NetworkManagerPath = "Assets/2.Prefabs/Network/NetworkManager.prefab";

    // 이진 소수로 딱 떨어지는 길이 — ②③④ 2·2·2 프레임 → 섬광 순간 6 프레임, ⑤ 1/8 · ⑥ 1/4 초.
    private sealed class FakeSettings : IInterruptHitFlashSettings, IInterruptOwnerPresentationSettings
    {
        public int HitFlashStarburstFrames => 2;
        public int HitFlashWhiteoutFrames => 2;
        public int HitFlashSmearFrames => 2;
        public float HitFlashAberrationDuration => 0.125f;
        public float HitFlashSettleDuration => 0.25f;
        public InterruptEffectTimeBase HitFlashTimeBase => InterruptEffectTimeBase.Unscaled;

        public float OwnerDimInDuration => 0.125f;
        public float OwnerDimOutDuration => 0.25f;
    }

    private InterruptOwnerPresentation _owner;
    private InterruptHitFlashSequencer _flash;
    private bool _switchOn;

    [SetUp]
    public void SetUp()
    {
        var settings = new FakeSettings();
        _owner = new InterruptOwnerPresentation(settings);
        // 디렉터와 같은 연결 — 섬광 순간에 본인 화면 성공 암전을 끈다.
        _flash = new InterruptHitFlashSequencer(settings, null,
            moment => _owner.SuccessFlashMoment(moment.Event.TriggerId, Now));
        _switchOn = true;
    }

    private float Now;

    private bool Hidden => InterruptProductionRules.ShouldHideDamageNumbers(_switchOn, _owner.State, _owner.DimIntensity, _flash.OverlayActive);

    private void Tick(float now, int frame)
    {
        Now = now;
        _owner.Tick(now);
        _flash.Tick(now, frame);
    }

    // 디렉터 HandleStarted(Success) 의 본인 화면 경로.
    private void OwnerSuccess(uint triggerId, float now, int frame)
    {
        Now = now;
        _owner.ServerStarted(triggerId, SlowMotionPurpose.Success, true, now);
        bool overlay = InterruptHitFlashRules.ShouldPlayOwnerOverlay(SlowMotionPurpose.Success, true, _owner.SuccessDimmed);
        if (overlay)
            _flash.BeginOwnerOverlay(Vector3.zero, Vector3.forward, now, frame);
        _flash.ScheduleFlash(new InterruptHitFlashMoment(InterruptProductionEvent.LocalSuccess(triggerId, 0, Vector3.zero), true, overlay), frame);
    }

    [Test]
    public void 강_시도_입력_프레임부터_숨긴다()
    {
        Assert.IsFalse(Hidden, "시작 전");
        _owner.Predict(InterruptAttemptForecast.Strong, null, 10f);
        Assert.AreEqual(0f, _owner.DimIntensity, "입력 프레임 암전 값은 아직 0");
        Assert.IsTrue(Hidden, "입력 프레임");

        _owner.ServerStarted(1, SlowMotionPurpose.Attempt, true, 10.05f);
        Tick(10.1f, 101);
        Assert.IsTrue(Hidden, "강 시도 유지");
    }

    [Test]
    public void 성공은_섬광_가라앉음_끝까지_숨기고_그_뒤_보인다()
    {
        _owner.Predict(InterruptAttemptForecast.Strong, null, 10f);
        _owner.ServerStarted(1, SlowMotionPurpose.Attempt, true, 10f);
        Tick(10.25f, 110);

        OwnerSuccess(2, 10.5f, 200);
        Assert.IsTrue(Hidden, "성공 순간");

        Tick(10.5f, 206); // 섬광 순간 — 암전 0, ⑤ 시작
        Assert.AreEqual(0f, _owner.DimIntensity, "섬광 순간 암전 끔");
        Assert.IsTrue(_flash.OverlayActive);
        Assert.IsTrue(Hidden, "⑤ 색수차");

        Tick(10.5f + 0.25f, 210);
        Assert.AreEqual(InterruptHitFlashStage.Settle, _flash.OverlayStage);
        Assert.IsTrue(Hidden, "⑥ 가라앉음");

        Tick(10.5f + 0.5f, 220);
        Assert.IsFalse(_flash.OverlayActive);
        Assert.IsFalse(Hidden, "⑥ 끝");
    }

    [Test]
    public void 실패는_암전_걷기_끝까지_숨긴다()
    {
        _owner.Predict(InterruptAttemptForecast.Strong, null, 10f);
        _owner.ServerStarted(1, SlowMotionPurpose.Attempt, true, 10f);
        Tick(10.25f, 110);

        _owner.ServerFailed(1, 10.5f);
        Assert.AreEqual(InterruptOwnerPresentationState.Failed, _owner.State);
        Tick(10.625f, 120);
        Assert.IsTrue(Hidden, "걷는 중");

        Tick(10.75f, 130);
        Assert.AreEqual(InterruptOwnerPresentationState.None, _owner.State);
        Assert.IsFalse(Hidden, "걷기 끝");
    }

    [Test]
    public void 판정_전_종료도_걷기_끝에_보인다()
    {
        _owner.Predict(InterruptAttemptForecast.Strong, null, 10f);
        Tick(10.25f, 110);
        _owner.LocalEnded(10.5f);
        Tick(10.625f, 120);
        Assert.IsTrue(Hidden);
        Tick(10.75f, 130);
        Assert.IsFalse(Hidden);
    }

    [Test]
    public void 약_시도와_그_성공은_안_숨긴다()
    {
        _owner.Predict(InterruptAttemptForecast.Weak, null, 10f);
        Assert.IsFalse(Hidden, "약 입력");
        _owner.ServerStarted(1, SlowMotionPurpose.AttemptWeak, true, 10f);
        Tick(10.25f, 110);
        Assert.IsFalse(Hidden, "약 시도");

        OwnerSuccess(2, 10.5f, 200);
        Assert.IsFalse(_owner.SuccessDimmed);
        Tick(10.5f, 206);
        Assert.IsFalse(Hidden, "약 시도 성공 — 섬광 오버레이 없음");
    }

    [Test]
    public void 대상_없음은_안_숨긴다()
    {
        Assert.IsFalse(_owner.Predict(InterruptAttemptForecast.NoTarget, null, 10f));
        Tick(10.25f, 110);
        Assert.IsFalse(Hidden);
    }

    [Test]
    public void 오너_아님_팀원_슬로우는_안_숨긴다()
    {
        // 팀원 화면 — 오너 예측이 없고, 남의 시도·성공 슬로우는 오너 상태 기계가 무시한다. 오버레이도 본인 성공만.
        _owner.ServerStarted(1, SlowMotionPurpose.Attempt, false, 10f);
        Tick(10.25f, 110);
        Assert.IsFalse(Hidden, "남의 강 시도");

        _owner.ServerStarted(2, SlowMotionPurpose.Success, false, 10.5f);
        Assert.IsFalse(InterruptHitFlashRules.ShouldPlayOwnerOverlay(SlowMotionPurpose.Success, false, _owner.SuccessDimmed));
        Tick(10.5f, 206);
        Assert.IsFalse(Hidden, "남의 성공");
    }

    [Test]
    public void 칸을_끄면_강_시도도_성공도_안_숨긴다()
    {
        _switchOn = false;
        _owner.Predict(InterruptAttemptForecast.Strong, null, 10f);
        Tick(10.25f, 110);
        Assert.IsFalse(Hidden, "강 시도");

        OwnerSuccess(2, 10.5f, 200);
        Tick(10.5f, 206);
        Assert.IsTrue(_flash.OverlayActive);
        Assert.IsFalse(Hidden, "섬광 중");
    }

    [Test]
    public void 규칙_조건별()
    {
        Assert.IsTrue(InterruptProductionRules.ShouldHideDamageNumbers(true, InterruptOwnerPresentationState.AttemptStrong, 0f, false));
        Assert.IsTrue(InterruptProductionRules.ShouldHideDamageNumbers(true, InterruptOwnerPresentationState.Failed, 0.5f, false));
        Assert.IsTrue(InterruptProductionRules.ShouldHideDamageNumbers(true, InterruptOwnerPresentationState.None, 0f, true));
        Assert.IsFalse(InterruptProductionRules.ShouldHideDamageNumbers(true, InterruptOwnerPresentationState.AttemptWeak, 0f, false));
        Assert.IsFalse(InterruptProductionRules.ShouldHideDamageNumbers(true, InterruptOwnerPresentationState.None, 0f, false));
        Assert.IsFalse(InterruptProductionRules.ShouldHideDamageNumbers(false, InterruptOwnerPresentationState.AttemptStrong, 1f, true));
    }

    [Test]
    public void 디렉터가_없으면_안_숨긴다()
    {
        Assume.That(InterruptProductionDirector.Instance == null, "EditMode 에 인스턴스가 살아 있다");
        Assert.IsFalse(InterruptProductionDirector.DamageNumbersHidden);
    }

    [Test]
    public void 칸_기본값은_켬_코드와_프리팹()
    {
        var go = new GameObject("DirectorDefaults");
        try
        {
            Assert.IsTrue(go.AddComponent<InterruptProductionDirector>().HideDamageNumbers, "코드 기본값");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPath);
        Assert.IsNotNull(prefab, NetworkManagerPath);
        Assert.IsTrue(prefab.GetComponent<InterruptProductionDirector>().HideDamageNumbers, "NetworkManager.prefab");
    }
}
