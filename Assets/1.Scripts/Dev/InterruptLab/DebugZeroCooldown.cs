using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 디버그 쿨 0(DebugInterruptLab·DebugTraitLab 씬에만 둔다, 할 일 115 C6·135) — 간파(성공·실패 무관)와 대시 충전을 바로 되돌린다.
///
/// 쿨 계산 코드는 그대로다 — 기존 리셋 API 만 부른다(튜토리얼 복도1 쿨 초기화와 같은 짝).
/// <list type="bullet">
/// <item>간파 [서버]: 간파 슬롯 쿨이 남아 있으면 <see cref="PlayerSkillController.ResetCooldownServer(PlayerSkillSlot)"/>.
///       장부가 오너 HUD 장부에도 미러하므로 오너 선검증(<see cref="PlayerSkillController.IsBlockedByCooldown"/>)도 통과한다.</item>
/// <item>Q·E·R [서버, <see cref="skills"/> 켰을 때만 — 특성 랩]: 같은 리셋 API 를 슬롯마다.</item>
/// <item>대시 [서버]: 승인(<see cref="PlayerDashController.ServerDashApproved"/>)마다 <see cref="PlayerDashController.ServerResetChargeToFull"/>.</item>
/// <item>대시 [오너]: 내 예측 충전이 줄었으면 <see cref="PlayerDashController.OwnerResetChargeToFull"/> — 각 피어의 씬 인스턴스가 자기 플레이어만.</item>
/// </list>
/// 컴포넌트를 끄면 평소 쿨로 돌아간다(이미 남은 쿨은 그대로 흐른다).
/// </summary>
[DisallowMultipleComponent]
public sealed class DebugZeroCooldown : MonoBehaviour
{
    static readonly PlayerSkillSlot[] SkillSlots = { PlayerSkillSlot.Main, PlayerSkillSlot.Sub, PlayerSkillSlot.Ultimate };

    [Tooltip("간파 쿨 0 — 끝나자마자(성공·실패 무관) 다시 쓸 수 있다.")]
    [SerializeField] bool interrupt = true;
    [Tooltip("대시 충전 즉시 회복 — 쓰자마자 가득 찬다.")]
    [SerializeField] bool dash = true;
    [Tooltip("Q·E·R 쿨 0 — 특성 랩용. 간파 랩은 끈다(기본).")]
    [SerializeField] bool skills;

    void OnEnable() => PlayerDashController.ServerDashApproved += HandleDashApprovedServer;

    void OnDisable() => PlayerDashController.ServerDashApproved -= HandleDashApprovedServer;

    void Update()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return;

        if ((interrupt || skills) && networkManager.IsServer)
        {
            foreach (NetworkClient client in networkManager.ConnectedClientsList)
            {
                if (client.PlayerObject == null || !client.PlayerObject.TryGetComponent(out PlayerSkillController controller))
                    continue;

                if (interrupt)
                    ResetIfCooling(controller, PlayerSkillSlot.Interrupt);
                if (skills)
                {
                    for (int i = 0; i < SkillSlots.Length; i++)
                        ResetIfCooling(controller, SkillSlots[i]);
                }
            }
        }

        if (dash && Player.LocalPlayer != null &&
            Player.LocalPlayer.TryGetComponent(out PlayerDashController localDash) &&
            NeedsOwnerRefill(localDash.PredictedCharge, localDash.MaxCharge))
        {
            localDash.OwnerResetChargeToFull();
        }
    }

    static void ResetIfCooling(PlayerSkillController controller, PlayerSkillSlot slot)
    {
        if (controller.GetCooldownRemaining(slot) > 0f)
            controller.ResetCooldownServer(slot);
    }

    /// <summary>오너 예측 충전을 채워야 하는가 — 장부가 있고(최대 &gt; 0) 덜 찼다.</summary>
    public static bool NeedsOwnerRefill(int predictedCharge, int maxCharge) => maxCharge > 0 && predictedCharge < maxCharge;

    void HandleDashApprovedServer(Player player)
    {
        if (dash && player != null && player.TryGetComponent(out PlayerDashController dashController))
            dashController.ServerResetChargeToFull();
    }
}
