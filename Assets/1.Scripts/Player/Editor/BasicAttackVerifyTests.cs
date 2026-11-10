using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 256 ① — 평타 오너 예측의 서버 2차 검증 규칙: 타 순서(직전 +1·루프 0·새 시작 0)·기대 간격(같은 타 히트 간 / 다른 타 Hit→End + 진입→Hit, 루프백 진입)·
/// 여유·최근 0.5초 최대 공속·(타, 히트) 중복·새 콤보 첫 히트 하한 없음·거너 고정 간격. 문서: Docs/tech/owner-predicted-basic-attack.md §3.
/// </summary>
public class BasicAttackVerifyTests
{
    static readonly BasicAttackVerifySettings Settings = new BasicAttackVerifySettings(0.15f, 0.5f);

    // 4타 콤보 — 타마다 Hit 0.3·End 0.6, 4타는 다단(0.2·0.4), 1타 루프백 진입 0.1.
    static BasicAttackStepTiming[] Timeline() => new[]
    {
        new BasicAttackStepTiming(new[] { 0.3f }, 0.6f, 0.1f),
        new BasicAttackStepTiming(new[] { 0.3f }, 0.6f),
        new BasicAttackStepTiming(new[] { 0.3f }, 0.6f),
        new BasicAttackStepTiming(new[] { 0.2f, 0.4f }, 0.7f),
    };

    // ── 순서 ─────────────────────────────────────────────

    [Test]
    public void Order_NewCombo_OnlyZero()
    {
        Assert.IsTrue(BasicAttackVerifyRules.IsValidStepOrder(false, -1, 0, 4, true));
        Assert.IsFalse(BasicAttackVerifyRules.IsValidStepOrder(false, -1, 1, 4, true));
        Assert.IsFalse(BasicAttackVerifyRules.IsValidStepOrder(false, 2, 3, 4, true), "새 시작은 직전과 무관하게 0 만");
    }

    [Test]
    public void Order_Continue_PlusOne_OrLoopZero()
    {
        Assert.IsTrue(BasicAttackVerifyRules.IsValidStepOrder(true, 0, 1, 4, true));
        Assert.IsTrue(BasicAttackVerifyRules.IsValidStepOrder(true, 3, 0, 4, true));
        Assert.IsFalse(BasicAttackVerifyRules.IsValidStepOrder(true, 3, 0, 4, false), "루프 없는 정책은 마지막 뒤 0 금지");
        Assert.IsFalse(BasicAttackVerifyRules.IsValidStepOrder(true, 1, 3, 4, true), "건너뛰기");
        Assert.IsFalse(BasicAttackVerifyRules.IsValidStepOrder(true, 1, 1, 4, true), "같은 타 반복");
        Assert.IsFalse(BasicAttackVerifyRules.IsValidStepOrder(true, 1, 0, 4, true), "중간에서 0");
        Assert.IsFalse(BasicAttackVerifyRules.IsValidStepOrder(true, 3, 4, 4, true), "범위 밖");
    }

    // ── 기대 간격 ─────────────────────────────────────────

    [Test]
    public void Expected_SameSwing_IsHitGap()
    {
        BasicAttackStepTiming[] t = Timeline();
        Assert.AreEqual(0.2f, BasicAttackVerifyRules.ExpectedInterval(t[3], 0, t[3], 1, true, false), 1e-5f);
    }

    [Test]
    public void Expected_NextSwing_TailPlusLead()
    {
        BasicAttackStepTiming[] t = Timeline();
        // 1타 Hit 0.3 → End 0.6 (0.3) + 2타 진입 0 → Hit 0.3 (0.3)
        Assert.AreEqual(0.6f, BasicAttackVerifyRules.ExpectedInterval(t[0], 0, t[1], 0, false, false), 1e-5f);
        // 4타 두 번째 Hit 0.4 → End 0.7 (0.3) + 1타 루프백 진입 0.1 → Hit 0.3 (0.2)
        Assert.AreEqual(0.5f, BasicAttackVerifyRules.ExpectedInterval(t[3], 1, t[0], 0, false, true), 1e-5f);
    }

