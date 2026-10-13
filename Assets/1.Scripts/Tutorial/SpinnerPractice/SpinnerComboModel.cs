using System;

/// <summary>
/// 튜토리얼 스피너봇 연계 연습(v8 §3.5)의 단계. 전 피어에 복제된다(<c>TutorialSpinnerPractice.ComboPhase</c>).
/// 연습 모드(<see cref="SpinnerPracticeMode.Practice"/>) 안에서만 의미가 있다.
/// </summary>
public enum SpinnerComboPhase : byte
{
    /// <summary>준비·간파 — 회전 공격을 예고하며 간파 창을 연다(원래 연습 루프).</summary>
    Ready = 0,
    /// <summary>간파 성공 직후 기절(원래 그로기). 취약 — 우클릭 밀치기 가능.</summary>
    Stunned = 1,
    /// <summary>취약 유지 — 벽 충돌 전까지 시간으로 끝나지 않는다. 스스로 이동·추격·공격하지 않는다(밀치기 이동만).</summary>
    Vulnerable = 2,
    /// <summary>벽 충돌 성공 뒤 짧은 회복 — 끝나면 그 자리에서 다시 준비·간파(원위치 복귀 없음 — 사용자 변경).</summary>
    Recovering = 3,
    /// <summary>연계 목표(3회) 달성 + 마지막 회복 끝 — 본 전투(G5) 대기.</summary>
    Done = 4,
}

/// <summary><see cref="SpinnerComboModel"/> 이 한 번의 호출에서 낸 일(여러 개가 겹칠 수 있다).</summary>
[Flags]
public enum SpinnerComboEvent : byte
{
    None = 0,
    /// <summary>기절이 끝나 취약 유지로 — 서버 FSM 을 세운다.</summary>
    StunEnded = 1 << 0,
    /// <summary>유효 벽 충돌 — 연계 1회 성공(<see cref="SpinnerComboModel.Successes"/> 증가).</summary>
    WallHit = 1 << 1,
    /// <summary>밀치기가 벽에 닿지 않고 끝났다(거리 소진·벽 아닌 곳에 막힘) — 취약은 유지.</summary>
    PushEndedNoWall = 1 << 2,
    /// <summary>회복 끝 — 목표 전이면 그 자리에서 다시 준비·간파.</summary>
    ReadyAgain = 1 << 3,
    /// <summary>회복 끝 + 목표 달성 — 본 전투로(G5).</summary>
    Completed = 1 << 4,
}

/// <summary>
/// 튜토리얼 스피너봇 연계 연습 상태 기계(순수 C# — EditMode 로 고정). v8 §3.5:
/// [간파 성공 → 기절 → 취약 유지(시간 종료 없음) → 우클릭 밀치기 → 유효 벽 충돌 → 회복] × 목표(3).
/// 회복이 끝나면 그 자리에서 다음 준비·간파(사용자 변경 — 원위치 복귀 없음). 3회째 회복 끝 = 완료.
/// <list type="bullet">
/// <item>간파 성공은 <see cref="SpinnerComboPhase.Ready"/> 에서만 받는다(하나의 간파 창 = 성공 1번은 창 쪽이 이미 보장).</item>
/// <item>밀치기는 기절·취약 중 우클릭 간파 적중만(호출자가 거른다). 진행 중 밀치기에 새 밀치기가 오면 방향·거리를 새로 잡는다.</item>
/// <item>벽 충돌 = 밀치기 이동 뒤 몸 가장자리 ↔ 유효 벽 면 거리가 <see cref="ContactSlack"/> 이하,
///   또는 이동이 막혔는데(NavMesh 가장자리) 그 거리가 <see cref="BlockedContactSlack"/> 이하. 한 연계에 1번만 센다.</item>
/// <item>회복 중에는 밀치기·간파를 받지 않는다. 사망·이탈로 성공 횟수를 되돌리지 않는다.</item>
/// </list>
/// 이동 자체(NavMeshAgent.Move)·벽 거리 계산은 호출자(<c>TutorialSpinnerPractice</c>) 몫 — 여기는 규칙만.
/// </summary>
public sealed class SpinnerComboModel
{
    public const int DefaultTarget = 3;

