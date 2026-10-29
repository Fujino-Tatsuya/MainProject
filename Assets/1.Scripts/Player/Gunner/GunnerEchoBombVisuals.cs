using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 잔류 포격 에너지 폭탄 1개의 복제 상태(할 일 149) — <c>GunnerBeamView</c> 의 NetworkList 항목.
/// 피해·폭발 시각은 서버만 든다(<see cref="GunnerEchoBombs"/>). 클라는 이걸로 붙은 구체만 그린다 — 늦게 합류해도 목록이 그대로 온다.
/// </summary>
public struct GunnerEchoBombState : INetworkSerializable, IEquatable<GunnerEchoBombState>
{
    public int BombId;
    public ulong TargetId;

    public GunnerEchoBombState(int bombId, ulong targetId)
    {
        BombId = bombId;
        TargetId = targetId;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref BombId);
        s.SerializeValue(ref TargetId);
    }

    public bool Equals(GunnerEchoBombState o) => BombId == o.BombId && TargetId == o.TargetId;
}

/// <summary>잔류 포격 폭탄 대상 찾기 — 서버(피해·생존 확인)와 클라(구체 위치)가 같은 함수를 쓴다.</summary>
public static class GunnerEchoBombTargets
{
    /// <summary>네트워크 ID → 스폰된 유닛(없거나 디스폰이면 null).</summary>
    public static Unit Find(ulong networkObjectId)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || manager.SpawnManager == null ||
            !manager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject networkObject) || networkObject == null)
            return null;

        return networkObject.TryGetComponent(out Unit unit) ? unit : networkObject.GetComponentInChildren<Unit>();
    }

    /// <summary>[서버] 폭탄이 터질 수 있는 대상인가 — 스폰돼 있고 살아 있다.</summary>
    public static bool IsAlive(ulong networkObjectId)
    {
        Unit unit = Find(networkObjectId);
        return unit != null && unit.IsSpawned && unit.CurrentHealth > 0;
    }

    /// <summary>구체가 붙는 몸통 점 — 기존 몸 중심 기준(<see cref="BossPatternTargets.BodyCenter"/>: 캡슐 콜라이더 중심, 없으면 발 + 1m).</summary>
    public static Vector3 BodyPoint(Unit unit) => BossPatternTargets.BodyCenter(unit.transform);
}

/// <summary>
/// [전 피어] 잔류 포격 에너지 폭탄 구체 표시(할 일 149) — 폭탄마다 구체 하나를 대상 몸통에 붙여 매 프레임 따라가게 한다(이동·넉백 포함).
/// 대상이 사라지면 마지막 위치에 남았다가 폭탄이 빠질 때 그 자리를 돌려준다(폭발 연출 위치). 런타임 메시·텍스처 생성 없음 — 프리팹만 복제한다.
/// </summary>
public sealed class GunnerEchoBombVisuals
{
    private sealed class Orb
    {
        public int Id;
        public ulong TargetId;
        public Unit Target;
        public GameObject Instance;
        public Vector3 LastPosition;
        // 한 번이라도 대상을 찾았는가 — 찾은 뒤 사라지면(사망·디스폰) 다시 찾지 않고 마지막 위치에 둔다.
        public bool Found;
    }

    private readonly List<Orb> orbs = new List<Orb>();
    private readonly GameObject prefab;

    public GunnerEchoBombVisuals(GameObject prefab)
    {
        this.prefab = prefab;
    }

    public int Count => orbs.Count;

    public void Add(int id, ulong targetId)
    {
        if (IndexOf(id) >= 0)
            return;

        var orb = new Orb { Id = id, TargetId = targetId };
        orbs.Add(orb);
        Follow(orb);
    }

    /// <summary>그 폭탄이 붙은 대상의 네트워크 ID(특성 연출 사건용, 할 일 145). 모르는 번호면 false.</summary>
    public bool TryGetTargetId(int id, out ulong targetId)
    {
        int index = IndexOf(id);
        targetId = index >= 0 ? orbs[index].TargetId : 0UL;
        return index >= 0;
    }

    /// <summary>폭탄이 빠졌다(터짐·빈 폭발) — 구체를 지우고 마지막 위치를 돌려준다. 모르는 번호거나 대상을 한 번도 못 찾았으면 false(위치 없음).</summary>
    public bool Remove(int id, out Vector3 lastPosition)
    {
        lastPosition = default;
        int index = IndexOf(id);
        if (index < 0)
            return false;

        Orb orb = orbs[index];
        Follow(orb);
        lastPosition = orb.LastPosition;
        DestroyInstance(orb);
        orbs.RemoveAt(index);
        return orb.Found;
    }

    /// <summary>예약 취소(사수 사망·씬 종료·디스폰) — 터뜨리지 않고 조용히 지운다.</summary>
    public void Clear()
    {
        for (int i = 0; i < orbs.Count; i++)
            DestroyInstance(orbs[i]);
        orbs.Clear();
    }

    /// <summary>복제 목록과 맞춘다(늦은 합류·전체 갱신) — 목록에 없는 구체는 조용히 지우고, 새 항목은 붙인다.</summary>
    public void Sync(IReadOnlyList<GunnerEchoBombState> states)
    {
        for (int i = 0; i < states.Count; i++)
            Add(states[i].BombId, states[i].TargetId);

        for (int i = orbs.Count - 1; i >= 0; i--)
        {
            if (Contains(states, orbs[i].Id))
                continue;
            DestroyInstance(orbs[i]);
            orbs.RemoveAt(i);
        }
    }

    private static bool Contains(IReadOnlyList<GunnerEchoBombState> states, int id)
    {
        for (int i = 0; i < states.Count; i++)
        {
            if (states[i].BombId == id)
                return true;
        }

        return false;
    }

    /// <summary>매 프레임(LateUpdate) — 대상 몸통 점으로 옮긴다. 대상이 사라졌으면 마지막 위치에 둔다.</summary>
    public void Tick()
    {
        for (int i = 0; i < orbs.Count; i++)
            Follow(orbs[i]);
    }

    private void Follow(Orb orb)
    {
        // 클라에서는 복제 순서상 대상이 폭탄보다 늦게 스폰될 수 있다 — 아직 못 찾았으면 다시 찾는다. 한 번도 못 찾았으면 구체를 띄우지 않는다.
        if (orb.Target == null && !orb.Found)
            orb.Target = GunnerEchoBombTargets.Find(orb.TargetId);
        if (orb.Target != null && orb.Target.IsSpawned)
        {
            orb.Found = true;
            orb.LastPosition = GunnerEchoBombTargets.BodyPoint(orb.Target);
            if (orb.Instance == null && prefab != null)
                orb.Instance = UnityEngine.Object.Instantiate(prefab, orb.LastPosition, Quaternion.identity);
        }

        if (orb.Instance != null)
            orb.Instance.transform.position = orb.LastPosition;
    }

    private static void DestroyInstance(Orb orb)
    {
        if (orb.Instance != null)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(orb.Instance);
            else
                UnityEngine.Object.DestroyImmediate(orb.Instance);
        }
        orb.Instance = null;
    }

    private int IndexOf(int id)
    {
        for (int i = 0; i < orbs.Count; i++)
        {
            if (orbs[i].Id == id)
                return i;
        }

        return -1;
    }
}
