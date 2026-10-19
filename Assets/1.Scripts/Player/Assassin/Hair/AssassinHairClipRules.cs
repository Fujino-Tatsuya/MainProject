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
}

/// <summary>
/// 할 일 47 — 보정의 "출발 반대 방향(trailing)"을 다시 잡을 순간을 고른다. 납품 검수 브리지와 같은 규칙:
/// 처음 · 상태/클립 전환 · 루프 한 바퀴 · 시간 되감기(Any State 로 같은 상태 재진입)에 다시 잡는다.
/// 브리지와 달리 v13 리셋(순간이동·재활성)에도 다시 잡는다 — 게임에선 리셋이 위치·방향이 바뀐 뒤에 온다.
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

    /// <summary>다음 관측을 처음으로 만든다(구동기 재활성 등).</summary>
    public void Invalidate()
    {
        observed = false;
    }

    /// <summary>이번 프레임 Base Layer 상태를 기록하고, trailing 방향을 다시 잡아야 하면 true.</summary>
    public bool Observe(int stateHash, string clipName, float normalizedTime, bool looping, int resetCount)
    {
        long currentLoop = AssassinHairClipRules.LoopIndex(normalizedTime, looping);
        bool relaunch = !observed
            || stateHash != this.stateHash
            || clipName != this.clipName
            || resetCount != this.resetCount
            || currentLoop != loop
            || normalizedTime < this.normalizedTime - RewindEpsilon;

        observed = true;
        this.stateHash = stateHash;
        this.clipName = clipName;
        this.normalizedTime = normalizedTime;
        this.resetCount = resetCount;
        loop = currentLoop;
        return relaunch;
    }
}
