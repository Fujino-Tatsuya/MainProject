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
    [Tooltip("개통 완료 시 NavMesh를 다시 굽는다. 다리가 걸어갈 수 있게 되는 시점.")]
    [SerializeField] private bool rebakeNavMeshOnOpen = true;

    private readonly NetworkList<GateState> gates = new NetworkList<GateState>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public static ZoneBridgeGateManager Instance { get; private set; }

    // SlotID → 이 피어에 로컬로 생성된 존 게이트
    private readonly Dictionary<int, ZoneBridgeGate> _localGates = new Dictionary<int, ZoneBridgeGate>();
    private readonly HashSet<int> _navRebakeDone = new HashSet<int>();

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

        if (IsServer && FindIndex(gate.SlotID) < 0)
        {
            gates.Add(new GateState { SlotID = gate.SlotID, ActivatedMask = 0, OpenStartServerTime = -1d });
        }

        ApplyState(gate);
    }

    public void UnregisterGate(ZoneBridgeGate gate)
    {
        if (gate != null) _localGates.Remove(gate.SlotID);
    }

    // ── 입력 (각 피어 로컬) ────────────────────────────────────────────────

    private void Update()
    {
        TickOpening();

        if (!IsSpawned) return;
        if (Keyboard.current == null) return;
        if (!Keyboard.current[interactKey].wasPressedThisFrame) return;

        TrySendInteract();
    }

    /// <summary>
    /// 로컬 플레이어 근처의 미활성 패널을 찾아 서버에 요청한다. 판정 권한은 서버에 있고
    /// 여기서의 검사는 불필요한 RPC를 줄이기 위한 것이다.
    /// </summary>
    private void TrySendInteract()
    {
        if (!TryGetLocalPlayer(out Transform player, out PlayerLifeCycleController life, out PlayerEncounterLock lockState))
            return;

        // 유령·사망·연출 잠금 중에는 상호작용하지 않는다.
        if (life != null && life.State != PlayerLifeState.Alive) return;
        if (lockState != null && lockState.IsCinematicLocked) return;

        if (!TryFindNearestPanel(player.position, out int slotID, out int panelIndex)) return;

        RequestInteractServerRpc(slotID, panelIndex);
    }

    private bool TryFindNearestPanel(Vector3 from, out int slotID, out int panelIndex)
    {
        slotID = -1;
        panelIndex = -1;
        float best = float.MaxValue;

        foreach (KeyValuePair<int, ZoneBridgeGate> kv in _localGates)
        {
            ZoneBridgeGate gate = kv.Value;
            if (gate == null) continue;

            int index = FindIndex(kv.Key);
            int mask = index >= 0 ? gates[index].ActivatedMask : 0;
            float radiusSqr = gate.InteractRadius * gate.InteractRadius;

            for (int i = 0; i < gate.PanelCount; i++)
            {
                if ((mask & (1 << i)) != 0) continue;                     // 이미 활성
                if (!gate.TryGetPanelPosition(i, out Vector3 p)) continue;

                float sqr = (p - from).sqrMagnitude;
                if (sqr > radiusSqr || sqr >= best) continue;

                best = sqr;
                slotID = kv.Key;
                panelIndex = i;
            }
        }

        return slotID >= 0;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestInteractServerRpc(int slotID, int panelIndex, ServerRpcParams p = default)
    {
        int index = FindIndex(slotID);
        if (index < 0) return;

        GateState state = gates[index];
        if (panelIndex < 0 || panelIndex >= 32) return;
        if ((state.ActivatedMask & (1 << panelIndex)) != 0) return;       // 멱등 — 중복 요청 무시

        if (!_localGates.TryGetValue(slotID, out ZoneBridgeGate gate) || gate == null) return;
        if (!gate.TryGetPanelPosition(panelIndex, out Vector3 panelPos)) return;

        // 서버 재검증: 보낸 클라의 플레이어가 실제로 그 패널 근처에 살아 있는지.
        // 클라 검사만 믿으면 어디서든 개통할 수 있게 된다.
        if (!TryGetPlayerOf(p.Receive.SenderClientId, out Transform sender, out PlayerLifeCycleController life))
            return;
        if (life != null && life.State != PlayerLifeState.Alive) return;

        float radius = gate.InteractRadius;
        if ((sender.position - panelPos).sqrMagnitude > radius * radius) return;

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

        NetworkObject po = NetworkManager != null ? NetworkManager.LocalClient.PlayerObject : null;
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
