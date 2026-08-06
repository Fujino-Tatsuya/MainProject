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

            // 진단용: 스냅샷 플래그 변화만 로그로 남기기 위한 직전 값(매 tick 로그는 스팸).
            public bool HasLastSnapshotFlags;
            public bool LastGrounded;
            public bool LastDead;
            public bool LastSoul;
            public bool LastCrowdControlled;
            public bool LastLandingProtected;
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
                DashLog.LogWarning(
                    $"[Dash/서버] 등록 생략: NetworkObjectId={networkObjectId}가 이미 등록돼 있습니다. " +
                    "(재스폰 시 Deregister가 누락되면 이전 충전 장부·스냅샷이 그대로 남습니다)", this);
                return;
            }

            DashLog.Log(
                $"[Dash/서버] 등록: NetworkObjectId={networkObjectId} owner={ownerClientId} " +
                $"활성={config.DashEnabled} 지속={config.DashDuration:F3}s 충전={config.MaxCharge} " +
                $"재충전={config.RechargeDuration:F2}s 스냅샷허용={config.SnapshotFreshnessTolerance:F3}s serverNow={now:F3}", this);

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
            // 등록 해제 후 도착하는 요청은 전부 ConfigDisabled로 거부된다 — 해제 시점을 남겨 둔다.
            DashLog.Log($"[Dash/서버] 등록 해제: NetworkObjectId={networkObjectId} (이후 이 오브젝트의 대시 요청은 전부 거부됩니다)", this);
            _players.Remove(networkObjectId);
        }

        /// <summary>생존 복귀/부활 시 서버 권한 충전을 강제 초기화한다(Epoch 증가). (PLAN §10)</summary>
        public void ForceReset(ulong networkObjectId, int count, double now)
        {
            if (_players.TryGetValue(networkObjectId, out PlayerEntry entry))
            {
                DashLog.Log(
                    $"[Dash/서버] 충전 강제 초기화: NetworkObjectId={networkObjectId} " +
                    $"{entry.Ledger.Count}→{count} (Epoch {entry.Ledger.Epoch}→{entry.Ledger.Epoch + 1u}) serverNow={now:F3}", this);
                entry.Ledger.ForceReset(count, now);
            }
            else
            {
                DashLog.LogWarning($"[Dash/서버] 충전 초기화 실패: 미등록 NetworkObjectId={networkObjectId}", this);
            }
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

            // 스냅샷 플래그가 바뀔 때만 남긴다(매 물리 tick 로그는 스팸). 대시 거부 사유(NotGrounded/
            // CrowdControlled/DeadOrSoul/LandingProtected)는 전부 "요청 시점의 이 플래그"에서 나오므로,
            // 이 변화 이력과 거부 로그의 추정입력시각을 맞춰 보면 왜 거부됐는지가 그대로 드러난다.
            if (!e.HasLastSnapshotFlags ||
                e.LastGrounded != grounded ||
                e.LastDead != dead ||
                e.LastSoul != soul ||
                e.LastCrowdControlled != crowdControlled ||
                e.LastLandingProtected != landingProtected)
            {
                if (e.HasLastSnapshotFlags)
                {
                    DashLog.Log(
                        $"[Dash/서버] 스냅샷 상태 변화 @serverNow={serverTime:F3} (NetworkObjectId={networkObjectId}): " +
                        $"접지 {e.LastGrounded}→{grounded}, CC {e.LastCrowdControlled}→{crowdControlled}, " +
                        $"사망 {e.LastDead}→{dead}, 소울 {e.LastSoul}→{soul}, 착지보호 {e.LastLandingProtected}→{landingProtected}", this);
                }

                e.HasLastSnapshotFlags = true;
                e.LastGrounded = grounded;
                e.LastDead = dead;
                e.LastSoul = soul;
                e.LastCrowdControlled = crowdControlled;
                e.LastLandingProtected = landingProtected;
            }

            e.Ledger.Advance(serverTime);
            bool pushed = e.History.Push(new DashStateSnapshot(
                serverTime, grounded, dead, soul, crowdControlled, landingProtected,
                e.Ledger.Count, e.Ledger.NextReadyTime, e.Ledger.Epoch, e.Ledger.Revision));

            // Push 거부 = 서버 게임시각이 뒤로 갔다는 뜻. 이 구간의 요청은 NoFreshSnapshot으로 거부된다.
            if (!pushed)
            {
                DashLog.LogWarning(
                    $"[Dash/서버] 스냅샷 저장 거부(시각 역전): serverTime={serverTime:F3}이 직전 저장보다 과거입니다. " +
                    "NetworkClock 재시작/씬 전환 직후라면 이 구간 대시 요청은 NoFreshSnapshot으로 거부됩니다.", this);
            }
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
            // ⚠️ 아래 4개 조기 반환은 전부 사유가 ConfigDisabled로 뭉개져 나간다. 오너 로그만 보면
            // "설정이 꺼졌다"로 읽히지만 실제 원인은 미등록·오너불일치·중복·순서역전으로 전혀 다르다.
            // 그래서 각 경로를 여기서 구분해 남긴다.
            if (!_players.TryGetValue(networkObjectId, out PlayerEntry e))
            {
                DashLog.LogWarning(
                    $"[Dash/서버] 거부(사유표기 ConfigDisabled): 미등록 Player NetworkObjectId={networkObjectId}. " +
                    $"RegisterPlayer가 호출되지 않았거나(매니저가 나중에 로드) 이미 Deregister됐습니다. 현재 등록 수={_players.Count}", this);
                return new DashServerResponse(false, DashRejectReason.ConfigDisabled, 0, false, 0, 0, 0, 0);
            }

            // Sender와 Owner 일치 검증.
            if (senderClientId != e.OwnerClientId)
            {
                DashLog.LogWarning(
                    $"[Dash/서버] 거부(사유표기 ConfigDisabled): 요청자≠오너 — sender={senderClientId}, owner={e.OwnerClientId}. " +
                    "소유권 이전 직후 등록 정보가 갱신되지 않은 경우입니다(RegisterPlayer는 스폰 시 1회만 기록).", this);
                return ChargeOnly(false, DashRejectReason.ConfigDisabled, e);
            }

            // 멱등: 같은 RequestId면 소비/상태를 다시 적용하지 않고 기존 결과 재전송.
            if (e.HasLastRequest && e.LastRequestId == requestId)
            {
                DashLog.Log(
                    $"[Dash/서버] 멱등 재전송: requestId={requestId} — 재검증·재소비 없이 이전 결과를 다시 보냅니다. " +
                    $"(승인={e.LastResponse.IsApproved}, 사유={e.LastResponse.Reason}, 남은시간={e.LastResponse.RemainingServerDuration:F3}s)", this);
                return e.LastResponse;
            }

            // 더 오래된 RequestId(순환 비교)는 Stale — 최신 충전 스냅샷만 전달.
            if (e.HasLastRequest && SequenceLess(requestId, e.LastRequestId))
            {
                DashLog.LogWarning(
                    $"[Dash/서버] 거부(사유표기 ConfigDisabled): 순서 지난 요청 requestId={requestId} < 최근처리 {e.LastRequestId}. " +
                    "RPC 순서 역전 또는 오너 재스폰으로 requestId가 1부터 다시 시작된 경우입니다.", this);
                return ChargeOnly(false, DashRejectReason.ConfigDisabled, e);
            }

            e.Ledger.Advance(serverNow);

            // 추정 입력시각(서버 도메인). DashValidationPolicy와 같은 식을 쓴다.
            double oneWayDelay = rttAvailable && serverRtt > 0.0 ? serverRtt * 0.5 : 0.0;
            double estimatedStart = serverNow - oneWayDelay;

            DashValidationResult result = DashValidationPolicy.Validate(
                e.Config.DashEnabled,
                e.Config.DashDuration,
                e.Config.SnapshotFreshnessTolerance,
                serverNow,
                serverRtt,
                rttAvailable,
                e.Ledger.Count,
                new DashValidationPolicy.Request(clientLocalTime, directionX, directionZ),
                e.History,
                new DashValidationPolicy.CurrentState(currentDead, currentSoul, currentCrowdControlled));

            // 승인은 충전 소비를 인정한다(중단/이미 종료라도). 현재 장부에 충전이 있으면 소비.
            //
            // ⚠️ 소비 시각은 RPC 도착시각(serverNow)이 아니라 추정 입력시각이다. 도착시각으로 잡으면
            // 서버 회복 완료가 오너보다 항상 편도지연만큼 늦어진다 → 오너가 "이제 찼다"고 믿고 누른
            // 입력이 서버에선 아직 0인 구간이 매 주기 생기고, 그 입력이 거부된다(원격 클라에서 심함).
            bool consumed = result.IsApproved && e.Ledger.HasCharge;
            if (consumed)
            {
                e.Ledger.TryConsume(estimatedStart);
            }

            DashServerResponse response = new DashServerResponse(
                result.IsApproved, result.Reason, result.RemainingServerDuration, result.WasInterruptedByServerState,
                e.Ledger.Count, e.Ledger.NextReadyTime, e.Ledger.Epoch, e.Ledger.Revision);

            LogValidationOutcome(
                networkObjectId, requestId, result, consumed, estimatedStart, oneWayDelay,
                serverNow, serverRtt, rttAvailable, e, currentDead, currentSoul, currentCrowdControlled);

            e.HasLastRequest = true;
            e.LastRequestId = requestId;
            e.LastResponse = response;
            return response;
        }

        /// <summary>
        /// 검증 결과를 사유별 설명과 함께 남긴다. 요청 1회당 1줄.
        ///
        /// 거부 사유는 전부 "추정입력시각(estimatedStart) 기준 스냅샷"에서 나오므로, 그 스냅샷의 실제 값과
        /// 시각 계산에 쓰인 원자료(serverNow / RTT / 편도지연)를 같이 찍어야 원인을 특정할 수 있다.
        /// </summary>
        private void LogValidationOutcome(
            ulong networkObjectId, uint requestId, in DashValidationResult result, bool consumed,
            double estimatedStart, double oneWayDelay, double serverNow, double serverRtt, bool rttAvailable,
            PlayerEntry e, bool currentDead, bool currentSoul, bool currentCrowdControlled)
        {
            string common =
                $"NetworkObjectId={networkObjectId} requestId={requestId} | " +
                $"serverNow={serverNow:F3} RTT={(rttAvailable ? $"{serverRtt * 1000.0:F1}ms" : "사용불가")} " +
                $"편도={oneWayDelay:F3}s 추정입력시각={estimatedStart:F3} | " +
                $"{DescribeSnapshot(e, estimatedStart)} | " +
                $"서버충전={e.Ledger.Count}/{e.Ledger.MaxCharge} 다음충전까지={DescribeNextReady(e, serverNow)} | " +
                $"현재 사망={currentDead} 소울={currentSoul} CC={currentCrowdControlled}";

            if (!result.IsApproved)
            {
                DashLog.LogWarning(
                    $"[Dash/서버] 거부 사유={result.Reason} — {ExplainReject(result.Reason, e)} | {common}", this);
                return;
            }

            if (result.WasInterruptedByServerState)
            {
                DashLog.LogWarning(
                    "[Dash/서버] 승인했지만 중단 지시 — 요청 시점에는 유효했으나 RPC가 도착한 현재 사망/소울/CC 상태입니다. " +
                    $"충전은 소비({consumed})되고 오너 대시는 즉시 종료됩니다. | {common}", this);
                return;
            }

            if (result.RemainingServerDuration <= 0.0)
            {
                DashLog.LogWarning(
                    $"[Dash/서버] 승인했지만 남은시간 0 — 추정입력시각+지속({e.Config.DashDuration:F3}s)이 이미 지났습니다. " +
                    $"RTT가 지속시간보다 크거나 시각 추정이 어긋난 경우이며, 오너 대시는 즉시 종료됩니다. 충전 소비={consumed} | {common}", this);
                return;
            }

            DashLog.Log(
                $"[Dash/서버] 승인 — 남은시간={result.RemainingServerDuration:F3}s 충전소비={consumed} | {common}", this);
        }

        /// <summary>거부 사유별로 "무엇을 봐야 하는지"를 한 문장으로 설명한다.</summary>
        private static string ExplainReject(DashRejectReason reason, PlayerEntry e)
        {
            switch (reason)
            {
                case DashRejectReason.ConfigDisabled:
                    return "DashRuntimeConfig가 비활성입니다(PlayerDashData 값 비정상 또는 미할당).";
                case DashRejectReason.InvalidPayload:
                    return "요청 payload가 NaN/Infinity이거나 방향이 0입니다(오너 시계 또는 입력 방향 산출 문제).";
                case DashRejectReason.RttUnavailable:
                    return "Transport RTT를 읽을 수 없습니다(NetworkTransport 미설정/연결 초기).";
                case DashRejectReason.NoFreshSnapshot:
                    return $"추정입력시각 이전 {e.Config.SnapshotFreshnessTolerance:F3}s 안에 스냅샷이 없습니다 — " +
                           "서버 FixedUpdate 스냅샷이 끊겼거나(매니저 늦은 로드/Player 비활성), RTT가 커서 추정시각이 링버퍼 범위를 벗어났습니다.";
                case DashRejectReason.DeadOrSoul:
                    return "요청 시점 스냅샷이 사망/소울 상태였습니다.";
                case DashRejectReason.CrowdControlled:
                    return "요청 시점 스냅샷이 CC(또는 연출 잠금)였습니다 — StatusEffects.BlocksMovement / PlayerEncounterLock 확인.";
                case DashRejectReason.LandingProtected:
                    return "요청 시점 스냅샷이 착지 보호 상태였습니다.";
                case DashRejectReason.NotGrounded:
                    return "요청 시점 스냅샷이 공중이었습니다 — 서버 PlayerGroundingSensor 판정(접지 깜빡임) 확인.";
                case DashRejectReason.NoCharge:
                    return "현재 서버 충전이 0입니다 — 오너 예측이 서버보다 먼저 회복했다고 판단한 경우입니다(시계 도메인/편도지연 확인).";
                default:
                    return "분류되지 않은 사유입니다.";
            }
        }

        /// <summary>거부 판정에 실제로 쓰인 스냅샷(또는 부재)을 사람이 읽을 수 있게 풀어 준다.</summary>
        private static string DescribeSnapshot(PlayerEntry e, double estimatedStart)
        {
            if (e.History.TrySelectAtOrBefore(estimatedStart, e.Config.SnapshotFreshnessTolerance, out DashStateSnapshot s))
            {
                return $"채택 스냅샷 age={(estimatedStart - s.ServerTime):F3}s " +
                       $"(접지={s.Grounded} CC={s.CrowdControlled} 사망={s.Dead} 소울={s.Soul} 착지보호={s.LandingProtected} 당시충전={s.ChargeCount})";
            }

            return $"채택 스냅샷 없음(보관 {e.History.Count}/{e.History.Capacity}, 허용 {e.Config.SnapshotFreshnessTolerance:F3}s)";
        }

        private static string DescribeNextReady(PlayerEntry e, double serverNow)
        {
            double next = e.Ledger.NextReadyTime;
            if (double.IsInfinity(next) || double.IsNaN(next))
            {
                return "만충";
            }

            return $"{System.Math.Max(0.0, next - serverNow):F2}s";
        }

        // uint 순환 비교: a가 b보다 과거이면 true.
        private static bool SequenceLess(uint a, uint b)
        {
            return unchecked((int)(a - b)) < 0;
        }
    }
}
