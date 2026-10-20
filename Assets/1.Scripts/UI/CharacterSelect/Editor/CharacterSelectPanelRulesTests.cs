using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 로비 캐릭터 선택 패널 판단(할 일 48.2) — 첫 포커스·순환 이동·카드 자리·확정(잠금·불가·같은 캐릭터)·ESC·배경 흐름.
/// 카드 이동 연출(할 일 60) — 이징·단계 전이·이동 중 재목표·테두리 알파·확대·바깥 자리 출입.
/// </summary>
public sealed class CharacterSelectPanelRulesTests
{
    static readonly bool[] AllAvailable = { true, true, true };

    static CharacterSelectPanelRules Opened(int current, bool locked = false, bool[] available = null)
    {
        var rules = new CharacterSelectPanelRules();
        rules.Open(current, locked, available ?? AllAvailable);
        return rules;
    }

    [Test]
    public void Open_FocusesCurrent_OrZeroWhenOutOfRange()
    {
        Assert.AreEqual(2, Opened(2).Focus);
        Assert.AreEqual(0, Opened(-1).Focus, "아직 고른 캐릭터가 없으면 0번");
        Assert.AreEqual(0, Opened(3).Focus, "범위 밖이면 0번");
        Assert.AreEqual(-1, CharacterSelectPanelRules.InitialFocus(0, 0), "카드가 없으면 -1");
        Assert.IsTrue(Opened(1).IsOpen);
    }

    [Test]
    public void MoveFocus_WrapsAtBothEnds()
    {
        var rules = Opened(0);

        Assert.IsTrue(rules.MoveFocus(-1));
        Assert.AreEqual(2, rules.Focus, "처음에서 위로 = 끝");
        Assert.IsTrue(rules.MoveFocus(1));
        Assert.AreEqual(0, rules.Focus, "끝에서 아래로 = 처음");
        Assert.IsTrue(rules.MoveFocus(4));
        Assert.AreEqual(1, rules.Focus);
    }

    [Test]
    public void MoveFocus_SingleCard_OrClosed_DoesNothing()
    {
        var single = Opened(0, available: new[] { true });
        Assert.IsFalse(single.MoveFocus(1), "한 장이면 포커스가 그대로");
        Assert.AreEqual(0, single.Focus);

        var closed = Opened(0);
        closed.Close();
        Assert.IsFalse(closed.MoveFocus(1));
        Assert.AreEqual(0, closed.Focus);
    }

    [Test]
    public void FocusCard_MovesToClickedCard_IgnoresOutOfRange()
    {
        var rules = Opened(0);

        Assert.IsTrue(rules.FocusCard(2));
        Assert.AreEqual(2, rules.Focus);
        Assert.IsFalse(rules.FocusCard(2), "같은 카드면 바뀐 것 없음");
        Assert.IsFalse(rules.FocusCard(5));
        Assert.IsFalse(rules.FocusCard(-1));
        Assert.AreEqual(2, rules.Focus);
    }

    [Test]
    public void SlotOffset_PreviousAboveNextBelow_Wraps()
    {
        // 3장, 포커스 0 → 2번이 위(-1), 1번이 아래(+1)
        Assert.AreEqual(0, CharacterSelectPanelRules.SlotOffset(0, 0, 3));
        Assert.AreEqual(1, CharacterSelectPanelRules.SlotOffset(1, 0, 3));
        Assert.AreEqual(-1, CharacterSelectPanelRules.SlotOffset(2, 0, 3));

        // 2장 — 다른 카드는 아래
        Assert.AreEqual(1, CharacterSelectPanelRules.SlotOffset(1, 0, 2));
        Assert.AreEqual(1, CharacterSelectPanelRules.SlotOffset(0, 1, 2));

        // 5장 — 포커스 0 이면 2·3번은 화면 밖
        var offsets = Enumerable.Range(0, 5).Select(i => CharacterSelectPanelRules.SlotOffset(i, 0, 5)).ToArray();
        CollectionAssert.AreEqual(new[] { 0, 1, 2, -2, -1 }, offsets);
    }

