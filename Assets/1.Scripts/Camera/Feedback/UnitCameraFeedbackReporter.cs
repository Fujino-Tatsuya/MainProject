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
        if (amount <= 0 || !IsLocalPlayerTarget())
            return;

        CameraFeedback.Instance?.ReportLocalPlayerHit();
    }

    void HandleAttributedDamage(int amount, DamageChannel channel, ulong attackerClientId)
    {
        if (amount <= 0 || IsLocalPlayerTarget() || !IsLocalAttacker(attackerClientId))
            return;

        CameraFeedback.Instance?.ReportLocalPlayerDealtDamage();
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
