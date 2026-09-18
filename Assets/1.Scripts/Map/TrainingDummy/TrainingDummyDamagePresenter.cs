using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 허수아비 전용 데미지 표시기.
///
/// Unit 기본 경로(FloatingDamagePresenter)는 복제된 "실제 HP 델타"를 띄우는데, 허수아비는
/// 체력 하한 1 에 붙어도 명목 피해를 계속 보여줘야 해서 전용 경로를 쓴다.
/// 타격 카메라 쉐이크도 여기서 함께 처리한다 — 같은 이유로 UnitCameraFeedbackReporter 를 대신한다.
/// (두 기본 컴포넌트는 TrainingDummy.OnNetworkSpawn 에서 제거된다.)
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TrainingDummy))]
public sealed class TrainingDummyDamagePresenter : MonoBehaviour
{
    TrainingDummy _dummy;

    void Awake()
    {
        _dummy = GetComponent<TrainingDummy>();
    }

    void OnEnable()
    {
        if (_dummy == null)
            _dummy = GetComponent<TrainingDummy>();

        if (_dummy != null)
            _dummy.NominalDamaged += HandleNominalDamage;
    }

    void OnDisable()
    {
        if (_dummy != null)
            _dummy.NominalDamaged -= HandleNominalDamage;
    }

    void HandleNominalDamage(int amount, ulong attackerClientId)
    {
        if (amount <= 0)
            return;

        bool fromLocalPlayer = IsLocalAttacker(attackerClientId);

        FloatingDamageSpawner spawner = FloatingDamageSpawner.Instance;
        if (spawner != null && spawner.Settings != null)
        {
            // 전용 경로라 필터를 직접 존중한다. AllDamage·AllWithOwnEmphasis 는 전부 띄우고,
            // OwnDealtOnly 는 내가 때린 것만 띄운다.
            bool suppressed =
                spawner.Settings.DisplayFilter == FloatingDamageDisplayFilter.OwnDealtOnly &&
                !fromLocalPlayer;

            if (!suppressed)
                spawner.Submit(new FloatingPopupRequest(_dummy, PopupKind.Damage, amount, fromLocalPlayer));
        }

        if (fromLocalPlayer)
            CameraFeedback.Instance?.ReportLocalPlayerDealtDamage();
    }

    static bool IsLocalAttacker(ulong attackerClientId)
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager != null && manager.IsListening && attackerClientId == manager.LocalClientId;
    }
}