    [Test]
    public void Confirm_OtherAvailableCharacter_ChosenAndCloses()
    {
        var rules = Opened(0);
        rules.MoveFocus(1);

        Assert.AreEqual(CharacterSelectConfirmResult.Chosen, rules.Confirm());
        Assert.IsFalse(rules.IsOpen);
        Assert.AreEqual(1, rules.Focus, "고른 id = 포커스");
        Assert.AreEqual(CharacterSelectConfirmResult.NotOpen, rules.Confirm(), "닫힌 뒤에는 다시 고르지 않는다");
    }

    [Test]
    public void Confirm_SameAsCurrent_ClosesWithoutChoice()
    {
        var rules = Opened(1);

        Assert.AreEqual(CharacterSelectConfirmResult.SameAsCurrent, rules.Confirm());
        Assert.IsFalse(rules.IsOpen);
    }

    [Test]
    public void CloseReason_SameAsCurrentIsPlainClose_ChosenIsChosen()
    {
        Assert.IsTrue(CharacterSelectPanelRules.TryGetCloseReason(CharacterSelectConfirmResult.SameAsCurrent, out var same));
        Assert.AreEqual(CharacterSelectCloseReason.Button, same, "지금 캐릭터 = 뒤로가기와 같은 그냥 닫기");
        Assert.IsTrue(CharacterSelectPanelRules.TryGetCloseReason(CharacterSelectConfirmResult.Chosen, out var chosen));
        Assert.AreEqual(CharacterSelectCloseReason.Chosen, chosen);

        foreach (var stays in new[] { CharacterSelectConfirmResult.NotOpen, CharacterSelectConfirmResult.Empty,
                     CharacterSelectConfirmResult.Locked, CharacterSelectConfirmResult.Unavailable })
            Assert.IsFalse(CharacterSelectPanelRules.TryGetCloseReason(stays, out _), stays.ToString());
    }

    [Test]
    public void Confirm_Locked_RejectsAndStaysOpen_EvenForCurrent()
    {
        var rules = Opened(0, locked: true);

        Assert.IsTrue(rules.MoveFocus(1), "잠겨도 구경(포커스 이동)은 된다");
        Assert.AreEqual(CharacterSelectConfirmResult.Locked, rules.Confirm());
        Assert.IsTrue(rules.IsOpen);

        rules.FocusCard(0);
        Assert.AreEqual(CharacterSelectConfirmResult.Locked, rules.Confirm(), "잠금이 지금 캐릭터 판단보다 먼저");
        Assert.IsTrue(rules.IsOpen);
    }

    [Test]
    public void Confirm_Unavailable_RejectsAndStaysOpen()
    {
        var rules = Opened(0, available: new[] { true, false, true });
        rules.MoveFocus(1);

        Assert.AreEqual(CharacterSelectConfirmResult.Unavailable, rules.Confirm());
        Assert.IsTrue(rules.IsOpen);
        Assert.IsFalse(rules.IsAvailable(1));
    }

    [Test]
    public void SetLocked_UnlocksWhileOpen()
    {
        var rules = Opened(0, locked: true);
        rules.MoveFocus(1);
        Assert.AreEqual(CharacterSelectConfirmResult.Locked, rules.Confirm());

        Assert.IsTrue(rules.SetLocked(false));
        Assert.IsFalse(rules.SetLocked(false), "같은 값이면 바뀐 것 없음");
        Assert.AreEqual(CharacterSelectConfirmResult.Chosen, rules.Confirm());
    }

    [Test]
    public void Confirm_EmptyRoster_IsEmpty()
    {
        var rules = new CharacterSelectPanelRules();
        rules.Open(0, false, new bool[0]);

        Assert.IsTrue(rules.IsOpen);
        Assert.AreEqual(-1, rules.Focus);
        Assert.AreEqual(CharacterSelectConfirmResult.Empty, rules.Confirm());
        Assert.IsTrue(rules.IsOpen);
    }

    [Test]
    public void Escape_IgnoredWhenClaimedByResultPanel()
    {
        var rules = Opened(0);

        Assert.IsFalse(rules.HandleEscape(true), "결과 패널이 ESC 를 쓰면 같이 닫히지 않는다");
        Assert.IsTrue(rules.IsOpen);
        Assert.IsTrue(rules.HandleEscape(false));
        Assert.IsFalse(rules.IsOpen);
        Assert.IsFalse(rules.HandleEscape(false), "닫혀 있을 때 ESC 는 패널이 쓰지 않는다");
    }

