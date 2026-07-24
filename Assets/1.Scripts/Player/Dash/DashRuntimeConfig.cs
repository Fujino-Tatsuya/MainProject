namespace BeaverLobby.Player.Dash
{
    /// <summary>
    /// 씬 초기화 시 <see cref="PlayerDashData"/>에서 복사·검증된 불변 런타임 설정. (PLAN §6)
    ///
    /// 설정 오류가 있으면 <see cref="DashEnabled"/>=false로 만들되 소비자가 안전하게 동작하도록
    /// 값은 안전 범위로 보정해 담는다(로딩은 계속, 대시만 비활성).
    /// v1 세션 중 값은 바뀌지 않는다.
    ///
    /// 이동/추락/공중 물리 등 나머지 튜닝 값은 해당 Workstream(W3/W10)에서 필드를 추가한다.
    /// </summary>
    internal readonly struct DashRuntimeConfig
    {
        public readonly bool DashEnabled;
        public readonly double DashSpeed;
        public readonly double DashDuration;
        public readonly int MaxCharge;
        public readonly double RechargeDuration;
        public readonly int SnapshotCapacity;
        public readonly double SnapshotFreshnessTolerance;

        private DashRuntimeConfig(
            bool dashEnabled,
            double dashSpeed,
            double dashDuration,
            int maxCharge,
            double rechargeDuration,
            int snapshotCapacity,
            double snapshotFreshnessTolerance)
        {
            DashEnabled = dashEnabled;
            DashSpeed = dashSpeed;
            DashDuration = dashDuration;
            MaxCharge = maxCharge;
            RechargeDuration = rechargeDuration;
            SnapshotCapacity = snapshotCapacity;
            SnapshotFreshnessTolerance = snapshotFreshnessTolerance;
        }

        /// <summary>
        /// 원본 값을 검증해 설정을 만든다. 모든 값이 유효하면 <see cref="DashEnabled"/>=true.
        /// 하나라도 비정상이면 값은 안전 범위로 보정하되 DashEnabled=false.
        /// </summary>
        public static DashRuntimeConfig Create(
            double dashSpeed,
            double dashDuration,
            int maxCharge,
            double rechargeDuration,
            int snapshotCapacity,
            double snapshotFreshnessTolerance)
        {
            bool valid =
                IsPositiveFinite(dashSpeed) &&
                IsPositiveFinite(dashDuration) &&
                maxCharge >= 1 &&
                IsNonNegativeFinite(rechargeDuration) &&
                snapshotCapacity >= 1 &&
                IsNonNegativeFinite(snapshotFreshnessTolerance);

            // 안전 보정값(소비자 크래시 방지). 유효하면 원본 그대로.
            double safeSpeed = IsPositiveFinite(dashSpeed) ? dashSpeed : 0.0;
            double safeDuration = IsPositiveFinite(dashDuration) ? dashDuration : 0.0;
            int safeMaxCharge = maxCharge >= 1 ? maxCharge : 1;
            double safeRecharge = IsNonNegativeFinite(rechargeDuration) ? rechargeDuration : 0.0;
            int safeCapacity = snapshotCapacity >= 1 ? snapshotCapacity : 1;
            double safeFreshness = IsNonNegativeFinite(snapshotFreshnessTolerance) ? snapshotFreshnessTolerance : 0.0;

            return new DashRuntimeConfig(
                valid, safeSpeed, safeDuration, safeMaxCharge, safeRecharge, safeCapacity, safeFreshness);
        }

        private static bool IsPositiveFinite(double v)
            => !double.IsNaN(v) && !double.IsInfinity(v) && v > 0.0;

        private static bool IsNonNegativeFinite(double v)
            => !double.IsNaN(v) && !double.IsInfinity(v) && v >= 0.0;
    }
}
