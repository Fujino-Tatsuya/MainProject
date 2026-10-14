using System;

/// <summary>
/// 플레이어 기본 체력 재생(할 일 31)의 순수 계산(Unity·NGO 비의존 — EditMode 테스트 대상).
/// 원본: Docs/tech/party-lives-respawn.md §8. 배선은 <c>Player</c>(서버 Update·피격 훅)가 한다.
///
///  · 마지막 피격 후 delay 초가 지나면 초당 rateBaseHp(FinalMaxHp) × percentPerSecond 를 회복한다. 상한(maxHp)에서 멈춘다.
///  · 피격(<see cref="NotifyDamage"/>) = 체력 피해 &gt; 0 또는 추락 피해. 지연을 처음부터 다시 센다. 쌓아 둔 소수 회복량도 버린다.
///    보호막만 깎인 피격은 재생을 멈추지 않는다(사용자 결정 10-13).
///  · 차단(사망·Soul·PermanentDead·연출 잠금·체력 0) 중에는 회복하지 않고, 차단이 풀린 시점부터 지연을 다시 센다
///    — 부활·리스폰 직후가 곧 "지연부터 시작" 이다.
///  · 체력이 정수라 틱마다 소수 회복량을 누적기에 모았다가 정수 부분만 내준다(작은 비율·짧은 틱에서도 손실 없음).
///  · 시간은 호출자가 넘긴다(게임 시간 — 일시정지·슬로모션과 같이 멈추고 느려진다).
/// </summary>
public sealed class PlayerHealthRegenModel
{
    public const float DefaultDelaySeconds = 5f;
    public const float DefaultPercentPerSecond = 0.02f;
    const double FractionEpsilon = 1e-6;

    double _lastHitTime;
    double _lastTickTime;
    bool _started;
    double _fraction;

    /// <summary>지금까지 모은 소수 회복량(0 이상 1 미만). 테스트·진단용.</summary>
    public double PendingFraction => _fraction;

    /// <summary>
    /// 서버에 실제로 적용된 피해를 알린다(사용자 결정 10-13). 체력이 깎였거나 추락 피해면 <see cref="NotifyHit"/>,
    /// 보호막만 깎인 피해는 무시한다(재생 지속). 리셋했으면 true.
    /// </summary>
    public bool NotifyDamage(double now, int healthLost, bool isFallDamage)
    {
        if (healthLost <= 0 && !isFallDamage)
            return false;

        NotifyHit(now);
        return true;
    }

    /// <summary>피격 — 회복을 즉시 멈추고 지연을 now 부터 다시 센다.</summary>
    public void NotifyHit(double now)
    {
        _lastHitTime = now;
        _lastTickTime = now;
        _started = true;
        _fraction = 0.0;
    }

    /// <summary>
    /// 한 틱을 진행하고 이번 틱에 회복할 정수 체력을 돌려준다(0 이상, maxHp - currentHp 이하).
    /// 첫 호출은 기준 시각만 잡는다(스폰 직후도 지연부터).
    /// </summary>
    /// <param name="blocked">사망·Soul·PermanentDead·연출 잠금 등 재생 금지 상태</param>
    /// <param name="maxHp">회복 상한(여기서 멈춘다)</param>
    /// <param name="rateBaseHp">초당 회복량의 기준 체력(FinalMaxHp)</param>
    public int Tick(double now, bool blocked, int currentHp, int maxHp, int rateBaseHp,
        float delaySeconds, float percentPerSecond)
    {
        if (!_started || blocked || currentHp <= 0)
        {
            NotifyHit(now);
            return 0;
        }

        double previous = _lastTickTime;
        _lastTickTime = now;

        int missing = maxHp - currentHp;
        if (missing <= 0 || rateBaseHp <= 0 || percentPerSecond <= 0f)
        {
            // 가득 찬 동안 회복량을 모아 두지 않는다 — 다음 피격 뒤 한꺼번에 튀어나오지 않게.
            _fraction = 0.0;
            return 0;
        }

        double regenStart = _lastHitTime + Math.Max(0f, delaySeconds);
        double from = Math.Max(previous, regenStart);
        if (now <= from)
            return 0;

        _fraction += rateBaseHp * (double)percentPerSecond * (now - from);
        // float 비율(0.02f = 0.0199999995…) 오차로 2.0 이 1.99999… 가 돼 1HP 씩 늦게 나오지 않게 아주 작은 여유를 둔다.
        int whole = (int)Math.Floor(_fraction + FractionEpsilon);
        if (whole <= 0)
            return 0;

        if (whole >= missing)
        {
            _fraction = 0.0;
            return missing;
        }

        _fraction = Math.Max(0.0, _fraction - whole);
        return whole;
    }
}