    [Test]
    public void BackButton_And_Close_OnlyOnce()
    {
        var rules = Opened(0);

        Assert.IsTrue(rules.HandleBackButton());
        Assert.IsFalse(rules.HandleBackButton());
        Assert.IsFalse(rules.Close());
    }

    [Test]
    public void Reopen_ResetsFocusAndLock()
    {
        var rules = Opened(0, locked: true);
        rules.MoveFocus(1);
        rules.Close();

        rules.Open(2, false, AllAvailable);
        Assert.IsTrue(rules.IsOpen);
        Assert.IsFalse(rules.IsLocked);
        Assert.AreEqual(2, rules.Focus);
    }

    [Test]
    public void BackgroundScroll_PhaseWrapsAndGridCoversScreen()
    {
        var tile = new Vector2(2900f, 1888f);
        Vector2 velocity = new Vector2(-1f, 0.3f).normalized * 40f; // 대각선도 확인

        // 26° 돌린 1920×1080 화면을 감싸는 상자 — 2×2 타일로 덮을 수 있어야 한다.
        Vector2 half = CharacterSelectBackgroundScroll.RotatedHalfExtents(new Vector2(1920f, 1080f), 26f);
        Assert.IsTrue(CharacterSelectBackgroundScroll.TryCoveringOrigin(tile, half, out Vector2 origin));

        foreach (float t in new[] { 0f, 1f, 72.5f, 1000f, 123456f })
        {
            Vector2 phase = CharacterSelectBackgroundScroll.Phase(velocity, t, tile);
            Assert.That(phase.x, Is.InRange(0f, tile.x));
            Assert.That(phase.y, Is.InRange(0f, tile.y));

            // 묶음(가운데 원점, y 위)이 회전 좌표의 화면 상자를 다 덮는다.
            Vector2 grid = CharacterSelectBackgroundScroll.GridPosition(phase, tile, origin);
            Assert.LessOrEqual(grid.x, -half.x);
            Assert.GreaterOrEqual(grid.x + tile.x * 2f, half.x);
            Assert.GreaterOrEqual(grid.y, half.y);
            Assert.LessOrEqual(grid.y - tile.y * 2f, -half.y);
        }

        Assert.IsFalse(CharacterSelectBackgroundScroll.TryCoveringOrigin(new Vector2(1000f, 600f), half, out _),
            "타일이 작으면 덮을 수 없다고 알린다");

        Vector2 oneSecond = CharacterSelectBackgroundScroll.Phase(velocity, 1f, tile);
        Vector2 zero = CharacterSelectBackgroundScroll.Phase(velocity, 0f, tile);
        float dx = Mathf.Repeat(oneSecond.x - zero.x + tile.x * 0.5f, tile.x) - tile.x * 0.5f;
        float dy = Mathf.Repeat(oneSecond.y - zero.y + tile.y * 0.5f, tile.y) - tile.y * 0.5f;
        Assert.AreEqual(40f, new Vector2(dx, dy).magnitude, 1e-2f, "1초에 40px");
    }

    // ── 카드 이동·확대 연출(할 일 60) ──────────────────────────

    static readonly CharacterSelectSlotLayout Slots =
        new CharacterSelectSlotLayout(new Vector2(523f, -72f), new Vector2(779f, 408f), new Vector2(255f, -550f), 0.84f);

    const float Eps = 1e-4f;

    static CharacterSelectCardMotion Motion() =>
        new CharacterSelectCardMotion { MoveSeconds = 0.18f, RingFadeSeconds = 0.1f, ScaleSeconds = 0.12f };

