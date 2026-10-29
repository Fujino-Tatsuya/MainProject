using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 보스 HP 바 잠김 표시(할 일 147) — 서버가 "지금 HP 피해가 0 으로 처리된다"를 bool 하나로 복제해
/// <see cref="BossHealthHUD"/> 가 바를 밝은 회색 + 자물쇠로 그린다. 판정은 바꾸지 않는다(읽기 전용 거울).
/// 공유: Docs/tech/handoff-boss-hp-lock.md
/// </summary>
public partial class TwentyThreeBoss
{
    // 서버 write / 모두 read — 늦게 들어온 클라도 현재 잠김을 본다.
    readonly NetworkVariable<bool> _hpLocked = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>[모든 피어] 지금 보스 HP 가 피해를 받지 않는다(무적) — 보스 체력바 잠김 표시용.</summary>
    public bool HpLocked => _hpLocked.Value;

    /// <summary>
    /// [서버] HP 피해가 0 이 되는 상태인가. 넣은 것 = 실제로 피해를 0 으로 만드는 게이트만:
    /// ① 레이드 기믹 무적(<see cref="RaidGimmickDamageImmune"/> — 릴레이·북진 압박, 플레이어 피해 0)
    /// ② 0줄 자폭 보호(Reserved·Active·폭발 대기 Exploding — <see cref="ZeroGuardFilter"/> 가 피해 0)
    /// ③ 휴면(<see cref="MonsterBase.IsDormant"/> — ReceiveAttack 거절).
    /// 🔴 <see cref="ZeroGuardProtecting"/> getter 는 Exploding → Done 을 넘기는 부작용이 있어 쓰지 않고 같은 조건을 읽기만 한다.
    /// </summary>
    bool ComputeHpLockedServer()
    {
        if (State == MonsterState.Dead || CurrentHealth <= 0) return false;
        if (IsDormant || RaidGimmickDamageImmune) return true;
        if (!ZeroSelfDestructOn) return false;
        return _zeroGuard == ZeroGuard.Reserved
               || _zeroGuard == ZeroGuard.Active
               || (_zeroGuard == ZeroGuard.Exploding && Time.time < _zeroExplodeUntil);
    }

    /// <summary>[서버] 매 프레임 — 바뀔 때만 쓴다(NetworkVariable 은 더티일 때만 보낸다).</summary>
    void PublishHpLockServer()
    {
        if (!IsServer || !IsSpawned) return;
        bool locked = ComputeHpLockedServer();
        if (_hpLocked.Value != locked)
            _hpLocked.Value = locked;
    }
}
