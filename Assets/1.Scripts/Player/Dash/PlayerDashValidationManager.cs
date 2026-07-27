using System.Collections.Generic;
using UnityEngine;

namespace BeaverLobby.Player.Dash
{
    /// <summary>서버 검증 응답 DTO(불변). 컨트롤러가 이 값으로 ClientRpc를 채운다. (PLAN §9)</summary>
    public readonly struct DashServerResponse
    {
        public readonly bool IsApproved;
        public readonly DashRejectReason Reason;
        public readonly double RemainingServerDuration;
        public readonly bool WasInterruptedByServerState;
        public readonly int AuthoritativeChargeCount;
        public readonly double NextChargeReadyServerTime;
        public readonly uint ChargeEpoch;
        public readonly uint ChargeRevision;

        public DashServerResponse(
            bool approved, DashRejectReason reason, double remaining, bool interrupted,
            int chargeCount, double nextReady, uint epoch, uint revision)
        {
            IsApproved = approved;
            Reason = reason;
            RemainingServerDuration = remaining;
            WasInterruptedByServerState = interrupted;
            AuthoritativeChargeCount = chargeCount;
            NextChargeReadyServerTime = nextReady;
            ChargeEpoch = epoch;
            ChargeRevision = revision;
        }
    }

    /// <summary>
    /// 서버 전용 대시 검증 매니저. (PLAN §6, §9)
    /// MainGame 기준 씬에 정확히 하나 배치하는 일반 MonoBehaviour. NetworkObject 아님. 파일 I/O 없음.
    /// Player별 Snapshot 이력·서버 권한 충전 장부·멱등 캐시를 유지한다.
    /// RTT/serverNow/현재상태는 호출자(PlayerDashController)가 주입한다(NGO 비의존).
    /// </summary>
    public sealed class PlayerDashValidationManager : MonoBehaviour
    {
        public static PlayerDashValidationManager Instance { get; private set; }

        private sealed class PlayerEntry
        {
            public ulong OwnerClientId;
            public DashRuntimeConfig Config;
            public DashSnapshotHistory History;
            public DashChargeLedger Ledger;

            public bool HasLastRequest;
            public uint LastRequestId;
            public DashServerResponse LastResponse;
        }

        private readonly Dictionary<ulong, PlayerEntry> _players = new Dictionary<ulong, PlayerEntry>();
        private bool _warnedDuplicate;

        public int RegisteredPlayerCount => _players.Count;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                if (!_warnedDuplicate)
                {
                    _warnedDuplicate = true;
                    Debug.LogWarning("[DashAlert] PlayerDashValidationManager가 씬에 둘 이상 존재합니다. 최초 인스턴스만 사용합니다.", this);
                }

                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void RegisterPlayer(ulong networkObjectId, ulong ownerClientId, in DashRuntimeConfig config, double now)
        {
            if (_players.ContainsKey(networkObjectId))
            {
                return;
            }

            _players[networkObjectId] = new PlayerEntry
            {
                OwnerClientId = ownerClientId,
                Config = config,
                History = new DashSnapshotHistory(Mathf.Max(1, config.SnapshotCapacity)),
                Ledger = new DashChargeLedger(config.MaxCharge, config.RechargeDuration, config.MaxCharge, now),
                HasLastRequest = false,
                LastRequestId = 0u,
                LastResponse = default,
            };
        }

        public void DeregisterPlayer(ulong networkObjectId)
        {
            _players.Remove(networkObjectId);
        }

        /// <summary>서버 tick마다 충전 장부를 진행시키고 현재 상태 스냅샷을 저장한다.</summary>
        public void CaptureSnapshot(
            ulong networkObjectId, double serverTime,
            bool grounded, bool dead, bool soul, bool crowdControlled, bool landingProtected)
        {
            if (!_players.TryGetValue(networkObjectId, out PlayerEntry e))
            {
                return;
            }

            e.Ledger.Advance(serverTime);
            e.History.Push(new DashStateSnapshot(
                serverTime, grounded, dead, soul, crowdControlled, landingProtected,
                e.Ledger.Count, e.Ledger.NextReadyTime, e.Ledger.Epoch, e.Ledger.Revision));
        }

        /// <summary>충전만 조회(거부/최신 동기화 응답에 사용).</summary>
        private static DashServerResponse ChargeOnly(bool approved, DashRejectReason reason, PlayerEntry e)
        {
            return new DashServerResponse(
                approved, reason, 0.0, false,
                e.Ledger.Count, e.Ledger.NextReadyTime, e.Ledger.Epoch, e.Ledger.Revision);
        }

        /// <summary>
        /// 대시 요청을 검증하고(멱등), 승인 시 서버 권한 충전을 소비한 뒤 응답을 만든다. (PLAN §9)
        /// </summary>
        public DashServerResponse ValidateRequest(
            ulong networkObjectId, ulong senderClientId, uint requestId,
            double clientLocalTime, double directionX, double directionZ,
            double serverNow, double serverRtt, bool rttAvailable,
            bool currentDead, bool currentSoul, bool currentCrowdControlled)
        {
            if (!_players.TryGetValue(networkObjectId, out PlayerEntry e))
            {
                return new DashServerResponse(false, DashRejectReason.ConfigDisabled, 0, false, 0, 0, 0, 0);
            }

            // Sender와 Owner 일치 검증.
            if (senderClientId != e.OwnerClientId)
            {
                return ChargeOnly(false, DashRejectReason.ConfigDisabled, e);
            }

            // 멱등: 같은 RequestId면 소비/상태를 다시 적용하지 않고 기존 결과 재전송.
            if (e.HasLastRequest && e.LastRequestId == requestId)
            {
                return e.LastResponse;
            }

            // 더 오래된 RequestId(순환 비교)는 Stale — 최신 충전 스냅샷만 전달.
            if (e.HasLastRequest && SequenceLess(requestId, e.LastRequestId))
            {
                return ChargeOnly(false, DashRejectReason.ConfigDisabled, e);
            }

            e.Ledger.Advance(serverNow);

            DashValidationResult result = DashValidationPolicy.Validate(
                e.Config.DashEnabled,
                e.Config.DashDuration,
                e.Config.SnapshotFreshnessTolerance,
                serverNow,
                serverRtt,
                rttAvailable,
                new DashValidationPolicy.Request(clientLocalTime, directionX, directionZ),
                e.History,
                new DashValidationPolicy.CurrentState(currentDead, currentSoul, currentCrowdControlled));

            // 승인은 충전 소비를 인정한다(중단/이미 종료라도). 현재 장부에 충전이 있으면 소비.
            if (result.IsApproved && e.Ledger.HasCharge)
            {
                e.Ledger.TryConsume(serverNow);
            }

            DashServerResponse response = new DashServerResponse(
                result.IsApproved, result.Reason, result.RemainingServerDuration, result.WasInterruptedByServerState,
                e.Ledger.Count, e.Ledger.NextReadyTime, e.Ledger.Epoch, e.Ledger.Revision);

            e.HasLastRequest = true;
            e.LastRequestId = requestId;
            e.LastResponse = response;
            return response;
        }

        // uint 순환 비교: a가 b보다 과거이면 true.
        private static bool SequenceLess(uint a, uint b)
        {
            return unchecked((int)(a - b)) < 0;
        }
    }
}