    [Test]
    public void EaseOutCubic_EndpointsMidpointAndClamp()
    {
        Assert.AreEqual(0f, CharacterSelectCardMotion.EaseOutCubic(0f), Eps);
        Assert.AreEqual(0.875f, CharacterSelectCardMotion.EaseOutCubic(0.5f), Eps);
        Assert.AreEqual(1f, CharacterSelectCardMotion.EaseOutCubic(1f), Eps);
        Assert.AreEqual(0f, CharacterSelectCardMotion.EaseOutCubic(-1f), Eps);
        Assert.AreEqual(1f, CharacterSelectCardMotion.EaseOutCubic(2f), Eps);

        float last = 0f;
        for (int i = 1; i <= 20; i++)
        {
            float v = CharacterSelectCardMotion.EaseOutCubic(i / 20f);
            Assert.Greater(v, last, "단조 증가");
            last = v;
        }
    }

    [Test]
    public void Motion_IdleMovingSettlingIdle_RingAndScaleOverTime()
    {
        var motion = Motion();
        motion.Snap();
        Assert.AreEqual(CharacterSelectMotionPhase.Idle, motion.Phase);
        Assert.AreEqual(1f, motion.RingAlpha(0f), Eps, "열 때 = 테두리 켜짐");
        Assert.AreEqual(1.08f, motion.FocusScale(0f, 1.08f), Eps, "열 때 = 확대 상태");
        Assert.AreEqual(1f, motion.MoveProgress(0f), Eps);

        motion.Retarget(0f);
        Assert.AreEqual(CharacterSelectMotionPhase.Moving, motion.Tick(0f));
        Assert.AreEqual(0f, motion.MoveProgress(0f), Eps);
        Assert.AreEqual(1f, motion.RingAlpha(0f), Eps);
        Assert.AreEqual(1f, motion.FocusScale(0f, 1.08f), Eps, "이동 중 확대 없음");

        Assert.AreEqual(CharacterSelectMotionPhase.Moving, motion.Tick(0.05f));
        Assert.AreEqual(0.5f, motion.RingAlpha(0.05f), Eps, "0.1초 페이드 아웃 절반");
        Assert.AreEqual(0.875f, motion.MoveProgress(0.09f), Eps, "시간 절반 = EaseOutCubic 0.875");
        Assert.AreEqual(0f, motion.RingAlpha(0.1f), Eps, "0.1초면 꺼짐");
        Assert.AreEqual(0f, motion.RingAlpha(0.17f), Eps);

        Assert.AreEqual(CharacterSelectMotionPhase.Settling, motion.Tick(0.181f));
        Assert.AreEqual(1f, motion.MoveProgress(0.181f), Eps, "정착 = 도착");
        Assert.AreEqual(0.01f, motion.RingAlpha(0.181f), 1e-3f, "정착 시작(0.18)부터 켜진다");
        Assert.AreEqual(0.5f, motion.RingAlpha(0.23f), Eps, "0.1초 페이드 인 절반");
        Assert.AreEqual(1f, motion.RingAlpha(0.28f), Eps);
        Assert.AreEqual(1f + 0.08f * 0.875f, motion.FocusScale(0.24f, 1.08f), Eps, "확대 0.12초 절반 = EaseOutCubic");

        Assert.AreEqual(CharacterSelectMotionPhase.Settling, motion.Tick(0.29f), "정착 = 테두리·확대 중 긴 쪽(0.12초)");
        Assert.AreEqual(CharacterSelectMotionPhase.Idle, motion.Tick(0.31f));
        Assert.AreEqual(1f, motion.RingAlpha(0.31f), Eps);
        Assert.AreEqual(1.08f, motion.FocusScale(0.31f, 1.08f), Eps);
    }

    [Test]
    public void Motion_RetargetWhileMoving_RestartsFromNow_NoQueue()
    {
        var motion = Motion();
        motion.Snap();
        motion.Retarget(0f);
        motion.Tick(0.05f);

        motion.Retarget(0.05f); // 빠른 연타 — 새 목표로 바로
        Assert.AreEqual(CharacterSelectMotionPhase.Moving, motion.Phase);
        Assert.AreEqual(0f, motion.MoveProgress(0.05f), Eps, "지금부터 다시 잰다");
        Assert.AreEqual(0.5f, motion.RingAlpha(0.05f), Eps, "테두리는 이어서 꺼진다(다시 켜지지 않는다)");
        Assert.AreEqual(0f, motion.RingAlpha(0.10f), Eps);

        Assert.AreEqual(CharacterSelectMotionPhase.Moving, motion.Tick(0.20f), "첫 출발 기준(0.18)이 아니라 다시 출발한 기준");
        Assert.AreEqual(CharacterSelectMotionPhase.Settling, motion.Tick(0.24f));
    }

