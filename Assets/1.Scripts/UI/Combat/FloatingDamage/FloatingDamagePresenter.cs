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

        _unit.ClientDamagedAmount += HandleReplicatedDamage;
        _unit.ClientDamagedAttributed += HandleAttributedDamage;
    }

    void OnDisable()
    {
        if (_unit == null)
            return;

        _unit.ClientDamagedAmount -= HandleReplicatedDamage;
        _unit.ClientDamagedAttributed -= HandleAttributedDamage;
    }

    void HandleReplicatedDamage(int amount, DamageChannel channel)
    {
        FloatingDamageSpawner spawner = FloatingDamageSpawner.Instance;
        if (spawner == null || spawner.Settings == null ||
            spawner.Settings.DisplayFilter != FloatingDamageDisplayFilter.AllDamage)
            return;

        Submit(spawner, amount, channel, false);
    }

    void HandleAttributedDamage(int amount, DamageChannel channel, ulong attackerClientId)
    {
        FloatingDamageSpawner spawner = FloatingDamageSpawner.Instance;
        if (spawner == null || spawner.Settings == null)
            return;

        FloatingDamageDisplayFilter filter = spawner.Settings.DisplayFilter;
        if (filter == FloatingDamageDisplayFilter.AllDamage)
            return;

        bool fromLocalPlayer = IsLocalAttacker(attackerClientId);
        if (filter == FloatingDamageDisplayFilter.OwnDealtOnly && !fromLocalPlayer)
            return;

        Submit(spawner, amount, channel, fromLocalPlayer);
    }

    void Submit(FloatingDamageSpawner spawner, int amount, DamageChannel channel, bool fromLocalPlayer)
    {
        if (amount <= 0 || IsLocalPlayerTarget())
            return;

        PopupKind kind = channel == DamageChannel.Shield
            ? PopupKind.ShieldDamage
            : PopupKind.Damage;
        spawner.Submit(new FloatingPopupRequest(_unit, kind, amount, fromLocalPlayer));
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
