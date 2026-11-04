/// <summary>
/// [서버] 제압 간파 표식(할 일 179) — 대상(23호)이 간파 성공으로 제압 칸 0 에 닿은 순간 <see cref="Mark"/>, 같은 판정 호출의 성공 사건이 <see cref="Consume"/>.
/// 대상 수신과 성공 사건은 같은 프레임 같은 호출 안이라 프레임 번호로 묶는다 — 다른 프레임에 남은 표식은 버린다(판정 코드 무수정).
/// 표식은 슬로우 Start 이벤트 비트 0 으로 전 피어에 가서 연출 단계 = 제압(11-04 부터 일반·대쉬와 같은 V3 — 할 일 211).
/// </summary>
public sealed class SuppressInterruptMark
{
    private int _frame = int.MinValue;

    public void Mark(int frame) => _frame = frame;

    /// <summary>이 프레임에 표식이 있었는가 — 읽으면 비운다.</summary>
    public bool Consume(int frame)
    {
        bool marked = _frame == frame;
        _frame = int.MinValue;
        return marked;
    }
}
