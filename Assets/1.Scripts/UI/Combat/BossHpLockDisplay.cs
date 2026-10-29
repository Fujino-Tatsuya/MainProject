using UnityEngine;

/// <summary>
/// 보스 HP 바 잠김 표시(할 일 147) — 보스 무적(<c>TwentyThreeBoss.HpLocked</c>) → 바 색 덮개 세기·자물쇠 표시. 순수 계산(EditMode 테스트 대상).
/// 잠기면 즉시 덮개 1(밝은 회색)·자물쇠 보임. 풀리면 <c>releaseSeconds</c>(최대 <see cref="MaxReleaseSeconds"/>) 동안
/// 덮개가 0 으로 걷히고 자물쇠가 커지며 사라진다. 0 이면 즉시 원래 색·자물쇠 숨김. 해제 중 다시 잠기면 바로 잠김으로.
/// </summary>
public sealed class BossHpLockDisplay
{
    /// <summary>해제 애니 상한(초) — 사양 0.2초 이하.</summary>
    public const float MaxReleaseSeconds = 0.2f;

    public struct Frame
    {
        public float Tint01;      // 잠김 색 덮개 세기(1 = 잠김 색 그대로, 0 = 원래 HP 색)
        public bool ShowLock;     // 자물쇠를 그린다
        public float LockAlpha;   // 자물쇠 불투명도
        public float LockScale;   // 자물쇠 배율(해제 중 1 → 1 + releasePop)
    }

    private bool _locked;
    private float _release = -1f;   // 해제 경과(초). < 0 = 해제 애니 없음

    /// <summary>지금 잠김(무적) 상태로 그리는 중인가.</summary>
    public bool Locked => _locked;

    /// <summary>보스가 바뀌거나 바가 숨을 때 — 애니 없이 원래 상태로.</summary>
    public void Reset()
    {
        _locked = false;
        _release = -1f;
    }

    /// <param name="locked">이번 프레임 보스 무적 여부(복제값).</param>
    /// <param name="releaseSeconds">해제 애니 시간(초). 0 이하 = 즉시. <see cref="MaxReleaseSeconds"/> 로 자른다.</param>
    /// <param name="releasePop">해제 끝에서 자물쇠가 커지는 정도(0 = 크기 유지).</param>
    public Frame Evaluate(bool locked, float deltaTime, float releaseSeconds, float releasePop = 0.3f)
    {
        float duration = Mathf.Clamp(releaseSeconds, 0f, MaxReleaseSeconds);

        if (locked)
        {
            _locked = true;
            _release = -1f;
            return new Frame { Tint01 = 1f, ShowLock = true, LockAlpha = 1f, LockScale = 1f };
        }

        if (_locked)
        {
            _locked = false;
            _release = duration > 0f ? 0f : -1f;
        }
        else if (_release >= 0f)
        {
            _release += Mathf.Max(0f, deltaTime);
        }

        if (_release < 0f || _release >= duration)
        {
            _release = -1f;
            return new Frame { Tint01 = 0f, ShowLock = false, LockAlpha = 0f, LockScale = 1f };
        }

        float t = _release / duration;   // 0 → 1
        float remain = 1f - t;
        return new Frame
        {
            Tint01 = remain,
            ShowLock = true,
            LockAlpha = remain,
            LockScale = 1f + Mathf.Max(0f, releasePop) * t,
        };
    }
}
