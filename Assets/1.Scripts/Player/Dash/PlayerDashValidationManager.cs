using System.Collections.Generic;
using UnityEngine;

namespace BeaverLobby.Player.Dash
{
    /// <summary>
    /// 서버 전용 대시 검증 매니저. (PLAN §6, §9)
    /// - MainGame 기준 씬에 정확히 하나 배치하는 일반 MonoBehaviour(프리팹). NetworkObject 아님.
    /// - 서버에서만 Player별 Snapshot 이력·충전 장부·멱등 캐시를 유지한다.
    /// - Player는 Network Spawn 때 등록, Despawn 때 제거. 키는 NetworkObjectId.
    /// - 파일을 작성하지 않는다(진단 로그는 별도 W9 LogManager).
    ///
    /// 이번 단위(W4-a)는 등록·스냅샷 저장·충전 장부 보관까지. 요청 검증/RPC는 W4-c.
    /// </summary>
    public sealed class PlayerDashValidationManager : MonoBehaviour
    {
        public static PlayerDashValidationManager Instance { get; private set; }

        private sealed class PlayerEntry
        {
            public ulong OwnerClientId;
            public DashSnapshotHistory History;
            public DashChargeLedger Ledger;

            // 멱등 캐시(W4-c에서 사용): 마지막 처리한 요청과 그 결과.
            public bool HasLastRequest;
            public uint LastRequestId;
            public DashValidationResult LastResult;
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

        /// <summary>서버에서 Player를 등록한다. 예측과 무관한 서버 권한 충전 장부를 만충으로 시작한다.</summary>
        public void RegisterPlayer(ulong networkObjectId, ulong ownerClientId, in DashRuntimeConfig config, double now)
        {
            if (_players.ContainsKey(networkObjectId))
            {
                return;
            }

            _players[networkObjectId] = new PlayerEntry
            {
                OwnerClientId = ownerClientId,
                History = new DashSnapshotHistory(Mathf.Max(1, config.SnapshotCapacity)),
                Ledger = new DashChargeLedger(config.MaxCharge, config.RechargeDuration, config.MaxCharge, now),
                HasLastRequest = false,
                LastRequestId = 0u,
                LastResult = default,
            };
        }

        public void DeregisterPlayer(ulong networkObjectId)
        {
            _players.Remove(networkObjectId);
        }

        /// <summary>서버 물리·상태 갱신 뒤 Player 상태 스냅샷을 저장한다.</summary>
        public void PushSnapshot(ulong networkObjectId, in DashStateSnapshot snapshot)
        {
            if (_players.TryGetValue(networkObjectId, out PlayerEntry entry))
            {
                entry.History.Push(snapshot);
            }
        }

        /// <summary>해당 Player의 서버 권한 충전 장부를 진행시킨다(순차 회복 catch-up).</summary>
        public void AdvanceCharges(ulong networkObjectId, double now)
        {
            if (_players.TryGetValue(networkObjectId, out PlayerEntry entry))
            {
                entry.Ledger.Advance(now);
            }
        }
    }
}
