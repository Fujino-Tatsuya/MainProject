using System;

/// <summary>할 일 119 즉사 패턴 종류 — 로그·판 파일 <c>lethal=</c> 토큰.</summary>
public enum QABoss103LethalKind
{
    None = 0,
    /// <summary>북진 압박 차징 실패 방전(<c>BeginPushNorthCharge</c> — 1회차 = 안전 구역 제외 칸, 2회차~ = 방 전체 전멸기).</summary>
    PushNorth = 1,
    /// <summary>④ 폐기 처분 저지 실패 대폭발(방 전체 전멸기).</summary>
    SelfDestruct = 2,
}

/// <summary>
/// 할 일 119 — 즉사 패턴 판별(순수, EditMode <c>QABoss103Round2Tests</c>). 할 일 105 로 게임이 즉사기에서 무적(대시 포함)을 무시하므로
/// 봇은 이 위험에 회피 굴림·대시를 하지 않고 '즉사(굴림 없음)'로 따로 센다. 게임 코드에서 즉사 + 무적 무시인 방전은 둘뿐이다
/// (<c>ServerQueueDischarge(…, lethal: true, ignoreInvulnerability: …)</c> 호출처):
/// <list type="bullet">
/// <item>북진 압박 — <c>TwentyThreeBoss.RaidRelay.BeginPushNorthCharge</c>: 예고 = 차징 시간(1회차 <c>pushNorthFirstChargeTime</c>, 그 뒤 <c>pushNorthChargeTime</c>) + 탑 상승 여유(<c>BossChargeSequence.RiseGrace</c>).
///   데이터 칸 <c>pushNorthDischargeIgnoreInvuln</c> ≠ 0 일 때만 무적으로 못 피한다.</item>
/// <item>④ 폐기 처분 실패 대폭발 — 방 전체(<c>AllMask</c>), 예고 <c>raidSelfDestructBlastWarn</c>. <c>raidSelfDestructBlastLethal</c> 이고 <c>raidSelfDestructBlastIgnoreInvuln</c> ≠ 0 일 때.</item>
/// </list>
/// 클라는 방전의 즉사 여부를 예고 때 모른다(<c>ClientWarnShown(mask, warn)</c> 뿐 — 즉사는 발동 <c>ClientFired</c> 에서야 온다) → 예고 마스크·시간을 데이터 값과 맞춘다.
/// 맞춘 값이 틀렸는지는 봇이 발동 순간 <c>lethal_check</c> 로그로 남긴다. 나머지 방전(② 실패·집결·측면·연쇄 실패)은 예고 시간이 달라 겹치지 않는다(No23 기본값 기준).
/// </summary>
public static class QABoss103Lethal
{
    /// <summary>예고 시간 일치 허용(초) — RPC 로 float 그대로 오므로 사실상 같은 값이다.</summary>
    public const float WarnMatchSeconds = 0.05f;

    /// <summary>서버 방전 예고 하한(<c>BossElectricFloor.ServerQueueDischarge</c> 의 <c>Mathf.Max(0.3f, warnTime)</c>).</summary>
    public const float MinDischargeWarn = 0.3f;

    /// <summary>판별에 쓰는 데이터 값(보스 데이터 SO — 테이블 모드면 피어마다 같은 xlsx 값).</summary>
    public struct Data
    {
        public bool PushNorthUnavoidable;
        public float PushNorthFirstWarn;
        public float PushNorthWarn;
        public bool SelfDestructUnavoidable;
        public float SelfDestructWarn;
    }

    /// <summary>
    /// 데이터 칸 → 판별 값. 북진 예고 = 차징 + 상승 여유, 자폭 = 즉사 칸이 켜졌고 무적 무시 칸 ≠ 0. 모두 서버 예고 하한을 적용한다.
    /// </summary>
    public static Data From(float pushNorthFirstChargeTime, float pushNorthChargeTime, float riseGrace, int pushNorthIgnoreInvuln,
                            bool selfDestructLethal, int selfDestructIgnoreInvuln, float selfDestructWarn) => new Data
    {
        PushNorthUnavoidable = pushNorthIgnoreInvuln != 0,
        PushNorthFirstWarn = Math.Max(MinDischargeWarn, pushNorthFirstChargeTime + riseGrace),
        PushNorthWarn = Math.Max(MinDischargeWarn, pushNorthChargeTime + riseGrace),
        SelfDestructUnavoidable = selfDestructLethal && selfDestructIgnoreInvuln != 0,
        SelfDestructWarn = Math.Max(MinDischargeWarn, selfDestructWarn),
    };

