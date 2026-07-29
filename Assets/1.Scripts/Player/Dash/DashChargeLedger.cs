using System;

namespace BeaverLobby.Player.Dash
{
    /// <summary>
    /// 서버 권한 대시 충전 장부(순수 계산). NetworkClock/MonoBehaviour를 참조하지 않고
    /// 모든 시간 입력을 <c>double now</c>로 받는다. (PLAN §10)
    ///
    /// 불변식:
    /// - 충전은 순차 회복한다.
    /// - 추가 소비가 진행 중인 회복 타이머를 초기화하지 않는다.
    /// - 장기간 조회가 없어 여러 완료시각이 지났으면 경과한 횟수만큼 한 번에 따라잡는다(catch-up).
    /// - 소비·회복은 <see cref="Revision"/>만 증가시킨다.
    /// - 강제 초기화(생존 복귀/부활)는 <see cref="Epoch"/>을 증가시키고 Revision을 0으로 되돌린다.
    ///
    /// 항상 <c>Count &lt; MaxCharge</c>인 동안에는 유효한 회복 타이머(<see cref="NextReadyTime"/>)가 존재한다.
    /// </summary>
    public sealed class DashChargeLedger
    {
        private readonly int _maxCharge;
        private readonly double _rechargeDuration;

        private int _count;
        private double _nextReadyTime; // Count < MaxCharge 일 때만 유효한 절대 완료시각; 만충이면 +Inf
        private uint _epoch;
        private uint _revision;

        public DashChargeLedger(int maxCharge, double rechargeDuration, int initialCount, double now)
        {
            _maxCharge = Math.Max(1, maxCharge);
            _rechargeDuration = Math.Max(0.0, rechargeDuration);
            _count = Clamp(initialCount, 0, _maxCharge);
            _epoch = 0u;
            _revision = 0u;
            _nextReadyTime = _count >= _maxCharge ? double.PositiveInfinity : now + _rechargeDuration;
        }

        public int MaxCharge => _maxCharge;
        public int Count => _count;

        /// <summary>다음 충전 완료 절대시각. 만충이면 <see cref="double.PositiveInfinity"/>.</summary>
        public double NextReadyTime => _nextReadyTime;

        /// <summary>강제 초기화 세대. 과거 요청 Snapshot과 Epoch이 다르면 장부를 덮어쓰지 않는 데 사용.</summary>
        public uint Epoch => _epoch;

        /// <summary>소비·회복 개정 번호(Epoch 내 단조 증가).</summary>
        public uint Revision => _revision;

        public bool IsFull => _count >= _maxCharge;
        public bool HasCharge => _count > 0;

        /// <summary>경과한 완료시각만큼 충전을 채운다(catch-up). 변화가 있으면 Revision 증가.</summary>
        public void Advance(double now)
        {
            if (_count >= _maxCharge)
            {
                return;
            }

            bool changed = false;

            if (_rechargeDuration <= 0.0)
            {
                // 회복시간 0 → 완료시각 도달 시 즉시 만충(무한 루프 방지).
                if (now >= _nextReadyTime)
                {
                    _count = _maxCharge;
                    _nextReadyTime = double.PositiveInfinity;
                    changed = true;
                }
            }
            else
            {
                while (_count < _maxCharge && now >= _nextReadyTime)
                {
                    _count++;
                    changed = true;
                    _nextReadyTime = _count >= _maxCharge
                        ? double.PositiveInfinity
                        : _nextReadyTime + _rechargeDuration;
                }
            }

            if (changed)
            {
                _revision++;
            }
        }

        /// <summary>충전 1개 소비. 성공 시 true. 진행 중인 회복 타이머는 보존한다.</summary>
        public bool TryConsume(double now)
        {
            Advance(now);

            if (_count <= 0)
            {
                return false;
            }

            bool wasFull = _count >= _maxCharge;
            _count--;

            // 만충 상태에서 소비하면 이제부터 회복 시작.
            // 이미 회복 중이었다면 진행 중인 타이머를 보존한다.
            if (wasFull)
            {
                _nextReadyTime = now + _rechargeDuration;
            }

            _revision++;
            return true;
        }

        /// <summary>
        /// 생존 복귀/부활 시 강제 초기화. count로 설정하고 다음 충전 진행도는 0부터 시작.
        /// Epoch을 증가시키고 Revision을 0으로 되돌린다.
        /// </summary>
        public void ForceReset(int count, double now)
        {
            _count = Clamp(count, 0, _maxCharge);
            _nextReadyTime = _count >= _maxCharge ? double.PositiveInfinity : now + _rechargeDuration;
            _epoch++;
            _revision = 0u;
        }

        /// <summary>
        /// 서버 권한 충전 스냅샷으로 정합한다(오너 예측 → 권한 동기화). (PLAN §10)
        /// 과거 Epoch/Revision 스냅샷은 최신 장부를 덮지 않는다. 채택 시 지역 회복 타이머를 now 기준으로 다시 시작한다.
        /// </summary>
        public void SyncToAuthoritative(int count, uint epoch, uint revision, double now)
        {
            if (epoch < _epoch)
            {
                return; // 더 최신 강제 초기화 장부를 과거 스냅샷으로 덮지 않는다.
            }

            if (epoch == _epoch && SequenceLess(revision, _revision))
            {
                return; // 같은 Epoch 안에서 더 오래된 Revision은 무시.
            }

            _epoch = epoch;
            _revision = revision;
            _count = Clamp(count, 0, _maxCharge);
            _nextReadyTime = _count >= _maxCharge ? double.PositiveInfinity : now + _rechargeDuration;
        }

        /// <summary>
        /// 서버가 요청을 <b>거부</b>했을 때 오너의 예측 소비를 되돌린다. Revision 비교를 하지 않는다.
        ///
        /// ⚠️ <see cref="SyncToAuthoritative"/>로는 이 경우가 항상 무시된다 — 오너는 입력 순간
        /// 예측 소비로 Revision을 이미 올렸고, 거부한 서버는 소비를 안 해 Revision이 더 낮다.
        /// 그래서 "과거 스냅샷" 가드에 걸려 권한 충전값이 버려지고, 오너만 충전 1개와
        /// 재충전 2초를 잃었다(대시는 안 나가고 쿨타임만 두 배가 되는 증상의 실제 원인).
        ///
        /// <paramref name="remainingToReady"/>는 오너 도메인으로 환산한 다음 충전까지 남은 시간.
        /// 비정상/범위 밖 값은 [0, rechargeDuration]으로 보정한다.
        /// </summary>
        public void ForceAdoptAuthoritative(int count, uint epoch, uint revision, double now, double remainingToReady)
        {
            _epoch = epoch;
            _revision = revision;
            _count = Clamp(count, 0, _maxCharge);

            if (_count >= _maxCharge)
            {
                _nextReadyTime = double.PositiveInfinity;
                return;
            }

            double remaining = remainingToReady;
            if (double.IsNaN(remaining) || remaining < 0.0)
            {
                remaining = 0.0;
            }
            else if (remaining > _rechargeDuration)
            {
                remaining = _rechargeDuration;
            }

            _nextReadyTime = now + remaining;
        }

        private static bool SequenceLess(uint a, uint b)
            => unchecked((int)(a - b)) < 0;

        private static int Clamp(int value, int min, int max)
            => value < min ? min : (value > max ? max : value);
    }
}
