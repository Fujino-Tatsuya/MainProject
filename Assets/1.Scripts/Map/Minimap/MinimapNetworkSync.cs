using Unity.Netcode;
using UnityEngine;

// 미니맵 탐사 상태 서버 권위 공유 (팀장 지시 2026-07-03: 호스트 정보 공유).
//  - 서버: 자기 MinimapController의 탐사 그리드(비트팩 ~2KB)를 1초 주기·변경 시에만 브로드캐스트.
//  - 클라: 수신 즉시 로컬 탐사 그리드에 병합(OR) — 로컬 스탬프는 즉각 반응용, 서버가 최종 일치 보장.
//  - 늦게 접속한 클라도 접속 시 전체 상태를 받아 복구된다.
//  - 씬 배치 NetworkObject (Minimap 오브젝트, Wire가 배선). 네트워크 미사용(에디터 단독)이면 스폰 안 됨.
[RequireComponent(typeof(MinimapController))]
public class MinimapNetworkSync : NetworkBehaviour
{
    [Tooltip("서버 브로드캐스트 주기(초)")] public float SyncInterval = 1f;

    private MinimapController _controller;
    private float _timer;
    private byte[] _lastSent;

    private void Awake() => _controller = GetComponent<MinimapController>();

    public override void OnNetworkSpawn()
    {
        // 늦게 들어온 클라 복구 — 서버가 현재 상태를 해당 클라에 즉시 전송
        if (IsServer)
            NetworkManager.OnClientConnectedCallback += OnClientConnected;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
    }

    private void OnClientConnected(ulong clientId)
    {
        if (_controller == null || !_controller.IsReady) return;
        var bits = _controller.GetExploredBits();
        if (bits == null) return;
        var p = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };
        SyncExploredClientRpc(bits, p);
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer || _controller == null || !_controller.IsReady) return;

        _timer -= Time.deltaTime;
        if (_timer > 0f) return;
        _timer = SyncInterval;

        var bits = _controller.GetExploredBits();
        if (bits == null || Same(bits, _lastSent)) return;
        _lastSent = bits;
        SyncExploredClientRpc(bits);
    }

    private static bool Same(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    [ClientRpc]
    private void SyncExploredClientRpc(byte[] bits, ClientRpcParams rpcParams = default)
    {
        if (IsServer) return; // 호스트 자신은 원본 보유
        _controller?.MergeExploredBits(bits);
    }
}
