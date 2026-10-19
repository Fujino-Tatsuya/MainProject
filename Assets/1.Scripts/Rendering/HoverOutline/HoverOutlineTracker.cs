/// <summary>
/// 호버 대상 전환 규칙 — 한 번에 하나. 대상이 바뀐 프레임에만 "이전 것 떼고 새 것 붙이기"가 일어난다.
/// 비교는 참조 동일성이다: 디스폰된 대상(유니티 null)에서 null 로 가는 것도 전환으로 본다 — 그래야 겹 목록이 비워진다.
/// </summary>
public sealed class HoverOutlineTracker
{
    public object Current { get; private set; }

    /// <summary>대상이 바뀌었으면 true 와 함께 직전 대상을 돌려준다.</summary>
    public bool Set(object next, out object previous)
    {
        previous = Current;
        if (ReferenceEquals(previous, next))
            return false;

        Current = next;
        return true;
    }
}
