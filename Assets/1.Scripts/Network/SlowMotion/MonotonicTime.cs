/// <summary>
/// "지금까지 돌려준 최댓값" 단조 클램프(PLAN-interrupt-slowmo R4).
///
/// 클라는 서버의 슬로우 Start 를 편도 지연만큼 늦게 받는다. 그 사이 이미 돌려준 GameNow 는 슬로우가 없던 값이라,
/// 받는 순간 과거 시각부터 손실이 소급되어 GameNow 가 뒤로 간다. 되감긴 시각은 상태이상 지속시간이 늘어나는 식의
/// 버그가 되므로, 실제 값이 최댓값을 다시 넘을 때까지 시계를 멈춘다(평평하게) — 되감지 않는다.
///
/// 순수 값 타입이다. 세션이 바뀌면 시계가 0 부터 다시 가므로 <see cref="Reset"/> 해야 한다.
/// </summary>
public struct MonotonicTime
{
    bool _has;
    double _max;

    /// <summary>값을 넣고 단조 클램프된 값을 받는다. NaN 은 기록하지 않고 지금까지의 최댓값(없으면 NaN)을 돌려준다.</summary>
    public double Next(double value)
    {
        if (double.IsNaN(value))
            return _has ? _max : value;

        if (!_has || value > _max)
        {
            _max = value;
            _has = true;
        }

        return _max;
    }

    public void Reset()
    {
        _has = false;
        _max = 0.0;
    }
}
