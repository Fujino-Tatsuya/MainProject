using System;

// 판 도중 재합류(할 일 48 C14 R5c)의 생명·체력 보관·복원 순수 규칙(Unity·NGO 비의존 — EditMode 테스트 대상).
// 배선: PlayerLifeCycleController(디스폰 때 기록·복원 적용) · RejoinMemory(playerId 키 보관) · NetworkLoadingFlowController(퇴장 확정·복원 호출).
// 원본: Docs/tech/networking.md "판 도중 재합류" · 설계 메모 C14 §3.5.

/// <summary>퇴장 순간 한 플레이어의 생명 기록 — 확정된 상태 + 체력 비율(Alive 일 때만 의미, 그 밖에는 0).</summary>
public readonly struct PlayerRejoinLifeRecord : IEquatable<PlayerRejoinLifeRecord>
{
    public readonly PlayerLifeState State;
    public readonly float HpRatio;

    public PlayerRejoinLifeRecord(PlayerLifeState state, float hpRatio)
    {
        State = state;
        HpRatio = state == PlayerLifeState.Alive ? PlayerRejoinLifeRules.ClampRatio(hpRatio) : 0f;
    }

    /// <summary>전멸 리스폰 덮어쓰기 값 — 그 자리에 있었으면 살아났을 것(체력 가득).</summary>
    public static PlayerRejoinLifeRecord AliveFull => new PlayerRejoinLifeRecord(PlayerLifeState.Alive, 1f);

    public bool Equals(PlayerRejoinLifeRecord other) => State == other.State && HpRatio.Equals(other.HpRatio);
    public override bool Equals(object obj) => obj is PlayerRejoinLifeRecord other && Equals(other);
    public override int GetHashCode() => ((int)State * 397) ^ HpRatio.GetHashCode();
    public override string ToString() => State == PlayerLifeState.Alive ? $"{State}({HpRatio:0.###})" : State.ToString();
}

/// <summary>재합류 때 새 PlayerObject 에 적용할 일.</summary>
public enum PlayerRejoinLifeRestore : byte
{
    /// <summary>Alive 그대로 — 체력만 비율로 맞춘다(Q-B, 스킬 쿨다운은 손대지 않는다 = 0부터).</summary>
    HealthRatio,
    /// <summary>Soul 로 바로 둔다(Q-C, 목숨 소비 없음). 부활은 기존 규칙 그대로.</summary>
    Soul,
    /// <summary>PermanentDead 로 바로 둔다 — 관전이 자동으로 켜진다.</summary>
    PermanentDead,
}

/// <summary>
/// 재합류 생명 규칙.
///  · 기록(<see cref="Capture"/>): DeadPresentation 중 퇴장이면 그 순간 목적지를 확정한다(<see cref="PartyLivesRules.ResolveDeathDestination"/> —
///    차감 없는 부활이 걸렸거나 공용 목숨 &gt; 0 → Soul, 아니면 PermanentDead). 체력 0 인데 아직 Alive(사망 신호 보류 — 연출 잠금 중 사망)도 같은 취급.
///  · 복원(<see cref="PlanRestore"/>): Alive → 체력 비율, Soul → Soul, PermanentDead → PermanentDead. DeadPresentation 은 기록 때 이미 확정돼 오지 않는다.
///  · 체력(<see cref="ResolveRestoreHealth"/>): Alive 는 1 이상(살아 있는데 0 이 되지 않게), Soul·PermanentDead 는 0.
///  · 전멸 리스폰 덮어쓰기: 부재자 기록을 <see cref="PlayerRejoinLifeRecord.AliveFull"/> 로(<c>RejoinMemory.OverwriteAbsentLivesAfterWipe</c>).
/// </summary>
public static class PlayerRejoinLifeRules
{
    /// <summary>비율을 0~1 로 자른다. NaN·무한은 1(모르면 만피 — 체력을 잃게 하지 않는다).</summary>
    public static float ClampRatio(float ratio)
    {
        if (float.IsNaN(ratio) || float.IsInfinity(ratio))
            return 1f;
        return ratio < 0f ? 0f : ratio > 1f ? 1f : ratio;
    }

    /// <summary>
    /// 퇴장 순간 기록. <paramref name="partyLives"/>·<paramref name="keepsSoul"/> 는 사망 목적지를 확정할 때만 쓴다
    /// (<c>PlayerLifeCycleController</c> 의 사망 목적지 판정과 같은 입력). 최대 체력 0 이하면 비율 1.
    /// </summary>
    public static PlayerRejoinLifeRecord Capture(PlayerLifeState state, int currentHealth, int maxHealth, int partyLives, bool keepsSoul)
    {
        bool dying = state == PlayerLifeState.DeadPresentation ||
                     (state == PlayerLifeState.Alive && maxHealth > 0 && currentHealth <= 0);
        if (dying)
            return new PlayerRejoinLifeRecord(PartyLivesRules.ResolveDeathDestination(partyLives, keepsSoul), 0f);

        if (state != PlayerLifeState.Alive)
            return new PlayerRejoinLifeRecord(state, 0f);

        float ratio = maxHealth > 0 ? (float)currentHealth / maxHealth : 1f;
        return new PlayerRejoinLifeRecord(PlayerLifeState.Alive, ratio);
    }

    /// <summary>복원할 일. DeadPresentation 이 들어오면(기록 규칙을 거치지 않은 값) Soul — 부활 가능한 쪽으로(사망 목적지 폴백과 같은 방향).</summary>
    public static PlayerRejoinLifeRestore PlanRestore(PlayerLifeState state)
    {
        switch (state)
        {
            case PlayerLifeState.Alive:
                return PlayerRejoinLifeRestore.HealthRatio;
            case PlayerLifeState.PermanentDead:
                return PlayerRejoinLifeRestore.PermanentDead;
            case PlayerLifeState.Soul:
            case PlayerLifeState.DeadPresentation:
            default:
                return PlayerRejoinLifeRestore.Soul;
        }
    }

    /// <summary>복원 체력. Alive = round(비율 × 최대) 를 1~최대로, Soul·PermanentDead = 0. 최대 0 이하면 0.</summary>
    public static int ResolveRestoreHealth(PlayerRejoinLifeRestore restore, float hpRatio, int maxHealth)
    {
        if (maxHealth <= 0 || restore != PlayerRejoinLifeRestore.HealthRatio)
            return 0;

        int health = (int)Math.Round(ClampRatio(hpRatio) * maxHealth, MidpointRounding.AwayFromZero);
        return health < 1 ? 1 : health > maxHealth ? maxHealth : health;
    }
}
