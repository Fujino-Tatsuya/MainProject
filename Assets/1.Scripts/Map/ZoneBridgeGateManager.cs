using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 존 다리 개통 장치의 <b>상태 소유자</b>(씬 상주 NetworkObject, 서버 권한).
///
/// 왜 씬에 있는가: 존 프리팹은 비네트워크 규약이라(<see cref="MapContentSpawner"/> — 양쪽에서
/// 로컬 Instantiate) 패널·다리에 <see cref="NetworkBehaviour"/>를 붙일 수 없다. 그래서 상태는
/// 씬 상주 매니저가 <c>(SlotID, 패널 인덱스)</c> 키로 복제하고, 존 쪽
/// <see cref="ZoneBridgeGate"/>는 저작 데이터와 로컬 연출만 담당한다.
/// (기존 <c>AttachBossEnterZone</c>이 쓰는 것과 같은 분업이다.)
///
/// 다리 이동을 매 프레임 복제하지 않는다 — <b>개통 시작 서버 시각만 1회 복제</b>하고 각 피어가
/// 그 시각으로 진행도를 스스로 계산한다. 메시지 1개로 끝나고, 늦게 들어온 클라이언트도
/// 경과 시간만큼 진행된 상태를 그대로 재현한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZoneBridgeGateManager : NetworkBehaviour
{
    /// <summary>복제되는 게이트 상태. 진행도가 아니라 <b>시작 시각</b>을 복제한다.</summary>
    public struct GateState : INetworkSerializable, System.IEquatable<GateState>
    {
        public int SlotID;
        public int ActivatedMask;              // 패널 비트마스크 (bit i = 패널 i 활성)
        public double OpenStartServerTime;     // 0 미만이면 아직 개통 시작 안 함

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref SlotID);
            s.SerializeValue(ref ActivatedMask);
            s.SerializeValue(ref OpenStartServerTime);
        }

        public bool Equals(GateState o)
            => SlotID == o.SlotID && ActivatedMask == o.ActivatedMask
               && OpenStartServerTime.Equals(o.OpenStartServerTime);
    }

    [Header("입력")]
    [Tooltip("상호작용 키. 플레이어는 이 키만 누르고, 판정은 서버가 한다.")]
    [SerializeField] private Key interactKey = Key.F;

    [Header("NavMesh")]
    [Tooltip("개통 완료 시 NavMesh 전체를 다시 굽는다. 기본은 끔 — 이 서피스는 맵 전체를 덮어 " +
             "재베이크가 수백 ms 멈추고, 그 멈춤을 서버에서 전원이 겪는다. 정상 경로는 " +
             "'열린 상태로 미리 굽고 평상시엔 카브로 막기'(ZoneBridgeGate)이므로 재베이크가 필요 없다. " +
             "카브 방식이 안 먹는 지형에서만 임시로 켠다.")]
    [SerializeField] private bool rebakeNavMeshOnOpen = false;

    private readonly NetworkList<GateState> gates = new NetworkList<GateState>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public static ZoneBridgeGateManager Instance { get; private set; }

    // SlotID → 이 피어에 로컬로 생성된 존 게이트
    private readonly Dictionary<int, ZoneBridgeGate> _localGates = new Dictionary<int, ZoneBridgeGate>();
    private readonly HashSet<int> _navRebakeDone = new HashSet<int>();
    private bool _warnedNoKeyboard;

    private void Awake() => Instance = this;

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        gates.OnListChanged += HandleGatesChanged;

        // ⚠️ 순서 함정: 존 스폰(=RegisterGate)이 이 OnNetworkSpawn보다 먼저 올 수 있다. 씬 상주
        // NetworkObject들 사이의 스폰 순서는 보장되지 않고, 맵 생성은 MapNetworkSync의
        // OnNetworkSpawn에서 시작된다. 그때 NetworkList에 쓰면 조용히 버려져 상태 항목이 없는
        // 채로 남고, 서버 RPC가 FindIndex에서 -1을 받아 아무 일도 일어나지 않는다.
        // → 스폰 시점에 이미 등록된 게이트의 항목을 여기서 만든다.
        foreach (int slotID in _localGates.Keys)
            EnsureStateEntry(slotID);

        Edit.Log($"[BridgeGate] 매니저 스폰 (서버:{IsServer}) — 등록된 게이트 {_localGates.Count}개 / 상태 항목 {gates.Count}개.", this);
        ApplyAllStates();
    }

    public override void OnNetworkDespawn()
    {
        gates.OnListChanged -= HandleGatesChanged;
        base.OnNetworkDespawn();
    }

    // ── 등록 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 존 스폰 시 호출한다(<see cref="MapContentSpawner"/>). 서버는 상태 항목을 만들고,
    /// 클라는 이미 복제된 상태를 즉시 반영한다(등록 순서가 복제 순서와 무관하도록).
    /// </summary>
    public void RegisterGate(ZoneBridgeGate gate)
    {
        if (gate == null) return;

        if (gate.SlotID < 0)
        {
            Edit.LogError($"[BridgeGate] '{gate.name}'의 SlotID가 설정되지 않았습니다 — 상태를 복제할 키가 없습니다.", gate);
            return;
        }

        _localGates[gate.SlotID] = gate;

        int unauthored = gate.CountUnauthoredSegments();
        if (unauthored > 0)
        {
            Edit.LogWarning(
                $"[BridgeGate] Slot {gate.SlotID}: 다리 {unauthored}조각의 열림 위치가 미저작입니다 — " +
                "그 조각은 움직이지 않습니다. 'Tools/Map/Authoring/Record Bridge Open Positions'로 저장하세요.", gate);
        }

        EnsureStateEntry(gate.SlotID);
        ApplyState(gate);

        Edit.Log(
            $"[BridgeGate] 게이트 등록 — Slot {gate.SlotID}, 패널 {gate.PanelCount}개, 다리 {gate.SegmentCount}조각 " +
            $"(반경 {gate.InteractRadius}m). 스폰됨:{IsSpawned} 서버:{IsServer} 상태항목:{gates.Count}", gate);
    }

    /// <summary>서버·스폰 완료 상태에서만 상태 항목을 만든다. 스폰 전 쓰기는 조용히 버려진다.</summary>
    private void EnsureStateEntry(int slotID)
    {
        if (!IsServer || !IsSpawned) return;
        if (FindIndex(slotID) >= 0) return;

        gates.Add(new GateState { SlotID = slotID, ActivatedMask = 0, OpenStartServerTime = -1d });
    }

    public void UnregisterGate(ZoneBridgeGate gate)
    {
        if (gate != null) _localGates.Remove(gate.SlotID);
    }

    // ── 입력 (각 피어 로컬) ────────────────────────────────────────────────

    private void Update()
    {
        TickOpening();

        if (Keyboard.current == null)
        {
            // 마지막 남은 조용한 경로 — Active Input Handling이 Input System을 포함하지 않으면 여기서 끝난다.
            if (!_warnedNoKeyboard)
            {
                _warnedNoKeyboard = true;
                Edit.LogError("[BridgeGate] Keyboard.current가 null입니다 — Input System 키보드를 읽을 수 없어 " +
                              "F 상호작용이 동작하지 않습니다(Project Settings > Player > Active Input Handling 확인).", this);
            }
            return;
        }

        if (!Keyboard.current[interactKey].wasPressedThisFrame) return;

        // ⚠️ 키를 눌렀는데 아무 일도 안 일어나는 상태를 조용히 두지 않는다. 조기 반환이 6가지라
        // 로그가 없으면 어디서 막혔는지 알 방법이 없다(F가 안 먹는다는 보고의 원인).
        if (!IsSpawned)
        {
            Edit.LogWarning("[BridgeGate] F: 매니저가 아직 네트워크 스폰되지 않았습니다 — " +
                            "MapScene에 배치한 뒤 씬을 저장했는지, NetworkObject가 붙어 있는지 확인하세요.", this);
            return;
        }

        TrySendInteract();
    }

    /// <summary>
    /// 로컬 플레이어 근처의 미활성 패널을 찾아 서버에 요청한다. 판정 권한은 서버에 있고
    /// 여기서의 검사는 불필요한 RPC를 줄이기 위한 것이다.
    /// </summary>
    private void TrySendInteract()
    {
        if (_localGates.Count == 0)
        {
            Edit.LogWarning("[BridgeGate] F: 등록된 게이트가 0개입니다 — ZoneL_typeB가 이번 시드에 배치되지 않았거나, " +
                            "존 프리팹에 ZoneBridgeGate가 없습니다('Wire Zone Bridge Gate' 확인).", this);
            return;
        }

        if (!TryGetLocalPlayer(out Transform player, out PlayerLifeCycleController life, out PlayerEncounterLock lockState))
        {
            Edit.LogWarning("[BridgeGate] F: 로컬 플레이어(PlayerObject)를 찾지 못했습니다 — 아직 스폰 전이거나 " +
                            "이 피어에 플레이어가 없습니다.", this);
            return;
        }

        // 유령·사망·연출 잠금 중에는 상호작용하지 않는다.
        if (life != null && life.State != PlayerLifeState.Alive)
        {
            Edit.Log($"[BridgeGate] F 무시: 플레이어 상태가 {life.State}입니다(Alive만 가능).", this);
            return;
        }

        if (lockState != null && lockState.IsCinematicLocked)
        {
            Edit.Log("[BridgeGate] F 무시: 연출 잠금 중입니다.", this);
            return;
        }

        if (!TryFindNearestPanel(player.position, out int slotID, out int panelIndex, out float nearestDistance, out int skippedActive))
        {
            Edit.Log(
                $"[BridgeGate] F 무시: 반경 안에 미활성 패널이 없습니다. 가장 가까운 미활성 패널 " +
                $"{(nearestDistance < float.MaxValue ? $"{nearestDistance:F2}m" : "없음")} / " +
                $"이미 활성 {skippedActive}개 / 플레이어 {player.position}", this);
            return;
        }

        Edit.Log($"[BridgeGate] F: Slot {slotID} 패널 {panelIndex} 요청 (거리 {nearestDistance:F2}m).", this);
        RequestInteractServerRpc(slotID, panelIndex);
    }

    private bool TryFindNearestPanel(Vector3 from, out int slotID, out int panelIndex,
                                     out float nearestDistance, out int skippedActive)
    {
        slotID = -1;
        panelIndex = -1;
        skippedActive = 0;
        float best = float.MaxValue;
        float nearestAny = float.MaxValue;   // 반경 밖이라도 가장 가까운 미활성 패널 거리(진단용)

        foreach (KeyValuePair<int, ZoneBridgeGate> kv in _localGates)
        {
            ZoneBridgeGate gate = kv.Value;
            if (gate == null) continue;

            int index = FindIndex(kv.Key);
            int mask = index >= 0 ? gates[index].ActivatedMask : 0;
            float radiusSqr = gate.InteractRadius * gate.InteractRadius;

            for (int i = 0; i < gate.PanelCount; i++)
            {
                if ((mask & (1 << i)) != 0) { skippedActive++; continue; }   // 이미 활성
                if (!gate.TryGetPanelPosition(i, out Vector3 p)) continue;

                float sqr = (p - from).sqrMagnitude;
                if (sqr < nearestAny) nearestAny = sqr;

                if (sqr > radiusSqr || sqr >= best) continue;

                best = sqr;
                slotID = kv.Key;
                panelIndex = i;
            }
        }

        nearestDistance = nearestAny < float.MaxValue ? Mathf.Sqrt(nearestAny) : float.MaxValue;
        return slotID >= 0;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestInteractServerRpc(int slotID, int panelIndex, ServerRpcParams p = default)
    {
        int index = FindIndex(slotID);
        if (index < 0)
        {
            Edit.LogWarning($"[BridgeGate] 서버 거부: Slot {slotID}의 상태 항목이 없습니다 — " +
                            "게이트 등록이 매니저 스폰보다 먼저였을 수 있습니다.", this);
            return;
        }

        GateState state = gates[index];
        if (panelIndex < 0 || panelIndex >= 32) return;
        if ((state.ActivatedMask & (1 << panelIndex)) != 0) return;       // 멱등 — 중복 요청 무시

        if (!_localGates.TryGetValue(slotID, out ZoneBridgeGate gate) || gate == null)
        {
            Edit.LogWarning($"[BridgeGate] 서버 거부: Slot {slotID}의 존이 서버에 없습니다.", this);
            return;
        }
        if (!gate.TryGetPanelPosition(panelIndex, out Vector3 panelPos)) return;

        // 서버 재검증: 보낸 클라의 플레이어가 실제로 그 패널 근처에 살아 있는지.
        // 클라 검사만 믿으면 어디서든 개통할 수 있게 된다.
        if (!TryGetPlayerOf(p.Receive.SenderClientId, out Transform sender, out PlayerLifeCycleController life))
        {
            Edit.LogWarning($"[BridgeGate] 서버 거부: client {p.Receive.SenderClientId}의 PlayerObject를 찾지 못했습니다.", this);
            return;
        }
        if (life != null && life.State != PlayerLifeState.Alive) return;

        float radius = gate.InteractRadius;
        float distance = Vector3.Distance(sender.position, panelPos);
        if (distance > radius)
        {
            Edit.LogWarning($"[BridgeGate] 서버 거부: 거리 {distance:F2}m > 반경 {radius}m " +
                            $"(서버가 본 플레이어 {sender.position} / 패널 {panelPos}). " +
                            "클라와 서버의 플레이어 위치가 어긋나면 이 값이 벌어진다.", this);
            return;
        }

        state.ActivatedMask |= 1 << panelIndex;

        int all = (1 << gate.PanelCount) - 1;
        bool complete = gate.PanelCount > 0 && (state.ActivatedMask & all) == all;
        if (complete && state.OpenStartServerTime < 0d)
        {
            state.OpenStartServerTime = NetworkManager.ServerTime.Time;
            Edit.Log($"[BridgeGate] Slot {slotID}: 패널 {gate.PanelCount}개 전부 활성 — 다리 개통 시작.", gate);
        }

        gates[index] = state;
        Edit.Log($"[BridgeGate] Slot {slotID} 패널 {panelIndex} 활성 (client {p.Receive.SenderClientId}).", gate);
    }

    // ── 상태 반영 ───────────────────────────────────────────────────────────

    private void HandleGatesChanged(NetworkListEvent<GateState> _) => ApplyAllStates();

    private void ApplyAllStates()
    {
        foreach (ZoneBridgeGate gate in _localGates.Values)
            if (gate != null) ApplyState(gate);
    }

    /// <summary>복제된 상태를 로컬 존에 그린다(링 표시 + 다리 진행도).</summary>
    private void ApplyState(ZoneBridgeGate gate)
    {
        int index = FindIndex(gate.SlotID);
        if (index < 0) return;

        GateState state = gates[index];

        for (int i = 0; i < gate.PanelCount; i++)
            gate.SetPanelActivatedVisual(i, (state.ActivatedMask & (1 << i)) != 0);

        gate.ApplyOpenProgress(ProgressOf(state, gate));
    }

    /// <summary>
    /// 매 프레임 진행도를 다시 계산한다. 개통 중에만 의미가 있고, 복제값이 아니라 <b>서버 시각</b>에서
    /// 계산하므로 모든 피어가 같은 위치를 그린다.
    /// </summary>
    private void TickOpening()
    {
        if (!IsSpawned) return;

        for (int i = 0; i < gates.Count; i++)
        {
            GateState state = gates[i];
            if (state.OpenStartServerTime < 0d) continue;
            if (!_localGates.TryGetValue(state.SlotID, out ZoneBridgeGate gate) || gate == null) continue;

            float progress = ProgressOf(state, gate);
            gate.ApplyOpenProgress(progress);

            // NavMesh는 다리가 제자리에 온 뒤 한 번만 다시 굽는다(서버 판정 기준).
            if (IsServer && progress >= 1f && rebakeNavMeshOnOpen && _navRebakeDone.Add(state.SlotID))
                RebakeNavMesh(state.SlotID);
        }
    }

    private float ProgressOf(GateState state, ZoneBridgeGate gate)
    {
        if (state.OpenStartServerTime < 0d) return 0f;

        double now = NetworkManager != null ? NetworkManager.ServerTime.Time : state.OpenStartServerTime;
        float elapsed = (float)(now - state.OpenStartServerTime);
        return Mathf.Clamp01(elapsed / Mathf.Max(0.05f, gate.OpenDuration));
    }

    private void RebakeNavMesh(int slotID)
    {
        MapNavMeshBaker baker = FindFirstObjectByType<MapNavMeshBaker>();
        if (baker == null)
        {
            Edit.LogWarning($"[BridgeGate] Slot {slotID}: MapNavMeshBaker가 없어 NavMesh를 갱신하지 못했습니다 — " +
                            "다리 위를 몬스터가 걸을 수 없습니다.", this);
            return;
        }

        baker.RebakeNow($"다리 개통(Slot {slotID})");
    }

    // ── 조회 ────────────────────────────────────────────────────────────────

    private int FindIndex(int slotID)
    {
        if (!IsSpawned) return -1;

        for (int i = 0; i < gates.Count; i++)
            if (gates[i].SlotID == slotID) return i;
        return -1;
    }

    private bool TryGetLocalPlayer(out Transform player, out PlayerLifeCycleController life, out PlayerEncounterLock lockState)
    {
        player = null;
        life = null;
        lockState = null;

        // LocalClient는 null일 수 있다 — 프로젝트의 다른 소비처(BossTeleportManager)와 같이 ?. 를 쓴다.
        NetworkObject po = NetworkManager != null ? NetworkManager.LocalClient?.PlayerObject : null;
        if (po == null) return false;

        player = po.transform;
        life = po.GetComponent<PlayerLifeCycleController>();
        lockState = po.GetComponent<PlayerEncounterLock>();
        return true;
    }

    private bool TryGetPlayerOf(ulong clientId, out Transform player, out PlayerLifeCycleController life)
    {
        player = null;
        life = null;

        if (NetworkManager == null || !NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
            return false;

        NetworkObject po = client.PlayerObject;
        if (po == null) return false;

        player = po.transform;
        life = po.GetComponent<PlayerLifeCycleController>();
        return true;
    }
}
