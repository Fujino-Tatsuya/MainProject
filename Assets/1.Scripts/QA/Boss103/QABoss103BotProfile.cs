using System;
using System.Globalization;

/// <summary>할 일 103 봇 숙련 단계 — 한 판의 봇 3명은 같은 단계.</summary>
public enum QABoss103BotTier
{
    Beginner = 0,
    Normal = 1,
    Skilled = 2,
}

/// <summary>
/// 할 일 103 봇 프로필(단계별 변수 한 벌). 값은 사용자 확정 표 그대로(<see cref="For"/>) — 리포트에도 <see cref="Describe"/> 로 그대로 찍는다.
/// C2 전투 봇이 조준 오차·스킬 빈도를, C3(<c>QABoss103CombatBot.Evasion</c>)가 패턴 피격 확률·반응 지연·간파 성공률을 쓴다.
/// </summary>
public readonly struct QABoss103BotProfile
{
    public readonly QABoss103BotTier Tier;
    /// <summary>패턴 피격 확률(0~1) — C3 예고 회피: 위험 1건마다 이 확률로 회피하지 않는다.</summary>
    public readonly double PatternHitChance;
    /// <summary>반응 지연(ms) — C3 가 쓴다.</summary>
    public readonly double ReactionMs;
    /// <summary>조준 오차 최대 반경(m) — 대상 지점에 이 반경 원판 안 균일 무작위 오프셋.</summary>
    public readonly float AimErrorRadius;
    /// <summary>스킬 사용 빈도(0~1) — 쿨이 돌면 쓸 확률, 실패면 <see cref="QABoss103SkillGate.RetrySeconds"/> 뒤 다시 굴림.</summary>
    public readonly double SkillUseChance;
    /// <summary>간파 성공률(0~1, 시도 시) — C3 가 쓴다.</summary>
    public readonly double InterruptSuccessChance;

    public QABoss103BotProfile(QABoss103BotTier tier, double patternHit, double reactionMs, float aimError, double skillUse, double interrupt)
    {
        Tier = tier;
        PatternHitChance = patternHit;
        ReactionMs = reactionMs;
        AimErrorRadius = aimError;
        SkillUseChance = skillUse;
        InterruptSuccessChance = interrupt;
    }

    /// <summary>단계 → 프로필(사용자 확정 표).</summary>
    public static QABoss103BotProfile For(QABoss103BotTier tier)
    {
        switch (tier)
        {
            case QABoss103BotTier.Beginner: return new QABoss103BotProfile(tier, 0.45, 450.0, 1.2f, 0.45, 0.25);
            case QABoss103BotTier.Skilled: return new QABoss103BotProfile(tier, 0.15, 150.0, 0.2f, 0.90, 0.80);
            default: return new QABoss103BotProfile(QABoss103BotTier.Normal, 0.30, 300.0, 0.6f, 0.70, 0.50);
        }
    }

    /// <summary>로그 토큰 한 덩어리 — <c>tier=Normal patternHit=0.30 reactMs=300 aimErrM=0.60 skillUse=0.70 interrupt=0.50</c>.</summary>
    public string Describe()
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        return $"tier={Tier} patternHit={PatternHitChance.ToString("F2", c)} reactMs={ReactionMs.ToString("F0", c)} " +
               $"aimErrM={AimErrorRadius.ToString("F2", c)} skillUse={SkillUseChance.ToString("F2", c)} interrupt={InterruptSuccessChance.ToString("F2", c)}";
    }
}

/// <summary>할 일 103 C2 봇 순수 규칙 — 단계 선택·봇 시드·조준 오프셋·거리 유지·끼임 탈출. Unity 의존 없음(EditMode <c>QABoss103BotTests</c>).</summary>
public static class QABoss103Bot
{
    /// <summary>
    /// 판 번호 → 단계(C6 연속 루프가 이 순서로 번갈아 돈다): 판 번호가 1부터라 (판 번호 − 1) mod 3 → 0 보통 · 1 숙련 · 2 초보.
    /// 판 1 = 보통, 판 2 = 숙련, 판 3 = 초보(사용자 확정 순서 보통 → 숙련 → 초보).
    /// </summary>
    public static QABoss103BotTier TierForRun(int run)
    {
        switch ((((run - 1) % 3) + 3) % 3)
        {
            case 1: return QABoss103BotTier.Skilled;
            case 2: return QABoss103BotTier.Beginner;
            default: return QABoss103BotTier.Normal;
        }
    }

    /// <summary>설정의 수동 단계(null = 자동)가 있으면 그것, 없으면 <see cref="TierForRun"/>.</summary>
    public static QABoss103BotTier ResolveTier(int run, QABoss103BotTier? manual) => manual ?? TierForRun(run);