    [Test]
    public void Motion_RetargetWhileSettling_FadesOutFromPartialRing()
    {
        var motion = Motion();
        motion.Retarget(0f);
        Assert.AreEqual(CharacterSelectMotionPhase.Settling, motion.Tick(0.181f));
        Assert.AreEqual(0.5f, motion.RingAlpha(0.23f), Eps);

        motion.Retarget(0.23f);
        Assert.AreEqual(CharacterSelectMotionPhase.Moving, motion.Phase);
        Assert.AreEqual(0.5f, motion.RingAlpha(0.23f), Eps, "켜지던 알파에서 바로 꺼지기 시작");
        Assert.AreEqual(0f, motion.RingAlpha(0.28f), Eps);
        Assert.AreEqual(1f, motion.FocusScale(0.23f, 1.08f), Eps);
    }

    [Test]
    public void Motion_ZeroDurations_ArriveAndSettleImmediately()
    {
        var motion = new CharacterSelectCardMotion { MoveSeconds = 0f, RingFadeSeconds = 0f, ScaleSeconds = 0f };
        motion.Retarget(1f);
        Assert.AreEqual(CharacterSelectMotionPhase.Idle, motion.Tick(1f));
        Assert.AreEqual(1f, motion.RingAlpha(1f), Eps);
        Assert.AreEqual(1.08f, motion.FocusScale(1f, 1.08f), Eps);
    }

    [Test]
    public void SlotLayout_OutsideSlotsExtendTheStack()
    {
        Assert.AreEqual(Slots.Focused, Slots.Position(0));
        Assert.AreEqual(Slots.Previous, Slots.Position(-1));
        Assert.AreEqual(Slots.Next, Slots.Position(1));
        Assert.AreEqual(new Vector2(1035f, 888f), Slots.Position(-2), "위 바깥 = 위 자리에서 같은 간격 더");
        Assert.AreEqual(new Vector2(-13f, -1028f), Slots.Position(2), "아래 바깥");
        Assert.AreEqual(1f, Slots.Scale(0), Eps);
        Assert.AreEqual(0.84f, Slots.Scale(-1), Eps);
        Assert.AreEqual(0.84f, Slots.Scale(2), Eps);
    }

    [Test]
    public void Track_VisibleCardSlidesFromCurrentPosition()
    {
        // 다음 카드(+1)가 이동 중 어딘가에 있다가 가운데(0)로 새 목표
        CharacterSelectCardTrack track = CharacterSelectCardMotion.Snap(1, Slots);
        var midway = new Vector2(400f, -300f);
        track = CharacterSelectCardMotion.Retarget(track, midway, 0.9f, 0, 1, Slots);

        Assert.IsTrue(track.Active);
        Assert.IsFalse(track.HideAtEnd);
        Assert.IsTrue(track.Focused);
        Assert.AreEqual(midway, track.Position(0f), "점프 없이 지금 자리에서");
        Assert.AreEqual(Slots.Focused, track.Position(1f));
        Assert.AreEqual(0.9f, track.Scale(0f, 1f), Eps);
        Assert.AreEqual(1.08f, track.Scale(1f, 1.08f), Eps, "가운데 도착 카드만 정착 확대");
    }

    [Test]
    public void Track_LeavingCenterShrinksBackToSideScale()
    {
        CharacterSelectCardTrack track = CharacterSelectCardMotion.Snap(0, Slots);
        float shown = track.Scale(1f, 1.08f);
        Assert.AreEqual(1.08f, shown, Eps);

        track = CharacterSelectCardMotion.Retarget(track, track.Position(1f), shown, -1, 1, Slots);
        Assert.IsFalse(track.Focused);
        Assert.AreEqual(1.08f, track.Scale(0f, 1.08f), Eps, "확대 상태에서 출발");
        Assert.AreEqual(0.84f, track.Scale(1f, 1.08f), Eps, "원래(옆) 크기로");
    }

