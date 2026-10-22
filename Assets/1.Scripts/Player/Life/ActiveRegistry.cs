using System.Collections.Generic;

/// <summary>
/// 켜져 있는 인스턴스 목록. 매 판정마다 씬 전체를 뒤지는 <c>FindObjectsByType</c> 대신 쓴다(할 일 69).
/// 소유자가 OnEnable 에서 <see cref="Add"/>, OnDisable 에서 <see cref="Remove"/> 한다 —
/// 그래서 목록 = <c>FindObjectsInactive.Exclude</c> 로 찾던 집합(비활성 GameObject 제외)과 같다.
/// 순서는 보장하지 않는다. 순회는 <see cref="Items"/> 를 인덱스로 돌면 할당이 없다.
/// </summary>
public sealed class ActiveRegistry<T> where T : class
{
    private readonly List<T> _items = new List<T>();

    public IReadOnlyList<T> Items => _items;

    /// <summary>이미 있으면 무시한다(같은 인스턴스가 두 번 세지지 않는다).</summary>
    public void Add(T item)
    {
        if (item == null || _items.Contains(item))
            return;
        _items.Add(item);
    }

    public void Remove(T item) => _items.Remove(item);

    /// <summary>도메인 리로드를 끈 Play 진입에서 지난 세션의 항목을 버린다.</summary>
    public void Clear() => _items.Clear();
}