    [Test]
    public void Interval_SlackAndSpeed()
    {
        Assert.IsTrue(BasicAttackVerifyRules.PassesInterval(0.46f, 0.6f, 1f, 0.15f), "여유 안 = 통과");
        Assert.IsFalse(BasicAttackVerifyRules.PassesInterval(0.44f, 0.6f, 1f, 0.15f));
        Assert.IsTrue(BasicAttackVerifyRules.PassesInterval(0.2f, 0.6f, 2f, 0.15f), "공속 2 → 기대 0.3");
        Assert.IsTrue(BasicAttackVerifyRules.PassesInterval(0.46f, 0.6f, 0f, 0.15f), "공속 0 은 1 로");
    }

    // ── 공속 기록 ─────────────────────────────────────────

    [Test]
    public void SpeedHistory_MaxInWindow_IncludesValueAtWindowStart()
    {
        var h = new BasicAttackSpeedHistory();
        Assert.AreEqual(1.3f, h.MaxInWindow(0, 0.5f, 1.3f), "기록 없음 = 대체값");

        h.Record(0.0, 2.4f); // 버프
        h.Record(1.0, 1.2f); // 풀림
        Assert.AreEqual(2.4f, h.MaxInWindow(1.3, 0.5f, 1f), 1e-5f, "풀린 지 0.3초 = 창 안 → 버프값");
        Assert.AreEqual(1.2f, h.MaxInWindow(1.6, 0.5f, 1f), 1e-5f, "0.6초 = 창 밖, 창 시작 때 유효값 1.2");
        Assert.AreEqual(1.2f, h.MaxInWindow(100.0, 0.5f, 1f), 1e-5f, "오래 지나도 마지막 값 유효");
    }

    // ── 검증기 ───────────────────────────────────────────

    [Test]
    public void Verifier_NormalCombo_AllAccepted()
    {
        var v = new BasicAttackServerVerifier();
        BasicAttackStepTiming[] t = Timeline();
        double now = 10;

        Assert.IsTrue(v.BeginSwing(1, 0, 4, true, false));
        Assert.AreEqual(BasicAttackHitVerdict.Accepted, v.VerifyHit(now + 0.3, 1, 0, 0, t, 1f, Settings));
        Assert.IsTrue(v.BeginSwing(2, 1, 4, true, true));
        Assert.AreEqual(BasicAttackHitVerdict.Accepted, v.VerifyHit(now + 0.9, 2, 1, 0, t, 1f, Settings));
        Assert.IsTrue(v.BeginSwing(3, 2, 4, true, true));
        Assert.AreEqual(BasicAttackHitVerdict.Accepted, v.VerifyHit(now + 1.5, 3, 2, 0, t, 1f, Settings));
        Assert.IsTrue(v.BeginSwing(4, 3, 4, true, true));
        Assert.AreEqual(BasicAttackHitVerdict.Accepted, v.VerifyHit(now + 2.0, 4, 3, 0, t, 1f, Settings));
        Assert.AreEqual(BasicAttackHitVerdict.Accepted, v.VerifyHit(now + 2.2, 4, 3, 1, t, 1f, Settings));
        Assert.IsTrue(v.BeginSwing(5, 0, 4, true, true), "루프 4→1");
        Assert.AreEqual(BasicAttackHitVerdict.Accepted, v.VerifyHit(now + 2.7, 5, 0, 0, t, 1f, Settings));
    }

