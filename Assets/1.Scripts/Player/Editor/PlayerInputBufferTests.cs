using NUnit.Framework;

/// <summary>
/// 선입력 버퍼(할 일 44·77) — 기록·종류별 보관과 개별 만료·우선순위 꺼내기·소비(전체 비움)·끔,
/// 거부 사유별 보관/버림(버림 = 다음 후보, 보관 = 중단)·입력 1회 = 피드백 1회.
/// </summary>
public sealed class PlayerInputBufferTests
{
    private const float Hold = 0.2f;

    [Test]
    public void Record_ThenPeek_ReturnsInput()
    {
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.Attack, 10f);

        Assert.That(buffer.Peek(10f), Is.EqualTo(PlayerBufferedInput.Attack));
        Assert.That(buffer.Has(PlayerBufferedInput.Attack, 10f), Is.True);
        Assert.That(buffer.Has(PlayerBufferedInput.Dash, 10f), Is.False);
    }

    [Test]
    public void Peek_WithinHold_Keeps_AfterHold_Expires()
    {
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.Dash, 0f);

        Assert.That(buffer.Peek(0.2f), Is.EqualTo(PlayerBufferedInput.Dash), "유지 시간과 같으면 아직 살아 있다");

        buffer.Record(PlayerBufferedInput.Dash, 10f);
        Assert.That(buffer.Peek(10.19f), Is.EqualTo(PlayerBufferedInput.Dash));
        Assert.That(buffer.Peek(10.21f), Is.EqualTo(PlayerBufferedInput.None));
        Assert.That(buffer.Peek(10.1f), Is.EqualTo(PlayerBufferedInput.None), "한 번 만료되면 되살아나지 않는다");
    }

    [Test]
    public void Record_SameKind_RefreshesTimeOnly()
    {
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.Attack, 10f);
        buffer.Record(PlayerBufferedInput.Attack, 10.1f);

        Assert.That(buffer.Count(10.1f), Is.EqualTo(1), "같은 종류는 하나");
        Assert.That(buffer.Peek(10.29f), Is.EqualTo(PlayerBufferedInput.Attack), "유지 시간은 다시 누른 시각 기준");
        Assert.That(buffer.Peek(10.31f), Is.EqualTo(PlayerBufferedInput.None));
    }

    [Test]
    public void Remove_RemovesOnlyMatchingKind()
    {
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.SkillSub, 10f);

        buffer.Remove(PlayerBufferedInput.Attack);
        Assert.That(buffer.Peek(10f), Is.EqualTo(PlayerBufferedInput.SkillSub), "다른 종류 지우기는 무시");

        buffer.Remove(PlayerBufferedInput.SkillSub);
        Assert.That(buffer.Peek(10f), Is.EqualTo(PlayerBufferedInput.None));
    }

    [Test]
    public void Clear_RemovesEverything()
    {
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.SkillUltimate, 10f);
        buffer.Clear();

        Assert.That(buffer.Peek(10f), Is.EqualTo(PlayerBufferedInput.None));
    }

    [Test]
    public void ZeroOrNegativeHold_DoesNotRecord()
    {
        var zero = new PlayerInputBuffer(0f);
        zero.Record(PlayerBufferedInput.Attack, 10f);
        Assert.That(zero.Peek(10f), Is.EqualTo(PlayerBufferedInput.None));

        var negative = new PlayerInputBuffer(-1f);
        negative.Record(PlayerBufferedInput.Dash, 10f);
        Assert.That(negative.Peek(10f), Is.EqualTo(PlayerBufferedInput.None));
    }

    [Test]
    public void Resolve_Started_ClearsEverything()
    {
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.Attack, 10f);
        buffer.Resolve(PlayerBufferedInput.Dash, PlayerActionAttempt.Started);

        Assert.That(buffer.Peek(10f), Is.EqualTo(PlayerBufferedInput.None), "시작하면 다른 종류도 비운다");
    }

    [TestCase(PlayerBufferedInput.SkillMain, PlayerActionAttempt.OutOfResource)]
    [TestCase(PlayerBufferedInput.Dash, PlayerActionAttempt.OutOfResource)]
    [TestCase(PlayerBufferedInput.Attack, PlayerActionAttempt.OutOfResource)]
    [TestCase(PlayerBufferedInput.SkillSub, PlayerActionAttempt.BlockedByEffect)]
    [TestCase(PlayerBufferedInput.Dash, PlayerActionAttempt.BlockedByEffect)]
    [TestCase(PlayerBufferedInput.SkillUltimate, PlayerActionAttempt.Rejected)]
    [TestCase(PlayerBufferedInput.SkillInterrupt, PlayerActionAttempt.Rejected)]
    public void Resolve_Rejection_DropsThatInput(PlayerBufferedInput input, PlayerActionAttempt attempt)
    {
        // 자원 부족(쿨타임·충전 없음·과열)·차단 효과(침묵·속박·약화)·그 밖(CanUse·빈 슬롯·비접지) — 재시도 없이 바로 버린다.
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(input, 10f);
        buffer.Resolve(input, attempt);

        Assert.That(buffer.Peek(10.01f), Is.EqualTo(PlayerBufferedInput.None));
    }

    [TestCase(PlayerActionAttempt.BlockedByEffect)]
    [TestCase(PlayerActionAttempt.Rejected)]
    public void Resolve_Rejection_KeepsOtherKind(PlayerActionAttempt attempt)
    {
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.Attack, 10f);
        buffer.Resolve(PlayerBufferedInput.SkillMain, attempt);

        Assert.That(buffer.Peek(10f), Is.EqualTo(PlayerBufferedInput.Attack), "거부된 종류만 버린다");
    }

    [TestCase(StatusEffectType.Silenced, PlayerActionAttempt.BlockedByEffect)]
    [TestCase(StatusEffectType.Rooted, PlayerActionAttempt.BlockedByEffect)]
    [TestCase(StatusEffectType.Debilitated, PlayerActionAttempt.BlockedByEffect)]
    [TestCase(StatusEffectType.Silenced | StatusEffectType.Slowed, PlayerActionAttempt.BlockedByEffect)]
    [TestCase(StatusEffectType.Stunned, PlayerActionAttempt.Blocked)]
    [TestCase(StatusEffectType.Airborne, PlayerActionAttempt.Blocked)]
    [TestCase(StatusEffectType.Stunned | StatusEffectType.Silenced, PlayerActionAttempt.Blocked)]
    [TestCase(StatusEffectType.Airborne | StatusEffectType.Rooted, PlayerActionAttempt.Blocked)]
    public void ClassifyEffectBlock_IncapacitatingKeeps_SealDrops(StatusEffectType active, PlayerActionAttempt expected)
    {
        // 기절·에어본(행동 불능 CC) = 보관, 그 행동만 막는 봉인 = 버림 + 차단 피드백. 겹치면 CC 가 이긴다.
        Assert.That(PlayerActionRejectPolicy.ClassifyEffectBlock(active), Is.EqualTo(expected));
    }

    [Test]
    public void Resolve_OutOfResource_KeepsOtherKind()
    {
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.Attack, 10f);
        buffer.Resolve(PlayerBufferedInput.Dash, PlayerActionAttempt.OutOfResource);

        Assert.That(buffer.Peek(10f), Is.EqualTo(PlayerBufferedInput.Attack), "거부된 종류만 버린다");
    }

    [Test]
    public void Resolve_Blocked_KeepsUntilExpiry()
    {
        // 행동·요청 중(바쁨)·CC — 유지 시간 동안 들고 있다가 다시 시도한다.
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.SkillSub, 10f);
        buffer.Resolve(PlayerBufferedInput.SkillSub, PlayerActionAttempt.Blocked);

        Assert.That(buffer.Peek(10.19f), Is.EqualTo(PlayerBufferedInput.SkillSub));
        Assert.That(buffer.Peek(10.21f), Is.EqualTo(PlayerBufferedInput.None));
    }

    [Test]
    public void CooldownReject_OnePressRaisesFeedbackOnce()
    {
        // FSM TryStartSkill 과 같은 순서: 눌린 프레임 또는 버퍼 → 쿨타임이면 버리고 피드백 1회.
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.SkillMain, 10f);

        SimResult result = SimulateIdleFrames(buffer, new SimTimeline { cooldownUntil = 99f });

        Assert.That(result.cooldownFeedback, Is.EqualTo(1), "입력 1회 = 피드백 1회 — 쿨타임 동안 프레임마다 다시 나지 않는다");
        Assert.That(result.effectFeedback, Is.EqualTo(0));
    }

    [Test]
    public void CooldownReject_PressDuringBusy_RaisesOnceWhenFree()
    {
        // 행동 중에 누른 입력은 바쁨이라 들고 있다가, 풀린 첫 틱에 쿨타임이면 그때 한 번만 거부된다.
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.SkillMain, 10f);

        SimResult result = SimulateIdleFrames(buffer, new SimTimeline { busyUntil = 10.1f, cooldownUntil = 99f });

        Assert.That(result.cooldownFeedback, Is.EqualTo(1));
    }

    [Test]
    public void CooldownReject_NotRaisedWhileBusyOnly()
    {
        // 바쁨이 유지 시간보다 길면 입력은 만료되고 거부 피드백은 나지 않는다(쿨타임 거부가 아니다).
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.SkillMain, 10f);

        SimResult result = SimulateIdleFrames(buffer, new SimTimeline { busyUntil = 11f, cooldownUntil = 99f });

        Assert.That(result.cooldownFeedback, Is.EqualTo(0));
    }

    [Test]
    public void EffectBlock_Seal_DropsAndRaisesOnce()
    {
        // 침묵 중 스킬 입력 — 바로 버리고 차단 피드백 1회. 침묵이 금방 풀려도 뒤늦게 실행되지 않는다.
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.SkillMain, 10f);

        SimResult result = SimulateIdleFrames(buffer, new SimTimeline
        {
            effectUntil = 10.05f, effects = StatusEffectType.Silenced
        });

        Assert.That(result.effectFeedback, Is.EqualTo(1), "입력 1회 = 차단 피드백 1회");
        Assert.That(result.cooldownFeedback, Is.EqualTo(0));
        Assert.That(result.started, Is.False, "봉인 중 입력은 풀린 뒤에도 실행하지 않는다");
    }

    [Test]
    public void EffectBlock_SealAndCooldown_RaisesSealOnly()
    {
        // 봉인 + 쿨타임 — 상태이상을 먼저 보므로 차단 피드백 하나만 난다(한 입력에 두 이벤트 없음).
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.SkillSub, 10f);

        SimResult result = SimulateIdleFrames(buffer, new SimTimeline
        {
            input = PlayerBufferedInput.SkillSub, effectUntil = 99f, effects = StatusEffectType.Silenced, cooldownUntil = 99f
        });

        Assert.That(result.effectFeedback + result.cooldownFeedback, Is.EqualTo(1));
        Assert.That(result.effectFeedback, Is.EqualTo(1));
    }

    [Test]
    public void EffectBlock_Incapacitated_KeepsAndRunsWhenFreed()
    {
        // 기절 중 입력 — 보관했다가 유지 시간 안에 풀리면 그 첫 틱에 실행. 피드백 없음.
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.Dash, 10f);

        SimResult result = SimulateIdleFrames(buffer, new SimTimeline
        {
            input = PlayerBufferedInput.Dash, effectUntil = 10.1f, effects = StatusEffectType.Stunned
        });

        Assert.That(result.started, Is.True);
        Assert.That(result.effectFeedback, Is.EqualTo(0));
    }

    [Test]
    public void EffectBlock_IncapacitatedLongerThanHold_ExpiresSilently()
    {
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.Attack, 10f);

        SimResult result = SimulateIdleFrames(buffer, new SimTimeline
        {
            input = PlayerBufferedInput.Attack, effectUntil = 11f, effects = StatusEffectType.Airborne
        });

        Assert.That(result.started, Is.False, "유지 시간이 지나면 버려진다");
        Assert.That(result.effectFeedback, Is.EqualTo(0));
    }

    [Test]
    public void EffectBlock_StunThenSilenceRemains_RaisesOnceAfterStun()
    {
        // 기절 + 침묵 — 기절 동안은 보관, 기절이 풀린 첫 틱에 침묵이 남아 있으면 그때 한 번 버리고 피드백.
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.SkillMain, 10f);

        SimResult result = SimulateIdleFrames(buffer, new SimTimeline
        {
            effectUntil = 10.1f, effects = StatusEffectType.Stunned | StatusEffectType.Silenced,
            secondEffectUntil = 99f, secondEffects = StatusEffectType.Silenced
        });

        Assert.That(result.effectFeedback, Is.EqualTo(1));
        Assert.That(result.started, Is.False);
    }

    [Test]
    public void OtherReject_DropsWithoutFeedback()
    {
        // 사용 조건(CanUse)·빈 슬롯·비접지 — 바로 버리고 피드백 없음. 조건이 곧 맞아도 뒤늦게 실행되지 않는다.
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.SkillUltimate, 10f);

        SimResult result = SimulateIdleFrames(buffer, new SimTimeline
        {
            input = PlayerBufferedInput.SkillUltimate, rejectedUntil = 10.05f
        });

        Assert.That(result.started, Is.False);
        Assert.That(result.effectFeedback + result.cooldownFeedback, Is.EqualTo(0));
    }

    private struct SimTimeline
    {
        public PlayerBufferedInput input;     // 기본 SkillMain
        public float busyUntil;               // 실행·요청 중
        public float effectUntil;             // 차단 상태이상(effects)이 걸려 있는 끝 시각
        public StatusEffectType effects;
        public float secondEffectUntil;       // effectUntil 이후 남는 상태이상
        public StatusEffectType secondEffects;
        public float cooldownUntil;
        public float rejectedUntil;           // CanUse 불충족 등 그 밖의 거부
    }

    private struct SimResult
    {
        public int cooldownFeedback;
        public int effectFeedback;
        public bool started;
    }

    // 60fps 로 10초~10.5초 프레임을 돌리며 PlayerStateBase.TryStartSkill 의 판정 순서를 흉내 낸다(10초에 1회 누름):
    // 바쁨(보관) → 상태이상(CC 보관 / 봉인 버림 + 차단 피드백) → 쿨타임(버림 + 쿨타임 피드백) → 그 밖의 거부(버림) → 시작.
    private static SimResult SimulateIdleFrames(PlayerInputBuffer buffer, SimTimeline t)
    {
        const float Frame = 1f / 60f;
        const float PressedAt = 10f;
        PlayerBufferedInput input = t.input == PlayerBufferedInput.None ? PlayerBufferedInput.SkillMain : t.input;
        var result = new SimResult();

        for (float now = PressedAt; now <= 10.5f; now += Frame)
        {
            bool pressedThisFrame = now == PressedAt;
            if (!pressedThisFrame && !buffer.Has(input, now))
                continue;

            if (now < t.busyUntil)
            {
                buffer.Resolve(input, PlayerActionAttempt.Blocked);
                continue;
            }

            StatusEffectType active = now < t.effectUntil ? t.effects
                : now < t.secondEffectUntil ? t.secondEffects
                : StatusEffectType.None;
            if (active != StatusEffectType.None)
            {
                PlayerActionAttempt attempt = PlayerActionRejectPolicy.ClassifyEffectBlock(active);
                buffer.Resolve(input, attempt);
                if (attempt == PlayerActionAttempt.BlockedByEffect)
                    result.effectFeedback++;
                continue;
            }

            if (now < t.cooldownUntil)
            {
                buffer.Resolve(input, PlayerActionAttempt.OutOfResource);
                result.cooldownFeedback++;
                continue;
            }

            if (now < t.rejectedUntil)
            {
                buffer.Resolve(input, PlayerActionAttempt.Rejected);
                continue;
            }

            buffer.Resolve(input, PlayerActionAttempt.Started);
            result.started = true;
        }

        return result;
    }

    // ── 우선순위(할 일 77) — PlayerInputReader 기본값: 대시5 > 우클릭4 > R3 > Q·E 2 > 평타1 ──

    private static PlayerInputBuffer CreatePrioritized(float hold = Hold)
    {
        var buffer = new PlayerInputBuffer(hold);
        buffer.SetPriority(PlayerBufferedInput.Dash, 5);
        buffer.SetPriority(PlayerBufferedInput.SkillInterrupt, 4);
        buffer.SetPriority(PlayerBufferedInput.SkillUltimate, 3);
        buffer.SetPriority(PlayerBufferedInput.SkillMain, 2);
        buffer.SetPriority(PlayerBufferedInput.SkillSub, 2);
        buffer.SetPriority(PlayerBufferedInput.Attack, 1);
        return buffer;
    }

    [Test]
    public void Multi_DifferentKinds_AllKept()
    {
        // 할 일 77 — 다른 종류는 덮지 않고 전부 보관한다(낮은 입력도 기록된다).
        var buffer = CreatePrioritized();
        Assert.That(buffer.Record(PlayerBufferedInput.Dash, 10f), Is.True);
        Assert.That(buffer.Record(PlayerBufferedInput.Attack, 10.05f), Is.True);
        Assert.That(buffer.Record(PlayerBufferedInput.SkillMain, 10.1f), Is.True);

        Assert.That(buffer.Count(10.1f), Is.EqualTo(3));
        Assert.That(buffer.Contains(PlayerBufferedInput.Attack, 10.1f), Is.True);
        Assert.That(buffer.Contains(PlayerBufferedInput.SkillMain, 10.1f), Is.True);
        Assert.That(buffer.Has(PlayerBufferedInput.Attack, 10.1f), Is.False, "살아 있어도 더 높은 입력에 밀려 있으면 꺼낼 입력이 아니다");
    }

    [Test]
    public void Multi_ExpireSeparately()
    {
        var buffer = CreatePrioritized();
        buffer.Record(PlayerBufferedInput.Dash, 10f);
        buffer.Record(PlayerBufferedInput.Attack, 10.15f);

        Assert.That(buffer.Peek(10.2f), Is.EqualTo(PlayerBufferedInput.Dash));
        Assert.That(buffer.Peek(10.21f), Is.EqualTo(PlayerBufferedInput.Attack), "대시만 만료 — 평타는 자기 시각 기준으로 살아 있다");
        Assert.That(buffer.Peek(10.36f), Is.EqualTo(PlayerBufferedInput.None));
    }

    [TestCase(PlayerBufferedInput.Dash, PlayerBufferedInput.SkillInterrupt)]
    [TestCase(PlayerBufferedInput.SkillInterrupt, PlayerBufferedInput.SkillUltimate)]
    [TestCase(PlayerBufferedInput.SkillUltimate, PlayerBufferedInput.SkillMain)]
    [TestCase(PlayerBufferedInput.SkillSub, PlayerBufferedInput.Attack)]
    [TestCase(PlayerBufferedInput.Dash, PlayerBufferedInput.Attack)]
    public void Peek_HighestPriority_RegardlessOfOrder(PlayerBufferedInput higher, PlayerBufferedInput lower)
    {
        var higherFirst = CreatePrioritized();
        higherFirst.Record(higher, 10f);
        higherFirst.Record(lower, 10.1f);

        var lowerFirst = CreatePrioritized();
        lowerFirst.Record(lower, 10f);
        lowerFirst.Record(higher, 10.1f);

        Assert.That(higherFirst.Peek(10.1f), Is.EqualTo(higher), "나중에 누른 낮은 입력보다 높은 입력");
        Assert.That(lowerFirst.Peek(10.1f), Is.EqualTo(higher));
        Assert.That(higherFirst.Has(higher, 10.1f), Is.True);
        Assert.That(higherFirst.Has(lower, 10.1f), Is.False);
    }

    [TestCase(PlayerBufferedInput.SkillMain, PlayerBufferedInput.SkillSub)]
    [TestCase(PlayerBufferedInput.SkillSub, PlayerBufferedInput.SkillMain)]
    public void Peek_EqualPriority_LaterPressWins(PlayerBufferedInput first, PlayerBufferedInput later)
    {
        var buffer = CreatePrioritized();
        buffer.Record(first, 10f);
        buffer.Record(later, 10.1f);

        Assert.That(buffer.Peek(10.1f), Is.EqualTo(later));

        // 같은 프레임(같은 시각)이면 기록 순서로 나중 것.
        var sameFrame = CreatePrioritized();
        sameFrame.Record(first, 10f);
        sameFrame.Record(later, 10f);
        Assert.That(sameFrame.Peek(10f), Is.EqualTo(later));
    }

    [Test]
    public void Peek_EqualPriority_RepressMovesToFront()
    {
        // 같은 종류를 다시 누르면 시각이 갱신돼 동순위 중 '나중 입력'이 된다.
        var buffer = CreatePrioritized();
        buffer.Record(PlayerBufferedInput.SkillMain, 10f);
        buffer.Record(PlayerBufferedInput.SkillSub, 10.05f);
        buffer.Record(PlayerBufferedInput.SkillMain, 10.1f);

        Assert.That(buffer.Peek(10.1f), Is.EqualTo(PlayerBufferedInput.SkillMain));
    }

    [Test]
    public void Peek_DefaultAllEqual_LastInputWins()
    {
        // 우선순위를 설정하지 않은 버퍼 = 전부 0 — 마지막 입력이 먼저다(앞 입력도 보관은 된다).
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.Dash, 10f);
        buffer.Record(PlayerBufferedInput.Attack, 10.1f);

        Assert.That(buffer.Peek(10.1f), Is.EqualTo(PlayerBufferedInput.Attack));
        Assert.That(buffer.Contains(PlayerBufferedInput.Dash, 10.1f), Is.True);
    }

    [TestCase(PlayerActionAttempt.OutOfResource)]
    [TestCase(PlayerActionAttempt.BlockedByEffect)]
    [TestCase(PlayerActionAttempt.Rejected)]
    public void Resolve_DropReason_NextCandidateSurfaces(PlayerActionAttempt attempt)
    {
        // 버림 사유 — 그 입력만 지우고 남은 것 중 다음 순위가 꺼낼 입력으로 올라온다.
        var buffer = CreatePrioritized();
        buffer.Record(PlayerBufferedInput.Attack, 10f);
        buffer.Record(PlayerBufferedInput.SkillMain, 10.02f);
        buffer.Record(PlayerBufferedInput.SkillUltimate, 10.04f);

        buffer.Resolve(PlayerBufferedInput.SkillUltimate, attempt);
        Assert.That(buffer.Peek(10.05f), Is.EqualTo(PlayerBufferedInput.SkillMain));

        buffer.Resolve(PlayerBufferedInput.SkillMain, attempt);
        Assert.That(buffer.Peek(10.05f), Is.EqualTo(PlayerBufferedInput.Attack));
        Assert.That(PlayerInputBuffer.DropsInput(attempt), Is.True);
    }

    [Test]
    public void Resolve_Blocked_KeepsAll_TopUnchanged()
    {
        // 보관 사유(바쁨·CC) — 아무것도 지우지 않는다. 꺼낼 입력도 그대로라 낮은 후보가 앞지르지 않는다.
        var buffer = CreatePrioritized();
        buffer.Record(PlayerBufferedInput.Attack, 10f);
        buffer.Record(PlayerBufferedInput.Dash, 10.02f);

        buffer.Resolve(PlayerBufferedInput.Dash, PlayerActionAttempt.Blocked);

        Assert.That(buffer.Peek(10.05f), Is.EqualTo(PlayerBufferedInput.Dash));
        Assert.That(buffer.Count(10.05f), Is.EqualTo(2));
        Assert.That(PlayerInputBuffer.DropsInput(PlayerActionAttempt.Blocked), Is.False);
        Assert.That(PlayerInputBuffer.DropsInput(PlayerActionAttempt.Started), Is.False);
    }

    [Test]
    public void Consume_ClearsEverything()
    {
        // 행동 하나가 실제로 실행되면 버퍼 전체를 비운다 — 오래된 후보가 연달아 실행되지 않게.
        var buffer = CreatePrioritized();
        buffer.Record(PlayerBufferedInput.SkillMain, 10f);
        buffer.Record(PlayerBufferedInput.Attack, 10.05f);
        buffer.Record(PlayerBufferedInput.Dash, 10.1f);

        buffer.Consume(PlayerBufferedInput.Attack);

        Assert.That(buffer.Count(10.1f), Is.EqualTo(0));
        Assert.That(buffer.Peek(10.1f), Is.EqualTo(PlayerBufferedInput.None));
    }

    [Test]
    public void Resolve_Started_ClearsAllKinds()
    {
        var buffer = CreatePrioritized();
        buffer.Record(PlayerBufferedInput.SkillSub, 10f);
        buffer.Record(PlayerBufferedInput.Dash, 10.05f);

        buffer.Resolve(PlayerBufferedInput.Dash, PlayerActionAttempt.Started);

        Assert.That(buffer.Count(10.05f), Is.EqualTo(0), "대시가 실행되면 함께 들고 있던 E 도 버린다");
    }

    [Test]
    public void HoldZero_RecordsNothing_AndDropsHeldWhenSwitchedOff()
    {
        var off = CreatePrioritized(0f);
        Assert.That(off.Record(PlayerBufferedInput.Dash, 10f), Is.False);
        Assert.That(off.Record(PlayerBufferedInput.Attack, 10f), Is.False);
        Assert.That(off.Peek(10f), Is.EqualTo(PlayerBufferedInput.None));
        Assert.That(off.Count(10f), Is.EqualTo(0));

        // 들고 있던 중에 유지 시간이 0 이 되면(데이터 테이블·인스펙터) 전부 없는 것으로 본다.
        var switched = CreatePrioritized();
        switched.Record(PlayerBufferedInput.SkillMain, 10f);
        switched.Record(PlayerBufferedInput.Attack, 10f);
        switched.HoldSeconds = 0f;
        Assert.That(switched.Peek(10f), Is.EqualTo(PlayerBufferedInput.None));
        Assert.That(switched.Count(10f), Is.EqualTo(0));
    }

    [Test]
    public void SortByPriority_DefaultOrder_StableForTies()
    {
        var buffer = CreatePrioritized();
        PlayerBufferedInput[] kinds =
        {
            PlayerBufferedInput.Attack, PlayerBufferedInput.SkillMain, PlayerBufferedInput.SkillSub,
            PlayerBufferedInput.SkillInterrupt, PlayerBufferedInput.SkillUltimate, PlayerBufferedInput.Dash
        };

        buffer.SortByPriority(kinds);

        Assert.That(kinds, Is.EqualTo(new[]
        {
            PlayerBufferedInput.Dash, PlayerBufferedInput.SkillInterrupt, PlayerBufferedInput.SkillUltimate,
            PlayerBufferedInput.SkillMain, PlayerBufferedInput.SkillSub, PlayerBufferedInput.Attack
        }), "대시 > 우클릭 > R > Q = E(원래 순서) > 평타");
    }

    // ── Idle/Move 후보 순회(PlayerStateBase.TryConsumeActionInput 과 같은 규칙) ──
    // 우선순위 높은 순으로 '꺼낼 입력'인 후보만 시도 → 시작 = 전부 비움, 버림 = 그 입력만 지우고 같은 프레임에 다음 후보, 보관 = 중단.

    private static PlayerBufferedInput Dispatch(
        PlayerInputBuffer buffer, float now, System.Func<PlayerBufferedInput, PlayerActionAttempt> tryStart,
        System.Collections.Generic.List<PlayerBufferedInput> attempted)
    {
        PlayerBufferedInput[] kinds =
        {
            PlayerBufferedInput.Dash, PlayerBufferedInput.SkillInterrupt, PlayerBufferedInput.SkillUltimate,
            PlayerBufferedInput.SkillMain, PlayerBufferedInput.SkillSub, PlayerBufferedInput.Attack
        };
        buffer.SortByPriority(kinds);
        foreach (PlayerBufferedInput kind in kinds)
        {
            if (!buffer.Has(kind, now))
                continue;

            attempted.Add(kind);
            PlayerActionAttempt attempt = tryStart(kind);
            buffer.Resolve(kind, attempt);
            if (attempt == PlayerActionAttempt.Started)
                return kind;
            if (attempt == PlayerActionAttempt.Blocked)
                return PlayerBufferedInput.None;
        }

        return PlayerBufferedInput.None;
    }

    [Test]
    public void Dispatch_DropRejection_TriesNextCandidateSameFrame()
    {
        // R 쿨타임(버림) → 같은 프레임에 Q → Q 도 봉인(버림) → 평타 시작.
        var buffer = CreatePrioritized();
        buffer.Record(PlayerBufferedInput.Attack, 10f);
        buffer.Record(PlayerBufferedInput.SkillMain, 10.02f);
        buffer.Record(PlayerBufferedInput.SkillUltimate, 10.04f);
        var attempted = new System.Collections.Generic.List<PlayerBufferedInput>();

        PlayerBufferedInput started = Dispatch(buffer, 10.05f, kind => kind switch
        {
            PlayerBufferedInput.SkillUltimate => PlayerActionAttempt.OutOfResource,
            PlayerBufferedInput.SkillMain => PlayerActionAttempt.BlockedByEffect,
            _ => PlayerActionAttempt.Started
        }, attempted);

        Assert.That(started, Is.EqualTo(PlayerBufferedInput.Attack));
        Assert.That(attempted, Is.EqualTo(new[]
        {
            PlayerBufferedInput.SkillUltimate, PlayerBufferedInput.SkillMain, PlayerBufferedInput.Attack
        }));
        Assert.That(buffer.Count(10.05f), Is.EqualTo(0), "시작하면 전부 비운다");
    }

    [Test]
    public void Dispatch_KeepRejection_StopsThisFrame_KeepsAll()
    {
        // 기절(보관) 중 대시 — 아래 Q·평타로 넘어가지 않고 이번 프레임은 멈춘다. 전부 남는다.
        var buffer = CreatePrioritized();
        buffer.Record(PlayerBufferedInput.Attack, 10f);
        buffer.Record(PlayerBufferedInput.SkillMain, 10.02f);
        buffer.Record(PlayerBufferedInput.Dash, 10.04f);
        var attempted = new System.Collections.Generic.List<PlayerBufferedInput>();

        PlayerBufferedInput started = Dispatch(buffer, 10.05f,
            kind => kind == PlayerBufferedInput.Dash ? PlayerActionAttempt.Blocked : PlayerActionAttempt.Started, attempted);

        Assert.That(started, Is.EqualTo(PlayerBufferedInput.None));
        Assert.That(attempted, Is.EqualTo(new[] { PlayerBufferedInput.Dash }));
        Assert.That(buffer.Count(10.05f), Is.EqualTo(3));
        Assert.That(buffer.Peek(10.05f), Is.EqualTo(PlayerBufferedInput.Dash));
    }

    [Test]
    public void Dispatch_KeptHigherExpires_LowerRunsAfterwards()
    {
        // 기절로 보관한 대시가 먼저 만료되면, 아직 살아 있는 Q 가 그다음 프레임에 실행된다.
        var buffer = CreatePrioritized();
        buffer.Record(PlayerBufferedInput.Dash, 10f);
        buffer.Record(PlayerBufferedInput.SkillMain, 10.1f);
        var attempted = new System.Collections.Generic.List<PlayerBufferedInput>();

        Assert.That(Dispatch(buffer, 10.15f, _ => PlayerActionAttempt.Blocked, attempted), Is.EqualTo(PlayerBufferedInput.None));
        Assert.That(Dispatch(buffer, 10.25f, _ => PlayerActionAttempt.Started, attempted), Is.EqualTo(PlayerBufferedInput.SkillMain));
    }

    [Test]
    public void FromSkillSlot_MapsEverySlot()
    {
        Assert.That(PlayerInputBuffer.FromSkillSlot(PlayerSkillSlot.Main), Is.EqualTo(PlayerBufferedInput.SkillMain));
        Assert.That(PlayerInputBuffer.FromSkillSlot(PlayerSkillSlot.Sub), Is.EqualTo(PlayerBufferedInput.SkillSub));
        Assert.That(PlayerInputBuffer.FromSkillSlot(PlayerSkillSlot.Interrupt), Is.EqualTo(PlayerBufferedInput.SkillInterrupt));
        Assert.That(PlayerInputBuffer.FromSkillSlot(PlayerSkillSlot.Ultimate), Is.EqualTo(PlayerBufferedInput.SkillUltimate));
    }
}
