using System.Collections.Generic;
using BaseNetCode;
using UnityEngine;

/// <summary>
/// 보스 프리팹 부착 마커. 스폰/디스폰 시 static 목록에 등록해 BossHealthHUD가 대상을 찾게 한다.
/// 보스 식별은 이 컴포넌트 부착 여부로만 판단 — Enemy 코드 무수정 (그릴 합의).
/// </summary>
[RequireComponent(typeof(Unit))]
public class BossHudTarget : BaseNetworkBehaviour
{
    private static readonly List<BossHudTarget> active = new List<BossHudTarget>();
    public static IReadOnlyList<BossHudTarget> Active => active;

    public Unit Unit { get; private set; }

    private void Awake()
    {
        Unit = GetComponent<Unit>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!active.Contains(this))
            active.Add(this);
    }

    public override void OnNetworkDespawn()
    {
        active.Remove(this);
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        active.Remove(this);
        base.OnDestroy();
    }
}
