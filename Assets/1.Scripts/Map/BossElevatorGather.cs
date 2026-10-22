using System.Collections.Generic;

/// <summary>참가자 한 명의 이번 판정 입력 — 서버(<see cref="BossTeleportManager"/>)가 매 프레임 읽어 넣는다.</summary>
public readonly struct BossElevatorGatherMember
{
    public BossElevatorGatherMember(ulong clientId, bool connected, PlayerLifeState lifeState, float health, bool onPad, bool busy)
    {
        ClientId = clientId;
        Connected = connected;
        LifeState = lifeState;
        Health = health;
        OnPad = onPad;
        Busy = busy;
    }

    public ulong ClientId { get; }

    /// <summary>접속 중이고 PlayerObject 가 있다.</summary>
    public bool Connected { get; }

    /// <summary>생명 상태. 생명주기 컴포넌트가 없는 구성은 호출자가 체력으로 Alive/PermanentDead 를 정해 넣는다.</summary>
    public PlayerLifeState LifeState { get; }

    public float Health { get; }

    /// <summary>본체 위치가 발판(<see cref="BossEnterTrigger"/> 박스) 안.</summary>
    public bool OnPad { get; }

    /// <summary>진행 중 행동(Player.CurrentState 가 Idle/Move 아님) — 카운트 시작만 미룬다.</summary>
    public bool Busy { get; }
}

/// <summary>판정 활성 조건 입력 — 하나라도 걸리면 집결 판정을 끈다(끄는 순간 카운트도 0).</summary>
public readonly struct BossElevatorGatherConditions
{
    public BossElevatorGatherConditions(bool padEnabled, bool forcedCountdown, bool elevatorRunning, bool awaitingArrival)
    {
        PadEnabled = padEnabled;
        ForcedCountdown = forcedCountdown;
        ElevatorRunning = elevatorRunning;
        AwaitingArrival = awaitingArrival;
    }

    /// <summary>튜토리얼 발판 잠금이 아니다(<see cref="TutorialBossPath.IsBossPadEnabled"/>).</summary>
    public bool PadEnabled { get; }

    /// <summary>제한시간 만료·구간 스킵 강제 카운트다운 진행 중(<see cref="BossTeleportManager.ForceStartEncounter"/>).</summary>
    public bool ForcedCountdown { get; }

    /// <summary>엘리베이터 연출(또는 리그 없는 즉시 이동) 진행 중.</summary>
    public bool ElevatorRunning { get; }

    /// <summary>보스방 도착 ACK 대기 중.</summary>
    public bool AwaitingArrival { get; }

    public bool IsJudgingActive => PadEnabled && !ForcedCountdown && !ElevatorRunning && !AwaitingArrival;
}

/// <summary>
/// [서버] 보스 엘리베이터 파티 집결(할 일 89) — 참가자 전원이 발판에 모여 유지 시간(기본 5초, <see cref="BossElevatorRig"/> 데이터)을 채우면 확정 1회.
/// 판정 자체는 할 일 86 의 <see cref="PartyGatherRules"/> 를 그대로 쓰고, 여기서는 보스 진입 입력 → 집결 입력 매핑만 한다. Unity·NGO 비의존(EditMode 테스트 대상).
/// <list type="bullet">
/// <item>참가자 = 접속 중 + PermanentDead 아님(<see cref="BossTeleportManager"/> 의 이동 대상과 같은 기준). 인원 변화 → 카운트 0.</item>
/// <item>쓰러짐 = Alive 아님 또는 체력 0 이하 — Soul 도 집결로 인정하지 않는다(살아날 때까지 기다린다). 사망·이탈 → 카운트 0, 다시 모이면 처음부터.</item>
/// <item>진행 중 행동이 있으면 카운트 시작만 미룬다(시작 뒤 행동은 끊지 않는다).</item>
/// <item>발판 잠김·강제 카운트다운·연출·도착 대기 중이면 판정하지 않는다(<see cref="BossElevatorGatherConditions"/>).</item>
/// </list>
/// </summary>
public sealed class BossElevatorGather
{
    readonly PartyGatherRules _rules;
    readonly List<ulong> _participants = new List<ulong>();

    public BossElevatorGather(double holdSeconds = PartyGatherRules.DefaultHoldSeconds)
    {
        _rules = new PartyGatherRules(holdSeconds);
    }

    /// <summary>유지 시간(초). 바꿔도 이미 시작한 카운트의 종료 시각은 그대로다.</summary>
    public double HoldSeconds
    {
        get => _rules.HoldSeconds;
        set => _rules.HoldSeconds = value;
    }

    public bool IsActive => _rules.IsActive;
    public bool IsConfirmed => _rules.IsConfirmed;
    public bool IsCountdownActive => _rules.IsCountdownActive;

    /// <summary>카운트 종료 서버 시각. <see cref="PartyGatherRules.NoCountdown"/>(0) = 없음.</summary>
    public double CountdownEndTime => _rules.CountdownEndTime;

    /// <summary>"집결 n/m" 의 n — 발판 위 + 쓰러짐 아님.</summary>
    public int GatheredCount => _rules.GatheredCount;

    /// <summary>"집결 n/m" 의 m — 참가자 수.</summary>
    public int ParticipantCount => _rules.ParticipantCount;

    /// <summary>이동 대상(참가자)인가 — 접속 중 + PermanentDead 아님.</summary>
    public static bool IsParticipant(bool connected, PlayerLifeState lifeState) =>
        connected && lifeState != PlayerLifeState.PermanentDead;

    /// <summary>쓰러짐 — Alive 아님(DeadPresentation·Soul 포함) 또는 체력 0 이하.</summary>
    public static bool IsIncapacitated(PlayerLifeState lifeState, float health) =>
        lifeState != PlayerLifeState.Alive || health <= 0f;

    /// <summary>진행 중 행동 — Idle/Move 가 아닌 상태(할 일 86 규칙 그대로).</summary>
    public static bool IsBusy(PlayerActionState state) =>
        state != PlayerActionState.Idle && state != PlayerActionState.Move;

    public static PartyGatherMemberState ToMemberState(BossElevatorGatherMember member) =>
        new PartyGatherMemberState(member.OnPad, IsIncapacitated(member.LifeState, member.Health), member.Busy);

    /// <summary>
    /// 판정 한 번 — 확정 순간에만 true(1회 래치, <see cref="ResetConfirmation"/> 로만 푼다).
    /// 참가자 아닌 입력(끊김·PermanentDead)은 빼고 명단을 맞춘 뒤, 활성 조건·상태를 넣고 <see cref="PartyGatherRules.Tick"/>.
    /// </summary>
    public bool Tick(double now, BossElevatorGatherConditions conditions, IReadOnlyList<BossElevatorGatherMember> members)
    {
        _participants.Clear();
        if (members != null)
        {
            for (int i = 0; i < members.Count; i++)
            {
                if (IsParticipant(members[i].Connected, members[i].LifeState))
                    _participants.Add(members[i].ClientId);
            }
        }

        _rules.SetParticipants(_participants);
        _rules.SetActive(conditions.IsJudgingActive);

        if (members != null)
        {
            for (int i = 0; i < members.Count; i++)
                _rules.SetMemberState(members[i].ClientId, ToMemberState(members[i]));
        }

        return _rules.Tick(now);
    }

    /// <summary>확정 래치·카운트 초기화 — 재도전 리셋(<see cref="BossTeleportManager.ResetForRetryServer"/>).</summary>
    public void ResetConfirmation() => _rules.ResetConfirmation();
}
