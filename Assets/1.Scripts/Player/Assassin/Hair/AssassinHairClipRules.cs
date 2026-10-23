using UnityEngine;

/// <summary>할 일 47 — 지금 클립에 덧칠할 머리카락 보정 종류.</summary>
public enum AssassinHairOverlayKind
{
    /// <summary>v13 물리만.</summary>
    None,
    /// <summary>v16 동작별 보정(<see cref="AssassinActionHairAnimation"/>).</summary>
    Action,
    /// <summary>v15 회피 전용(<see cref="AssassinDiveHairAnimation"/>).</summary>
    Dive,
}

/// <summary>
/// 할 일 47 — 어쌔신 머리카락 구동기(<see cref="AssassinHairDriver"/>)의 순수 규칙. Animator 없이 EditMode 로 검증한다.
/// 기준 클립은 항상 Base Layer 다(UpperBody 레이어는 보지 않는다 — 사용자 결정).
/// </summary>
public static class AssassinHairClipRules
{
    /// <summary>v15 회피 머리를 쓰는 유일한 클립.</summary>
    public const string DodgeClipName = "Dodge_Combat_F_0";

    /// <summary>클립 이름 → 보정 종류. 회피는 v15, v16 프로필이 있는 클립은 v16, 그 외는 v13 만.</summary>
    public static AssassinHairOverlayKind Select(string clipName)
    {
        if (clipName == DodgeClipName)
            return AssassinHairOverlayKind.Dive;

        return AssassinActionHairProfiles.IsSupported(clipName)
            ? AssassinHairOverlayKind.Action
            : AssassinHairOverlayKind.None;
    }

    /// <summary>
    /// 상태 정규화 시간 → 클립 안 초. 재생 속도(평타 3.6배 등)와 무관하게 클립 위 위치가 같다.
    /// 루프 클립은 한 바퀴 안으로 접고, 루프가 아닌 클립은 끝(길이)에서 멈춘다 — Animator 가 마지막 프레임을 유지하는 것과 같다.
    /// </summary>
    public static float ClipTime(float normalizedTime, float clipLength, bool looping)
    {
        if (clipLength <= 0f)
            return 0f;

        float progress = Mathf.Max(0f, normalizedTime);
        return (looping ? progress - Mathf.Floor(progress) : Mathf.Min(progress, 1f)) * clipLength;
    }

    /// <summary>몇 번째 바퀴인가. 루프가 아닌 클립은 끝을 지나도 0.</summary>
    public static long LoopIndex(float normalizedTime, bool looping)
    {
        return looping ? (long)Mathf.Floor(Mathf.Max(0f, normalizedTime)) : 0L;
    }

    /// <summary>
    /// 달리기 계열(Start·Loop·Stop). 이 클립들은 trailing 을 출발 순간에 한 번 고정하지 않고 몸 방향을 매 프레임 따라간다.
    /// 고정하면 달리며 방향을 틀 때 머리카락이 옛 방향을 가리키다 루프 한 바퀴(상태 속도 0.8 기준 약 0.58초)마다 새 방향으로 한 번에 넘어간다(10-22 사용자 보고).
    /// </summary>
    public static bool FollowsFacing(string clipName)
    {
        return clipName == "Run_Combat_Fast_Start"
            || clipName == "Run_Combat_Fast_Loop"
            || clipName == "Run_Combat_Fast_Stop";
    }

    /// <summary>
    /// 이번 프레임 trailing 방향(보정이 가닥을 눕히는 수평 방향).
    /// <paramref name="facingTrailing"/> 은 지금 몸 방향의 반대(-forward)다.
    /// <paramref name="relaunch"/> 는 <see cref="AssassinHairClipTracker.Observe"/> 결과,
    /// <paramref name="restarted"/> 는 <see cref="AssassinHairClipTracker.Restarted"/>(처음 관측·v13 리셋).
    /// <paramref name="following"/> 은 이번 클립이 달리기 계열, <paramref name="wasFollowing"/> 은 지난 프레임도 달리기 계열이었는가.
    /// 규칙:
    /// · 달리기 계열 안에서 이어지면(루프 한 바퀴·Start→Loop→Stop·같은 상태 되감기) 다시 잡지 않고 이어서 따라간다.
    /// · 그 밖의 relaunch(처음·리셋·다른 클립에서 들어옴)는 예전처럼 지금 몸 방향으로 다시 잡는다.
    /// · 달리기 계열이 아니면 relaunch 사이에는 그대로 둔다(평타·Q·회피의 출발 방향 고정은 바뀌지 않는다).
    /// </summary>
    public static Vector3 NextTrailing(Vector3 current, Vector3 facingTrailing, bool relaunch, bool restarted,
        bool following, bool wasFollowing, float deltaTime, float followSeconds)
    {
        Vector3 target = Vector3.ProjectOnPlane(facingTrailing, Vector3.up);
        bool continues = following && wasFollowing && !restarted && IsHorizontalDirection(current);
        if (relaunch && !continues)
            return target;
        if (!following)
            return current;
        return FollowFacing(current, target, deltaTime, followSeconds);
    }

