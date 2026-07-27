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
/// 로컬 Player의 복제된 생명주기 상태를 CombatUI 표현으로 변환한다.
/// HUD GameObject는 이벤트 구독을 유지하고 Canvas만 숨기므로
/// DeadPresentation 이후 Soul 진입 시 다시 표시할 수 있다.
/// </summary>
[RequireComponent(typeof(Canvas))]
public class PlayerCombatUiLifecyclePolicy : MonoBehaviour
{
    private Canvas hudCanvas;
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
        hudCanvas = GetComponent<Canvas>();
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

        ApplyState(ResolveDisplayState(player));
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

    private PlayerLifeState ResolveDisplayState(Player player)
    {
        if (player == null)
            return PlayerLifeState.PermanentDead;

        return lifeCycle != null
            ? lifeCycle.State
            : PlayerLifeState.Alive;
    }

    private void ApplyState(PlayerLifeState state)
    {
        bool isSoul = state == PlayerLifeState.Soul;
        bool shouldShow =
            state == PlayerLifeState.Alive ||
            state == PlayerLifeState.Soul;

        // 실제 Player HP/Shield NetworkVariable은 건드리지 않고 표시값만 덮는다.
        if (playerHealthHUD != null)
            playerHealthHUD.SetDisplayOverrideZero(isSoul);

        if (blockedStateViews != null)
        {
            for (int i = 0; i < blockedStateViews.Length; i++)
                blockedStateViews[i].SetBlocked(isSoul);
        }

        if (hudCanvas != null)
            hudCanvas.enabled = shouldShow;
    }
}
