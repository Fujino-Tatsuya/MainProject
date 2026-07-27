using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Soul 부활 요청과 서버 승인을 연결한다.
/// F10 입력은 실제 부활 조건(구역/아군/타이머)이 확정될 때 교체할 v1 디버그 트리거다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerLifeCycleController))]
public sealed class PlayerReviveController : NetworkBehaviour
{
    [Header("Revive")]
    [SerializeField] private PlayerLifeCycleController lifeCycle;
    [SerializeField] private Temp_MultiGameRule gameRule;
    [Tooltip("실제 부활 조건으로 교체하기 전까지 사용하는 로컬 오너 디버그 입력입니다.")]
    [SerializeField] private Key debugReviveKey = Key.F10;

    /// <summary>
    /// 서버에서 Alive 전환과 LifeCount 차감이 모두 끝난 뒤 발생한다.
    /// Dash 충전 1/MaxCharge 초기화와 부활 보호 Token/Blink는 통합 시 이 seam을 구독한다.
    /// </summary>
    public event Action<int> ServerReviveCompleted;

    private void Awake()
    {
        ResolveLocalReferences();
        ResolveGameRuleReference();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            ResolveLocalReferences();
            ResolveGameRuleReference();
        }
    }

    private void Update()
    {
        if (!CanRequestDebugRevive() || !WasDebugRevivePressed())
            return;

        RequestDebugReviveRpc();
    }

    /// <summary>
    /// 실제 부활 조건 소비자가 서버에서 호출할 승인 진입점.
    /// 현재 상태가 Soul이고 LifeCount가 남아 있을 때만 성공한다.
    /// </summary>
    public bool TryCompleteReviveOnServer()
    {
        if (!IsSpawned || !IsServer)
            return false;

        ResolveLocalReferences();
        ResolveGameRuleReference();
        if (lifeCycle == null || lifeCycle.State != PlayerLifeState.Soul)
            return false;

        if (gameRule == null ||
            !gameRule.TryRegisterClient(OwnerClientId) ||
            !gameRule.HasReviveAvailable(OwnerClientId))
        {
            Debug.LogWarning(
                $"[SoulAlert] {OwnerClientId}번 Player의 부활을 거부했습니다. " +
                "Temp_MultiGameRule이 없거나 LifeCount가 0입니다.",
                this);
            return false;
        }

        if (!lifeCycle.TryCompleteRevive())
            return false;

        if (!gameRule.TryConsumeLifeAfterAliveRevive(
                OwnerClientId,
                out int remainingLifeCount))
        {
            // 같은 서버 호출 안에서 사전 검증 직후 차감하므로 정상 경로에서는 도달하지 않는다.
            Debug.LogError(
                $"[SoulAlert] {OwnerClientId}번 Player가 Alive로 전환됐지만 " +
                "LifeCount 차감에 실패했습니다.",
                this);
            return false;
        }

        ServerReviveCompleted?.Invoke(remainingLifeCount);
        return true;
    }

    [Rpc(SendTo.Server)]
    private void RequestDebugReviveRpc()
    {
        TryCompleteReviveOnServer();
    }

    private void ResolveLocalReferences()
    {
        if (lifeCycle == null)
            lifeCycle = GetComponent<PlayerLifeCycleController>();
    }

    private void ResolveGameRuleReference()
    {
        if (gameRule == null)
            gameRule = FindFirstObjectByType<Temp_MultiGameRule>();
    }

    private bool CanRequestDebugRevive()
    {
        return IsSpawned &&
            IsOwner &&
            lifeCycle != null &&
            lifeCycle.State == PlayerLifeState.Soul;
    }

    private bool WasDebugRevivePressed()
    {
        return Keyboard.current != null &&
            Keyboard.current[debugReviveKey].wasPressedThisFrame;
    }
}