    /// <summary>
    /// <paramref name="current"/> 를 수평면에서 <paramref name="target"/> 쪽으로 돌린다. 시간 상수 <paramref name="followSeconds"/> 의 지수 따라가기라
    /// 프레임 속도가 달라도 같은 시간에 같은 만큼 돈다. 0 이하면 바로 맞춘다. 결과는 수평 단위 벡터다(180° 반대여도 위로 꺾이지 않는다).
    /// </summary>
    public static Vector3 FollowFacing(Vector3 current, Vector3 target, float deltaTime, float followSeconds)
    {
        target = Vector3.ProjectOnPlane(target, Vector3.up);
        if (!IsHorizontalDirection(target))
            return current;
        target.Normalize();

        current = Vector3.ProjectOnPlane(current, Vector3.up);
        if (!IsHorizontalDirection(current) || !(followSeconds > 0f))
            return target;
        current.Normalize();

        if (!(deltaTime > 0f))
            return current;

        float amount = 1f - Mathf.Exp(-deltaTime / followSeconds);
        float angle = Vector3.SignedAngle(current, target, Vector3.up);
        return (Quaternion.AngleAxis(angle * amount, Vector3.up) * current).normalized;
    }

    /// <summary>
    /// 보정 전환 이어주기(관성 전환)의 남은 비율. 전환 순간 1 → <paramref name="duration"/> 초 뒤 0(smootherstep).
    /// 0 이하 길이·NaN 은 0(이어주기 없음).
    /// </summary>
    public static float CarryWeight(float elapsed, float duration)
    {
        if (!(duration > 0f) || float.IsNaN(elapsed) || elapsed >= duration)
            return 0f;

        float x = Mathf.Clamp01(elapsed / duration);
        return 1f - x * x * x * (x * (x * 6f - 15f) + 10f);
    }

    /// <summary>
    /// 전환 순간의 차이. <paramref name="previousApplied"/> 는 지난 프레임에 실제로 얹은 보정 회전(월드, base 왼쪽에 곱함),
    /// <paramref name="current"/> 는 이번 프레임 새 보정 회전. <see cref="ApplyCarry"/> 에 비율 1 을 주면 지난 프레임 모양이 그대로 나온다.
    /// </summary>
    public static Quaternion CarryOffset(Quaternion previousApplied, Quaternion current)
    {
        return Normalized(previousApplied * Quaternion.Inverse(current));
    }

    /// <summary>새 보정 <paramref name="current"/> 에 남은 차이를 <paramref name="weight"/> 만큼 얹는다(0 = 새 보정 그대로).</summary>
    public static Quaternion ApplyCarry(Quaternion offset, float weight, Quaternion current)
    {
        if (!(weight > 0f))
            return current;

        return Normalized(Quaternion.Slerp(Quaternion.identity, offset, Mathf.Clamp01(weight)) * current);
    }

    private static Quaternion Normalized(Quaternion q)
    {
        float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        return m > 1e-8f && !float.IsNaN(m) ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity;
    }

    private static bool IsHorizontalDirection(Vector3 direction)
    {
        Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
        return flat.sqrMagnitude > 1e-8f
            && !float.IsNaN(flat.x) && !float.IsNaN(flat.z)
            && !float.IsInfinity(flat.x) && !float.IsInfinity(flat.z);
    }
}

/// <summary>
/// 할 일 47 — 보정의 "출발 반대 방향(trailing)"을 다시 잡을 순간을 고른다. 납품 검수 브리지와 같은 규칙:
/// 처음 · 상태/클립 전환 · 루프 한 바퀴 · 시간 되감기(Any State 로 같은 상태 재진입)에 다시 잡는다.
/// 브리지와 달리 v13 리셋(순간이동·재활성)에도 다시 잡는다 — 게임에선 리셋이 위치·방향이 바뀐 뒤에 온다.
/// 달리기 계열은 이 신호를 받아도 이어서 따라간다(<see cref="AssassinHairClipRules.NextTrailing"/>) — 다만 <see cref="Restarted"/> 면 다시 잡는다.
/// </summary>
public sealed class AssassinHairClipTracker
{
    // Animator 정규화 시간은 정방향 재생에서 줄지 않는다. 이만큼 넘게 줄면 같은 상태를 처음부터 다시 튼 것이다.
    private const float RewindEpsilon = 1e-4f;

    private bool observed;
    private int stateHash;
    private string clipName;
    private long loop;
    private float normalizedTime;
    private int resetCount;

    /// <summary>지난 <see cref="Observe"/> 가 처음 관측(<see cref="Invalidate"/> 뒤 포함)이었거나 v13 리셋을 봤는가.</summary>
    public bool Restarted { get; private set; }

    /// <summary>
    /// 지난 relaunch 가 같은 상태·같은 클립의 루프 한 바퀴뿐이었는가(전환·되감기·리셋 없음).
    /// 이때는 보정 모양이 이어지므로 전환 이어주기를 하지 않는다.
    /// </summary>
    public bool LoopOnly { get; private set; }

    /// <summary>다음 관측을 처음으로 만든다(구동기 재활성 등).</summary>
    public void Invalidate()
    {
        observed = false;
    }

    /// <summary>이번 프레임 Base Layer 상태를 기록하고, trailing 방향을 다시 잡아야 하면 true.</summary>
    public bool Observe(int stateHash, string clipName, float normalizedTime, bool looping, int resetCount)
    {
        long currentLoop = AssassinHairClipRules.LoopIndex(normalizedTime, looping);
        Restarted = !observed || resetCount != this.resetCount;
        bool switched = Restarted
            || stateHash != this.stateHash
            || clipName != this.clipName
            || normalizedTime < this.normalizedTime - RewindEpsilon;
        bool relaunch = switched || currentLoop != loop;
        LoopOnly = relaunch && !switched;

        observed = true;
        this.stateHash = stateHash;
        this.clipName = clipName;
        this.normalizedTime = normalizedTime;
        this.resetCount = resetCount;
        loop = currentLoop;
        return relaunch;
    }
}
