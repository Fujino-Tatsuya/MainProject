using UnityEngine;

namespace BeaverLobby.Player.Dash
{
    /// <summary>대쉬 간파 판정 시점 규칙의 결과. (할 일 115)</summary>
    public enum DashInterruptResolveTiming
    {
        Wait,        // 아직 판정하지 않는다(대시 중이거나 평소 Hit 전)
        HoldHit,     // 대시 중 Hit 이 왔다 — 대시 끝까지 보류
        ResolveNow   // 지금 판정
    }

    /// <summary>대시 중 우클릭 한 번을 어떻게 다룰지(오너 입력 주체). (할 일 115)</summary>
    public enum DashInterruptInputDecision
    {
        NoPress,          // 누른 입력이 없다
        OutOfWindow,      // 창 밖 — 손대지 않는다(선입력 버퍼 규칙 그대로, 대시 끝난 뒤 처리)
        NotInterruptSkill,// 우클릭 슬롯이 간파 스킬이 아니다(팔라딘 E 대체 등) — 손대지 않는다
        ConsumeOnly,      // 이 대시에서 이미 요청했다 — 버퍼에서 지우기만(재요청 없음)
        RejectByEffect,   // 차단 효과(BlocksSkill·BlocksInterrupt) — 기존 차단 처리
        RejectByCooldown, // 쿨타임 — 거부 피드백 1회 + 버림
        Request           // 대쉬 간파 요청
    }

    /// <summary>
    /// 대쉬 간파(대시 시작 직후 짧은 창 안에 우클릭 → 대시하며 간파 준비 → 대시 끝 지점에서 판정)의 순수 규칙. (할 일 115)
    /// 시각은 모두 같은 시간축의 초. 창·허용오차 값은 <see cref="PlayerDashData"/>.
    /// 판정 시각 = max(대시 끝, 평소 Hit) — 평소 간파보다 빠르지 않고, 대시가 짧으면 남은 준비 시간만큼 미룬다.
    /// </summary>
    public static class DashInterruptRules
    {
        /// <summary>창이 0 이하면 기능이 꺼진 것이다.</summary>
        public static bool IsEnabled(float window) => window > 0f;

        /// <summary>
        /// 누른 시각이 대시 시작 후 창 안인가(경계 포함). 대시 시작 전에 누른 입력은 창 밖.
        /// 누른 시각 = 이번 프레임 press 또는 선입력 버퍼 기록 시각.
        /// </summary>
        public static bool IsPressInWindow(float dashStartTime, float pressTime, float window)
        {
            if (!IsEnabled(window))
                return false;

            float elapsed = pressTime - dashStartTime;
            return elapsed >= 0f && elapsed <= window;
        }

        /// <summary>
        /// 대시 중 우클릭 판단 순서: 입력 없음 → 창 밖 → 간파 스킬 아님 → 이미 요청 → 차단 효과 → 쿨타임 → 요청.
        /// 창 밖·간파 스킬 아님은 입력을 그대로 둔다(<see cref="LeavesInput"/>).
        /// </summary>
        public static DashInterruptInputDecision EvaluateInput(
            bool hasPress, float dashStartTime, float pressTime, float window,
            bool isInterruptSkill, bool alreadyRequested, bool blockedByEffect, bool onCooldown)
        {
            if (!hasPress)
                return DashInterruptInputDecision.NoPress;
            if (!IsPressInWindow(dashStartTime, pressTime, window))
                return DashInterruptInputDecision.OutOfWindow;
            if (!isInterruptSkill)
                return DashInterruptInputDecision.NotInterruptSkill;
            if (alreadyRequested)
                return DashInterruptInputDecision.ConsumeOnly;
            if (blockedByEffect)
                return DashInterruptInputDecision.RejectByEffect;
            return onCooldown ? DashInterruptInputDecision.RejectByCooldown : DashInterruptInputDecision.Request;
        }

        /// <summary>Dash 상태가 이 입력을 건드리지 않는가 — 기존 선입력 버퍼 규칙에 맡긴다.</summary>
        public static bool LeavesInput(DashInterruptInputDecision decision) =>
            decision == DashInterruptInputDecision.NoPress ||
            decision == DashInterruptInputDecision.OutOfWindow ||
            decision == DashInterruptInputDecision.NotInterruptSkill;

        /// <summary>
        /// 대시가 끝날 때(시간 만료·유닛 막힘) 간파 Skill 상태로 바로 넘어가는가 — 실린 간파가 아직 실행 중일 때만.
        /// 아니면 기존대로 Idle/Move.
        /// </summary>
        public static bool EndsIntoSkill(bool hasAttached, bool attachedStillActive) =>
            hasAttached && attachedStillActive;

        /// <summary>
        /// 서버 승인 창 —서버 기준 대시 경과가 창 + 허용오차 안인가(경계 포함).
        /// 원격 오너는 서버 대시도 요청 도착 시 시작하므로 경과가 비슷하다. 허용오차 음수는 0.
        /// </summary>
        public static bool IsServerElapsedAccepted(float serverDashElapsed, float window, float tolerance)
        {
            if (!IsEnabled(window))
                return false;

            return serverDashElapsed >= 0f && serverDashElapsed <= window + Mathf.Max(0f, tolerance);
        }

        /// <summary>
        /// 판정 시점 규칙. 대시 중 Hit 이 오면 보류, 대시가 끝났으면 보류된 Hit 은 즉시 판정,
        /// 아직 Hit 전이면 Hit 을 기다린다.
        /// </summary>
        /// <param name="dashEnded">대시가 끝났다(시간 만료·조기 종료 모두).</param>
        /// <param name="hitReached">평소 Hit(클립 이벤트·타이머)이 이미 왔다(이번 프레임 포함).</param>
        public static DashInterruptResolveTiming EvaluateResolveTiming(bool dashEnded, bool hitReached)
        {
            if (!hitReached)
                return DashInterruptResolveTiming.Wait;
            return dashEnded ? DashInterruptResolveTiming.ResolveNow : DashInterruptResolveTiming.HoldHit;
        }

        /// <summary>판정 시각 = max(대시 끝, 평소 Hit).</summary>
        public static float GetResolveTime(float dashEndTime, float normalHitTime) =>
            Mathf.Max(dashEndTime, normalHitTime);

        /// <summary>
        /// 예상 대시 끝 오프셋 = 방향 × 속도 × 남은 시간(음수 시간 = 0). 방향은 그대로 쓴다(호출자가 정규화).
        /// 시도 사건 예측 위치(지금 위치 + 이 값)에 쓴다.
        /// </summary>
        public static Vector3 GetPredictedEndOffset(Vector3 direction, float speed, float remainingTime) =>
            direction * (speed * Mathf.Max(0f, remainingTime));
    }
}
