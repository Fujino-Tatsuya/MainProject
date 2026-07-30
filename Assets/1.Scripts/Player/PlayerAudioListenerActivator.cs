using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 플레이어의 AudioListener를 "로컬 소유(IsOwner) + 본게임 준비 완료" 조건에서만 활성화한다.
///
/// - NGO에선 각 클라이언트가 모든 플레이어를 스폰하므로, 리스너를 그대로 두면 한 클라에 여러 개가 생긴다.
///   → 원격 플레이어(IsOwner=false) 인스턴스는 영구 비활성, 로컬 소유 인스턴스만 활성화한다.
/// - 로딩 중에는 로딩씬 카메라 리스너 하나만 살아 있도록, 플레이어 리스너는 MainGameReady 전까지 꺼둔다.
///
/// AudioListener + NetworkObject가 함께 있는 Player.prefab 루트에 부착한다.
/// </summary>
[RequireComponent(typeof(AudioListener))]
public class PlayerAudioListenerActivator : NetworkBehaviour
{
    private AudioListener _listener;

    private void Awake()
    {
        _listener = GetComponent<AudioListener>();
        _listener.enabled = false; // 모든 인스턴스 기본 off (원격 플레이어는 이 상태 유지)
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            return; // 원격 플레이어 인스턴스: 리스너 영구 비활성
        }

        // 내 플레이어: 본게임 준비 완료 시점에 활성화 (이미 준비됐으면 즉시 1회)
        GameManager.SubscribeMainGameStart(EnableListener);
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            GameManager.UnsubscribeMainGameStart(EnableListener);
        }
    }

    private void EnableListener()
    {
        if (_listener != null)
        {
            _listener.enabled = true;
        }
    }
}