    /// <summary>봇 RNG 용도 — 같은 판·자리라도 용도마다 따로 굴려 한 용도의 호출 수가 다른 용도의 순서를 밀지 않게 한다.</summary>
    public enum Stream
    {
        Aim = 1,
        Skill = 2,
        Move = 3,
        /// <summary>C3 예고 회피 굴림(위험 1건 1회).</summary>
        Dodge = 4,
        /// <summary>C3 간파 성공/실패 굴림(창 1개 1회).</summary>
        Counter = 5,
        /// <summary>할 일 119 C2 툴팁 규칙 E 사용 굴림(<c>QABoss103CombatBot.Rules.cs</c>) — Q·R 굴림 순서를 밀지 않게 따로.</summary>
        Rule = 6,
        /// <summary>할 일 116 대쉬 간파 굴림(창 1개 1회 — 사용 여부·순서·누름 지연) — 간파 성공/실패 굴림 순서를 밀지 않게 따로.</summary>
        DashInterrupt = 7,
    }

    /// <summary>봇 RNG 시드 = 판 시드 × 자리(0 팔라딘·1 어쌔신·2 거너) × 용도. 같은 값 = 같은 굴림 순서.</summary>
    public static int Seed(int runSeed, int slot, Stream stream) => QABoss103Plan.Mix(runSeed, slot, (int)stream, 0xB07);

    /// <summary>
    /// 반경 <paramref name="radius"/> 원판 안 균일 무작위 오프셋(평면 x·z). r = R·√u, θ = 2πv — 면적 균일.
    /// 반경 0 이하면 (0,0). <paramref name="rng"/> 를 정확히 두 번 굴린다(반경과 무관 — 굴림 순서가 단계와 상관없다).
    /// </summary>
    public static void AimOffset(Random rng, float radius, out float x, out float z)
    {
        double u = rng.NextDouble();
        double v = rng.NextDouble();
        if (radius <= 0f)
        {
            x = z = 0f;
            return;
        }
        double r = radius * Math.Sqrt(u);
        double t = 2.0 * Math.PI * v;
        x = (float)(r * Math.Cos(t));
        z = (float)(r * Math.Sin(t));
    }

    // ── 거리 유지 ────────────────────────────────────────────────

    /// <summary>거리 유지 띠(보스 몸 표면까지 수평 거리, m).</summary>
    public readonly struct RangeBand
    {
        /// <summary>이보다 가까우면 물러난다(음수 = 물러나지 않음).</summary>
        public readonly float Min;
        /// <summary>움직이기 시작하면 이 거리까지 간다(히스테리시스 — 띠 경계에서 떨지 않게).</summary>
        public readonly float Settle;
        /// <summary>이보다 멀면 다가간다.</summary>
        public readonly float Max;

        public RangeBand(float min, float settle, float max)
        {
            Min = min;
            Settle = settle;
            Max = max;
        }
    }

    /// <summary>근접(팔라딘·어쌔신) — 표면 2m 안팎: 2m 넘으면 붙고, 1m 까지 들어가 멈춘다.</summary>
    public static readonly RangeBand MeleeBand = new RangeBand(-1f, 1.0f, 2.0f);

    /// <summary>거너 — 6~9m: 9m 넘으면 다가가고 6m 안이면 물러나며, 둘 다 7.5m 에서 멈춘다.</summary>
    public static readonly RangeBand GunnerBand = new RangeBand(6f, 7.5f, 9f);

    public static RangeBand BandFor(QABoss103Character character) =>
        character == QABoss103Character.Gunner ? GunnerBand : MeleeBand;

    public enum Move
    {
        Hold = 0,
        Approach = 1,
        Retreat = 2,
    }

    /// <summary>
    /// 이번 프레임 이동 판단. 띠 밖이면 그쪽으로 움직이기 시작하고, 이미 움직이는 중이면 <see cref="RangeBand.Settle"/> 을 지날 때까지 계속한다.
    /// </summary>
    public static Move DecideMove(float surfaceDistance, RangeBand band, Move current)
    {
        if (surfaceDistance > band.Max)
            return Move.Approach;
        if (band.Min >= 0f && surfaceDistance < band.Min)
            return Move.Retreat;
        if (current == Move.Approach && surfaceDistance > band.Settle)
            return Move.Approach;
        if (current == Move.Retreat && surfaceDistance < band.Settle)
            return Move.Retreat;
        return Move.Hold;
    }

    /// <summary>평타를 내밀 거리인가 — 띠의 바깥 경계(<see cref="RangeBand.Max"/>) 안.</summary>
    public static bool InAttackRange(float surfaceDistance, RangeBand band) => surfaceDistance <= band.Max;
}

