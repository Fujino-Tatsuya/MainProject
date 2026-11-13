using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class UnitCameraFeedbackReporter : MonoBehaviour
{
    Unit _unit;

    void Awake()
    {
        _unit = GetComponent<Unit>();
    }

    void OnEnable()
    {
        if (_unit == null)
            _unit = GetComponent<Unit>();

        if (_unit == null)
            return;

        _unit.ClientDamagedAmount += HandleReplicatedDamage;
        _unit.ClientDamagedAttributed += HandleAttributedDamage;
        _unit.ClientHitAttributed += HandleAttributedHit;
    }

    void OnDisable()
    {
        if (_unit == null)
            return;

        _unit.ClientDamagedAmount -= HandleReplicatedDamage;
        _unit.ClientDamagedAttributed -= HandleAttributedDamage;
        _unit.ClientHitAttributed -= HandleAttributedHit;
    }

    void HandleReplicatedDamage(int amount, DamageChannel channel)
    {
        if (amount <= 0 || !IsLocalPlayerTarget())
            return;

        CameraFeedback.Instance?.ReportLocalPlayerHit();
        // 278 햅틱 — 내 플레이어 피격, 피해량(최대 체력 대비) 비례.
        GamepadHaptics.Play(GamepadHapticEvent.DamageTaken, GamepadHaptics.DamageScale(amount, _unit.FinalMaxHp));
    }

    void HandleAttributedDamage(DamageDealtInfo damage)
    {
        if (damage.amount <= 0 || IsLocalPlayerTarget() || !IsLocalAttacker(damage.attackerClientId))
            return;

        CameraFeedback.Instance?.ReportLocalPlayerDealtDamage();
        // 278 햅틱 — 내 평타가 적(플레이어 아님)에게 맞음. 다단 히트는 프리셋 최소 간격으로 거른다.
        if (damage.attackType == AttackType.Default && !(_unit is Player) &&
            damage.attackerObjectId == DamageDealtInfo.NoAttackerObject)
            GamepadHaptics.Play(GamepadHapticEvent.BasicAttackHit);
    }

    // 제압 가속 N타 줌인(기본 4타, 할 일 174·233) — 내가 보스에게 맞힌 타격 1회(서버 확정 RPC 1개 = 1타). 가속 여부·셈은 카메라가 본다.
    void HandleAttributedHit(ulong attackerClientId)
    {
        if (_unit.Rank != MonsterRank.Boss || !IsLocalAttacker(attackerClientId))
            return;

        CameraTargetSwitcher.Active?.ReportLocalBossHit();
    }

    bool IsLocalPlayerTarget()
    {
        return _unit is Player player && (player == Player.LocalPlayer || player.IsOwner);
    }

    static bool IsLocalAttacker(ulong attackerClientId)
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager != null && manager.IsListening && attackerClientId == manager.LocalClientId;
    }
}