    [Test]
    public void Track_HiddenCardEntersFromOutside_LeavingCardExitsThenTurnsOff()
    {
        // 4장, 포커스 0 → 1 : 2번(+2, 꺼짐) → +1 은 아래 바깥에서 들어오고, 3번(-1) → -2 는 위 바깥으로 빠진다.
        CharacterSelectCardTrack entering = CharacterSelectCardMotion.Snap(CharacterSelectPanelRules.SlotOffset(2, 0, 4), Slots);
        CharacterSelectCardTrack leaving = CharacterSelectCardMotion.Snap(CharacterSelectPanelRules.SlotOffset(3, 0, 4), Slots);
        Assert.IsFalse(entering.Active);
        Assert.IsTrue(leaving.Active);

        entering = CharacterSelectCardMotion.Retarget(entering, Vector2.zero, 0f, CharacterSelectPanelRules.SlotOffset(2, 1, 4), 1, Slots);
        Assert.IsTrue(entering.Active);
        Assert.AreEqual(Slots.Position(2), entering.Position(0f), "아래 바깥에서 출발");
        Assert.AreEqual(Slots.Next, entering.Position(1f));

        leaving = CharacterSelectCardMotion.Retarget(leaving, Slots.Previous, 0.84f, CharacterSelectPanelRules.SlotOffset(3, 1, 4), 1, Slots);
        Assert.IsTrue(leaving.Active, "나가는 동안은 보인다");
        Assert.IsTrue(leaving.HideAtEnd);
        Assert.AreEqual(Slots.Position(-2), leaving.Position(1f), "위 바깥으로");

        Assert.IsFalse(CharacterSelectCardMotion.Arrive(leaving).Active, "이동이 끝나면 꺼진다");
        CharacterSelectCardTrack arrived = CharacterSelectCardMotion.Arrive(entering);
        Assert.IsTrue(arrived.Active);
        Assert.AreEqual(Slots.Next, arrived.Position(0f), "도착 자리에 고정");

        // 반대 방향(포커스 1 → 0)이면 꺼져 있던 3번이 위(-1)로, 위 바깥에서 들어온다.
        CharacterSelectCardTrack fromTop = CharacterSelectCardMotion.Retarget(CharacterSelectCardMotion.Arrive(leaving), Vector2.zero, 0f,
            CharacterSelectPanelRules.SlotOffset(3, 0, 4), -1, Slots);
        Assert.AreEqual(Slots.Position(-2), fromTop.Position(0f));
        Assert.AreEqual(Slots.Previous, fromTop.Position(1f));
    }

    [Test]
    public void Track_HiddenStaysHidden_EnterOffsetForBigJumpFollowsDirection()
    {
        CharacterSelectCardTrack hidden = CharacterSelectCardMotion.Snap(2, Slots);
        Assert.IsFalse(CharacterSelectCardMotion.Retarget(hidden, Vector2.zero, 0f, -2, 1, Slots).Active);

        Assert.AreEqual(-2, CharacterSelectCardMotion.EnterOffset(-1, 1));
        Assert.AreEqual(2, CharacterSelectCardMotion.EnterOffset(1, -1));
        Assert.AreEqual(2, CharacterSelectCardMotion.EnterOffset(0, 1), "앞으로 가면 카드가 올라가므로 아래에서 온다");
        Assert.AreEqual(-2, CharacterSelectCardMotion.EnterOffset(0, -1));

        // 4장이면 ±2 가 +2 로 정해지지만, 빠지는 쪽은 카드가 흘러가는 방향이다.
        Assert.AreEqual(2, CharacterSelectPanelRules.SlotOffset(3, 1, 4));
        Assert.AreEqual(-2, CharacterSelectCardMotion.ExitOffset(2, 1), "앞으로 가면 위로 빠진다");
        Assert.AreEqual(2, CharacterSelectCardMotion.ExitOffset(2, -1), "뒤로 가면 아래로 빠진다");
        CharacterSelectCardTrack down = CharacterSelectCardMotion.Retarget(CharacterSelectCardMotion.Snap(1, Slots), Slots.Next, 0.84f,
            CharacterSelectPanelRules.SlotOffset(2, 0, 4), -1, Slots); // 4장, 포커스 1 → 0 : 2번(+1) 은 아래 바깥으로
        Assert.AreEqual(Slots.Position(2), down.Position(1f));
    }

