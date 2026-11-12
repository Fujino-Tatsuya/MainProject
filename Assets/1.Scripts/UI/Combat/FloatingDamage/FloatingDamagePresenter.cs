using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class FloatingDamagePresenter : MonoBehaviour
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

        _unit.ClientDamagedAttributed += HandleAttributedDamage;
        _unit.ClientHealedAttributed += HandleHeal;
    }

    void OnDisable()
    {
        if (_unit == null)
            return;

        _unit.ClientDamagedAttributed -= HandleAttributedDamage;
        _unit.ClientHealedAttributed -= HandleHeal;
    }

    void HandleAttributedDamage(DamageDealtInfo damage)
    {
        FloatingDamageSpawner spawner = FloatingDamageSpawner.Instance;
        if (spawner == null || spawner.Settings == null)
            return;

        FloatingDamageDisplayFilter filter = spawner.Settings.DisplayFilter;
        bool fromLocalPlayer = IsLocalAttacker(damage.attackerClientId);
        FloatingDamageDisplayDecision decision = FloatingDamageDisplayPolicy.Evaluate(
            filter,
            fromLocalPlayer,
            spawner.Settings.TeammateScaleMultiplier,
            spawner.Settings.TeammateAlpha);
        if (!decision.shouldDisplay)
            return;

        // AllDamage는 공격자 귀속으로 거르지 않으며, 기존처럼 로컬 공격 강조도 적용하지 않는다.
        Submit(spawner, damage,
            filter != FloatingDamageDisplayFilter.AllDamage && fromLocalPlayer);
    }

    void Submit(FloatingDamageSpawner spawner, DamageDealtInfo damage, bool fromLocalPlayer)
    {
        if (damage.amount <= 0 || IsLocalPlayerTarget())
            return;

        PopupKind kind = damage.channel == DamageChannel.Shield
            ? PopupKind.ShieldDamage
            : PopupKind.Damage;
        spawner.Submit(new FloatingPopupRequest(_unit, kind, damage.amount, fromLocalPlayer,
                damage.attackerClientId, damage.attackType, damage.hitPattern)
            .WithAttackerSource(damage.attackerClientId, damage.attackerObjectId)); // 반대쪽 연출 기준점(268)
    }

    // 힐 숫자(268) — 피해 숫자와 같은 표시 필터·연출(내 힐 = 크게, 팀원끼리 = 작고 흐리게), 힐한 사람·대상별 합산.
    // 반대쪽 기준 = 힐한 사람, 자기 회복(소생 포함)은 기준점 없음 = 위로만.
    void HandleHeal(HealReceivedInfo heal)
    {
        FloatingDamageSpawner spawner = FloatingDamageSpawner.Instance;
        if (spawner == null || spawner.Settings == null || heal.amount <= 0 || IsLocalPlayerTarget())
            return;

        FloatingDamageDisplayFilter filter = spawner.Settings.DisplayFilter;
        bool fromLocalPlayer = IsLocalAttacker(heal.healerClientId);
        FloatingDamageDisplayDecision decision = FloatingDamageDisplayPolicy.Evaluate(
            filter,
            fromLocalPlayer,
            spawner.Settings.TeammateScaleMultiplier,
            spawner.Settings.TeammateAlpha);
        if (!decision.shouldDisplay)
            return;

        spawner.Submit(new FloatingPopupRequest(_unit, PopupKind.Heal, heal.amount,
                filter != FloatingDamageDisplayFilter.AllDamage && fromLocalPlayer,
                heal.healerClientId, AttackType.Default, AttackHitPattern.Multi)
            .WithPlayerSource(heal.healerClientId));
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
