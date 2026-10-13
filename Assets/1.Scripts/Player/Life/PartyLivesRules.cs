using System;
using System.Collections.Generic;

// 파티 공용 목숨 풀의 순수 규칙(Unity·NGO 비의존 — EditMode 테스트 대상).
// 원본: Docs/tech/party-lives-respawn.md §2. 배선은 MultiGameRule(서버 쓰기)·PartyWipeWatcher(판정)가 한다.

/// <summary>전멸 판정에 넣는 한 플레이어의 생명 상태.</summary>
public readonly struct PartyMemberLife
{
    public readonly PlayerLifeState State;
    /// <summary>목숨 차감 없는 부활을 기다리는 중(튜토리얼 G11 전 사망 — <c>PlayerLifeCycleController.ServerDeathKeepsSoul</c>).</summary>
    public readonly bool HasFreeRevive;

    public PartyMemberLife(PlayerLifeState state, bool hasFreeRevive)
    {
        State = state;
        HasFreeRevive = hasFreeRevive;
    }
}

/// <summary>
/// 파티 공용 목숨 풀(할 일 30 (1)) — 클라별 목숨 대신 파티가 한 풀을 같이 쓴다.
///
///  · 사망 목적지: 차감 없는 부활이 걸렸거나(튜토리얼 G11 전) 풀 &gt; 0 이면 Soul, 아니면 PermanentDead. 이 시점엔 차감하지 않는다.
///  · 차감: Soul → Alive 부활이 <b>성공한 직후</b> 1개(차감 부활만). 풀 0 이면 차감 부활은 거부된다.
///  · 풀이 0 이 되면 이미 Soul 인 사람도 차감 부활을 못 한다 — 그 Soul 은 전멸 판정에서 '탈락'으로 센다(<see cref="IsPartyWiped"/>).
///  · 재충전(전멸 리스폰 — 2단계): 풀을 용량으로 되돌린다.
/// </summary>
public static class PartyLivesRules
{
    public const int DefaultCapacity = 5;

    public static int ClampLives(int lives) => Math.Max(0, lives);

    /// <summary>사망 연출이 끝날 때 갈 상태.</summary>
    public static PlayerLifeState ResolveDeathDestination(int partyLives, bool keepsSoul) =>
        keepsSoul || partyLives > 0 ? PlayerLifeState.Soul : PlayerLifeState.PermanentDead;

    /// <summary>부활을 승인할 수 있는가. 차감 없는 부활은 풀과 무관하다.</summary>
    public static bool CanRevive(int partyLives, bool consumeLife) => !consumeLife || partyLives > 0;

    /// <summary>차감 부활 성공 직후 1개 뺀다. 풀이 0 이면 false(값 그대로).</summary>
    public static bool TryConsume(int partyLives, out int remaining)
    {
        remaining = ClampLives(partyLives);
        if (remaining <= 0) return false;
        remaining--;
        return true;
    }

    /// <summary>재충전 값 = 용량(음수는 0).</summary>
    public static int Refill(int capacity) => ClampLives(capacity);

    /// <summary>이 플레이어가 아직 다시 싸울 수 있는가(전멸이 아닌 이유가 되는가).</summary>
    public static bool IsStillInPlay(PartyMemberLife member, int partyLives)
    {
        switch (member.State)
        {
            case PlayerLifeState.Alive:
            case PlayerLifeState.DeadPresentation: // 곧 Soul/PermanentDead 로 정해진다 — 그때 다시 본다
                return true;
            case PlayerLifeState.Soul:
                return member.HasFreeRevive || partyLives > 0;
            default:
                return false;
        }
    }

    /// <summary>
    /// 전멸인가 — 전원이 PermanentDead 이거나, 풀이 0 이라 부활할 수 없는 Soul 이다.
    /// 0명은 전멸이 아니다(스폰 전 오판 방지 — <c>PartyWipeWatcher</c> 와 같은 규칙).
    /// </summary>
    public static bool IsPartyWiped(int partyLives, IReadOnlyList<PartyMemberLife> members)
    {
        if (members == null || members.Count == 0) return false;
        for (int i = 0; i < members.Count; i++)
            if (IsStillInPlay(members[i], partyLives)) return false;
        return true;
    }
}
