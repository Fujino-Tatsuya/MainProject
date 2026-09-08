using UnityEngine;

/// <summary>
/// 인터럽트 카운터 창 하나의 수명 — 열림 · 시간 감소 · 인터럽트 1회 소비.
///
/// 순수 C# 이다(MonoBehaviour 아님). 창의 규칙을 EditMode 에서 순서 조합까지 고정하기 위해서다.
/// 애니메이터 정지·데미지·그로기는 이 클래스가 하지 않는다 — 호출측(몬스터)의 몫이다.
///
/// 🔴 확정한 규칙 세 개(2026-09-08):
/// <list type="number">
/// <item><b>길이가 0 이하면 아예 열지 않는다.</b> "열렸지만 이미 만료된 창"을 만들면 같은 프레임의
///       인터럽트가 소비되는지가 호출 순서에 따라 달라진다 — 저작값 0 은 "카운터 없음"으로 읽는다.</item>
/// <item><b>인터럽트는 창 하나에 한 번만</b> 소비된다. 두 명이 같은 창에 인터럽트를 넣어도 성공은 하나다.</item>
/// <item><b>성공하면 창이 즉시 닫힌다.</b> 이후 히트는 카운터로 인정되지 않는다(23호 규약과 같다).</item>
/// </list>
/// </summary>
public sealed class CounterWindow
{
    float _remaining;

    /// <summary>창이 열려 있는가. 이때 들어온 인터럽트만 카운터로 인정된다.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>이 창에서 인터럽트가 이미 소비됐는가(성공했는가).</summary>
    public bool InterruptConsumed { get; private set; }

    /// <summary>남은 시간(초). 닫혀 있으면 0.</summary>
    public float Remaining => IsOpen ? _remaining : 0f;

    /// <param name="duration">창 길이(초). <b>0 이하면 창을 열지 않는다.</b></param>
    public void Open(float duration)
    {
        Close();
        if (duration <= 0f) return;

        IsOpen = true;
        _remaining = duration;
    }

    /// <summary>
    /// 시간을 흘린다. <b>이번 호출에서 만료됐으면 true</b> — 호출측이 "실패 확정"을 한 번만 처리하도록.
    /// 만료되면 창은 닫힌다. 닫힌 창에 대고 불러도 false 다(멱등).
    /// </summary>
    /// <param name="deltaTime">서버 틱 간격. 음수는 0 으로 눌러 시간이 되감기지 않게 한다.</param>
    public bool TickAndDetectExpiry(float deltaTime)
    {
        if (!IsOpen) return false;

        _remaining -= Mathf.Max(0f, deltaTime);
        if (_remaining > 0f) return false;

        Close();
        return true;
    }

    /// <summary>
    /// 인터럽트를 소비한다. 창이 열려 있고 아직 아무도 성공하지 않았을 때만 true.
    /// true 를 돌려준 뒤에는 창이 닫히고 <see cref="InterruptConsumed"/> 가 선다.
    /// </summary>
    public bool TryConsumeInterrupt()
    {
        if (!IsOpen) return false;

        InterruptConsumed = true;
        IsOpen = false;
        _remaining = 0f;
        return true;
    }

    /// <summary>창을 비운다. 공격 취소·사망·디스폰에서 호출한다. 멱등이다.</summary>
    public void Close()
    {
        IsOpen = false;
        InterruptConsumed = false;
        _remaining = 0f;
    }
}
