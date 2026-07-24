using System;

namespace BeaverLobby.Player.Dash
{
    /// <summary>
    /// 서버 과거 상태 Ring Buffer(순수 계산). (PLAN §9)
    ///
    /// - <see cref="Push"/>는 서버 게임시각이 단조 비감소일 때만 저장한다(더 오래된 값은 무시).
    /// - <see cref="TrySelectAtOrBefore"/>는 요청시각 이전(이하)의 가장 가까운 스냅샷을 선택한다.
    /// - 선택된 스냅샷이 요청시각과 freshness 허용값보다 멀면 Receive-time fallback 없이 거부한다.
    /// </summary>
    public sealed class DashSnapshotHistory
    {
        private readonly DashStateSnapshot[] _buffer;
        private int _count;   // 유효 항목 수 (<= Capacity)
        private int _head;    // 다음 기록 위치
        private double _lastTime = double.NegativeInfinity;

        public DashSnapshotHistory(int capacity)
        {
            _buffer = new DashStateSnapshot[Math.Max(1, capacity)];
        }

        public int Capacity => _buffer.Length;
        public int Count => _count;

        /// <summary>
        /// 스냅샷을 저장한다. 서버시각이 마지막 저장보다 과거면(순서 역전) 무시하고 false.
        /// 용량 초과 시 가장 오래된 항목을 덮어쓴다.
        /// </summary>
        public bool Push(in DashStateSnapshot snapshot)
        {
            if (snapshot.ServerTime < _lastTime)
            {
                return false;
            }

            _buffer[_head] = snapshot;
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length)
            {
                _count++;
            }
            _lastTime = snapshot.ServerTime;
            return true;
        }

        /// <summary>
        /// 요청시각 이하의 가장 최신 스냅샷을 선택한다.
        /// 없거나, 선택된 스냅샷이 요청시각보다 freshnessTolerance를 초과해 과거이면 false.
        /// </summary>
        public bool TrySelectAtOrBefore(double requestTime, double freshnessTolerance, out DashStateSnapshot result)
        {
            // 최신 → 과거 순회. 저장이 단조 비감소이므로 요청시각 이하를 처음 만나는 항목이 곧 최댓값(at-or-before).
            for (int k = 0; k < _count; k++)
            {
                int idx = ((_head - 1 - k) % _buffer.Length + _buffer.Length) % _buffer.Length;
                DashStateSnapshot candidate = _buffer[idx];
                if (candidate.ServerTime <= requestTime)
                {
                    if (requestTime - candidate.ServerTime > freshnessTolerance)
                    {
                        result = default;
                        return false;
                    }

                    result = candidate;
                    return true;
                }
            }

            result = default;
            return false;
        }
    }
}