    /// <summary>전기 장판 예고 1건(마스크·예고 시간) → 무적으로 못 피하는 즉사인가. 아니면 <see cref="QABoss103LethalKind.None"/>.</summary>
    public static QABoss103LethalKind ClassifyFloor(ulong mask, float warnTime, ulong allMask, in Data d)
    {
        if (mask == 0UL)
            return QABoss103LethalKind.None;
        if (d.SelfDestructUnavoidable && mask == allMask && Near(warnTime, d.SelfDestructWarn))
            return QABoss103LethalKind.SelfDestruct;
        if (d.PushNorthUnavoidable && (Near(warnTime, d.PushNorthFirstWarn) || Near(warnTime, d.PushNorthWarn)))
            return QABoss103LethalKind.PushNorth;
        return QABoss103LethalKind.None;
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) <= WarnMatchSeconds;

    public static string Token(QABoss103LethalKind kind)
    {
        switch (kind)
        {
            case QABoss103LethalKind.PushNorth: return "push_north";
            case QABoss103LethalKind.SelfDestruct: return "self_destruct";
            default: return "none";
        }
    }
}

/// <summary>
/// 할 일 119 — 벽 끼임 감시(순수, EditMode <c>QABoss103Round2Tests</c>). 오너 봇이 매 틱 '벽 속·보스방 경계 밖' 여부를 넣는다.
/// <see cref="ConfirmSeconds"/> 동안 이어지면 탈출(QA 서버 재배치) 1회, 그 뒤 <see cref="CooldownSeconds"/> 는 다시 요청하지 않는다(재배치가 반영될 시간).
/// 대시가 벽을 스치는 한두 프레임은 탈출 대상이 아니다. 기존 끼임 단계(2초 옆걸음·6초 대시·20초 재배치)는 이 감시가 끼임을 보는 동안·쿨다운 동안 멈춘다(<see cref="Busy"/>).
/// </summary>
public sealed class QABoss103WallWatch
{
    public const double ConfirmSeconds = 0.25;
    public const double CooldownSeconds = 2.0;

    private bool _bad;
    private double _since;
    private double _cooldownUntil = double.NegativeInfinity;

    public int Escapes { get; private set; }

    /// <summary>끼임을 보는 중이거나 탈출 직후 — 기존 끼임 단계를 돌리지 않는다.</summary>
    public bool Busy(double now) => _bad || now < _cooldownUntil;

    /// <summary>매 틱. <paramref name="stuck"/> = 지금 벽 속·경계 밖. 탈출을 요청할 순간이면 true.</summary>
    public bool Tick(double now, bool stuck)
    {
        if (!stuck)
        {
            _bad = false;
            return false;
        }
        if (!_bad)
        {
            _bad = true;
            _since = now;
        }
        if (now < _cooldownUntil || now - _since < ConfirmSeconds)
            return false;
        Escapes++;
        _cooldownUntil = now + CooldownSeconds;
        _since = now;
        return true;
    }

    public void Reset()
    {
        _bad = false;
        _cooldownUntil = double.NegativeInfinity;
    }

    /// <summary>방 로컬 좌표(x,z)가 경계 안쪽 면 밖인가 — <paramref name="slack"/>(m) 만큼은 봐준다(경계 면에 붙어 선 몸 반경).</summary>
    public static bool OutsideArena(float x, float z, float minX, float maxX, float minZ, float maxZ, float slack) =>
        x < minX - slack || x > maxX + slack || z < minZ - slack || z > maxZ + slack;
}

/// <summary>
/// 할 일 119 — F10 디버그 부활 요청 시각(순수, EditMode <c>QABoss103Round2Tests</c>). 오너 봇이 자기 플레이어가 Soul 이 된 순간부터
/// <see cref="DelaySeconds"/>(기존 QA 자동 부활과 같은 3초 — <c>MapSceneDeathRulesModel.DefaultReviveDelaySeconds</c>) 뒤 F10 과 같은 RPC 를 보낸다.
/// 아직 Soul 이면 <see cref="RetrySeconds"/> 마다 다시 보낸다(서버가 성공할 때만 목숨을 깎으므로 재요청은 목숨을 더 쓰지 않는다).
/// 공용 목숨이 0 이면 보내지 않는다(게임도 거부 — 전멸 판정으로 간다). Soul 을 벗어나면 초기화.
/// </summary>
public sealed class QABoss103ReviveTimer
{
    public const double RetrySeconds = 2.0;