    [Test]
    public void Verifier_TooFast_NoDamage_AndNotRecorded()
    {
        var v = new BasicAttackServerVerifier();
        BasicAttackStepTiming[] t = Timeline();

        v.BeginSwing(1, 0, 4, true, false);
        Assert.AreEqual(BasicAttackHitVerdict.Accepted, v.VerifyHit(10.3, 1, 0, 0, t, 1f, Settings));
        v.BeginSwing(2, 1, 4, true, true);
        Assert.AreEqual(BasicAttackHitVerdict.TooFast, v.VerifyHit(10.6, 2, 1, 0, t, 1f, Settings), "0.3 + 0.15 < 0.6");
        // 거부된 히트는 기준이 아니다 — 같은 (타, 히트) 다시 와도 중복이 아니라 간격으로 본다.
        Assert.AreEqual(BasicAttackHitVerdict.Accepted, v.VerifyHit(10.8, 2, 1, 0, t, 1f, Settings));
    }

    [Test]
    public void Verifier_UsesRecentMaxAttackSpeed()
    {
        var v = new BasicAttackServerVerifier();
        BasicAttackStepTiming[] t = Timeline();
        v.RecordAttackSpeed(0, 2f);

        v.BeginSwing(1, 0, 4, true, false);
        v.VerifyHit(10.15, 1, 0, 0, t, 2f, Settings);
        v.BeginSwing(2, 1, 4, true, true);
        Assert.AreEqual(BasicAttackHitVerdict.Accepted, v.VerifyHit(10.45, 2, 1, 0, t, 2f, Settings), "기대 0.6 ÷ 2 = 0.3");

        v.RecordAttackSpeed(10.5, 1f);
        v.BeginSwing(3, 2, 4, true, true);
        Assert.AreEqual(BasicAttackHitVerdict.Accepted, v.VerifyHit(10.75, 3, 2, 0, t, 1f, Settings), "공속이 막 풀려도 0.5초 안은 2 로 본다");
    }

    [Test]
    public void Verifier_Duplicate_SameSwingSameHit()
    {
        var v = new BasicAttackServerVerifier();
        BasicAttackStepTiming[] t = Timeline();
        v.BeginSwing(7, 3, 4, true, false); // 순서 위반 → 아래에서 따로
        v.EndCombo();

        v.BeginSwing(1, 0, 4, true, false);
        Assert.AreEqual(BasicAttackHitVerdict.Accepted, v.VerifyHit(10.3, 1, 0, 0, t, 1f, Settings));
        Assert.AreEqual(BasicAttackHitVerdict.Duplicate, v.VerifyHit(11.0, 1, 0, 0, t, 1f, Settings));
    }

    [Test]
    public void Verifier_OutOfOrder_HitsRejected_MotionNotBlocked()
    {
        var v = new BasicAttackServerVerifier();
        BasicAttackStepTiming[] t = Timeline();

        Assert.IsFalse(v.BeginSwing(1, 2, 4, true, false), "새 시작인데 3타");
        Assert.IsTrue(v.ComboActive, "거부하지 않고 상태는 따라간다");
        Assert.AreEqual(BasicAttackHitVerdict.OutOfOrder, v.VerifyHit(10.3, 1, 2, 0, t, 1f, Settings));
    }

    [Test]
    public void Verifier_UnknownSwing()
    {
        var v = new BasicAttackServerVerifier();
        BasicAttackStepTiming[] t = Timeline();
        Assert.AreEqual(BasicAttackHitVerdict.UnknownSwing, v.VerifyHit(1, 1, 0, 0, t, 1f, Settings), "시작 통지 없음");
        v.BeginSwing(1, 0, 4, true, false);
        Assert.AreEqual(BasicAttackHitVerdict.UnknownSwing, v.VerifyHit(1.3, 2, 0, 0, t, 1f, Settings), "다른 일련번호");
        Assert.AreEqual(BasicAttackHitVerdict.UnknownSwing, v.VerifyHit(1.3, 1, 1, 0, t, 1f, Settings), "다른 타");
    }

