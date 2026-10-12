using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 결과 화면 플레이어별 통계 집계 규칙(PLAN-result-stats). 필터는 호출부가 아니라 집계기 입력 규칙이다.
/// </summary>
public sealed class SessionStatsAggregatorTests
{
    const ulong Host = 0;
    const ulong Client1 = 1;
    const ulong Client2 = 2;

    static PlayerSessionStats Get(SessionStatsAggregator aggregator, ulong clientId)
    {
        Assert.IsTrue(aggregator.TryGet(clientId, out PlayerSessionStats stats), $"{clientId} 행이 있어야 한다");
        return stats;
    }

    // ─── 데미지 필터 ───────────────────────────────────────────────────

    [Test]
    public void 몬스터_대상_피해는_공격자에게_누적된다()
    {
        var a = new SessionStatsAggregator();
        Assert.IsTrue(a.RecordDamage(Client1, true, 30, false));
        Assert.IsTrue(a.RecordDamage(Client1, true, 12, false));
        Assert.AreEqual(42, Get(a, Client1).Damage);
        Assert.AreEqual(0, Get(a, Client1).Kills);
    }

    [Test]
    public void 몬스터가_아닌_대상_피해는_세지_않는다()
    {
        var a = new SessionStatsAggregator();
        Assert.IsFalse(a.RecordDamage(Client1, false, 50, true), "더미·송전탑·플레이어 등");
        Assert.AreEqual(0, a.PlayerCount, "걸러진 피해로는 행도 만들지 않는다");
    }

    [Test]
    public void 공격자_없는_피해는_세지_않는다()
    {
        var a = new SessionStatsAggregator();
        Assert.IsFalse(a.RecordDamage(SessionStatsAggregator.NoAttacker, true, 50, true));
        Assert.AreEqual(0, a.PlayerCount);
    }

    [Test]
    public void 영_이하_피해는_무시한다()
    {
        var a = new SessionStatsAggregator();
        Assert.IsFalse(a.RecordDamage(Client1, true, 0, true));
        Assert.IsFalse(a.RecordDamage(Client1, true, -5, true));
        Assert.AreEqual(0, a.PlayerCount);
    }

    [Test]
    public void 데미지는_포화한다()
    {
        var a = new SessionStatsAggregator();
        a.RecordDamage(Client1, true, int.MaxValue, false);
        a.RecordDamage(Client1, true, 10, false);
        Assert.AreEqual(int.MaxValue, Get(a, Client1).Damage);
    }

    // ─── 처치(막타) ───────────────────────────────────────────────────

    [Test]
    public void 처치는_막타_공격자_개인에게만_간다()
    {
        var a = new SessionStatsAggregator();
        a.RecordDamage(Host, true, 90, false);
        a.RecordDamage(Client1, true, 10, true);
        Assert.AreEqual(0, Get(a, Host).Kills);
        Assert.AreEqual(1, Get(a, Client1).Kills);
    }

    [Test]
    public void 공격자_없는_막타는_누구의_처치도_아니다()
    {
        var a = new SessionStatsAggregator();
        a.RecordDamage(Host, true, 90, false);
        a.RecordDamage(SessionStatsAggregator.NoAttacker, true, 10, true);
        Assert.AreEqual(0, Get(a, Host).Kills);
    }

    // ─── 간파 ─────────────────────────────────────────────────────────

    [Test]
    public void 간파_성공은_공격자에게_1회씩()
    {
        var a = new SessionStatsAggregator();
        Assert.IsTrue(a.RecordCounterSuccess(Client2));
        Assert.IsTrue(a.RecordCounterSuccess(Client2));
        Assert.IsFalse(a.RecordCounterSuccess(SessionStatsAggregator.NoAttacker));
        Assert.AreEqual(2, Get(a, Client2).Counters);
    }

    // ─── 사망 ─────────────────────────────────────────────────────────

    [Test]
    public void 사망은_Alive에서_DeadPresentation_전이만_센다()
    {
        var a = new SessionStatsAggregator();
        Assert.IsTrue(a.RecordLifeStateChange(Client1, PlayerLifeState.Alive, PlayerLifeState.DeadPresentation));
        Assert.IsFalse(a.RecordLifeStateChange(Client1, PlayerLifeState.DeadPresentation, PlayerLifeState.Soul));
        Assert.IsFalse(a.RecordLifeStateChange(Client1, PlayerLifeState.Soul, PlayerLifeState.Alive));
        Assert.IsTrue(a.RecordLifeStateChange(Client1, PlayerLifeState.Alive, PlayerLifeState.DeadPresentation));
        Assert.IsFalse(a.RecordLifeStateChange(Client1, PlayerLifeState.DeadPresentation, PlayerLifeState.PermanentDead));
        Assert.AreEqual(2, Get(a, Client1).Deaths);
    }

    [Test]
    public void 같은_상태_통지는_사망으로_세지_않는다()
    {
        var a = new SessionStatsAggregator();
        // 스폰 초기 통지 (s,s) — DeadPresentation 상태로 스폰돼도(늦은 접속 등) 사망이 아니다.
        Assert.IsFalse(a.RecordLifeStateChange(Client1, PlayerLifeState.Alive, PlayerLifeState.Alive));
        Assert.IsFalse(a.RecordLifeStateChange(Client1, PlayerLifeState.DeadPresentation, PlayerLifeState.DeadPresentation));
        Assert.AreEqual(0, Get(a, Client1).Deaths);
    }

    [Test]
    public void 같은_사망의_중복_통지는_한_번만_센다()
    {
        var a = new SessionStatsAggregator();
        a.RecordLifeStateChange(Client1, PlayerLifeState.Alive, PlayerLifeState.DeadPresentation);
        a.RecordLifeStateChange(Client1, PlayerLifeState.DeadPresentation, PlayerLifeState.DeadPresentation);
        Assert.AreEqual(1, Get(a, Client1).Deaths);
    }

    [Test]
    public void 스폰_통지만으로_행이_등록된다()
    {
        var a = new SessionStatsAggregator();
        a.RecordLifeStateChange(Client2, PlayerLifeState.Alive, PlayerLifeState.Alive);
        PlayerSessionStats row = Get(a, Client2);
        Assert.AreEqual(0, row.Damage);
        Assert.AreEqual(PlayerSessionStats.UnknownCharacterId, row.CharacterId);
    }

    // ─── 캐릭터·스냅샷 ────────────────────────────────────────────────

    [Test]
    public void 캐릭터_id_는_모름으로_덮이지_않는다()
    {
        var a = new SessionStatsAggregator();
        a.SetCharacterId(Client1, 2);
        a.SetCharacterId(Client1, PlayerSessionStats.UnknownCharacterId);
        Assert.AreEqual(2, Get(a, Client1).CharacterId);
    }

    [Test]
    public void 스냅샷은_clientId_오름차순이다()
    {
        var a = new SessionStatsAggregator();
        a.RecordCounterSuccess(Client2);
        a.RegisterPlayer(Host);
        a.RecordDamage(Client1, true, 5, false);

        var rows = new List<PlayerSessionStats> { new PlayerSessionStats { ClientId = 99 } };
        a.Snapshot(rows);

        Assert.AreEqual(3, rows.Count, "기존 내용은 비우고 채운다");
        Assert.AreEqual(Host, rows[0].ClientId);
        Assert.AreEqual(Client1, rows[1].ClientId);
        Assert.AreEqual(Client2, rows[2].ClientId);
    }
}
