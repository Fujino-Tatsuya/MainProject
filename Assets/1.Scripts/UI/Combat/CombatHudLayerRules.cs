using System;

/// <summary>CombatHUD 루트 아래 상태별 표시 레이어. 프리팹 자식 GameObject 하나씩에 대응한다.</summary>
[Flags]
public enum CombatHudLayers
{
    None = 0,
    /// <summary>항상 보이는 화면 효과(체력 비네팅).</summary>
    Persistent = 1 << 0,
    /// <summary>내 전투 위젯(HP·스킬바·키가이드·미니맵·보스 타이머·툴팁).</summary>
    Combat = 1 << 1,
    /// <summary>보스 HP — 관전 중에도 보인다.</summary>
    BossInfo = 1 << 2,
    /// <summary>관전 UI 자리(현재 비어 있음).</summary>
    Spectate = 1 << 3,
}

/// <summary>
/// 로컬 Player 생명주기 상태 → CombatHUD 표시 규칙. <see cref="PlayerCombatUiLifecyclePolicy"/> 가 적용하고,
/// 순수 함수라 EditMode 테스트 대상이다.
/// </summary>
public static class CombatHudLayerRules
{
    public static CombatHudLayers VisibleLayers(PlayerLifeState state)
    {
        switch (state)
        {
            case PlayerLifeState.Alive:
            case PlayerLifeState.Soul:
                return CombatHudLayers.Persistent | CombatHudLayers.Combat | CombatHudLayers.BossInfo;
            case PlayerLifeState.DeadPresentation:
                return CombatHudLayers.Persistent;
            case PlayerLifeState.PermanentDead:
                return CombatHudLayers.Persistent | CombatHudLayers.BossInfo | CombatHudLayers.Spectate;
            default:
                return CombatHudLayers.Persistent;
        }
    }

    /// <summary>Soul 은 HP/Shield 를 0 으로 보이고 슬롯을 사용 불가로 칠한다(실제 값은 건드리지 않는다).</summary>
    public static bool ShowsSoulOverrides(PlayerLifeState state) => state == PlayerLifeState.Soul;

    /// <summary>바인딩된 Player 가 없으면 관전 화면으로 본다.</summary>
    public static PlayerLifeState ResolveDisplayState(bool hasPlayer, bool hasLifeCycle, PlayerLifeState lifeCycleState)
    {
        if (!hasPlayer)
            return PlayerLifeState.PermanentDead;

        return hasLifeCycle ? lifeCycleState : PlayerLifeState.Alive;
    }
}
