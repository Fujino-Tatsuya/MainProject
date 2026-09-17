using System;

/// <summary>
/// 허수아비 체력 회복 타이머. MonoBehaviour·NetworkBehaviour 에 의존하지 않는 순수 계산기다
/// (나중에 EditMode 테스트를 싸게 붙이려는 의도).
///
/// 규칙 — 마지막 피격 후 delay 초가 지나면 duration 초에 걸쳐 최대 체력까지 회복한다.
/// 회복 도중 맞으면 즉시 중단하고 대기 타이머를 처음부터 다시 센다.
/// </summary>
public sealed class TrainingDummyRegen
{
    readonly float _delaySeconds;
    readonly float _durationSeconds;

    float _sinceLastHit;
    // 정수 회복량으로 잘라 쓰고 남은 소수. 버리면 회복이 영영 안 끝난다.
    float _carry;

    public TrainingDummyRegen(float delaySeconds, float durationSeconds)
    {
        _delaySeconds = Math.Max(0f, delaySeconds);
        _durationSeconds = Math.Max(0.01f, durationSeconds);
        // 스폰 직후는 만피라 대기 상태로 둘 이유가 없다.
        _sinceLastHit = _delaySeconds;
    }

    /// <summary>대기 시간을 넘겨 실제로 체력을 되돌리는 중인지.</summary>
    public bool IsRegenerating { get; private set; }

    /// <summary>피격 알림. 대기 타이머를 리셋하고 진행 중인 회복을 중단한다.</summary>
    public void NotifyDamaged()
    {
        _sinceLastHit = 0f;
        _carry = 0f;
        IsRegenerating = false;
    }

    /// <summary>
    /// 이번 프레임에 회복할 체력을 정수로 돌려준다. 회복할 게 없으면 0.
    /// </summary>
    /// <param name="deltaTime">경과 시간(초)</param>
    /// <param name="currentHp">현재 체력</param>
    /// <param name="maxHp">최대 체력</param>
    public int Tick(float deltaTime, int currentHp, int maxHp)
    {
        if (deltaTime <= 0f || maxHp <= 0)
            return 0;

        _sinceLastHit += deltaTime;

        if (currentHp >= maxHp)
        {
            IsRegenerating = false;
            _carry = 0f;
            return 0;
        }

        if (_sinceLastHit < _delaySeconds)
            return 0;

        IsRegenerating = true;

        _carry += maxHp / _durationSeconds * deltaTime;

        int healed = (int)_carry;
        if (healed <= 0)
            return 0;

        _carry -= healed;
        return healed;
    }
}
