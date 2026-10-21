using UnityEngine;

/// <summary>카드 연출 단계 — 정지 → 이동(미끄러짐, 테두리 페이드 아웃) → 정착(가운데 확대·테두리 페이드 인) → 정지.</summary>
public enum CharacterSelectMotionPhase
{
    Idle = 0,     // 다 섰다 — 가운데 카드 테두리 켜짐·확대
    Moving = 1,   // 새 자리로 미끄러지는 중 — 테두리 꺼지는 중, 확대 없음
    Settling = 2  // 자리에 섰다 — 가운데 카드가 커지고 테두리가 다시 켜지는 중
}

/// <summary>
/// 세로 스택 자리(cardContainer 기준). offset 0 = 가운데, -1 = 위(이전), +1 = 아래(다음).
/// |offset| ≥ 2 = 화면 밖 — 위·아래 자리에서 같은 간격만큼 더 나간 곳(들어오는 카드의 출발점·나가는 카드의 도착점).
/// </summary>
public readonly struct CharacterSelectSlotLayout
{
    public readonly Vector2 Focused;
    public readonly Vector2 Previous;
    public readonly Vector2 Next;
    public readonly float SideScale;

    public CharacterSelectSlotLayout(Vector2 focused, Vector2 previous, Vector2 next, float sideScale)
    {
        Focused = focused;
        Previous = previous;
        Next = next;
        SideScale = sideScale;
    }

    public Vector2 Position(int offset)
    {
        if (offset == 0)
            return Focused;
        if (offset == -1)
            return Previous;
        if (offset == 1)
            return Next;
        return offset < 0 ? Previous + (Previous - Focused) : Next + (Next - Focused);
    }

    /// <summary>자리 배율(가운데 확대 제외) — 가운데 1, 그 밖 SideScale.</summary>
    public float Scale(int offset) => offset == 0 ? 1f : SideScale;
}

/// <summary>
/// 카드 하나의 이번 이동 — 출발(지금 보이는 자리·배율)에서 도착(새 자리)까지. 진행도는 <see cref="CharacterSelectCardMotion.MoveProgress"/>.
/// </summary>
public struct CharacterSelectCardTrack
{
    /// <summary>화면에 켜져 있다(나가는 중 포함).</summary>
    public bool Active;

    /// <summary>화면 밖으로 나가는 중 — 이동이 끝나면 끈다(<see cref="CharacterSelectCardMotion.Arrive"/>).</summary>
    public bool HideAtEnd;

    /// <summary>도착 자리가 가운데 — 정착 때 확대된다.</summary>
    public bool Focused;

    public Vector2 From;
    public Vector2 To;
    public float FromScale;
    public float ToScale;

    public Vector2 Position(float progress) => Vector2.Lerp(From, To, progress);

    /// <summary>배율 — 자리 배율 보간 × (가운데면) 정착 확대 배율.</summary>
    public float Scale(float progress, float focusScale) => Mathf.Lerp(FromScale, ToScale, progress) * (Focused ? focusScale : 1f);
}

/// <summary>
/// 캐릭터 선택 카드 이동·확대 연출(할 일 60)의 시간 규칙(MonoBehaviour 밖 — EditMode 테스트 대상).
/// 시간은 호출 측이 넣는다(패널 = <c>Time.unscaledTime</c>, 슬로모션 무관). 상태 전이 = Idle → Moving → Settling → Idle.
/// - 이동 중 새 목표가 오면 큐잉하지 않고 지금 위치에서 새 목표로 다시 출발한다(<see cref="Retarget(float)"/>).
/// - 금색 테두리 = "가운데 자리 강조": 이동이 시작되면 지금 알파에서 꺼지고, 정착하면 다시 켜진다.
/// </summary>
public sealed class CharacterSelectCardMotion
{
    /// <summary>이동 시간(초, EaseOutCubic).</summary>
    public float MoveSeconds = 0.18f;

    /// <summary>테두리 페이드(초, 0→1 또는 1→0 전체 시간).</summary>
    public float RingFadeSeconds = 0.1f;

    /// <summary>정착 확대 시간(초, EaseOutCubic).</summary>
    public float ScaleSeconds = 0.12f;

    private float _moveStart;
    private float _settleStart;
    private float _ringAlphaAtMove = 1f;

    public CharacterSelectMotionPhase Phase { get; private set; } = CharacterSelectMotionPhase.Idle;

    /// <summary>애니메이션 없이 다 선 상태로(열 때·잠금 변경).</summary>
    public void Snap() => Phase = CharacterSelectMotionPhase.Idle;

    /// <summary>새 목표로 이동 시작 — 어느 단계에서든 지금부터 다시 잰다. 테두리는 지금 알파에서 꺼지기 시작한다.</summary>
    public void Retarget(float now)
    {
        _ringAlphaAtMove = RingAlpha(now);
        _moveStart = now;
        Phase = CharacterSelectMotionPhase.Moving;
    }

    /// <summary>시간을 흘려 단계를 넘긴다. 이동이 끝나면 Settling, 정착(테두리·확대 중 긴 쪽)이 끝나면 Idle.</summary>
    public CharacterSelectMotionPhase Tick(float now)
    {
        if (Phase == CharacterSelectMotionPhase.Moving && now - _moveStart >= MoveSeconds)
        {
            Phase = CharacterSelectMotionPhase.Settling;
            _settleStart = _moveStart + Mathf.Max(0f, MoveSeconds);
        }

        if (Phase == CharacterSelectMotionPhase.Settling && now - _settleStart >= Mathf.Max(RingFadeSeconds, ScaleSeconds))
            Phase = CharacterSelectMotionPhase.Idle;

        return Phase;
    }