    [Test]
    public void LogicalFocus_UpdatesImmediately_ConfirmTargetsNewCenterDuringMove()
    {
        // 이동 연출은 논리 포커스를 늦추지 않는다 — MoveFocus 직후 Confirm 대상 = 새 가운데.
        var rules = Opened(0);
        var motion = Motion();
        motion.Snap();

        rules.MoveFocus(1);
        motion.Retarget(0f);
        Assert.AreEqual(CharacterSelectMotionPhase.Moving, motion.Tick(0.05f));
        Assert.AreEqual(1, rules.Focus);
        Assert.AreEqual(CharacterSelectConfirmResult.Chosen, rules.Confirm());
        Assert.AreEqual(1, rules.Focus);
    }

    // ── 이동 키 길게 누르기(할 일 60·60.1) — 대기 0.25s · 간격 0.25s → 0.1s(램프 0.75s) ──

    static CharacterSelectKeyRepeat Repeat() => new CharacterSelectKeyRepeat
    {
        DelaySeconds = 0.25f, StartIntervalSeconds = 0.25f, MinIntervalSeconds = 0.1f, RampSeconds = 0.75f
    };

    // 앞으로 키를 0 부터 end 까지 1ms 마다 누르고 있을 때 칸을 낸 시각.
    static List<float> HoldStepTimes(CharacterSelectKeyRepeat repeat, float end)
    {
        var times = new List<float>();
        for (int ms = 0; ms <= Mathf.RoundToInt(end * 1000f); ms++)
        {
            float t = ms / 1000f;
            if (repeat.Tick(false, true, t) != 0)
                times.Add(t);
        }
        return times;
    }

    [Test]
    public void KeyRepeat_FirstPressMovesOnce_ThenWaitsDelay_ThenEveryInterval()
    {
        var repeat = Repeat();
        Assert.AreEqual(1, repeat.Tick(false, true, 0f), "누른 순간 1칸");
        Assert.AreEqual(0, repeat.Tick(false, true, 0.1f));
        Assert.AreEqual(0, repeat.Tick(false, true, 0.24f), "대기 전에는 그대로");
        Assert.AreEqual(1, repeat.Tick(false, true, 0.26f), "첫 반복");
        Assert.AreEqual(0, repeat.Tick(false, true, 0.36f));
        Assert.AreEqual(0, repeat.Tick(false, true, 0.49f), "첫 반복 직후 간격 = 시작 간격 0.25s");
        Assert.AreEqual(1, repeat.Tick(false, true, 0.51f), "이후 간격마다");
        Assert.AreEqual(0, repeat.Tick(false, true, 0.69f));
        Assert.AreEqual(1, repeat.Tick(false, true, 0.71f));
    }

    [Test]
    public void KeyRepeat_IntervalShrinksLinearly_ReachingMinAtOneSecond()
    {
        // 간격 0.25 → 0.20 → 0.16 → 0.128 → 0.1024 → 0.1 …
        float[] expected = { 0f, 0.25f, 0.50f, 0.70f, 0.86f, 0.988f, 1.0904f, 1.1904f };
        List<float> times = HoldStepTimes(Repeat(), 1.25f);
        Assert.AreEqual(expected.Length, times.Count, string.Join(", ", times));
        for (int i = 0; i < expected.Length; i++)
            Assert.AreEqual(expected[i], times[i], 0.003f, $"{i}번째 칸 시각 — {string.Join(", ", times)}");
    }

    [Test]
    public void KeyRepeat_LongHold_NeverGoesBelowMinInterval()
    {
        List<float> times = HoldStepTimes(Repeat(), 5f);
        for (int i = 1; i < times.Count; i++)
            Assert.GreaterOrEqual(times[i] - times[i - 1], 0.0985f,$"{times[i - 1]}s → {times[i]}s");
        Assert.AreEqual(0.1f, times[times.Count - 1] - times[times.Count - 2], 0.0015f, "램프 뒤에는 최소 간격 유지");
    }

