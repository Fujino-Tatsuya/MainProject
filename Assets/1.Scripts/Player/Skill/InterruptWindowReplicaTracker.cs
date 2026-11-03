using Unity.Netcode;
using UnityEngine;

/// <summary>
/// [모든 피어] 간파 대상 카운터 창 복제값 전이 알림(할 일 132 → 191) — 대상(<see cref="IInterruptWindowReplica"/>)이 복제값 OnValueChanged 에서 <see cref="Record"/> 를 부른다.
/// 새 전송 없음. 열림이면 로컬 선착순 장부(<see cref="InterruptLocalWindowClaims"/>, 할 일 143-2)를 초기화한다 — 서버 장부와 같은 복제값 전이.
/// 할 일 191 5단계: 옛 예측기용 창 추정(열린 뒤 경과·관측 중앙값·로컬 길이 해석)은 삭제했다 — 예측·판정은 <see cref="InterruptManager"/> 창 시간표를 본다.
/// </summary>
public static class InterruptWindowReplicaTracker
{
    /// <summary>[모든 피어] 복제된 창 전이. 세션 밖이면 아무것도 안 한다. 열림이면 로컬 선착순 장부를 초기화한다(호스트 포함).</summary>
    public static void Record(GameObject owner, bool open)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (owner == null || nm == null || !nm.IsListening)
            return;

        if (open)
            InterruptLocalWindowClaims.Local.WindowOpened(TargetIdOf(owner));
    }

    /// <summary>[모든 피어] 대상이 디스폰된다 — 로컬 선착순 장부에서 지운다.</summary>
    public static void Forget(GameObject owner)
    {
        if (owner == null)
            return;

        InterruptLocalWindowClaims.Local.Forget(TargetIdOf(owner));
    }

    /// <summary>간파 대상 키 — 창 소유자의 NetworkObjectId(NetworkBehaviour.NetworkObject 와 같은 찾기). 없으면 0. 매니저 창 시간표·로컬 선착순 장부 공용.</summary>
    public static ulong TargetIdOf(GameObject owner)
    {
        NetworkObject networkObject = owner != null ? owner.GetComponentInParent<NetworkObject>() : null;
        return networkObject != null ? networkObject.NetworkObjectId : 0;
    }
}