    public SpinnerComboModel(int target = DefaultTarget, float stunSeconds = 1f, float recoverSeconds = 0.6f,
                             float pushDistance = 3f, float pushSeconds = 0.35f,
                             float contactSlack = 0.4f, float blockedContactSlack = 1.6f)
    {
        Target = Math.Max(1, target);
        StunSeconds = Math.Max(0f, stunSeconds);
        RecoverSeconds = Math.Max(0f, recoverSeconds);
        PushDistance = Math.Max(0.01f, pushDistance);
        PushSpeed = PushDistance / Math.Max(0.01f, pushSeconds);
        ContactSlack = Math.Max(0f, contactSlack);
        BlockedContactSlack = Math.Max(ContactSlack, blockedContactSlack);
    }

    public int Target { get; }
    public float StunSeconds { get; }
    public float RecoverSeconds { get; }
    public float PushDistance { get; }
    public float PushSpeed { get; }
    public float ContactSlack { get; }
    public float BlockedContactSlack { get; }

    public SpinnerComboPhase Phase { get; private set; } = SpinnerComboPhase.Ready;
    public int Successes { get; private set; }

    public bool IsPushing { get; private set; }
    public float PushDirX { get; private set; }
    public float PushDirZ { get; private set; }
    public float PushRemaining { get; private set; }

    private float _timer;

    /// <summary>취약(기절 포함) — 우클릭 밀치기를 받는 구간.</summary>
    public bool IsVulnerable => Phase == SpinnerComboPhase.Stunned || Phase == SpinnerComboPhase.Vulnerable;

    /// <summary>서버 FSM 을 세워 두는가 — 기절 뒤 취약 유지·회복(기절 동안은 원래 그로기가 돈다). 완료 뒤는 본 모드 전환이 맡는다.</summary>
    public bool SuspendsFsm =>
        Phase == SpinnerComboPhase.Vulnerable || Phase == SpinnerComboPhase.Recovering;

    /// <summary>외부 넉백을 막아 두는가(슈퍼 아머) — 간파 성공 뒤 회복 끝까지. 밀치기 이동은 이 모델이 직접 한다.</summary>
    public bool BlocksExternalKnockback => Phase != SpinnerComboPhase.Ready && Phase != SpinnerComboPhase.Done;

    /// <summary>원래 연습 루프(스핀·간파 창)를 돌리는가 — 준비·간파 단계만. 취약·회복 중에는 새 간파 공격을 예고하지 않는다.</summary>
    public bool AllowsSpin => Phase == SpinnerComboPhase.Ready;

    /// <summary>스폰 반경 클램프를 거는가 — 준비·간파 단계만(밀치기는 벽까지 움직여야 한다).</summary>
    public bool ClampsToPracticeRadius => Phase == SpinnerComboPhase.Ready;

    /// <summary>간파 성공(이 스피너봇). 준비 단계면 기절로 가고 true — 호출자는 파티 우클릭 쿨을 1회 초기화한다.</summary>
    public bool NotifyCounterSucceeded()
    {
        if (Phase != SpinnerComboPhase.Ready) return false;
        Phase = SpinnerComboPhase.Stunned;
        _timer = StunSeconds;
        IsPushing = false;
        return true;
    }

    /// <summary>
    /// 우클릭 간파 적중(밀치기). 취약 중이면 (dirX, dirZ) = 공격자 → 스피너봇 방향으로 밀치기를 (다시) 시작하고 true.
    /// 방향 길이가 0 이면 거절.
    /// </summary>
    public bool TryBeginPush(float dirX, float dirZ)
    {
        if (!IsVulnerable) return false;
        float len = (float)Math.Sqrt(dirX * dirX + dirZ * dirZ);
        if (len < 1e-4f) return false;

        PushDirX = dirX / len;
        PushDirZ = dirZ / len;
        PushRemaining = PushDistance;
        IsPushing = true;
        return true;
    }

    /// <summary>이번 프레임에 밀어야 할 거리(m). 밀치기 중이 아니면 0.</summary>
    public float PushStep(float deltaSeconds) =>
        IsPushing ? Math.Min(PushRemaining, PushSpeed * Math.Max(0f, deltaSeconds)) : 0f;

