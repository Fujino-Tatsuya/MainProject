#if UNITY_EDITOR
using System;
using System.Globalization;
using System.Text;

/// <summary>
/// 할 일 116-C6 — 대시 1회가 어디서 왜 멈췄나(대시 끊긴 이유)의 순수 규칙(EditMode <c>QABoss116LabTests</c>).
/// 신호 = 게임 코드가 에디터에서 이미 남기는 <c>[Dash]</c>·<c>[DashInterrupt]</c> 로그(<c>Edit.Log</c> — 에디터 전용이라 랩도 에디터 전용)를
/// 랩 디렉터가 <c>Application.logMessageReceived</c> 로 받아 <see cref="QABoss116DashRecord.Feed"/> 에 넣고, 매 프레임 위치·상태 관측을 더한다.
/// Player 코드는 바꾸지 않는다.
/// <para>대시를 멈추는 코드 경로(<see cref="Reasons"/> 순서 = 분류 우선순위):</para>
/// <list type="number">
/// <item><c>server_cancel</c> — <c>PlayerDashController.RespondDashClientRpc</c> → <c>PlayerStateController.EndDash</c>(서버 거부·승인 후 중단·남은시간 0). 로그 <c>[Dash] 취소 원인 분류:</c>.</item>
/// <item><c>unit_block</c> — <c>PlayerDashState.OnMotorMovementResolved</c> 의 <c>PlayerUnitCollisionRules.ShouldEndDashOnUnitBlock</c> + <c>PlayerMotor.IsUnitBlockingAhead</c>
///   (몬스터·플레이어에 막히면 그 틱에 끝 — 실린 간파면 <c>TryHandOffDashInterrupt</c> 로 Skill). 로그 <c>[Dash] tick #n: 유닛에 막혀 대시 종료</c>.</item>
/// <item><c>knockback</c>·<c>restrained</c>·<c>cinematic</c>·<c>dead</c> — <c>PlayerStateController.SetState</c> 강제 전이(넉백·잡기/돌진 밀기·연출 잠금·사망). <c>[Dash] 종료:</c> 의 다음상태.</item>
/// <item><c>redash</c> — <c>PlayerStateController.BeginDash</c> 가 진행 중 대시를 새 대시로 덮음(다음상태 = Dash).</item>
/// <item><c>early_&lt;상태&gt;</c> — 그 밖의 조기 중단(<c>ChangeState</c> 외부 전이, 요청지점 = <c>LastTransitionCause</c>).</item>
/// <item><c>no_tick</c> — 진입 프레임에 끝나 <c>PlayerDashState.FixedTick</c> 이 한 번도 안 돎(<c>[Dash] 이동 없음</c>).</item>
/// <item><c>motor_idle</c> — FixedTick 은 돌았는데 모터 요청 이동이 0(<c>PlayerMotor.FixedUpdate</c> 가 Dynamic 모드·비활성이라 Tick 을 안 돎).</item>
/// <item><c>wall_sweep</c> — 시간은 다 채웠지만 <c>PlayerMotor</c> 스윕(RuntimeMotionResolver)이 요청 이동을 깎음(막힌tick &gt; 0 또는 적용 &lt; 요청의 90%).</item>
/// <item><c>airborne</c> — 대시 중 접지 상실(<c>PlayerGroundingSensor</c>, 접지상실=True).</item>
/// <item><c>post_apply_loss</c> — 스윕은 적용했는데 실제 위치가 덜 감(PhysX 접촉·NetworkTransform 경쟁, <c>[Dash] 이동 손실(적용 후)</c>).</item>
/// <item><c>full</c> — 정상 만료 + 덜 간 거리 &lt; <see cref="QABoss116Lab.ShortfallMeters"/>. <c>short_unexplained</c> — 정상 만료·신호 없음인데 덜 감.</item>
/// <item><c>no_exit_log</c> — 종료 줄을 못 봄(판 끝·Play 정지가 대시 중).</item>
/// </list>
/// 이동 잠금(<c>CanMove</c>)·애니 루트모션·상체 레이어(<c>DashInterruptUpper</c>)·슬로우(<c>Time.timeScale</c>)는 대시 이동 계산(<c>PlayerMovementSimulation.Simulate</c> 의
/// <c>DashDirection × DashSpeed × dt</c>)에 들어가지 않는다 — 정지 경로가 아니라 참고 열(<c>can_move</c>·<c>root_motion</c>·<c>upper_w</c>·<c>min_ts</c>)로만 남긴다.
/// </summary>
public static class QABoss116DashStop
{
    public const string ServerCancel = "server_cancel";
    public const string UnitBlock = "unit_block";
    public const string Knockback = "knockback";
    public const string Restrained = "restrained";
    public const string Cinematic = "cinematic";
    public const string Dead = "dead";
    public const string Redash = "redash";
    public const string EarlyPrefix = "early_";
    public const string NoTick = "no_tick";
    public const string MotorIdle = "motor_idle";
    public const string WallSweep = "wall_sweep";
    public const string Airborne = "airborne";
    public const string PostApplyLoss = "post_apply_loss";
    public const string Full = "full";
    public const string ShortUnexplained = "short_unexplained";
    public const string NoExitLog = "no_exit_log";

