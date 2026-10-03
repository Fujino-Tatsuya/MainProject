using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 몬스터 animator.speed 규칙. 고정할 것은 <b>어느 상태에서 어느 값을 쓰는가</b>의 갈래와 블렌드 산식이다.
/// 측정값(W·주기)은 튜닝·아트 대상이라 잠그지 않는다 — 아래 숫자는 산식 확인용 임의값이다.
/// </summary>
public sealed class MonsterAnimSpeedPolicyTests
{
    static readonly Vector2 Range = new Vector2(0.5f, 2.5f);

    static MonsterAnimSpeedPolicy.Locomotion Loco(float W, float th = 0f, float idle = 0f, float move = 0f, Vector2? range = null) =>
        new MonsterAnimSpeedPolicy.Locomotion
        {
            clipSpeed = W, fullBlendSpeed = th, idleCycle = idle, moveCycle = move, range = range ?? Range,
        };

    [Test]
    public void 공격_애니_재생중이면_공격속도()
    {
        Assert.AreEqual(2f, MonsterAnimSpeedPolicy.Resolve(false, 0f, true, 2f, Loco(2f)), 1e-5f);
    }

    [Test]
    public void 로직은_Attack_이어도_이동_애니가_나오면_공격속도를_안_건다()
    {
        // Gauntlet 펀치 예고: 상태는 Attack, 화면은 대기 애니.
        Assert.AreEqual(1f, MonsterAnimSpeedPolicy.Resolve(true, 0f, true, 2f, Loco(2f)), 1e-5f);
    }

    [Test]
    public void 피격_그로기_사망은_1()
    {
        Assert.AreEqual(1f, MonsterAnimSpeedPolicy.Resolve(false, 0f, false, 2f, Loco(2f)), 1e-5f);
    }

    [Test]
    public void 이동중이면_실제속도_나누기_클립속도()
    {
        Assert.AreEqual(2.25f, MonsterAnimSpeedPolicy.Resolve(true, 4.5f, false, 1f, Loco(2f)), 1e-5f);
    }

    [Test]
    public void 이동_재생속도는_범위로_자른다()
    {
        Assert.AreEqual(2.5f, MonsterAnimSpeedPolicy.LocomotionSpeed(20f, Loco(2f)), 1e-5f);
        Assert.AreEqual(0.5f, MonsterAnimSpeedPolicy.LocomotionSpeed(0.2f, Loco(2f)), 1e-5f);
    }

    [Test]
    public void 클립속도_0이면_맞추지_않는다()
    {
        Assert.AreEqual(1f, MonsterAnimSpeedPolicy.LocomotionSpeed(4.5f, Loco(0f)), 1e-5f);
    }

    [Test]
    public void 블렌드_임계값_아래에선_비중만큼_발이_덜_나간다_주기가_같으면()
    {
        // 주기 정보가 없으면(0) 같은 주기로 본다 → foot = w·W, 재생 속도 = th/W 로 고정.
        Assert.AreEqual(1f, MonsterAnimSpeedPolicy.LocomotionSpeed(1f, Loco(2f, th: 2f)), 1e-5f);
        Assert.AreEqual(1f, MonsterAnimSpeedPolicy.LocomotionSpeed(0.5f, Loco(2f, th: 2f)), 1e-5f);
        Assert.AreEqual(2.25f, MonsterAnimSpeedPolicy.LocomotionSpeed(4.5f, Loco(2f, th: 2f)), 1e-5f);
    }

    [Test]
    public void 대기_주기가_길면_섞인_이동_클립이_느려진다()
    {
        // 1D 블렌드 정규화 시간 동기: w=0.5, Lm=0.4, Li=3.6 → 주기 = 2.0 → 이동 클립 0.2 배.
        // foot = 0.5 · 2 · 0.2 = 0.2 m/s
        var loco = Loco(2f, th: 2f, idle: 3.6f, move: 0.4f);
        Assert.AreEqual(0.2f, MonsterAnimSpeedPolicy.FootSpeed(1f, loco), 1e-5f);
        // 비중 100% 면 주기 보정이 사라진다.
        Assert.AreEqual(2f, MonsterAnimSpeedPolicy.FootSpeed(2f, loco), 1e-5f);
        // 같은 주기면 보정 1.
        Assert.AreEqual(1f, MonsterAnimSpeedPolicy.FootSpeed(1f, Loco(2f, th: 2f, idle: 1f, move: 1f)), 1e-5f);
    }

    [Test]
    public void 움직이는_동안_블렌드_100퍼센트면_이동_클립만_기준으로_맞춘다()
    {
        // Chomp 형: th 4.5 · 대기 3.8s · 이동 0.4s. 켜면 블렌드 값이 th 로 올라가 이동 클립 100%.
        var loco = Loco(2.5f, th: 4.5f, idle: 3.8f, move: 0.4f);
        loco.fullBlendWhileMoving = true;
        Assert.AreEqual(4.5f, MonsterAnimSpeedPolicy.BlendParam(2.5f, loco), 1e-5f);
        Assert.AreEqual(6f, MonsterAnimSpeedPolicy.BlendParam(6f, loco), 1e-5f);
        Assert.AreEqual(0f, MonsterAnimSpeedPolicy.BlendParam(0f, loco), 1e-5f);    // 서 있으면 대기 그대로
        Assert.AreEqual(1f, MonsterAnimSpeedPolicy.LocomotionSpeed(2.5f, loco), 1e-5f);  // 2.5 / 2.5
        // 끄면 섞인 구간 슬로모션이 그대로 남아 상한에 걸린다.
        loco.fullBlendWhileMoving = false;
        Assert.AreEqual(2.5f, MonsterAnimSpeedPolicy.LocomotionSpeed(2.5f, loco), 1e-5f);
        Assert.AreEqual(2.5f, MonsterAnimSpeedPolicy.BlendParam(2.5f, loco), 1e-5f);
    }

    [Test]
    public void 서_있으면_1()
    {
        Assert.AreEqual(1f, MonsterAnimSpeedPolicy.LocomotionSpeed(0.05f, Loco(2f)), 1e-5f);
    }
}