    /// <summary>
    /// 밀치기 이동 결과 보고. <paramref name="requested"/> = <see cref="PushStep"/> 값, <paramref name="moved"/> = 실제로 움직인 수평 거리,
    /// <paramref name="wallGap"/> = 이동 뒤 몸 가장자리 ↔ 유효 벽 면 거리(m, 벽 안으로 들어가면 음수).
    /// <paramref name="towardWall"/> = 밀치기 방향이 벽 쪽인가(벽 면 법선과 반대 방향 성분이 있음).
    /// </summary>
    public SpinnerComboEvent ReportPushMoved(float requested, float moved, float wallGap, bool towardWall)
    {
        if (!IsPushing) return SpinnerComboEvent.None;

        bool blocked = requested > 1e-4f && moved < requested * 0.5f;
        bool hit = towardWall && (wallGap <= ContactSlack || (blocked && wallGap <= BlockedContactSlack));
        if (hit)
        {
            IsPushing = false;
            PushRemaining = 0f;
            Successes = Math.Min(Target, Successes + 1);
            Phase = SpinnerComboPhase.Recovering;
            _timer = RecoverSeconds;
            return SpinnerComboEvent.WallHit;
        }

        PushRemaining = Math.Max(0f, PushRemaining - Math.Max(0f, moved));
        if (blocked || PushRemaining <= 0f)
        {
            IsPushing = false;
            PushRemaining = 0f;
            return SpinnerComboEvent.PushEndedNoWall;
        }
        return SpinnerComboEvent.None;
    }

    /// <summary>
    /// 시간 진행(기절 → 취약 유지, 회복 → 그 자리에서 준비·간파 또는 완료). 취약 유지는 시간으로 끝나지 않는다.
    /// 회복 끝: 목표 전이면 <see cref="SpinnerComboEvent.ReadyAgain"/>, 달성이면 <see cref="SpinnerComboEvent.Completed"/>.
    /// </summary>
    public SpinnerComboEvent Tick(float deltaSeconds)
    {
        float dt = Math.Max(0f, deltaSeconds);
        switch (Phase)
        {
            case SpinnerComboPhase.Stunned:
                _timer -= dt;
                if (_timer > 0f) return SpinnerComboEvent.None;
                Phase = SpinnerComboPhase.Vulnerable;
                return SpinnerComboEvent.StunEnded;

            case SpinnerComboPhase.Recovering:
                _timer -= dt;
                if (_timer > 0f) return SpinnerComboEvent.None;
                if (Successes >= Target)
                {
                    Phase = SpinnerComboPhase.Done;
                    return SpinnerComboEvent.Completed;
                }
                Phase = SpinnerComboPhase.Ready;
                return SpinnerComboEvent.ReadyAgain;

            default:
                return SpinnerComboEvent.None;
        }
    }

    /// <summary>본 전투 전환(G5 — 디버그로 일찍 열린 경우 포함). 교육용 규칙을 모두 끈다.</summary>
    public void Finish()
    {
        Phase = SpinnerComboPhase.Done;
        IsPushing = false;
        PushRemaining = 0f;
    }
}

/// <summary>연계 연습 지오메트리(순수) — 유효 벽 면 거리.</summary>
public static class SpinnerComboGeometry
{
    /// <summary>
    /// 몸 가장자리 ↔ 벽 면 거리. 벽 = 점 (wx, wz) 을 지나고 방 안쪽 법선 (nx, nz)(단위)인 직선. 벽 안으로 파고들면 음수.
    /// </summary>
    public static float WallGap(float x, float z, float wx, float wz, float nx, float nz, float bodyRadius) =>
        (x - wx) * nx + (z - wz) * nz - Math.Max(0f, bodyRadius);

    /// <summary>밀치기 방향이 벽 쪽인가 — 방 안쪽 법선과의 내적이 -<paramref name="minCos"/> 이하.</summary>
    public static bool IsTowardWall(float dirX, float dirZ, float nx, float nz, float minCos = 0.3f) =>
        dirX * nx + dirZ * nz <= -Math.Abs(minCos);
}
