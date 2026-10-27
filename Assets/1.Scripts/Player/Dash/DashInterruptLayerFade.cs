using UnityEngine;

namespace BeaverLobby.Player.Dash
{
    /// <summary>
    /// 대쉬 간파 상체 레이어(DashInterruptUpper) 가중치 전환의 순수 계산. (할 일 115 §11)
    /// 시작 가중치에서 목표까지 <c>duration</c> 초 동안 선형으로 간다. 진행 중 다시 시작하면 지금 가중치에서 이어 간다(튀지 않게).
    /// 시간 0 이하는 즉시 목표.
    /// </summary>
    public struct DashInterruptLayerFade
    {
        private float from;
        private float to;
        private float duration;
        private float elapsed;
        private bool active;

        /// <summary>전환이 진행 중이다(끝나면 false — 목표 가중치에 머문다).</summary>
        public bool IsActive => active;

        public float Target => to;

        /// <summary>지금 가중치(0~1).</summary>
        public float Weight => duration <= 0f || elapsed >= duration
            ? to
            : Mathf.Lerp(from, to, elapsed / duration);

        public static DashInterruptLayerFade Start(float currentWeight, float targetWeight, float duration) =>
            new DashInterruptLayerFade
            {
                from = Mathf.Clamp01(currentWeight),
                to = Mathf.Clamp01(targetWeight),
                duration = Mathf.Max(0f, duration),
                elapsed = 0f,
                active = true
            };

        /// <summary>즉시 이 가중치에 고정(진행 없음) — 취소용.</summary>
        public static DashInterruptLayerFade Hold(float weight) =>
            new DashInterruptLayerFade { from = Mathf.Clamp01(weight), to = Mathf.Clamp01(weight) };

        /// <summary>시간을 진행하고 이번 가중치를 돌려준다. 목표에 닿으면 비활성이 된다.</summary>
        public float Advance(float deltaTime)
        {
            if (!active)
                return to;

            elapsed += Mathf.Max(0f, deltaTime);
            if (duration <= 0f || elapsed >= duration)
                active = false;
            return Weight;
        }

        /// <summary>
        /// 인수인계 때 0층이 시작할 정규화 시각 — 상체 레이어가 그 상태에 있으면 그 시각(0~1 로 자름, 한 번 재생 클립 기준),
        /// 아니면(아직 시작 전·다른 상태) 0.
        /// </summary>
        public static float HandoffNormalizedTime(bool upperInState, float upperNormalizedTime) =>
            upperInState ? Mathf.Clamp01(upperNormalizedTime) : 0f;
    }
}
