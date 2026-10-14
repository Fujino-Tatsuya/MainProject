using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 수직 낙하 포격 판정 규칙(PLAN-monster-tells M8) — 원 반경은 평면(XZ) 거리(몸 두께 포함), 높이는 착탄 바닥 ± 허용치.
/// 위층·다리 아래는 안 맞아야 한다.
/// </summary>
public sealed class MonsterSkyStrikeTests
{
    static readonly Vector3 Floor0 = new Vector3(10f, 0f, 10f);

    [Test]
    public void SameFloor_InsideRadius_Hits()
    {
        Assert.IsTrue(MonsterSkyStrike.InFootprint(Floor0, 1.5f, 1.5f, new Vector3(11.4f, 0.8f, 10f), 0f));
    }

    [Test]
    public void OutsideRadius_Misses_EvenIfBodyHigh()
    {
        Assert.IsFalse(MonsterSkyStrike.InFootprint(Floor0, 1.5f, 1.5f, new Vector3(11.6f, 0.8f, 10f), 0f));
    }

    [Test]
    public void RadiusIsPlanar_HeightDoesNotShrinkIt()
    {
        // 구(球)였다면 몸통 높이(1.2m) 때문에 반경 1.5 안쪽 1.4m 지점도 빠졌다 — 평면 거리라 맞는다.
        Assert.IsTrue(MonsterSkyStrike.InFootprint(Floor0, 1.5f, 1.5f, new Vector3(11.4f, 1.2f, 10f), 0.2f));
    }

    [Test]
    public void UpperFloor_Misses()
    {
        // 맵의 위층 높이 2.94(ZoneL_typeB) — 같은 XZ 라도 발 높이가 허용치 밖이면 안 맞는다.
        Assert.IsFalse(MonsterSkyStrike.InFootprint(Floor0, 1.5f, 1.5f, new Vector3(10f, 3.5f, 10f), 2.94f));
    }

    [Test]
    public void SmallStep_WithinTolerance_Hits()
    {
        Assert.IsTrue(MonsterSkyStrike.InFootprint(Floor0, 1.5f, 1.5f, new Vector3(10.5f, 1.0f, 10f), 0.4f));
    }

    [Test]
    public void RangedStyleEnum_KeepsSerializedOrder()
    {
        Assert.AreEqual(0, (int)MonsterRangedStyle.Projectile);
        Assert.AreEqual(1, (int)MonsterRangedStyle.SkyStrike);
    }
}
