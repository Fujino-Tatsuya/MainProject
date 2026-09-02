using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 카운터 성공 누적 → 그로기/Break 판정 테스트.
///
/// 규칙(설계 §3.3): 성공마다 카운트가 오르고, 임계에 닿으면 카운트를 <b>0 으로 리셋</b>하며 Break 로 간다.
/// 반환 <c>Duration</c> 은 <b>전체 행동 불능 시간</b>이다 — 앞에 Hit 리액션을 따로 더하지 않는다.
/// </summary>
public sealed class BossCounterProgressTests
{
    // current / allowBreak / 기대 NextCount / 기대 IsBreak / 기대 Duration   (임계 5, 그로기 0.5, Break 2)
    [TestCase(0, true, 1, false, 0.5f)]  // 첫 성공
    [TestCase(3, true, 4, false, 0.5f)]  // 임계 직전
    [TestCase(4, true, 0, true,  2f)]    // 5회째 = Break, 카운트 리셋
    [TestCase(4, false, 5, false, 0.5f)] // 송전기 실패(S7) — 카운트는 올리되 Break 로 승격하지 않는다
    public void Resolve_ReturnsExpectedOutcome(
        int current, bool allowBreak, int next, bool isBreak, float duration)
    {
        BossCounterOutcome result = BossCounterProgress.Resolve(current, 5, allowBreak, 0.5f, 2f);
        Assert.That(result.NextCount, Is.EqualTo(next));
        Assert.That(result.IsBreak, Is.EqualTo(isBreak));
        Assert.That(result.Duration, Is.EqualTo(duration));
    }

    /// 🔴 임계 1 — maxGroggyCount 는 SO 로 조절되므로 1 도 저작 가능하다.
    /// 이때는 첫 성공이 곧 Break 여야 한다(임계 비교가 > 가 아니라 >= 인지 고정).
    [Test]
    public void Threshold_One_BreaksOnFirstSuccess()
    {
        BossCounterOutcome result = BossCounterProgress.Resolve(0, 1, true, 0.5f, 2f);
        Assert.That(result.IsBreak, Is.True);
        Assert.That(result.NextCount, Is.EqualTo(0));
        Assert.That(result.Duration, Is.EqualTo(2f));
    }

    /// 창 길이는 공격 행마다 저작한다. 인스펙터 범위가 설계(0~2초)와 어긋나면 튜닝이 조용히 벗어난다.
    [Test]
    public void CounterDuration_HasZeroToTwoRange()
    {
        var field = typeof(BossAttackEntry).GetField(nameof(BossAttackEntry.counterWindowDuration));
        var range = field?.GetCustomAttributes(typeof(UnityEngine.RangeAttribute), false)
            .Cast<UnityEngine.RangeAttribute>().SingleOrDefault();
        Assert.That(range?.min, Is.EqualTo(0f));
        Assert.That(range?.max, Is.EqualTo(2f));
    }
}