    /// <summary>이유 토큰 → (설명, 코드 근거). 리포트 표가 쓴다.</summary>
    public static readonly (string Reason, string Text, string Code)[] Reasons =
    {
        (ServerCancel, "서버가 대시를 취소(거부·승인 후 중단·남은시간 0)", "PlayerDashController.RespondDashClientRpc → PlayerStateController.EndDash"),
        (UnitBlock, "몬스터·플레이어에 막혀 그 틱에 종료", "PlayerDashState.OnMotorMovementResolved · PlayerUnitCollisionRules.ShouldEndDashOnUnitBlock · PlayerMotor.IsUnitBlockingAhead"),
        (Knockback, "피격 넉백 강제 전이", "Unit.Knockback → PlayerStateController.SetState"),
        (Restrained, "보스 구속(잡기·돌진 밀기)", "PlayerStateController.TryReceiveRestraint → SetState"),
        (Cinematic, "연출 잠금", "PlayerEncounterLock → SetState"),
        (Dead, "사망", "SetState(Dead)"),
        (Redash, "새 대시가 덮음", "PlayerStateController.BeginDash"),
        (EarlyPrefix + "*", "그 밖의 조기 중단(요청지점 열 참고)", "PlayerStateController.ChangeState · LastTransitionCause"),
        (NoTick, "진입 프레임에 끝(FixedTick 0회)", "PlayerDashState.Exit tickCount==0"),
        (MotorIdle, "FixedTick 은 돌았는데 모터 요청 이동 0 — 모터가 대시 이동을 안 돌림", "PlayerMotor.FixedUpdate(Dynamic 모드·비활성 → Tick 없음) · PlayerMovementSimulation.Simulate"),
        (WallSweep, "시간은 채웠지만 벽·급경사 스윕이 이동을 깎음", "PlayerMotor RuntimeMotionResolver · PlayerDashState.OnMotorMovementResolved 막힌tick"),
        (Airborne, "대시 중 접지 상실", "PlayerDashState.FixedTick lostGroundingDuringDash"),
        (PostApplyLoss, "적용 뒤 실제 위치가 덜 감", "PlayerDashState.Exit 이동 손실(적용 후)"),
        (Full, "정상 만료 — 의도 거리만큼 감", "PlayerDashState.Tick endTime"),
        (ShortUnexplained, "정상 만료·신호 없음인데 덜 감", "—"),
        (NoExitLog, "종료 줄 없음(판 끝·정지가 대시 중)", "—"),
    };

    public static string Describe(string reason)
    {
        if (string.IsNullOrEmpty(reason))
            return "";
        string key = reason.StartsWith(EarlyPrefix, StringComparison.Ordinal) ? EarlyPrefix + "*" : reason;
        foreach ((string r, string text, string code) in Reasons)
            if (r == key)
                return text + " — " + code;
        return "";
    }

