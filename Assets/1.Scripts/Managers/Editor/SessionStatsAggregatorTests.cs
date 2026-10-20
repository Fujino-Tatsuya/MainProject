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

    // ─── 회복량 ───────────────────────────────────────────────────────

    [Test]
    public void 플레이어_대상_회복은_회복_주체에게_누적된다()
    {
        var a = new SessionStatsAggregator();
        Assert.IsTrue(a.RecordHealing(Client1, true, 40));
        Assert.IsTrue(a.RecordHealing(Client1, true, 15));
        Assert.AreEqual(55, Get(a, Client1).Healing);
        Assert.AreEqual(0, Get(a, Client1).Damage, "회복은 가한 피해에 섞이지 않는다");
    }

    [Test]
    public void 다른_플레이어를_회복시키면_주체에게_간다()
    {
        var a = new SessionStatsAggregator();
        a.RegisterPlayer(Client2);
        Assert.IsTrue(a.RecordHealing(Client1, true, 30)); // Client1 이 Client2 를 회복
        Assert.AreEqual(30, Get(a, Client1).Healing);
        Assert.AreEqual(0, Get(a, Client2).Healing);
    }

    [Test]
    public void 플레이어가_아닌_대상_회복은_세지_않는다()
    {
        var a = new SessionStatsAggregator();
        Assert.IsFalse(a.RecordHealing(Host, false, 500), "더미 자기 회복 등 — 호스트에게 붙지 않는다");
        Assert.AreEqual(0, a.PlayerCount);
    }

    [Test]
    public void 주체_없는_회복과_영_이하_회복은_무시한다()
    {
        var a = new SessionStatsAggregator();
        Assert.IsFalse(a.RecordHealing(SessionStatsAggregator.NoAttacker, true, 10));
        Assert.IsFalse(a.RecordHealing(Client1, true, 0));
        Assert.IsFalse(a.RecordHealing(Client1, true, -3));
        Assert.AreEqual(0, a.PlayerCount);
    }

    [Test]
    public void 회복량은_포화한다()
    {
        var a = new SessionStatsAggregator();
        a.RecordHealing(Client1, true, int.MaxValue);
        a.RecordHealing(Client1, true, 10);
        Assert.AreEqual(int.MaxValue, Get(a, Client1).Healing);
    }

    // ─── 입은 피해 ────────────────────────────────────────────────────

    [Test]
    public void 플레이어가_입은_피해는_그_플레이어에게_누적된다()
    {
        var a = new SessionStatsAggregator();
        Assert.IsTrue(a.RecordDamageTaken(Client2, true, 25));
        Assert.IsTrue(a.RecordDamageTaken(Client2, true, 5));
        Assert.AreEqual(30, Get(a, Client2).DamageTaken);
        Assert.AreEqual(0, Get(a, Client2).Damage, "입은 피해는 가한 피해와 따로다");
    }

    [Test]
    public void 플레이어가_아닌_대상의_감소는_입은_피해가_아니다()
    {
        var a = new SessionStatsAggregator();
        Assert.IsFalse(a.RecordDamageTaken(Host, false, 50));
        Assert.IsFalse(a.RecordDamageTaken(SessionStatsAggregator.NoAttacker, true, 50));
        Assert.IsFalse(a.RecordDamageTaken(Client1, true, 0));
        Assert.AreEqual(0, a.PlayerCount);
    }

    [Test]
    public void 입은_피해는_포화한다()
    {
        var a = new SessionStatsAggregator();
        a.RecordDamageTaken(Client1, true, int.MaxValue);
        a.RecordDamageTaken(Client1, true, 1);
        Assert.AreEqual(int.MaxValue, Get(a, Client1).DamageTaken);
    }

    [Test]
    public void 입은_피해와_회복은_가한_피해_처치를_바꾸지_않는다()
    {
        var a = new SessionStatsAggregator();
        a.RecordDamage(Client1, true, 90, true);
        a.RecordDamageTaken(Client1, true, 40);
        a.RecordHealing(Client1, true, 20);
        PlayerSessionStats row = Get(a, Client1);
        Assert.AreEqual(90, row.Damage);
        Assert.AreEqual(1, row.Kills);
        Assert.AreEqual(40, row.DamageTaken);
        Assert.AreEqual(20, row.Healing);
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

    // ─── 판 도중 재합류 행 합치기(할 일 48 C14 R5c) ─────────────────────

    const ulong Rejoined1 = 5;
    const ulong Rejoined2 = 8;

    [Test]
    public void 재합류하면_옛_행을_새_clientId_로_합쳐_같은_사람_1행()
    {
        var a = new SessionStatsAggregator();
        a.SetCharacterId(Client1, 2);
        a.RecordDamage(Client1, true, 100, true);
        a.RecordLifeStateChange(Client1, PlayerLifeState.Alive, PlayerLifeState.DeadPresentation);
        a.RecordHealing(Client1, true, 7);
        a.RecordDamageTaken(Client1, true, 30);
        a.RecordCounterSuccess(Client1);

        // 재스폰 직후 — 스폰 초기 통지가 새 행을 이미 만들었고 새 행에도 값이 쌓였다
        a.RecordLifeStateChange(Rejoined1, PlayerLifeState.Alive, PlayerLifeState.Alive);
        a.RecordDamage(Rejoined1, true, 20, false);

        Assert.IsTrue(a.Rebind(Client1, Rejoined1));
        Assert.AreEqual(1, a.PlayerCount, "같은 사람은 1행");
        Assert.IsFalse(a.TryGet(Client1, out _), "옛 행은 없어진다");

        var row = Get(a, Rejoined1);
        Assert.AreEqual(Rejoined1, row.ClientId);
        Assert.AreEqual(120, row.Damage);
        Assert.AreEqual(1, row.Kills);
        Assert.AreEqual(1, row.Deaths);
        Assert.AreEqual(7, row.Healing);
        Assert.AreEqual(30, row.DamageTaken);
        Assert.AreEqual(1, row.Counters);
        Assert.AreEqual(2, row.CharacterId, "새 행 캐릭터를 모르면 옛 값");
    }

    [Test]
    public void 새_행_캐릭터를_알면_새_값을_쓴다()
    {
        var a = new SessionStatsAggregator();
        a.SetCharacterId(Client1, 1);
        a.SetCharacterId(Rejoined1, 3);

        Assert.IsTrue(a.Rebind(Client1, Rejoined1));
        Assert.AreEqual(3, Get(a, Rejoined1).CharacterId);
    }

    [Test]
    public void 새_행이_아직_없으면_옛_행을_그대로_옮긴다()
    {
        var a = new SessionStatsAggregator();
        a.RecordDamage(Client1, true, 50, false);

        Assert.IsTrue(a.Rebind(Client1, Rejoined1));
        Assert.AreEqual(1, a.PlayerCount);
        Assert.AreEqual(50, Get(a, Rejoined1).Damage);
    }

    [Test]
    public void 같은_쌍을_두_번_불러도_행과_값이_늘지_않는다()
    {
        var a = new SessionStatsAggregator();
        a.RecordDamage(Client1, true, 50, false);
        a.RegisterPlayer(Rejoined1);

        Assert.IsTrue(a.Rebind(Client1, Rejoined1));
        Assert.IsFalse(a.Rebind(Client1, Rejoined1), "옛 행이 이미 없다");
        Assert.AreEqual(1, a.PlayerCount);
        Assert.AreEqual(50, Get(a, Rejoined1).Damage);
    }

    [Test]
    public void 옛_행이_없거나_같은_id_면_아무것도_안_한다()
    {
        var a = new SessionStatsAggregator();
        a.RegisterPlayer(Rejoined1);

        Assert.IsFalse(a.Rebind(Client1, Rejoined1));
        Assert.IsFalse(a.Rebind(Rejoined1, Rejoined1));
        Assert.IsFalse(a.Rebind(Rejoined1, SessionStatsAggregator.NoAttacker));
        Assert.AreEqual(1, a.PlayerCount);
    }

    [Test]
    public void 합친_행은_옛_자리_순서를_지킨다_두_번_재합류해도()
    {
        var a = new SessionStatsAggregator();
        a.RegisterPlayer(Host);
        a.RegisterPlayer(Client1);
        a.RegisterPlayer(Client2);

        a.Rebind(Client1, Rejoined1);
        a.Rebind(Rejoined1, Rejoined2);

        var rows = new List<PlayerSessionStats>();
        a.Snapshot(rows);
        Assert.AreEqual(3, rows.Count);
        Assert.AreEqual(Host, rows[0].ClientId);
        Assert.AreEqual(Rejoined2, rows[1].ClientId, "P2 자리 그대로");
        Assert.AreEqual(Client2, rows[2].ClientId);

        a.Reset();
        a.RegisterPlayer(Rejoined2);
        a.RegisterPlayer(Client2);
        a.Snapshot(rows);
        Assert.AreEqual(Client2, rows[0].ClientId, "Reset 뒤에는 정렬 키도 비워 clientId 순");
    }
}