    [Test]
    public void KeyRepeat_ReleaseStops_RepressStartsOver()
    {
        var repeat = Repeat();
        Assert.AreEqual(-1, repeat.Tick(true, false, 0f));
        Assert.AreEqual(0, repeat.Tick(false, false, 0.1f), "떼면 멈춤");
        Assert.AreEqual(0, repeat.Tick(false, false, 0.5f));
        Assert.AreEqual(0, repeat.Direction);
        Assert.AreEqual(-1, repeat.Tick(true, false, 0.6f), "다시 누르면 즉시 1칸");
        Assert.AreEqual(0, repeat.Tick(true, false, 0.8f), "대기부터 다시");
        Assert.AreEqual(-1, repeat.Tick(true, false, 0.86f));
        Assert.AreEqual(0, repeat.Tick(true, false, 1.05f), "가속도 처음부터 — 다음 간격 0.25s");
        Assert.AreEqual(-1, repeat.Tick(true, false, 1.11f));
    }

    [Test]
    public void KeyRepeat_OppositePress_MovesImmediately_AndRestartsDelay()
    {
        var repeat = Repeat();
        repeat.Tick(false, true, 0f);
        repeat.Tick(false, true, 0.26f);
        Assert.AreEqual(-1, repeat.Tick(true, true, 0.30f), "나중에 누른 반대 방향이 즉시 1칸");
        Assert.AreEqual(0, repeat.Tick(true, true, 0.50f), "대기부터 다시");
        Assert.AreEqual(-1, repeat.Tick(true, true, 0.56f), "둘 다 눌려 있으면 나중에 누른 쪽으로 반복");
        Assert.AreEqual(0, repeat.Tick(true, true, 0.79f), "가속도 처음부터 — 다음 간격 0.25s");
        Assert.AreEqual(-1, repeat.Tick(true, true, 0.81f));
    }

    [Test]
    public void KeyRepeat_ReleasingWinner_FallsBackToStillHeld_WithDelayAndNoImmediateStep()
    {
        var repeat = Repeat();
        repeat.Tick(false, true, 0f);
        repeat.Tick(true, true, 0.1f); // 뒤로가 이김
        Assert.AreEqual(0, repeat.Tick(false, true, 0.2f), "이기던 키를 떼면 남은 쪽으로 — 즉시 1칸은 없다");
        Assert.AreEqual(1, repeat.Direction);
        Assert.AreEqual(0, repeat.Tick(false, true, 0.44f));
        Assert.AreEqual(1, repeat.Tick(false, true, 0.46f), "대기 뒤 반복");
        Assert.AreEqual(0, repeat.Tick(false, true, 0.69f), "가속도 처음부터 — 다음 간격 0.25s");
        Assert.AreEqual(1, repeat.Tick(false, true, 0.71f));
    }

    [Test]
    public void KeyRepeat_BothPressedSameFrame_BackWins()
    {
        Assert.AreEqual(-1, Repeat().Tick(true, true, 0f), "기존 키 검사 순서와 같이 뒤로");
    }

    [Test]
    public void KeyRepeat_LongFrame_MovesAtMostOnce_AndDropsBacklog()
    {
        var repeat = Repeat();
        repeat.Tick(false, true, 0f);
        Assert.AreEqual(1, repeat.Tick(false, true, 1f), "간격 여러 개가 지나도 한 프레임 1칸");
        Assert.AreEqual(0, repeat.Tick(false, true, 1.01f), "밀린 칸을 다음 프레임에 몰아서 가지 않는다");
        Assert.AreEqual(1, repeat.Tick(false, true, 1.11f));
    }

    [Test]
    public void KeyRepeat_Reset_IgnoresKeysHeldUntilRepressed()
    {
        var repeat = Repeat();
        repeat.Tick(false, true, 0f);
        repeat.Reset();
        Assert.AreEqual(0, repeat.Tick(false, true, 0.3f), "리셋 때 눌려 있던 키는 무시");
        Assert.AreEqual(0, repeat.Tick(false, true, 1f));
        Assert.AreEqual(-1, repeat.Tick(true, true, 1.1f), "다른 키는 새로 누르면 바로 받는다");
        Assert.AreEqual(0, repeat.Tick(false, false, 1.2f));
        Assert.AreEqual(1, repeat.Tick(false, true, 1.3f), "뗐다가 새로 누르면 받는다");
    }
}
