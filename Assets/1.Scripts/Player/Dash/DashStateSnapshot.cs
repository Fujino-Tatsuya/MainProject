namespace BeaverLobby.Player.Dash
{
    /// <summary>
    /// 서버가 물리·상태 갱신 뒤 남기는 Player 상태 스냅샷(불변). (PLAN §9)
    /// 서버 게임시각 기준이며, 대시 요청 검증 시 요청시각 이전의 가장 가까운 스냅샷을 선택하는 데 쓰인다.
    /// </summary>
    public readonly struct DashStateSnapshot
    {
        public readonly double ServerTime;

        public readonly bool Grounded;
        public readonly bool Dead;
        public readonly bool Soul;
        public readonly bool CrowdControlled;
        public readonly bool LandingProtected;

        public readonly int ChargeCount;
        public readonly double NextChargeReadyTime;
        public readonly uint ChargeEpoch;
        public readonly uint ChargeRevision;

        public DashStateSnapshot(
            double serverTime,
            bool grounded,
            bool dead,
            bool soul,
            bool crowdControlled,
            bool landingProtected,
            int chargeCount,
            double nextChargeReadyTime,
            uint chargeEpoch,
            uint chargeRevision)
        {
            ServerTime = serverTime;
            Grounded = grounded;
            Dead = dead;
            Soul = soul;
            CrowdControlled = crowdControlled;
            LandingProtected = landingProtected;
            ChargeCount = chargeCount;
            NextChargeReadyTime = nextChargeReadyTime;
            ChargeEpoch = chargeEpoch;
            ChargeRevision = chargeRevision;
        }
    }
}
