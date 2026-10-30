using System;

/// <summary>
/// 특성 창 배경 블러 텍스처의 수명(할 일 165 후속 A안) — 열기 → 캡처 대기 → 결과 보유 → 닫기 반납. 렌더링을 모르는 순수 규칙이라 EditMode 로 검사한다.
/// - <see cref="Open"/> 은 캡처 표(ticket)를 준다. 캡처가 끝나면 그 표로 <see cref="Complete"/> — 그 사이 닫혔거나 다시 열렸으면 결과를 바로 반납한다.
/// - <see cref="Close"/> 는 보유 결과를 반납하고 대기 중인 캡처를 무효로 만든다. 몇 번 불러도 안전하다.
/// 결과는 한 번에 하나만 들고 있다 — 열린 채 다시 열어도 새 캡처를 받지 않는다.
/// </summary>
public sealed class TraitBackdropLifetime<T> where T : class
{
    private readonly Action<T> release;
    private int ticket;

    public TraitBackdropLifetime(Action<T> release)
    {
        this.release = release ?? throw new ArgumentNullException(nameof(release));
    }

    /// <summary>창이 열린 상태(캡처 대기 포함).</summary>
    public bool IsOpen { get; private set; }

    /// <summary>열렸지만 아직 캡처 결과가 오지 않았다 — 창을 아직 보이지 않는다.</summary>
    public bool IsPending { get; private set; }

    /// <summary>지금 들고 있는 블러 결과. 없으면 null.</summary>
    public T Current { get; private set; }

    /// <summary>창을 연다. 새 캡처가 필요하면 표(1 이상)를, 이미 열려 있으면 0 을 준다.</summary>
    public int Open()
    {
        if (IsOpen)
            return 0;

        IsOpen = true;
        IsPending = true;
        return ++ticket;
    }

    /// <summary>이 표의 캡처를 아직 기다리는 중인가 — 캡처 직전에 확인해 헛캡처를 피한다.</summary>
    public bool IsWaitingFor(int captureTicket) => IsOpen && IsPending && captureTicket != 0 && captureTicket == ticket;

    /// <summary>캡처 결과를 넘긴다. 받아들이면 true(이제 <see cref="Current"/>), 이미 늦었으면 결과를 바로 반납하고 false.
    /// 결과가 null(캡처 실패)이어도 대기는 끝난다 — 창은 블러 없이 보인다.</summary>
    public bool Complete(int captureTicket, T result)
    {
        if (!IsWaitingFor(captureTicket))
        {
            if (result != null)
                release(result);
            return false;
        }

        IsPending = false;
        Current = result;
        return true;
    }

    /// <summary>창을 닫는다 — 결과 반납, 대기 중 캡처 무효.</summary>
    public void Close()
    {
        IsOpen = false;
        IsPending = false;
        ticket++;
        if (Current == null)
            return;

        T held = Current;
        Current = null;
        release(held);
    }
}
