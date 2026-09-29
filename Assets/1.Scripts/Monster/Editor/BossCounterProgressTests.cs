using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 간파 게이지 → 그로기/제압 판정 테스트(팀 기획 `Re_C_간파_시스템.md` §10).
///
/// 규칙: 성공마다 게이지가 step 만큼 깎이고 0 에 닿으면 제압. 0 미만으로 내려가지 않는다.
/// 반환 <c>Duration</c> 은 <b>전체 행동 불능 시간</b>이다 — 앞에 Hit 리액션을 따로 더하지 않는다.
/// </summary>
public sealed class BossCounterProgressTests
{
    // current / 기대 NextGauge / 기대 IsSuppress / 기대 Duration   (step 20, 그로기 1.5, 제압 5)
    [TestCase(100f, 80f, false, 1.5f)] // 첫 성공
    [TestCase(40f, 20f, false, 1.5f)]  // 제압 직전
    [TestCase(20f, 0f, true, 5f)]      // 5회째 = 제압
    [TestCase(10f, 0f, true, 5f)]      // 환경 감소 뒤 남은 10 — 0 미만 없이 제압
    [TestCase(-5f, 0f, true, 5f)]      // 음수 방어
    [TestCase(150f, 80f, false, 1.5f)] // 초과 방어(100 으로 본다)
    public void Resolve_ReturnsExpectedOutcome(float current, float next, bool isSuppress, float duration)
    {
        BossCounterOutcome result = BossCounterProgress.Resolve(current, 20f, 1.5f, 5f);
        Assert.That(result.NextGauge, Is.EqualTo(next).Within(0.0001f));
        Assert.That(result.IsSuppress, Is.EqualTo(isSuppress));
        Assert.That(result.Duration, Is.EqualTo(duration));
    }

    /// 환경 상호작용 없이 간파 성공만으로는 **5회**에 제압(기획 §10.3 진행 예시).
    [Test]
    public void FiveSuccesses_WithoutEnvironment_Suppress()
    {
        float g = BossCounterProgress.GaugeMax;
        for (int i = 0; i < 4; i++)
        {
            BossCounterOutcome r = BossCounterProgress.Resolve(g, 20f, 1.5f, 5f);
            Assert.That(r.IsSuppress, Is.False, $"{i + 1}회째에 제압되면 안 된다");
            g = r.NextGauge;
        }
        Assert.That(BossCounterProgress.Resolve(g, 20f, 1.5f, 5f).IsSuppress, Is.True);
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
