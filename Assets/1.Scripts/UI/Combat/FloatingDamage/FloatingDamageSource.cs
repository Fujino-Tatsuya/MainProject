using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 반대쪽 연출(할 일 268) 기준점 — 피해 RPC 의 공격자 clientId(힐이면 힐한 사람)로 그 플레이어를 찾아 지금 위치를 쓴다.
/// clientId 는 이미 전 피어에 복제되는 값(<c>Unit.ClientDamageDealtClientRpc</c>)이라 새 RPC 가 없다.
/// 투사체도 공격자 본인 위치 기준. 몬스터 공격자는 같은 RPC 에 실린 공격자 NetworkObjectId 로 찾는다(새 RPC 아님, 인자 1개 추가).
/// </summary>
public static class FloatingDamageSource
{
    /// <summary>그 clientId 의 플레이어 위치. 플레이어가 아니거나 못 찾거나 대상 자신이면(자기 회복) false.</summary>
    public static bool TryGetPlayerPosition(ulong clientId, Unit target, out Vector3 position)
    {
        position = default;
        if (clientId == ulong.MaxValue)
            return false;

        IReadOnlyList<PlayerLifeCycleController> lives = PlayerLifeCycleController.Active;
        for (int i = 0; i < lives.Count; i++)
        {
            PlayerLifeCycleController life = lives[i];
            if (life == null || !life.IsSpawned || life.OwnerClientId != clientId)
                continue;

            if (target != null && life.gameObject == target.gameObject)
                return false;

            position = life.transform.position;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 플레이어가 아닌 공격자(몬스터·보스) — 피해 RPC 의 공격자 NetworkObjectId 로 지금 위치를 찾는다.
    /// 없음 값이거나 이미 디스폰됐으면 false(위로만).
    /// </summary>
    public static bool TryGetObjectPosition(ulong networkObjectId, Unit target, out Vector3 position)
    {
        position = default;
        if (networkObjectId == DamageDealtInfo.NoAttackerObject)
            return false;

        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || manager.SpawnManager == null ||
            !manager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject source) ||
            source == null || (target != null && source.gameObject == target.gameObject))
            return false;

        position = source.transform.position;
        return true;
    }
}
