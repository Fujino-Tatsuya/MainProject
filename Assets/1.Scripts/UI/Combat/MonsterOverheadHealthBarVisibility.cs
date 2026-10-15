/// <summary>
/// 몬스터 머리 위 체력바의 표시 판단(순수 로직 — MonoBehaviour·Unity 시간 없음, 시간은 인자로 받는다).
/// 규칙: 처음엔 숨김 → 체력이 줄면 켜짐 → 전투 상태가 끝난 뒤 hideDelay 초가 지나면 숨김.
/// 전투 중이거나 다시 맞으면 숨김 타이머가 처음부터 다시 돈다. 사망(Dead 상태 또는 체력 0 이하)이면 즉시 숨김.
/// 상태값은 MonsterBase 의 복제된 _state 를 각 피어가 읽어 넘긴다(표시만 로컬 판단, 서버 로직 없음).
/// 상세: Docs/tech/handoff-monster-healthbar.md
/// </summary>
public sealed class MonsterOverheadHealthBarVisibility
{
    bool _shown;
    bool _hasHealth;
    int _lastHealth;
    float _lastActiveAt;

    public bool IsShown => _shown;

    /// <summary>
    /// 전투 중으로 보는 상태 — 타깃을 물고 있는 동안 FSM 이 거치는 값.
    /// Idle(대기)·Return(리쉬 복귀)·Dead 는 전투가 아니다.
    /// </summary>
    public static bool IsCombatState(MonsterState state)
    {
        switch (state)
        {
            case MonsterState.Chase:
            case MonsterState.Attack:
            case MonsterState.Hit:
            case MonsterState.Groggy:
            case MonsterState.Knockback:
                return true;
            default:
                return false;
        }
    }

    /// <summary>바 높이 배율 — 중간 보스만 midBossHeightScale, 나머지는 1.</summary>
    public static float HeightScale(MonsterRank rank, float midBossHeightScale)
        => rank == MonsterRank.MidBoss ? midBossHeightScale : 1f;

    /// <summary>
    /// 이 몬스터에 체력바를 붙일지. 최종 보스(Rank=Boss 또는 BossHudTarget 부착 — 보스 HUD 가 따로 있다)와
    /// 이미 머리 위 바가 있는 프리팹(튜토리얼 스피너봇 등)은 제외한다.
    /// </summary>
    public static bool ShouldAttach(MonsterRank rank, bool hasBossHudTarget, bool hasExistingBar)
        => rank != MonsterRank.Boss && !hasBossHudTarget && !hasExistingBar;

    /// <summary>처음 상태(숨김)로 되돌린다. 풀 재사용·재스폰 때 쓴다.</summary>
    public void Reset()
    {
        _shown = false;
        _hasHealth = false;
        _lastHealth = 0;
        _lastActiveAt = 0f;
    }

    /// <summary>매 프레임 호출. 반환값 = 지금 표시할지.</summary>
    public bool Tick(float now, int health, MonsterState state, float hideDelay)
    {
        bool damaged = _hasHealth && health < _lastHealth;
        _lastHealth = health;
        _hasHealth = true;

        if (state == MonsterState.Dead || health <= 0)
        {
            _shown = false;
            return false;
        }

        if (damaged)
        {
            _shown = true;
            _lastActiveAt = now;
        }

        if (!_shown)
            return false;

        if (IsCombatState(state))
            _lastActiveAt = now;
        else if (now - _lastActiveAt >= hideDelay)
            _shown = false;

        return _shown;
    }
}
