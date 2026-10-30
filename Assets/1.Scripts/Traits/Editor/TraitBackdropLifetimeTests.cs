using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 특성 창 배경 블러 텍스처 수명 고정(할 일 165 후속 A안) — 열기→캡처 결과 보유, 닫기→반납,
/// 연속 열기/닫기·캡처 대기 중 닫기·늦게 온 캡처에서 누수(반납 안 된 결과) 0.
/// </summary>
public sealed class TraitBackdropLifetimeTests
{
    private sealed class FakeTexture { }

    // 만든 수·반납한 수를 세는 가짜 텍스처 풀 — 살아 있는 수 = 누수 후보.
    private sealed class Pool
    {
        public readonly HashSet<FakeTexture> Alive = new HashSet<FakeTexture>();
        public int Released;

        public FakeTexture Create()
        {
            var texture = new FakeTexture();
            Alive.Add(texture);
            return texture;
        }

        public void Release(FakeTexture texture)
        {
            Assert.That(Alive.Remove(texture), Is.True, "같은 텍스처를 두 번 반납했거나 모르는 텍스처");
            Released++;
        }
    }

    [Test]
    public void OpenThenCapture_HoldsResult_CloseReleases()
    {
        var pool = new Pool();
        var life = new TraitBackdropLifetime<FakeTexture>(pool.Release);

        int ticket = life.Open();
        Assert.That(ticket, Is.Not.Zero);
        Assert.That(life.IsPending, Is.True, "캡처 전에는 창을 보이지 않는다");

        FakeTexture result = pool.Create();
        Assert.That(life.Complete(ticket, result), Is.True);
        Assert.That(life.Current, Is.SameAs(result));
        Assert.That(life.IsPending, Is.False);

        life.Close();
        Assert.That(life.Current, Is.Null);
        Assert.That(pool.Alive, Is.Empty);
    }

    [Test]
    public void CloseWhilePending_LateCaptureIsReleasedImmediately()
    {
        var pool = new Pool();
        var life = new TraitBackdropLifetime<FakeTexture>(pool.Release);

        int ticket = life.Open();
        life.Close(); // 캡처 전에 닫힘(자동 선택·뷰 비활성)
        Assert.That(life.IsWaitingFor(ticket), Is.False, "닫힌 뒤에는 캡처하지 않는다");

        Assert.That(life.Complete(ticket, pool.Create()), Is.False);
        Assert.That(life.Current, Is.Null);
        Assert.That(pool.Alive, Is.Empty);
    }

    [Test]
    public void ReopenBeforeOldCapture_OnlyNewTicketIsAccepted()
    {
        var pool = new Pool();
        var life = new TraitBackdropLifetime<FakeTexture>(pool.Release);

        int first = life.Open();
        life.Close();
        int second = life.Open();
        Assert.That(second, Is.Not.EqualTo(first));

        Assert.That(life.Complete(first, pool.Create()), Is.False, "지난 열림의 캡처는 버린다");
        FakeTexture result = pool.Create();
        Assert.That(life.Complete(second, result), Is.True);
        Assert.That(life.Current, Is.SameAs(result));
        Assert.That(pool.Alive.Count, Is.EqualTo(1));

        life.Close();
        Assert.That(pool.Alive, Is.Empty);
    }

    [Test]
    public void OpenWhileOpen_KeepsCurrentAndAsksNoNewCapture()
    {
        var pool = new Pool();
        var life = new TraitBackdropLifetime<FakeTexture>(pool.Release);

        int ticket = life.Open();
        FakeTexture result = pool.Create();
        life.Complete(ticket, result);

        Assert.That(life.Open(), Is.Zero, "열린 채 새 제시 — 창이 찍히지 않게 다시 캡처하지 않는다");
        Assert.That(life.Current, Is.SameAs(result));
        Assert.That(life.Complete(ticket, pool.Create()), Is.False, "중복 결과는 반납");
        Assert.That(pool.Alive.Count, Is.EqualTo(1));

        life.Close();
        Assert.That(pool.Alive, Is.Empty);
    }

    [Test]
    public void ManyOpenCloseCycles_NoLeakAndCloseIsIdempotent()
    {
        var pool = new Pool();
        var life = new TraitBackdropLifetime<FakeTexture>(pool.Release);

        for (int i = 0; i < 50; i++)
        {
            int ticket = life.Open();
            if (i % 3 != 0)
                life.Complete(ticket, pool.Create()); // 3번에 1번은 캡처 전에 닫힘
            life.Close();
            life.Close(); // OnDisable·Bind(null) 등 겹친 닫기
        }

        Assert.That(pool.Alive, Is.Empty);
        Assert.That(life.IsOpen, Is.False);
    }

    [Test]
    public void FailedCapture_EndsWaitWithoutResult()
    {
        var pool = new Pool();
        var life = new TraitBackdropLifetime<FakeTexture>(pool.Release);

        int ticket = life.Open();
        Assert.That(life.Complete(ticket, null), Is.True, "캡처 실패여도 창은 블러 없이 보인다");
        Assert.That(life.IsPending, Is.False);
        Assert.That(life.Current, Is.Null);

        life.Close();
        Assert.That(pool.Released, Is.Zero);
    }
}