    /// <summary>이동 진행도(0~1, EaseOutCubic). 이동 중이 아니면 1(도착).</summary>
    public float MoveProgress(float now)
    {
        if (Phase != CharacterSelectMotionPhase.Moving)
            return 1f;
        return EaseOutCubic(Fraction(now - _moveStart, MoveSeconds));
    }

    /// <summary>가운데 자리 테두리 알파(0~1). 이동 중 = 시작 알파에서 선형으로 꺼짐, 정착 중 = 0에서 켜짐, 정지 = 1.</summary>
    public float RingAlpha(float now)
    {
        switch (Phase)
        {
            case CharacterSelectMotionPhase.Moving:
                if (RingFadeSeconds <= 0f)
                    return 0f;
                return Mathf.Max(0f, _ringAlphaAtMove - (now - _moveStart) / RingFadeSeconds);
            case CharacterSelectMotionPhase.Settling:
                return Fraction(now - _settleStart, RingFadeSeconds);
            default:
                return 1f;
        }
    }

    /// <summary>가운데 카드 확대 배율 — 이동 중 1, 정착 중 1 → <paramref name="focusScale"/>(EaseOutCubic), 정지 = <paramref name="focusScale"/>.</summary>
    public float FocusScale(float now, float focusScale)
    {
        switch (Phase)
        {
            case CharacterSelectMotionPhase.Moving:
                return 1f;
            case CharacterSelectMotionPhase.Settling:
                return Mathf.LerpUnclamped(1f, focusScale, EaseOutCubic(Fraction(now - _settleStart, ScaleSeconds)));
            default:
                return focusScale;
        }
    }

    public static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }

    /// <summary>
    /// 보이지 않던 카드가 들어올 출발 자리(화면 밖 ±2). 도착이 위(-1)면 위 바깥, 아래(+1)면 아래 바깥.
    /// 도착이 가운데(큰 점프)면 이동 방향 쪽 — 포커스가 앞으로(+) 가면 카드는 위로 올라가므로 아래 바깥에서 온다.
    /// </summary>
    public static int EnterOffset(int newOffset, int moveDirection)
    {
        if (newOffset != 0)
            return newOffset < 0 ? -2 : 2;
        return moveDirection < 0 ? -2 : 2;
    }

    /// <summary>
    /// 보이던 카드가 화면 밖이 될 때 빠질 자리(±2) — 카드가 흘러가는 쪽. 포커스가 앞으로(+) 가면 카드는 위로 올라가므로 위 바깥.
    /// 자리 offset 은 4장일 때 ±2 가 +2 로 정해져(<see cref="CharacterSelectPanelRules.SlotOffset"/>) 그대로 쓰면 위 카드가 스택을 가로질러 아래로 빠진다.
    /// </summary>
    public static int ExitOffset(int newOffset, int moveDirection)
    {
        if (moveDirection != 0)
            return moveDirection > 0 ? -2 : 2;
        return newOffset < 0 ? -2 : 2;
    }

    /// <summary>애니메이션 없이 그 자리에 둔다. |offset| ≥ 2 면 꺼짐.</summary>
    public static CharacterSelectCardTrack Snap(int offset, in CharacterSelectSlotLayout layout)
    {
        bool visible = offset >= -1 && offset <= 1;
        Vector2 position = layout.Position(Mathf.Clamp(offset, -2, 2));
        float scale = layout.Scale(offset);
        return new CharacterSelectCardTrack
        {
            Active = visible,
            Focused = offset == 0,
            From = position,
            To = position,
            FromScale = scale,
            ToScale = scale
        };
    }

    /// <summary>
    /// 새 목표. 켜져 있던 카드는 지금 보이는 자리·배율(<paramref name="currentPosition"/>·<paramref name="currentScale"/>)에서 출발한다.
    /// - 보이던 → 보임: 그대로 미끄러진다(2·3장 순환에서 반대편으로 가는 카드는 가운데 카드 뒤로 가로지른다).
    /// - 꺼져 있던 → 보임: <see cref="EnterOffset"/> 바깥 자리에서 들어온다.
    /// - 보이던 → 화면 밖: 흘러가는 쪽 바깥 자리(<see cref="ExitOffset"/>)로 빠지고 이동이 끝나면 꺼진다.
    /// - 꺼져 있던 → 화면 밖: 꺼진 채.
    /// </summary>
    public static CharacterSelectCardTrack Retarget(in CharacterSelectCardTrack current, Vector2 currentPosition, float currentScale,
        int newOffset, int moveDirection, in CharacterSelectSlotLayout layout)
    {
        bool visible = newOffset >= -1 && newOffset <= 1;
        if (!visible && !current.Active)
            return Snap(newOffset, layout);

        var track = new CharacterSelectCardTrack
        {
            Active = true,
            HideAtEnd = !visible,
            Focused = newOffset == 0,
            To = layout.Position(visible ? newOffset : ExitOffset(newOffset, moveDirection)),
            ToScale = layout.Scale(newOffset)
        };

        if (current.Active)
        {
            track.From = currentPosition;
            track.FromScale = currentScale;
        }
        else
        {
            int enter = EnterOffset(newOffset, moveDirection);
            track.From = layout.Position(enter);
            track.FromScale = layout.Scale(enter);
        }

        return track;
    }

    /// <summary>이동이 끝났다 — 도착 자리에 고정하고, 나가던 카드는 끈다.</summary>
    public static CharacterSelectCardTrack Arrive(in CharacterSelectCardTrack track)
    {
        CharacterSelectCardTrack arrived = track;
        arrived.From = track.To;
        arrived.FromScale = track.ToScale;
        if (track.HideAtEnd)
        {
            arrived.Active = false;
            arrived.HideAtEnd = false;
        }
        return arrived;
    }

    private static float Fraction(float elapsed, float duration) => duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
}
