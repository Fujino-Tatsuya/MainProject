using NUnit.Framework;

/// <summary>
/// 개전 직후 억제할 공격을 고르는 규칙.
///
/// 왜 필요한가 — 보스방 진입 시 플레이어가 멀면 거리창을 통과하는 행이 Jump(거리 무관)와
/// Dash(5~20m) <b>둘뿐</b>이라 룰렛이 반드시 그중 하나를 뽑는다. 확률이 아니라 구조라서
/// 가중치를 낮춰도 안 없어진다. 그래서 개전에는 이 둘만 쿨을 걸어 두고 걸어 들어가게 한다.
/// </summary>
public sealed class BossOpeningAttackPolicyTests
{
    static BossAttackEntry Row(BossAttackId id, float min, float max, bool ignoreWindow = false) =>
        new BossAttackEntry
        {
            attackId = id, minDistance = min, maxDistance = max, ignoreDistanceWindow = ignoreWindow
        };

    /// 근접기 — 개전에 그대로 쓸 수 있어야 한다(억제 대상 아님).
    [TestCase(BossAttackId.LeftHook,  0f, 3f)]
    [TestCase(BossAttackId.RightHook, 0f, 3f)]
    [TestCase(BossAttackId.Upper,     0f, 2.5f)]
    [TestCase(BossAttackId.Grab,      0f, 3.5f)]
    public void MeleeRows_AreNotSuppressed(BossAttackId id, float min, float max)
    {
        Assert.That(BossOpeningAttackPolicy.IsRangedOpener(Row(id, min, max)), Is.False);
    }

    /// 원거리 진입기 — 개전에 억제한다.
    [Test]
    public void Dash_IsSuppressed()
    {
        Assert.That(BossOpeningAttackPolicy.IsRangedOpener(Row(BossAttackId.Dash, 5f, 20f)), Is.True);
    }

    /// 🔴 Jump 는 minDistance 가 0 이지만 거리창을 통째로 무시한다 — minDistance 만 보면 놓친다.
    [Test]
    public void Jump_IsSuppressed_EvenThoughMinDistanceIsZero()
    {
        Assert.That(
            BossOpeningAttackPolicy.IsRangedOpener(Row(BossAttackId.Jump, 0f, 0f, ignoreWindow: true)),
            Is.True);
    }

    /// null 방어 — 공격 테이블에 빈 행이 섞일 수 있다.
    [Test]
    public void NullRow_IsNotSuppressed()
    {
        Assert.That(BossOpeningAttackPolicy.IsRangedOpener(null), Is.False);
    }
}
