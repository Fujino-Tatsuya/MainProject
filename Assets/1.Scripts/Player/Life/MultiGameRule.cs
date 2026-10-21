using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 임시 멀티플레이 규칙 — <b>파티 공용 목숨 풀</b>(할 일 30, 2026-10-13). 서버만 차감·재충전하고,
/// 복제된 값은 모든 피어가 읽는다. 규칙은 순수 <see cref="PartyLivesRules"/>, 설계는 Docs/tech/party-lives-respawn.md.
///
/// 예전 클라별 LifeCount 장부의 API(<see cref="TryGetLifeCount"/>·<see cref="HasReviveAvailable"/>·
/// <see cref="TryResolveDeathState"/>·<see cref="TryConsumeLifeAfterAliveRevive"/>)는 이름을 유지하고
/// clientId 와 무관하게 공용 풀을 본다. <see cref="TryRegisterClient"/>·<see cref="TryUnregisterClient"/> 는 호환용(장부 없음).
/// </summary>
[DataTableSheet("Player", Order = 7)]
public class MultiGameRule : NetworkBehaviour
{
    [Header("공용 목숨")]
    [Tooltip("파티 공용 목숨 수. 게임 시작·전멸 리스폰 때 이 값으로 채운다. 부활(Soul → Alive) 성공마다 1개 쓴다.")]
    [SerializeField, Min(0)] private int partyLifeCount = PartyLivesRules.DefaultCapacity;

    private readonly NetworkVariable<int> partyLives =
        new NetworkVariable<int>(
            PartyLivesRules.DefaultCapacity,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    /// <summary>재충전 값(데이터 테이블 <c>partyLifeCount</c>).</summary>
    public int PartyLifeCapacity => PartyLivesRules.Refill(partyLifeCount);

    /// <summary>복제된 현재 공용 목숨. 스폰 전이면 용량.</summary>
    public int PartyLives => IsSpawned ? PartyLivesRules.ClampLives(partyLives.Value) : PartyLifeCapacity;

    /// <summary>공용 목숨이 바뀌었다(모든 피어). 인자 = 새 값.</summary>
    public event Action<int> PartyLivesChanged;

    /// <summary>Debug UI 등 읽기 소비자가 값을 다시 조회하는 훅(예전 장부 이벤트 — <see cref="PartyLivesChanged"/> 와 같이 발생).</summary>
    public event Action LifeCountsChanged;

    private bool CanWriteLifeCounts => IsSpawned && IsServer;

    private static readonly ActiveRegistry<MultiGameRule> ActiveList = new ActiveRegistry<MultiGameRule>();

    /// <summary>
    /// 켜져 있는 규칙(OnEnable 등록·OnDisable 해제 — 스폰 여부와 무관, 없으면 null). 판 도중 네트워크 스폰돼도 켜지는 순간 잡힌다.
    /// 전멸 판정(<c>PartyWipeWatcher</c>)이 주기마다 씬을 뒤지지 않고 이것을 읽는다(할 일 69). 스폰 여부는 호출자가 <c>IsSpawned</c> 로 본다.
    /// </summary>
    public static MultiGameRule Current => ActiveList.Items.Count > 0 ? ActiveList.Items[0] : null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetActive() => ActiveList.Clear();

    private void OnEnable() => ActiveList.Add(this);

    private void OnDisable() => ActiveList.Remove(this);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        partyLives.OnValueChanged += HandlePartyLivesChanged;

        if (IsServer)
            partyLives.Value = PartyLifeCapacity;
    }

    public override void OnNetworkDespawn()
    {
        partyLives.OnValueChanged -= HandlePartyLivesChanged;
        base.OnNetworkDespawn();
    }

    /// <summary>공용 목숨을 읽는다(clientId 무관). 모든 피어에서 호출 가능하다. 스폰 전이면 false.</summary>
    public bool TryGetLifeCount(ulong clientId, out int lifeCount)
    {
        lifeCount = IsSpawned ? PartyLives : 0;
        return IsSpawned;
    }

    /// <summary>표시/사전 게이트용 읽기 API. 사망 상태의 최종 확정에는 TryResolveDeathState를 사용한다.</summary>
    public bool HasReviveAvailable(ulong clientId)
    {
        return TryGetLifeCount(clientId, out int lifeCount) &&
            PartyLivesRules.CanRevive(lifeCount, consumeLife: true);
    }

    /// <summary>호환용 — 공용 풀은 등록이 필요 없다. 서버 스폰 상태면 true.</summary>
    public bool TryRegisterClient(ulong clientId) => CanWriteLifeCounts;

    /// <summary>호환용 — 이탈해도 공용 풀은 그대로다. 서버 스폰 상태면 true.</summary>
    public bool TryUnregisterClient(ulong clientId) => CanWriteLifeCounts;

    /// <summary>
    /// 서버가 사망 시 Soul 또는 PermanentDead 목적지를 판정한다(공용 목숨 &gt; 0 → Soul).
    /// 목숨은 이 시점에 감소하지 않는다.
    /// </summary>
    public bool TryResolveDeathState(
        ulong clientId,
        out PlayerLifeState destinationState)
    {
        destinationState = PlayerLifeState.PermanentDead;
        if (!CanWriteLifeCounts)
            return false;

        destinationState = PartyLivesRules.ResolveDeathDestination(PartyLives, keepsSoul: false);
        return true;
    }

    /// <summary>
    /// 서버가 Soul에서 Alive로 실제 부활을 성공시킨 직후 공용 목숨을 1 감소시킨다.
    /// Soul 진입이나 부활 시도 단계에서는 호출하지 않는다.
    /// </summary>
    public bool TryConsumeLifeAfterAliveRevive(
        ulong clientId,
        out int remainingLifeCount)
    {
        remainingLifeCount = PartyLives;
        if (!CanWriteLifeCounts ||
            !PartyLivesRules.TryConsume(partyLives.Value, out remainingLifeCount))
        {
            return false;
        }

        partyLives.Value = remainingLifeCount;
        return true;
    }

    /// <summary>
    /// [서버] 공용 목숨을 용량(<see cref="PartyLifeCapacity"/>)으로 다시 채운다 — 전멸 리스폰(할 일 30 (2)).
    /// </summary>
    public bool TryRefillPartyLivesOnServer(out int partyLifeCountAfter)
    {
        partyLifeCountAfter = PartyLives;
        if (!CanWriteLifeCounts)
            return false;

        partyLifeCountAfter = PartyLifeCapacity;
        partyLives.Value = partyLifeCountAfter;
        return true;
    }

    private void HandlePartyLivesChanged(int previous, int current)
    {
        PartyLivesChanged?.Invoke(PartyLivesRules.ClampLives(current));
        LifeCountsChanged?.Invoke();
    }
}