    /// <summary>
    /// 신호 → 끊긴 이유(위 우선순위). <paramref name="shortfallM"/> = 의도 − 실제(대시 방향 투영), 모르면 NaN.
    /// </summary>
    public static string Classify(QABoss116DashRecord r, double shortfallM)
    {
        if (r.ServerCancel)
            return ServerCancel;
        if (r.UnitBlock)
            return UnitBlock;
        if (!r.HasExit)
            return NoExitLog;
        if (!r.Expired)
        {
            switch (r.NextState)
            {
                case "Knockback": return Knockback;
                case "Restrained": return Restrained;
                case "Cinematic": return Cinematic;
                case "Dead": return Dead;
                case "Dash": return Redash;
            }
            return EarlyPrefix + (string.IsNullOrEmpty(r.NextState) ? "na" : r.NextState);
        }
        if (r.Ticks == 0 || r.NoTickLine)
            return NoTick;
        if (r.Ticks > 0 && r.ReqM <= 0.01f)
            return MotorIdle;
        if (r.BlockedTicks > 0 || r.SweepLines > 0 || (r.ReqM > 0.01 && r.AppliedM < r.ReqM * 0.9))
            return WallSweep;
        if (r.LostGround)
            return Airborne;
        if (r.PostApplyLossLine || (r.AppliedM > 0.01 && r.LogActualM >= 0 && r.LogActualM < r.AppliedM * 0.5))
            return PostApplyLoss;
        return !double.IsNaN(shortfallM) && shortfallM >= QABoss116Lab.ShortfallMeters ? ShortUnexplained : Full;
    }

    // ── 로그 줄 해석(게임 문구 그대로 — PlayerStateController.cs · PlayerDashController.cs · PlayerSkillController.cs) ──

    public const string EnterPrefix = "[Dash] 상태 진입 —";
    public const string ExitPrefix = "[Dash] 종료:";
    public const string UnitBlockMark = "유닛에 막혀 대시 종료";
    public const string SweepMark = "스윕이 이동을 차단";
    public const string CancelPrefix = "[Dash] 취소 원인 분류:";
    public const string NoTickPrefix = "[Dash] 이동 없음";
    public const string PostApplyPrefix = "[Dash] 이동 손실(적용 후)";
    public const string TransferMark = "[DashInterrupt] 간파 → 대시 전환 완료";
    public const string AbortMark = "[DashInterrupt] 대시 중 강제 종료";
    public const string RejectTransitionPrefix = "[Dash] 상태 전이 거부:";

    /// <summary><c>[Dash] 상태 진입 — 이전상태=X 방향=(x, z) 속도=S 지속=Ds …</c></summary>
    public static bool TryParseEnter(string line, out string prevState, out float speed, out float duration, out float dirX, out float dirZ)
    {
        prevState = "";
        speed = duration = dirX = dirZ = float.NaN;
        if (line == null || !line.StartsWith(EnterPrefix, StringComparison.Ordinal))
            return false;
        prevState = Word(line, "이전상태=");
        speed = Num(Word(line, "속도="));
        duration = Num(Word(line, "지속=").TrimEnd('s'));
        string dir = Between(line, "방향=(", ")");
        int comma = dir.IndexOf(',');
        if (comma > 0)
        {
            dirX = Num(dir.Substring(0, comma).Trim());
            dirZ = Num(dir.Substring(comma + 1).Trim());
        }
        return true;
    }

