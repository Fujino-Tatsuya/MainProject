using UnityEngine;

/// <summary><see cref="MonsterOverheadHealthBarVisibility.DecideInstall"/> 결과.</summary>
public enum MonsterHealthBarInstall
{
    Skip,
    ResetExisting,
    AttachWithRule,
    AttachBarOnly,
}

/// <summary>
/// 몬스터 발밑 체력바의 표시 판단(순수 로직 — MonoBehaviour·Unity 시간 없음, 시간은 인자로 받는다).
/// 부착 판단(<see cref="DecideInstall"/>)·등급별 선택(<see cref="ForRank{T}"/>)도 여기 둔다.
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

    /// <summary>등급별 값 — 중간 보스면 midBoss, 나머지(일반)는 normal. 프리팹·발밑 오프셋 선택에 쓴다.</summary>
    public static T ForRank<T>(MonsterRank rank, T normal, T midBoss)
        => rank == MonsterRank.MidBoss ? midBoss : normal;

    /// <summary>발밑 오프셋(아래로 내릴 픽셀) → UnitOverheadHealthBar 화면 오프셋(위 = +y). 음수 입력은 0.</summary>
    public static Vector2 FootScreenOffset(float downPixels)
        => new Vector2(0f, -Mathf.Max(0f, downPixels));

    /// <summary>
    /// 스폰 때 할 일. 순서가 규칙이다:
    /// 1) 이 규칙이 이미 붙어 있음 → 표시 상태만 리셋(풀 재사용).
    /// 2) 자식에 바가 이미 있음 → 건너뜀(프리팹 자체 바, 또는 자체 규칙 몬스터에 전에 붙인 바 — 바 2개 방지).
    /// 3) 최종 보스(Rank=Boss 또는 BossHudTarget — 보스 HUD 가 따로 있다) → 건너뜀.
    /// 4) 몬스터에 다른 IOverheadHealthBarRule 이 있음(튜토리얼 스피너봇) → 바만 붙이고 표시·색·라벨은 그 규칙을 따른다.
    /// 5) 그 외 → 이 규칙 + 바를 붙인다.
    /// </summary>
    public static MonsterHealthBarInstall DecideInstall(MonsterRank rank, bool hasBossHudTarget,
        bool hasThisRule, bool hasExistingBar, bool hasOtherRule)
    {
        if (hasThisRule) return MonsterHealthBarInstall.ResetExisting;
        if (hasExistingBar) return MonsterHealthBarInstall.Skip;
        if (rank == MonsterRank.Boss || hasBossHudTarget) return MonsterHealthBarInstall.Skip;
        return hasOtherRule ? MonsterHealthBarInstall.AttachBarOnly : MonsterHealthBarInstall.AttachWithRule;
    }

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
