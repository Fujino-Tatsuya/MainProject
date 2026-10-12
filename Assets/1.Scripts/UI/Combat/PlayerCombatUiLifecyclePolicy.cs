using UnityEngine;

/// <summary>
/// Soul 상태에서도 진행 표시를 계속 갱신해야 하는 CombatUI 위젯의 표시 정책.
/// Dash HUD 등 후속 위젯은 이 인터페이스를 구현하면 생명주기 정책에 자동 참여한다.
/// </summary>
public interface ICombatUiBlockedStateView
{
    void SetBlocked(bool blocked);
}

/// <summary>
/// 로컬 Player의 복제된 생명주기 상태를 CombatHUD 레이어 표시로 변환하는 상태머신.
/// 상태 → 레이어 규칙은 <see cref="CombatHudLayerRules"/>.
/// 🔴 레이어는 CanvasGroup 으로만 숨긴다 — SetActive 는 하위 위젯의 OnEnable/OnDisable(구독·Bind·잔상)을 흔든다.
///    위젯은 계속 이벤트를 받으므로 DeadPresentation 이후 Soul 진입 시 그대로 다시 보인다.
/// </summary>
public class PlayerCombatUiLifecyclePolicy : MonoBehaviour
{
    // 저작 스크립트(CombatHudSlotAuthoring·SkillTooltipAuthoring)가 새 슬롯을 놓을 레이어 이름.
    public const string PersistentLayerName = "Layer_Persistent";
    public const string CombatLayerName = "Layer_Combat";
    public const string BossInfoLayerName = "Layer_BossInfo";
    public const string SpectateLayerName = "Layer_Spectate";

    [SerializeField] private CanvasGroup persistentLayer;
    [SerializeField] private CanvasGroup combatLayer;
    [SerializeField] private CanvasGroup bossInfoLayer;
    [SerializeField] private CanvasGroup spectateLayer;

    private PlayerHealthHUD playerHealthHUD;
    private ICombatUiBlockedStateView[] blockedStateViews;
    private PlayerLifeCycleController lifeCycle;

    private void Awake()
    {
        CacheViews();
    }

    private void OnEnable()
    {
        Player.LocalPlayerChanged += Bind;
        Bind(Player.LocalPlayer);
    }

    private void OnDisable()
    {
        Player.LocalPlayerChanged -= Bind;
        UnbindLifeCycle();
    }

    private void CacheViews()
    {
        playerHealthHUD = GetComponentInChildren<PlayerHealthHUD>(true);

        MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
        int count = 0;
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is ICombatUiBlockedStateView)
                count++;
        }

        blockedStateViews = new ICombatUiBlockedStateView[count];
        int index = 0;
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is ICombatUiBlockedStateView blockedStateView)
                blockedStateViews[index++] = blockedStateView;
        }
    }

    private void Bind(Player player)
    {
        UnbindLifeCycle();

        lifeCycle = ResolveLifeCycle(player);

        if (lifeCycle != null)
            lifeCycle.LifeStateChanged += HandleLifeStateChanged;

        ApplyState(CombatHudLayerRules.ResolveDisplayState(
            player != null,
            lifeCycle != null,
            lifeCycle != null ? lifeCycle.State : PlayerLifeState.Alive));
    }

    private void UnbindLifeCycle()
    {
        if (lifeCycle != null)
            lifeCycle.LifeStateChanged -= HandleLifeStateChanged;

        lifeCycle = null;
    }

    private void HandleLifeStateChanged(
        PlayerLifeState previousState,
        PlayerLifeState currentState)
    {
        ApplyState(currentState);
    }

    private static PlayerLifeCycleController ResolveLifeCycle(Player player)
    {
        return player != null
            ? player.GetComponent<PlayerLifeCycleController>()
            : null;
    }

    private void ApplyState(PlayerLifeState state)
    {
        bool soulOverrides = CombatHudLayerRules.ShowsSoulOverrides(state);

        // 실제 Player HP/Shield NetworkVariable은 건드리지 않고 표시값만 덮는다.
        if (playerHealthHUD != null)
            playerHealthHUD.SetDisplayOverrideZero(soulOverrides);

        if (blockedStateViews != null)
        {
            for (int i = 0; i < blockedStateViews.Length; i++)
                blockedStateViews[i].SetBlocked(soulOverrides);
        }

        CombatHudLayers visible = CombatHudLayerRules.VisibleLayers(state);
        SetLayerVisible(persistentLayer, (visible & CombatHudLayers.Persistent) != 0);
        SetLayerVisible(combatLayer, (visible & CombatHudLayers.Combat) != 0);
        SetLayerVisible(bossInfoLayer, (visible & CombatHudLayers.BossInfo) != 0);
        SetLayerVisible(spectateLayer, (visible & CombatHudLayers.Spectate) != 0);
    }

    // BossHealthHUD 의 중첩 Canvas 도 부모 CanvasGroup 알파를 상속한다(ignoreParentGroups=false).
    private static void SetLayerVisible(CanvasGroup layer, bool visible)
    {
        if (layer == null)
            return;

        layer.alpha = visible ? 1f : 0f;
        layer.interactable = visible;
        layer.blocksRaycasts = visible;
    }
}