    /// <summary>
    /// <c>[Dash] 종료: 정상 종료(지속시간 만료) | 다음상태=Idle 경과=0.300s/0.300s tick=15회 요청이동=3.60m 적용이동=3.60m 실제이동=3.58m 막힌tick=0 접지상실=False</c>
    /// 조기 중단이면 <c>조기 중단 — 남은시간 … 중단 원인=… 요청지점=M() @ F.cs:L | 다음상태=…</c>.
    /// </summary>
    public static bool TryParseExit(string line, QABoss116DashRecord r)
    {
        if (line == null || !line.StartsWith(ExitPrefix, StringComparison.Ordinal))
            return false;
        r.HasExit = true;
        r.Expired = line.IndexOf("정상 종료", StringComparison.Ordinal) >= 0;
        r.NextState = Word(line, "다음상태=");
        r.InterruptCause = Between(line, "중단 원인=", " 요청지점=");
        r.Caller = Between(line, "요청지점=", " | ");
        string elapsed = Word(line, "경과=");
        int slash = elapsed.IndexOf('/');
        if (slash > 0)
        {
            r.ExitElapsedS = Num(elapsed.Substring(0, slash).TrimEnd('s'));
            r.ExitDurationS = Num(elapsed.Substring(slash + 1).TrimEnd('s'));
        }
        float ticks = Num(Word(line, " tick=").Replace("회", ""));
        r.Ticks = float.IsNaN(ticks) ? -1 : (int)Math.Round(ticks);
        r.ReqM = Num0(Word(line, "요청이동=").TrimEnd('m'));
        r.AppliedM = Num0(Word(line, "적용이동=").TrimEnd('m'));
        float actual = Num(Word(line, "실제이동=").TrimEnd('m'));
        r.LogActualM = float.IsNaN(actual) ? -1f : actual;
        r.BlockedTicks = (int)Math.Round(Num0(Word(line, "막힌tick=")));
        r.LostGround = Word(line, "접지상실=").Equals("True", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    /// <summary><c>[Dash] 취소 원인 분류: 서버 거부(NotGrounded) (requestId=3)</c> → <c>서버 거부(NotGrounded)</c>.</summary>
    public static string ParseCancel(string line)
    {
        if (line == null || !line.StartsWith(CancelPrefix, StringComparison.Ordinal))
            return null;
        string rest = line.Substring(CancelPrefix.Length).Trim();
        int req = rest.LastIndexOf(" (requestId=", StringComparison.Ordinal);
        return req > 0 ? rest.Substring(0, req) : rest;
    }

    // "키=" 뒤 공백 전까지.
    private static string Word(string line, string key)
    {
        int i = line.IndexOf(key, StringComparison.Ordinal);
        if (i < 0)
            return "";
        i += key.Length;
        int end = line.IndexOf(' ', i);
        return (end < 0 ? line.Substring(i) : line.Substring(i, end - i)).Trim();
    }

    private static string Between(string line, string from, string to)
    {
        int i = line.IndexOf(from, StringComparison.Ordinal);
        if (i < 0)
            return "";
        i += from.Length;
        int end = line.IndexOf(to, i, StringComparison.Ordinal);
        return (end < 0 ? line.Substring(i) : line.Substring(i, end - i)).Trim();
    }

    private static float Num(string s) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : float.NaN;

    private static float Num0(string s)
    {
        float v = Num(s);
        return float.IsNaN(v) ? 0f : v;
    }
}

/// <summary>
/// 할 일 116-C6 — 대시 1회 기록(로그 신호 + 프레임 관측). 디렉터가 <c>[Dash] 상태 진입</c> 줄에서 열고 <c>[Dash] 종료:</c> 줄 다음 프레임에 닫는다.
/// </summary>
public sealed class QABoss116DashRecord
{
    // 진입
    public string PrevState = "";
    /// <summary>우클릭 → 대시 전환(<c>[DashInterrupt] 간파 → 대시 전환 완료</c>, 진입 줄보다 먼저 찍힌다).</summary>
    public bool Transfer;
    public float Speed = float.NaN, Duration = float.NaN, DirX = float.NaN, DirZ = float.NaN;

    // 끝
    public bool HasExit, Expired;
    public string NextState = "", InterruptCause = "", Caller = "";
    public float ExitElapsedS = float.NaN, ExitDurationS = float.NaN;
    public int Ticks = -1, BlockedTicks;
    public float ReqM, AppliedM, LogActualM = -1f;
    public bool LostGround;

    // 도중 신호
    public bool UnitBlock, ServerCancel, InterruptAborted, NoTickLine, PostApplyLossLine;
    public string CancelCause = "";
    public int SweepLines, RejectedTransitions;

    // 프레임 관측(디렉터 Update)
    public int Frames, MoveFrames, FirstMoveFrame = -1;
    public float FirstMoveS = -1f;
    public float MinTimeScale = float.NaN, MaxUpperWeight = -1f;

    /// <summary>로그 한 줄 — 도중 신호만. 진입·종료 줄은 디렉터가 직접 처리한다(새 기록 열기·닫기).</summary>
    public void Feed(string line)
    {
        if (string.IsNullOrEmpty(line))
            return;
        if (line.IndexOf(QABoss116DashStop.UnitBlockMark, StringComparison.Ordinal) >= 0 && line.StartsWith("[Dash] tick", StringComparison.Ordinal))
            UnitBlock = true;
        else if (line.IndexOf(QABoss116DashStop.SweepMark, StringComparison.Ordinal) >= 0 && line.StartsWith("[Dash] tick", StringComparison.Ordinal))
            SweepLines++;
        else if (QABoss116DashStop.ParseCancel(line) is string cancel)
        {
            ServerCancel = true;
            CancelCause = cancel;
        }
        else if (line.StartsWith(QABoss116DashStop.NoTickPrefix, StringComparison.Ordinal))
            NoTickLine = true;
        else if (line.StartsWith(QABoss116DashStop.PostApplyPrefix, StringComparison.Ordinal))
            PostApplyLossLine = true;
        else if (line.StartsWith(QABoss116DashStop.AbortMark, StringComparison.Ordinal))
            InterruptAborted = true;
        else if (line.StartsWith(QABoss116DashStop.RejectTransitionPrefix, StringComparison.Ordinal))
            RejectedTransitions++;
    }

    /// <summary>프레임 1개 — 이번 프레임 대시 방향 진척(m)·대시 경과(초)·시간 배율·상체 레이어 가중치(모르면 음수).</summary>
    public void Frame(float progressM, float elapsedS, float timeScale, float upperWeight)
    {
        if (progressM > 0.01f)
        {
            if (FirstMoveFrame < 0)
            {
                FirstMoveFrame = Frames;
                FirstMoveS = elapsedS;
            }
            MoveFrames++;
        }
        Frames++;
        if (float.IsNaN(MinTimeScale) || timeScale < MinTimeScale)
            MinTimeScale = timeScale;
        if (upperWeight > MaxUpperWeight)
            MaxUpperWeight = upperWeight;
    }

    /// <summary>판 파일 <c>labdash</c> 줄에 붙일 키·값(key, value 순서).</summary>
    public string[] ToKv(string reason)
    {
        return new[]
        {
            "prevState", Or(PrevState),
            "transfer", Transfer ? "true" : "false",
            "expired", HasExit ? (Expired ? "true" : "false") : "na",
            "nextState", Or(NextState),
            "stopReason", Or(reason),
            "stopDetail", Or(Detail()),
            "caller", Or(Caller),
            "ticks", Ticks >= 0 ? Ticks.ToString(CultureInfo.InvariantCulture) : "na",
            "reqM", HasExit ? ReqM.ToString("0.00", CultureInfo.InvariantCulture) : "na",
            "appliedM", HasExit ? AppliedM.ToString("0.00", CultureInfo.InvariantCulture) : "na",
            "logActualM", LogActualM >= 0f ? LogActualM.ToString("0.00", CultureInfo.InvariantCulture) : "na",
            "blockedTicks", BlockedTicks.ToString(CultureInfo.InvariantCulture),
            "lostGround", LostGround ? "true" : "false",
            "exitElapsedS", F3(ExitElapsedS),
            "frames", Frames.ToString(CultureInfo.InvariantCulture),
            "moveFrames", MoveFrames.ToString(CultureInfo.InvariantCulture),
            "firstMoveFrame", FirstMoveFrame >= 0 ? FirstMoveFrame.ToString(CultureInfo.InvariantCulture) : "na",
            "firstMoveS", FirstMoveS >= 0f ? F3(FirstMoveS) : "na",
            "minTs", F3(MinTimeScale),
            "upperW", MaxUpperWeight >= 0f ? MaxUpperWeight.ToString("0.00", CultureInfo.InvariantCulture) : "na",
            "interruptAborted", InterruptAborted ? "true" : "false",
        };
    }

    /// <summary>이유를 받친 원문 — 서버 취소 사유·중단 원인·막힌 틱 수 등.</summary>
    public string Detail()
    {
        var sb = new StringBuilder();
        if (ServerCancel)
            sb.Append(CancelCause);
        else if (HasExit && !Expired && InterruptCause.Length > 0)
            sb.Append(InterruptCause);
        if (SweepLines > 0 || BlockedTicks > 0)
            sb.Append(sb.Length > 0 ? ";" : "").Append("blocked_ticks_").Append(Math.Max(SweepLines, BlockedTicks));
        if (RejectedTransitions > 0)
            sb.Append(sb.Length > 0 ? ";" : "").Append("transition_rejected_").Append(RejectedTransitions);
        return sb.ToString();
    }

    private static string Or(string v) => string.IsNullOrEmpty(v) ? "na" : v;
    private static string F3(float v) => float.IsNaN(v) ? "na" : v.ToString("0.000", CultureInfo.InvariantCulture);
}
#endif