    [Test]
    public void Verifier_NewComboAfterEnd_FirstHitHasNoLowerBound()
    {
        var v = new BasicAttackServerVerifier();
        BasicAttackStepTiming[] t = Timeline();

        v.BeginSwing(1, 0, 4, true, false);
        v.VerifyHit(10.3, 1, 0, 0, t, 1f, Settings);
        v.EndCombo();
        v.BeginSwing(2, 0, 4, true, false);
        Assert.AreEqual(BasicAttackHitVerdict.Accepted, v.VerifyHit(10.31, 2, 0, 0, t, 1f, Settings));
    }

    [Test]
    public void Verifier_ContinueAfterServerEnded_TreatedAsNewStart()
    {
        var v = new BasicAttackServerVerifier();
        v.BeginSwing(1, 0, 4, true, false);
        v.EndCombo(); // 서버가 끊음(넉백 등) — 오너의 '이어짐' 통지가 엇갈려 도착
        Assert.IsFalse(v.BeginSwing(2, 1, 4, true, true), "끊긴 뒤 이어짐 = 새 시작, 0 이 아니면 위반");
        v.EndCombo();
        Assert.IsTrue(v.BeginSwing(3, 0, 4, true, true));
    }

    [Test]
    public void Verifier_FixedInterval_Gunner()
    {
        var v = new BasicAttackServerVerifier();
        Assert.AreEqual(BasicAttackHitVerdict.Accepted, v.VerifyFixedInterval(5.0, 0.3f, 1f, Settings), "첫 발");
        Assert.AreEqual(BasicAttackHitVerdict.Accepted, v.VerifyFixedInterval(5.16, 0.3f, 1f, Settings), "0.16 + 0.15 ≥ 0.3");
        Assert.AreEqual(BasicAttackHitVerdict.TooFast, v.VerifyFixedInterval(5.21, 0.3f, 1f, Settings));
        v.EndCombo();
        Assert.AreEqual(BasicAttackHitVerdict.Accepted, v.VerifyFixedInterval(5.22, 0.3f, 1f, Settings), "새 연사");
    }

    // ── 클립 이벤트 읽기 ───────────────────────────────────

    [Test]
    public void StepTiming_FromClip_ReadsHitAndEnd_DividedByStateSpeed()
    {
        var clip = new AnimationClip();
        try
        {
            clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1.2f, 1f));
            UnityEditor.AnimationUtility.SetAnimationEvents(clip, new[]
            {
                new AnimationEvent { time = 0.6f, functionName = "F", intParameter = 0 },
                new AnimationEvent { time = 0.3f, functionName = "F", intParameter = 0 },
                new AnimationEvent { time = 0.4f, functionName = "F", intParameter = 1 },
                new AnimationEvent { time = 0.9f, functionName = "F", intParameter = 3 },
                new AnimationEvent { time = 0.5f, functionName = "Other", intParameter = 0 },
            });

            BasicAttackStepTiming timing = BasicAttackStepTiming.FromClip(clip, "F", 0, 3, 1.5f, 0.15f);
            Assert.AreEqual(2, timing.HitCount);
            Assert.AreEqual(0.2f, timing.HitTime(0), 1e-4f, "시각 순 정렬 + ÷ 상태 속도");
            Assert.AreEqual(0.4f, timing.HitTime(1), 1e-4f);
            Assert.AreEqual(0.6f, timing.EndTime, 1e-4f);
            Assert.AreEqual(0.1f, timing.LoopBackEntryTime, 1e-4f);

            BasicAttackStepTiming noEnd = BasicAttackStepTiming.FromClip(clip, "F", 0, 7);
            Assert.AreEqual(clip.length, noEnd.EndTime, 1e-4f, "End 없음 = 클립 길이");
        }
        finally
        {
            Object.DestroyImmediate(clip);
        }
    }

    [Test]
    public void Settings_DefaultsWithoutManager()
    {
        BasicAttackVerifySettings s = BasicAttackVerifySettings.Current;
        Assert.AreEqual(0.15f, s.IntervalSlack, 1e-5f);
        Assert.AreEqual(0.5f, s.AttackSpeedWindow, 1e-5f);
    }
}