    public readonly double DelaySeconds;

    private bool _soul;
    private double _dueAt;

    public int Requests { get; private set; }

    public QABoss103ReviveTimer(double delaySeconds) => DelaySeconds = delaySeconds;

    /// <summary>매 틱. <paramref name="soul"/> = 내 플레이어가 Soul, <paramref name="livesLeft"/> = 공용 목숨(모르면 음수 — 보낸다). 지금 요청하면 true.</summary>
    public bool Tick(double now, bool soul, int livesLeft)
    {
        if (!soul)
        {
            _soul = false;
            return false;
        }
        if (!_soul)
        {
            _soul = true;
            _dueAt = now + DelaySeconds;
        }
        if (now < _dueAt || livesLeft == 0)
            return false;
        _dueAt = now + RetrySeconds;
        Requests++;
        return true;
    }
}

/// <summary>
/// 할 일 119 — 판 종료 신호 래치(순수, EditMode <c>QABoss103Round2Tests</c>). 호스트 판정 루프가 매 틱 폴링하는 값과 이벤트 신호를 한 곳에 모아
/// '무엇으로 판정했나'(runs.csv <c>outcome_source</c>)를 남긴다. 신호는 한 번 켜지면 꺼지지 않는다 — 보스 사망 뒤 결과 화면 전환으로 디렉터가 사라져도 클리어가 유지된다.
/// <list type="bullet">
/// <item>클리어 = 보스 <c>Unit.Died</c>(서버, 게임의 실제 사망 신호) 또는 <c>BossEncounterDirector.IsBossDefeatedServer</c>(같은 Died 를 구독한 디렉터의 클리어 확정 — 결과 화면 예약).</item>
/// <item>전멸 = <c>PartyWipeRespawner.PartyWipeRespawned</c> 또는 보스를 잡지 않고 맵을 떠남(결과 화면 — 리스폰 대상 아님).</item>
/// </list>
/// </summary>
public sealed class QABoss103EndSignals
{
    public const string SourceBossDied = "boss_died";
    public const string SourceDirector = "director_defeated";
    public const string SourceWipe = "party_wipe_respawned";
    public const string SourceLeftMap = "left_map";
    public const string SourceTimeout = "combat_limit";

    public bool BossDied { get; private set; }
    public bool DirectorDefeated { get; private set; }
    public bool Wiped { get; private set; }
    public bool LeftMap { get; private set; }

    public void OnBossDied() => BossDied = true;
    public void OnWipe() => Wiped = true;

    /// <summary>폴링 값 — 켜진 것만 반영한다(꺼짐은 무시).</summary>
    public void Poll(bool directorDefeated, bool leftMap)
    {
        DirectorDefeated |= directorDefeated;
        LeftMap |= leftMap;
    }

    public bool BossDead => BossDied || DirectorDefeated;

    /// <summary>판정(<see cref="QABoss103Plan.Judge"/> — 클리어 &gt; 전멸 &gt; 시간초과).</summary>
    public QABoss103Outcome Judge(double combatElapsedSeconds) =>
        QABoss103Plan.Judge(BossDead, Wiped || (LeftMap && !BossDead), combatElapsedSeconds);

    /// <summary>판정 출처 토큰. 클리어 = 켜진 신호 모두(<c>boss_died+director_defeated</c>), 전멸 = 리스폰 우선, 시간초과 = 상한.</summary>
    public string Source(QABoss103Outcome outcome)
    {
        switch (outcome)
        {
            case QABoss103Outcome.Clear:
                return BossDied && DirectorDefeated ? SourceBossDied + "+" + SourceDirector : BossDied ? SourceBossDied : SourceDirector;
            case QABoss103Outcome.Wipe:
                return Wiped ? SourceWipe : SourceLeftMap;
            case QABoss103Outcome.Timeout:
                return SourceTimeout;
            default:
                return "";
        }
    }
}