/// <summary>
/// 스킬 사용 빈도 게이트(슬롯 하나). 쿨이 돌아 쓸 수 있게 되면 확률을 굴리고, 실패면 <see cref="RetrySeconds"/> 뒤 다시 굴린다(사용자 확정).
/// 성공하면 '장전' 상태로 남아 실제로 시전될 때(<see cref="MarkUsed"/>)까지 매 틱 true — 바쁨·사거리 밖 등으로 거부돼도 다시 굴리지 않는다.
/// 쿨이 다시 돌기 시작하면(준비 아님) 초기화돼 다음 준비 때 바로 굴린다.
/// </summary>
public sealed class QABoss103SkillGate
{
    public const double RetrySeconds = 1.0;

    private double _nextRollAt = double.NegativeInfinity;
    private bool _armed;

    public int Rolls { get; private set; }
    public int Fails { get; private set; }
    public bool Armed => _armed;

    /// <summary>이번 틱에 시전을 시도할 것인가. <paramref name="ready"/> = 쿨 준비됨, <paramref name="chance"/> = 사용 확률(0~1).</summary>
    public bool Tick(bool ready, double now, Random rng, double chance)
    {
        if (!ready)
        {
            _armed = false;
            _nextRollAt = double.NegativeInfinity;
            return false;
        }
        if (_armed)
            return true;
        if (now < _nextRollAt)
            return false;

        Rolls++;
        if (rng.NextDouble() < chance)
        {
            _armed = true;
            return true;
        }
        Fails++;
        _nextRollAt = now + RetrySeconds;
        return false;
    }

    /// <summary>실제로 시전됐다 — 장전을 푼다(다음 굴림은 쿨이 다시 돈 뒤).</summary>
    public void MarkUsed()
    {
        _armed = false;
        _nextRollAt = double.NegativeInfinity;
    }
}

/// <summary>
/// 끼임 감지 — 이동하려는 동안 <see cref="WindowSeconds"/> 이상 <see cref="MinTravel"/> 미만으로만 움직였으면 끼임.
/// 끼이면 <see cref="SidestepSeconds"/> 동안 옆걸음(왼쪽·오른쪽 번갈아) 하고 다시 잰다.
/// </summary>
public sealed class QABoss103StuckDetector
{
    public const double WindowSeconds = 2.0;
    public const float MinTravel = 0.3f;
    public const double SidestepSeconds = 0.8;

    private bool _tracking;
    private double _since;
    private float _x0, _z0;
    private double _sidestepUntil = double.NegativeInfinity;

    public int Escapes { get; private set; }
    /// <summary>지금 옆걸음 쪽(+1 = 진행 방향의 왼쪽, -1 = 오른쪽). 끼일 때마다 뒤집힌다.</summary>
    public int Side { get; private set; } = -1;

    public bool IsSidestepping(double now) => now < _sidestepUntil;

    /// <summary>할 일 119 — 처음으로(옆걸음 중이면 멈춘다). 벽 끼임 재배치 뒤 같은 끼임을 다시 세지 않게.</summary>
    public void Reset()
    {
        _tracking = false;
        _sidestepUntil = double.NegativeInfinity;
    }

    /// <summary>
    /// 매 틱. <paramref name="wantsMove"/> = 이동 입력을 넣으려는 중(옆걸음 중이면 무시). 새로 끼였으면 true(옆걸음 시작).
    /// </summary>
    public bool Tick(double now, float x, float z, bool wantsMove)
    {
        if (IsSidestepping(now))
            return false;
        if (!wantsMove)
        {
            _tracking = false;
            return false;
        }
        if (!_tracking)
        {
            Restart(now, x, z);
            return false;
        }
        float dx = x - _x0, dz = z - _z0;
        if (dx * dx + dz * dz >= MinTravel * MinTravel)
        {
            Restart(now, x, z);
            return false;
        }
        if (now - _since < WindowSeconds)
            return false;

        Escapes++;
        Side = -Side;
        _sidestepUntil = now + SidestepSeconds;
        _tracking = false;
        return true;
    }

    private void Restart(double now, float x, float z)
    {
        _tracking = true;
        _since = now;
        _x0 = x;
        _z0 = z;
    }

    /// <summary>진행 방향(x,z)의 옆(<paramref name="side"/> +1 = 왼쪽) 방향. 크기는 진행 방향과 같다.</summary>
    public static void SideDirection(float dirX, float dirZ, int side, out float x, out float z)
    {
        // 왼쪽 = (−z, x)
        x = -dirZ * side;
        z = dirX * side;
    }
}
